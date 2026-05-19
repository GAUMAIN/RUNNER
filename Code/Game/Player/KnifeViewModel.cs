using System.Linq;
using Sandbox;
using Runner.Economy;

namespace Runner.Player;

/// <summary>
/// First-person knife viewmodel. Two primitives — blade + handle — composed
/// into a recognizable knife silhouette and anchored to the camera each Update.
///
/// Per-knife presets vary the blade shape (Katana long & wide, Karambit short
/// & angled, Butterfly compact, Bayonet medium…) so each skin reads differently
/// even without real .vmdl assets.
///
/// Tint applies to the blade only; the handle stays dark to read as a grip.
/// Real .vmdl models can replace the boxes later; the per-knife preset table
/// (shape + rotation) stays the same.
/// </summary>
public sealed class KnifeViewModel : Component
{
	[Property] public float ForwardOffset { get; set; } = 14f;
	[Property] public float RightOffset { get; set; } = 8f;
	[Property] public float DownOffset { get; set; } = 9f;

	/// <summary>Yaw/Pitch/Roll tweak to make the blade angle look "held".</summary>
	[Property] public float YawTweak { get; set; } = -8f;
	[Property] public float PitchTweak { get; set; } = -22f;
	[Property] public float RollTweak { get; set; } = 0f;

	private GameObject _root;
	private GameObject _blade;
	private GameObject _handle;
	private ModelRenderer _bladeRenderer;
	private ModelRenderer _handleRenderer;
	private CameraComponent _camera;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		EnsureMeshes();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if ( _root.IsValid() )
		{
			_root.Destroy();
			_root = null;
		}
	}

	private void EnsureMeshes()
	{
		if ( _root.IsValid() )
			return;

		_root = new GameObject( true, "Knife_Viewmodel" );
		_root.Tags.Add( "viewmodel" );

		_blade = new GameObject( true, "Blade" );
		_blade.SetParent( _root, false );
		_bladeRenderer = _blade.Components.Create<ModelRenderer>();
		_bladeRenderer.Model = Model.Load( "models/dev/box.vmdl" );

		_handle = new GameObject( true, "Handle" );
		_handle.SetParent( _root, false );
		_handleRenderer = _handle.Components.Create<ModelRenderer>();
		_handleRenderer.Model = Model.Load( "models/dev/box.vmdl" );
		_handleRenderer.Tint = new Color( 0.14f, 0.09f, 0.05f ); // dark wood/leather grip
	}

	protected override void OnUpdate()
	{
		// Only the local owner sees their own viewmodel.
		if ( IsProxy )
		{
			if ( _root.IsValid() )
				_root.Enabled = false;
			return;
		}

		EnsureMeshes();

		if ( !_camera.IsValid() )
			_camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( !_camera.IsValid() )
		{
			_root.Enabled = false;
			return;
		}

		var stats = GameObject.Components.Get<PlayerStats>();
		if ( !stats.IsValid() || string.IsNullOrEmpty( stats.EquippedKnifeId ) )
		{
			_root.Enabled = false;
			return;
		}

		var knife = KnifeCatalog.GetById( stats.EquippedKnifeId );
		if ( knife is null )
		{
			_root.Enabled = false;
			return;
		}

		_root.Enabled = true;
		_bladeRenderer.Tint = TintFor( knife.Rarity );

		// Per-knife shape preset
		ApplyShape( knife.Id );

		// Anchor root to camera with screen-corner offset
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

	/// <summary>Set blade + handle local transforms to match the knife's silhouette.</summary>
	private void ApplyShape( string knifeId )
	{
		// Format: (bladeLocalPos, bladeLocalRot, bladeScale, handleLocalPos, handleLocalRot, handleScale)
		// All in local space of _root. +X = forward, +Y = right (Source), +Z = up.
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

		_blade.LocalPosition  = preset.Item1;
		_blade.LocalRotation  = preset.Item2;
		_blade.LocalScale     = preset.Item3;
		_handle.LocalPosition = preset.Item4;
		_handle.LocalRotation = preset.Item5;
		_handle.LocalScale    = preset.Item6;
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
