using System;
using System.Collections.Generic;
using System.Linq;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of a <c>[Module]</c>'s self-compiled registrations. <see cref="SelfCompiledRegistrationModule" />
///     lives in the referenced Awaiten.Tests.Support assembly, which grants this assembly no
///     <c>InternalsVisibleTo</c>, and registers internal implementations. Its build emitted a factory per
///     implementation, so the containers here import it and treat each implementation exactly like one they construct.
/// </summary>
public partial class SelfCompiledModuleRegistrationTests
{
	[Test]
	public async Task ResolvesTheInternalImplementationWithItsDependenciesAndHook()
	{
		using ImportingContainer.Root container = new();

		ISelfCompiledGreeter greeter = container.Resolve<ISelfCompiledGreeter>();

		await That(greeter.GetType().Name).IsEqualTo("SelfCompiledGreeter")
			.Because("the module's generated factory constructs its internal implementation");
		await That(greeter.Clock.Origin).IsEqualTo("library")
			.Because("the factory's parameter resolves from the importing container's graph, here the module's own default");
		await That(greeter.Activated).IsTrue()
			.Because("the module's internal activation hook runs through its generated public wrapper");
	}

	[Test]
	public async Task SingletonIsSharedAcrossItsKeyedAndUnkeyedRegistrations()
	{
		using ImportingContainer.Root container = new();

		ISelfCompiledGreeter greeter = container.Resolve<ISelfCompiledGreeter>();

		await That(container.Resolve<ISelfCompiledGreeter>()).IsSameAs(greeter);
		await That(container.Resolve<ISelfCompiledGreeter>("formal")).IsSameAs(greeter)
			.Because("both registrations name the same implementation, which a container shares as one singleton");
	}

	[Test]
	public async Task PublicImplementationWithAnInternalConstructorIsOneInstanceForBothServices()
	{
		using ImportingContainer.Root container = new();

		await That(container.Resolve<ISelfCompiledFront>()).IsSameAs(container.Resolve<ISelfCompiledBack>())
			.Because("the generated factory returns the public implementation, so both services share its one instance");
	}

	[Test]
	public async Task ScopedImplementationIsDisposedWithItsScope()
	{
		using ImportingContainer.Root container = new();
		ISelfCompiledSession session;
		using (IAwaitenScope scope = container.CreateScope())
		{
			session = scope.Resolve<ISelfCompiledSession>();
			await That(scope.Resolve<ISelfCompiledSession>()).IsSameAs(session);
			await That(session.IsDisposed).IsFalse();
		}

		await That(session.IsDisposed).IsTrue()
			.Because("the container owns the disposal of the internal implementation it cannot see");
	}

	[Test]
	public async Task AsyncDisposableImplementationIsDisposedAsynchronously()
	{
		ISelfCompiledAsyncResource resource;
		await using (AsyncResourceContainer.Root container = new())
		{
			resource = container.Resolve<ISelfCompiledAsyncResource>();
		}

		await That(resource.IsDisposed).IsTrue();
	}

	[Test]
	public async Task SuppressedDisposalIsHonored()
	{
		ISelfCompiledKept kept;
		using (ImportingContainer.Root container = new())
		{
			kept = container.Resolve<ISelfCompiledKept>();
		}

		await That(kept.IsDisposed).IsFalse()
			.Because("the registration opted out of the container's disposal");
	}

	[Test]
	public async Task AsyncInitializableImplementationIsInitializedAndNotResolvedSynchronously()
	{
		await using ImportingContainer.Root container = new();

		await That(() => container.Resolve<ISelfCompiledWarmup>()).Throws<InvalidOperationException>()
			.WithMessage("*ResolveAsync*").AsWildcard()
			.Because("an async-initialized implementation is withheld from a synchronous resolve, also across the assembly boundary");

		ISelfCompiledWarmup warmup = await container.ResolveAsync<ISelfCompiledWarmup>();

		await That(warmup.IsInitialized).IsTrue();
	}

	[Test]
	public async Task AsyncInitializableScanMatchIsInitialized()
	{
		await using ScanWarmupContainer.Root container = new();

		ISelfCompiledScanWarmup warmup = await container.ResolveAsync<ISelfCompiledScanWarmup>();

		await That(warmup.IsInitialized).IsTrue()
			.Because("the module reports the trait of the internal match, so the consumer initializes it");
	}

	[Test]
	public async Task InternalImplementationsCollect()
	{
		using ImportingContainer.Root container = new();

		IEnumerable<ISelfCompiledTool> tools = container.Resolve<IEnumerable<ISelfCompiledTool>>();

		await That(tools.Select(tool => tool.GetType().Name)).IsEqualTo(["SelfCompiledHammer", "SelfCompiledSaw",])
			.Because("each self-compiled implementation is a collection member, in registration order");
	}

	[Test]
	public async Task EagerSingletonIsBuiltWithTheRoot()
	{
		int before = SelfCompiledEagerCounter.Constructed;

		using ImportingContainer.Root container = new();

		await That(SelfCompiledEagerCounter.Constructed).IsGreaterThan(before)
			.Because("the eager singleton is constructed when the root is built, before anything resolves it");
	}

	[Test]
	public async Task ImporterOverridesTheModuleDefault()
	{
		using OverridingContainer.Root container = new();

		await That(container.Resolve<ISelfCompiledGreeter>().Clock.Origin).IsEqualTo("application")
			.Because("the container's own registration wins over the module's Fallback default, also as a factory parameter");
	}

	[Test]
	public async Task ImporterDecoratesTheService()
	{
		using DecoratingContainer.Root container = new();

		ISelfCompiledGreeter greeter = container.Resolve<ISelfCompiledGreeter>();

		await That(greeter).IsExactly<LoudGreeter>();
		await That(((LoudGreeter)greeter).Inner.GetType().Name).IsEqualTo("SelfCompiledGreeter")
			.Because("the container's decorator wraps the instance the module's factory builds");
	}

	[Test]
	public async Task ExtendingContainerInheritsTheSelfCompiledRegistration()
	{
		using ExtendingContainer.Root container = new();

		await That(container.Resolve<ISelfCompiledGreeter>().Activated).IsTrue()
			.Because("a container extending an importer of the module inherits its registrations, factory and hook wrapper included");
	}

	[Test]
	public async Task AFactoryAndASelfCompiledRegistrationSharingAHookWrapper_EachRunTheHook()
	{
		using SharedHookContainer.Root container = new();

		SharedHookKettle made = container.Resolve<SharedHookKettle>("made");
		SharedHookKettle built = container.Resolve<SharedHookKettle>("built");

		await That(made).IsNotSameAs(built)
			.Because("the Factory registration and the self-compiled registration build their own instances");
		await That(made.Boiled).IsEqualTo(1)
			.Because("the Factory registration runs the hook through the wrapper it shares with the self-compiled registration");
		await That(built.Boiled).IsEqualTo(1);
	}

	[Container]
	[Import(typeof(SelfCompiledRegistrationModule))]
	public static partial class ImportingContainer;

	[Container]
	[Import(typeof(SharedHookModule))]
	public static partial class SharedHookContainer;

	[Container(Extends = typeof(ImportingContainer))]
	public static partial class ExtendingContainer;

	[Container]
	[Import(typeof(SelfCompiledRegistrationModule))]
	[Singleton<ApplicationClock, ISelfCompiledClock>]
	public static partial class OverridingContainer;

	[Container]
	[Import(typeof(SelfCompiledRegistrationModule))]
	[Decorate<LoudGreeter, ISelfCompiledGreeter>]
	public static partial class DecoratingContainer;

	[Container]
	[Import(typeof(SelfCompiledScanWarmupModule))]
	public static partial class ScanWarmupContainer;

	[Container]
	[Import(typeof(SelfCompiledAsyncResourceModule))]
	public static partial class AsyncResourceContainer;

	public sealed class ApplicationClock : ISelfCompiledClock
	{
		public string Origin => "application";
	}

	public sealed class LoudGreeter(ISelfCompiledGreeter inner) : ISelfCompiledGreeter
	{
		public ISelfCompiledGreeter Inner => inner;

		public ISelfCompiledClock Clock => inner.Clock;

		public bool Activated => inner.Activated;
	}
}
