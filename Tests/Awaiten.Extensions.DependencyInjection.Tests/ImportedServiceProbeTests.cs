using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     <see cref="IServiceProviderIsService" /> and <see cref="IServiceProviderIsKeyedService" /> on the
///     provider-replacement path for a type the container routes to the host. <c>GetService</c> asks the host for
///     such a type, so the probe asks the host too, and a framework binds the parameter from dependency injection
///     exactly when the host has it.
/// </summary>
public sealed partial class ImportedServiceProbeTests
{
	public interface IClock;

	public sealed class FixedClock : IClock;

	public interface IRepository<T>;

	public sealed class Repository<T> : IRepository<T>;

	public sealed class Order;

	public sealed class Invoice;

	public interface IUnregistered;

	public sealed class TimeReporter(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public sealed class KeyedReporter([FromKey("utc")] IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public sealed class OrderDesk(IRepository<Order> orders)
	{
		public IRepository<Order> Orders { get; } = orders;
	}

	public sealed class Greeter
	{
		public Greeter(string name) => Name = name;

		public Greeter(IClock clock, string name)
		{
			Clock = clock;
			Name = name;
		}

		public IClock? Clock { get; }

		public string Name { get; }
	}

	[Container]
	[ImportService<IClock>]
	[Singleton<TimeReporter>]
	[Singleton<KeyedReporter>]
	public static partial class ClosedImportContainer;

	[Container]
	[ImportService(typeof(IRepository<>))]
	[Singleton<OrderDesk>]
	public static partial class OpenImportContainer;

	[Container]
	[ImportServices]
	[Singleton<TimeReporter>]
	public static partial class ImportingContainer;

	private static ServiceProvider Host(Action<ServiceCollection> configure)
	{
		ServiceCollection services = new();
		configure(services);
		return services.BuildServiceProvider();
	}

	public sealed class DeclaredImport
	{
		[Test]
		public async Task WhenTheHostHasIt_IsAService()
		{
			using ServiceProvider host = Host(services => services.AddSingleton<IClock, FixedClock>());
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IClock))).IsTrue()
				.Because("GetService asks the host for a declared import, and the host has it");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IClock),])).IsEmpty();
		}

		[Test]
		public async Task WhenTheHostLacksIt_IsNotAService()
		{
			using ServiceProvider host = Host(_ => { });
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IClock))).IsFalse()
				.Because("the host owns the answer for a declared import, and it has no such service");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IClock),])).IsEmpty();
		}

		[Test]
		public async Task AClosingOfAnOpenImport_AsksTheHost()
		{
			using ServiceProvider host = Host(services => services.AddSingleton(typeof(IRepository<>), typeof(Repository<>)));
			using AwaitenServiceProvider provider = new(new OpenImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IRepository<Invoice>))).IsTrue()
				.Because("every closing of an open import is routed to the host, also one the graph does not consume");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IRepository<Order>), typeof(IRepository<Invoice>),])).IsEmpty();
		}

		[Test]
		public async Task AClosingTheHostLacks_IsNotAService()
		{
			using ServiceProvider host = Host(_ => { });
			using AwaitenServiceProvider provider = new(new OpenImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IRepository<Order>))).IsFalse()
				.Because("the host has no closing of the open import");
		}

		[Test]
		public async Task UnderAKeyTheHostHas_IsAKeyedService()
		{
			using ServiceProvider host = Host(services => services.AddKeyedSingleton<IClock, FixedClock>("utc"));
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsKeyedService(typeof(IClock), "utc")).IsTrue()
				.Because("a declared import is routed to the host with a key too");
			await That(provider.IsKeyedService(typeof(IClock), "local")).IsFalse()
				.Because("the host has no clock under that key");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IClock),], "utc")).IsEmpty();
			await That(ProbeAgreement.Disagreements(provider, [typeof(IClock),], "local")).IsEmpty();
		}

		[Test]
		public async Task ActivatorUtilities_InjectsItIntoTheConstructorThatTakesIt()
		{
			using ServiceProvider host = Host(services => services.AddSingleton<IClock, FixedClock>());
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			Greeter greeter = ActivatorUtilities.CreateInstance<Greeter>(provider, "Ada");

			await That(greeter.Clock).IsSameAs(host.GetRequiredService<IClock>())
				.Because("ActivatorUtilities picks the longest constructor whose other parameters the probe reports as services");
		}
	}

	public sealed class AllImports
	{
		[Test]
		public async Task AHostService_IsAService()
		{
			using ServiceProvider host = Host(services => services.AddSingleton<IClock, FixedClock>());
			using AwaitenServiceProvider provider = new(new ImportingContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IClock))).IsTrue()
				.Because("[ImportServices] routes an unregistered type to the host, which has it");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IClock), typeof(IUnregistered), typeof(TimeReporter),])).IsEmpty();
		}

		[Test]
		public async Task AnUnknownType_IsNotAService()
		{
			using ServiceProvider host = Host(services => services.AddSingleton<IClock, FixedClock>());
			using AwaitenServiceProvider provider = new(new ImportingContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IUnregistered))).IsFalse()
				.Because("[ImportServices] does not make every type a service: the host has no such service");
		}

		[Test]
		public async Task AKeyedHostService_IsNotAKeyedService()
		{
			using ServiceProvider host = Host(services => services.AddKeyedSingleton<IClock, FixedClock>("utc"));
			using AwaitenServiceProvider provider = new(new ImportingContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsKeyedService(typeof(IClock), "utc")).IsFalse()
				.Because("[ImportServices] routes only an unkeyed resolve to the host, so GetKeyedService answers null");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IClock),], "utc")).IsEmpty();
		}

		[Test]
		public async Task AnUnknownType_DoesNotLoopBetweenTheProviders()
		{
			ServiceCollection services = new();
			services.AddSingleton<IClock, FixedClock>();
			services.AddAwaitenContainer<ImportingContainer.Root>();
			using ServiceProvider host = services.BuildServiceProvider(validateScopes: true);
			_ = host.GetRequiredService<TimeReporter>();
			using AwaitenServiceProvider provider = new(host.GetRequiredService<ImportingContainer.Root>(), ownsContainer: false);

			await That(provider.IsService(typeof(IUnregistered))).IsFalse()
				.Because("the host answers from its own registrations and never asks the container back");
			await That(provider.IsService(typeof(IClock))).IsTrue();
			await That(provider.IsService(typeof(TimeReporter))).IsTrue();
		}
	}

	public sealed class AHostWithoutAProbe
	{
		private sealed class ClockOnlyProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => serviceType == typeof(IClock) ? new FixedClock() : null;
		}

		[Test]
		public async Task ADeclaredImport_IsNotAService()
		{
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(new ClockOnlyProvider())));

			await That(provider.IsService(typeof(IClock))).IsFalse()
				.Because("only the host can tell whether it has the service, and this host offers no IServiceProviderIsService");
		}
	}

	public sealed class Unchanged
	{
		[Test]
		public async Task ARegisteredService_IsAService()
		{
			using ServiceProvider host = Host(_ => { });
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(TimeReporter))).IsTrue()
				.Because("the container registers it, whatever the host has");
		}

		[Test]
		public async Task AnUnregisteredTypeThatIsNotImported_IsNotAService()
		{
			using ServiceProvider host = Host(services => services.AddSingleton<IUnregistered>(new FixedUnregistered()));
			using AwaitenServiceProvider provider = new(new ClosedImportContainer.Root(new AwaitenServiceProviderExternalResolver(host)));

			await That(provider.IsService(typeof(IUnregistered))).IsFalse()
				.Because("the container routes only its imports to the host, so the host's registration is not reachable");
			await That(ProbeAgreement.Disagreements(provider, [typeof(IUnregistered), typeof(TimeReporter),])).IsEmpty();
		}

		private sealed class FixedUnregistered : IUnregistered;
	}
}
