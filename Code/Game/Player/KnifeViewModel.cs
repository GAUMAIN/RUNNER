using System;
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

	// ─── Inspect animation ────────────────────────────────────────────────
	[Property] public SoundEvent InspectSound { get; set; }
	[Property] public float InspectDuration { get; set; } = 1.2f;

	private TimeSince _timeSinceInspect = 999f;
	private bool IsInspecting => _timeSinceInspect < InspectDuration;

	// FPS hierarchy (camera-anchored)
	private GameObject _root;
	private GameObject _blade;
	private GameObject _handle;
	private ModelRenderer _bladeRenderer;
	private ModelRenderer _handleRenderer;

	// TPS hierarchy — free in the scene, written to bone transform each frame.
	private GameObject _tpsRoot;
	private GameObject _tpsBlade;
	private GameObject _tpsHandle;
	private ModelRenderer _tpsBladeRenderer;
	private ModelRenderer _tpsHandleRenderer;

	private CameraComponent _camera;

	// Logged once the first time we resolve (or fail to resolve) the grip bone.
	private bool _boneDebugLogged;
	private string _resolvedBoneName;

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

	private void EnsureTpsMeshes()
	{
		if ( _tpsRoot.IsValid() )
			return;

		// Free in the scene — we write its WorldTransform from the bone each frame.
		_tpsRoot = new GameObject( true, "Knife_Held_TPS" );

		_tpsBlade = new GameObject( true, "Blade" );
		_tpsBlade.SetParent( _tpsRoot, false );
		_tpsBladeRenderer = _tpsBlade.Components.Create<ModelRenderer>();
		_tpsBladeRenderer.Model = Model.Load( "models/dev/box.vmdl" );

		_tpsHandle = new GameObject( true, "Handle" );
		_tpsHandle.SetParent( _tpsRoot, false );
		_tpsHandleRenderer = _tpsHandle.Components.Create<ModelRenderer>();
		_tpsHandleRenderer.Model = Model.Load( "models/dev/box.vmdl" );
		_tpsHandleRenderer.Tint = new Color( 0.14f, 0.09f, 0.05f );
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

		// Resolve once — used both for the inspect-sound gate and the FPS-render gate.
		var fpsVm = GameObject.Components.Get<FpsViewmodel>();
		bool fpsVmOwnsView = fpsVm.IsValid() && fpsVm.Enabled;

		// Inspect trigger — only when no FpsViewmodel is on the player. If there
		// is one, IT owns the inspect (sound + animgraph trigger), so skip here
		// to avoid playing two sounds.
		if ( !fpsVmOwnsView
			&& pawn.FirstPerson
			&& !Runner.Config.UserSettings.IsPaused
			&& !IsInspecting
			&& Input.Pressed( "Inspect" ) )
		{
			_timeSinceInspect = 0f;
			PlayInspectSound();
		}

		EnsureFpsMeshes();

		// Resolve camera lazily for FPS branch.
		if ( !_camera.IsValid() )
			_camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();

		var bladeTint = TintFor( knife.Rarity );

		if ( pawn.FirstPerson )
		{
			// FpsViewmodel handles the FPS knife → hide both of OUR meshes here.
			if ( fpsVmOwnsView )
			{
				if ( _root.IsValid() )    _root.Enabled    = false;
				if ( _tpsRoot.IsValid() ) _tpsRoot.Enabled = false;
				return;
			}

			// Legacy fallback: no FpsViewmodel → show camera-anchored knife.
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

			// Inspect anim: bell-shaped position pull-in + smooth full spin around the
			// camera-forward axis. t goes 0→1 over InspectDuration; ramp peaks at 0.5
			// (knife closest to camera, raised, centered).
			float t = IsInspecting ? Math.Clamp( (float)_timeSinceInspect / InspectDuration, 0f, 1f ) : 0f;
			float ramp = MathF.Sin( t * MathF.PI );      // 0 → 1 → 0
			float spin = t * 360f;                        // one full revolution

			float fwd   = ForwardOffset - ramp * 4f;     // pull closer to the face
			float right = RightOffset   - ramp * 8f;     // slide toward center
			float down  = DownOffset    - ramp * 5f;     // raise up

			var camRot = _camera.WorldRotation;
			var camPos = _camera.WorldPosition;
			var worldOffset =
				  camRot.Forward * fwd
				+ camRot.Right   * right
				+ camRot.Up      * -down;

			_root.WorldPosition = camPos + worldOffset;
			_root.WorldRotation = camRot
				* Rotation.From( PitchTweak, YawTweak, RollTweak )
				* Rotation.From( 0f, 0f, spin );        // roll around forward
			_root.WorldScale = Vector3.One;
		}
		else
		{
			// TPS: hide camera-anchored knife, show hand-held knife on the grip bone.
			if ( _root.IsValid() )
				_root.Enabled = false;

			if ( !TryResolveHoldTransform( pawn, out var boneTx ) )
			{
				if ( _tpsRoot.IsValid() )
					_tpsRoot.Enabled = false;
				return;
			}

			EnsureTpsMeshes();
			_tpsRoot.Enabled = true;

			// Position the root at the bone, then apply the local tweak.
			var tweakRot = Rotation.From( TpsPitch, TpsYaw, TpsRoll );
			_tpsRoot.WorldRotation = boneTx.Rotation * tweakRot;
			_tpsRoot.WorldPosition = boneTx.Position + boneTx.Rotation * TpsLocalOffset;
			_tpsRoot.WorldScale = Vector3.One;

			_tpsBladeRenderer.Tint = bladeTint;
			ApplyShape( knife.Id, _tpsBlade, _tpsHandle );
		}
	}

	/// <summary>
	/// Sample the world transform of the citizen's grip bone. Tries several
	/// common bone names since rigs vary, and logs the resolved name once for
	/// debugging.
	/// </summary>
	private bool TryResolveHoldTransform( PlayerPawn pawn, out Transform tx )
	{
		tx = global::Transform.Zero;

		var body = pawn.BodyRenderer;
		if ( !body.IsValid() || body.Model is null )
			return false;

		// Candidate names — Citizen rig uses hold_R/hand_R, but be defensive.
		string[] candidates = new[]
		{
			HoldBoneName,
			"hold_R", "hand_R", "Hand_R",
			"R_Hand", "RightHand", "right_hand"
		};

		// Iterate the bone GameObjects directly and match by name. This avoids
		// relying on the Bones.GetBone(name) API which can return null on some
		// rigs/versions even when the bone exists.
		int count = body.Model.BoneCount;
		for ( int i = 0; i < count; i++ )
		{
			var o = body.GetBoneObject( i );
			if ( !o.IsValid() ) continue;
			foreach ( var cand in candidates )
			{
				if ( string.IsNullOrEmpty( cand ) ) continue;
				if ( o.Name != cand ) continue;

				if ( !_boneDebugLogged )
				{
					_resolvedBoneName = cand;
					Log.Info( $"[KnifeViewModel] Resolved grip bone '{cand}' at index {i}" );
					_boneDebugLogged = true;
				}
				tx = new global::Transform( o.WorldPosition, o.WorldRotation, 1f );
				return true;
			}
		}

		// Diagnostic: log the first ~40 bone names by iterating bone indices.
		if ( !_boneDebugLogged )
		{
			_boneDebugLogged = true;
			try
			{
				var names = new System.Collections.Generic.List<string>();
				int sample = System.Math.Min( body.Model.BoneCount, 40 );
				for ( int i = 0; i < sample; i++ )
				{
					var o = body.GetBoneObject( i );
					if ( o.IsValid() ) names.Add( o.Name );
				}
				Log.Warning( $"[KnifeViewModel] No grip bone found. Available bones (first {sample} of {body.Model.BoneCount}): {string.Join( ", ", names )}" );
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"[KnifeViewModel] No grip bone found and bone enumeration failed: {e.Message}" );
			}
		}

		return false;
	}

	private void HideAll()
	{
		if ( _root.IsValid() ) _root.Enabled = false;
		if ( _tpsRoot.IsValid() ) _tpsRoot.Enabled = false;
	}

	// Inspect uses a Kenney UI tick as a placeholder until a real knife sound is wired.
	private const string InspectFallbackSound = "sounds/kenney/ui/ui.button.press.sound";

	private void PlayInspectSound()
	{
		var handle = InspectSound is not null
			? Sound.Play( InspectSound )
			: Sound.Play( InspectFallbackSound );
		if ( handle is not null )
			handle.Volume *= Runner.Config.UserSettings.Volume;
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
