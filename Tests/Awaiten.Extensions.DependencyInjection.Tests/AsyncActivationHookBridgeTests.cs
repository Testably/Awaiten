using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class AsyncActivationHookBridgeTests
{
	public sealed class PaymentTerminal
	{
		public int Connections { get; set; }
	}

	[Container]
	[Singleton<PaymentTerminal>(OnActivated = nameof(Connect))]
	public static partial class StrictShop
	{
		private static async Task Connect(PaymentTerminal terminal, CancellationToken cancellationToken)
		{
			await Task.Yield();
			terminal.Connections++;
		}
	}

	[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
	[Singleton<PaymentTerminal>(OnActivated = nameof(Connect))]
	public static partial class HostedShop
	{
		private static Task Connect(PaymentTerminal terminal)
		{
			terminal.Connections++;
			return Task.CompletedTask;
		}
	}

	[Test]
	public async Task AddAwaitenContainer_ProjectsTheHookedServiceAsTask()
	{
		ServiceCollection services = new();
		services.AddAwaitenContainer<StrictShop.Root>();
		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetService<PaymentTerminal>()).IsNull()
			.Because("the asynchronous hook leaves the terminal without a synchronous path, like a service with an async initializer convention");

		PaymentTerminal terminal = await provider.GetRequiredService<Task<PaymentTerminal>>();
		await That(terminal.Connections).IsEqualTo(1);
	}

	[Test]
	public async Task WithInitialization_AwaitsTheHookOnStartup()
	{
		using IHost host = new HostBuilder()
			.ConfigureServices(services => services.AddAwaitenContainer<HostedShop.Root>().WithInitialization())
			.Build();

		await host.StartAsync(TestContext.Current!.Execution.CancellationToken);

		PaymentTerminal terminal = host.Services.GetRequiredService<PaymentTerminal>();
		await That(terminal.Connections).IsEqualTo(1)
			.Because("the host warms the singleton on startup, so the synchronous resolve returns the connected instance without connecting again");

		await host.StopAsync(TestContext.Current!.Execution.CancellationToken);
	}
}
