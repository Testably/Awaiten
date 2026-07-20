using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt199ScanHookConflict
	{
		[Fact]
		public async Task ReportsWhenTwoScansNameDifferentOnActivatedHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public interface IHandler { }
			                                       public sealed class Widget : IPlugin, IHandler { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), OnActivated = nameof(PluginStarted))]
			                                       [Scan(typeof(IHandler), OnActivated = nameof(HandlerStarted))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void PluginStarted(object instance) { }
			                                       	private static void HandlerStarted(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT199*'PluginStarted'*'HandlerStarted'*").AsWildcard()
				.Because("which OnActivated ran for Widget would depend on attribute order, so the contradiction is surfaced with the first scan's winner named");
		}

		[Fact]
		public async Task ReportsWhenTwoScansNameDifferentOnReleaseHooks()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public interface IHandler { }
			                                       public sealed class Widget : IPlugin, IHandler { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton, OnRelease = nameof(PluginStopped))]
			                                       [Scan(typeof(IHandler), Lifetime = AwaitenLifetime.Singleton, OnRelease = nameof(HandlerStopped))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void PluginStopped(object instance) { }
			                                       	private static void HandlerStopped(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT199*OnRelease*").AsWildcard()
				.Because("the OnRelease slot contradicts across the two scans exactly like OnActivated");
		}

		[Fact]
		public async Task DoesNotReportWhenTwoScansNameTheSameHook()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public interface IHandler { }
			                                       public sealed class Widget : IPlugin, IHandler { }

			                                       [Container]
			                                       [Scan(typeof(IPlugin), OnActivated = nameof(Started))]
			                                       [Scan(typeof(IHandler), OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT199*").AsWildcard()
				.Because("both scans agree on the method, so there is nothing order-dependent to surface");
		}

		[Fact]
		public async Task DoesNotReportWhenTwoScansHookDifferentSlots()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IStartable { }
			                                       public interface IStoppable { }
			                                       public sealed class Worker : IStartable, IStoppable { }

			                                       [Container]
			                                       [Scan(typeof(IStartable), Lifetime = AwaitenLifetime.Singleton, OnActivated = nameof(Started))]
			                                       [Scan(typeof(IStoppable), Lifetime = AwaitenLifetime.Singleton, OnRelease = nameof(Stopped))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(object instance) { }
			                                       	private static void Stopped(object instance) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("one scan's OnActivated and the other's OnRelease occupy different slots, so they merge instead of conflicting");
			await That(result.Sources.Values.Any(source => source.Contains("Started") && source.Contains("Stopped"))).IsTrue()
				.Because("the merged registration carries both scans' hooks, not just the first scan's");
		}

		[Fact]
		public async Task DoesNotReportWhenAnExplicitRegistrationOverridesTheScanHook()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Singleton<AlphaPlugin, IPlugin>(OnActivated = nameof(Special))]
			                                       [Scan(typeof(IPlugin), OnActivated = nameof(FromScan))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Special(IPlugin plugin) { }
			                                       	private static void FromScan(IPlugin plugin) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a scan yields to an explicit registration of the same type, hooks included, so overriding is not a conflict");
			await That(result.Sources.Values.Any(source => source.Contains("Special"))).IsTrue()
				.Because("the explicit registration's hook is the one that runs");
			await That(result.Sources.Values.Any(source => source.Contains("FromScan("))).IsFalse()
				.Because("the scan's hook is replaced, not merged in beside the explicit one");
		}

		[Fact]
		public async Task DoesNotReportWhenAnExplicitRegistrationWithoutHooksOptsOutOfTheScanHook()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class AlphaPlugin : IPlugin { }

			                                       [Container]
			                                       [Singleton<AlphaPlugin, IPlugin>]
			                                       [Scan(typeof(IPlugin), OnActivated = nameof(FromScan))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void FromScan(IPlugin plugin) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an explicit registration replaces the scan's hooks along with everything else");
			await That(result.Sources.Values.Any(source => source.Contains("FromScan("))).IsFalse()
				.Because("leaving the hook off the explicit registration deliberately opts the type out of it");
		}
	}
}
