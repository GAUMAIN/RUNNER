using System;
using System.Collections.Generic;

namespace Runner.Core;

/// <summary>
/// Minimal service locator. Register at boot, resolve anywhere.
/// Not a DI container — kept small on purpose. See ARCHITECTURE.md.
/// </summary>
public static class ServiceLocator
{
	private static readonly Dictionary<Type, object> _services = new();

	public static void Register<T>( T instance ) where T : class
	{
		_services[typeof( T )] = instance ?? throw new ArgumentNullException( nameof( instance ) );
	}

	public static T Get<T>() where T : class
	{
		if ( _services.TryGetValue( typeof( T ), out var svc ) )
			return (T)svc;

		throw new InvalidOperationException( $"Service not registered: {typeof( T ).Name}" );
	}

	public static bool TryGet<T>( out T service ) where T : class
	{
		if ( _services.TryGetValue( typeof( T ), out var svc ) )
		{
			service = (T)svc;
			return true;
		}

		service = null!;
		return false;
	}

	public static void Clear()
	{
		_services.Clear();
	}
}
