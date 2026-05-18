using System.Linq;
using Sandbox;
using Runner.Player;

namespace Runner.World;

/// <summary>
/// Hub rebirth pad. Same proximity pattern as ShopPad — when the local player
/// is within <see cref="TriggerRadius"/>, the RebirthPanel modal opens and the
/// player can press "use" (E) to commit, or click the button in the panel.
///
/// Server-authoritative: TryRebirth runs on the host. Will no-op below the
/// configured min level.
/// </summary>
public sealed class RebirthPad : Component
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

		if ( !IsPlayerNear || !NearbyStats.IsValid() )
			return;

		// Keyboard shortcut: "use" key (E by default).
		if ( Input.Pressed( "use" ) )
			NearbyStats.TryRebirth();
	}
}
