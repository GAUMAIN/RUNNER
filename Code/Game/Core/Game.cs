using System;
using Sandbox;

namespace Runner.Core;

/// <summary>
/// Entry point. Resolves services and brings the game online.
/// </summary>
public sealed class Game : Component
{
	public static Game Current { get; private set; } = null!;

	protected override void OnAwake()
	{
		Current = this;
		Log.Info( "[Runner] Game.OnAwake — bootstrapping…" );
	}

	protected override void OnStart()
	{
		// Services and managers will be registered here.
		// See ARCHITECTURE.md → "Patterns Used → Service Locator".
		Log.Info( "[Runner] Game.OnStart — ready." );
	}
}
