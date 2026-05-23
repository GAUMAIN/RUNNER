using System;
using System.Linq;
using Sandbox;

namespace Runner.Player;

/// <summary>
/// Turns the equipped katana into a real weapon: Mouse1 swings, a sphere
/// trace forward from the camera looks for things to damage, and we apply
/// <see cref="DamageInfo"/> to whatever <see cref="IDamageable"/> the hit
/// GameObject (or its parents) carries — plus a physics impulse on the
/// hit body for that satisfying knock-back feel.
///
/// Pattern adapted from sbox-scenestaging/Libraries/weaponlab/TestWeapon.cs.
/// The visual swing animation itself is driven by <see cref="FpsViewmodel"/>
/// reading our <see cref="IsSwinging"/> + <see cref="SwingT"/> state each
/// frame — that way we own the gameplay logic here and the rendering knows
/// when to override the rest pose.
/// </summary>
public sealed class KnifeAttack : Component
{
	/// <summary>Min seconds between two swings.</summary>
	[Property, Range( 0.1f, 2f )] public float SwingCooldown { get; set; } = 0.4f;

	/// <summary>How long the swing animation plays (also the hit window).</summary>
	[Property, Range( 0.1f, 1f )] public float SwingDuration { get; set; } = 0.3f;

	/// <summary>Max reach of the swing in units.</summary>
	[Property] public float SwingRange { get; set; } = 80f;

	/// <summary>Sphere radius for the swing trace — gives the knife "girth".</summary>
	[Property] public float SwingRadius { get; set; } = 25f;

	/// <summary>Damage applied to anything implementing IDamageable on hit.</summary>
	[Property] public float BaseDamage { get; set; } = 10f;

	/// <summary>Physics impulse applied to rigid bodies on hit.</summary>
	[Property] public float HitImpulse { get; set; } = 200f;

	/// <summary>Optional swing whoosh sound. Falls back to a Kenney UI tick.</summary>
	[Property] public SoundEvent SwingSound { get; set; }

	// ─── Runtime state ────────────────────────────────────────────────────

	private TimeSince _timeSinceSwing = 999f;
	private CameraComponent _camera;

	/// <summary>True from the moment we trigger a swing until SwingDuration elapses.</summary>
	public bool IsSwinging => _timeSinceSwing < SwingDuration;

	/// <summary>0→1 progress through the current swing animation, or -1 if idle.</summary>
	public float SwingT => IsSwinging ? MathX.Clamp( (float)_timeSinceSwing / SwingDuration, 0f, 1f ) : -1f;

	// ─── Per-frame ────────────────────────────────────────────────────────

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( Runner.Config.UserSettings.IsPaused ) return;

		// Cooldown gate.
		if ( _timeSinceSwing < SwingCooldown ) return;

		if ( !Input.Pressed( "Attack1" ) ) return;

		// FPS-only attacks for now (we don't drive the citizen body in TPS).
		var pawn = GameObject.Components.Get<PlayerPawn>();
		if ( !pawn.IsValid() || !pawn.FirstPerson ) return;

		StartSwing();
	}

	private void StartSwing()
	{
		_timeSinceSwing = 0f;

		PlaySwingSound();
		TriggerAttackAnimation();
		DoHitDetection();
	}

	/// <summary>
	/// Trigger the citizen rig's built-in melee attack animation
	/// (citizen@melee_weapons_2h_attack_01) via the animgraph parameters.
	/// Splats common param names since we don't have the animgraph open to
	/// know exactly which one drives the attack.
	/// </summary>
	private void TriggerAttackAnimation()
	{
		var anim = GameObject.Components.Get<Sandbox.Citizen.CitizenAnimationHelper>( true );
		if ( !anim.IsValid() || !anim.Target.IsValid() )
			return;

		anim.Target.Set( "b_attack", true );
		anim.Target.Set( "b_swing", true );
		anim.Target.Set( "attack", true );
		anim.Target.Set( "holdtype_attack", 1 );
	}

	private void PlaySwingSound()
	{
		var handle = SwingSound is not null
			? Sound.Play( SwingSound )
			: Sound.Play( "sounds/kenney/ui/ui.drag.start.sound" );
		if ( handle is not null )
			handle.Volume *= Runner.Config.UserSettings.Volume * 0.8f;
	}

	private void DoHitDetection()
	{
		if ( !_camera.IsValid() )
			_camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( !_camera.IsValid() ) return;

		var from = _camera.WorldPosition;
		var dir  = _camera.WorldRotation.Forward;
		var to   = from + dir * SwingRange;

		var tr = Scene.Trace
			.Sphere( SwingRadius, from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !tr.Hit ) return;
		if ( !tr.GameObject.IsValid() ) return;

		// Impact sound — pick by what the trace hit. We re-use the same
		// name-based surface classification as the footsteps so a swing
		// against a wood platform sounds different from one against grass.
		PlayImpactSound( tr.GameObject, tr.HitPosition );

		// Physics impulse — knock-back for any rigid body we hit.
		if ( tr.Body.IsValid() )
		{
			var clampedMass = MathX.Clamp( tr.Body.Mass, 0f, 200f );
			tr.Body.ApplyImpulseAt( tr.HitPosition, dir * HitImpulse * clampedMass );
		}

		// Damage — broadcast to IDamageable components on the hit object.
		var damage = new DamageInfo( BaseDamage, GameObject, GameObject, tr.Hitbox );
		damage.Position = tr.HitPosition;
		damage.Shape = tr.Shape;
		foreach ( var d in tr.GameObject.Components.GetAll<IDamageable>() )
		{
			d.OnDamage( damage );
		}
	}

	private void PlayImpactSound( GameObject hit, Vector3 pos )
	{
		// Walk parents looking for a known surface keyword so the impact
		// sound matches what the player is hitting.
		var current = hit;
		int depth = 0;
		string path = null;
		while ( current.IsValid() && depth < 8 && path is null )
		{
			var name = current.Name ?? string.Empty;
			if ( name.StartsWith( "Platform_" ) || name.StartsWith( "L1_" ) || name == "Plaza_Hub" || name.StartsWith( "Hub_" ) || name == "CashOutPad" )
				path = "sounds/impacts/melee/impact-melee-grass.sound";
			else if ( name.StartsWith( "L2_" ) )
				path = "sounds/impacts/melee/impact-melee-dirt.sound";
			else if ( name.StartsWith( "L3_" ) )
				path = "sounds/impacts/melee/impact-melee-wood.sound";
			else if ( name.StartsWith( "L4_" ) )
				path = "sounds/impacts/melee/impact-melee-metal.sound";

			current = current.Parent;
			depth++;
		}

		// Default to concrete if we couldn't classify.
		path ??= "sounds/impacts/melee/impact-melee-concrete.sound";

		var handle = Sound.Play( path, pos );
		if ( handle is not null )
			handle.Volume *= Runner.Config.UserSettings.Volume;
	}
}
