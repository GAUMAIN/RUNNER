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

		// Ensure an FpsViewmodel component exists on the local player so the
		// 1st-person arms + knife render properly. Proxies skip this — they
		// don't render a viewmodel for other players.
		if ( !IsProxy && GameObject.Components.Get<FpsViewmodel>() is null )
		{
			GameObject.Components.Create<FpsViewmodel>();
		}

		// Ambient sound loops per zone (Hub/L1/L2/L3/L4) — also local-owner
		// only. Each client computes their own zone based on their player's
		// world position, no networking needed.
		if ( !IsProxy && GameObject.Components.Get<Runner.Audio.AmbientZoneAudio>() is null )
		{
			GameObject.Components.Create<Runner.Audio.AmbientZoneAudio>();
		}
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

			// Hide our own body in 1st person so the camera isn't inside the head.
			if ( BodyRenderer.IsValid() )
				BodyRenderer.RenderType = FirstPerson
					? ModelRenderer.ShadowRenderType.ShadowsOnly
					: ModelRenderer.ShadowRenderType.On;
		}

		RotateBodyToVelocity();
		DriveCitizenAnimation();
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
	}

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
