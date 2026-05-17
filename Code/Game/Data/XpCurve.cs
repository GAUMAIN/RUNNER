using System;

namespace Runner.Data;

/// <summary>
/// Pure functions for XP ↔ Level math.
///
/// Defaults give: lvl 1→2 = 100 XP · lvl 10→11 = 236 XP · lvl 25→26 = 985 XP · lvl 50→51 = 10,672 XP
/// </summary>
public static class XpCurve
{
	/// <summary>XP needed to GO FROM <paramref name="level"/> TO <paramref name="level"/>+1.</summary>
	public static long XpRequiredForLevel( int level, long baseXp = 100, float growth = 1.10f )
	{
		if ( level < 1 )
			return baseXp;

		return (long)(baseXp * MathF.Pow( growth, level - 1 ));
	}

	/// <summary>
	/// Decompose a total XP value into (level, xpInsideCurrentLevel, xpNeededForNextLevel).
	/// Hard-caps the loop to avoid runaway computation on absurd XP values.
	/// </summary>
	public static (int Level, long CurrentLevelXp, long NextLevelXp) ComputeLevel(
		long totalXp,
		long baseXp = 100,
		float growth = 1.10f,
		int maxLevel = 9999 )
	{
		int level = 1;
		long remaining = Math.Max( 0, totalXp );

		while ( level < maxLevel )
		{
			long needed = XpRequiredForLevel( level, baseXp, growth );
			if ( remaining < needed )
				return (level, remaining, needed);
			remaining -= needed;
			level++;
		}

		return (maxLevel, 0, XpRequiredForLevel( maxLevel, baseXp, growth ));
	}
}
