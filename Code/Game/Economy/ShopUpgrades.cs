using System;

namespace Runner.Economy;

public enum UpgradeType
{
	Speed,
	Xp,
	Coin
}

/// <summary>One row in the shop. Cost grows exponentially per owned level.</summary>
public sealed record ShopUpgrade(
	UpgradeType Type,
	string Name,
	string Hotkey,
	long BaseCost,
	float CostGrowth,
	float EffectPerLevel
);

/// <summary>
/// Hardcoded for v1. Later this becomes a data-driven GameResource so designers can tune in editor.
/// </summary>
public static class ShopUpgrades
{
	public static readonly ShopUpgrade[] All = new[]
	{
		new ShopUpgrade( UpgradeType.Speed, "+5%  Speed",     "1", BaseCost: 10, CostGrowth: 1.5f, EffectPerLevel: 0.05f ),
		new ShopUpgrade( UpgradeType.Xp,    "+10% XP Gain",   "2", BaseCost: 10, CostGrowth: 1.5f, EffectPerLevel: 0.10f ),
		new ShopUpgrade( UpgradeType.Coin,  "+10% Coin Gain", "3", BaseCost: 25, CostGrowth: 1.6f, EffectPerLevel: 0.10f ),
	};

	public static ShopUpgrade GetByType( UpgradeType type )
	{
		foreach ( var u in All )
			if ( u.Type == type )
				return u;
		return null;
	}

	public static long CostAtLevel( ShopUpgrade upgrade, int currentLevel )
	{
		if ( upgrade is null )
			return long.MaxValue;
		return (long)(upgrade.BaseCost * MathF.Pow( upgrade.CostGrowth, currentLevel ));
	}
}
