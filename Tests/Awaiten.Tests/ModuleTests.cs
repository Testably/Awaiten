namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of modules: a container imports a module's registrations with
///     <c>[Import(typeof(Module))]</c>, its own registrations override the module's overridable
///     <c>Default</c>/<c>TryAdd</c> registrations, and an imported default is used only when the container
///     does not provide its own - so an overridden default is absent even from the service's collection.
///     The containers, modules and services are nested types, so the enclosing class is <c>partial</c>.
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
	public async Task ImportedTryAddRegistration_ContributesWhenNothingElseProvidesTheService()
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
	public async Task GenericImportForm_BehavesTheSameAsTheTypeofForm()
	{
		using GenericImportContainer.Root container = new();

		// [Import<InfrastructureModule>] imports exactly like [Import(typeof(InfrastructureModule))].
		await That(container.Resolve<Logger>()).IsNotNull();
		await That(container.Resolve<IClock>()).Is<AppClock>();
	}

	public interface IClock;

	public sealed class ModuleClock : IClock;

	public sealed class AppClock : IClock;

	public sealed class Logger;

	public interface ICache;

	public sealed class MemoryCache : ICache;

	[Module]
	[Singleton<ModuleClock, IClock>(Default = true)]
	[Singleton<Logger>]
	[Singleton<MemoryCache, ICache>(TryAdd = true)]
	public sealed class InfrastructureModule;

	[Container]
	[Import(typeof(InfrastructureModule))]
	[Singleton<AppClock, IClock>]
	public static partial class AppContainer;

	[Container]
	[Import(typeof(InfrastructureModule))]
	public static partial class DefaultsContainer;

	[Container]
	[Import<InfrastructureModule>]
	[Singleton<AppClock, IClock>]
	public static partial class GenericImportContainer;
}
