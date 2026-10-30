using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of <c>[ImportService&lt;T&gt;]</c>: a declared external type is resolved from the wired
///     <see cref="IExternalResolver" /> rather than the Awaiten graph, whether it reaches a constructor parameter or
///     an <c>[Inject]</c> property, and a <c>[FromKey]</c> forwards its key to the resolver.
/// </summary>
public partial class ExternalServiceTests
{
	public interface IClock
	{
		string Now { get; }
	}

	public sealed class FixedClock : IClock
	{
		public string Now => "noon";
	}

	public sealed class UtcClock : IClock
	{
		public string Now => "utc-noon";
	}

	public sealed class Reporter
	{
		// No Awaiten registration for IClock: the container declares [ImportService<IClock>], so it routes here.
		public Reporter(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class InjectedReporter
	{
		[Inject]
		public IClock Clock { get; set; } = null!;
	}

	public sealed class KeyedReporter
	{
		public KeyedReporter([FromKey("utc")] IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class HookReporter
	{
		// No Awaiten registration for IClock: the activation hook's IClock parameter routes externally too.
		public IClock? Clock { get; set; }
	}

	[Container]
	[ImportService<IClock>]
	[Transient<Reporter>]
	[Transient<InjectedReporter>]
	[Transient<KeyedReporter>]
	public static partial class ExternalContainer;

	[Container]
	[ImportService<IClock>]
	[Singleton<HookReporter>(OnActivated = nameof(ApplyClock))]
	public static partial class HookExternalContainer
	{
		private static void ApplyClock(HookReporter reporter, IClock clock) => reporter.Clock = clock;
	}

	[Container]
	[ImportService<IClock>]
	[Singleton<Reporter>(Eager = true)]
	public static partial class EagerExternalContainer;

	private sealed class SingleClockResolver(IClock clock) : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = serviceType == typeof(IClock) ? clock : null;
			return instance is not null;
		}
	}

	private sealed class ClockResolver : IExternalResolver
	{
		private readonly Dictionary<object, IClock> _keyed = new()
		{
			["utc"] = new UtcClock(),
		};

		private readonly IClock _unkeyed = new FixedClock();

		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			if (serviceType == typeof(IClock))
			{
				instance = key is null ? _unkeyed : _keyed[key];
				return true;
			}

			instance = null;
			return false;
		}
	}

	[Test]
	public async Task ImportService_ConstructorDependency_ResolvesThroughTheExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IAwaitenExternalResolverHost)container).ExternalResolver = new ClockResolver();

		Reporter reporter = container.Resolve<Reporter>();

		await That(reporter.Clock).Is<FixedClock>()
			.Because("the unregistered [ImportService<IClock>] dependency is drawn from the external resolver");
	}

	[Test]
	public async Task ImportService_InjectedProperty_ResolvesThroughTheExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IAwaitenExternalResolverHost)container).ExternalResolver = new ClockResolver();

		InjectedReporter reporter = container.Resolve<InjectedReporter>();

		await That(reporter.Clock).Is<FixedClock>()
			.Because("[ImportService<T>] routes an [Inject] property of that type externally too");
	}

	[Test]
	public async Task ImportService_WithFromKey_ForwardsTheKeyToTheExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IAwaitenExternalResolverHost)container).ExternalResolver = new ClockResolver();

		KeyedReporter reporter = container.Resolve<KeyedReporter>();

		await That(reporter.Clock).Is<UtcClock>()
			.Because("[ImportService<T>] with [FromKey] forwards the key to the resolver");
	}

	[Test]
	public async Task ImportService_AdvertisesTheDeclaredExternalDependency()
	{
		using ExternalContainer.Root container = new();

		await That(((IAwaitenRoot)container).ExternalDependencies)
			.Contains(new AwaitenExternalDependency(typeof(IClock)))
			.Because("an unkeyed [ImportService<IClock>] consumer advertises the external dependency")
			.And.Contains(new AwaitenExternalDependency(typeof(IClock), "utc"))
			.Because("the keyed consumer advertises the forwarded key");
	}

	[Test]
	public async Task ImportService_LifecycleHookParameter_ResolvesThroughTheExternalResolver()
	{
		using HookExternalContainer.Root container = new();
		((IAwaitenExternalResolverHost)container).ExternalResolver = new ClockResolver();

		HookReporter reporter = container.Resolve<HookReporter>();

		await That(reporter.Clock).Is<FixedClock>()
			.Because("a lifecycle hook parameter of an [ImportService<T>] type is drawn from the external resolver");
	}

	[Test]
	public async Task ImportService_ConsumedOnlyByAHookParameter_AdvertisesTheExternalDependency()
	{
		using HookExternalContainer.Root container = new();

		await That(((IAwaitenRoot)container).ExternalDependencies)
			.Contains(new AwaitenExternalDependency(typeof(IClock)))
			.Because("an [ImportService<T>] reached only through a lifecycle hook parameter is still advertised and driven onto the external surface");
	}

	public sealed class ResolverWiring
	{
		[Test]
		public async Task RootConstructor_ResolvesThroughTheGivenResolver()
		{
			using ExternalContainer.Root container = new(new ClockResolver());

			await That(container.Resolve<Reporter>().Clock).Is<FixedClock>()
				.Because("the resolver passed to the Root constructor serves the [ImportService<T>] dependency without a cast");
		}

		[Test]
		public async Task RootConstructor_WiresTheResolverBeforeEagerActivation()
		{
			FixedClock clock = new();

			using EagerExternalContainer.Root container = new(new SingleClockResolver(clock));

			await That(container.Resolve<Reporter>().Clock).IsSameAs(clock)
				.Because("an eager singleton is built in the Root constructor, so its external dependency needs the resolver already in place");
		}

		[Test]
		public async Task Setter_SameResolverAgain_IsANoOp()
		{
			ClockResolver resolver = new();
			using ExternalContainer.Root container = new(resolver);
			IAwaitenExternalResolverHost host = container;

			await That(() => host.ExternalResolver = resolver).DoesNotThrow()
				.Because("setting the resolver the scope already holds cannot mix providers, which keeps a host's idempotent wiring working");
			await That(host.ExternalResolver).IsSameAs(resolver);
		}

		[Test]
		public async Task Setter_DifferentResolverOnceSet_Throws()
		{
			ClockResolver resolver = new();
			using ExternalContainer.Root container = new(resolver);
			IAwaitenExternalResolverHost host = container;

			await That(() => host.ExternalResolver = new ClockResolver()).Throws<InvalidOperationException>()
				.WithMessage("*already has an ExternalResolver*two providers*Root constructor*").AsWildcard()
				.Because("replacing the resolver would let one container mix instances from two providers, and the message names the cause and the remedy");
			await That(host.ExternalResolver).IsSameAs(resolver)
				.Because("the rejected resolver must not replace the one already set");
		}

		[Test]
		public async Task Setter_NullOnceSet_Throws()
		{
			using ExternalContainer.Root container = new(new ClockResolver());
			IAwaitenExternalResolverHost host = container;

			await That(() => host.ExternalResolver = null).Throws<InvalidOperationException>()
				.Because("clearing the resolver is a replacement too: a later one would mix providers just the same");
		}

		[Test]
		public async Task ChildScope_SetsItsOwnResolverOnce()
		{
			UtcClock scopeClock = new();
			using ExternalContainer.Root container = new(new ClockResolver());
			using ExternalContainer.Scope scope = container.CreateScope();
			IAwaitenExternalResolverHost host = scope;

			host.ExternalResolver = new SingleClockResolver(scopeClock);

			await That(scope.Resolve<Reporter>().Clock).IsSameAs(scopeClock)
				.Because("each scope holds its own set-once resolver, which a host wires to the provider aligned with that scope");
			await That(() => host.ExternalResolver = new ClockResolver()).Throws<InvalidOperationException>()
				.Because("the child scope's resolver is set-once just like the root's");
		}
	}
}
