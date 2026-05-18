using System.Linq;
using Sandbox;
using Runner.Economy;
using Runner.Player;

namespace Runner.World;

/// <summary>
/// Hub shop. When the local player is within <see cref="TriggerRadius"/>, the
/// associated <c>ShopPanel</c> UI lights up and the player can press 1/2/3 to
/// buy each upgrade row from <see cref="ShopUpgrades"/>.
///
/// Server-authoritative: TryBuyUpgrade runs on the host; clients only see
/// replicated PlayerStats fields.
/// </summary>
public sealed class ShopPad : Component
{
	[Property] public float TriggerRadius { get; set; } = 120f;

	public bool IsPlayerNear { get; private set; }
	public PlayerStats NearbyStats { get; private set; }

	protected override void OnUpdate()
	{
		var pawn = Scene.GetAllComponents<PlayerPawn>().FirstOrDefault( p => !p.IsProxy );
		if ( !pawn.IsValid() )
		{
			IsPlayerNear = false;
			NearbyStats = null;
			return;
		}

		float dist = (pawn.WorldPosition - WorldPosition).Length;
		IsPlayerNear = dist <= TriggerRadius;
		NearbyStats = IsPlayerNear ? pawn.GameObject.Components.Get<PlayerStats>() : null;

		// Input only fires on the owning client; the host runs the actual purchase.
		if ( !IsPlayerNear || !NearbyStats.IsValid() )
			return;

		if ( Input.Pressed( "Slot1" ) )
			NearbyStats.TryBuyUpgrade( UpgradeType.Speed );
		else if ( Input.Pressed( "Slot2" ) )
			NearbyStats.TryBuyUpgrade( UpgradeType.Xp );
		else if ( Input.Pressed( "Slot3" ) )
			NearbyStats.TryBuyUpgrade( UpgradeType.Coin );
	}
}
