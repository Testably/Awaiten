using System.Collections.Generic;

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

	[Container]
	[ImportService<IClock>]
	[Transient<Reporter>]
	[Transient<InjectedReporter>]
	[Transient<KeyedReporter>]
	public static partial class ExternalContainer;

	private sealed class ClockResolver : IExternalResolver
	{
		private readonly Dictionary<object, IClock> _keyed = new()
		{
			["utc"] = new UtcClock(),
		};

		private readonly IClock _unkeyed = new FixedClock();

		public bool TryResolve(Type serviceType, object? serviceKey, out object? instance)
		{
			if (serviceType == typeof(IClock))
			{
				instance = serviceKey is null ? _unkeyed : _keyed[serviceKey];
				return true;
			}

			instance = null;
			return false;
		}
	}

	[Fact]
	public async Task ImportService_ConstructorDependency_ResolvesThroughTheExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IExternalResolverHost)container).ExternalResolver = new ClockResolver();

		Reporter reporter = container.Resolve<Reporter>();

		await That(reporter.Clock).Is<FixedClock>()
			.Because("the unregistered [ImportService<IClock>] dependency is drawn from the external resolver");
	}

	[Fact]
	public async Task ImportService_InjectedProperty_ResolvesThroughTheExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IExternalResolverHost)container).ExternalResolver = new ClockResolver();

		InjectedReporter reporter = container.Resolve<InjectedReporter>();

		await That(reporter.Clock).Is<FixedClock>()
			.Because("[ImportService<T>] routes an [Inject] property of that type externally too");
	}

	[Fact]
	public async Task ImportService_WithFromKey_ForwardsTheKeyToTheExternalResolver()
	{
		using ExternalContainer.Root container = new();
		((IExternalResolverHost)container).ExternalResolver = new ClockResolver();

		KeyedReporter reporter = container.Resolve<KeyedReporter>();

		await That(reporter.Clock).Is<UtcClock>()
			.Because("[ImportService<T>] with [FromKey] forwards the key to the resolver");
	}

	[Fact]
	public async Task ImportService_AdvertisesTheDeclaredExternalDependency()
	{
		using ExternalContainer.Root container = new();

		await That(((IAwaitenContainerMetadata)container).ExternalDependencies)
			.Contains(new AwaitenExternalDependency(typeof(IClock)))
			.Because("an unkeyed [ImportService<IClock>] consumer advertises the external dependency")
			.And.Contains(new AwaitenExternalDependency(typeof(IClock), "utc"))
			.Because("the keyed consumer advertises the forwarded key");
	}
}
