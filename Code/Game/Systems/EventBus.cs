using System;
using System.Collections.Generic;

namespace Runner.Systems;

/// <summary>
/// Strongly-typed, fire-and-forget event bus.
/// Systems publish events; other systems subscribe without knowing the publisher.
/// </summary>
public static class EventBus
{
	private static readonly Dictionary<Type, List<Delegate>> _handlers = new();

	public static void Subscribe<T>( Action<T> handler ) where T : struct
	{
		if ( !_handlers.TryGetValue( typeof( T ), out var list ) )
		{
			list = new List<Delegate>();
			_handlers[typeof( T )] = list;
		}
		list.Add( handler );
	}

	public static void Unsubscribe<T>( Action<T> handler ) where T : struct
	{
		if ( _handlers.TryGetValue( typeof( T ), out var list ) )
			list.Remove( handler );
	}

	public static void Publish<T>( T evt ) where T : struct
	{
		if ( !_handlers.TryGetValue( typeof( T ), out var list ) )
			return;

		// Copy to allow unsubscribe during dispatch
		var snapshot = list.ToArray();
		foreach ( var d in snapshot )
		{
			try
			{
				((Action<T>)d)( evt );
			}
			catch ( Exception ex )
			{
				Sandbox.Log.Error( $"[EventBus] handler for {typeof( T ).Name} threw: {ex}" );
			}
		}
	}

	public static void Clear()
	{
		_handlers.Clear();
	}
}
