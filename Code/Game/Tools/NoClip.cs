using Sandbox;

namespace Runner.Tools;

/// <summary>
/// Dev free-fly camera control. Attach to anything with a camera-aligned transform.
/// Adapted from Facepunch/sbox-scenestaging NoClip.
/// </summary>
public sealed class NoClip : Component
{
	[Property] public float MoveSpeed { get; set; } = 600f;
	[Property] public float BoostMultiplier { get; set; } = 3f;

	private Angles _eyeAngles;

	protected override void OnStart()
	{
		_eyeAngles = WorldRotation;
	}

	protected override void OnUpdate()
	{
		_eyeAngles += Input.AnalogLook;
		_eyeAngles.pitch = _eyeAngles.pitch.Clamp( -89f, 89f );
		_eyeAngles.roll = 0;
		WorldRotation = _eyeAngles;

		var movement = Input.AnalogMove;
		if ( movement.IsNearlyZero() )
			return;

		float speed = MoveSpeed;
		if ( Input.Down( "Run" ) )
			speed *= BoostMultiplier;

		WorldPosition += WorldRotation * movement.Normal * Time.Delta * speed;
	}
}
