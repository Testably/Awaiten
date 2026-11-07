using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Fallout.Common;
using Fallout.Common.IO;
using Serilog;
using static Build.PageBenchmarkReportGenerator;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	const int DocsBenchmarkHistoryLength = 16;

	static readonly string[] DocsBenchmarkLibraries =
		["Awaiten", "MsDI", "Autofac", "DryIoc", "SimpleInjector",];

	static readonly Dictionary<string, string> DocsBenchmarkPackages = new()
	{
		["Microsoft.Extensions.DependencyInjection"] = "MsDI",
		["Autofac"] = "Autofac",
		["DryIoc.dll"] = "DryIoc",
		["SimpleInjector"] = "SimpleInjector",
	};

	/// <summary>
	///     The release note categories in page order. Breaking changes of any type come first; other types
	///     (<c>chore</c>, <c>refactor</c>, <c>docs</c>, <c>test</c>, ...) are not user-facing and are left out.
	/// </summary>
	static readonly (string Type, string Category)[] ChangelogCategories =
	[
		("!", "breaking"),
		("feat", "feature"),
		("fix", "fix"),
		("perf", "performance"),
	];

	static readonly Regex ReleaseItemRegex = new(
		@"^\* (?<title>.+?) by @\S+ in (?<url>https://github\.com/\S+/pull/(?<number>\d+))\s*$", RegexOptions.Compiled);

	static readonly Regex ConventionalTitleRegex = new(
		@"^(?<type>[a-zA-Z]+)(\([^)]*\))?(?<breaking>!)?:\s*(?<text>.+)$", RegexOptions.Compiled);

	static readonly JsonSerializerOptions DocsDataJsonOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true,
	};

	Target DocsData => _ => _
		.Description("Refreshes the benchmark snapshot and the changelog that the documentation site renders.")
		.Executes(async () =>
		{
			AbsolutePath dataDirectory = RootDirectory / "Docs" / "site" / "src" / "data";
			await WriteDocsData(dataDirectory / "benchmarks.json", await ReadDocsBenchmarks());
			await WriteDocsData(dataDirectory / "changelog.json", new DocsChangelog(await ReadReleases()));
		});

	static async Task WriteDocsData<T>(AbsolutePath path, T data)
	{
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, DocsDataJsonOptions) + "\n");
		Log.Information("Wrote {Path}", path);
	}

	/// <summary>
	///     Reduces the latest entries of the <c>benchmarks</c> branch to the shape the <c>AwaitenBenchmarkResult</c>
	///     component renders. The <c>Size</c> variants of a scenario become the parameters of one scenario.
	/// </summary>
	async Task<DocsBenchmarks> ReadDocsBenchmarks()
	{
		BuildExtensions.GithubFile file =
			await BuildExtensions.ReadBranchFileAsync(BenchmarkLimitedDataPath, BenchmarkBranch, GithubToken)
			?? throw new InvalidOperationException(
				$"'{BenchmarkLimitedDataPath}' was not found on the '{BenchmarkBranch}' branch.");
		PageReportData data = JsonSerializer.Deserialize<PageReportData>(
			file.Content.Substring(FilePrefix.Length).TrimEnd(';', '\r', '\n', ' '));
		CommitInfo latest = data.Values.Where(report => report.Commits.Count > 0)
			                    .Select(report => report.Commits[^1]).FirstOrDefault()
		                    ?? throw new InvalidOperationException("The benchmark data has no commits.");

		Dictionary<string, DocsBenchmarkScenario> scenarios = new();
		foreach ((string chartKey, PageReport report) in data.OrderBy(entry => entry.Key, StringComparer.Ordinal))
		{
			Dictionary<string, DocsBenchmarkSample> samples = new();
			foreach (string library in DocsBenchmarkLibraries)
			{
				List<double> time = report.Datasets.FirstOrDefault(d => d.Label == $"{library} time")?.Data;
				List<double> memory = report.Datasets.FirstOrDefault(d => d.Label == $"{library} memory")?.Data;
				if (time is {Count: > 0,})
				{
					samples[library] = new DocsBenchmarkSample(
						Math.Round(time[^1], 1),
						memory is {Count: > 0,} ? (long)Math.Round(memory[^1]) : null,
						new DocsBenchmarkHistory(
							time.TakeLast(DocsBenchmarkHistoryLength).Select(value => Math.Round(value, 1)).ToArray(),
							memory?.TakeLast(DocsBenchmarkHistoryLength).Select(value => (long)Math.Round(value)).ToArray() ?? []));
				}
			}

			if (samples.Count == 0)
			{
				continue;
			}

			int open = chartKey.IndexOf('(');
			(string scenario, string parameter) = open > 0 && chartKey.EndsWith(')')
				? (chartKey[..open].TrimEnd(), chartKey[(open + 1)..^1].Trim())
				: (chartKey, "");
			if (!scenarios.TryGetValue(scenario, out DocsBenchmarkScenario entry))
			{
				entry = new DocsBenchmarkScenario([], new Dictionary<string, Dictionary<string, DocsBenchmarkSample>>());
				scenarios[scenario] = entry;
			}

			entry.Parameters.Add(parameter);
			entry.Samples[parameter] = samples;
		}

		foreach (DocsBenchmarkScenario scenario in scenarios.Values)
		{
			scenario.Parameters.Sort((a, b) => ParameterSize(a).CompareTo(ParameterSize(b)));
		}

		DateTimeOffset date = DateTimeOffset.ParseExact(latest.Date,
			["ddd MMM d HH:mm:ss yyyy zzz", "ddd MMM dd HH:mm:ss yyyy zzz",], CultureInfo.InvariantCulture);
		return new DocsBenchmarks(
			new DocsBenchmarkCommit(latest.Sha, latest.Sha[..8], date.ToString("yyyy-MM-dd"), latest.Message),
			new DocsBenchmarkEnvironment("ubuntu-latest", "BenchmarkDotNet MediumRun (in-process)"),
			scenarios,
			await ReadDocsBenchmarkVersions(latest.Sha));
	}

	/// <summary>
	///     Reads the versions of the compared containers at the measured commit, so that they always match the numbers.
	/// </summary>
	static async Task<Dictionary<string, string>> ReadDocsBenchmarkVersions(string sha)
	{
		using HttpClient client = new();
		string props = await client.GetStringAsync(
			$"https://raw.githubusercontent.com/{BuildExtensions.Owner}/{BuildExtensions.Repo}/{sha}/Directory.Packages.props");
		return XDocument.Parse(props).Descendants("PackageVersion")
			.Where(package => DocsBenchmarkPackages.ContainsKey((string)package.Attribute("Include") ?? ""))
			.ToDictionary(package => DocsBenchmarkPackages[(string)package.Attribute("Include")!],
				package => (string)package.Attribute("Version") ?? "");
	}

	/// <summary>
	///     Sorts the <c>Size=N</c> parameters by their number, so that the tabs read 8, 64, 256.
	/// </summary>
	static int ParameterSize(string parameter)
		=> int.TryParse(parameter[(parameter.LastIndexOf('=') + 1)..], out int size) ? size : -1;

	/// <summary>
	///     Reads the published releases with at least one user-facing change, newest first.
	/// </summary>
	async Task<List<DocsRelease>> ReadReleases()
	{
		using HttpClient client = new();
		client.DefaultRequestHeaders.UserAgent.ParseAdd(BuildExtensions.Repo);
		if (!string.IsNullOrEmpty(GithubToken))
		{
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GithubToken);
		}

		List<GithubRelease> releases = [];
		for (int page = 1; ; page++)
		{
			string json = await client.GetStringAsync(
				$"https://api.github.com/repos/{BuildExtensions.Owner}/{BuildExtensions.Repo}/releases?per_page=100&page={page}");
			List<GithubRelease> batch = JsonSerializer.Deserialize<List<GithubRelease>>(json) ?? [];
			releases.AddRange(batch.Where(release => !release.Draft));
			if (batch.Count < 100)
			{
				break;
			}
		}

		return releases
			.OrderByDescending(release => release.PublishedAt)
			.Select(release => new DocsRelease(
				release.TagName.TrimStart('v'),
				release.PublishedAt.ToString("yyyy-MM-dd"),
				release.Prerelease,
				release.HtmlUrl,
				CountCommits(release.Body ?? ""),
				ReadChangelogItems(release.Body ?? "")))
			.Where(release => release.Items.Count > 0)
			.ToList();
	}

	/// <summary>
	///     Counts all pull requests in the generated release notes, including the ones that are not user-facing.
	///     They are squash-merged, so this is the number of commits in the release.
	/// </summary>
	static int CountCommits(string body)
		=> body.Split('\n').Count(line => ReleaseItemRegex.IsMatch(line.TrimEnd('\r')));

	/// <summary>
	///     Reads the pull requests that GitHub lists in generated release notes, in category order. The
	///     "New contributors" lines and the pull requests of other types don't match and are skipped.
	/// </summary>
	static List<DocsChangelogItem> ReadChangelogItems(string body)
	{
		List<(string Type, DocsChangelogItem Item)> items = body.Split('\n')
			.Select(line => ReleaseItemRegex.Match(line.TrimEnd('\r')))
			.Where(item => item.Success)
			.Select(item => (Item: item, Title: ConventionalTitleRegex.Match(item.Groups["title"].Value)))
			.Where(entry => entry.Title.Success)
			.Select(entry => (
				Type: entry.Title.Groups["breaking"].Success ? "!" : entry.Title.Groups["type"].Value.ToLowerInvariant(),
				Item: new DocsChangelogItem(
					"",
					Capitalize(entry.Title.Groups["text"].Value),
					int.Parse(entry.Item.Groups["number"].Value),
					entry.Item.Groups["url"].Value)))
			.ToList();
		return ChangelogCategories
			.SelectMany(category => items
				.Where(entry => entry.Type == category.Type)
				.Select(entry => entry.Item with {Category = category.Category,}))
			.ToList();
	}

	static string Capitalize(string text)
		=> text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

	sealed record DocsBenchmarks(
		DocsBenchmarkCommit CapturedAt,
		DocsBenchmarkEnvironment Environment,
		Dictionary<string, DocsBenchmarkScenario> Benchmarks,
		Dictionary<string, string> Versions);

	sealed record DocsBenchmarkCommit(string Sha, string ShortSha, string Date, string Message);

	sealed record DocsBenchmarkEnvironment(string Runner, string Toolchain);

	sealed record DocsBenchmarkScenario(
		List<string> Parameters,
		Dictionary<string, Dictionary<string, DocsBenchmarkSample>> Samples);

	sealed record DocsBenchmarkSample(double TimeNs, long? MemoryBytes, DocsBenchmarkHistory History);

	sealed record DocsBenchmarkHistory(double[] TimeNs, long[] MemoryBytes);

	sealed record DocsChangelog(List<DocsRelease> Releases);

	sealed record DocsRelease(
		string Version,
		string Date,
		bool Prerelease,
		string Url,
		int Commits,
		List<DocsChangelogItem> Items);

	sealed record DocsChangelogItem(string Category, string Text, int Number, string Url);

	sealed class GithubRelease
	{
		[JsonPropertyName("tag_name")] public string TagName { get; init; } = "";
		[JsonPropertyName("html_url")] public string HtmlUrl { get; init; } = "";
		[JsonPropertyName("body")] public string Body { get; init; }
		[JsonPropertyName("draft")] public bool Draft { get; init; }
		[JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
		[JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; init; }
	}
}
