using System;
using System.Linq;
using Sandbox;
using Runner.Player;

namespace Runner.VFX;

/// <summary>
/// Drives the camera's field-of-view based on the local player's speed.
/// Smoothly lerps so the camera doesn't snap when the player accelerates or stops.
/// Attach to the same GameObject as the <see cref="CameraComponent"/>.
/// </summary>
public sealed class CameraSpeedFx : Component
{
	[Property] public float BaseFov { get; set; } = 80f;
	[Property] public float MaxFov { get; set; } = 110f;

	/// <summary>Speed (u/s) at which FOV starts opening up.</summary>
	[Property] public float SpeedAtBase { get; set; } = 320f;

	/// <summary>Speed (u/s) at which FOV reaches <see cref="MaxFov"/>.</summary>
	[Property] public float SpeedAtMax { get; set; } = 1500f;

	/// <summary>Higher = faster catch-up. ~6 feels smooth.</summary>
	[Property, Range( 1f, 20f )] public float Damping { get; set; } = 6f;

	private CameraComponent _camera;
	private float _currentFov;

	protected override void OnEnabled()
	{
		_camera = GameObject.Components.Get<CameraComponent>();
		if ( _camera.IsValid() )
			_currentFov = _camera.FieldOfView;
	}

	protected override void OnUpdate()
	{
		if ( !_camera.IsValid() )
			return;

		var pawn = Scene.GetAllComponents<PlayerPawn>().FirstOrDefault( p => !p.IsProxy );
		if ( !pawn.IsValid() )
			return;

		var cc = pawn.GameObject.Components.Get<CharacterController>();
		if ( !cc.IsValid() )
			return;

		float speed = cc.Velocity.WithZ( 0 ).Length;
		float span = MathF.Max( 1f, SpeedAtMax - SpeedAtBase );
		float t = ((speed - SpeedAtBase) / span).Clamp( 0f, 1f );
		float targetFov = MathX.Lerp( BaseFov, MaxFov, t );

		_currentFov = MathX.Lerp( _currentFov, targetFov, Time.Delta * Damping );
		_camera.FieldOfView = _currentFov;
	}
}
