using System.Collections.Generic;

namespace Runner.Backend;

/// <summary>
/// Telemetry hook. Implementations: NoOp (default), local-log (dev), HTTP (prod).
/// Calls must be fire-and-forget — never block gameplay.
/// </summary>
public interface IAnalyticsClient
{
	void Track( string eventName, IReadOnlyDictionary<string, object>? properties = null );
	void Identify( ulong playerId, IReadOnlyDictionary<string, object>? traits = null );
}
