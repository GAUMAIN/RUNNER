namespace Runner.Config;

/// <summary>
/// Compile-time constants used across the game.
/// Anything that designers should tune lives in a GameResource under Code/Game/Data/, not here.
/// </summary>
public static class GameConfig
{
	// ── Build / versioning ────────────────────────────────────────────────────
	public const string GameName = "RUNNER";
	public const string SaveSchemaVersion = "0.1.0";

	// ── Networking ────────────────────────────────────────────────────────────
	public const int TickRate = 64;
	public const int MaxPlayersPerServer = 64;
	public const int InterestRadiusUnits = 4096;

	// ── Movement ──────────────────────────────────────────────────────────────
	public const float BaseRunSpeed = 320f;
	public const float MaxRunSpeedAbsolute = 25_000f;     // anti-cheat ceiling
	public const float DashCooldownSeconds = 1.5f;

	// ── Economy ───────────────────────────────────────────────────────────────
	public const int OfflineAfkCapHours = 8;
	public const float AfkPadDiminishingAfterMinutes = 90f;

	// ── Persistence ───────────────────────────────────────────────────────────
	public const float ProfileSaveThrottleSeconds = 5f;
}
