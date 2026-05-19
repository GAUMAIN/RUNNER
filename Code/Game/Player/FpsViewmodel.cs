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

	/// <summary>
	/// Debug toggle. When true the viewmodel uses the proper Overlay render path
	/// (drawn on top, never clipped). When false the arms render in the normal
	/// game pass so they're visible "in world" — useful to confirm the model
	/// loaded and is positioned correctly without trusting the overlay system.
	///
	/// Defaulted to FALSE for now: lets us first verify the arms actually load
	/// and anchor to the camera. Once that's confirmed, flip this on for the
	/// proper CS-style overlay pass.
	/// </summary>
	[Property] public bool UseOverlayRender { get; set; } = false;

	// ─── Internal state ───────────────────────────────────────────────────

	private GameObject _viewmodel;
	private SkinnedModelRenderer _arms;

	// Real .vmdl knife rendered on the arms' hand bone. We track the currently
	// loaded knife id so we only swap the Model when the catalog selection
	// actually changes.
	private GameObject _knifeRoot;
	private SkinnedModelRenderer _knifeRenderer;
	private string _knifeLoadedId;

	private CameraComponent _camera;
	private Rotation _lastCamRot;
	private bool _boneWarned;

	// Inspect — F triggers the deploy parameter on the animgraph.
	private TimeSince _timeSinceInspect = 999f;
	private const float InspectCooldown = 0.6f;

	// ─── Lifecycle ────────────────────────────────────────────────────────

	private bool _firstPreRenderLogged;
	private bool _dumpBonesPending;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		Log.Info( $"[FpsViewmodel] OnEnabled (IsProxy={IsProxy})" );
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

		Log.Info( "[FpsViewmodel] BuildViewmodel start" );

		// Lazy-load defaults so the component just works after AddComponent.
		var model = ArmsModel ?? Model.Load( DefaultArmsModelPath );
		if ( model is null )
		{
			Log.Warning( $"[FpsViewmodel] Could not load arms model '{DefaultArmsModelPath}' — viewmodel disabled. Falling back to dev box so something visible renders." );
			model = Model.Load( "models/dev/box.vmdl" );
			if ( model is null )
			{
				Log.Error( "[FpsViewmodel] Even box.vmdl failed to load — bailing." );
				return;
			}
		}
		else
		{
			Log.Info( $"[FpsViewmodel] Arms model loaded: {model.ResourcePath} (BoneCount={model.BoneCount})" );
		}

		// One-shot bone dump so we can pick the right HandBoneName if our defaults
		// don't match. Bones are enumerated lazily via GetBoneObject — we'll do
		// this in OnPreRender once the renderer has created them.
		_dumpBonesPending = model.BoneCount > 0;

		_viewmodel = new GameObject( true, "FpsViewmodel" );
		_viewmodel.Tags.Add( "viewmodel" );

		// Arms — primary skinned mesh + animgraph + viewmodel render flags.
		_arms = _viewmodel.Components.Create<SkinnedModelRenderer>();
		_arms.Model = model;
		_arms.CreateBoneObjects = true;
		// NOTE: Overlay flags are gated behind a debug Property so we can A/B test
		// the viewmodel render path — if the user sees nothing, flipping these
		// off in inspector reveals whether it's a render-pass problem.
		_arms.RenderOptions.Overlay = UseOverlayRender;
		_arms.RenderOptions.Game = !UseOverlayRender;
		_arms.RenderType = ModelRenderer.ShadowRenderType.Off;

		// Try the assigned animgraph first; fall back to the punching graph the
		// Facepunch FPS arms ship with. If neither resolves, the model's baked
		// default animgraph plays (idle pose).
		var graph = ArmsAnimationGraph
			?? ResourceLibrary.Get<AnimationGraph>( DefaultArmsGraphPath );
		if ( graph is not null )
		{
			_arms.AnimationGraph = graph;
			Log.Info( $"[FpsViewmodel] AnimationGraph set: {graph.ResourcePath}" );
		}
		else
		{
			Log.Info( "[FpsViewmodel] No animgraph — model's baked default will play (idle)." );
		}

		// Knife — single SkinnedModelRenderer holding a REAL knife .vmdl. World
		// transform is driven from the arms' hand bone every frame in OnPreRender.
		// The actual Model is loaded lazily in UpdateKnife() so we don't pay for
		// the load until the player actually has a knife equipped.
		_knifeRoot = new GameObject( true, "Knife" );
		_knifeRenderer = _knifeRoot.Components.Create<SkinnedModelRenderer>();
		_knifeRenderer.RenderOptions.Overlay = UseOverlayRender;
		_knifeRenderer.RenderOptions.Game = !UseOverlayRender;
		_knifeRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
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

		if ( !_firstPreRenderLogged )
		{
			_firstPreRenderLogged = true;
			Log.Info( $"[FpsViewmodel] First OnPreRender — viewmodel active, camera at {_camera.WorldPosition}, knife equipped={knife?.Id ?? "none"}" );
		}

		// Subtle idle sway — three uncorrelated sines on each axis gives a
		// natural "breathing" motion to the viewmodel without ever drifting
		// from the camera. Amplitude is small (≈0.15u) so it reads as "alive"
		// rather than "shaky cam".
		var t = (float)Time.Now;
		var idleSway = new Vector3(
			MathF.Sin( t * 1.4f )       * 0.15f,
			MathF.Sin( t * 1.8f + 1.1f) * 0.12f,
			MathF.Sin( t * 2.2f + 2.3f) * 0.10f
		);

		// One-shot bone dump so we can see exactly what the arms rig exposes
		// and pick the right grip bone if our default 'hold_R' isn't there.
		if ( _dumpBonesPending && _arms.IsValid() && _arms.Model is not null && _arms.Model.BoneCount > 0 )
		{
			_dumpBonesPending = false;
			var names = new System.Collections.Generic.List<string>();
			int n = _arms.Model.BoneCount;
			for ( int i = 0; i < n; i++ )
			{
				var o = _arms.GetBoneObject( i );
				if ( o.IsValid() ) names.Add( $"{i}:{o.Name}" );
			}
			Log.Info( $"[FpsViewmodel] Arms rig bones ({n}): {string.Join( ", ", names )}" );
		}

		// Anchor to camera with the configured local tweak + idle sway.
		_viewmodel.WorldPosition = _camera.WorldPosition;
		_viewmodel.WorldRotation = _camera.WorldRotation * ViewModelRotation.ToRotation();
		_viewmodel.LocalPosition += _viewmodel.WorldRotation * (ViewModelOffset + idleSway);
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

	/// <summary>
	/// Resolve the real .vmdl path for a given knife id. Falls back to the m9
	/// bayonet (only one we can guarantee is mounted via the package reference
	/// in the .sbproj) for any id we don't have a specific model for yet.
	/// </summary>
	private static string ModelPathFor( string knifeId ) => knifeId switch
	{
		"butterfly"        => "models/butterflyknife/butterfly_knife.vmdl",
		"bayonet"          => "models/weapons/v_m9_bayonet_knife.vmdl",
		_                  => "models/weapons/v_m9_bayonet_knife.vmdl",
	};

	/// <summary>Place the knife at the arms' hand bone with the configured local offset.</summary>
	private void UpdateKnife( KnifeSkin knife )
	{
		if ( !_knifeRoot.IsValid() ) return;

		// No knife equipped → hide.
		if ( knife is null )
		{
			_knifeRoot.Enabled = false;
			_knifeLoadedId = null;
			return;
		}

		var handGo = ResolveHandBone();
		if ( !handGo.IsValid() )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		// Swap the knife model only when the equipped knife changes — Model.Load
		// isn't cheap and we don't want to thrash it every frame.
		if ( _knifeLoadedId != knife.Id )
		{
			var path = ModelPathFor( knife.Id );
			var m = Model.Load( path );
			if ( m is null || m.BoneCount == 0 )
			{
				Log.Warning( $"[FpsViewmodel] Knife model '{path}' did not resolve (BoneCount={m?.BoneCount ?? 0}). Make sure the package reference is in RUNNER.sbproj." );
				_knifeRoot.Enabled = false;
				return;
			}
			_knifeRenderer.Model = m;
			_knifeLoadedId = knife.Id;
			Log.Info( $"[FpsViewmodel] Loaded knife model '{path}' for '{knife.Id}' (BoneCount={m.BoneCount})" );
		}

		_knifeRoot.Enabled = true;

		var localRot = KnifeLocalRotation.ToRotation();
		_knifeRoot.WorldRotation = handGo.WorldRotation * localRot;
		_knifeRoot.WorldPosition = handGo.WorldPosition + handGo.WorldRotation * KnifeLocalOffset;
		_knifeRoot.WorldScale = Vector3.One;

		// Subtle tint by rarity so each skin still reads differently. Pure white
		// for common — the underlying material handles the metallic look.
		_knifeRenderer.Tint = TintFor( knife.Rarity );
	}

	private GameObject _resolvedHandBone;
	private string _resolvedHandBoneName;

	private GameObject ResolveHandBone()
	{
		if ( _resolvedHandBone.IsValid() )
			return _resolvedHandBone;

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
				{
					_resolvedHandBone = o;
					_resolvedHandBoneName = c;
					Log.Info( $"[FpsViewmodel] Resolved hand bone '{c}' at index {i}" );
					return o;
				}
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
		// Fire on BOTH the arms and the knife — whichever rig has the matching
		// animgraph param will play. Different vmdl ship with different
		// trigger names, so we splat the common ones.
		string[] triggers = { "b_deploy", "b_inspect", "b_attack_inspect", "b_holster" };
		if ( _arms.IsValid() )
		{
			foreach ( var p in triggers )
				_arms.Set( p, true );
		}
		if ( _knifeRenderer.IsValid() )
		{
			foreach ( var p in triggers )
				_knifeRenderer.Set( p, true );
		}

		var handle = InspectSound is not null
			? Sound.Play( InspectSound )
			: Sound.Play( "sounds/kenney/ui/ui.button.press.sound" );
		if ( handle is not null )
			handle.Volume *= Runner.Config.UserSettings.Volume;
	}

	// Subtle rarity tint blended onto the real material — keeps the metal feel.
	// Common = pure white = pristine PBR material. Higher rarities get a colored
	// metallic wash so each skin is recognizable without ruining the realism.
	private static Color TintFor( KnifeRarity r ) => r switch
	{
		KnifeRarity.Common   => Color.White,
		KnifeRarity.Uncommon => new Color( 0.78f, 1.00f, 0.85f ),
		KnifeRarity.Rare     => new Color( 0.78f, 0.88f, 1.00f ),
		KnifeRarity.Epic     => new Color( 0.95f, 0.78f, 1.00f ),
		KnifeRarity.Mythic   => new Color( 1.00f, 0.80f, 0.65f ),
		KnifeRarity.Secret   => new Color( 1.00f, 0.92f, 0.65f ),
		_ => Color.White
	};
}
