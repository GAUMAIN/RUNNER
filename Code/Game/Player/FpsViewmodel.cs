using System;
using System.Linq;
using Sandbox;
using Runner.Economy;

namespace Runner.Player;

/// <summary>
/// First-person viewmodel for RUNNER, built around the Facepunch FPS arms asset
/// (<c>models/first_person/v_first_person_arms_citizen.vmdl</c>) and the official
/// melee animgraph (<c>v_first_person_arms_punching.vanmgrph</c>).
///
/// Architecture mirrors the pattern used by <c>sbox-scenestaging</c>'s weaponlab
/// <c>TestWeapon</c>:
/// <list type="bullet">
///   <item>A standalone GameObject (<see cref="_viewmodel"/>) anchored to the
///         camera every frame in <c>OnPreRender</c>.</item>
///   <item>The arms <see cref="SkinnedModelRenderer"/> uses
///         <c>RenderOptions.Overlay = true</c> + <c>Game = false</c> so it draws
///         on top without ever being clipped by world geometry — the classic
///         viewmodel render pass.</item>
///   <item>Shadows are off (<see cref="ModelRenderer.ShadowRenderType.Off"/>) —
///         the world model (Citizen body) on the player handles the shadow.</item>
///   <item>The knife is a non-animated child mesh whose world transform is
///         written every frame from the arms' right-hand bone, so it stays
///         glued to the hand through the animgraph's motion.</item>
/// </list>
///
/// Only the local owner sees this viewmodel — proxies (other clients) see the
/// TPS body via <see cref="PlayerPawn.BodyRenderer"/>. The Citizen body on the
/// local player is set to <see cref="ModelRenderer.ShadowRenderType.ShadowsOnly"/>
/// in 1st person so it still casts a shadow but doesn't render its head in the
/// player's face.
/// </summary>
public sealed class FpsViewmodel : Component
{
	// ─── Model + animgraph ────────────────────────────────────────────────

	[Property]
	public Model ArmsModel { get; set; }

	[Property]
	public AnimationGraph ArmsAnimationGraph { get; set; }

	// Default asset paths — loaded lazily in BuildViewmodel if the Property is
	// still null, so the component works out of the box without any inspector
	// wiring while still letting designers override.
	private const string DefaultArmsModelPath = "models/first_person/v_first_person_arms_citizen.vmdl";
	private const string DefaultArmsGraphPath = "models/first_person/v_first_person_arms_punching.vanmgrph";

	// ─── Camera anchor offsets (tuneable in inspector) ────────────────────

	[Property] public Vector3 ViewModelOffset { get; set; } = new Vector3( 0, 0, -2 );
	[Property] public Angles ViewModelRotation { get; set; } = new Angles( 0, 0, 0 );

	// ─── Knife attachment ─────────────────────────────────────────────────

	/// <summary>Name of the hand bone on the arms rig where the knife clips.</summary>
	[Property] public string HandBoneName { get; set; } = "hold_R";

	/// <summary>Fine-tune the knife's local pose in the hand.</summary>
	[Property] public Vector3 KnifeLocalOffset { get; set; } = new Vector3( 0, 0, 0 );
	[Property] public Angles KnifeLocalRotation { get; set; } = new Angles( 0, 0, 0 );

	[Property] public SoundEvent InspectSound { get; set; }

	// ─── Internal state ───────────────────────────────────────────────────

	private GameObject _viewmodel;
	private SkinnedModelRenderer _arms;
	private GameObject _knifeRoot;
	private GameObject _knifeBlade;
	private GameObject _knifeHandle;
	private ModelRenderer _knifeBladeRenderer;
	private ModelRenderer _knifeHandleRenderer;
	private CameraComponent _camera;
	private Rotation _lastCamRot;
	private bool _boneWarned;

	// Inspect — F triggers the deploy parameter on the animgraph.
	private TimeSince _timeSinceInspect = 999f;
	private const float InspectCooldown = 0.6f;

	// ─── Lifecycle ────────────────────────────────────────────────────────

	protected override void OnEnabled()
	{
		base.OnEnabled();
		BuildViewmodel();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		DestroyViewmodel();
	}

	private void BuildViewmodel()
	{
		if ( _viewmodel.IsValid() ) return;

		// Lazy-load defaults so the component just works after AddComponent.
		var model = ArmsModel ?? Model.Load( DefaultArmsModelPath );
		if ( model is null )
		{
			Log.Warning( $"[FpsViewmodel] Could not load arms model '{DefaultArmsModelPath}' — viewmodel disabled." );
			return;
		}

		_viewmodel = new GameObject( true, "FpsViewmodel" );
		_viewmodel.Tags.Add( "viewmodel" );

		// Arms — primary skinned mesh + animgraph + viewmodel render flags.
		_arms = _viewmodel.Components.Create<SkinnedModelRenderer>();
		_arms.Model = model;
		_arms.CreateBoneObjects = true;
		_arms.RenderOptions.Overlay = true;     // draws on top — no world clipping
		_arms.RenderOptions.Game = false;        // skip the game render pass
		_arms.RenderType = ModelRenderer.ShadowRenderType.Off;

		// Try the assigned animgraph first; fall back to the punching graph the
		// Facepunch FPS arms ship with. If neither resolves, the model's baked
		// default animgraph plays (idle pose).
		var graph = ArmsAnimationGraph
			?? ResourceLibrary.Get<AnimationGraph>( DefaultArmsGraphPath );
		if ( graph is not null )
			_arms.AnimationGraph = graph;

		// Knife — non-animated cube primitives, world transform driven from the
		// arms' hand bone every frame in OnPreRender.
		_knifeRoot = new GameObject( true, "Knife" );

		_knifeBlade = new GameObject( true, "Blade" );
		_knifeBlade.SetParent( _knifeRoot, false );
		_knifeBladeRenderer = _knifeBlade.Components.Create<ModelRenderer>();
		_knifeBladeRenderer.Model = Model.Load( "models/dev/box.vmdl" );
		_knifeBladeRenderer.RenderOptions.Overlay = true;
		_knifeBladeRenderer.RenderOptions.Game = false;
		_knifeBladeRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		_knifeHandle = new GameObject( true, "Handle" );
		_knifeHandle.SetParent( _knifeRoot, false );
		_knifeHandleRenderer = _knifeHandle.Components.Create<ModelRenderer>();
		_knifeHandleRenderer.Model = Model.Load( "models/dev/box.vmdl" );
		_knifeHandleRenderer.RenderOptions.Overlay = true;
		_knifeHandleRenderer.RenderOptions.Game = false;
		_knifeHandleRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		_knifeHandleRenderer.Tint = new Color( 0.14f, 0.09f, 0.05f );
	}

	private void DestroyViewmodel()
	{
		if ( _viewmodel.IsValid() )
		{
			_viewmodel.Destroy();
			_viewmodel = null;
		}
		if ( _knifeRoot.IsValid() )
		{
			_knifeRoot.Destroy();
			_knifeRoot = null;
		}
		_arms = null;
		_camera = null;
	}

	// ─── Per-frame ────────────────────────────────────────────────────────

	protected override void OnUpdate()
	{
		// Trigger inspect on F, in FPS only, when not paused.
		if ( IsProxy ) return;

		var pawn = GameObject.Components.Get<PlayerPawn>();
		if ( !pawn.IsValid() ) return;
		if ( !pawn.FirstPerson ) return;
		if ( Runner.Config.UserSettings.IsPaused ) return;

		if ( _timeSinceInspect > InspectCooldown && Input.Pressed( "Inspect" ) )
		{
			_timeSinceInspect = 0f;
			TriggerInspect();
		}
	}

	/// <summary>
	/// Anchor the viewmodel to the camera and refresh per-frame animgraph
	/// parameters. Runs in OnPreRender so the write happens AFTER any input /
	/// movement update but before the final render pass, eliminating the
	/// one-frame lag you'd get from OnUpdate.
	/// </summary>
	protected override void OnPreRender()
	{
		if ( IsProxy )
		{
			SetActive( false );
			return;
		}

		var pawn = GameObject.Components.Get<PlayerPawn>();
		if ( !pawn.IsValid() )
		{
			SetActive( false );
			return;
		}

		// Viewmodel is FPS-only. In TPS we hide it entirely; the world Citizen
		// body + the KnifeViewModel handle the visual there.
		if ( !pawn.FirstPerson )
		{
			SetActive( false );
			return;
		}

		// Resolve camera once.
		if ( !_camera.IsValid() )
			_camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( !_camera.IsValid() )
		{
			SetActive( false );
			return;
		}

		if ( !_viewmodel.IsValid() )
			BuildViewmodel();
		if ( !_viewmodel.IsValid() )
			return;

		var stats = GameObject.Components.Get<PlayerStats>();
		var knife = stats.IsValid() && !string.IsNullOrEmpty( stats.EquippedKnifeId )
			? KnifeCatalog.GetById( stats.EquippedKnifeId )
			: null;

		SetActive( true );

		// Anchor to camera with the configured local tweak.
		_viewmodel.WorldPosition = _camera.WorldPosition;
		_viewmodel.WorldRotation = _camera.WorldRotation * ViewModelRotation.ToRotation();
		_viewmodel.LocalPosition += _viewmodel.WorldRotation * ViewModelOffset;
		_viewmodel.WorldScale = Vector3.One;

		FeedAnimgraph( pawn );
		UpdateKnife( knife );
	}

	private void SetActive( bool on )
	{
		if ( _viewmodel.IsValid() )
			_viewmodel.Enabled = on;
		if ( _knifeRoot.IsValid() )
			_knifeRoot.Enabled = on;
	}

	/// <summary>Feed common viewmodel animgraph params so the arms move with the player.</summary>
	private void FeedAnimgraph( PlayerPawn pawn )
	{
		if ( !_arms.IsValid() ) return;

		var cc = pawn.GameObject.Components.Get<CharacterController>();
		if ( !cc.IsValid() ) return;

		var rot = _arms.WorldRotation;
		var localVel = new Vector3(
			rot.Forward.Dot( cc.Velocity ),
			rot.Right.Dot( cc.Velocity ),
			cc.Velocity.z
		);

		_arms.Set( "move_x", localVel.x );
		_arms.Set( "move_y", localVel.y );
		_arms.Set( "move_z", localVel.z );
		_arms.Set( "move_groundspeed", cc.Velocity.WithZ( 0 ).Length );
		_arms.Set( "b_grounded", cc.IsOnGround );
		_arms.Set( "b_sprint", pawn.IsSprinting && cc.Velocity.Length > 50 );

		// Camera rotation delta → aim_pitch/yaw inertia for a subtle weapon sway.
		var camRot = _camera.WorldRotation;
		var delta = Rotation.Difference( _lastCamRot, camRot ).Angles();
		_lastCamRot = camRot;
		_arms.Set( "aim_pitch", delta.pitch );
		_arms.Set( "aim_yaw", delta.yaw );
	}

	/// <summary>Place the knife at the arms' hand bone with the configured local offset.</summary>
	private void UpdateKnife( KnifeSkin knife )
	{
		if ( !_knifeRoot.IsValid() ) return;

		// No knife equipped → hide.
		if ( knife is null )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		var handGo = ResolveHandBone();
		if ( !handGo.IsValid() )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		_knifeRoot.Enabled = true;

		var localRot = KnifeLocalRotation.ToRotation();
		_knifeRoot.WorldRotation = handGo.WorldRotation * localRot;
		_knifeRoot.WorldPosition = handGo.WorldPosition + handGo.WorldRotation * KnifeLocalOffset;
		_knifeRoot.WorldScale = Vector3.One;

		// Tint blade by rarity, dark handle, and apply per-knife shape.
		_knifeBladeRenderer.Tint = TintFor( knife.Rarity );
		ApplyShape( knife.Id, _knifeBlade, _knifeHandle );
	}

	private GameObject ResolveHandBone()
	{
		if ( !_arms.IsValid() || _arms.Model is null ) return null;

		string[] candidates = { HandBoneName, "hold_R", "hand_R", "weapon_bone" };

		int n = _arms.Model.BoneCount;
		for ( int i = 0; i < n; i++ )
		{
			var o = _arms.GetBoneObject( i );
			if ( !o.IsValid() ) continue;
			foreach ( var c in candidates )
			{
				if ( !string.IsNullOrEmpty( c ) && o.Name == c )
					return o;
			}
		}

		if ( !_boneWarned )
		{
			_boneWarned = true;
			var names = new System.Collections.Generic.List<string>();
			int sample = Math.Min( n, 40 );
			for ( int i = 0; i < sample; i++ )
			{
				var o = _arms.GetBoneObject( i );
				if ( o.IsValid() ) names.Add( o.Name );
			}
			Log.Warning( $"[FpsViewmodel] No hand bone found. Arms bones (first {sample} of {n}): {string.Join( ", ", names )}" );
		}
		return null;
	}

	private void TriggerInspect()
	{
		if ( _arms.IsValid() )
		{
			// The punching animgraph exposes b_deploy / b_inspect on most rigs;
			// flip both so whichever the graph supports fires.
			_arms.Set( "b_deploy", true );
			_arms.Set( "b_inspect", true );
		}

		var handle = InspectSound is not null
			? Sound.Play( InspectSound )
			: Sound.Play( "sounds/kenney/ui/ui.button.press.sound" );
		if ( handle is not null )
			handle.Volume *= Runner.Config.UserSettings.Volume;
	}

	// ─── Per-knife shape preset (mirror of KnifeViewModel) ────────────────

	private static void ApplyShape( string knifeId, GameObject blade, GameObject handle )
	{
		var preset = knifeId switch
		{
			"katana"           => ( new Vector3( 8,  0, 0),  Rotation.Identity,        new Vector3( 0.50f, 0.025f, 0.06f ),
									new Vector3(-3,  0, 0),  Rotation.Identity,        new Vector3( 0.18f, 0.07f,  0.06f ) ),
			"karambit"         => ( new Vector3( 4,  0, 1),  Rotation.From( 0, 0, 35), new Vector3( 0.16f, 0.04f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,        new Vector3( 0.10f, 0.06f,  0.05f ) ),
			"cursed_karambit"  => ( new Vector3( 4,  0, 1),  Rotation.From( 0, 0, 40), new Vector3( 0.18f, 0.04f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,        new Vector3( 0.10f, 0.06f,  0.05f ) ),
			"butterfly"        => ( new Vector3( 5,  0, 0),  Rotation.Identity,        new Vector3( 0.22f, 0.03f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,        new Vector3( 0.12f, 0.05f,  0.05f ) ),
			"bayonet"          => ( new Vector3( 7,  0, 0),  Rotation.Identity,        new Vector3( 0.35f, 0.03f,  0.05f ),
									new Vector3(-3,  0, 0),  Rotation.Identity,        new Vector3( 0.14f, 0.06f,  0.05f ) ),
			"skull"            => ( new Vector3( 5,  0, 0),  Rotation.Identity,        new Vector3( 0.26f, 0.035f, 0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,        new Vector3( 0.10f, 0.06f,  0.05f ) ),
			_                  => ( new Vector3( 5,  0, 0),  Rotation.Identity,        new Vector3( 0.24f, 0.03f,  0.04f ),
									new Vector3(-2,  0, 0),  Rotation.Identity,        new Vector3( 0.10f, 0.05f,  0.05f ) ),
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
