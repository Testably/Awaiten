using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Tools.Git;
using Octokit;
using Serilog;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	private const string BenchmarkBranch = "benchmarks";
	private const string BenchmarkDataPath = "Docs/pages/static/js/data.js";
	private const string BenchmarkLimitedDataPath = "Docs/pages/static/js/limited-data.js";
	private const int BenchmarkLimit = 50;

	[Parameter("Filter for BenchmarkDotNet - Default is '*'")] readonly string BenchmarkFilter = "*";

	Target BenchmarkDotNet => _ => _
		.Executes(() =>
		{
			AbsolutePath benchmarkDirectory = ArtifactsDirectory / "Benchmarks";
			benchmarkDirectory.CreateOrCleanDirectory();

			DotNetBuild(s => s
				.SetProjectFile(Solution.Benchmarks.Awaiten_Benchmarks)
				.SetConfiguration(Configuration.Release)
				.EnableNoLogo());

			DotNet(
				$"{Solution.Benchmarks.Awaiten_Benchmarks.Name}.dll --exporters json --filter {BenchmarkFilter} --artifacts \"{benchmarkDirectory}\"",
				Solution.Benchmarks.Awaiten_Benchmarks.Directory / "bin" / "Release");
		});

	Target BenchmarkResult => _ => _
		.After(BenchmarkDotNet)
		.Executes(async () =>
		{
			if (!Directory.Exists(ArtifactsDirectory / "Benchmarks" / "results"))
			{
				Log.Information("Skip benchmark result, because no results directory was generated.");
				return;
			}

			string[] files = Directory.GetFiles(ArtifactsDirectory / "Benchmarks" / "results", "*-report-github.md");
			if (files.Length == 0)
			{
				Log.Information("Skip benchmark result, because no report file was generated.");
				return;
			}

			foreach (string file in files)
			{
				string fileContent = await File.ReadAllTextAsync(file);
				Log.Information("Report ({FileName}):\n {FileContent}", Path.GetFileName(file), fileContent);
			}
		});

	Target Benchmarks => _ => _
		.DependsOn(BenchmarkDotNet)
		.DependsOn(BenchmarkResult);

	Target PublishBenchmarkReport => _ => _
		.Description("Aggregates BenchmarkDotNet JSON results from the matrix Benchmarks-* artifacts of the " +
		             "current workflow run, then commits an updated data.js / limited-data.js to the " +
		             $"'{BenchmarkBranch}' branch.")
		.Requires(() => GithubToken)
		.Executes(async () =>
		{
			await "Benchmarks-".DownloadArtifactsStartingWith(ArtifactsDirectory, GithubToken);

			List<string> benchmarkReports = LoadBenchmarkJsonReports(ArtifactsDirectory / "Benchmarks" / "results");
			if (benchmarkReports.Count == 0)
			{
				Log.Warning("Skip benchmark report: no benchmark JSON reports found.");
				return;
			}

			PageBenchmarkReportGenerator.CommitInfo commitInfo = ReadCurrentCommitInfo();
			Log.Information(
				"Appending benchmark data for commit {Sha} ({Author}, {Date}): {Message}",
				commitInfo.Sha, commitInfo.Author, commitInfo.Date, commitInfo.Message);

			BuildExtensions.GithubFile dataFile =
				await BuildExtensions.ReadBranchFileAsync(BenchmarkDataPath, BenchmarkBranch, GithubToken);
			BuildExtensions.GithubFile limitedFile =
				await BuildExtensions.ReadBranchFileAsync(BenchmarkLimitedDataPath, BenchmarkBranch, GithubToken);

			if (dataFile == null || limitedFile == null)
			{
				throw new InvalidOperationException(
					$"The '{BenchmarkBranch}' branch must be seeded with '{BenchmarkDataPath}' and " +
					$"'{BenchmarkLimitedDataPath}' before benchmark data can be published, but " +
					$"'{(dataFile == null ? BenchmarkDataPath : BenchmarkLimitedDataPath)}' was not found.");
			}

			(string updated, string limited) = PageBenchmarkReportGenerator.Append(
				commitInfo,
				dataFile.Content,
				benchmarkReports,
				BenchmarkLimit);

			if (string.IsNullOrWhiteSpace(updated))
			{
				Log.Information("No changes to publish (commit already recorded).");
				return;
			}

			string commitMessage =
				$"Update benchmark for {commitInfo.Sha.Substring(0, 8)}: {commitInfo.Message} by {commitInfo.Author}";
			await BuildExtensions.WriteBranchFileAsync(BenchmarkDataPath, BenchmarkBranch, commitMessage, updated,
				dataFile.Sha, GithubToken);
			await BuildExtensions.WriteBranchFileAsync(BenchmarkLimitedDataPath, BenchmarkBranch, commitMessage,
				limited, limitedFile.Sha, GithubToken);
		});

	private static List<string> LoadBenchmarkJsonReports(AbsolutePath resultsDirectory)
	{
		List<string> reports = new();
		if (!Directory.Exists(resultsDirectory))
		{
			return reports;
		}

		foreach (string file in Directory.GetFiles(resultsDirectory, "*-report-full-compressed.json"))
		{
			reports.Add(File.ReadAllText(file));
		}

		return reports;
	}

	private static PageBenchmarkReportGenerator.CommitInfo ReadCurrentCommitInfo()
	{
		Output[] lines = GitTasks.Git("log -1").ToArray();
		string commitId = null, author = null, date = null, message = null;
		foreach (string line in lines.Select(x => x.Text))
		{
			if (commitId == null && line.StartsWith("commit "))
			{
				commitId = line.Substring("commit ".Length).Substring(0, 40);
				continue;
			}

			if (author == null && line.StartsWith("Author: "))
			{
				author = line.Substring("Author: ".Length);
				int index = author.IndexOf(" <", StringComparison.Ordinal);
				if (index > 0)
				{
					author = author.Substring(0, index);
				}

				continue;
			}

			if (date == null && line.StartsWith("Date:   "))
			{
				date = line.Substring("Date:   ".Length);
				continue;
			}

			if (commitId != null && author != null && date != null && !string.IsNullOrWhiteSpace(line))
			{
				message = line.Trim();
				break;
			}
		}

		return new PageBenchmarkReportGenerator.CommitInfo(commitId, author, date, message);
	}
}
