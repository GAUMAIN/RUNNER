using System;
using Sandbox;
using Sandbox.Citizen;
using System.Linq;
using Runner.Config;

namespace Runner.Player;

/// <summary>
/// First-pass player pawn for RUNNER.
/// Server-authoritative movement via CharacterController; owner predicts locally.
///
/// Adapted from Facepunch/sbox-scenestaging PlayerController (MIT-spirit reference repo).
/// Replaced hardcoded speeds with values from <see cref="GameConfig"/>.
/// Speed curve, dash, momentum, FOV scaling and prediction layers are added in later milestones (M1+).
/// </summary>
public sealed class PlayerPawn : Component
{
	[Property] public Vector3 Gravity { get; set; } = new Vector3( 0, 0, 800 );

	[Property] public GameObject Body { get; set; }
	[Property] public GameObject Eye { get; set; }
	[Property] public CitizenAnimationHelper AnimationHelper { get; set; }
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	[Property] public bool FirstPerson { get; set; } = true;

	/// <summary>Walk speed in units / second.</summary>
	[Property] public float WalkSpeed { get; set; } = 110f;

	/// <summary>Run speed in units / second (pre-multipliers).</summary>
	[Property] public float RunSpeed { get; set; } = GameConfig.BaseRunSpeed;

	/// <summary>Jump impulse magnitude (Source/CS2 default ≈ 268.3).</summary>
	[Property] public float JumpStrength { get; set; } = 268.3f;

	/// <summary>Falling below this world Z respawns the player instantly.</summary>
	[Property] public float DeathZ { get; set; } = -200f;

	// ── CS2 / Source-style movement tunables ─────────────────────────────────

	/// <summary>sv_accelerate equivalent. Higher = snappier ground response. CS2 ≈ 5.5.</summary>
	[Property, Range( 1f, 20f )] public float GroundAccelRate { get; set; } = 5.5f;

	/// <summary>sv_friction equivalent. Higher = sharper stops. CS2 ≈ 5.2.</summary>
	[Property, Range( 1f, 20f )] public float GroundFriction { get; set; } = 5f;

	/// <summary>sv_airaccelerate equivalent. CS2 ≈ 12 — we run higher for more responsive air control.</summary>
	[Property, Range( 1f, 100f )] public float AirAccelRate { get; set; } = 28f;

	/// <summary>Max wish-speed cap while airborne. Higher = more direct strafe control. CS2 default 30.</summary>
	[Property] public float MaxAirWishSpeed { get; set; } = 120f;

	/// <summary>sv_stopspeed equivalent. Below this speed friction uses this value as the multiplier baseline — gives the snappy counter-strafe stop.</summary>
	[Property] public float StopSpeed { get; set; } = 100f;

	public Vector3 WishVelocity { get; private set; }

	[Sync] public Angles EyeAngles { get; set; }
	[Sync] public bool IsSprinting { get; set; }

	private Vector3 _spawnPosition;
	private bool _spawnCaptured;
	private float _bodyRotationSpeed;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( IsProxy )
			return;

		var cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( cam.IsValid() )
		{
			var ee = cam.WorldRotation.Angles();
			ee.roll = 0;
			EyeAngles = ee;
		}
	}

	protected override void OnStart()
	{
		base.OnStart();
		_spawnPosition = WorldPosition;
		_spawnCaptured = true;

		// Required so we can find + manipulate the head bone in FPS (hide it
		// so the camera isn't staring at the inside of the citizen's skull).
		if ( BodyRenderer.IsValid() )
			BodyRenderer.CreateBoneObjects = true;
	}

	/// <summary>Instant TP back to the captured spawn point. Called on death-by-fall and by cash-out pads.</summary>
	public void Respawn()
	{
		if ( !_spawnCaptured )
			return;

		WorldPosition = _spawnPosition;
		var cc = GameObject.Components.Get<CharacterController>();
		if ( cc.IsValid() )
			cc.Velocity = Vector3.Zero;
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy )
		{
			HandleLookInput();
			DriveCamera();
			IsSprinting = Input.Down( "Run" );

			// Toggle 1st / 3rd person with the View key (C by default).
			if ( !Mouse.Visible && Input.Pressed( "View" ) )
				FirstPerson = !FirstPerson;

			// In FPS we KEEP the body rendering (so the arms holding the knife are
			// visible) — the head itself is hidden in OnPreRender, after the
			// animgraph has finished computing bone transforms.
			if ( BodyRenderer.IsValid() )
				BodyRenderer.RenderType = ModelRenderer.ShadowRenderType.On;
		}

		RotateBodyToVelocity();
		DriveCitizenAnimation();
	}

	// Cached bones that need to be shrunk to nothing in 1st person so the
	// camera doesn't see the inside of the skull. Resolved lazily once
	// CitizenAnimationHelper has driven the rig at least one frame.
	private int[] _headBoneIndices;

	/// <summary>
	/// Runs after the animation graph has computed bone transforms for the frame,
	/// so any scale we write here actually sticks for rendering instead of being
	/// overwritten by the animation pass.
	/// </summary>
	protected override void OnPreRender()
	{
		if ( IsProxy ) return;
		ApplyHeadVisibility( hide: FirstPerson );
	}

	/// <summary>
	/// Hide / show the citizen's head by both scaling the head-related bones to
	/// nothing AND shoving them far below the world. Scale alone wasn't enough on
	/// the Citizen rig — the collapsed verts ended up exactly where the camera
	/// sits, so we still saw "inside the head". Shoving them down moves those
	/// verts out of the view frustum entirely.
	/// </summary>
	private void ApplyHeadVisibility( bool hide )
	{
		if ( !BodyRenderer.IsValid() || BodyRenderer.Model is null )
			return;

		// Resolve head bone indices once with broad name matching — any bone whose
		// name contains "head", "eye", "jaw", "tooth" or "teeth" is fair game.
		if ( _headBoneIndices is null )
		{
			var found = new System.Collections.Generic.List<int>();
			int n = BodyRenderer.Model.BoneCount;
			for ( int i = 0; i < n; i++ )
			{
				var o = BodyRenderer.GetBoneObject( i );
				if ( !o.IsValid() ) continue;
				var name = o.Name?.ToLowerInvariant() ?? string.Empty;
				if ( name.Contains( "head" )
					|| name.Contains( "eye" )
					|| name.Contains( "jaw" )
					|| name.Contains( "tooth" )
					|| name.Contains( "teeth" )
					|| name.Contains( "mouth" )
					|| name.Contains( "ear_" )
					|| name == "neck_0" || name == "neck_1" )
				{
					found.Add( i );
				}
			}
			_headBoneIndices = found.ToArray();
			Log.Info( $"[PlayerPawn] Cached {_headBoneIndices.Length} head/face bones for FPS hide." );
		}

		if ( hide )
		{
			var sink = new Vector3( 0, 0, -10000f );
			foreach ( var i in _headBoneIndices )
			{
				var o = BodyRenderer.GetBoneObject( i );
				if ( !o.IsValid() ) continue;
				o.LocalScale = Vector3.Zero;
				o.WorldPosition = sink; // far below the world — verts follow
			}
		}
		else
		{
			// In TPS, just restore scale — the animation pass will pull the
			// positions back to their bind/anim values automatically.
			foreach ( var i in _headBoneIndices )
			{
				var o = BodyRenderer.GetBoneObject( i );
				if ( o.IsValid() ) o.LocalScale = Vector3.One;
			}
		}
	}

	/// <summary>Feed the citizen animgraph with current state so it actually animates.</summary>
	private void DriveCitizenAnimation()
	{
		if ( !AnimationHelper.IsValid() )
			return;

		var cc = GameObject.Components.Get<CharacterController>();
		if ( !cc.IsValid() )
			return;

		AnimationHelper.WithVelocity( cc.Velocity );
		AnimationHelper.WithWishVelocity( WishVelocity );
		AnimationHelper.IsGrounded = cc.IsOnGround;
		AnimationHelper.MoveRotationSpeed = _bodyRotationSpeed;
		AnimationHelper.WithLook( EyeAngles.Forward, 1f, 1f, 1f );
		AnimationHelper.MoveStyle = IsSprinting
			? CitizenAnimationHelper.MoveStyles.Run
			: CitizenAnimationHelper.MoveStyles.Walk;

		// Knife = melee, so the citizen holds arms forward in a swing-ready stance
		// (instead of arms relaxed at the sides). This is what makes the held
		// knife visible from FPS at all.
		AnimationHelper.HoldType = CitizenAnimationHelper.HoldTypes.Swing;
		AnimationHelper.Handedness = CitizenAnimationHelper.Hand.Right;

		// Inspect — F triggers the citizen's deploy animation, which raises the
		// knife up. Throttled so spamming F doesn't cancel itself.
		if ( !Runner.Config.UserSettings.IsPaused
			&& _timeSinceDeploy > DeployCooldown
			&& Input.Pressed( "Inspect" ) )
		{
			AnimationHelper.TriggerDeploy();
			_timeSinceDeploy = 0f;
		}
	}

	private TimeSince _timeSinceDeploy = 999f;
	private const float DeployCooldown = 1.0f;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;

		// Pause menu open → freeze movement input (don't accumulate velocity).
		if ( UserSettings.IsPaused )
			return;

		// Fell into the void — respawn instantly, skip movement this tick.
		if ( _spawnCaptured && WorldPosition.z < DeathZ )
		{
			Respawn();
			return;
		}

		var cc = GameObject.Components.Get<CharacterController>();
		if ( !cc.IsValid() )
			return;

		BuildWishVelocity();

		var wishDir = WishVelocity.IsNearZeroLength ? Vector3.Zero : WishVelocity.Normal;
		float wishSpeed = WishVelocity.Length;

		if ( cc.IsOnGround && Input.Pressed( "Jump" ) )
		{
			cc.Punch( Vector3.Up * JumpStrength );
		}

		if ( cc.IsOnGround )
		{
			cc.Velocity = cc.Velocity.WithZ( 0 );
			ApplyFrictionSource( cc, GroundFriction );
			AccelerateSource( cc, wishDir, wishSpeed, GroundAccelRate );
		}
		else
		{
			cc.Velocity -= Gravity * Time.Delta * 0.5f;
			AirAccelerateSource( cc, wishDir, wishSpeed, AirAccelRate );
		}

		cc.Move();

		if ( !cc.IsOnGround )
			cc.Velocity -= Gravity * Time.Delta * 0.5f;
		else
			cc.Velocity = cc.Velocity.WithZ( 0 );

		ClampToAntiCheatCeiling( cc );
		GrantRunXp( cc );
	}

	// ── Source / CS2-style movement primitives ────────────────────────────────
	// Reference: https://developer.valvesoftware.com/wiki/CS_air_acceleration
	// These give snappy ground response + counter-strafe + air-strafe / bhop feel.

	private void ApplyFrictionSource( CharacterController cc, float friction )
	{
		var horizontal = cc.Velocity.WithZ( 0 );
		float speed = horizontal.Length;
		if ( speed < 0.1f )
			return;

		// Below stop_speed, friction uses stop_speed as the baseline → harder stop.
		float control = speed < StopSpeed ? StopSpeed : speed;
		float drop = control * friction * Time.Delta;
		float newSpeed = MathF.Max( 0f, speed - drop );

		float scale = newSpeed / speed;
		cc.Velocity = new Vector3( horizontal.x * scale, horizontal.y * scale, cc.Velocity.z );
	}

	private void AccelerateSource( CharacterController cc, Vector3 wishDir, float wishSpeed, float accel )
	{
		if ( wishSpeed <= 0f )
			return;

		float currentSpeed = cc.Velocity.WithZ( 0 ).Dot( wishDir );
		float addSpeed = wishSpeed - currentSpeed;
		if ( addSpeed <= 0f )
			return;

		float accelSpeed = accel * Time.Delta * wishSpeed;
		if ( accelSpeed > addSpeed )
			accelSpeed = addSpeed;

		cc.Velocity += wishDir * accelSpeed;
	}

	private void AirAccelerateSource( CharacterController cc, Vector3 wishDir, float wishSpeed, float accel )
	{
		if ( wishSpeed <= 0f )
			return;

		// Cap the target velocity along wishDir, but use full wishSpeed for the accel rate (bhop).
		float wishSpd = MathF.Min( wishSpeed, MaxAirWishSpeed );
		float currentSpeed = cc.Velocity.WithZ( 0 ).Dot( wishDir );
		float addSpeed = wishSpd - currentSpeed;
		if ( addSpeed <= 0f )
			return;

		float accelSpeed = accel * Time.Delta * wishSpeed;
		if ( accelSpeed > addSpeed )
			accelSpeed = addSpeed;

		cc.Velocity += wishDir * accelSpeed;
	}

	/// <summary>Convert distance covered while running on the ground into XP.</summary>
	private void GrantRunXp( CharacterController cc )
	{
		if ( !IsSprinting || !cc.IsOnGround )
			return;

		var stats = GameObject.Components.Get<PlayerStats>();
		if ( !stats.IsValid() )
			return;

		float horizontalSpeed = cc.Velocity.WithZ( 0 ).Length;
		float distanceThisTick = horizontalSpeed * Time.Delta;
		stats.GrantXpForDistance( distanceThisTick );
	}

	private void BuildWishVelocity()
	{
		var rot = EyeAngles.ToRotation();
		WishVelocity = rot * Input.AnalogMove;
		WishVelocity = WishVelocity.WithZ( 0 );

		if ( !WishVelocity.IsNearZeroLength )
			WishVelocity = WishVelocity.Normal;

		float speed = Input.Down( "Run" ) ? RunSpeed : WalkSpeed;

		// Apply level-based × shop-based multipliers — long-term speed scaling lives in PlayerStats.
		var stats = GameObject.Components.Get<PlayerStats>();
		if ( stats.IsValid() )
			speed *= stats.TotalSpeedMultiplier;

		WishVelocity *= speed;
	}

	private void HandleLookInput()
	{
		// When a modal UI (Shop, Rebirth, Pause, etc.) frees the cursor, skip the look
		// update so the camera doesn't spin while the user is clicking.
		if ( Mouse.Visible )
			return;

		var ee = EyeAngles;
		ee += Input.AnalogLook * UserSettings.MouseSensitivity;
		ee.pitch = ee.pitch.Clamp( -89f, 89f );
		ee.roll = 0;
		EyeAngles = ee;
	}

	private void DriveCamera()
	{
		var cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( !cam.IsValid() )
			return;

		var lookDir = EyeAngles.ToRotation();

		if ( FirstPerson && Eye.IsValid() )
		{
			cam.WorldPosition = Eye.WorldPosition;
			cam.WorldRotation = lookDir;
		}
		else
		{
			cam.WorldPosition = WorldPosition + lookDir.Backward * 300f + Vector3.Up * 75f;
			cam.WorldRotation = lookDir;
		}
	}

	private void RotateBodyToVelocity()
	{
		_bodyRotationSpeed = 0f;
		if ( !Body.IsValid() )
			return;

		var cc = GameObject.Components.Get<CharacterController>();
		if ( !cc.IsValid() )
			return;

		var targetAngle = new Angles( 0, EyeAngles.yaw, 0 ).ToRotation();
		var v = cc.Velocity.WithZ( 0 );

		if ( v.Length > 10.0f )
			targetAngle = Rotation.LookAt( v, Vector3.Up );

		float rotateDifference = Body.WorldRotation.Distance( targetAngle );
		if ( rotateDifference > 50.0f || cc.Velocity.Length > 10.0f )
		{
			var newRotation = Rotation.Lerp( Body.WorldRotation, targetAngle, Time.Delta * 2.0f );
			var angleDiff = Body.WorldRotation.Angles() - newRotation.Angles();
			_bodyRotationSpeed = angleDiff.yaw / Time.Delta;
			Body.WorldRotation = newRotation;
		}
	}

	private static void ClampToAntiCheatCeiling( CharacterController cc )
	{
		float maxSq = GameConfig.MaxRunSpeedAbsolute * GameConfig.MaxRunSpeedAbsolute;
		if ( cc.Velocity.LengthSquared > maxSq )
			cc.Velocity = cc.Velocity.Normal * GameConfig.MaxRunSpeedAbsolute;
	}
}
