namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of assembly scanning: <c>[Scan(typeof(TMarker))]</c> synthesizes an overridable
///     self-registration for every concrete class in the container's assembly assignable to the marker, skipping
///     abstract/static classes and the marker itself. The synthesized registrations are ordinary self
///     registrations, so the existing dispatch, construction and lifetime plumbing emits them — no new emission
///     is introduced. An explicit registration of the same type takes precedence over the scan.
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

		// Both concrete plugins are registered and dispatched as themselves.
		await That(source).Contains("new __Bucket(typeof(global::MyCode.AlphaPlugin)")
			.Because("the concrete AlphaPlugin is self-registered by the scan");
		await That(source).Contains("new __Bucket(typeof(global::MyCode.BetaPlugin)")
			.Because("the concrete BetaPlugin is self-registered by the scan");
		await That(source).Contains("new global::MyCode.AlphaPlugin()")
			.And.Contains("new global::MyCode.BetaPlugin()");

		// The abstract base and the marker interface itself are not instantiable and must not be registered.
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

		// The explicit transient BetaPlugin wins the single-dispatch slot, so it is not cached as a singleton
		// while the scanned AlphaPlugin is - a scan provides bulk defaults that a specific registration refines.
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
	public async Task ScanAsImplementedInterfaces_RegistersMatchesUnderTheMarkerAsACollection()
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
		                                       [Scan(typeof(IHandler), As = ScanAs.ImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Singleton<Dispatcher>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Both matches register under IHandler and become members of its collection, resolved by their concrete
		// resolvers; the concrete types themselves are not self-registered for single dispatch.
		await That(source).Contains("new global::MyCode.Dispatcher(new global::MyCode.IHandler[] { ResolveEmailHandler(), ResolveSmsHandler() })");
		await That(source).DoesNotContain("new __Bucket(typeof(global::MyCode.EmailHandler)")
			.Because("ImplementedInterfaces registers under the marker interface, not the concrete type");
	}

	[Fact]
	public async Task ScanAsSelfAndImplementedInterfaces_RegistersBoth()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IReport { }
		                                       public sealed class SalesReport : IReport { }
		                                       public sealed class Consumer { public Consumer(SalesReport self, IEnumerable<IReport> all) { } }

		                                       [Container]
		                                       [Scan(typeof(IReport), As = ScanAs.SelfAndImplementedInterfaces, Lifetime = AwaitenLifetime.Singleton)]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Resolvable both as its own concrete type and as a member of the marker's collection.
		await That(source).Contains("new __Bucket(typeof(global::MyCode.SalesReport)")
			.Because("SelfAndImplementedInterfaces keeps the concrete self registration");
		await That(source).Contains("new global::MyCode.IReport[] { ResolveSalesReport() }")
			.Because("SelfAndImplementedInterfaces also registers the match under the marker collection");
	}
}
