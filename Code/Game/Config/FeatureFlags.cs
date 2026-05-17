namespace Runner.Config;

/// <summary>
/// Toggle features on/off without redeploying. For live ops, swap to data-driven.
/// </summary>
public static class FeatureFlags
{
	public static bool MultiplayerRacesEnabled = true;
	public static bool RebirthEnabled = true;
	public static bool BattlePassEnabled = false;        // M5
	public static bool DailyRewardsEnabled = true;
	public static bool AnalyticsEnabled = true;
	public static bool DebugOverlayEnabled = false;
	public static bool AdminPanelEnabled = false;
}
