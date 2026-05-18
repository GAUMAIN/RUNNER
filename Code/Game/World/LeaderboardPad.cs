using System.Linq;
using Sandbox;
using Runner.Player;

namespace Runner.World;

/// <summary>
/// Hub leaderboard / records pad. Same proximity pattern as Shop/Rebirth pads.
/// Read-only display — no input, the panel just shows lifetime stats from the
/// local player's PlayerStats when you stand on the pad.
///
/// Cloud-backed multi-player leaderboard will swap the data source later;
/// the pad and panel stay the same.
/// </summary>
public sealed class LeaderboardPad : Component
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
	}
}
