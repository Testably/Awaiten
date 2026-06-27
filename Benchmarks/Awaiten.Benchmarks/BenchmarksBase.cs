using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Awaiten.Benchmarks;

/// <summary>
///     Shared configuration for the resolution benchmarks: a medium in-process job (fast enough to run on
///     a CI runner per scenario, stable enough to compare), the GitHub-flavored markdown exporter the
///     pipeline post-processes into a PR comment, and the memory diagnoser. Each scenario lives in its own
///     class — run on its own CI runner via the benchmark matrix — with one <c>[Benchmark]</c> method per
///     DI framework, named <c>{Scenario}_{Library}</c> so the report tooling can group them.
/// </summary>
[Config(typeof(Config))]
[MarkdownExporterAttribute.GitHub]
[MemoryDiagnoser]
public abstract class BenchmarksBase
{
	// The constructor is invoked by BenchmarkDotNet via [Config(typeof(Config))], not from code.
#pragma warning disable S1144
	private sealed class Config : ManualConfig
	{
		public Config()
		{
			AddJob(Job.MediumRun
				.WithLaunchCount(1)
				.WithToolchain(InProcessEmitToolchain.Instance)
				.WithId("InProcess"));
		}
	}
#pragma warning restore S1144
}
