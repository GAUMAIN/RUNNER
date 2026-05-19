using System.Linq;
using Sandbox;
using Runner.Player;

namespace Runner.World;

/// <summary>
/// Hub knife-case pad. Proximity-driven like Shop/Rebirth/Leaderboard.
/// The associated CasePanel UI opens automatically when the player is nearby.
/// Pressing the "use" key (E) or clicking the panel button triggers the roll.
///
/// Gated by GameConfig.MinRebirthsForKnifeCase and GameConfig.KnifeCaseCost.
/// </summary>
public sealed class CasePad : Component
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

		// Press "use" (E) to roll
		if ( Input.Pressed( "use" ) )
			NearbyStats.TryOpenKnifeCase();
	}
}
