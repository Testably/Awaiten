using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt123Awt124Decorator
	{
		[Fact]
		public async Task ReportsAwt123WhenTheDecoratedServiceHasNoRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       // IService has no registration to decorate.
			                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }

			                                       [Container]
			                                       [Decorate<IService, LoggingDecorator>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT123"))).IsTrue()
				.Because("a [Decorate] over a service with no registration has nothing to wrap");
		}

		[Fact]
		public async Task ReportsAwt124WhenTheDecoratorHasNoSingleInnerParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // Two IService parameters: ambiguous which one receives the inner instance.
			                                       public sealed class BadDecorator : IService { public BadDecorator(IService a, IService b) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<IService, BadDecorator>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT124"))).IsTrue()
				.Because("a decorator with more than one parameter assignable to the service is ambiguous");
		}

		[Fact]
		public async Task ReportsAwt124WhenTheDecoratorTakesNoInnerParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // No IService parameter at all: nothing receives the inner instance.
			                                       public sealed class BadDecorator : IService { public BadDecorator() { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<IService, BadDecorator>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT124"))).IsTrue()
				.Because("a decorator with no parameter assignable to the service cannot receive the inner instance");
		}

		[Fact]
		public async Task DoesNotReportForAWellFormedDecorator()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<IService, LoggingDecorator>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a decorator with exactly one inner parameter over a registered service is well-formed");
		}
	}
}
