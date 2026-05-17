using Sandbox;

namespace Runner.Audio;

/// <summary>
/// Time-based footstep playback. Doesn't require an animated rig.
/// Runs on every client (owner + proxies) so all players hear each other's steps.
/// The trace finds the ground surface; we use its built-in left/right footstep sounds.
///
/// Interval scales with speed: walking spaces steps further, sprinting tightens them.
/// At higher speeds (post-rebirth multipliers) we hit a hard floor to avoid machine-gun audio.
/// </summary>
public sealed class FootstepSystem : Component
{
	[Property] public float MinSpeedToStep { get; set; } = 30f;

	/// <summary>Seconds between steps when moving at WalkSpeed (~110 u/s).</summary>
	[Property] public float StepIntervalAtWalk { get; set; } = 0.45f;

	/// <summary>Seconds between steps when moving at RunSpeed (~320 u/s).</summary>
	[Property] public float StepIntervalAtRun { get; set; } = 0.28f;

	/// <summary>Hard floor — never play steps faster than this, even at sonic speeds.</summary>
	[Property] public float MinStepInterval { get; set; } = 0.08f;

	[Property, Range( 0f, 2f )] public float Volume { get; set; } = 1f;

	/// <summary>Fallback played when the ground surface has no SoundCollection.</summary>
	[Property] public SoundEvent FallbackStepSound { get; set; }

	private TimeSince _timeSinceStep;
	private bool _useLeftFoot;

	protected override void OnUpdate()
	{
		var cc = GameObject.Components.Get<CharacterController>();
		if ( !cc.IsValid() )
			return;

		if ( !cc.IsOnGround )
			return;

		float speed = cc.Velocity.WithZ( 0 ).Length;
		if ( speed < MinSpeedToStep )
			return;

		float interval = ComputeStepInterval( speed );
		if ( _timeSinceStep < interval )
			return;

		_timeSinceStep = 0f;
		PlayStep();
	}

	private float ComputeStepInterval( float speed )
	{
		// Linear interp from walk pace at 110 u/s to run pace at 320 u/s,
		// then keep tightening proportionally for higher speeds.
		const float walkSpeedRef = 110f;
		const float runSpeedRef = 320f;

		float interval;
		if ( speed <= runSpeedRef )
		{
			float t = ((speed - walkSpeedRef) / (runSpeedRef - walkSpeedRef)).Clamp( 0f, 1f );
			interval = MathX.Lerp( StepIntervalAtWalk, StepIntervalAtRun, t );
		}
		else
		{
			// Post-run scaling: interval shrinks inversely with speed.
			interval = StepIntervalAtRun * (runSpeedRef / speed);
		}

		return interval.Clamp( MinStepInterval, StepIntervalAtWalk );
	}

	private void PlayStep()
	{
		var origin = WorldPosition + Vector3.Up * 8f;
		var end = WorldPosition + Vector3.Down * 40f;

		var tr = Scene.Trace
			.Ray( origin, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !tr.Hit )
			return;

		SoundEvent sound = null;
		if ( tr.Surface?.SoundCollection != null )
		{
			sound = _useLeftFoot
				? tr.Surface.SoundCollection.FootLeft
				: tr.Surface.SoundCollection.FootRight;
		}

		sound ??= FallbackStepSound;
		_useLeftFoot = !_useLeftFoot;

		if ( sound is null )
			return;

		var handle = Sound.Play( sound, tr.HitPosition + tr.Normal * 5f );
		if ( handle is not null )
			handle.Volume *= Volume;
	}
}
