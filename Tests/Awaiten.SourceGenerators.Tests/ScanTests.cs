using Awaiten;

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
	public async Task ScanAsSelfBitOrMarkerBit_RegistersBoth()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IReport { }
		                                       public sealed class SalesReport : IReport { }
		                                       public sealed class Consumer { public Consumer(SalesReport self, IEnumerable<IReport> all) { } }

		                                       [Container]
		                                       [Scan(typeof(IReport), As = ScanAs.Self | ScanAs.Marker, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::MyCode.SalesReport)")
			.Because("Self | Marker keeps the concrete self registration");
		await That(source).Contains("new global::MyCode.IReport[] { Root.ResolveSalesReport(__s.__root) }")
			.Because("Self | Marker also registers the match under the marker collection");
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

		// The support assembly's internal InternalPlugin is assignable to the marker but not nameable from here,
		// which AWT193 reports; nothing else about this scan is remarkable.
		await That(result.Diagnostics).Contains("*AWT193*InternalPlugin*").AsWildcard();
		await That(result.Diagnostics).HasCount(1);
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

		await That(result.Diagnostics).Contains("*AWT193*HiddenPlugin*").AsWildcard()
			.Because("a private nested match cannot be referenced from the generated code, so it is skipped, but not silently");
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

		// As above, the support assembly's internal InternalPlugin earns an AWT193 and nothing else does.
		await That(result.Diagnostics).Contains("*AWT193*InternalPlugin*").AsWildcard();
		await That(result.Diagnostics).HasCount(1);
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

	[Fact]
	public async Task ScanAsMatchingInterface_PrependsIEvenWhenTheNameAlreadyStartsWithI()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IIdentity { }
		                                       public sealed class Identity : IIdentity { }

		                                       [Container]
		                                       [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Identity" })]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("typeof(global::MyCode.IIdentity)")
			.Because("Identity's convention interface is I + Identity = IIdentity");
	}

	[Fact]
	public async Task ScanAsMatchingInterface_DoesNotMatchAGenericInterfaceOfTheConventionName()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IThing { }
		                                       public interface IWidget<T> { }
		                                       public sealed class Widget : IThing, IWidget<int> { }   // only generic IWidget<T>

		                                       [Container]
		                                       [Scan(typeof(IThing), As = ScanAs.MatchingInterface)]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT182*").AsWildcard()
			.And.Contains("*Widget*").AsWildcard()
			.Because("a generic IWidget<T> is not the non-generic IWidget the convention requires");
	}

	[Fact]
	public async Task ScanAsMatchingInterface_RegistersUnderASameNamedInterfaceInAnotherNamespace()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace Contracts { public interface IWorker { } }

		                                       namespace MyCode
		                                       {
		                                           public sealed class Worker : Contracts.IWorker { }

		                                           [Container]
		                                           [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Worker" })]
		                                           public static partial class MyContainer
		                                           {
		                                           }
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("typeof(global::Contracts.IWorker)")
			.Because("with no same-namespace IWorker, the convention falls back to the implemented IWorker");
	}

	[Fact]
	public async Task ScanAsMarkerOrMatchingInterface_DeduplicatesWhenTheMarkerIsTheConventionInterface()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IFoo { }
		                                       public sealed class Foo : IFoo { }
		                                       public sealed class Consumer { public Consumer(IEnumerable<IFoo> all) { } }

		                                       [Container]
		                                       [Scan(typeof(IFoo), As = ScanAs.Marker | ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Marker (IFoo) and MatchingInterface (also IFoo) select the same interface; the union is deduplicated so
		// Foo joins the IFoo collection once. A duplicate would materialize a two-element array instead.
		await That(source).Contains("new global::MyCode.IFoo[] { Root.ResolveFoo(__s.__root) }")
			.Because("the same interface selected by both exposures is registered only once");
	}

	[Fact]
	public async Task ScanAsMatchingInterface_SkipsAConventionInterfaceTheGeneratedCodeCannotReference()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			namespace Lib;

			internal interface IWidget { }
			public sealed class Widget : IWidget { }
			""", """
			using Awaiten;

			namespace MyCode;

			[Container]
			[Scan(As = ScanAs.Self | ScanAs.MatchingInterface, InAssembliesOf = new[] { typeof(Lib.Widget) })]
			public static partial class MyContainer
			{
			}
			""");

		// The internal IWidget cannot be referenced from the container's assembly; registering under it would
		// emit typeof(global::Lib.IWidget) and fail the consumer's build with CS0122.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __Bucket(typeof(global::Lib.Widget)")
			.Because("the accessible match still self-registers");
		await That(source).DoesNotContain("IWidget")
			.Because("an interface inaccessible to the generated code is dropped from the contracts");
	}

	[Fact]
	public async Task ScanAsMatchingInterface_PrefersTheOwnNamespaceInterfaceAcrossAssemblies()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			namespace MyCode { public interface IWorker { } }
			namespace Other { public interface IWorker { } }
			""", """
			using Awaiten;

			namespace MyCode
			{
			    public sealed class Worker : IWorker, Other.IWorker { }

			    [Container]
			    [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Worker" })]
			    public static partial class MyContainer
			    {
			    }
			}
			""");

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// MyCode.IWorker lives in a referenced assembly, but its namespace NAME matches Worker's, so the
		// own-namespace preference still applies across the assembly boundary.
		await That(source).Contains("typeof(global::MyCode.IWorker)")
			.Because("the convention interface in the match's own namespace wins the tiebreak");
		await That(source).DoesNotContain("Other.IWorker")
			.Because("the same-named interface in a foreign namespace loses to the own-namespace one");
	}

	[Fact]
	public async Task ScanAs_IsMirroredBitForBitByTheGeneratorsScanExposures()
	{
		// The generator reads ScanAs as its underlying int off the attribute's TypedConstant and casts it to its
		// internal ScanExposures mirror, so the bit values must stay aligned; nothing else links the two enums.
		Type exposures = typeof(AwaitenGenerator).Assembly.GetType("Awaiten.SourceGenerators.Entities.ScanExposures")!;

		foreach (ScanAs flag in Enum.GetValues<ScanAs>())
		{
			object mirrored = Enum.Parse(exposures, flag.ToString());
			await That(Convert.ToInt32(mirrored)).IsEqualTo((int)flag)
				.Because($"the generator casts the ScanAs int to ScanExposures, so {flag} must keep its bit value");
		}
	}

	[Fact]
	public async Task ScanAsMatchingInterface_RegistersUnderEverySameNamedInterfaceWhenNoneIsInItsNamespace()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace A { public interface IWorker { } }
		                                       namespace B { public interface IWorker { } }

		                                       namespace MyCode
		                                       {
		                                           public sealed class Worker : A.IWorker, B.IWorker { }

		                                           [Container]
		                                           [Scan(As = ScanAs.MatchingInterface, NamePatterns = new[] { "Worker" })]
		                                           public static partial class MyContainer
		                                           {
		                                           }
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a markerless match is never warned, so the unresolved tie registers silently (a marker scan reports AWT187)");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// With no own-namespace candidate to prefer, the convention is ambiguous; every same-named implemented
		// interface registers, deterministically ordered.
		await That(source).Contains("typeof(global::A.IWorker)")
			.And.Contains("typeof(global::B.IWorker)");
	}
}
