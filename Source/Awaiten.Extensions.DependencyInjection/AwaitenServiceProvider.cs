using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
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
	// Adapts ResolveAsync's Task<object> to the Task<T> a consumer asks for; closed and bound once per
	// requested service type, shared across providers (the conversion is type-specific, not container-specific).
	private static readonly MethodInfo AsTaskMethod =
		typeof(AwaitenServiceProvider).GetMethod(nameof(AsTask), BindingFlags.NonPublic | BindingFlags.Static)!;

	private static readonly ConcurrentDictionary<Type, Func<Task<object>, object>> TaskConverters = new();

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
			if (RequiresAsync(innerType))
			{
				return TaskConverterFor(innerType)(_container.ResolveAsync(innerType));
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

	private bool RequiresAsync(Type serviceType)
	{
		IReadOnlyList<AwaitenRegistration> registrations = _metadata!.Registrations;
		for (int index = 0; index < registrations.Count; index++)
		{
			if (registrations[index].ServiceType == serviceType)
			{
				return registrations[index].RequiresAsync;
			}
		}

		return false;
	}

	private static Func<Task<object>, object> TaskConverterFor(Type serviceType)
		=> TaskConverters.GetOrAdd(serviceType, static type =>
			(Func<Task<object>, object>)AsTaskMethod.MakeGenericMethod(type).CreateDelegate(typeof(Func<Task<object>, object>)));

	private static async Task<T> AsTask<T>(Task<object> resolution) => (T)await resolution.ConfigureAwait(false);
}
