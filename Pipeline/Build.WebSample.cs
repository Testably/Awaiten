using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

// ReSharper disable AllUnderscoreLocalParameterName

namespace Build;

partial class Build
{
	Target WebSample => _ => _
		.Description("Builds Samples/Awaiten.WebSample and runs its smoke test, which starts the host on an " +
		             "ephemeral port and drives every endpoint over HTTP, proving the MS.DI bridge serves real " +
		             "requests rather than only compiling.")
		.Executes(() =>
		{
			AbsolutePath sampleProject = RootDirectory / "Samples" / "Awaiten.WebSample" / "Awaiten.WebSample.csproj";

			DotNetBuild(s => s
				.SetProjectFile(sampleProject)
				.SetConfiguration(Configuration.Release)
				.EnableNoLogo());

			AbsolutePath output = sampleProject.Parent / "bin" / "Release" / "net10.0";

			// Run the assembly through the SDK rather than the native apphost, whose file name differs per
			// platform. The working directory has to be the output directory: the host takes its content root
			// from there, and the sample reads its menu, and so the prices the smoke test asserts, out of
			// appsettings.json. A non-zero exit code fails the target.
			DotNet($"exec \"{output / "Awaiten.WebSample.dll"}\" --smoke", output);
		});
}
