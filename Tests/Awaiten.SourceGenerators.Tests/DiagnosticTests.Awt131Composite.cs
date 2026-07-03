using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt131Composite
	{
		[Fact]
		public async Task ReportsAwt131WhenTheCompositeIsAlsoRegisteredAsABareMember()
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
			                                       [Transient<CompositeNotifier, INotifier>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT131"))).IsTrue()
				.Because("the composite is also registered as a bare member of its own service, which has no effect");
		}

		[Fact]
		public async Task DoesNotSurfaceTheDroppedMembershipAsAnAwt102Cycle()
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
			                                       [Transient<CompositeNotifier, INotifier>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT102"))).IsFalse()
				.Because("dropping the redundant membership keeps the composite out of its own collection, so there is no self-edge cycle");
		}

		[Fact]
		public async Task DoesNotReportAwt131WhenTheCompositeIsRegisteredAsAMemberOfADifferentService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public interface IAuditor { }
			                                       public sealed class Email : INotifier { }
			                                       // The composite is a legitimate member of IAuditor's collection — that membership is unrelated to its INotifier fan-out.
			                                       public sealed class CompositeNotifier : INotifier, IAuditor { public CompositeNotifier(IEnumerable<INotifier> channels) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Transient<CompositeNotifier, IAuditor>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT131"))).IsFalse()
				.Because("the composite is a member of a different service, not the one it composes");
		}
	}
}
