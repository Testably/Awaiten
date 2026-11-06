using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     A container that extends a production container is projected into Microsoft.Extensions.DependencyInjection like
///     any other: the replacements are what the host sees, and a derived <c>[ImportService&lt;T&gt;]</c> draws the
///     replaced service from the host.
/// </summary>
public sealed partial class ExtendedContainerTests
{
	[Test]
	public async Task Projection_ExposesTheReplacementsInsteadOfTheInheritedRegistrations()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<TestShop.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetServices<INotifier>().Select(notifier => notifier.GetType())).IsEqualTo([typeof(FakeNotifier),])
			.Because("the inherited notifiers are replaced, so the host's collection holds only the test's");
	}

	[Test]
	public async Task DerivedImportService_DrawsTheReplacedServiceFromTheHost()
	{
		FakeClock clock = new();
		ServiceCollection services = new();
		services.AddSingleton<IClock>(clock);
		services.AddAwaitenContainer<TestShop.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetRequiredService<Barista>().Clock).IsSameAs(clock);
	}

	public interface IClock;
	public sealed class SystemClock : IClock;
	public sealed class FakeClock : IClock;

	public sealed class Barista(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	public interface INotifier;
	public sealed class EmailNotifier : INotifier;
	public sealed class SmsNotifier : INotifier;
	public sealed class FakeNotifier : INotifier;

	[Container]
	[Singleton<SystemClock, IClock>]
	[Singleton<Barista>]
	[Singleton<EmailNotifier, INotifier>]
	[Singleton<SmsNotifier, INotifier>]
	public static partial class Shop;

	[Container(Extends = typeof(Shop))]
	[ImportService<IClock>]
	[Singleton<FakeNotifier, INotifier>]
	public static partial class TestShop;
}
