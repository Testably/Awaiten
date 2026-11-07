using System.Threading;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of internal services that modules of Awaiten.Tests.Support build through generated handles, in
///     a container of this assembly, which the support assembly grants no <c>InternalsVisibleTo</c>.
/// </summary>
public class ModuleHandleRuntimeTests
{
	[Test]
	public async Task AsyncInitializationThatFails_DisposesTheInstanceOfTheHandle()
	{
		using FailingHeaterContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		await That(() => scope.ResolveAsync<IFailingHeaterUser>(TestContext.Current!.Execution.CancellationToken)).Throws<InvalidOperationException>()
			.WithMessage("heater failed");

		await That(FailingHeaterProbe.Disposed).IsEqualTo(FailingHeaterProbe.Constructed)
			.Because("the handle forwards the disposal of an instance whose initialization failed, so it is not leaked");
		await That(FailingHeaterProbe.Constructed).IsEqualTo(1);
	}

	[Test]
	public async Task KeyedRegistrationOfAHandledImplementation_IsTheInstanceOfTheHandle()
	{
		using KeyedFlavorContainer.Root container = new();

		await That(container.Resolve<IKeyedFlavor>("vanilla")).IsSameAs(container.Resolve<IKeyedFlavorUser>().Flavor)
			.Because("the keyed registration is an alias of the handle that serves the internal service too");
	}

	[Test]
	public async Task TokenOfTheResolution_ReachesTheInitializerAndTheAsyncFactoryWrappers()
	{
		using CancellationTokenSource cancellation = new();
		await using TokenContainer.Root container = new();
		await using IAwaitenScope scope = container.CreateScope();

		ITokenUser user = await scope.ResolveAsync<ITokenUser>(cancellation.Token);

		await That(user.InitializerToken).IsEqualTo(cancellation.Token)
			.Because("the initializer wrapper passes the resolution's token on to the convention");
		await That(user.FactoryToken).IsEqualTo(cancellation.Token)
			.Because("the async factory wrapper passes the resolution's token on to the factory");
	}

	[Test]
	public async Task FactoryResultThatOnlyDisposesAsynchronously_IsDisposedThroughItsHandle()
	{
		IAsyncValveUser user;
		await using (AsyncValveContainer.Root container = new())
		{
			user = container.Resolve<IAsyncValveUser>();
			await That(user.ValveDisposed).IsFalse();
		}

		await That(user.ValveDisposed).IsTrue()
			.Because("the handle wraps the factory's result in the variant that matches its disposal at run time");
	}

	[Test]
	public async Task ModuleWithDirectivesImportedBeforeOneWithout_KeepsItsDirectives()
	{
		IDirectiveGrinder grinder;
		await using (DirectiveBThenAContainer.Root container = new())
		{
			grinder = await container.ResolveAsync<IDirectiveGrinder>(TestContext.Current!.Execution.CancellationToken);
			await That(grinder.StoreActivated).IsTrue()
				.Because("the first import sets the hook, and the later one leaves it unset, which merges with it");
			await That(grinder.StoreWarmed).IsTrue();
		}

		await That(grinder.StoreDisposed).IsFalse()
			.Because("the first import suppresses the disposal of the one shared store");
	}
}

[Container]
[Import(typeof(CrossAssemblyFailingHeaterModule))]
public static partial class FailingHeaterContainer;

[Container]
[Import(typeof(CrossAssemblyKeyedFlavorModule))]
public static partial class KeyedFlavorContainer;

[Container]
[Import(typeof(CrossAssemblyTokenModule))]
public static partial class TokenContainer;

[Container]
[Import(typeof(CrossAssemblyAsyncValveModule))]
public static partial class AsyncValveContainer;

[Container]
[Import(typeof(CrossAssemblyDirectiveBModule))]
[Import(typeof(CrossAssemblyDirectiveAModule))]
public static partial class DirectiveBThenAContainer;
