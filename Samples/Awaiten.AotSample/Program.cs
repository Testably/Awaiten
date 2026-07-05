using System;
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

[Container]
[Singleton<SystemClock, IClock>]
[Transient<Report>]
public static partial class SampleContainer;

public static class Program
{
	public static int Main()
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

		return rendered == "Awaiten on AOT @ 2026-06-24" ? 0 : 1;
	}
}
