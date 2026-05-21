using System;
using Sandbox;
using Runner.Config;

namespace Runner.Audio;

/// <summary>
/// Per-zone ambient sound loop, crossfaded as the player moves between zones
/// along the run axis (+X).
///
/// Zones are defined by X bands chosen from the existing level layout in
/// testbed.scene:
///   Hub  : x &lt;   200  (Plaza_Hub at -800)
///   L1   : 200 - 3000  (Platform_Start..Platform_End span)
///   L2   : 3000 - 9700 (L2_Start at 3750, L2_End at 9460)
///   L3   : 9700 - 15000 (L3_Start ~10000, L3_End at 14750)
///   L4   : x &gt; 15000 (L4_MovingPlat1 at 15250)
///
/// We play S&amp;box core ambience loops (auto-mounted, no PackageReference
/// needed). The handle for each loop is kept around so we can fade it
/// out smoothly instead of cutting hard.
///
/// Attach to the local player GameObject (PlayerPawn auto-creates it the
/// same way it auto-creates FpsViewmodel).
/// </summary>
public sealed class AmbientZoneAudio : Component
{
	/// <summary>Crossfade duration in seconds.</summary>
	[Property, Range( 0.1f, 10f )] public float FadeDuration { get; set; } = 1.5f;

	/// <summary>Peak volume of an ambient loop (before <see cref="UserSettings.Volume"/>).</summary>
	[Property, Range( 0f, 1f )] public float MaxVolume { get; set; } = 0.45f;

	// ─── Zone definition ──────────────────────────────────────────────────

	private enum Zone { None, Hub, L1, L2, L3, L4 }

	// Path → S&box core ambience loop. Empty string = silence for that zone.
	private static string PathFor( Zone z ) => z switch
	{
		Zone.Hub => "sounds/ambience/forest-day-loop.vsnd",
		Zone.L1  => "sounds/ambience/forest-day-loop.vsnd",
		Zone.L2  => "sounds/ambience/plains-wind-loop.vsnd",
		Zone.L3  => "sounds/ambience/cliffs-wind-loop.vsnd",
		Zone.L4  => "sounds/ambience/cave-loop.vsnd",
		_        => null,
	};

	private static Zone ClassifyByX( float x )
	{
		if ( x < 200f )    return Zone.Hub;
		if ( x < 3000f )   return Zone.L1;
		if ( x < 9700f )   return Zone.L2;
		if ( x < 15000f )  return Zone.L3;
		return Zone.L4;
	}

	// ─── State ────────────────────────────────────────────────────────────

	private Zone _currentZone = Zone.None;
	private SoundHandle _currentHandle;
	private SoundHandle _previousHandle;
	private TimeSince _timeSinceTransition = 999f;

	protected override void OnDisabled()
	{
		base.OnDisabled();
		_currentHandle?.Stop();
		_previousHandle?.Stop();
		_currentHandle = null;
		_previousHandle = null;
	}

	protected override void OnUpdate()
	{
		// Only the local owner runs ambience (we don't network it; each client
		// computes their own zone based on their own player position).
		if ( IsProxy )
			return;

		var newZone = ClassifyByX( WorldPosition.x );
		if ( newZone != _currentZone )
		{
			TransitionTo( newZone );
		}

		UpdateCrossfade();
	}

	private void TransitionTo( Zone next )
	{
		// Stop the old "previous" if there's still one fading out from a prior
		// transition — three-way crossfade is overkill and would pile up handles.
		_previousHandle?.Stop();

		_previousHandle = _currentHandle;
		_currentHandle  = null;

		var path = PathFor( next );
		if ( !string.IsNullOrEmpty( path ) )
		{
			_currentHandle = Sound.Play( path );
			if ( _currentHandle is not null )
				_currentHandle.Volume = 0f;
		}

		_currentZone = next;
		_timeSinceTransition = 0f;
	}

	private void UpdateCrossfade()
	{
		float t = MathX.Clamp( (float)_timeSinceTransition / MathF.Max( FadeDuration, 0.05f ), 0f, 1f );
		float vol = MaxVolume * UserSettings.Volume;

		if ( _currentHandle is not null )
			_currentHandle.Volume = t * vol;

		if ( _previousHandle is not null )
		{
			_previousHandle.Volume = (1f - t) * vol;
			if ( t >= 1f )
			{
				_previousHandle.Stop();
				_previousHandle = null;
			}
		}
	}
}
