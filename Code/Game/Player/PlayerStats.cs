using Sandbox;
using Runner.Data;
using Runner.Events;
using Runner.Systems;

namespace Runner.Player;

/// <summary>
/// Per-player progression state. Server-authoritative; replicated to owner.
/// XP is the truth — Level is derived from XP each time it's queried.
/// </summary>
public sealed class PlayerStats : Component
{
	// ── Tunables (designer-editable in inspector) ────────────────────────────
	[Property] public long BaseXpPerLevel { get; set; } = 100;
	[Property] public float XpGrowth { get; set; } = 1.10f;
	[Property] public float BaseSpeedMultiplier { get; set; } = 1f;
	[Property] public float SpeedGainPerLevel { get; set; } = 0.06f;

	/// <summary>How many run-units travelled grants 1 XP. Lower = faster leveling.</summary>
	[Property] public float UnitsPerXp { get; set; } = 30f;

	// ── Replicated state (server writes, all read) ───────────────────────────
	[Sync] public long Xp { get; set; }
	[Sync] public int Level { get; set; } = 1;

	// ── Derived (don't store, always compute) ────────────────────────────────
	public float SpeedMultiplier
		=> SpeedCurve.MultiplierAtLevel( Level, BaseSpeedMultiplier, SpeedGainPerLevel );

	public (long CurrentInLevel, long NeededForNext) GetLevelProgress()
	{
		var (_, current, needed) = XpCurve.ComputeLevel( Xp, BaseXpPerLevel, XpGrowth );
		return (current, needed);
	}

	// ── XP gain ──────────────────────────────────────────────────────────────

	// Sub-XP fractional accumulator. Each tick adds a few units; we only commit
	// whole XP once the accumulator crosses UnitsPerXp. Without this, integer
	// truncation per-tick would drop every grant to 0.
	private float _runUnitsAccumulator;

	/// <summary>Convert distance run into XP. Called by PlayerPawn per fixed tick while running.</summary>
	public void GrantXpForDistance( float units )
	{
		if ( IsProxy )
			return;
		if ( units <= 0f || UnitsPerXp <= 0f )
			return;

		_runUnitsAccumulator += units;
		if ( _runUnitsAccumulator < UnitsPerXp )
			return;

		long xp = (long)(_runUnitsAccumulator / UnitsPerXp);
		_runUnitsAccumulator -= xp * UnitsPerXp;

		if ( xp > 0 )
			GrantXp( xp, "run" );
	}

	public void GrantXp( long amount, string reason )
	{
		if ( IsProxy )
			return;
		if ( amount <= 0 )
			return;

		int oldLevel = Level;
		Xp += amount;

		var (newLevel, _, _) = XpCurve.ComputeLevel( Xp, BaseXpPerLevel, XpGrowth );
		Level = newLevel;

		EventBus.Publish( new PlayerXpGranted( SteamId(), amount, reason ) );

		if ( newLevel > oldLevel )
		{
			Log.Info( $"[Runner] LEVEL UP! {oldLevel} → {newLevel} (×{SpeedMultiplier:0.00} speed)" );
			EventBus.Publish( new PlayerLeveledUp( SteamId(), newLevel, oldLevel ) );
		}
	}

	private ulong SteamId()
	{
		var conn = Network.Owner;
		return conn is null ? 0UL : conn.SteamId;
	}
}
