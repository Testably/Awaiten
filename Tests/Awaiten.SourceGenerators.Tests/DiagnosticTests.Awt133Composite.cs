using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt133Composite
	{
		[Fact]
		public async Task ReportsAwt133WhenTheCollectionIsOfABaseTypeOfTheComposedService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IBase { }
			                                       public interface INotifier : IBase { }
			                                       public sealed class Email : INotifier { }
			                                       // IEnumerable<IBase> is convertible-from INotifier, but collections resolve by exact element
			                                       // type, so this would fan out over IBase's registrations, not INotifier's.
			                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(IEnumerable<IBase> channels) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT133"))).IsTrue()
				.Because("a collection of a base type would fan out over a different collection than the composed service");
			await That(result.Diagnostics.Any(d => d.Contains("AWT130"))).IsFalse()
				.Because("the composite has a collection parameter, it is of the wrong element type, not missing");
		}

		[Fact]
		public async Task DoesNotReportAwt133WhenAnExactCollectionAlsoAccompaniesABaseTypedOne()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IBase { }
			                                       public interface INotifier : IBase { }
			                                       public sealed class Email : INotifier { }
			                                       // One parameter is exactly IEnumerable<INotifier>, so the composite fans out over the service
			                                       // correctly; the extra base-typed collection is the composite's own concern.
			                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(IEnumerable<INotifier> channels, IReadOnlyList<IBase> all) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT133"))).IsFalse()
				.Because("an exact collection of the composed service satisfies the composite regardless of other collection parameters");
		}
	}
}
