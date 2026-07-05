using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt132Composite
	{
		[Fact]
		public async Task ReportsAwt132WhenTwoCompositesOfDifferentTypesNameTheSameService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public sealed class Email : INotifier { }
			                                       public sealed class CompositeA : INotifier { public CompositeA(IEnumerable<INotifier> channels) { } }
			                                       public sealed class CompositeB : INotifier { public CompositeB(IEnumerable<INotifier> channels) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Composite<CompositeA, INotifier>]
			                                       [Composite<CompositeB, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT132"))).IsTrue()
				.Because("a service can have at most one composite façade");
		}

		[Fact]
		public async Task DoesNotReportAwt132WhenTheSameCompositeTypeIsNamedTwiceForOneService()
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
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("naming the same composite type twice for one service is idempotent, not a conflict");
		}

		[Fact]
		public async Task DoesNotReportAwt132ForCompositesOverDifferentServices()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface INotifier { }
			                                       public interface IValidator { }
			                                       public sealed class Email : INotifier { }
			                                       public sealed class NotNull : IValidator { }
			                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(IEnumerable<INotifier> channels) { } }
			                                       public sealed class CompositeValidator : IValidator { public CompositeValidator(IEnumerable<IValidator> rules) { } }

			                                       [Container]
			                                       [Transient<Email, INotifier>]
			                                       [Transient<NotNull, IValidator>]
			                                       [Composite<CompositeNotifier, INotifier>]
			                                       [Composite<CompositeValidator, IValidator>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("distinct services may each have their own composite, [Composite] is AllowMultiple for exactly this");
		}
	}
}
