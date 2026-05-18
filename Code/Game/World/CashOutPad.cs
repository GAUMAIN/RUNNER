using System.Linq;
using Sandbox;
using Runner.Player;

namespace Runner.World;

/// <summary>
/// End-of-level reward pad. When the local player walks within <see cref="TriggerRadius"/>
/// of the pad, grants <see cref="CoinReward"/> coins (persistent) and teleports
/// the pawn back to <see cref="SpawnPosition"/>.
///
/// Uses a proximity check rather than ITriggerListener — the CharacterController
/// movement doesn't fire trigger callbacks on external Collider/IsTrigger volumes,
/// only static world geometry does.
///
/// Design intent: this is the ONLY way to bank coins. Dying or backing out
/// before reaching a pad costs the run.
/// </summary>
public sealed class CashOutPad : Component
{
	[Property] public long CoinReward { get; set; } = 5;

	/// <summary>Where the player is teleported on cash-out.</summary>
	[Property] public Vector3 SpawnPosition { get; set; } = new Vector3( 0, 0, 64 );

	/// <summary>Distance (in world units) at which the pad fires. Centered on the GameObject.</summary>
	[Property] public float TriggerRadius { get; set; } = 80f;

	[Property] public SoundEvent CashOutSound { get; set; }

	/// <summary>Re-arm time so a single overlap doesn't fire repeatedly.</summary>
	[Property] public float CooldownSeconds { get; set; } = 1.5f;

	private TimeSince _timeSinceTrigger = 999f;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;
		if ( _timeSinceTrigger < CooldownSeconds )
			return;

		float radiusSq = TriggerRadius * TriggerRadius;

		foreach ( var pawn in Scene.GetAllComponents<PlayerPawn>() )
		{
			if ( !pawn.IsValid() )
				continue;

			float distSq = (pawn.WorldPosition - WorldPosition).LengthSquared;
			if ( distSq > radiusSq )
				continue;

			var stats = pawn.GameObject.Components.Get<PlayerStats>();
			if ( !stats.IsValid() )
				continue;

			long reward = (long)(CoinReward * stats.CoinGainMultiplier);
			if ( reward < CoinReward )
				reward = CoinReward;

			Log.Info( $"[CashOutPad] {pawn.GameObject.Name} cashed out +{reward} (base {CoinReward} × {stats.CoinGainMultiplier:0.00})" );

			stats.GrantCoins( reward );
			TeleportPawn( pawn, SpawnPosition );

			if ( CashOutSound is not null )
				Sound.Play( CashOutSound, pawn.WorldPosition );

			_timeSinceTrigger = 0f;
			return; // one player per tick
		}
	}

	private static void TeleportPawn( PlayerPawn pawn, Vector3 position )
	{
		pawn.WorldPosition = position;
		var cc = pawn.GameObject.Components.Get<CharacterController>();
		if ( cc.IsValid() )
			cc.Velocity = Vector3.Zero;
	}
}
