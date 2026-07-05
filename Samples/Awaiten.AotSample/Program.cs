using System;
using System.Threading;
using System.Threading.Tasks;
using Awaiten;
using Awaiten.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.AotSample;

public interface IClock
{
	string Today();
}

public sealed class SystemClock : IClock
{
	public string Today() => "2026-06-24";
}

/// <summary>A host-owned (external) service, registered directly in the service collection.</summary>
public sealed class Banner
{
	public string Text { get; } = "Awaiten on AOT";
}

/// <summary>
///     A transient Awaiten service that mixes an Awaiten-owned dependency (<see cref="IClock" />) with a
///     host-owned one (<see cref="Banner" />, resolved across the seam).
/// </summary>
public sealed class Report
{
	private readonly IClock _clock;
	private readonly Banner _banner;

	public Report(IClock clock, [FromServices] Banner banner)
	{
		_clock = clock;
		_banner = banner;
	}

	public string Render() => $"{_banner.Text} @ {_clock.Today()}";
}

/// <summary>
///     An async-initialized Awaiten service. Because it is <see cref="IAsyncInitializable" />, the bridge has
///     no synchronous resolution path for it and projects it as <c>Task&lt;Warmup&gt;</c> - the path that
///     builds the closed <c>Task&lt;T&gt;</c> and its converter from generator-emitted metadata, so it
///     publishes natively without reflection.
/// </summary>
public sealed class Warmup : IAsyncInitializable
{
	public bool Ready { get; private set; }

	public Task InitializeAsync(CancellationToken cancellationToken)
	{
		Ready = true;
		return Task.CompletedTask;
	}
}

[Container]
[Singleton<SystemClock, IClock>]
[Transient<Report>]
[Singleton<Warmup>]
public static partial class SampleContainer;

public static class Program
{
	public static async Task<int> Main()
	{
		ServiceCollection services = new();
		services.AddSingleton<Banner>();
		services.AddGeneratedContainer<SampleContainer.Root>();

		using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
		provider.VerifyAwaitenContainers();

		using IServiceScope scope = provider.CreateScope();
		Report report = scope.ServiceProvider.GetRequiredService<Report>();
		string rendered = report.Render();
		Console.WriteLine(rendered);

		// The async path: an IAsyncInitializable service is bridged as Task<T>, resolved through ResolveAsync
		// and initialized exactly once. This is what previously forced runtime MakeGenericType/MakeGenericMethod.
		Warmup warmup = await scope.ServiceProvider.GetRequiredService<Task<Warmup>>();
		Console.WriteLine($"warmup ready: {warmup.Ready}");

		return rendered == "Awaiten on AOT @ 2026-06-24" && warmup.Ready ? 0 : 1;
	}
}
