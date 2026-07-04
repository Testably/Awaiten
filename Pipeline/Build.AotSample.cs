using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	Target AotSample => _ => _
		.Description("Publishes Samples/Awaiten.AotSample with native AOT and runs it, proving the generated " +
		             "container and the MS.DI bridge are reflection-free and AOT-compatible. Requires a platform " +
		             "linker (on Windows the 'Desktop development with C++' workload).")
		.Executes(() =>
		{
			AbsolutePath sampleProject = RootDirectory / "Samples" / "Awaiten.AotSample" / "Awaiten.AotSample.csproj";
			string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
			string runtime = $"{(EnvironmentInfo.IsWin ? "win" : EnvironmentInfo.IsOsx ? "osx" : "linux")}-{architecture}";

			DotNetPublish(s => s
				.SetProject(sampleProject)
				.SetConfiguration(Configuration.Release)
				.SetRuntime(runtime)
				.EnableNoLogo());

			AbsolutePath executable = sampleProject.Parent / "bin" / "Release" / "net10.0" / runtime / "publish" /
			                          ("Awaiten.AotSample" + (EnvironmentInfo.IsWin ? ".exe" : ""));
			ProcessTasks.StartProcess(executable).AssertZeroExitCode();
		});
}
