using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Xml.XPath;
using PublicApiGenerator;

namespace Awaiten.Api.Tests;

public static class Helper
{
	private static readonly ConcurrentDictionary<string, string> PublicApiCache = new();

	private static readonly string ProbeRoot =
		Path.Combine(Path.GetTempPath(), "Awaiten.Api.Tests", Guid.NewGuid().ToString("N"));

	private static readonly Lazy<bool> StaleProbeRootSweep = new(SweepStaleProbeRoots);

	/// <summary>
	///     Generates the public API of the built <paramref name="assemblyName" /> for the given
	///     <paramref name="framework" />, memoized per pair (<see cref="Assembly.LoadFile(string)" /> caches and
	///     locks each staged file for the process lifetime, so a pair is never staged or loaded twice).
	/// </summary>
	public static string CreatePublicApi(string framework, string assemblyName)
		=> PublicApiCache.GetOrAdd($"{assemblyName}|{framework}", _ => GeneratePublicApi(framework, assemblyName));

	/// <summary>
	///     Stages the built <paramref name="assemblyName" /> assembly together with the Awaiten and
	///     Microsoft.Extensions.DependencyInjection dependency assemblies this test project carries in its output,
	///     then generates the public API from there. Mono.Cecil resolves an assembly's dependencies from its own
	///     directory, so the product project need not copy its package dependencies into its output for this tool.
	/// </summary>
	private static string GeneratePublicApi(string framework, string assemblyName)
	{
		_ = StaleProbeRootSweep.Value;

#if DEBUG
		string configuration = "Debug";
#else
		string configuration = "Release";
#endif
		string assemblyFile =
			CombinedPaths("Source", assemblyName, "bin", configuration, framework, $"{assemblyName}.dll");

		string probeDirectory = Path.Combine(ProbeRoot, $"{assemblyName}_{framework}");
		Directory.CreateDirectory(probeDirectory);
		foreach (string dependency in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
		{
			string fileName = Path.GetFileName(dependency);
			if (!fileName.StartsWith("Awaiten", StringComparison.OrdinalIgnoreCase)
			    && !fileName.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			File.Copy(dependency, Path.Combine(probeDirectory, fileName), overwrite: true);
		}

		string probeAssembly = Path.Combine(probeDirectory, $"{assemblyName}.dll");
		File.Copy(assemblyFile, probeAssembly, overwrite: true);

		Assembly assembly = Assembly.LoadFile(probeAssembly);
		string publicApi = assembly.GeneratePublicApi();
		return publicApi.Replace("\r\n", "\n");
	}

	/// <summary>
	///     Deletes the <see cref="ProbeRoot" /> directories that previous runs leaked. A run cannot delete its own
	///     directory because <see cref="Assembly.LoadFile(string)" /> locks the staged files until the process
	///     exits; a directory still locked by a lingering host is skipped and reclaimed by a later run.
	/// </summary>
	private static bool SweepStaleProbeRoots()
	{
		string probeParent = Path.GetDirectoryName(ProbeRoot)!;
		if (!Directory.Exists(probeParent))
		{
			return true;
		}

		foreach (string staleRoot in Directory.EnumerateDirectories(probeParent))
		{
			if (string.Equals(staleRoot, ProbeRoot, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			try
			{
				Directory.Delete(staleRoot, recursive: true);
			}
			catch
			{
				// Still locked by a lingering test host - leave it for a later run to reclaim.
			}
		}

		return true;
	}

	public static string GetExpectedApi(string framework, string assemblyName)
	{
		string expectedPath = CombinedPaths("Tests", "Awaiten.Api.Tests",
			"Expected", $"{assemblyName}_{framework}.txt");
		try
		{
			return File.ReadAllText(expectedPath)
				.Replace("\r\n", "\n");
		}
		catch
		{
			return string.Empty;
		}
	}

	public static IEnumerable<string> GetAssemblyNames()
	{
		yield return "Awaiten";
		yield return "Awaiten.Extensions.DependencyInjection";
	}

	public static IEnumerable<string> GetTargetFrameworks()
	{
		string csproj = CombinedPaths("Source", "Directory.Build.props");
		XDocument project = XDocument.Load(csproj);
		XElement? targetFrameworks =
			project.XPathSelectElement("/Project/PropertyGroup/TargetFrameworks");
		foreach (string targetFramework in targetFrameworks!.Value.Split(';'))
		{
			yield return targetFramework;
		}
	}

	public static void SetExpectedApi(string framework, string assemblyName, string publicApi)
	{
		string expectedPath = CombinedPaths("Tests", "Awaiten.Api.Tests",
			"Expected", $"{assemblyName}_{framework}.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
		File.WriteAllText(expectedPath, publicApi);
	}

	private static string CombinedPaths(params string[] paths) =>
		Path.GetFullPath(Path.Combine(paths.Prepend(GetSolutionDirectory()).ToArray()));

	private static string GetSolutionDirectory([CallerFilePath] string path = "") =>
		Path.Combine(Path.GetDirectoryName(path)!, "..", "..");
}
