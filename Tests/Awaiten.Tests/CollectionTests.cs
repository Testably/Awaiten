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
}
