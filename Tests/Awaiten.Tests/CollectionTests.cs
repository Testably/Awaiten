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
}
