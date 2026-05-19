using System;
using Sandbox;

namespace Runner.World;

/// <summary>
/// Oscillates the host GameObject back and forth between two extremes along a
/// configurable local axis. Used for Level 4's "platform that moves" obstacle —
/// the player has to time their jump to land on the platform while it's at a
/// reachable position.
///
/// Implementation notes:
/// - We capture the spawn world position in <see cref="OnEnabled"/> and
///   oscillate around it, so dropping the GameObject anywhere in the scene
///   gives you a moving platform without further wiring.
/// - Movement is a pure sine wave on Time.Now → smooth, symmetric, network-
///   safe (everyone runs the same clock).
/// - We do NOT attempt to carry the player. The CharacterController doesn't
///   parent itself to moving platforms by default; landing on a moving plat
///   and staying on it is part of the timing challenge.
/// </summary>
public sealed class MovingPlatform : Component
{
	/// <summary>How far (in units) the platform travels from its rest position
	/// in each direction along <see cref="Direction"/>.</summary>
	[Property, Range( 10f, 1000f )] public float Amplitude { get; set; } = 250f;

	/// <summary>Time in seconds for a full back-and-forth cycle.</summary>
	[Property, Range( 0.5f, 20f )] public float Period { get; set; } = 4f;

	/// <summary>
	/// Travel direction in WORLD space. Normalized internally.
	/// Default is +Y (sideways, perpendicular to the typical run direction +X).
	/// Set to (1,0,0) for forward/back motion, (0,0,1) for up/down (vertical
	/// elevator).
	/// </summary>
	[Property] public Vector3 Direction { get; set; } = new Vector3( 0, 1, 0 );

	/// <summary>Phase offset in seconds — lets you stagger multiple platforms
	/// so they don't all hit max travel at the same instant.</summary>
	[Property, Range( 0f, 20f )] public float PhaseOffset { get; set; } = 0f;

	private Vector3 _basePosition;
	private bool _baseCaptured;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_basePosition = WorldPosition;
		_baseCaptured = true;
	}

	protected override void OnUpdate()
	{
		if ( !_baseCaptured )
			return;

		if ( Period <= 0f )
			return;

		var dir = Direction.IsNearZeroLength ? Vector3.Up : Direction.Normal;
		var phase = MathF.Sin( (float)((Time.Now + PhaseOffset) / Period) * MathF.PI * 2f );
		WorldPosition = _basePosition + dir * Amplitude * phase;
	}
}
