using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Resolving an imported service directly from a generated <c>Root</c> or <c>Scope</c>: a typed, by-type or keyed
///     resolution of a type the container imports asks the <see cref="IExternalResolver" />, exactly as an injected
///     dependency of that type does, and leaves the instance to the host.
/// </summary>
public sealed partial class ExternalServiceResolveTests
{
	public interface IClock;

	public interface ILog<T>;

	public interface IUnknown;

	public sealed class Clock : IClock;

	public sealed class DisposableClock : IClock, IDisposable
	{
		public bool IsDisposed { get; private set; }

		public void Dispose() => IsDisposed = true;
	}

	public sealed class Log<T> : ILog<T>;

	public sealed class Reporter(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public sealed class KeyedReporter([FromKey("utc")] IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public sealed class LoggingReporter(ILog<LoggingReporter> log)
	{
		public ILog<LoggingReporter> Log { get; } = log;
	}

	public interface IHandler<in T>;

	public sealed class Order;

	public sealed class AnyHandler : IHandler<object>;

	public sealed class OrderHandler : IHandler<Order>;

	public sealed class OrderDesk(IHandler<Order> handler)
	{
		public IHandler<Order> Handler { get; } = handler;
	}

	public sealed class SlowService : IInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	[Container]
	[ImportService<IClock>]
	[Transient<Reporter>]
	[Transient<KeyedReporter>]
	public static partial class ImportingContainer;

	[Container]
	[ImportService(typeof(ILog<>))]
	[Transient<LoggingReporter>]
	public static partial class OpenImportingContainer;

	[Container(Extends = typeof(ImportingContainer))]
	public static partial class ExtendingContainer;

	[Container]
	[ImportService<IHandler<Order>>]
	[Singleton<AnyHandler, IHandler<object>>]
	[Transient<OrderDesk>]
	public static partial class VariantImportingContainer;

	[Container]
	[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
	[ImportServices]
	[Transient<Reporter>]
	[Singleton<SlowService>]
	public static partial class ImportingEverythingContainer;

	private sealed class HostResolver(Func<Type, object?, object?> provide) : IExternalResolver
	{
		public List<(Type Type, object? Key)> Requests { get; } = new();

		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			Requests.Add((serviceType, key));
			instance = provide(serviceType, key);
			return instance is not null;
		}
	}

	private sealed class ThrowingResolver : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
			=> throw new NotSupportedException("the host failed");
	}

	public sealed class ImportService
	{
		[Test]
		public async Task Resolve_ReturnsTheHostsInstance()
		{
			Clock clock = new();
			using ImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));

			IClock resolved = root.Resolve<IClock>();

			await That(resolved).IsSameAs(clock)
				.Because("a typed Resolve<T>() of an imported type asks the host, as an injected dependency of that type does");
		}

		[Test]
		public async Task ResolveAsync_ReturnsTheHostsInstance()
		{
			Clock clock = new();
			using ImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));

			IClock resolved = await root.ResolveAsync<IClock>();

			await That(resolved).IsSameAs(clock)
				.Because("the asynchronous resolution of an imported type asks the host too");
		}

		[Test]
		public async Task ResolveByType_ReturnsTheHostsInstance()
		{
			Clock clock = new();
			using ImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));

			object resolved = root.Resolve(typeof(IClock));
			object resolvedAsync = await root.ResolveAsync(typeof(IClock));

			await That(resolved).IsSameAs(clock)
				.Because("Resolve(Type) shares the dispatch the typed Resolve<T>() falls back to");
			await That(resolvedAsync).IsSameAs(clock)
				.Because("ResolveAsync(Type) shares it as well");
		}

		[Test]
		public async Task TryResolve_ReturnsTheHostsInstance()
		{
			Clock clock = new();
			using ImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));

			bool resolved = root.TryResolve(out IClock? instance);

			await That(resolved).IsTrue()
				.Because("TryResolve reaches the host like Resolve");
			await That(instance).IsSameAs(clock);
		}

		[Test]
		public async Task TryResolve_WhenTheHostLacksIt_ReturnsFalse()
		{
			using ImportingContainer.Root root = new(new HostResolver((_, _) => null));

			bool resolved = root.TryResolve(out IClock? instance);

			await That(resolved).IsFalse()
				.Because("TryResolve reports absence instead of throwing");
			await That(instance).IsNull();
		}

		[Test]
		public async Task KeyedResolve_ForwardsTheKeyToTheHost()
		{
			Clock utc = new();
			using ImportingContainer.Root root = new(new HostResolver((type, key) => type == typeof(IClock) && Equals(key, "utc") ? utc : null));

			IClock resolved = root.Resolve<IClock>("utc");

			await That(resolved).IsSameAs(utc)
				.Because("a keyed resolution of an imported type forwards its key, as [FromKey] injection does");
		}

		[Test]
		public async Task ChildScope_AsksTheRootsResolver()
		{
			Clock clock = new();
			using ImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));
			using ImportingContainer.Scope scope = root.CreateScope();

			await That(scope.Resolve<IClock>()).IsSameAs(clock)
				.Because("a child scope without a resolver of its own falls back to the root's, as injection does");
		}

		[Test]
		public async Task ChildScope_PrefersItsOwnResolver()
		{
			Clock rootClock = new();
			Clock scopeClock = new();
			using ImportingContainer.Root root = new(new HostResolver((_, _) => rootClock));
			using ImportingContainer.Scope scope = root.CreateScope();
			((IAwaitenExternalResolverHost)scope).SetExternalResolver(new HostResolver((_, _) => scopeClock));

			await That(scope.Resolve<IClock>()).IsSameAs(scopeClock)
				.Because("a host wires each scope to its aligned provider, so the scope's own resolver wins");
		}

		[Test]
		public async Task Resolve_DoesNotCacheTheHostsInstance()
		{
			HostResolver host = new((type, _) => type == typeof(IClock) ? new Clock() : null);
			using ImportingContainer.Root root = new(host);

			IClock first = root.Resolve<IClock>();
			IClock second = root.Resolve<IClock>();

			await That(first).IsNotSameAs(second)
				.Because("the host decides the lifetime of its instance, so the container asks it every time");
			await That(host.Requests).HasCount(2);
		}

		[Test]
		public async Task Dispose_DoesNotDisposeTheHostsInstance()
		{
			DisposableClock clock = new();
			using (ImportingContainer.Root root = new(new HostResolver((_, _) => clock)))
			{
				using (ImportingContainer.Scope scope = root.CreateScope())
				{
					_ = scope.Resolve<IClock>();
				}

				_ = root.Resolve<IClock>();
			}

			await That(clock.IsDisposed).IsFalse()
				.Because("the host owns an imported instance, so neither the scope nor the root disposes it");
		}

		[Test]
		public async Task Resolve_WithoutAResolver_ThrowsTheExternalGuidance()
		{
			using ImportingContainer.Root root = new();

			await That(() => root.Resolve<IClock>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IClock*is not available*IExternalResolver*").AsWildcard()
				.Because("a missing provider gets the guidance an injected dependency of that type gets");
		}

		[Test]
		public async Task Resolve_WhenTheHostLacksIt_ThrowsTheExternalGuidanceAfterAskingOnce()
		{
			HostResolver host = new((_, _) => null);
			using ImportingContainer.Root root = new(host);

			await That(() => root.Resolve<IClock>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IClock*is not available*register it in the host provider*").AsWildcard()
				.Because("a host without the service gets the guidance an injected dependency of that type gets");
			await That(host.Requests).HasCount(1)
				.Because("the failed resolution asks the host once");
		}

		[Test]
		public async Task KeyedResolve_WhenTheHostLacksIt_NamesTheKey()
		{
			using ImportingContainer.Root root = new(new HostResolver((_, _) => null));

			await That(() => root.Resolve<IClock>("utc")).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IClock*(key: utc)*is not available*").AsWildcard();
		}

		[Test]
		public async Task Resolve_WhenTheHostThrows_PropagatesItsException()
		{
			using ImportingContainer.Root root = new(new ThrowingResolver());

			await That(() => root.Resolve<IClock>()).Throws<NotSupportedException>()
				.WithMessage("the host failed")
				.Because("the host's own failure surfaces unchanged, as it does for an injected dependency");
		}

		[Test]
		public async Task Resolve_OfAnImportInheritedThroughExtends_ReturnsTheHostsInstance()
		{
			Clock clock = new();
			using ExtendingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));

			await That(root.Resolve<IClock>()).IsSameAs(clock)
				.Because("a container inherits the imports of the container it extends");
		}

		[Test]
		public async Task Resolve_OfAnImportAVariantRegistrationCouldServe_AsksTheHost()
		{
			OrderHandler handler = new();
			using VariantImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IHandler<Order>) ? handler : null));

			await That(root.Resolve<IHandler<Order>>()).IsSameAs(handler)
				.Because("injection routes a declared import to the host ahead of a variance-compatible registration, so resolution does too");
			await That(root.Resolve<OrderDesk>().Handler).IsSameAs(handler);
		}

		[Test]
		public async Task Resolve_OfATypeTheContainerDoesNotImport_StillThrowsNoRegistration()
		{
			HostResolver host = new((_, _) => new Clock());
			using ImportingContainer.Root root = new(host);

			await That(() => root.Resolve<IUnknown>()).Throws<InvalidOperationException>()
				.WithMessage("No registration for type*IUnknown*").AsWildcard()
				.Because("only an imported type goes to the host");
			await That(host.Requests).IsEmpty();
		}
	}

	public sealed class OpenImportService
	{
		[Test]
		public async Task Resolve_OfAClosingTheGraphConsumes_ReturnsTheHostsInstance()
		{
			Log<LoggingReporter> log = new();
			using OpenImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(ILog<LoggingReporter>) ? log : null));

			await That(root.Resolve<ILog<LoggingReporter>>()).IsSameAs(log)
				.Because("a closing of an open import is routed to the host");
		}

		[Test]
		public async Task Resolve_OfAnyOtherClosing_ReturnsTheHostsInstance()
		{
			Log<string> log = new();
			using OpenImportingContainer.Root root = new(new HostResolver((type, _) => type == typeof(ILog<string>) ? log : null));

			await That(root.Resolve<ILog<string>>()).IsSameAs(log)
				.Because("an open import routes every closing to the host, also one no dependency consumes");
			await That(await root.ResolveAsync<ILog<string>>()).IsSameAs(log);
		}

		[Test]
		public async Task Resolve_OfAnUnrelatedGenericType_StillThrowsNoRegistration()
		{
			HostResolver host = new((_, _) => new Log<string>());
			using OpenImportingContainer.Root root = new(host);

			await That(() => root.Resolve<List<string>>()).Throws<InvalidOperationException>()
				.WithMessage("No registration for type*").AsWildcard();
			await That(host.Requests).IsEmpty()
				.Because("only a closing of the imported definition goes to the host");
		}
	}

	public sealed class ImportServices
	{
		[Test]
		public async Task Resolve_OfAnUnregisteredType_ReturnsTheHostsInstance()
		{
			Clock clock = new();
			using ImportingEverythingContainer.Root root = new(new HostResolver((type, _) => type == typeof(IClock) ? clock : null));

			await That(root.Resolve<IClock>()).IsSameAs(clock)
				.Because("[ImportServices] sends every unregistered type to the host");
			await That(await root.ResolveAsync<IClock>()).IsSameAs(clock);
			await That(root.Resolve(typeof(IClock))).IsSameAs(clock);
		}

		[Test]
		public async Task TryResolve_WhenTheHostLacksIt_ReturnsFalse()
		{
			using ImportingEverythingContainer.Root root = new(new HostResolver((_, _) => null));

			await That(root.TryResolve(out IUnknown? _)).IsFalse();
		}

		[Test]
		public async Task Resolve_WhenTheHostLacksIt_ThrowsTheExternalGuidance()
		{
			using ImportingEverythingContainer.Root root = new(new HostResolver((_, _) => null));

			await That(() => root.Resolve<IUnknown>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IUnknown*is not available*").AsWildcard();
		}

		[Test]
		public async Task Resolve_OfARegisteredType_DoesNotAskTheHost()
		{
			HostResolver host = new((_, _) => new Clock());
			using ImportingEverythingContainer.Root root = new(host);

			_ = root.Resolve<Reporter>();

			await That(host.Requests).HasSingle().Which.IsEqualTo((typeof(IClock), null))
				.Because("only the unregistered IClock dependency goes to the host, never the registered Reporter");
		}

		[Test]
		public async Task Resolve_OfAnAsyncOnlyService_KeepsItsGuidance()
		{
			HostResolver host = new((_, _) => new SlowService());
			using ImportingEverythingContainer.Root root = new(host);

			await That(() => root.Resolve<SlowService>()).Throws<InvalidOperationException>()
				.WithMessage("*ResolveAsync*").AsWildcard()
				.Because("a registered async-only service is the container's, so it is not handed to the host");
			await That(host.Requests).IsEmpty();
		}

		[Test]
		public async Task KeyedResolve_IsNotSentToTheHost()
		{
			HostResolver host = new((_, _) => new Clock());
			using ImportingEverythingContainer.Root root = new(host);

			await That(() => root.Resolve<IClock>("utc")).Throws<InvalidOperationException>()
				.WithMessage("No registration for type*IClock*utc*").AsWildcard()
				.Because("[ImportServices] routes only unkeyed dependencies, so a keyed resolution is not routed either");
			await That(host.Requests).IsEmpty();
		}
	}

	public sealed class Resolvability
	{
		[Test]
		public async Task OfADeclaredImport_IsExternal()
		{
			using ImportingContainer.Root root = new();

			await That(root.GetResolvability<IClock>().Status).IsEqualTo(AwaitenResolvabilityStatus.External)
				.Because("resolving a declared import asks the host");
			await That(root.GetResolvability(typeof(IClock), "utc").Status).IsEqualTo(AwaitenResolvabilityStatus.External)
				.Because("a declared import is routed with a key too");
			await That(root.GetResolvability<Reporter>().Status).IsEqualTo(AwaitenResolvabilityStatus.Resolvable);
			await That(root.GetResolvability<IUnknown>().Status).IsEqualTo(AwaitenResolvabilityStatus.NotRegistered)
				.Because("a type that is neither registered nor imported is not routed");
		}

		[Test]
		public async Task OfAClosingOfAnOpenImport_IsExternal()
		{
			using OpenImportingContainer.Root root = new();

			await That(root.GetResolvability<ILog<Order>>().Status).IsEqualTo(AwaitenResolvabilityStatus.External)
				.Because("every closing of an open import is routed, also one the graph does not consume");
		}

		[Test]
		public async Task OfADeclaredImportAVariantRegistrationCouldServe_IsExternal()
		{
			using VariantImportingContainer.Root root = new();

			await That(root.GetResolvability<IHandler<Order>>().Status).IsEqualTo(AwaitenResolvabilityStatus.External)
				.Because("a declared import goes to the host ahead of the variance fallback");
		}

		[Test]
		public async Task UnderImportServices_FollowsTheRouting()
		{
			using ImportingEverythingContainer.Root root = new();

			await That(root.GetResolvability<IClock>().Status).IsEqualTo(AwaitenResolvabilityStatus.External)
				.Because("[ImportServices] routes an unregistered unkeyed type to the host");
			await That(root.GetResolvability(typeof(IClock), "utc").Status).IsEqualTo(AwaitenResolvabilityStatus.NotRegistered)
				.Because("[ImportServices] does not route a keyed resolution");
			await That(root.GetResolvability<SlowService>().Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly)
				.Because("a registered async-only service keeps its guidance");
		}
	}
}
