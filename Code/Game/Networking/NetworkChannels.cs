namespace Runner.Networking;

/// <summary>
/// Channel identifiers for replication grouping. See docs/networking.md.
/// Frequencies are documented here; enforcement happens per-system.
/// </summary>
public enum NetworkChannel
{
	/// <summary>Per-player movement state. ~64 Hz, owner + nearby only.</summary>
	Movement = 1,

	/// <summary>Race state (checkpoints, finish). ~16 Hz, race participants.</summary>
	Race = 2,

	/// <summary>Owner profile (XP, currencies, inventory). On-change.</summary>
	Profile = 3,

	/// <summary>World entities (collectibles, AFK pads). ~8 Hz, zone-bound.</summary>
	World = 4,

	/// <summary>Cosmetics (trails, auras, pets). On-change, broadcast.</summary>
	Cosmetic = 5,
}
