using System;
using System.Collections.Generic;
using Runner.Config;

namespace Runner.Player;

/// <summary>
/// Persistent per-player state. Server-authoritative; sent to owner only.
/// Schema-versioned so we can migrate saves on update.
/// </summary>
public sealed class PlayerProfile
{
	public string SchemaVersion { get; set; } = GameConfig.SaveSchemaVersion;
	public ulong PlayerId { get; set; }
	public string DisplayName { get; set; } = "";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

	// ── Progression ────────────────────────────────────────────────────────────
	public int Level { get; set; } = 1;
	public long Xp { get; set; }
	public int Rebirths { get; set; }

	// ── Currencies ─────────────────────────────────────────────────────────────
	public Dictionary<string, long> Currencies { get; set; } = new()
	{
		["coins"] = 0,
		["gems"] = 0,
		["tokens"] = 0,
	};

	// ── Inventory ──────────────────────────────────────────────────────────────
	public List<string> OwnedEquipmentIds { get; set; } = new();
	public List<string> OwnedPetIds { get; set; } = new();
	public string EquippedTrailId { get; set; } = "";
	public string EquippedAuraId { get; set; } = "";

	// ── Multipliers (derived from rebirths, gear, pets, boosts) ────────────────
	public float SpeedMultiplier { get; set; } = 1f;
	public float XpMultiplier { get; set; } = 1f;
	public float CoinMultiplier { get; set; } = 1f;

	// ── Lifetime stats ─────────────────────────────────────────────────────────
	public long LifetimeDistanceTraveled { get; set; }
	public int RacesWon { get; set; }
	public int RacesEntered { get; set; }
}
