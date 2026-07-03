using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt130Composite
	{
		[Fact]
		public async Task ReportsAwt130WhenTheCompositeTakesNoCollectionParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public sealed class Email : INotifier { }
			                                       // A bare INotifier parameter is not a collection: there is nothing to fan out to.
			                                       public sealed class BadComposite : INotifier { public BadComposite(INotifier single) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<BadComposite, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT130"))).IsTrue()
				.Because("a composite with no collection parameter of the composed service has nothing to fan out to");
		}

		[Fact]
		public async Task ReportsAwt130WhenTheCompositeCollectionIsOfADifferentService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public sealed class Email : INotifier { }
			                                       // A collection parameter, but of string — not of the composed INotifier.
			                                       public sealed class BadComposite : INotifier { public BadComposite(IEnumerable<string> names) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<BadComposite, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT130"))).IsTrue()
				.Because("the collection parameter must be of the composed service for the composite to fan out over it");
		}

		[Fact]
		public async Task DoesNotReportForAWellFormedComposite()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public sealed class Email : INotifier { }
			                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(IEnumerable<INotifier> channels) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a composite with a collection parameter of the composed service is well-formed");
		}

		[Fact]
		public async Task DoesNotReportAwt130ForAnArrayParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public sealed class Email : INotifier { }
			                                       // An array is a recognized collection shape too.
			                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(INotifier[] channels) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a rank-1 array of the composed service is a valid composite collection parameter");
		}
	}
}
