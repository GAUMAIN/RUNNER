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
	// Tag the platform GameObjects with one of these surface tags. We look at
	// the hit GameObject (or any ancestor up 8 levels) and the first matching
	// tag wins. Untagged platforms fall through to the engine's surface
	// SoundCollection, then to FallbackStepSound.
	//
	//   "surface_grass"     → Hub + Level 1
	//   "surface_sand"      → Level 2
	//   "surface_concrete"  → reserved
	//   "surface_wood"      → reserved
	//   "surface_metal"     → reserved
	//   "surface_glass"     → reserved
	//
	// Each property below has a hardcoded fallback path so the sound plays
	// out of the box; assign a SoundEvent in Inspector to override.

	[Property, Group( "Level Step Sounds" )] public SoundEvent GrassStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent SandStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent ConcreteStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent WoodStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent MetalStepSound { get; set; }
	[Property, Group( "Level Step Sounds" )] public SoundEvent GlassStepSound { get; set; }

	// Hardcoded fallback paths — S&box ships these in the surfaces/sounds tree.
	// "Sand" uses wood as a substitute since no sand footsteps asset exists in
	// the base install; swap if you install a sand pack later.
	private const string GrassPath    = "surfaces/sounds/grass/grass_footsteps.sound";
	private const string SandPath     = "surfaces/sounds/wood/wood_footsteps.sound";
	private const string ConcretePath = "surfaces/sounds/footsteps_concrete.sound";
	private const string WoodPath     = "surfaces/sounds/wood/wood_footsteps.sound";
	private const string MetalPath    = "surfaces/sounds/metallic/metal_footsteps.sound";
	private const string GlassPath    = "surfaces/sounds/glass/glass_sheet_footsteps.sound";

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

		var stepPos = tr.HitPosition + tr.Normal * 5f;

		// 1. Per-surface tag override. If a tag matches, play the assigned
		//    SoundEvent or fall back to the hardcoded path string — Sound.Play
		//    accepts either, and a string load goes through the engine's
		//    runtime resource resolver which DOES find these even when the
		//    SoundEvent ResourceLibrary lookup returns null.
		if ( tr.GameObject.IsValid()
			&& TryPlayTaggedSurfaceSound( tr.GameObject, stepPos ) )
		{
			_useLeftFoot = !_useLeftFoot;
			return;
		}

		// 2. Engine's built-in surface SoundCollection (left/right alternation).
		SoundEvent sound = null;
		if ( tr.Surface?.SoundCollection != null )
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

		var handle = Sound.Play( sound, stepPos );
		if ( handle is not null )
			handle.Volume *= Volume;
	}

	/// <summary>
	/// Walk the ancestor chain looking for a "surface_*" tag. If we find one,
	/// play the matching SoundEvent (Inspector-assigned) or the hardcoded path
	/// string fallback. Returns true if a sound was played.
	/// </summary>
	private bool TryPlayTaggedSurfaceSound( GameObject go, Vector3 stepPos )
	{
		var current = go;
		int depth = 0;
		while ( current.IsValid() && depth < 8 )
		{
			if ( current.Tags.Has( "surface_grass" ) )    return PlaySurface( GrassStepSound,    GrassPath,    "grass",    stepPos );
			if ( current.Tags.Has( "surface_sand" ) )     return PlaySurface( SandStepSound,     SandPath,     "sand",     stepPos );
			if ( current.Tags.Has( "surface_concrete" ) ) return PlaySurface( ConcreteStepSound, ConcretePath, "concrete", stepPos );
			if ( current.Tags.Has( "surface_wood" ) )     return PlaySurface( WoodStepSound,     WoodPath,     "wood",     stepPos );
			if ( current.Tags.Has( "surface_metal" ) )    return PlaySurface( MetalStepSound,    MetalPath,    "metal",    stepPos );
			if ( current.Tags.Has( "surface_glass" ) )    return PlaySurface( GlassStepSound,    GlassPath,    "glass",    stepPos );

			current = current.Parent;
			depth++;
		}

		// Diagnostic: log what got hit and what tags it actually has, once
		// per unique GameObject. Helps figure out why a tag isn't being seen.
		if ( !_loggedHits.Contains( go.Id ) )
		{
			_loggedHits.Add( go.Id );
			Log.Info( $"[FootstepSystem] Trace hit '{go.Name}' (id={go.Id}) tags={go.Tags} — no surface_* tag matched." );
		}
		return false;
	}

	private readonly System.Collections.Generic.HashSet<Guid> _loggedHits = new();

	private bool _surfaceDebugLogged;

	private bool PlaySurface( SoundEvent assigned, string fallbackPath, string label, Vector3 pos )
	{
		var handle = assigned is not null
			? Sound.Play( assigned, pos )
			: Sound.Play( fallbackPath, pos );

		if ( handle is null )
		{
			if ( !_surfaceDebugLogged )
			{
				_surfaceDebugLogged = true;
				Log.Warning( $"[FootstepSystem] '{label}' sound failed to play (assigned={assigned}, path={fallbackPath})." );
			}
			return false;
		}

		handle.Volume *= Volume;
		return true;
	}

}
