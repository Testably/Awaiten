namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Assembly scanning: <c>[Scan(typeof(TMarker))]</c> synthesizes a self-registration for every concrete
///     class assignable to the marker, skipping abstract/static classes and the marker itself. The synthesized
///     registrations reuse the ordinary dispatch/construction/lifetime plumbing, so no new emission is introduced.
///     An explicit registration of the same type takes precedence over the scan.
/// </summary>
public class ScanTests
{
	[Fact]
	public async Task Scan_SelfRegistersEveryConcreteImplementationOfTheMarker()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class AlphaPlugin : IPlugin { }
		                                       public sealed class BetaPlugin : IPlugin { }
		                                       public abstract class PluginBase : IPlugin { }

		                                       [Container]
		                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::MyCode.AlphaPlugin)")
			.Because("the concrete AlphaPlugin is self-registered by the scan");
		await That(source).Contains("new __Bucket(typeof(global::MyCode.BetaPlugin)")
			.Because("the concrete BetaPlugin is self-registered by the scan");
		await That(source).Contains("new global::MyCode.AlphaPlugin()")
			.And.Contains("new global::MyCode.BetaPlugin()");

		await That(source).DoesNotContain("PluginBase")
			.Because("an abstract class is skipped by the scan");
	}

	[Fact]
	public async Task Scan_IsOverriddenByAnExplicitRegistrationOfTheSameType()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class AlphaPlugin : IPlugin { }
		                                       public sealed class BetaPlugin : IPlugin { }

		                                       [Container]
		                                       [Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
		                                       [Transient<BetaPlugin>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A specific registration refines the scan's bulk defaults, so explicit transient BetaPlugin wins the
		// single-dispatch slot while scanned AlphaPlugin stays a singleton.
		await That(source).Contains("new __Bucket(typeof(global::MyCode.BetaPlugin)")
			.And.Contains("new __Bucket(typeof(global::MyCode.AlphaPlugin)");
		int betaConstructions = source.Split(new[]
		{
			"new global::MyCode.BetaPlugin()",
		}, System.StringSplitOptions.None).Length - 1;
		await That(betaConstructions).IsEqualTo(1)
			.Because("the scan match is skipped for a type already registered explicitly, so BetaPlugin is built once");
	}

	[Fact]
	public async Task ScanAsMarker_RegistersMatchesUnderTheMarkerAsACollection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IHandler { }
		                                       public sealed class EmailHandler : IHandler { }
		                                       public sealed class SmsHandler : IHandler { }
		                                       public sealed class Dispatcher { public Dispatcher(IEnumerable<IHandler> handlers) { } }

		                                       [Container]
		                                       [Scan(typeof(IHandler), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Singleton<Dispatcher>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Marker registers matches under the marker collection, not as self-dispatched concrete types.
		await That(source).Contains("new global::MyCode.Dispatcher(new global::MyCode.IHandler[] { Root.ResolveEmailHandler(__s.__root), Root.ResolveSmsHandler(__s.__root) })");
		await That(source).DoesNotContain("new __Bucket(typeof(global::MyCode.EmailHandler)")
			.Because("Marker registers under the marker interface, not the concrete type");
	}

	[Fact]
	public async Task ScanAsSelfAndMarker_RegistersBoth()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IReport { }
		                                       public sealed class SalesReport : IReport { }
		                                       public sealed class Consumer { public Consumer(SalesReport self, IEnumerable<IReport> all) { } }

		                                       [Container]
		                                       [Scan(typeof(IReport), As = ScanAs.SelfAndMarker, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::MyCode.SalesReport)")
			.Because("SelfAndMarker keeps the concrete self registration");
		await That(source).Contains("new global::MyCode.IReport[] { Root.ResolveSalesReport(__s.__root) }")
			.Because("SelfAndMarker also registers the match under the marker collection");
	}

	[Fact]
	public async Task ScanInAssembliesOf_RegistersConcreteTypesFromTheReferencedAssembly()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using Awaiten.Tests.Support;

		                                       namespace MyCode;

		                                       [Container]
		                                       [Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """, typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin));

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::Awaiten.Tests.Support.GammaPlugin)");
		await That(source).Contains("new __Bucket(typeof(global::Awaiten.Tests.Support.DeltaPlugin)");
		await That(source).DoesNotContain("PluginBase")
			.Because("the abstract base in the referenced assembly is skipped");
	}

	[Fact]
	public async Task ScanInAssembliesOf_RegistersMatchesInADeterministicOrder()
	{
		string Generate() => Generator.Run("""
		                                   using Awaiten;
		                                   using Awaiten.Tests.Support;

		                                   namespace MyCode;

		                                   [Container]
		                                   [Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, Lifetime = AwaitenLifetime.Singleton)]
		                                   public static partial class MyContainer
		                                   {
		                                   }
		                                   """, typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin))
			.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Sorted by fully-qualified name, so DeltaPlugin precedes GammaPlugin, and two builds match byte-for-byte.
		string first = Generate();
		await That(first).IsEqualTo(Generate());
		await That(first.IndexOf("ResolveDeltaPlugin", System.StringComparison.Ordinal))
			.IsLessThan(first.IndexOf("ResolveGammaPlugin", System.StringComparison.Ordinal));
	}

	[Fact]
	public async Task ScanClosedTypesOf_RegistersEachMatchUnderItsClosedMarkerInterface()
	{
		GeneratorResult result = Generator.Run("""
		                                       using System.Collections.Generic;
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IView<TViewModel> { }
		                                       public sealed class ViewModelOne { }
		                                       public sealed class ViewModelTwo { }
		                                       public sealed class ViewOne : IView<ViewModelOne> { }
		                                       public sealed class ViewTwo : IView<ViewModelTwo> { }
		                                       public sealed class DualView : IView<ViewModelOne>, IView<ViewModelTwo> { }

		                                       [Container]
		                                       [Scan(typeof(IView<>), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Each view registers under the closed marker interface(s) it implements, not its concrete type; a view
		// closing two type args registers under both.
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IView<global::MyCode.ViewModelOne>)");
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IView<global::MyCode.ViewModelTwo>)");
		await That(source).Contains("new global::MyCode.IView<global::MyCode.ViewModelOne>[] { Root.ResolveDualView(__s.__root), Root.ResolveViewOne(__s.__root) }");
		await That(source).Contains("new global::MyCode.IView<global::MyCode.ViewModelTwo>[] { Root.ResolveDualView(__s.__root), Root.ResolveViewTwo(__s.__root) }");
		await That(source).DoesNotContain("new __Bucket(typeof(global::MyCode.ViewOne)")
			.Because("Marker registers under the closed marker interface, not the concrete view");
	}

	[Fact]
	public async Task GenericScan_ProducesTheSameRegistrationsAsTheTypeofForm()
	{
		const string body = """
		                    using Awaiten;
		                    using System.Collections.Generic;

		                    namespace MyCode;

		                    public interface IHandler { }
		                    public sealed class EmailHandler : IHandler { }
		                    public sealed class SmsHandler : IHandler { }
		                    public sealed class Dispatcher { public Dispatcher(IEnumerable<IHandler> handlers) { } }

		                    [Container]
		                    {0}
		                    [Singleton<Dispatcher>]
		                    public static partial class MyContainer
		                    {
		                    }
		                    """;

		string typeofForm = Generator.Run(
				body.Replace("{0}", "[Scan(typeof(IHandler), As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]"))
			.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		string genericForm = Generator.Run(
				body.Replace("{0}", "[Scan<IHandler>(As = ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]"))
			.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(genericForm).IsEqualTo(typeofForm)
			.Because("[Scan<IHandler>] is the generic spelling of [Scan(typeof(IHandler))] and generates identically");
	}

	[Fact]
	public async Task Scan_SkipsOpenGenericImplementers()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IHandler { }
		                                       public sealed class LoggingHandler<T> : IHandler { }
		                                       public sealed class PlainHandler : IHandler { }

		                                       [Container]
		                                       [Scan(typeof(IHandler))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an open generic implementer has no closed form to construct and must not break the generated code");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.PlainHandler()");
		await That(source).DoesNotContain("LoggingHandler")
			.Because("a generic type definition is skipped by the scan");
	}

	[Fact]
	public async Task ScanClosedTypesOf_SkipsOpenGenericImplementers()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IView<T> { }
		                                       public sealed class GenericView<T> : IView<T> { }
		                                       public sealed class ViewModel { }
		                                       public sealed class ClosedView : IView<ViewModel> { }

		                                       [Container]
		                                       [Scan(typeof(IView<>), As = ScanAs.Marker)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a generic view closing the marker over its own type parameter is not a closed form");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new __Bucket(typeof(global::MyCode.IView<global::MyCode.ViewModel>)");
		await That(source).DoesNotContain("GenericView")
			.Because("a generic type definition is skipped by the scan");
	}

	[Fact]
	public async Task Scan_SkipsTypesTheContainerCannotAccess()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class VisiblePlugin : IPlugin { }

		                                       public sealed class Host
		                                       {
		                                           private sealed class HiddenPlugin : IPlugin { }
		                                       }

		                                       [Container]
		                                       [Scan(typeof(IPlugin))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a private nested match cannot be referenced from the generated code and must be skipped");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.VisiblePlugin()");
		await That(source).DoesNotContain("HiddenPlugin");
	}

	[Fact]
	public async Task Scan_SeedsOpenGenericExpansionForScannedDependencies()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T> { }
		                                       public sealed class Order { }

		                                       public interface IPlugin { }
		                                       public sealed class OrderPlugin : IPlugin
		                                       {
		                                           public OrderPlugin(IRepository<Order> repository) { }
		                                       }

		                                       [Container]
		                                       [Scan(typeof(IPlugin))]
		                                       [Transient(typeof(Repository<>), typeof(IRepository<>))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a scanned implementation's closed generic dependency is expanded from the open registration");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
		await That(source).Contains("new global::MyCode.Repository<global::MyCode.Order>()")
			.Because("the scanned OrderPlugin's IRepository<Order> dependency seeds open generic expansion");
	}

	[Fact]
	public async Task GenericScan_HonorsInAssembliesOfAndSelfExposure()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using Awaiten.Tests.Support;

		                                       namespace MyCode;

		                                       [Container]
		                                       [Scan<ICrossAssemblyPlugin>(InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, Lifetime = AwaitenLifetime.Singleton)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """, typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin));

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The generic form threads through InAssembliesOf exactly like the typeof form.
		await That(source).Contains("new __Bucket(typeof(global::Awaiten.Tests.Support.GammaPlugin)");
		await That(source).Contains("new __Bucket(typeof(global::Awaiten.Tests.Support.DeltaPlugin)");
	}

	[Fact]
	public async Task NamePatterns_RegisterOnlyMatchingNamesAndOrMultiplePatterns()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IPlugin { }
			public sealed class OrderHandler : IPlugin { }
			public sealed class EmailValidator : IPlugin { }
			public sealed class PlainService : IPlugin { }

			[Container]
			[Scan(typeof(IPlugin), NamePatterns = new[] { "*Handler", "*Validator" })]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.OrderHandler()")
			.And.Contains("new global::MyCode.EmailValidator()")
			.Because("a candidate matching any include pattern is registered");
		await That(source).DoesNotContain("PlainService")
			.Because("a candidate matching no include pattern is filtered out");
	}

	[Fact]
	public async Task NamePatterns_ExcludeDropsNegatedMatches()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IPlugin { }
			public sealed class LegacyHandler : IPlugin { }
			public sealed class ModernHandler : IPlugin { }

			[Container]
			[Scan(typeof(IPlugin), NamePatterns = new[] { "!Legacy*" })]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.ModernHandler()")
			.Because("with no include patterns every non-excluded candidate passes");
		await That(source).DoesNotContain("LegacyHandler")
			.Because("a !-prefixed pattern excludes the matching candidate");
	}

	[Fact]
	public async Task NamePatterns_AreCaseSensitive()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IPlugin { }
			public sealed class OrderHandler : IPlugin { }

			[Container]
			[Scan(typeof(IPlugin), NamePatterns = new[] { "*handler" })]
			public static partial class MyContainer
			{
			}
			""");

		// The glob is ordinal, so lower-case "handler" does not match "OrderHandler"; nothing is left to register.
		await That(result.Diagnostics).Contains("*AWT172*").AsWildcard();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("OrderHandler");
	}

	[Fact]
	public async Task NamespacePatterns_MatchTheSubtreeButNotASibling()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace App.Services { public interface IPlugin { } public sealed class CoreService : IPlugin { } }
			namespace App.Services.Billing { public sealed class InvoiceService : App.Services.IPlugin { } }
			namespace App.ServicesLegacy { public sealed class OldService : App.Services.IPlugin { } }

			namespace MyCode
			{
			    [Container]
			    [Scan(typeof(App.Services.IPlugin), NamespacePatterns = new[] { "App.Services.**" })]
			    public static partial class MyContainer { }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::App.Services.CoreService()")
			.Because("'**' includes the anchor namespace itself");
		await That(source).Contains("new global::App.Services.Billing.InvoiceService()")
			.Because("'**' includes nested namespaces");
		await That(source).DoesNotContain("OldService")
			.Because("segment-aware matching does not leak into the sibling App.ServicesLegacy");
	}

	[Fact]
	public async Task NamespacePatterns_SingleStarMatchesImmediateChildrenOnly()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace App.Data { public interface IPlugin { } }
			namespace App.Data.Sql { public sealed class SqlStore : App.Data.IPlugin { } }
			namespace App.Data.Sql.Internal { public sealed class InternalStore : App.Data.IPlugin { } }

			namespace MyCode
			{
			    [Container]
			    [Scan(typeof(App.Data.IPlugin), NamespacePatterns = new[] { "App.Data.*" })]
			    public static partial class MyContainer { }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::App.Data.Sql.SqlStore()")
			.Because("'*' matches exactly one segment");
		await That(source).DoesNotContain("InternalStore")
			.Because("'*' does not match a two-segment-deeper namespace");
	}

	[Fact]
	public async Task NamespacePatterns_ExcludeByTrailingSegment()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace App.Core { public interface IPlugin { } public sealed class RealService : IPlugin { } }
			namespace App.Core.Tests { public sealed class FakeService : App.Core.IPlugin { } }

			namespace MyCode
			{
			    [Container]
			    [Scan(typeof(App.Core.IPlugin), NamespacePatterns = new[] { "!**.Tests" })]
			    public static partial class MyContainer { }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::App.Core.RealService()");
		await That(source).DoesNotContain("FakeService")
			.Because("'!**.Tests' excludes any namespace whose last segment is Tests");
	}

	[Fact]
	public async Task Exclude_RemovesTheExactTypeOnly()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			public interface IPlugin { }
			public sealed class KeepPlugin : IPlugin { }
			public sealed class DropPlugin : IPlugin { }

			[Container]
			[Scan(typeof(IPlugin), Exclude = new[] { typeof(DropPlugin) })]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.KeepPlugin()");
		await That(source).DoesNotContain("DropPlugin")
			.Because("an Exclude entry removes that exact type");
	}

	[Fact]
	public async Task Filters_ComposeAcrossAxesWithAnd()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace App.Handlers { public interface IPlugin { } public sealed class OrderHandler : IPlugin { } public sealed class OrderService : IPlugin { } }
			namespace App.Other { public sealed class AuditHandler : App.Handlers.IPlugin { } }

			namespace MyCode
			{
			    [Container]
			    [Scan(typeof(App.Handlers.IPlugin), NamePatterns = new[] { "*Handler" }, NamespacePatterns = new[] { "App.Handlers" })]
			    public static partial class MyContainer { }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::App.Handlers.OrderHandler()")
			.Because("it matches both the name and the namespace filter");
		await That(source).DoesNotContain("OrderService")
			.Because("it fails the name filter");
		await That(source).DoesNotContain("AuditHandler")
			.Because("it fails the namespace filter");
	}

	[Fact]
	public async Task Filters_ApplyToReferencedAssemblyCandidates()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;
			using Awaiten.Tests.Support;

			namespace MyCode;

			[Container]
			[Scan(typeof(ICrossAssemblyPlugin), InAssembliesOf = new[] { typeof(ICrossAssemblyPlugin) }, NamePatterns = new[] { "Gamma*" }, Lifetime = AwaitenLifetime.Singleton)]
			public static partial class MyContainer
			{
			}
			""", typeof(global::Awaiten.Tests.Support.ICrossAssemblyPlugin));

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::Awaiten.Tests.Support.GammaPlugin)");
		await That(source).DoesNotContain("DeltaPlugin")
			.Because("the name filter narrows referenced-assembly candidates too");
	}
}
