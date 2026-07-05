#if NET
using System.Linq;
using System.Net.Http;
using System.Threading;
using Awaiten.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.ExampleTests;

// These examples read as a consumer would write them, so calls omit the xUnit test-cancellation token
// (xUnit1051). That token is test plumbing that would obscure the usage pattern being documented.
#pragma warning disable xUnit1051

/// <summary>
///     Examples of consuming a generated Awaiten container through the
///     <c>Awaiten.Extensions.DependencyInjection</c> bridge, documenting the three supported use cases:
///     hosting inside an ASP.NET Core app with <see cref="AwaitenServiceProviderFactory{TRoot}" />,
///     projecting into a plain <see cref="IServiceCollection" />, and adapting the container to an
///     <see cref="IServiceProvider" /> with <see cref="AwaitenServiceProvider" />.
/// </summary>
/// <remarks>
///     ASP.NET Core ships only on .NET (net8.0+), so this whole file is compiled out on net48.
/// </remarks>
public partial class AspNetCoreExampleTests
{
	/// <summary>
	///     Hosting use case: hand an <see cref="AwaitenServiceProviderFactory{TRoot}" /> to the host with
	///     <c>UseServiceProviderFactory</c>, and the container's services resolve into the app's dependency
	///     injection. Here an endpoint injects the Awaiten <see cref="IGreeter" /> singleton from its handler.
	/// </summary>
	[Fact]
	public async Task AspNetCore_InjectsAwaitenSingletonIntoEndpoint()
	{
		WebApplicationBuilder builder = CreateBuilder();
		builder.Host.UseServiceProviderFactory(new AwaitenServiceProviderFactory<WebContainer.Root>());
		await using WebApplication app = builder.Build();

		app.MapGet("/greeting", (IGreeter greeter) => greeter.Greet("world"));
		await app.StartAsync();

		using HttpClient client = ClientFor(app);
		string body = await client.GetStringAsync("/greeting");

		await That(body).IsEqualTo("Hello, world!");
	}

	/// <summary>
	///     One Awaiten scope aligns to each ASP.NET Core request scope, so an Awaiten scoped service is one
	///     instance within a request and fresh across requests, the same lifetime a native scoped service has.
	/// </summary>
	[Fact]
	public async Task AspNetCore_ResolvesScopedServiceOncePerRequest()
	{
		WebApplicationBuilder builder = CreateBuilder();
		builder.Host.UseServiceProviderFactory(new AwaitenServiceProviderFactory<WebContainer.Root>());
		await using WebApplication app = builder.Build();

		app.MapGet("/request-id", (RequestId id) => id.Value.ToString());
		await app.StartAsync();

		using HttpClient client = ClientFor(app);
		string first = await client.GetStringAsync("/request-id");
		string second = await client.GetStringAsync("/request-id");

		await That(first).IsNotEqualTo(second)
			.Because("the scoped service is created once per request scope, so two requests see two instances");
	}

	/// <summary>
	///     A service that requires asynchronous initialization has no synchronous resolution path, so the bridge
	///     projects it as <c>Task&lt;T&gt;</c>. Warm it once at startup (resolve
	///     <see cref="IAwaitenContainerMetadata" /> and await <c>InitializeAsync</c>), then endpoints await the
	///     projected <c>Task&lt;T&gt;</c> to obtain the initialized instance.
	/// </summary>
	[Fact]
	public async Task AspNetCore_WarmsAndServesAsyncInitializedService()
	{
		WebApplicationBuilder builder = CreateBuilder();
		builder.Host.UseServiceProviderFactory(new AwaitenServiceProviderFactory<WebContainer.Root>());
		await using WebApplication app = builder.Build();

		app.MapGet("/report", async (IServiceProvider services)
			=> (await services.GetRequiredService<Task<Report>>()).IsReady ? "ready" : "pending");
		await app.StartAsync();

		// Warm the async-initialized singletons once, so the first request hands back a ready instance instead
		// of paying initialization on the request path.
		await app.Services.GetRequiredService<IAwaitenContainerMetadata>().InitializeAsync();

		using HttpClient client = ClientFor(app);
		string body = await client.GetStringAsync("/report");

		await That(body).IsEqualTo("ready");
	}

	/// <summary>
	///     Standalone use case: project the container into a plain <see cref="IServiceCollection" /> with
	///     <c>AddGeneratedContainer</c>. Awaiten lifetimes map to the matching <c>ServiceLifetime</c>.
	/// </summary>
	[Fact]
	public async Task Collection_ProjectsServicesWithTheirLifetimes()
	{
		ServiceCollection services = new();
		services.AddGeneratedContainer<WebContainer.Root>();
		await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

		await That(provider.GetRequiredService<IGreeter>().Greet("Ada")).IsEqualTo("Hello, Ada!");

		using IServiceScope scope = provider.CreateScope();
		RequestId first = scope.ServiceProvider.GetRequiredService<RequestId>();
		RequestId second = scope.ServiceProvider.GetRequiredService<RequestId>();
		await That(first).IsSameAs(second)
			.Because("a scoped service is a single instance within one scope");
	}

	/// <summary>
	///     Standalone use case with end-to-end ownership: wrap the container in an
	///     <see cref="AwaitenServiceProvider" />. Here Awaiten owns disposal, so disposing the provider (via
	///     <c>await using</c>) disposes the container and the singletons it built.
	/// </summary>
	[Fact]
	public async Task Provider_AsWholeContainer_DisposesWhatItBuilt()
	{
		WebContainer.Root container = new();
		DisposableProbe probe;
		await using (AwaitenServiceProvider provider = new(container))
		{
			probe = (DisposableProbe)provider.GetService(typeof(DisposableProbe))!;
			await That(probe.Disposed).IsFalse();
		}

		await That(probe.Disposed).IsTrue();
	}

	// Test-specific setup only: bind to a loopback port the OS picks, so the example runs without a fixed
	// port. Each test then wires the AwaitenServiceProviderFactory and builds, so that step stays visible.
	private static WebApplicationBuilder CreateBuilder()
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		return builder;
	}

	private static HttpClient ClientFor(WebApplication app)
	{
		string address = app.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new HttpClient { BaseAddress = new Uri(address) };
	}

	/// <summary>
	///     The Awaiten container hosted by the examples: a singleton (<see cref="Greeter" />), a scoped service
	///     (<see cref="RequestId" />), an async-initialized singleton (<see cref="Report" />), and a disposable
	///     singleton (<see cref="DisposableProbe" />).
	/// </summary>
	[Container]
	[Singleton<Greeter, IGreeter>]
	[Scoped<RequestId>]
	[Singleton<Report>]
	[Singleton<DisposableProbe>]
	public static partial class WebContainer;
}

public interface IGreeter
{
	string Greet(string name);
}

public sealed class Greeter : IGreeter
{
	public string Greet(string name) => $"Hello, {name}!";
}

// Scoped: a distinct value per scope (per ASP.NET Core request), stable within it.
public sealed class RequestId
{
	public Guid Value { get; } = Guid.NewGuid();
}

// Async-initialized singleton: reached through the Task<T> projection.
public sealed class Report : IAsyncInitializable
{
	public bool IsReady { get; private set; }

	public Task InitializeAsync(CancellationToken cancellationToken)
	{
		IsReady = true;
		return Task.CompletedTask;
	}
}

public sealed class DisposableProbe : IDisposable
{
	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
}
#pragma warning restore xUnit1051
#endif
