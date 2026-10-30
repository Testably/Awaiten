namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt206TypeofImplementationNotAssignable
	{
		[Test]
		public async Task ReportsWhenTheImplementationDoesNotImplementTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class Clock { }

			                                       [Container]
			                                       [Singleton(typeof(Clock), typeof(IClock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT206*").AsWildcard()
				.Because("the generic form's TImplementation : TService constraint has no typeof counterpart, so the generator checks it");
		}

		[Test]
		public async Task ReportsWhenTheImplementationIsAValueType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public struct Clock : IClock { }

			                                       [Container]
			                                       [Singleton(typeof(Clock), typeof(IClock))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT206*").AsWildcard()
				.Because("the generic form only accepts a class implementation");
		}

		[Test]
		public async Task DoesNotReportForAnAssignablePair()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public class ClockBase : IClock { }
			                                       public sealed class Clock : ClockBase { }

			                                       [Container]
			                                       [Singleton(typeof(Clock), typeof(IClock))]
			                                       [Singleton(typeof(Clock), typeof(ClockBase))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT206*").AsWildcard()
				.Because("an inherited interface or base class is a valid service");
		}
	}
}
