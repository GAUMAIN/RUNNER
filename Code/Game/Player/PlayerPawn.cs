using Sandbox;
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
	[Property] public bool FirstPerson { get; set; } = true;

	/// <summary>Walk speed in units / second.</summary>
	[Property] public float WalkSpeed { get; set; } = 110f;

	/// <summary>Run speed in units / second (pre-multipliers).</summary>
	[Property] public float RunSpeed { get; set; } = GameConfig.BaseRunSpeed;

	/// <summary>Jump impulse magnitude.</summary>
	[Property] public float JumpStrength { get; set; } = 322f;

	/// <summary>Falling below this world Z respawns the player instantly.</summary>
	[Property] public float DeathZ { get; set; } = -200f;

	public Vector3 WishVelocity { get; private set; }

	[Sync] public Angles EyeAngles { get; set; }
	[Sync] public bool IsSprinting { get; set; }

	private Vector3 _spawnPosition;
	private bool _spawnCaptured;

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
		}

		RotateBodyToVelocity();
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

		if ( cc.IsOnGround && Input.Pressed( "Jump" ) )
		{
			cc.Punch( Vector3.Up * JumpStrength );
		}

		if ( cc.IsOnGround )
		{
			cc.Velocity = cc.Velocity.WithZ( 0 );
			cc.Accelerate( WishVelocity );
			cc.ApplyFriction( 4.0f );
		}
		else
		{
			cc.Velocity -= Gravity * Time.Delta * 0.5f;
			cc.Accelerate( WishVelocity.ClampLength( 50 ) );
			cc.ApplyFriction( 0.1f );
		}

		cc.Move();

		if ( !cc.IsOnGround )
			cc.Velocity -= Gravity * Time.Delta * 0.5f;
		else
			cc.Velocity = cc.Velocity.WithZ( 0 );

		ClampToAntiCheatCeiling( cc );
		GrantRunXp( cc );
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
			Body.WorldRotation = Rotation.Lerp( Body.WorldRotation, targetAngle, Time.Delta * 2.0f );
	}

	private static void ClampToAntiCheatCeiling( CharacterController cc )
	{
		float maxSq = GameConfig.MaxRunSpeedAbsolute * GameConfig.MaxRunSpeedAbsolute;
		if ( cc.Velocity.LengthSquared > maxSq )
			cc.Velocity = cc.Velocity.Normal * GameConfig.MaxRunSpeedAbsolute;
	}
}
