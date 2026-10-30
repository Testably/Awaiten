using System.Collections.Generic;
using System.Linq;
using Fallout.Common;
using Fallout.Solutions;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	/// <summary>
	///     Selects the tests of the category <c>Slow</c>, which are explicit and therefore left out of a run without a
	///     filter.
	/// </summary>
	const string SlowTestsFilter = "/*/*/*/*[Category=Slow]";

	Target UnitTests => _ => _
		.DependsOn(DotNetUnitTests);

	Project[] UnitTestProjects =>
	[
		Solution.Tests.Awaiten_Tests,
		Solution.Tests.Awaiten_Extensions_DependencyInjection_Tests,
		Solution.Tests.Awaiten_SourceGenerators_Tests,
		Solution.Tests.Awaiten_ExampleTests,
	];

	/// <summary>
	///     The projects with slow tests. A project that gets its first slow test must be added here, otherwise the
	///     pipeline never runs it, and a project without slow tests must not be listed, because the run of its slow
	///     tests fails when it finds none.
	/// </summary>
	Project[] ProjectsWithSlowTests =>
	[
		Solution.Tests.Awaiten_Docs_Tests,
	];

	Target DotNetUnitTests => _ => _
		.Unlisted()
		.DependsOn(Compile)
		.Executes(() =>
		{
			RunUnitTests(UnitTestProjects);
		});

	/// <summary>
	///     Runs the tests of the <paramref name="projects" /> for each of their target frameworks (net48 only on
	///     Windows), and then their slow tests.
	/// </summary>
	/// <remarks>
	///     <c>global.json</c> runs <c>dotnet test</c> on the Microsoft.Testing.Platform, which takes the project
	///     through <c>--project</c> and passes the TRX and coverage options on to the TUnit test application. The slow
	///     tests of a project run in a separate leg after its regular tests.
	/// </remarks>
	void RunUnitTests(Project[] projects)
	{
		string[] excludedFrameworks =
			EnvironmentInfo.IsWin
				? []
				: ["net48",];
		(Project Project, string Framework)[] testRuns = projects
			.SelectMany(project => (project.GetTargetFrameworks() ?? [])
				.Except(excludedFrameworks)
				.Select(framework => (project, framework)))
			.ToArray();
		DotNetTest(s => s
				.SetConfiguration(Configuration)
				.SetProcessEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en-US")
				.EnableNoBuild()
				.SetResultsDirectory(TestResultsDirectory)
				.CombineWith(
					testRuns
						.Select(testRun => (testRun.Project, testRun.Framework, IsSlow: false))
						.Concat(testRuns
							.Where(testRun => ProjectsWithSlowTests.Contains(testRun.Project))
							.Select(testRun => (testRun.Project, testRun.Framework, IsSlow: true))),
					(settings, testRun) => settings
						.SetFramework(testRun.Framework)
						.SetProcessAdditionalArguments(TestArguments(testRun.Project, testRun.Framework,
							testRun.IsSlow))
				), completeOnFailure: true
		);
	}

	static string[] TestArguments(Project project, string framework, bool isSlow)
	{
		string name = isSlow
			? $"{project.Name}_{framework}_Slow"
			: $"{project.Name}_{framework}";
		List<string> arguments = [$"--project \"{project.Path}\"",];
		if (isSlow)
		{
			// A project that is listed without having slow tests fails here instead of passing with nothing run.
			arguments.AddRange([$"--treenode-filter \"{SlowTestsFilter}\"", "--minimum-expected-tests 1",]);
		}

		arguments.AddRange(
		[
			"--report-trx",
			$"--report-trx-filename {name}.trx",
			"--coverage",
			"--coverage-output-format cobertura",
			$"--coverage-output {name}.cobertura.xml",
		]);
		return arguments.ToArray();
	}
}
