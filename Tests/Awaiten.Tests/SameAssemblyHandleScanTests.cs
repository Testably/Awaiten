using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     A module of this assembly whose scan matches take an internal service the module registers: its generated
///     factories take the service through its handle for a container in another assembly, while a container here
///     builds the service itself and still registers every match.
/// </summary>
public partial class SameAssemblyHandleScanTests
{
	[Test]
	public async Task ScanMatchesTakingAnInternalService_AreRegisteredByAContainerOfTheModulesAssembly()
	{
		using SameAssemblyRoastingContainer.Root container = new();

		IRoastingPlugin[] plugins = container.Resolve<IEnumerable<IRoastingPlugin>>().ToArray();

		await That(plugins).HasCount(2)
			.Because("the internal match and the match private to the module both take the internal store, which this container builds itself");
		await That(plugins.Select(plugin => plugin.Store).Distinct()).HasCount(1)
			.Because("both matches receive the one singleton store");
	}

	public interface IRoastingPlugin
	{
		object Store { get; }
	}

	internal interface IRoastingStore;

	internal sealed class RoastingStore : IRoastingStore;

	internal sealed class InternalRoaster(IRoastingStore store) : IRoastingPlugin
	{
		public object Store => store;
	}

	[Module]
	[Singleton<RoastingStore, IRoastingStore>]
	[Scan<IRoastingPlugin>(As = ScanAs.Marker)]
	public static partial class RoastingModule
	{
		private sealed class PrivateRoaster(IRoastingStore store) : IRoastingPlugin
		{
			public object Store => store;
		}
	}

	[Container]
	[Import(typeof(RoastingModule))]
	public static partial class SameAssemblyRoastingContainer;
}
