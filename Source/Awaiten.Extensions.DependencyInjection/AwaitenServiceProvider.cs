using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts an <see cref="IAwaitenScope" /> to the Microsoft.Extensions.DependencyInjection resolution
///     surface, so an Awaiten container can be consumed as an <see cref="IServiceProvider" /> (and used to
///     create <see cref="IServiceScope">scopes</see>). <see cref="GetService" /> maps to the container's
///     <see cref="IAwaitenResolver.TryResolve(Type, out object)" />, returning <see langword="null" /> for an unregistered
///     service as <see cref="IServiceProvider" /> requires. A service that requires asynchronous resolution
///     (advertised through <see cref="IAwaitenContainerMetadata" />) is served as a <c>Task&lt;T&gt;</c>
///     (request <c>Task&lt;TService&gt;</c> and await it), mirroring the collection projection.
/// </summary>
/// <remarks>
///     <para>
///         Awaiten owns the lifetime and disposal of the services: disposing the provider disposes the
///         underlying container (and the singletons it created), and disposing a scope disposes the
///         <see cref="IAwaitenScope" /> behind it. Prefer <c>await using</c> (<see cref="DisposeAsync" />) when
///         the container tracks asynchronously disposable instances.
///     </para>
///     <para>
///         The provider also answers <see cref="IServiceProviderIsService" /> and
///         <see cref="IServiceProviderIsKeyedService" /> from the container's registration metadata, which the
///         ASP.NET Core stack depends on: minimal APIs consult it to tell a parameter that comes from
///         dependency injection from one that comes from the request, and MVC's controller activation does the
///         same. Without it those stacks fall back to their own heuristics and bind such a parameter wrongly
///         rather than failing.
///     </para>
/// </remarks>
[SuppressMessage("Awaiten", "AWT135:Service locator: a resolver interface is injected into a service", Justification = "This is the MS.DI bridge adapter: holding the IAwaitenScope is the adaptation itself, not a hidden run-time dependency.")]
public sealed class AwaitenServiceProvider : IKeyedServiceProvider, IServiceScopeFactory,
	IServiceProviderIsKeyedService, IDisposable, IAsyncDisposable
{
	private readonly IAwaitenScope _container;
	private readonly bool _ownsContainer;

	/// <summary>
	///     The container's registration metadata (implemented by the generated Root), flowed into scope providers
	///     so async services stay resolvable as <c>Task&lt;T&gt;</c> from scopes too. Null when the scope offers no
	///     metadata.
	/// </summary>
	private readonly IAwaitenContainerMetadata? _metadata;

	/// <summary>Adapts the given container to <see cref="IServiceProvider" />.</summary>
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

		if (IsProviderService(serviceType))
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

		// The container has this service but declined to build it here: a disposable transient asked on the root
		// under the strict lifetime default, whose every resolution would accumulate there for the container's
		// lifetime. Resolve turns that into the guidance naming the fix, without constructing anything, which is
		// what MS.DI does for a scoping violation too. Returning null would instead let a host bind the parameter
		// from somewhere else and fail far from the cause.
		if (_metadata is not null && _metadata.IsResolvable(serviceType, null))
		{
			return _container.Resolve(serviceType);
		}

		return null;
	}

	/// <inheritdoc />
	/// <remarks>
	///     A <see langword="null" /> <paramref name="serviceKey" /> resolves the unkeyed registration, exactly like
	///     <see cref="GetService" />. <see cref="KeyedService.AnyKey" /> is declined (Awaiten has no wildcard-key
	///     semantics), returning <see langword="null" />. An async-tainted keyed service is served as a
	///     <c>Task&lt;T&gt;</c> under the same key, mirroring the unkeyed projection.
	/// </remarks>
	public object? GetKeyedService(Type serviceType, object? serviceKey)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		if (serviceKey is null)
		{
			return GetService(serviceType);
		}

		// Awaiten resolves an exact user-declared [Key]; it has no wildcard-key registration, so AnyKey matches
		// nothing and is declined rather than resolving an arbitrary keyed instance.
		if (ReferenceEquals(serviceKey, KeyedService.AnyKey))
		{
			return null;
		}

		if (_container.TryResolve(serviceType, serviceKey, out object? instance))
		{
			return instance;
		}

		// An async-tainted keyed service has no synchronous path; serve it as Task<T> through the keyed ResolveAsync
		// when the container's metadata advertises it under this key, mirroring the unkeyed Task<T> projection.
		if (_metadata is not null
		    && serviceType.IsConstructedGenericType
		    && serviceType.GetGenericTypeDefinition() == typeof(Task<>))
		{
			Type innerType = serviceType.GenericTypeArguments[0];
			Func<Task<object>, object>? asTypedTask = AsyncConverterFor(innerType, serviceKey);
			if (asTypedTask is not null)
			{
				return asTypedTask(_container.ResolveAsync(innerType, serviceKey));
			}
		}

		return null;
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">No service is registered for <paramref name="serviceType" /> under <paramref name="serviceKey" />.</exception>
	public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
	{
		object? service = GetKeyedService(serviceType, serviceKey);
		if (service is null)
		{
			throw new InvalidOperationException(
				$"No service for type '{serviceType}' with key '{serviceKey}' has been registered.");
		}

		return service;
	}

	/// <inheritdoc />
	/// <remarks>
	///     Delegated to <see cref="IAwaitenContainerMetadata.IsResolvable" />, so it covers every shape the
	///     container dispatches rather than a re-derivation of the generator's synthesis rules: a registration, the
	///     relationship shapes over it, the synthesized collections and keyed dictionaries, the awaited views over
	///     those, and a variance-compatible closing. The container's own answer cannot drift from what
	///     <see cref="GetService" /> does, because both read the same tables. A keyed registration is not reported
	///     here, mirroring how <see cref="GetService" /> resolves only the unkeyed one; ask
	///     <see cref="IsKeyedService" /> for those.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public bool IsService(Type serviceType)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		return IsProviderService(serviceType) || IsResolvableShape(serviceType, null);
	}

	/// <inheritdoc />
	/// <remarks>
	///     A <see langword="null" /> <paramref name="serviceKey" /> asks about the unkeyed registration, exactly
	///     like <see cref="IsService" />. <see cref="KeyedService.AnyKey" /> reports <see langword="false" />,
	///     because <see cref="GetKeyedService" /> declines it: Awaiten has no wildcard-key semantics.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public bool IsKeyedService(Type serviceType, object? serviceKey)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		if (serviceKey is null)
		{
			return IsService(serviceType);
		}

		return !ReferenceEquals(serviceKey, KeyedService.AnyKey) && IsResolvableShape(serviceType, serviceKey);
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

	/// <summary>
	///     Whether <paramref name="serviceType" /> is one of the provider's own services, which
	///     <see cref="GetService" /> answers with itself rather than from the container.
	/// </summary>
	/// <remarks>
	///     The two probe interfaces are only offered when the container carries registration metadata. A bare
	///     generated <c>Scope</c> (as opposed to a <c>Root</c>) does not, and a probe that answered "not a
	///     service" to everything would be worse than none at all: a framework would misbind every parameter
	///     instead of falling back to its own heuristics.
	/// </remarks>
	private bool IsProviderService(Type serviceType)
		=> serviceType == typeof(IServiceProvider)
		   || serviceType == typeof(IServiceScopeFactory)
		   || serviceType == typeof(AwaitenServiceProvider)
		   || (_metadata is not null
		       && (serviceType == typeof(IServiceProviderIsService)
		           || serviceType == typeof(IServiceProviderIsKeyedService)));

	/// <summary>
	///     Whether the container can resolve <paramref name="serviceType" /> under <paramref name="key" />, or the
	///     bridge can serve it as the <c>Task&lt;T&gt;</c> projection of an async-only registration. Those are the
	///     two paths <see cref="GetService" /> and <see cref="GetKeyedService" /> take, in the same order.
	/// </summary>
	private bool IsResolvableShape(Type serviceType, object? key)
	{
		if (_metadata is null)
		{
			return false;
		}

		if (_metadata.IsResolvable(serviceType, key))
		{
			return true;
		}

		// The container withholds an async-only service from synchronous resolution, so the bridge serves it as a
		// Task<T> built from the registration metadata. That projection is the bridge's own, so it is answered
		// here rather than by the container.
		return serviceType.IsConstructedGenericType
		       && serviceType.GetGenericTypeDefinition() == typeof(Task<>)
		       && AsyncConverterFor(serviceType.GenericTypeArguments[0], key) is not null;
	}

	/// <summary>
	///     The generator-emitted <c>Task&lt;object&gt;</c> to <c>Task&lt;T&gt;</c> converter for an unkeyed
	///     async-advertised service type, or null when the type is not an async registration.
	/// </summary>
	private Func<Task<object>, object>? AsyncConverterFor(Type serviceType) => AsyncConverterFor(serviceType, null);

	/// <summary>
	///     The generator-emitted <c>Task&lt;object&gt;</c> to <c>Task&lt;T&gt;</c> converter for the async-advertised
	///     registration of <paramref name="serviceType" /> under <paramref name="key" />, or null when there is no
	///     such async registration. Reflection-free: the closed converter is carried by the metadata.
	/// </summary>
	private Func<Task<object>, object>? AsyncConverterFor(Type serviceType, object? key)
	{
		IReadOnlyList<AwaitenRegistration> registrations = _metadata!.Registrations;
		for (int index = 0; index < registrations.Count; index++)
		{
			AwaitenRegistration registration = registrations[index];
			if (registration.ServiceType == serviceType && registration.RequiresAsync && Equals(registration.Key, key))
			{
				return registration.AsyncTaskConverter;
			}
		}

		return null;
	}
}
