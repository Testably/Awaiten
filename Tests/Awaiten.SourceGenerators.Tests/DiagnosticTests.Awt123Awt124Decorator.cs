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
			                                       [Decorate<LoggingDecorator, IService>]
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
			                                       [Decorate<BadDecorator, IService>]
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
			                                       [Decorate<BadDecorator, IService>]
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
			                                       [Decorate<LoggingDecorator, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a decorator with exactly one inner parameter over a registered service is well-formed");
		}

		[Fact]
		public async Task DoesNotReportAwt124WhenAnExtraParameterIsAlsoAssignableFromTheService()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // (IService inner, object state): `object` is technically assignable-from IService,
			                                       // but the inner is the most-derived assignable parameter, so it is unambiguous.
			                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner, object state) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<LoggingDecorator, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT124"))).IsFalse()
				.Because("the inner is the most-derived assignable parameter, so an unrelated `object` sibling is not ambiguous");
		}

		[Fact]
		public async Task DoesNotReportAwt124ForADecoratorWithAnInternalConstructor()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // The container builds the decorator through its internal (same-assembly) constructor,
			                                       // so validation must consider it too - a public-only check would wrongly report AWT124.
			                                       public sealed class LoggingDecorator : IService { internal LoggingDecorator(IService inner) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<LoggingDecorator, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the decorator's internal same-assembly constructor is the one the container constructs, so it is well-formed");
		}

		[Fact]
		public async Task ReportsAwt124WhenTheOnlyServiceAssignableParameterIsKeyed()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // The single IService parameter is [FromKey]-ed: it selects a specific keyed
			                                       // registration, so it is a separate dependency, not the chain inner - leaving the
			                                       // decorator with no unkeyed parameter to receive the inner instance.
			                                       public sealed class Deco : IService { public Deco([FromKey("k")] IService inner) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<Deco, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT124"))).IsTrue()
				.Because("a [FromKey] parameter is not the chain inner, so the decorator has no parameter to receive it");
		}

		[Fact]
		public async Task DiagnosticsNameTheRealDecoratorTypeNotTheSyntheticIdentity()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // Depending on the collection of the decorated service closes a cycle (the collection
			                                       // now yields the decorator itself), so this reports AWT102 naming the decorator.
			                                       public sealed class Deco : IService { public Deco(IService inner, IEnumerable<IService> all) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<Deco, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT102") && d.Contains("MyCode.Deco"))).IsTrue()
				.Because("the cycle diagnostic must name the real decorator type");
			await That(result.Diagnostics.Any(d => d.Contains("@__dec:"))).IsFalse()
				.Because("the internal synthetic '@__dec:' identity must not leak into user-facing diagnostics");
		}
	}
}
