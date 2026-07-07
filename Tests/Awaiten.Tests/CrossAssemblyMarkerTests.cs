using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Consumer-side markers are now source-injected internal types, so each assembly carries its own
///     <c>Awaiten.FromKeyAttribute</c>/<c>Awaiten.InjectAttribute</c>. This resolves a service whose
///     <c>[FromKey]</c> and <c>[Inject]</c> markers were applied in the referenced <c>Awaiten.Tests.Support</c>
///     assembly through a container defined here, proving the generator's by-metadata-name classification still
///     reads markers across the assembly boundary despite the per-assembly marker identity.
/// </summary>
public partial class CrossAssemblyMarkerTests
{
	[Fact]
	public async Task CrossAssemblyFromKey_SelectsTheKeyedRegistration()
	{
		using MarkerContainer.Root container = new();

		CrossAssemblyMarkerConsumer consumer = container.Resolve<CrossAssemblyMarkerConsumer>();

		await That(consumer.Selected.Name).IsEqualTo("backup")
			.Because("the [FromKey(\"backup\")] applied in the referenced assembly selects the keyed registration");
	}

	[Fact]
	public async Task CrossAssemblyInject_FillsThePropertyFromTheUnkeyedRegistration()
	{
		using MarkerContainer.Root container = new();

		CrossAssemblyMarkerConsumer consumer = container.Resolve<CrossAssemblyMarkerConsumer>();

		await That(consumer.Primary).IsNotNull();
		await That(consumer.Primary!.Name).IsEqualTo("primary")
			.Because("the [Inject] property applied in the referenced assembly is filled from the unkeyed registration");
	}

	[Container]
	[Singleton<PrimaryChannel, ICrossAssemblyChannel>]
	[Singleton<BackupChannel, ICrossAssemblyChannel>(Key = "backup")]
	[Singleton<CrossAssemblyMarkerConsumer>]
	public static partial class MarkerContainer;
}
