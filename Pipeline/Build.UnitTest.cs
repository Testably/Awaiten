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
	Target UnitTests => _ => _
		.DependsOn(DotNetUnitTests);

	Project[] UnitTestProjects =>
	[
		Solution.Tests.Awaiten_Tests,
		Solution.Tests.Awaiten_Extensions_DependencyInjection_Tests,
		Solution.Tests.Awaiten_SourceGenerators_Tests,
		Solution.Tests.Awaiten_ExampleTests,
	];

	/// <remarks>
	///     <c>global.json</c> runs <c>dotnet test</c> on the Microsoft.Testing.Platform, which takes the project
	///     through <c>--project</c> and passes the TRX and coverage options on to the TUnit test application.
	/// </remarks>
	Target DotNetUnitTests => _ => _
		.Unlisted()
		.DependsOn(Compile)
		.Executes(() =>
		{
			string[] excludedFrameworks =
				EnvironmentInfo.IsWin
					? []
					: ["net48",];
			DotNetTest(s => s
					.SetConfiguration(Configuration)
					.SetProcessEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en-US")
					.EnableNoBuild()
					.SetResultsDirectory(TestResultsDirectory)
					.CombineWith(
						UnitTestProjects,
						(settings, project) => settings
							.CombineWith(
								project.GetTargetFrameworks()?.Except(excludedFrameworks),
								(frameworkSettings, framework) => frameworkSettings
									.SetFramework(framework)
									.SetProcessAdditionalArguments(
										$"--project \"{project.Path}\"",
										"--report-trx",
										$"--report-trx-filename {project.Name}_{framework}.trx",
										"--coverage",
										"--coverage-output-format cobertura",
										$"--coverage-output {project.Name}_{framework}.cobertura.xml")
							)
					), completeOnFailure: true
			);
		});
}
