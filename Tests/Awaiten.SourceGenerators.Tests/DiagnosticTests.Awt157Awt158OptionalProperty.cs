using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt157Awt158OptionalProperty
	{
		[Fact]
		public async Task OptionalPropertyWithNoRegistration_DoesNotReportAwt101()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // Optional: Bus is not registered, so the property is left unset rather than being a missing dependency.
			                                           [Inject(Optional = true)] public Bus Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("an optional property with no registration is left unset, not reported as a missing dependency");
			await That(result.Diagnostics).IsEmpty()
				.Because("an optional set-accessor property with no registration is well-defined and reports nothing");
		}

		[Fact]
		public async Task OptionalPropertyWithARegistration_IsSatisfiedAndReportsNothing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           [Inject(Optional = true)] public Bus Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a registered optional property is filled exactly like a required one, so nothing is reported");
		}

		[Fact]
		public async Task ReportsAwt157WhenOptionalPropertyIsRequired()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // A required member cannot be omitted from the object initializer, so it cannot be optional.
			                                           [Inject(Optional = true)] public required Bus Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT157*").AsWildcard()
				.Because("an optional property is omitted from the object initializer when unregistered, which a required member does not allow - the targeted AWT157 is reported rather than leaving only the opaque CS9035 the omitted required member would otherwise produce");
		}

		[Fact]
		public async Task ReportsAwt158WhenOptionalPropertyIsInitOnly_AndUnregistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // An init-only optional property can never be assigned after construction, so if Bus is never
			                                           // registered it stays permanently at its default - a suppressible warning, not an error.
			                                           [Inject(Optional = true)] public Bus Bus { get; init; }
			                                       }

			                                       [Container]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT158*").AsWildcard()
				.Because("an optional init-only property left unset when unregistered can never be filled afterwards, so it is warned about");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the property is still optional, so a missing registration is not the AWT101 error");
		}

		[Fact]
		public async Task ReportsAwt158WhenOptionalPropertyIsInitOnly_EvenWhenRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           [Inject(Optional = true)] public Bus Bus { get; init; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT158*").AsWildcard()
				.Because("AWT158 flags the init-only optional declaration itself: if the registration is ever removed the property would silently and permanently be default");
		}

		[Fact]
		public async Task OptionalCollectionPropertyWithNoMembers_ReportsNothing()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Host
			                                       {
			                                           // Optional has no effect on a collection: an unregistered collection already yields an empty one.
			                                           [Inject(Optional = true)] public IEnumerable<IPlugin> Plugins { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a collection member is never a missing dependency, so marking it optional is a harmless no-op");
		}

		[Fact]
		public async Task OptionalInitOnlyCollectionProperty_DoesNotReportAwt158()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Host
			                                       {
			                                           // Init-only would earn AWT158 on a scalar, but a collection is always filled through the
			                                           // initializer (empty when unregistered), so it is never left at its default - no warning.
			                                           [Inject(Optional = true)] public IEnumerable<IPlugin> Plugins { get; init; }
			                                       }

			                                       [Container]
			                                       [Transient<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT158*").AsWildcard()
				.Because("Optional has no effect on a collection, which is always filled, so the init-only-stays-default warning does not apply");
			await That(result.Diagnostics).IsEmpty()
				.Because("an optional init-only collection is well-defined and reports nothing");
		}

		[Fact]
		public async Task OptionalRequiredCollectionProperty_DoesNotReportAwt157()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IPlugin { }
			                                       public sealed class Host
			                                       {
			                                           // required would earn AWT157 on a scalar, but a collection is never omitted from the
			                                           // initializer, so there is no CS9035 risk and required is fine.
			                                           [Inject(Optional = true)] public required IEnumerable<IPlugin> Plugins { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT157*").AsWildcard()
				.Because("a collection is always filled and never omitted, so a required optional collection is not the AWT157 fault");
			await That(result.Diagnostics).IsEmpty()
				.Because("an optional required collection is well-defined and reports nothing");
		}

		[Fact]
		public async Task OptionalOwnedThroughLazyProperty_StillReportsAwt121()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Widget : IDisposable { public void Dispose() { } }
			                                       public sealed class Workshop
			                                       {
			                                           // Owned<T> can never be produced through Lazy - a structural fault, not a missing
			                                           // registration - so Optional must not swallow it: AWT121 is reported regardless.
			                                           [Inject(Optional = true)] public Lazy<Owned<Widget>> Widgets { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Widget>]
			                                       [Transient<Workshop>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT121*").AsWildcard()
				.Because("an Owned<T> disposal handle cannot be produced through Lazy, and Optional does not suppress a structurally impossible request");
		}

		[Fact]
		public async Task DeferredOptionalProperty_WithNoRegistration_IsDroppedWithoutDiagnostic()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // Deferred + Optional combine cleanly: a well-formed deferred property (plain set, not required)
			                                           // whose dependency is unregistered is dropped by Optional rather than assigned after construction,
			                                           // so neither the deferred shape rule (AWT144) nor a missing dependency (AWT101) applies.
			                                           [Inject(Deferred = true, Optional = true)] public Bus Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an unregistered deferred optional property is dropped and left at its default, exactly like a non-deferred optional one, so nothing is reported");
		}
	}
}
