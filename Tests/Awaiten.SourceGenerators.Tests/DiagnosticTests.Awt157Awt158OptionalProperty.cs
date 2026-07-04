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

			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsFalse()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT157"))).IsTrue()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT158"))).IsTrue()
				.Because("an optional init-only property left unset when unregistered can never be filled afterwards, so it is warned about");
			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsFalse()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT158"))).IsTrue()
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
	}
}
