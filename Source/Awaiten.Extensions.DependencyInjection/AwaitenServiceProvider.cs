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
	///     Answered from the container's registration metadata, without constructing anything. Reports the
	///     shapes <see cref="GetService" /> serves: the provider's own services, an advertised registration,
	///     the <c>Task&lt;T&gt;</c> projection of an async-only registration, and a collection over an
	///     advertised element type. A keyed registration is not reported here — ask
	///     <see cref="IsKeyedService" /> — mirroring how <see cref="GetService" /> resolves only the unkeyed one.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public bool IsService(Type serviceType)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		return IsProviderService(serviceType) || IsAdvertisedShape(serviceType, null);
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

		return !ReferenceEquals(serviceKey, KeyedService.AnyKey) && IsAdvertisedShape(serviceType, serviceKey);
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
	///     Whether the container advertises a resolution for <paramref name="serviceType" /> under
	///     <paramref name="key" />, in any of the shapes the resolution methods above serve.
	/// </summary>
	/// <remarks>
	///     <para>
	///         A relationship shape (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>, <c>Owned&lt;T&gt;</c>) reports
	///         <see langword="false" /> even though the container resolves it, because it is not advertised as a
	///         registration. That matches what MS.DI answers — it has no such shapes — so a framework calibrated
	///         against MS.DI is told what it expects.
	///     </para>
	///     <para>
	///         This reports whether the service exists, not whether resolving it from <em>this</em> scope will
	///         succeed: a scoped service reports <see langword="true" /> on the root, as does a disposable
	///         transient whose bare type the container withholds there under the strict lifetime default.
	///     </para>
	/// </remarks>
	private bool IsAdvertisedShape(Type serviceType, object? key)
	{
		if (_metadata is null)
		{
			return false;
		}

		if (HasSyncRegistration(serviceType, key))
		{
			return true;
		}

		// A single-rank array is a collection shape; the container synthesizes it alongside the interfaces.
		if (serviceType.IsArray)
		{
			return key is null && serviceType.GetArrayRank() == 1
			                   && SynthesizesCollection(serviceType.GetElementType()!, requireSyncMember: true);
		}

		if (!serviceType.IsConstructedGenericType)
		{
			return false;
		}

		Type definition = serviceType.GetGenericTypeDefinition();
		Type[] arguments = serviceType.GenericTypeArguments;

		if (definition == typeof(Task<>))
		{
			// Either an async-only registration served through ResolveAsync, or an awaited view over a
			// collection or keyed dictionary.
			return AsyncConverterFor(arguments[0], key) is not null
			       || (key is null && SynthesizesAwaitedShape(arguments[0]));
		}

		if (key is not null)
		{
			return false;
		}

		if (IsCollectionDefinition(definition))
		{
			return SynthesizesCollection(arguments[0], requireSyncMember: true);
		}

		// Synthesized under the same conditions as the synchronous shapes, and kept out of
		// IsCollectionDefinition because it is neither an awaited shape nor one that suppresses the others.
		// It does claim its own slot though: an explicitly registered one steps the synthesized view aside, and
		// when that registration is async-only it has no synchronous path either.
		if (definition == typeof(IAsyncEnumerable<>))
		{
			return AsyncConverterFor(serviceType, null) is null
			       && SynthesizesCollection(arguments[0], requireSyncMember: true);
		}

		// A keyed-dictionary view over the registrations of the element type that carry a key of this type.
		return definition == typeof(IReadOnlyDictionary<,>)
		       && SynthesizesKeyedDictionary(arguments[0], arguments[1], requireSyncMembers: true);
	}

	/// <summary>
	///     Whether the container serves <paramref name="awaited" /> as the payload of a <c>Task&lt;&gt;</c>: an
	///     awaited collection or keyed-dictionary view.
	/// </summary>
	private bool SynthesizesAwaitedShape(Type awaited)
	{
		if (awaited.IsArray)
		{
			return awaited.GetArrayRank() == 1
			       && SynthesizesCollection(awaited.GetElementType()!, requireSyncMember: false);
		}

		if (!awaited.IsConstructedGenericType)
		{
			return false;
		}

		Type definition = awaited.GetGenericTypeDefinition();
		Type[] arguments = awaited.GenericTypeArguments;
		if (IsCollectionDefinition(definition))
		{
			return SynthesizesCollection(arguments[0], requireSyncMember: false);
		}

		return definition == typeof(IReadOnlyDictionary<,>)
		       && SynthesizesKeyedDictionary(arguments[0], arguments[1], requireSyncMembers: false);
	}

	/// <summary>
	///     Whether the container synthesizes a collection over <paramref name="elementType" />: it needs an
	///     unkeyed registration of that type — a synchronously resolvable one when
	///     <paramref name="requireSyncMember" /> is set, as the synchronous collection shapes need — and no
	///     explicitly registered collection shape of it, since such a registration replaces the synthesized
	///     shapes entirely.
	/// </summary>
	/// <remarks>
	///     One case here cannot be made exact, and the reason is worth recording. The container synthesizes the
	///     synchronous shapes only when <em>every</em> member can be materialized synchronously, but
	///     <c>Registrations</c> coalesces the implementations of one service type into a single advertised entry,
	///     so the members cannot be counted. A container with one synchronous implementation and a container with
	///     that same one plus an async-initialized sibling advertise identical metadata and resolve differently.
	///     Both report <see langword="true" />, which is right for the common case and, for the other, surfaces as
	///     a named <see cref="InvalidOperationException" /> on resolution rather than a silently misbound
	///     parameter. Answering it exactly needs the container to advertise the shapes it dispatches — which it
	///     knows at compile time — instead of the bridge inferring them from registrations.
	/// </remarks>
	private bool SynthesizesCollection(Type elementType, bool requireSyncMember)
	{
		bool anyMember = false;
		IReadOnlyList<AwaitenRegistration> registrations = _metadata!.Registrations;
		for (int index = 0; index < registrations.Count; index++)
		{
			AwaitenRegistration registration = registrations[index];
			if (registration.Key is not null)
			{
				continue;
			}

			if (registration.ServiceType == elementType)
			{
				anyMember |= !requireSyncMember || !registration.RequiresAsync;
			}
			else if (IsCollectionOf(registration.ServiceType, elementType))
			{
				return false;
			}
		}

		return anyMember;
	}

	/// <summary>
	///     Whether the container synthesizes an <c>IReadOnlyDictionary&lt;TKey, TValue&gt;</c> over the keyed
	///     registrations of <paramref name="valueType" />. It needs at least one of them, every one keyed by
	///     exactly <paramref name="keyType" /> — which has to be a <see cref="string" /> or an enum, the only key
	///     kinds a dictionary is emitted for — no explicitly registered dictionary of the same shape, and, for the
	///     synchronous shape, every member synchronously resolvable.
	/// </summary>
	/// <remarks>
	///     The key type must match exactly rather than merely be assignable: a dictionary is emitted per key kind,
	///     so <c>IReadOnlyDictionary&lt;object, T&gt;</c> over string-keyed registrations is not a service even
	///     though every key is an <see cref="object" />. Registrations keyed under more than one kind get no
	///     dictionary at all. Unlike the collection members, keyed registrations are advertised one per key and
	///     never coalesced, so all of this is exact.
	/// </remarks>
	private bool SynthesizesKeyedDictionary(Type keyType, Type valueType, bool requireSyncMembers)
	{
		if (keyType != typeof(string) && !keyType.IsEnum)
		{
			return false;
		}

		bool anyMember = false;
		IReadOnlyList<AwaitenRegistration> registrations = _metadata!.Registrations;
		for (int index = 0; index < registrations.Count; index++)
		{
			AwaitenRegistration registration = registrations[index];
			if (registration.ServiceType == valueType && registration.Key is not null)
			{
				if (registration.Key.GetType() != keyType || (requireSyncMembers && registration.RequiresAsync))
				{
					return false;
				}

				anyMember = true;
			}
			else if (registration.Key is null
			         && IsKeyedDictionaryOf(registration.ServiceType, keyType, valueType))
			{
				// Only an unkeyed registration of the dictionary shape replaces the synthesized one, exactly as
				// for the collection shapes: a keyed one is reached under its key and suppresses nothing.
				return false;
			}
		}

		return anyMember;
	}

	/// <summary>Whether <paramref name="candidate" /> is the keyed-dictionary shape being asked about.</summary>
	private static bool IsKeyedDictionaryOf(Type candidate, Type keyType, Type valueType)
		=> candidate.IsConstructedGenericType
		   && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
		   && candidate.GenericTypeArguments[0] == keyType
		   && candidate.GenericTypeArguments[1] == valueType;

	/// <summary>Whether <paramref name="candidate" /> is one of the collection shapes over <paramref name="elementType" />.</summary>
	private static bool IsCollectionOf(Type candidate, Type elementType)
	{
		if (candidate.IsArray)
		{
			return candidate.GetArrayRank() == 1 && candidate.GetElementType() == elementType;
		}

		return candidate.IsConstructedGenericType
		       && IsCollectionDefinition(candidate.GetGenericTypeDefinition())
		       && candidate.GenericTypeArguments[0] == elementType;
	}

	/// <summary>The collection interfaces the container synthesizes over a registered element type.</summary>
	private static bool IsCollectionDefinition(Type definition)
		=> definition == typeof(IEnumerable<>)
		   || definition == typeof(IReadOnlyList<>)
		   || definition == typeof(IReadOnlyCollection<>)
		   || definition == typeof(IList<>)
		   || definition == typeof(ICollection<>);

	/// <summary>
	///     Whether the metadata carries a <em>synchronously resolvable</em> registration of
	///     <paramref name="serviceType" /> under <paramref name="key" />.
	/// </summary>
	/// <remarks>
	///     An async-only registration is advertised too, but it has no synchronous path: the bare service type
	///     is not resolvable, only its <c>Task&lt;T&gt;</c> projection. Counting it here would report a service
	///     that <see cref="GetService" /> then answers with <see langword="null" />. (A container that opted into
	///     <c>SyncResolveAfterInit</c> advertises such a registration as synchronous, and it is one.)
	/// </remarks>
	private bool HasSyncRegistration(Type serviceType, object? key)
	{
		IReadOnlyList<AwaitenRegistration> registrations = _metadata!.Registrations;
		for (int index = 0; index < registrations.Count; index++)
		{
			AwaitenRegistration registration = registrations[index];
			if (!registration.RequiresAsync && registration.ServiceType == serviceType
			                                && Equals(registration.Key, key))
			{
				return true;
			}
		}

		return false;
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
