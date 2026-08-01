namespace Awaiten.AotSample.Domain;

/// <summary>
///     A transient Awaiten service that mixes an Awaiten-owned dependency (<see cref="IClock" />) with a
///     host-owned one (<see cref="Banner" />, resolved across the seam).
/// </summary>
public sealed class Report
{
	private readonly Banner _banner;
	private readonly IClock _clock;

	public Report(IClock clock, Banner banner)
	{
		_clock = clock;
		_banner = banner;
	}

	public string Render() => $"{_banner.Text} @ {_clock.Today()}";
}
