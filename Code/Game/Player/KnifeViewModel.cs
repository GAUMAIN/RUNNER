using System.Linq;
using Sandbox;
using Runner.Economy;

namespace Runner.Player;

/// <summary>
/// First-person knife viewmodel. Reads the player's equipped knife and renders
/// a primitive box (tinted by rarity) anchored to the camera each frame.
///
/// Attach to the same GameObject as PlayerPawn / PlayerStats. The mesh is
/// created at runtime as a root GameObject we own — re-positioned each Update
/// to follow the camera transform with a screen-corner offset.
///
/// Real .vmdl knife models can swap the dev box later; the tint/swap logic
/// here stays the same.
/// </summary>
public sealed class KnifeViewModel : Component
{
	[Property] public float ForwardOffset { get; set; } = 14f;
	[Property] public float RightOffset { get; set; } = 8f;
	[Property] public float DownOffset { get; set; } = 9f;

	/// <summary>Knife "blade" scale (length, width, thickness).</summary>
	[Property] public Vector3 ViewScale { get; set; } = new Vector3( 0.3f, 0.04f, 0.04f );

	/// <summary>Yaw/Pitch tweak to make the blade angle look "held".</summary>
	[Property] public float YawTweak { get; set; } = -12f;
	[Property] public float PitchTweak { get; set; } = -25f;
	[Property] public float RollTweak { get; set; } = 0f;

	private GameObject _knifeGO;
	private ModelRenderer _renderer;
	private CameraComponent _camera;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		EnsureKnife();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if ( _knifeGO.IsValid() )
		{
			_knifeGO.Destroy();
			_knifeGO = null;
		}
	}

	private void EnsureKnife()
	{
		if ( _knifeGO.IsValid() )
			return;

		_knifeGO = new GameObject( true, "Knife_Viewmodel" );
		_knifeGO.Tags.Add( "viewmodel" );

		_renderer = _knifeGO.Components.Create<ModelRenderer>();
		_renderer.Model = Model.Load( "models/dev/box.vmdl" );
		_renderer.Tint = Color.White;
	}

	protected override void OnUpdate()
	{
		// Only the local owner sees their own viewmodel.
		if ( IsProxy )
		{
			if ( _knifeGO.IsValid() )
				_knifeGO.Enabled = false;
			return;
		}

		EnsureKnife();

		if ( !_camera.IsValid() )
			_camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( !_camera.IsValid() )
		{
			_knifeGO.Enabled = false;
			return;
		}

		var stats = GameObject.Components.Get<PlayerStats>();
		if ( !stats.IsValid() || string.IsNullOrEmpty( stats.EquippedKnifeId ) )
		{
			_knifeGO.Enabled = false;
			return;
		}

		var knife = KnifeCatalog.GetById( stats.EquippedKnifeId );
		if ( knife is null )
		{
			_knifeGO.Enabled = false;
			return;
		}

		_knifeGO.Enabled = true;
		_renderer.Tint = TintFor( knife.Rarity );

		var camRot = _camera.WorldRotation;
		var camPos = _camera.WorldPosition;
		var worldOffset =
			  camRot.Forward * ForwardOffset
			+ camRot.Right   * RightOffset
			+ camRot.Up      * -DownOffset;

		_knifeGO.WorldPosition = camPos + worldOffset;
		_knifeGO.WorldRotation = camRot * Rotation.From( PitchTweak, YawTweak, RollTweak );
		_knifeGO.WorldScale = ViewScale;
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
