using Sandbox;
using Runner.Player;

namespace Runner.World;

/// <summary>
/// End-of-level reward pad. Step on it → grants <see cref="CoinReward"/> coins
/// to the player (persistent) and teleports the pawn back to <see cref="SpawnPosition"/>.
///
/// Requirements on the same GameObject:
///   • a Collider (Box / Sphere) with IsTrigger = true
///
/// Design intent: this is the ONLY way to bank coins. Dying or backing out
/// before reaching a pad costs the run.
/// </summary>
public sealed class CashOutPad : Component, Component.ITriggerListener
{
	[Property] public long CoinReward { get; set; } = 5;

	/// <summary>Where the player is teleported on cash-out.</summary>
	[Property] public Vector3 SpawnPosition { get; set; } = new Vector3( 0, 0, 64 );

	[Property] public SoundEvent CashOutSound { get; set; }

	/// <summary>Re-arm time so a single overlap doesn't fire repeatedly.</summary>
	[Property] public float CooldownSeconds { get; set; } = 1f;

	private TimeSince _timeSinceTrigger = 999f;

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( IsProxy )
			return;
		if ( _timeSinceTrigger < CooldownSeconds )
			return;

		var pawn = FindPawnOn( other.GameObject );
		if ( !pawn.IsValid() )
			return;

		var stats = pawn.GameObject.Components.Get<PlayerStats>();
		if ( !stats.IsValid() )
			return;

		stats.GrantCoins( CoinReward );
		TeleportPawn( pawn, SpawnPosition );

		if ( CashOutSound is not null )
			Sound.Play( CashOutSound, pawn.WorldPosition );

		_timeSinceTrigger = 0f;
	}

	private static PlayerPawn FindPawnOn( GameObject go )
	{
		var node = go;
		while ( node.IsValid() )
		{
			var pawn = node.Components.Get<PlayerPawn>();
			if ( pawn.IsValid() )
				return pawn;
			node = node.Parent;
		}
		return null;
	}

	private static void TeleportPawn( PlayerPawn pawn, Vector3 position )
	{
		pawn.WorldPosition = position;
		var cc = pawn.GameObject.Components.Get<CharacterController>();
		if ( cc.IsValid() )
			cc.Velocity = Vector3.Zero;
	}
}
