using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of one implementation that a module of Awaiten.Tests.Support registers under two internal
///     services, which a public registration takes both of.
/// </summary>
public partial class ModuleTwoInternalServicesTests
{
	[Test]
	public async Task Singleton_IsOneInstanceUnderBothServices()
	{
		using SingletonContainer.Root container = new();
		ITwoServicesUser user = container.Resolve<ITwoServicesUser>();

		await That(user.First).IsSameAs(user.Second)
			.Because("both internal services are served from the one instance handle of the singleton");
		await That(container.Resolve<ITwoServicesUser>().First).IsSameAs(user.First);
	}

	[Test]
	public async Task TransientDisposable_IsBuiltForEachService()
	{
		using TransientContainer.Root container = new();
		using TransientContainer.Scope scope = container.CreateScope();
		ITwoServicesUser user = scope.Resolve<ITwoServicesUser>();

		await That(user.First).IsNotSameAs(user.Second)
			.Because("a transient is a new instance for each dependency");
		await That(user.First.GetType().Name).IsEqualTo("TwoServicesDisposable");
	}

	[Container]
	[Import(typeof(CrossAssemblyTwoInternalServicesModule))]
	public static partial class SingletonContainer;

	[Container]
	[Import(typeof(CrossAssemblyTwoInternalTransientServicesModule))]
	public static partial class TransientContainer;
}
