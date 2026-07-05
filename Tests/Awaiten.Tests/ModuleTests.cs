namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of modules: a container imports a module's registrations with
///     <c>[Import(typeof(Module))]</c>, its own registrations override the module's overridable
///     <c>Fallback.Warn</c>/<c>Fallback.Silent</c> registrations, and an imported default is used only when the container
///     does not provide its own, so an overridden default is absent even from the service's collection.
/// </summary>
public partial class ModuleTests
{
	[Fact]
	public async Task ImportedModule_ContributesItsStrongRegistrations()
	{
		using DefaultsContainer.Root container = new();

		await That(container.Resolve<Logger>()).IsNotNull();
	}

	[Fact]
	public async Task ContainerRegistration_OverridesAnImportedDefault()
	{
		using AppContainer.Root container = new();

		await That(container.Resolve<IClock>()).Is<AppClock>();
	}

	[Fact]
	public async Task ImportedDefault_IsUsedWhenTheContainerDoesNotOverrideIt()
	{
		using DefaultsContainer.Root container = new();

		await That(container.Resolve<IClock>()).Is<ModuleClock>();
	}

	[Fact]
	public async Task ImportedSilentFallbackRegistration_ContributesWhenNothingElseProvidesTheService()
	{
		using DefaultsContainer.Root container = new();

		await That(container.Resolve<ICache>()).Is<MemoryCache>();
	}

	[Fact]
	public async Task OverriddenDefault_IsAbsentFromTheServiceCollection()
	{
		using AppContainer.Root container = new();

		IClock[] clocks = container.Resolve<IClock[]>();

		// The overridden module default is dropped in full, so the collection sees only the container's own clock.
		await That(clocks).HasCount(1);
		await That(clocks[0]).Is<AppClock>();
	}

	[Fact]
	public async Task ModuleFactory_ProducesTheServiceThroughTheModuleMethod()
	{
		using ProductionContainer.Root container = new();

		await That(container.Resolve<IClock>()).Is<ModuleClock>();
	}

	[Fact]
	public async Task ModuleInstance_ExposesTheModulesPreBuiltMember()
	{
		using ProductionContainer.Root container = new();

		await That(container.Resolve<ICache>()).IsSameAs(ProductionModule.Cache);
	}

	public interface IClock;

	public sealed class ModuleClock : IClock;

	public sealed class AppClock : IClock;

	public sealed class Logger;

	public interface ICache;

	public sealed class MemoryCache : ICache;

	[Module]
	[Singleton<ModuleClock, IClock>(Fallback = Fallback.Warn)]
	[Singleton<Logger>]
	[Singleton<MemoryCache, ICache>(Fallback = Fallback.Silent)]
	public static class InfrastructureModule;

	[Module]
	[Singleton<ModuleClock, IClock>(Factory = nameof(ProductionModule.CreateClock))]
	[Singleton<MemoryCache, ICache>(Instance = nameof(ProductionModule.Cache))]
	public static class ProductionModule
	{
		public static MemoryCache Cache { get; } = new();

		public static ModuleClock CreateClock() => new();
	}

	[Container]
	[Import(typeof(InfrastructureModule))]
	[Singleton<AppClock, IClock>]
	public static partial class AppContainer;

	[Container]
	[Import(typeof(InfrastructureModule))]
	public static partial class DefaultsContainer;

	[Container]
	[Import(typeof(ProductionModule))]
	public static partial class ProductionContainer;
}
