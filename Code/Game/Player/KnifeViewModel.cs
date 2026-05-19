using System.Linq;
using Sandbox;
using Runner.Economy;

namespace Runner.Player;

/// <summary>
/// Knife viewmodel — drives both the 1st-person (camera-anchored) knife and the
/// 3rd-person (held in the citizen's right hand) knife so the player and other
/// observers always see the right weapon in the right place.
///
/// Two primitives — blade + handle — composed into a recognizable knife
/// silhouette. Per-knife presets vary the blade shape (Katana long & wide,
/// Karambit short & angled, Butterfly compact, Bayonet medium…) so each skin
/// reads differently even without real .vmdl assets.
///
/// FPS branch: _root is anchored to the camera every frame, hidden in TPS.
/// TPS branch: _tpsRoot is parented to the citizen's "hold_R" bone, hidden in FPS.
///
/// Tint applies to the blade only; the handle stays dark to read as a grip.
/// </summary>
public sealed class KnifeViewModel : Component
{
	// ─── FPS viewmodel offsets (camera-relative) ──────────────────────────
	[Property] public float ForwardOffset { get; set; } = 13f;
	[Property] public float RightOffset { get; set; } = 10f;
	[Property] public float DownOffset { get; set; } = 11f;

	/// <summary>Yaw/Pitch/Roll tweak to make the blade angle look "held".</summary>
	[Property] public float YawTweak { get; set; } = -18f;
	[Property] public float PitchTweak { get; set; } = -32f;
	[Property] public float RollTweak { get; set; } = -10f;

	// ─── TPS knife offsets (relative to hold_R bone) ──────────────────────
	[Property] public Vector3 TpsLocalOffset { get; set; } = new Vector3( 0, 0, 0 );
	[Property] public float TpsPitch { get; set; } = 0f;
	[Property] public float TpsYaw { get; set; } = 0f;
	[Property] public float TpsRoll { get; set; } = 0f;

	/// <summary>Name of the right-hand grip bone on the Citizen rig.</summary>
	[Property] public string HoldBoneName { get; set; } = "hold_R";

	// FPS hierarchy (camera-anchored)
	private GameObject _root;
	private GameObject _blade;
	private GameObject _handle;
	private ModelRenderer _bladeRenderer;
	private ModelRenderer _handleRenderer;

	// TPS hierarchy (parented to hold_R bone)
	private GameObject _tpsRoot;
	private GameObject _tpsBlade;
	private GameObject _tpsHandle;
	private ModelRenderer _tpsBladeRenderer;
	private ModelRenderer _tpsHandleRenderer;
	private GameObject _attachedBone;

	private CameraComponent _camera;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		EnsureFpsMeshes();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if ( _root.IsValid() )
		{
			_root.Destroy();
			_root = null;
		}
		if ( _tpsRoot.IsValid() )
		{
			_tpsRoot.Destroy();
			_tpsRoot = null;
		}
		_attachedBone = null;
	}

	// ─── Mesh construction ────────────────────────────────────────────────

	private void EnsureFpsMeshes()
	{
		if ( _root.IsValid() )
			return;

		_root = new GameObject( true, "Knife_Viewmodel_FPS" );
		_root.Tags.Add( "viewmodel" );

		_blade = new GameObject( true, "Blade" );
		_blade.SetParent( _root, false );
		_bladeRenderer = _blade.Components.Create<ModelRenderer>();
		_bladeRenderer.Model = Model.Load( "models/dev/box.vmdl" );

		_handle = new GameObject( true, "Handle" );
		_handle.SetParent( _root, false );
		_handleRenderer = _handle.Components.Create<ModelRenderer>();
		_handleRenderer.Model = Model.Load( "models/dev/box.vmdl" );
		_handleRenderer.Tint = new Color( 0.14f, 0.09f, 0.05f );
	}

	private void EnsureTpsMeshes( GameObject boneGo )
	{
		// Rebuild if the bone reference changed (e.g. citizen re-spawned).
		if ( _tpsRoot.IsValid() && _attachedBone == boneGo )
			return;

		if ( _tpsRoot.IsValid() )
		{
			_tpsRoot.Destroy();
			_tpsRoot = null;
		}

		_tpsRoot = new GameObject( true, "Knife_Held_TPS" );
		_tpsRoot.SetParent( boneGo, false );
		_tpsRoot.LocalPosition = TpsLocalOffset;
		_tpsRoot.LocalRotation = Rotation.From( TpsPitch, TpsYaw, TpsRoll );
		_tpsRoot.LocalScale = Vector3.One;

		_tpsBlade = new GameObject( true, "Blade" );
		_tpsBlade.SetParent( _tpsRoot, false );
		_tpsBladeRenderer = _tpsBlade.Components.Create<ModelRenderer>();
		_tpsBladeRenderer.Model = Model.Load( "models/dev/box.vmdl" );

		_tpsHandle = new GameObject( true, "Handle" );
		_tpsHandle.SetParent( _tpsRoot, false );
		_tpsHandleRenderer = _tpsHandle.Components.Create<ModelRenderer>();
		_tpsHandleRenderer.Model = Model.Load( "models/dev/box.vmdl" );
		_tpsHandleRenderer.Tint = new Color( 0.14f, 0.09f, 0.05f );

		_attachedBone = boneGo;
	}

	// ─── Frame update ─────────────────────────────────────────────────────

	protected override void OnUpdate()
	{
		// Only the local owner sees their own viewmodel.
		if ( IsProxy )
		{
			HideAll();
			return;
		}

		var pawn = GameObject.Components.Get<PlayerPawn>();
		if ( !pawn.IsValid() )
		{
			HideAll();
			return;
		}

		var stats = GameObject.Components.Get<PlayerStats>();
		if ( !stats.IsValid() || string.IsNullOrEmpty( stats.EquippedKnifeId ) )
		{
			HideAll();
			return;
		}

		var knife = KnifeCatalog.GetById( stats.EquippedKnifeId );
		if ( knife is null )
		{
			HideAll();
			return;
		}

		EnsureFpsMeshes();

		// Resolve camera lazily for FPS branch.
		if ( !_camera.IsValid() )
			_camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();

		var bladeTint = TintFor( knife.Rarity );

		if ( pawn.FirstPerson )
		{
			// FPS: show camera-anchored knife, hide hand knife.
			if ( _tpsRoot.IsValid() )
				_tpsRoot.Enabled = false;

			if ( !_camera.IsValid() )
			{
				_root.Enabled = false;
				return;
			}

			_root.Enabled = true;
			_bladeRenderer.Tint = bladeTint;
			ApplyShape( knife.Id, _blade, _handle );

			var camRot = _camera.WorldRotation;
			var camPos = _camera.WorldPosition;
			var worldOffset =
				  camRot.Forward * ForwardOffset
				+ camRot.Right   * RightOffset
				+ camRot.Up      * -DownOffset;

			_root.WorldPosition = camPos + worldOffset;
			_root.WorldRotation = camRot * Rotation.From( PitchTweak, YawTweak, RollTweak );
			_root.WorldScale = Vector3.One;
		}
		else
		{
			// TPS: hide camera-anchored knife, show hand-held knife on hold_R.
			if ( _root.IsValid() )
				_root.Enabled = false;

			var boneGo = ResolveHoldBone( pawn );
			if ( !boneGo.IsValid() )
			{
				// No bone found — nothing to attach to.
				if ( _tpsRoot.IsValid() )
					_tpsRoot.Enabled = false;
				return;
			}

			EnsureTpsMeshes( boneGo );
			_tpsRoot.Enabled = true;

			// Keep the root pinned to the bone with the configured tweak each frame
			// in case the inspector values are tuned at runtime.
			_tpsRoot.LocalPosition = TpsLocalOffset;
			_tpsRoot.LocalRotation = Rotation.From( TpsPitch, TpsYaw, TpsRoll );

			_tpsBladeRenderer.Tint = bladeTint;
			ApplyShape( knife.Id, _tpsBlade, _tpsHandle );
		}
	}

	private GameObject ResolveHoldBone( PlayerPawn pawn )
	{
		if ( !pawn.BodyRenderer.IsValid() )
			return null;

		// Try the configured grip bone, fall back to hand_R if the rig uses a
		// different naming convention.
		return pawn.BodyRenderer.GetBoneObject( HoldBoneName )
			?? pawn.BodyRenderer.GetBoneObject( "hand_R" );
	}

	private void HideAll()
	{
		if ( _root.IsValid() ) _root.Enabled = false;
		if ( _tpsRoot.IsValid() ) _tpsRoot.Enabled = false;
	}

	// ─── Per-knife shape ──────────────────────────────────────────────────

	/// <summary>Set blade + handle local transforms to match the knife's silhouette.</summary>
	private static void ApplyShape( string knifeId, GameObject blade, GameObject handle )
	{
		// Format: (bladeLocalPos, bladeLocalRot, bladeScale, handleLocalPos, handleLocalRot, handleScale)
		// All in local space of the parent root. +X = forward, +Y = right, +Z = up.
		var preset = knifeId switch
		{
			"katana"           => ( new Vector3( 8,  0, 0),  Rotation.Identity,           new Vector3( 0.50f, 0.025f, 0.06f ),
									new Vector3(-3,  0, 0),  Rotation.Identity,           new Vector3( 0.18f, 0.07f,  0.06f ) ),
			"karambit"         => ( new Vector3( 4,  0, 1),  Rotation.From( 0, 0, 35),    new Vector3( 0.16f, 0.04f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,           new Vector3( 0.10f, 0.06f,  0.05f ) ),
			"cursed_karambit"  => ( new Vector3( 4,  0, 1),  Rotation.From( 0, 0, 40),    new Vector3( 0.18f, 0.04f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,           new Vector3( 0.10f, 0.06f,  0.05f ) ),
			"butterfly"        => ( new Vector3( 5,  0, 0),  Rotation.Identity,           new Vector3( 0.22f, 0.03f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,           new Vector3( 0.12f, 0.05f,  0.05f ) ),
			"bayonet"          => ( new Vector3( 7,  0, 0),  Rotation.Identity,           new Vector3( 0.35f, 0.03f,  0.05f ),
									new Vector3(-3,  0, 0),  Rotation.Identity,           new Vector3( 0.14f, 0.06f,  0.05f ) ),
			"skull"            => ( new Vector3( 5,  0, 0),  Rotation.Identity,           new Vector3( 0.26f, 0.035f, 0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,           new Vector3( 0.10f, 0.06f,  0.05f ) ),
			_                  => ( new Vector3( 5,  0, 0),  Rotation.Identity,           new Vector3( 0.24f, 0.03f,  0.04f ),  // steel
									new Vector3(-2,  0, 0),  Rotation.Identity,           new Vector3( 0.10f, 0.05f,  0.05f ) ),
		};

		blade.LocalPosition  = preset.Item1;
		blade.LocalRotation  = preset.Item2;
		blade.LocalScale     = preset.Item3;
		handle.LocalPosition = preset.Item4;
		handle.LocalRotation = preset.Item5;
		handle.LocalScale    = preset.Item6;
	}

	private static Color TintFor( KnifeRarity r ) => r switch
	{
		KnifeRarity.Common   => new Color( 0.78f, 0.80f, 0.85f ),
		KnifeRarity.Uncommon => new Color( 0.42f, 0.95f, 0.55f ),
		KnifeRarity.Rare     => new Color( 0.35f, 0.62f, 1.00f ),
		KnifeRarity.Epic     => new Color( 0.78f, 0.40f, 1.00f ),
		KnifeRarity.Mythic   => new Color( 1.00f, 0.45f, 0.20f ),
		KnifeRarity.Secret   => new Color( 1.00f, 0.85f, 0.30f ),
		_ => Color.White
	};
}
