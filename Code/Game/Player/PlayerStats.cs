using System;
using System.Threading.Tasks;
using Sandbox;
using Runner.Backend;
using Runner.Config;
using Runner.Data;
using Runner.Economy;
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

	// ── Shop upgrades (persistent, bought from Pad_Shop) ─────────────────────
	[Sync] public int Upgrade_Speed { get; set; }
	[Sync] public int Upgrade_Xp { get; set; }
	[Sync] public int Upgrade_Coin { get; set; }

	/// <summary>Number of rebirths completed. Persistent. Each one grants a flat bonus to all gains.</summary>
	[Sync] public int Rebirths { get; set; }

	// ── Persistence ──────────────────────────────────────────────────────────
	private static readonly IProfileRepository Repo = new LocalProfileRepository();

	private ulong _profileId;
	private bool _profileLoaded;
	private bool _dirty;
	private TimeSince _timeSinceSave;

	// ── Derived (don't store, always compute) ────────────────────────────────
	public float SpeedMultiplier
		=> SpeedCurve.MultiplierAtLevel( Level, BaseSpeedMultiplier, SpeedGainPerLevel );

	/// <summary>Permanent global bonus from every rebirth (1.0 at 0 rebirths, +50% per).</summary>
	public float RebirthBonus => 1f + Rebirths * GameConfig.RebirthBonusPerCount;

	/// <summary>Level-based speed × shop speed × rebirth bonus.</summary>
	public float TotalSpeedMultiplier
		=> SpeedMultiplier * (1f + Upgrade_Speed * 0.05f) * RebirthBonus;

	public float XpGainMultiplier => (1f + Upgrade_Xp * 0.10f) * RebirthBonus;
	public float CoinGainMultiplier => (1f + Upgrade_Coin * 0.10f) * RebirthBonus;

	public int GetUpgradeLevel( UpgradeType type ) => type switch
	{
		UpgradeType.Speed => Upgrade_Speed,
		UpgradeType.Xp    => Upgrade_Xp,
		UpgradeType.Coin  => Upgrade_Coin,
		_ => 0
	};

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
				Upgrade_Speed = profile.Upgrade_Speed;
				Upgrade_Xp = profile.Upgrade_Xp;
				Upgrade_Coin = profile.Upgrade_Coin;
				Rebirths = profile.Rebirths;
				Log.Info( $"[Runner] Profile loaded — Lvl {Level} · {Xp} XP · {Coins} coins · upg S{Upgrade_Speed}/X{Upgrade_Xp}/C{Upgrade_Coin} · {Rebirths} rebirths" );
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
				Upgrade_Speed = Upgrade_Speed,
				Upgrade_Xp = Upgrade_Xp,
				Upgrade_Coin = Upgrade_Coin,
				Rebirths = Rebirths,
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

		// Apply shop XP-gain multiplier (never below original amount).
		long boosted = (long)(amount * XpGainMultiplier);
		if ( boosted < amount )
			boosted = amount;

		int oldLevel = Level;
		Xp += boosted;

		var (newLevel, _, _) = XpCurve.ComputeLevel( Xp, BaseXpPerLevel, XpGrowth );
		Level = newLevel;

		EventBus.Publish( new PlayerXpGranted( SteamId(), boosted, reason ) );
		_dirty = true;

		if ( newLevel > oldLevel )
		{
			Log.Info( $"[Runner] LEVEL UP! {oldLevel} → {newLevel} (×{TotalSpeedMultiplier:0.00} speed)" );
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

		// Coins are a rare, high-value event — skip the throttle and persist now.
		if ( _profileLoaded )
			_ = SaveProfileAsync();
	}

	/// <summary>
	/// Dev / testing: wipe progression so you can re-test the level at base speed.
	/// Resets Xp, Level, Coins, upgrades, and the XP accumulator. Persists immediately.
	/// Exposed as a button in the Inspector when the Player is selected.
	/// </summary>
	[Button( "↺ Reset progress" )]
	public void ResetProgress()
	{
		if ( IsProxy )
			return;

		Xp = 0;
		Level = 1;
		Coins = 0;
		Upgrade_Speed = 0;
		Upgrade_Xp = 0;
		Upgrade_Coin = 0;
		Rebirths = 0;
		_runUnitsAccumulator = 0f;
		_dirty = true;

		Log.Info( "[Runner] Progress reset → Lvl 1 · 0 XP · 0 coins · upgrades cleared · 0 rebirths" );

		if ( _profileLoaded )
			_ = SaveProfileAsync();
	}

	// ── Shop purchase ────────────────────────────────────────────────────────

	/// <summary>Attempt to buy the next level of an upgrade. Server-side; no-op if not enough coins.</summary>
	public bool TryBuyUpgrade( UpgradeType type )
	{
		if ( IsProxy )
			return false;

		var upgrade = ShopUpgrades.GetByType( type );
		if ( upgrade is null )
			return false;

		int currentLevel = GetUpgradeLevel( type );
		long cost = ShopUpgrades.CostAtLevel( upgrade, currentLevel );
		if ( Coins < cost )
			return false;

		Coins -= cost;
		switch ( type )
		{
			case UpgradeType.Speed: Upgrade_Speed = currentLevel + 1; break;
			case UpgradeType.Xp:    Upgrade_Xp = currentLevel + 1; break;
			case UpgradeType.Coin:  Upgrade_Coin = currentLevel + 1; break;
		}

		_dirty = true;
		Log.Info( $"[Shop] Bought {type} → lvl {currentLevel + 1} · -{cost} coins (now {Coins})" );
		EventBus.Publish( new PlayerCurrencyChanged( SteamId(), "coins", -cost, Coins ) );

		if ( _profileLoaded )
			_ = SaveProfileAsync();

		return true;
	}

	// ── Rebirth ──────────────────────────────────────────────────────────────

	public bool CanRebirth => Level >= GameConfig.MinLevelForRebirth;

	/// <summary>
	/// Wipe XP/Level/Coins/Upgrades and grant +1 Rebirth. Permanent rebirth bonus
	/// stacks on top of everything else. Server-side; persisted immediately.
	/// </summary>
	public bool TryRebirth()
	{
		if ( IsProxy )
			return false;
		if ( !CanRebirth )
			return false;

		Rebirths += 1;
		Xp = 0;
		Level = 1;
		Coins = 0;
		Upgrade_Speed = 0;
		Upgrade_Xp = 0;
		Upgrade_Coin = 0;
		_runUnitsAccumulator = 0f;
		_dirty = true;

		Log.Info( $"[Rebirth] ✦ Rebirth #{Rebirths} — permanent bonus now ×{RebirthBonus:0.00} to all gains." );
		EventBus.Publish( new PlayerRebirthed( SteamId(), Rebirths ) );

		if ( _profileLoaded )
			_ = SaveProfileAsync();

		return true;
	}
}
