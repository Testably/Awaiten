using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts an <see cref="IAwaitenScope" /> to the Microsoft.Extensions.DependencyInjection resolution
///     surface, so an Awaiten container can be consumed as an <see cref="IServiceProvider" /> (and used to
///     create <see cref="IServiceScope">scopes</see>). <see cref="GetService" /> maps to the container's
///     <see cref="IAwaitenResolver.TryResolve" />, returning <see langword="null" /> for an unregistered
///     service as <see cref="IServiceProvider" /> requires. A service that requires asynchronous resolution
///     (advertised through <see cref="IAwaitenContainerMetadata" />) is served as a <c>Task&lt;T&gt;</c>
///     (request <c>Task&lt;TService&gt;</c> and await it), mirroring the collection projection.
/// </summary>
/// <remarks>
///     Awaiten owns the lifetime and disposal of the services: disposing the provider disposes the
///     underlying container (and the singletons it created), and disposing a scope disposes the
///     <see cref="IAwaitenScope" /> behind it. Prefer <c>await using</c> (<see cref="DisposeAsync" />) when
///     the container tracks asynchronously disposable instances.
/// </remarks>
public sealed class AwaitenServiceProvider : IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
	private readonly IAwaitenScope _container;
	private readonly bool _ownsContainer;

	// The container's registration metadata (implemented by the generated Root), flowed into scope providers
	// so async services stay resolvable as Task<T> from scopes too. Null when the scope offers no metadata.
	private readonly IAwaitenContainerMetadata? _metadata;

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
		_metadata = container as IAwaitenContainerMetadata;
	}

	private AwaitenServiceProvider(IAwaitenScope scope, IAwaitenContainerMetadata? metadata)
	{
		_container = scope;
		_ownsContainer = true;
		_metadata = metadata;
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
		                                            || serviceType == typeof(AwaitenServiceProvider))
		{
			return this;
		}

		if (_container.TryResolve(serviceType, out object? instance))
		{
			return instance;
		}

		// An async-tainted service has no synchronous resolution path; serve it as Task<T> through
		// ResolveAsync when the container's metadata advertises it, mirroring the collection projection.
		if (_metadata is not null
		    && serviceType.IsConstructedGenericType
		    && serviceType.GetGenericTypeDefinition() == typeof(Task<>))
		{
			Type innerType = serviceType.GenericTypeArguments[0];
			Func<Task<object>, object>? asTypedTask = AsyncConverterFor(innerType);
			if (asTypedTask is not null)
			{
				return asTypedTask(_container.ResolveAsync(innerType));
			}
		}

		return null;
	}

	/// <inheritdoc />
	public IServiceScope CreateScope()
		=> new AwaitenServiceScope(new AwaitenServiceProvider(_container.CreateScope(), _metadata));

	/// <inheritdoc />
	public void Dispose()
	{
		if (_ownsContainer)
		{
			_container.Dispose();
		}
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (!_ownsContainer)
		{
			return default;
		}

		// The generated scope implements IAsyncDisposable concretely (it is not on IAwaitenScope), so async
		// disposal is preferred when available and falls back to the synchronous path otherwise.
		if (_container is IAsyncDisposable asyncDisposable)
		{
			return asyncDisposable.DisposeAsync();
		}

		_container.Dispose();
		return default;
	}

	// The generator-emitted Task<object>->Task<T> converter for an async-advertised service type, or null when
	// the type is not an async registration. Reflection-free: the closed converter is carried by the metadata.
	private Func<Task<object>, object>? AsyncConverterFor(Type serviceType)
	{
		IReadOnlyList<AwaitenRegistration> registrations = _metadata!.Registrations;
		for (int index = 0; index < registrations.Count; index++)
		{
			AwaitenRegistration registration = registrations[index];
			if (registration.ServiceType == serviceType && registration.RequiresAsync)
			{
				return registration.AsyncTaskConverter;
			}
		}

		return null;
	}
}
