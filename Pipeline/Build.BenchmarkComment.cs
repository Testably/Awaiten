using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Fallout.Common;
using Fallout.Common.IO;
using Octokit;
using Serilog;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	const string BenchmarkCommentHeading = "## :rocket: Benchmark Results";

	const double RelativeChangeThreshold = 0.15;

	/// <summary>
	///     Leaves room for the summary within the limit of a GitHub comment.
	/// </summary>
	const int MaxRawResultsLength = 45000;

	const string StrongWorse = "🔴";
	const string StrongBetter = "🟢";
	const string WeakWorse = "🔸";
	const string WeakBetter = "🔹";

	static readonly (string Library, string DisplayName)[] ComparedLibraries =
	[
		("MsDI", "MS.DI"), ("Autofac", "Autofac"), ("DryIoc", "DryIoc"), ("SimpleInjector", "Simple Injector"),
	];

	Target BenchmarkComment => _ => _
		.Executes(async () =>
		{
			// The artifacts are untrusted, because the pull request controls the code that produces them, so the pull
			// request to comment on comes from the event of the workflow run.
			int? prId = await BuildExtensions.ResolvePullRequestOfWorkflowRun(GithubToken);
			if (prId == null)
			{
				Log.Information("Skip writing a comment, as the workflow run belongs to no pull request.");
				return;
			}

			await "Benchmarks-".DownloadArtifactsStartingWith(ArtifactsDirectory, GithubToken);
			AbsolutePath resultsDirectory = ArtifactsDirectory / "Benchmarks" / "results";
			string[] reportFiles = resultsDirectory.DirectoryExists()
				? Directory.GetFiles(resultsDirectory, "*-report-github.md").Order(StringComparer.Ordinal).ToArray()
				: [];
			if (reportFiles.Length == 0)
			{
				Log.Information("Skip writing a comment, as the workflow run produced no report.");
				return;
			}

			BuildExtensions.GithubFile baselineFile =
				await BuildExtensions.ReadBranchFileAsync(BenchmarkLimitedDataPath, BenchmarkBranch, GithubToken);
			string body = CreateBenchmarkCommentBody(
					ReadBenchmarkMeasurements(resultsDirectory),
					ReadBenchmarkBaseline(baselineFile?.Content),
					BenchmarkReport.BuildRawResults(reportFiles, BenchmarkReport.DefaultColumnsToRemove))
				.AsUntrustedCommentContent();

			GitHubClient gitHubClient = new(new ProductHeaderValue("Fallout"));
			gitHubClient.Credentials = new Credentials(GithubToken);
			IReadOnlyList<IssueComment> comments =
				await gitHubClient.Issue.Comment.GetAllForIssue(BuildExtensions.Owner, BuildExtensions.Repo, prId.Value);
			IssueComment existingComment =
				comments.LastOrDefault(comment => comment.IsPipelineComment(BenchmarkCommentHeading));
			if (existingComment == null)
			{
				Log.Information($"Create comment:\n{body}");
				await gitHubClient.Issue.Comment.Create(BuildExtensions.Owner, BuildExtensions.Repo, prId.Value, body);
			}
			else
			{
				Log.Information($"Update comment:\n{body}");
				await gitHubClient.Issue.Comment.Update(BuildExtensions.Owner, BuildExtensions.Repo, existingComment.Id,
					body);
			}
		});

	static string CreateBenchmarkCommentBody(Dictionary<string, Dictionary<string, BenchmarkMeasurement>> measurements,
		BenchmarkBaseline baseline, string rawResults)
	{
		StringBuilder sb = new();
		sb.AppendLine(BenchmarkCommentHeading);
		sb.Append(CreateBenchmarkSummary(measurements, baseline));
		// The summary takes precedence over the raw results when the comment would get too long.
		sb.AppendLine(rawResults.Length <= MaxRawResultsLength
			? rawResults
			: "_The raw results do not fit into this comment, see the `Benchmarks-*` artifacts of the workflow run._");
		return sb.ToString();
	}

	static string CreateBenchmarkSummary(Dictionary<string, Dictionary<string, BenchmarkMeasurement>> measurements,
		BenchmarkBaseline baseline)
	{
		StringBuilder sb = new();
		if (baseline == null)
		{
			sb.AppendLine("_There are no results of `main` to compare to._");
			sb.AppendLine();
			return sb.ToString();
		}

		(string Library, string DisplayName)[] libraries = ComparedLibraries
			.Where(compared => measurements.Values.Any(benchmark => benchmark.ContainsKey(compared.Library)))
			.ToArray();
		sb.AppendLine(
			$"Compared to the latest run of `main` @ `{baseline.Sha.Substring(0, 8)}`. " +
			$"The time table marks when Awaiten gets more than {FormatPercent(RelativeChangeThreshold).TrimStart('+')} faster or slower, which can also come from a faster or slower runner. " +
			$"A relative change marks when Awaiten gets more than {FormatPercent(RelativeChangeThreshold).TrimStart('+')} faster or slower relative to " +
			$"{JoinNames(libraries.Select(compared => compared.DisplayName).ToArray())}: {StrongWorse} / {StrongBetter} against all of them, " +
			$"{WeakWorse} / {WeakBetter} against some.");
		sb.AppendLine();

		string[] benchmarks = measurements
			.Where(benchmark => benchmark.Value.ContainsKey("Awaiten"))
			.Select(benchmark => benchmark.Key)
			.Order(StringComparer.Ordinal)
			.ToArray();
		string[] newBenchmarks = benchmarks.Where(name => !baseline.Benchmarks.ContainsKey(name)).ToArray();
		string[] removedBenchmarks = baseline.Benchmarks.Keys.Except(benchmarks).Order(StringComparer.Ordinal).ToArray();
		List<string> memoryRows = new();
		List<string> timeRows = new();
		List<string> relativeRows = new();
		List<string> allRows = new();
		foreach (string name in benchmarks.Except(newBenchmarks))
		{
			BenchmarkMeasurement current = measurements[name]["Awaiten"];
			BaselineBenchmark main = baseline.Benchmarks[name];
			string[] relativeCells =
				CompareRelativePerformance(measurements[name], main, libraries, out bool relativeChanged);
			if (current.Allocated != main.Allocated)
			{
				memoryRows.Add(
					$"| `{name}` | {FormatBytes(main.Allocated)} | {FormatBytes(current.Allocated)} | {FormatMemoryChange(main.Allocated, current.Allocated)} |");
			}

			double timeChange = current.Mean / main.Mean - 1;
			if (Math.Abs(timeChange) > RelativeChangeThreshold)
			{
				timeRows.Add(
					$"| `{name}` | {FormatTime(main.Mean)} | {FormatTime(current.Mean)} | {(timeChange > 0 ? StrongWorse : StrongBetter)} {FormatPercent(timeChange)} |");
			}

			if (relativeChanged)
			{
				relativeRows.Add($"| `{name}` | {string.Join(" | ", relativeCells)} |");
			}

			allRows.Add(
				$"| `{name}` | {FormatTime(main.Mean)} → {FormatTime(current.Mean)} ({FormatPercent(current.Mean / main.Mean - 1)}) | " +
				$"{FormatBytes(main.Allocated)} → {FormatBytes(current.Allocated)} | {string.Join(" | ", relativeCells)} |");
		}

		string libraryHeaders = string.Join(" | ", libraries.Select(compared => $"vs {compared.DisplayName}"));
		string libraryAlignments = string.Concat(libraries.Select(_ => "--:|"));
		if (memoryRows.Count == 0 && timeRows.Count == 0 && relativeRows.Count == 0)
		{
			sb.AppendLine($"✅ No changes in memory, time or relative performance ({allRows.Count} benchmarks)");
			sb.AppendLine();
		}

		if (memoryRows.Count > 0)
		{
			sb.AppendLine("**Memory**");
			sb.AppendLine();
			sb.AppendLine("| Benchmark | main | PR | Δ |");
			sb.AppendLine("|---|--:|--:|--:|");
			memoryRows.ForEach(row => sb.AppendLine(row));
			sb.AppendLine();
		}

		if (timeRows.Count > 0)
		{
			sb.AppendLine("**Time** (Awaiten, latest `main` run → PR)");
			sb.AppendLine();
			sb.AppendLine("| Benchmark | main | PR | Δ |");
			sb.AppendLine("|---|--:|--:|--:|");
			timeRows.ForEach(row => sb.AppendLine(row));
			sb.AppendLine();
		}

		if (relativeRows.Count > 0)
		{
			sb.AppendLine("**Relative performance** (how many times as long the other container takes as Awaiten, main → PR)");
			sb.AppendLine();
			sb.AppendLine($"| Benchmark | {libraryHeaders} |");
			sb.AppendLine($"|---|{libraryAlignments}");
			relativeRows.ForEach(row => sb.AppendLine(row));
			sb.AppendLine();
		}

		if (newBenchmarks.Length > 0)
		{
			sb.AppendLine($"New, without results on `main`: {string.Join(", ", newBenchmarks.Select(name => $"`{name}`"))}");
			sb.AppendLine();
		}

		if (removedBenchmarks.Length > 0)
		{
			sb.AppendLine($"Removed: {string.Join(", ", removedBenchmarks.Select(name => $"`{name}`"))}");
			sb.AppendLine();
		}

		sb.AppendLine("<details>");
		sb.AppendLine("<summary>All benchmarks compared to main</summary>");
		sb.AppendLine();
		sb.AppendLine($"| Benchmark | Time | Memory | {libraryHeaders} |");
		sb.AppendLine($"|---|--:|--:|{libraryAlignments}");
		allRows.ForEach(row => sb.AppendLine(row));
		sb.AppendLine();
		sb.AppendLine("</details>");
		sb.AppendLine();
		return sb.ToString();
	}

	/// <summary>
	///     Compares the time of Awaiten relative to each of the <paramref name="libraries" /> with <c>main</c>.
	/// </summary>
	/// <remarks>
	///     The ratio of two benchmarks of the same run does not depend on the speed of the runner. A change against
	///     all libraries in the same direction is most likely caused by Awaiten and gets a stronger marker.
	/// </remarks>
	static string[] CompareRelativePerformance(Dictionary<string, BenchmarkMeasurement> measurements,
		BaselineBenchmark main, (string Library, string DisplayName)[] libraries, out bool changed)
	{
		BenchmarkMeasurement awaiten = measurements["Awaiten"];
		double?[] ratios = libraries
			.Select(compared => measurements.TryGetValue(compared.Library, out BenchmarkMeasurement other)
				? awaiten.Mean / other.Mean
				: (double?)null)
			.ToArray();
		double?[] relativeChanges = libraries
			.Select((compared, index) => main.Ratios.TryGetValue(compared.Library, out double mainRatio) && ratios[index] != null
				? ratios[index] / mainRatio - 1
				: null)
			.ToArray();
		bool[] exceeds = relativeChanges
			.Select(relativeChange => relativeChange != null && Math.Abs(relativeChange.Value) > RelativeChangeThreshold)
			.ToArray();
		bool allAgree = libraries.Length > 1 && exceeds.All(x => x) &&
		                relativeChanges.Select(relativeChange => Math.Sign(relativeChange!.Value)).Distinct().Count() == 1;
		changed = exceeds.Any(x => x);
		return libraries.Select((compared, index) =>
		{
			if (ratios[index] == null)
			{
				return "";
			}

			string cell = main.Ratios.TryGetValue(compared.Library, out double mainRatio)
				? $"{FormatRatio(mainRatio)} → {FormatRatio(ratios[index]!.Value)}"
				: $"→ {FormatRatio(ratios[index]!.Value)}";
			if (exceeds[index])
			{
				double relativeChange = relativeChanges[index]!.Value;
				string marker = (relativeChange > 0, allAgree) switch
				{
					(true, true) => StrongWorse,
					(true, false) => WeakWorse,
					(false, true) => StrongBetter,
					(false, false) => WeakBetter,
				};
				cell += $" {marker} {FormatPercent(relativeChange)}";
			}

			return cell;
		}).ToArray();
	}

	/// <summary>
	///     Reads the results of the pull request, keyed like the benchmark data of <c>main</c>.
	/// </summary>
	static Dictionary<string, Dictionary<string, BenchmarkMeasurement>> ReadBenchmarkMeasurements(
		AbsolutePath resultsDirectory)
	{
		Dictionary<string, Dictionary<string, BenchmarkMeasurement>> measurements = new();
		foreach (string file in Directory.GetFiles(resultsDirectory, "*-report-full-compressed.json"))
		{
			PageBenchmarkReportGenerator.BenchmarkReport report =
				JsonSerializer.Deserialize<PageBenchmarkReportGenerator.BenchmarkReport>(File.ReadAllText(file));
			foreach (PageBenchmarkReportGenerator.BenchmarkReport.Benchmark benchmark in report.Benchmarks)
			{
				if (benchmark.Statistics == null ||
				    !PageBenchmarkReportGenerator.PageReportData.ParseMethod(benchmark.Method, out string scenario,
					    out string library))
				{
					continue;
				}

				string name = PageBenchmarkReportGenerator.PageReportData.BuildChartKey(scenario, benchmark.Parameters);
				if (!measurements.TryGetValue(name, out Dictionary<string, BenchmarkMeasurement> libraries))
				{
					libraries = new Dictionary<string, BenchmarkMeasurement>();
					measurements[name] = libraries;
				}

				libraries[library] = new BenchmarkMeasurement(benchmark.Statistics.Mean, benchmark.Metrics
					.Where(metric => metric.Descriptor.Id == "Allocated Memory")
					.Select(metric => metric.Value)
					.FirstOrDefault(double.NaN));
			}
		}

		return measurements;
	}

	/// <summary>
	///     Reads the results of <c>main</c> from the benchmark data that the docs site shows.
	/// </summary>
	/// <remarks>
	///     A benchmark that was removed from <c>main</c> is ignored, as its last result belongs to an older commit than
	///     the latest one.
	/// </remarks>
	static BenchmarkBaseline ReadBenchmarkBaseline(string content)
	{
		if (content?.StartsWith(PageBenchmarkReportGenerator.FilePrefix, StringComparison.Ordinal) != true)
		{
			return null;
		}

		PageBenchmarkReportGenerator.PageReportData data =
			JsonSerializer.Deserialize<PageBenchmarkReportGenerator.PageReportData>(
				content.Substring(PageBenchmarkReportGenerator.FilePrefix.Length));
		List<(string Sha, string Name, BaselineBenchmark Benchmark)> benchmarks = new();
		foreach ((string name, PageBenchmarkReportGenerator.PageReport report) in data)
		{
			double[] times = DatasetValues(report, "Awaiten time");
			double[] allocated = DatasetValues(report, "Awaiten memory");
			if (report.Commits.Count == 0 || times.Length == 0 || allocated.Length == 0)
			{
				continue;
			}

			Dictionary<string, double> ratios = new();
			foreach ((string library, string _) in ComparedLibraries)
			{
				double[] otherTimes = DatasetValues(report, $"{library} time");
				if (otherTimes.Length > 0)
				{
					// The libraries of a run are appended together, so their last values belong to the same run.
					ratios[library] = times[^1] / otherTimes[^1];
				}
			}

			benchmarks.Add((report.Commits[^1].Sha, name, new BaselineBenchmark(times[^1], allocated[^1], ratios)));
		}

		if (benchmarks.Count == 0)
		{
			return null;
		}

		string latestSha = benchmarks.GroupBy(benchmark => benchmark.Sha).MaxBy(group => group.Count())!.Key;
		return new BenchmarkBaseline(latestSha, benchmarks
			.Where(benchmark => benchmark.Sha == latestSha)
			.ToDictionary(benchmark => benchmark.Name, benchmark => benchmark.Benchmark));
	}

	static double[] DatasetValues(PageBenchmarkReportGenerator.PageReport report, string label)
		=> report.Datasets.FirstOrDefault(dataset => dataset.Label == label)?.Data.ToArray() ?? [];

	static string JoinNames(string[] names)
		=> names.Length <= 1
			? string.Concat(names)
			: $"{string.Join(", ", names.Take(names.Length - 1))} and {names[^1]}";

	static string FormatPercent(double relativeChange)
		=> (relativeChange * 100).ToString("+0;-0;±0", CultureInfo.InvariantCulture) + "%";

	/// <summary>
	///     Formats the <paramref name="ratio" /> of Awaiten to another library as the factor of the other library, as
	///     the docs site does, which keeps its precision when Awaiten is much faster.
	/// </summary>
	static string FormatRatio(double ratio)
		=> (1 / ratio).ToString(ratio > 0.1 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + "×";

	static string FormatBytes(double bytes)
		=> bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";

	static string FormatMemoryChange(double main, double current)
	{
		double difference = current - main;
		string change =
			$"{(difference > 0 ? StrongWorse : StrongBetter)} {difference.ToString("+#,0;-#,0", CultureInfo.InvariantCulture)} B";
		return main == 0 ? change : $"{change} ({FormatPercent(current / main - 1)})";
	}

	static string FormatTime(double nanoseconds)
		=> nanoseconds switch
		{
			< 1_000 => nanoseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ns",
			< 1_000_000 => (nanoseconds / 1_000).ToString("0.00", CultureInfo.InvariantCulture) + " μs",
			_ => (nanoseconds / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + " ms",
		};

	sealed record BenchmarkMeasurement(double Mean, double Allocated);

	sealed record BaselineBenchmark(double Mean, double Allocated, Dictionary<string, double> Ratios);

	sealed record BenchmarkBaseline(string Sha, Dictionary<string, BaselineBenchmark> Benchmarks);
}
