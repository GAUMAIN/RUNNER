namespace Runner.Config;

/// <summary>
/// Runtime user preferences. Written to from the pause menu, read by gameplay systems.
/// Not yet persisted to disk — survives only within a play session for now.
/// </summary>
public static class UserSettings
{
	/// <summary>Master volume multiplier applied to all gameplay sound playbacks. 0.0–1.0.</summary>
	public static float Volume { get; set; } = 1f;

	/// <summary>Base FOV that CameraSpeedFx interpolates AROUND. 60–110.</summary>
	public static float BaseFov { get; set; } = 80f;

	/// <summary>Mouse look sensitivity multiplier. PlayerPawn multiplies AnalogLook by this. 0.1–3.0.</summary>
	public static float MouseSensitivity { get; set; } = 0.5f;
}
