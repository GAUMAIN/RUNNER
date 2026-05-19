using System;
using System.Collections.Generic;
using System.Linq;

namespace Runner.Economy;

public enum KnifeRarity
{
	Common,
	Uncommon,
	Rare,
	Epic,
	Mythic,
	Secret
}

/// <summary>One row in the knife catalog. Probability is a 0-1 weight; the
/// sum across the catalog must equal 1.</summary>
public sealed record KnifeSkin(
	string Id,
	string Name,
	KnifeRarity Rarity,
	float Probability
);

/// <summary>
/// Hardcoded knife catalog. Probabilities sum to 1.000. Data-driven config (GameResource)
/// will swap this in later so designers can tune rarities in the editor.
/// </summary>
public static class KnifeCatalog
{
	public static readonly KnifeSkin[] All = new[]
	{
		new KnifeSkin( "steel",           "Steel Knife",       KnifeRarity.Common,   0.50f ),
		new KnifeSkin( "skull",           "Skull Pattern",     KnifeRarity.Uncommon, 0.25f ),
		new KnifeSkin( "karambit",        "Karambit",          KnifeRarity.Rare,     0.12f ),
		new KnifeSkin( "bayonet",         "Bayonet",           KnifeRarity.Rare,     0.085f ),
		new KnifeSkin( "butterfly",       "Butterfly Knife",   KnifeRarity.Epic,     0.035f ),
		new KnifeSkin( "katana",          "Katana",            KnifeRarity.Mythic,   0.009f ),
		new KnifeSkin( "cursed_karambit", "Cursed Karambit",   KnifeRarity.Secret,   0.001f ),
	};

	public static KnifeSkin GetById( string id )
	{
		foreach ( var k in All )
			if ( k.Id == id )
				return k;
		return null;
	}

	/// <summary>Pick a knife from the catalog according to the probability table.</summary>
	public static KnifeSkin Roll( Random rng = null )
	{
		rng ??= Random.Shared;
		float r = (float)rng.NextDouble();
		float cumulative = 0f;
		foreach ( var k in All )
		{
			cumulative += k.Probability;
			if ( r <= cumulative )
				return k;
		}
		return All[0]; // fallback for floating-point edge case
	}

	public static string RarityLabel( KnifeRarity r ) => r switch
	{
		KnifeRarity.Common   => "COMMON",
		KnifeRarity.Uncommon => "UNCOMMON",
		KnifeRarity.Rare     => "RARE",
		KnifeRarity.Epic     => "EPIC",
		KnifeRarity.Mythic   => "MYTHIC",
		KnifeRarity.Secret   => "SECRET",
		_ => "UNKNOWN"
	};
}
