using System.Linq;
using Sandbox;
using Runner.Events;
using Runner.Player;
using Runner.Systems;

namespace Runner.Economy;

/// <summary>
/// World pickup. A player runs through it → coins added to their in-run wallet.
/// Disables itself on pickup and (optionally) respawns after <see cref="RespawnSeconds"/>.
///
/// Requirements on the same GameObject:
///   • a Collider with IsTrigger = true (Box / Sphere)
///   • a ModelRenderer (optional but recommended) — its enabled state mirrors the pickup
/// </summary>
public sealed class CoinPickup : Component, Component.ITriggerListener
{
	[Property] public long Value { get; set; } = 1;

	/// <summary>Seconds before the coin reappears. 0 = never respawn.</summary>
	[Property] public float RespawnSeconds { get; set; } = 3f;

	[Property] public SoundEvent PickupSound { get; set; }

	/// <summary>Visual flair — degrees per second spin around Z. 0 to disable.</summary>
	[Property] public float SpinSpeed { get; set; } = 180f;

	private bool _consumed;
	private TimeSince _timeSinceConsumed;

	protected override void OnUpdate()
	{
		// Spin the visual when active.
		if ( !_consumed && SpinSpeed != 0f )
			WorldRotation *= Rotation.FromYaw( SpinSpeed * Time.Delta );

		// Respawn handling — server-authoritative.
		if ( _consumed && RespawnSeconds > 0f && _timeSinceConsumed >= RespawnSeconds )
			Respawn();
	}

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( _consumed )
			return;

		// Only the host runs the award logic. Visual state is replicated via Enabled.
		if ( IsProxy )
			return;

		var stats = FindStatsOn( other.GameObject );
		if ( !stats.IsValid() )
			return;

		stats.GrantHeldCoins( Value );

		if ( PickupSound is not null )
			Sound.Play( PickupSound, WorldPosition );

		Consume();
	}

	private static PlayerStats FindStatsOn( GameObject go )
	{
		// Walk up the GameObject hierarchy so trigger hits on child colliders still resolve to the pawn.
		var node = go;
		while ( node.IsValid() )
		{
			var stats = node.Components.Get<PlayerStats>();
			if ( stats.IsValid() )
				return stats;
			node = node.Parent;
		}
		return null;
	}

	private void Consume()
	{
		_consumed = true;
		_timeSinceConsumed = 0f;
		SetVisualEnabled( false );
	}

	private void Respawn()
	{
		_consumed = false;
		SetVisualEnabled( true );
	}

	private void SetVisualEnabled( bool enabled )
	{
		foreach ( var renderer in Components.GetAll<ModelRenderer>( FindMode.EnabledInSelfAndDescendants | FindMode.DisabledInSelfAndDescendants ) )
			renderer.Enabled = enabled;

		foreach ( var collider in Components.GetAll<Collider>( FindMode.EnabledInSelfAndDescendants | FindMode.DisabledInSelfAndDescendants ) )
			collider.Enabled = enabled;
	}
}
