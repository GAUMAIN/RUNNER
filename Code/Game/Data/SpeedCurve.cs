using System;

namespace Runner.Data;

/// <summary>
/// Pure function. Converts a player level into a speed multiplier.
/// Compound growth: each level multiplies the previous by (1 + gainPerLevel).
///
/// Defaults give: lvl 1 = 1.00x · lvl 10 = 1.30x · lvl 25 = 2.09x · lvl 50 = 4.38x · lvl 100 = 19.21x
/// </summary>
public static class SpeedCurve
{
	public static float MultiplierAtLevel( int level, float baseMultiplier = 1f, float gainPerLevel = 0.03f )
	{
		if ( level <= 1 )
			return baseMultiplier;

		return baseMultiplier * MathF.Pow( 1f + gainPerLevel, level - 1 );
	}
}
