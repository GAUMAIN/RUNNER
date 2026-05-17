using System;
using System.Threading.Tasks;
using Sandbox;
using Runner.Backend;
using Runner.Config;
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

	/// <summary>Persistent coin balance. Awarded only by reaching a cash-out pad.</summary>
	[Sync] public long Coins { get; set; }

	// ── Persistence ──────────────────────────────────────────────────────────
	private static readonly IProfileRepository Repo = new LocalProfileRepository();

	private ulong _profileId;
	private bool _profileLoaded;
	private bool _dirty;
	private TimeSince _timeSinceSave;

	// ── Derived (don't store, always compute) ────────────────────────────────
	public float SpeedMultiplier
		=> SpeedCurve.MultiplierAtLevel( Level, BaseSpeedMultiplier, SpeedGainPerLevel );

	public (long CurrentInLevel, long NeededForNext) GetLevelProgress()
	{
		var (_, current, needed) = XpCurve.ComputeLevel( Xp, BaseXpPerLevel, XpGrowth );
		return (current, needed);
	}

	// ── Lifecycle: load on enable, save on disable ───────────────────────────

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( IsProxy )
			return;

		_profileId = ResolveProfileId();
		_ = LoadProfileAsync();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if ( IsProxy )
			return;

		if ( _profileLoaded && _dirty )
			_ = SaveProfileAsync();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy )
			return;
		if ( !_profileLoaded || !_dirty )
			return;
		if ( _timeSinceSave < GameConfig.ProfileSaveThrottleSeconds )
			return;

		_ = SaveProfileAsync();
	}

	private async Task LoadProfileAsync()
	{
		try
		{
			var profile = await Repo.LoadAsync( _profileId );
			if ( profile is not null )
			{
				Xp = profile.Xp;
				Level = Math.Max( 1, profile.Level );
				Coins = profile.Currencies != null && profile.Currencies.TryGetValue( "coins", out var c ) ? c : 0L;
				Log.Info( $"[Runner] Profile loaded — Lvl {Level} · {Xp} XP · {Coins} coins" );
			}
			else
			{
				Log.Info( $"[Runner] No saved profile, starting fresh." );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Runner] Profile load threw: {ex.Message}" );
		}
		finally
		{
			_profileLoaded = true;
		}
	}

	private async Task SaveProfileAsync()
	{
		try
		{
			var profile = new PlayerProfile
			{
				PlayerId = _profileId,
				Xp = Xp,
				Level = Level,
				LastSeenAt = DateTime.UtcNow,
			};
			profile.Currencies["coins"] = Coins;
			await Repo.SaveAsync( profile );
			_dirty = false;
			_timeSinceSave = 0;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Runner] Profile save threw: {ex.Message}" );
		}
	}

	private ulong ResolveProfileId()
	{
		// Single-player / local: bucket everything under id 0 (=> "local").
		// Multiplayer: wire to Network.Owner.SteamId once the session layer exists.
		return 0UL;
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
		_dirty = true;

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

	// ── Coins (persistent, banked only at cash-out pads) ─────────────────────

	public void GrantCoins( long amount )
	{
		if ( IsProxy )
			return;
		if ( amount <= 0 )
			return;

		Coins += amount;
		_dirty = true;
		EventBus.Publish( new PlayerCurrencyChanged( SteamId(), "coins", amount, Coins ) );
		Log.Info( $"[Runner] +{amount} coins (total: {Coins})" );
	}
}
