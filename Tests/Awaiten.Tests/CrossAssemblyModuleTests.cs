using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of a <c>[Module]</c> compiled into a real referenced assembly (Awaiten.Tests.Support):
///     a container in this assembly <c>[Import]</c>s it and resolves its registrations end to end: a strong
///     registration, an overridable <c>Default</c> the container can override, and <c>Factory</c>/<c>Instance</c>
///     members the generated container must call qualified on the cross-assembly module. This exercises the real
///     metadata-import path, complementing the generator-level in-memory tests.
/// </summary>
public partial class CrossAssemblyModuleTests
{
	[Fact]
	public async Task ImportedCrossAssemblyModule_ContributesItsStrongRegistration()
	{
		using SupportContainer.Root container = new();

		await That(container.Resolve<SupportLogger>()).IsNotNull();
	}

	[Fact]
	public async Task CrossAssemblyDefault_IsUsedWhenTheContainerDoesNotOverrideIt()
	{
		using SupportContainer.Root container = new();

		await That(container.Resolve<ICrossAssemblyClock>()).Is<SupportClock>();
	}

	[Fact]
	public async Task ContainerRegistration_OverridesACrossAssemblyDefault()
	{
		using OverrideContainer.Root container = new();

		await That(container.Resolve<ICrossAssemblyClock>()).Is<TestClock>();
	}

	[Fact]
	public async Task OverriddenCrossAssemblyDefault_IsAbsentFromTheServiceCollection()
	{
		using OverrideContainer.Root container = new();

		ICrossAssemblyClock[] clocks = container.Resolve<ICrossAssemblyClock[]>();

		// The overridden module default is dropped in full, so the collection sees only the container's own clock.
		await That(clocks).HasCount(1);
		await That(clocks[0]).Is<TestClock>();
	}

	[Fact]
	public async Task CrossAssemblyModuleFactory_ProducesTheServiceThroughTheModuleMethod()
	{
		using SupportContainer.Root container = new();

		// Proves the generated container actually calls the module's static factory across the assembly boundary.
		await That(container.Resolve<SupportGreeter>().Greeting).IsEqualTo("hello from the support module");
	}

	[Fact]
	public async Task CrossAssemblyModuleInstance_ExposesTheModulesPreBuiltMember()
	{
		using SupportContainer.Root container = new();

		await That(container.Resolve<SupportCache>()).IsSameAs(CrossAssemblySupportModule.Cache);
	}

	/// <summary>The container's own clock, overriding the module's cross-assembly default.</summary>
	public sealed class TestClock : ICrossAssemblyClock;

	[Container]
	[Import(typeof(CrossAssemblySupportModule))]
	public static partial class SupportContainer;

	[Container]
	[Import(typeof(CrossAssemblySupportModule))]
	[Singleton<TestClock, ICrossAssemblyClock>]
	public static partial class OverrideContainer;
}
