using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts an <see cref="IAwaitenScope" /> to the Microsoft.Extensions.DependencyInjection resolution
///     surface, so an Awaiten container can be consumed as an <see cref="IServiceProvider" /> (and used to
///     create <see cref="IServiceScope">scopes</see>). <see cref="GetService" /> maps to the container's
///     <see cref="IAwaitenResolver.TryResolve" />, returning <see langword="null" /> for an unregistered
///     service as <see cref="IServiceProvider" /> requires.
/// </summary>
/// <remarks>
///     Awaiten owns the lifetime and disposal of the services: disposing the provider disposes the
///     underlying container (and the singletons it created), and disposing a scope disposes the
///     <see cref="IAwaitenScope" /> behind it.
/// </remarks>
public sealed class AwaitenServiceProvider : IServiceProvider, IServiceScopeFactory, IDisposable
{
	private readonly IAwaitenScope _container;
	private readonly bool _ownsContainer;

	/// <summary>
	///     Initializes a new instance of the <see cref="AwaitenServiceProvider" /> class over the given
	///     container.
	/// </summary>
	/// <param name="container">The Awaiten container to adapt.</param>
	/// <param name="ownsContainer">
	///     When <see langword="true" /> (the default), disposing this provider disposes the container.
	///     Pass <see langword="false" /> when the container's lifetime is owned elsewhere.
	/// </param>
	public AwaitenServiceProvider(IAwaitenScope container, bool ownsContainer = true)
	{
		_container = container ?? throw new ArgumentNullException(nameof(container));
		_ownsContainer = ownsContainer;
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		if (serviceType == typeof(IServiceProvider) || serviceType == typeof(AwaitenServiceProvider))
		{
			return this;
		}

		if (serviceType == typeof(IServiceScopeFactory))
		{
			return this;
		}

		return _container.TryResolve(serviceType, out object? instance) ? instance : null;
	}

	/// <inheritdoc />
	public IServiceScope CreateScope() => new AwaitenServiceScope(_container.CreateScope());

	/// <inheritdoc />
	public void Dispose()
	{
		if (_ownsContainer)
		{
			_container.Dispose();
		}
	}
}
