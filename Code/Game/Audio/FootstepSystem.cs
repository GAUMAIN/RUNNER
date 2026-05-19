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

	// ─── Per-level / per-surface step sounds ──────────────────────────────
	// Tag the platform GameObjects with one of these surface tags and the
	// matching SoundEvent below wins over the engine's surface SoundCollection.
	//
	//   "surface_concrete"  → Level 1 (hub + level 1 plats)
	//   "surface_wood"      → Level 2
	//   "surface_metal"     → Level 3
	//   "surface_glass"     → Level 4 (moving plats / future mechanics)
	//
	// Untagged platforms fall back to the engine's surface sound, then to
	// FallbackStepSound. So you can tag incrementally — anything you haven't
	// touched keeps the original step sound.

	[Property, Group( "Level Step Sounds" )] public SoundEvent ConcreteStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent WoodStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent MetalStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent GlassStepSound { get; set; }

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

		// 1. Per-surface tag override on the hit GameObject (or any of its
		//    ancestors so a tagged Level container also wins).
		if ( tr.GameObject.IsValid() )
			sound = ResolveSurfaceSoundByTag( tr.GameObject );

		// 2. Engine's built-in surface SoundCollection (left/right alternation).
		if ( sound is null && tr.Surface?.SoundCollection != null )
		{
			sound = _useLeftFoot
				? tr.Surface.SoundCollection.FootLeft
				: tr.Surface.SoundCollection.FootRight;
		}

		// 3. Global fallback.
		sound ??= FallbackStepSound;
		_useLeftFoot = !_useLeftFoot;

		if ( sound is null )
			return;

		var handle = Sound.Play( sound, tr.HitPosition + tr.Normal * 5f );
		if ( handle is not null )
			handle.Volume *= Volume;
	}

	/// <summary>
	/// Walk up the GameObject's ancestor chain looking for a "surface_*" tag.
	/// Lets you tag either an individual platform or a whole level container
	/// and get the right step sound either way.
	/// </summary>
	private SoundEvent ResolveSurfaceSoundByTag( GameObject go )
	{
		var current = go;
		int depth = 0;
		while ( current.IsValid() && depth < 8 )
		{
			if ( current.Tags.Has( "surface_concrete" ) ) return ConcreteStepSound;
			if ( current.Tags.Has( "surface_wood" ) )     return WoodStepSound;
			if ( current.Tags.Has( "surface_metal" ) )    return MetalStepSound;
			if ( current.Tags.Has( "surface_glass" ) )    return GlassStepSound;

			current = current.Parent;
			depth++;
		}
		return null;
	}
}
