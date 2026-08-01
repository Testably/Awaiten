using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT135 fires when a resolver seam (IAwaitenResolver and everything that extends it) is injected into a type
	// that is not a [Container] composition root, as a constructor parameter or property - the Service Locator
	// anti-pattern. Fields are not reported (the container never populates them). Reported by
	// AwaitenBoundaryAnalyzer, so these tests drive that analyzer.
	public class Awt135ServiceLocator
	{
		[Fact]
		public async Task ReportsForAResolverConstructorParameter()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class OrderProcessor
			                                       {
			                                       	public OrderProcessor(IAwaitenResolver resolver) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsTrue()
				.Because("a service that takes IAwaitenResolver locates its dependencies at run time");
		}

		[Fact]
		public async Task ReportsForAScopeConstructorParameter()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class OrderProcessor
			                                       {
			                                       	public OrderProcessor(IAwaitenScope scope) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsTrue()
				.Because("IAwaitenScope extends IAwaitenResolver, so injecting it is the same anti-pattern");
		}

		[Fact]
		public async Task ReportsForAResolverProperty()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class OrderProcessor
			                                       {
			                                       	public IAwaitenScope Scope { get; set; }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsTrue()
				.Because("a resolver-typed property is a run-time location seam the container can populate");
		}

		[Fact]
		public async Task ReportsForAResolverParameterOnAStruct()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public readonly struct Handle
			                                       {
			                                       	public Handle(IAwaitenScope scope) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsTrue()
				.Because("a struct that injects the resolver is service location too; structs are analyzed, not just classes");
		}

		[Fact]
		public async Task DoesNotReportForAResolverField()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class OrderProcessor
			                                       {
			                                       	private IAwaitenRoot _root;
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsFalse()
				.Because("Awaiten never populates a field, so a resolver-typed field is not an injection point; only constructor parameters and properties are reported");
		}

		[Fact]
		public async Task DoesNotReportForAPlainDependency()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock;
			                                       public sealed class Clock : IClock;

			                                       public sealed class Report
			                                       {
			                                       	public Report(IClock clock) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsFalse()
				.Because("a normal abstraction dependency is exactly what dependency injection is for");
		}

		[Fact]
		public async Task DoesNotReportForTheTypedResolverFastPath()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Grinder;

			                                       public sealed class Barista
			                                       {
			                                       	public Barista(IAwaitenResolver<Grinder> grinders) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsFalse()
				.Because("IAwaitenResolver<T> is a single-service seam (closer to Func<T>), not a service locator");
		}

		[Fact]
		public async Task DoesNotReportTheGeneratedRootOrScope()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Grinder;

			                                       [Container]
			                                       [Singleton<Grinder>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsFalse()
				.Because("the generated Root/Scope implement the resolver interfaces but are the composition root itself");
		}

		[Fact]
		public async Task DoesNotReportWhenSuppressedInSource()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;
			                                       using System.Diagnostics.CodeAnalysis;

			                                       namespace MyCode;

			                                       [SuppressMessage("Awaiten", "AWT135")]
			                                       public sealed class DeliberateAdapter
			                                       {
			                                       	public DeliberateAdapter(IAwaitenScope scope) { }
			                                       }
			                                       """);

			await That(diagnostics.Any(d => d.Contains("AWT135"))).IsFalse()
				.Because("the diagnostic is suppressible, so a deliberate seam like the MS.DI bridge adapter can opt out");
		}

		[Fact]
		public async Task PointsAnAdapterAtSuppressionRatherThanAtInjectingTheDependency()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenBoundaryAnalyzer>("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class OrderProcessor
			                                       {
			                                       	public OrderProcessor(IAwaitenResolver resolver) { }
			                                       }
			                                       """);

			await That(diagnostics.Single(d => d.Contains("AWT135"))).Contains("suppress this in source")
				.Because("the standing advice to inject the dependency instead is wrong for a host-integration adapter, where holding the resolver is the adaptation, and suppression is the sanctioned answer");
		}
	}
}
