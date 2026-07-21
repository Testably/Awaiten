using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime lifetimes of a <c>[Module]</c>'s self-compiled <c>[Scan]</c>. The module lives in the referenced
///     Awaiten.Tests.Support assembly, where it compiled its scans into ordinary registrations carrying the scans'
///     declared lifetimes. Those registrations are honored here by the importing container, so instance ownership
///     stays container-side even though the implementations are internal to the library.
/// </summary>
public partial class ModuleScanLifetimeTests
{
	[Fact]
	public async Task SingletonMatch_IsOneInstancePerRoot()
	{
		using ScanLifetimeContainer.Root first = new();
		using ScanLifetimeContainer.Root second = new();

		IScanSingleton fromFirst = first.Resolve<IScanSingleton>();

		await That(first.Resolve<IScanSingleton>()).IsSameAs(fromFirst)
			.Because("the scan declared Singleton, so the importing container shares one instance for the root's life");
		await That(second.Resolve<IScanSingleton>()).IsNotSameAs(fromFirst)
			.Because("the instance is owned by the root that built it, not cached in the library's module type");
	}

	[Fact]
	public async Task SingletonMatch_IsSharedByEveryScopeOfItsRoot()
	{
		using ScanLifetimeContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		await That(scope.Resolve<IScanSingleton>()).IsSameAs(container.Resolve<IScanSingleton>())
			.Because("a scan-declared singleton resolves from the root like any other, rather than being re-created per scope");
	}

	[Fact]
	public async Task TransientMatch_IsANewInstancePerResolve()
	{
		using ScanLifetimeContainer.Root container = new();

		await That(container.Resolve<IScanTransient>()).IsNotSameAs(container.Resolve<IScanTransient>())
			.Because("the second scan declared Transient, so the container honors a different lifetime for a match from the same module");
	}

	[Container]
	[Import(typeof(ScanLifetimeModule))]
	public static partial class ScanLifetimeContainer;
}
