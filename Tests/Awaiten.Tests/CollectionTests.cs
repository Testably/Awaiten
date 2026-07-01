using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of collection dependencies: a constructor parameter typed as a collection of a
///     service - <see cref="IEnumerable{T}" />, <see cref="IReadOnlyList{T}" />,
///     <see cref="IReadOnlyCollection{T}" />, <see cref="IList{T}" />, <see cref="ICollection{T}" /> or
///     <c>T[]</c> - resolves to every unkeyed registration of that service, in registration order, each
///     member keeping its own lifetime. <see cref="IEnumerable{T}" /> and <c>T[]</c> are also publicly
///     resolvable. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class CollectionTests
{
	[Fact]
	public async Task Enumerable_ResolvesEveryRegistrationInOrder()
	{
		using CollectionContainer.Root container = new();

		PluginHost host = container.Resolve<PluginHost>();

		await That(host.Plugins).HasCount(2);
		await That(host.Plugins[0].Name).IsEqualTo("alpha");
		await That(host.Plugins[1].Name).IsEqualTo("beta");
	}

	[Fact]
	public async Task Enumerable_IsPubliclyResolvable()
	{
		using CollectionContainer.Root container = new();

		string[] names = container.Resolve<IEnumerable<IPlugin>>().Select(p => p.Name).ToArray();

		await That(names).HasCount(2);
		await That(names[0]).IsEqualTo("alpha");
		await That(names[1]).IsEqualTo("beta");
	}

	[Fact]
	public async Task Array_IsPubliclyResolvable()
	{
		using CollectionContainer.Root container = new();

		IPlugin[] plugins = container.Resolve<IPlugin[]>();

		await That(plugins).HasCount(2);
	}

	[Fact]
	public async Task SingletonMembers_ShareTheInstanceWithSingleResolution()
	{
		using CollectionContainer.Root container = new();

		IPlugin[] plugins = container.Resolve<IPlugin[]>();

		// The single resolution returns the first (winning) registration; the collection holds the same
		// singleton instance, not a second copy.
		await That(plugins.First()).IsSameAs(container.Resolve<IPlugin>());
	}

	[Fact]
	public async Task EmptyCollection_ResolvesToAnEmptyCollection()
	{
		using CollectionContainer.Root container = new();

		// IExtension has no registration; the collection is empty rather than a missing-dependency error.
		ExtensionHost host = container.Resolve<ExtensionHost>();

		await That(host.Count).IsEqualTo(0);
	}

	[Fact]
	public async Task TransientMembers_AreFreshOnEachCollectionResolution()
	{
		using CollectionContainer.Root container = new();

		IReadOnlyList<IStep> first = container.Resolve<StepRunner>().Steps;
		IReadOnlyList<IStep> second = container.Resolve<Func<StepRunner>>()().Steps;

		// Each StepRunner gets its own freshly-built transient steps.
		await That(ReferenceEquals(first[0], second[0])).IsFalse();
	}

	[Fact]
	public async Task ScopedMembers_ResolveFromTheScope()
	{
		using CollectionContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		IReadOnlyList<IUnit> units = scope.Resolve<UnitHost>().Units;

		await That(units.Single()).IsSameAs(scope.Resolve<IUnit>());
	}

	[Fact]
	public async Task Strict_ResolvingACollectionOfDisposableTransientsFromTheRoot_ThrowsGuidance()
	{
		using DisposableCollectionContainer.Root container = new();

		await That(() => container.Resolve<IWidget[]>()).Throws<InvalidOperationException>()
			.Because("materializing a collection of disposable transients by type on the root would accumulate them for the container's lifetime, so it is withheld under strict lifetime safety - just like the singular resolution of such a member");
		await That(() => container.Resolve<IEnumerable<IWidget>>()).Throws<InvalidOperationException>()
			.Because("the IEnumerable<T> shape of the same collection is withheld on the root too");
	}

	[Fact]
	public async Task Strict_TryResolvingAWithheldCollectionFromTheRoot_ReturnsFalseRatherThanThrowing()
	{
		using DisposableCollectionContainer.Root container = new();

		bool resolved = container.TryResolve<IWidget[]>(out IWidget[]? widgets);

		await That(resolved).IsFalse()
			.Because("TryResolve is a non-throwing probe; the withheld collection reports false on the root rather than throwing the guidance");
		await That(widgets).IsNull();
	}

	[Fact]
	public async Task Strict_ResolvingAWithheldCollectionFromAScope_Works()
	{
		using DisposableCollectionContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		IWidget[] widgets = scope.Resolve<IWidget[]>();

		await That(widgets).HasCount(1)
			.Because("a child scope bounds the members' lifetime, so it resolves by type the collection the root withholds");
	}

	[Fact]
	public async Task Strict_AWithheldCollectionResolvedFromAScope_IsDisposedWithTheScope()
	{
		using DisposableCollectionContainer.Root container = new();

		DisposableWidget widget;
		using (IAwaitenScope scope = container.CreateScope())
		{
			widget = (DisposableWidget)scope.Resolve<IWidget[]>().Single();
			await That(widget.Disposed).IsFalse()
				.Because("the scope is still alive");
		}

		await That(widget.Disposed).IsTrue()
			.Because("the scope tracks the transient members it materialized and disposes them with the scope - the accumulation the root would suffer is bounded here");
	}

	[Fact]
	public async Task Loose_ResolvingACollectionOfDisposableTransientsFromTheRoot_Works()
	{
		using LooseCollectionContainer.Root container = new();

		IWidget[] widgets = container.Resolve<IWidget[]>();

		await That(widgets).HasCount(1)
			.Because("Loose lifetime safety keeps the collection resolvable by type on the root, like a singular disposable transient");
	}

	[Fact]
	public async Task ExplicitCollectionRegistration_WinsOverSynthesis()
	{
		using ExplicitCollectionContainer.Root container = new();

		BundleHost host = container.Resolve<BundleHost>();

		// IEnumerable<IPlugin> is registered as an opaque value (a Bundle), so both the injected parameter and the
		// public resolution return that registration - not the collection synthesized from the IPlugin members.
		await That(host.Plugins).HasCount(1);
		await That(host.Plugins[0].Name).IsEqualTo("bundle");
		await That(container.Resolve<IEnumerable<IPlugin>>().Single().Name).IsEqualTo("bundle");

		// All-or-nothing synthesis: because a collection shape of IPlugin (IEnumerable<IPlugin>) is explicitly
		// registered, no shape is synthesized - the unregistered IPlugin[] shape is unresolvable rather than a
		// silently synthesized second collection that would disagree with the registered one.
		await That(() => container.Resolve<IPlugin[]>()).Throws<InvalidOperationException>()
			.Because("registering one collection shape of IPlugin suppresses synthesis for every shape of IPlugin");
	}

	[Fact]
	public async Task AllCollectionShapes_ArePubliclyResolvable()
	{
		using CollectionContainer.Root container = new();

		// Every supported collection shape resolves the same two members (the eagerly materialized array
		// satisfies each), not only IEnumerable<T> and T[].
		await That(container.Resolve<IReadOnlyList<IPlugin>>()).HasCount(2);
		await That(container.Resolve<IReadOnlyCollection<IPlugin>>()).HasCount(2);
		await That(container.Resolve<IList<IPlugin>>()).HasCount(2);
		await That(container.Resolve<ICollection<IPlugin>>()).HasCount(2);
	}

	[Fact]
	public async Task FromKey_CollectionResolvesOnlyTheMembersUnderThatKey()
	{
		using KeyedCollectionContainer.Root container = new();

		KeyedPluginHost host = container.Resolve<KeyedPluginHost>();

		// Each [FromKey] collection resolves the registration(s) under that key, never the others. A key
		// identifies at most one registration per service type here (two would be AWT117), so each keyed
		// collection holds its single keyed member; the point is that the buckets stay disjoint.
		await That(host.Primary).HasCount(1);
		await That(host.Primary[0].Name).IsEqualTo("alpha");
		await That(host.Secondary).HasCount(1);
		await That(host.Secondary[0].Name).IsEqualTo("gamma");

		// The unkeyed collection resolves only the unkeyed registration - a keyed member is never an unkeyed one.
		await That(host.Unkeyed).HasCount(1);
		await That(host.Unkeyed[0].Name).IsEqualTo("plain");
	}

	public interface IPlugin
	{
		string Name { get; }
	}

	public sealed class Alpha : IPlugin
	{
		public string Name => "alpha";
	}

	public sealed class Beta : IPlugin
	{
		public string Name => "beta";
	}

	public sealed class PluginHost
	{
		public PluginHost(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToArray();

		public IReadOnlyList<IPlugin> Plugins { get; }
	}

	public interface IExtension;

	public sealed class ExtensionHost
	{
		public ExtensionHost(IReadOnlyList<IExtension> extensions) => Count = extensions.Count;

		public int Count { get; }
	}

	public interface IStep;

	public sealed class StepA : IStep;

	public sealed class StepRunner
	{
		public StepRunner(IReadOnlyList<IStep> steps) => Steps = steps;

		public IReadOnlyList<IStep> Steps { get; }
	}

	public interface IUnit;

	public sealed class UnitA : IUnit;

	public sealed class UnitHost
	{
		public UnitHost(IReadOnlyList<IUnit> units) => Units = units;

		public IReadOnlyList<IUnit> Units { get; }
	}

	public interface IWidget;

	public sealed class DisposableWidget : IWidget, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class Bundled : IPlugin
	{
		public string Name => "bundle";
	}

	// An opaque collection value: a concrete IEnumerable<IPlugin> registered as a whole, the way command-line
	// arguments (string[]) or a config list would be. It must win over the collection synthesized from the
	// individual IPlugin registrations.
	public sealed class PluginBundle : List<IPlugin>
	{
		public PluginBundle() => Add(new Bundled());
	}

	public sealed class BundleHost
	{
		public BundleHost(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToArray();

		public IReadOnlyList<IPlugin> Plugins { get; }
	}

	public sealed class Gamma : IPlugin
	{
		public string Name => "gamma";
	}

	public sealed class Plain : IPlugin
	{
		public string Name => "plain";
	}

	public sealed class KeyedPluginHost
	{
		public KeyedPluginHost(
			[FromKey("primary")] IEnumerable<IPlugin> primary,
			[FromKey("secondary")] IReadOnlyList<IPlugin> secondary,
			IEnumerable<IPlugin> unkeyed)
		{
			Primary = primary.ToArray();
			Secondary = secondary;
			Unkeyed = unkeyed.ToArray();
		}

		public IReadOnlyList<IPlugin> Primary { get; }

		public IReadOnlyList<IPlugin> Secondary { get; }

		public IReadOnlyList<IPlugin> Unkeyed { get; }
	}

	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<Beta, IPlugin>]
	[Singleton<PluginHost>]
	[Singleton<ExtensionHost>]
	[Transient<StepA, IStep>]
	[Transient<StepRunner>]
	[Scoped<UnitA, IUnit>]
	[Scoped<UnitHost>]
	public static partial class CollectionContainer;

	// A collection whose member is a disposable transient: materializing it by type on the root would
	// accumulate the transients for the container's lifetime, so under strict lifetime safety the collection is
	// withheld on the root (resolvable from a child scope, which bounds it). Loose keeps it root-resolvable.
	[Container]
	[Transient<DisposableWidget, IWidget>]
	public static partial class DisposableCollectionContainer;

	[Container(LifetimeSafety = LifetimeSafety.Loose)]
	[Transient<DisposableWidget, IWidget>]
	public static partial class LooseCollectionContainer;

	// IEnumerable<IPlugin> is registered directly (an opaque value); the individual IPlugin registrations would
	// otherwise synthesize a collection of two, so the counts distinguish which one injection resolves to.
	[Container]
	[Singleton<Alpha, IPlugin>]
	[Singleton<Beta, IPlugin>]
	[Singleton<PluginBundle, IEnumerable<IPlugin>>]
	[Singleton<BundleHost>]
	public static partial class ExplicitCollectionContainer;

	// Registrations under three disjoint buckets: 'primary' (alpha), 'secondary' (gamma) and unkeyed (plain).
	// A [FromKey] collection resolves exactly the members of its bucket. A key identifies at most one
	// registration per service type (two would be AWT117), so each keyed collection holds its single member.
	[Container]
	[Singleton<Alpha, IPlugin>(Key = "primary")]
	[Singleton<Gamma, IPlugin>(Key = "secondary")]
	[Singleton<Plain, IPlugin>]
	[Singleton<KeyedPluginHost>]
	public static partial class KeyedCollectionContainer;
}
