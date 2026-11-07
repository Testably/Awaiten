namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt230ModuleContributionNotSelfCompiled
	{
		private const string Consumer = """
		                                using Awaiten;
		                                using Lib;

		                                namespace MyCode;

		                                [Container]
		                                [Import(typeof(GreetingModule))]
		                                public static partial class MyContainer
		                                {
		                                }
		                                """;

		[Test]
		public async Task ReportsAnInternalDecorator()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				public sealed class Greeter : IGreeter { }
				internal sealed class LoudGreeter : IGreeter
				{
				    public LoudGreeter(IGreeter inner) { }
				}

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Decorate<LoudGreeter, IGreeter>]
				public static class GreetingModule { }
				""", Consumer);

			await That(result.Diagnostics).Contains("*AWT230*GreetingModule*the decorator 'Lib.LoudGreeter'*").AsWildcard()
				.Because("a module that is not partial carries no factory for its internal decorator, so the consumer cannot construct it");
		}

		[Test]
		public async Task ReportsAnInternalComposite()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using System.Collections.Generic;
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				public sealed class Greeter : IGreeter { }
				internal sealed class AllGreeters : IGreeter
				{
				    public AllGreeters(IEnumerable<IGreeter> greeters) { }
				}

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Composite<AllGreeters, IGreeter>]
				public static class GreetingModule { }
				""", Consumer);

			await That(result.Diagnostics).Contains("*AWT230*GreetingModule*the composite 'Lib.AllGreeters'*").AsWildcard()
				.Because("a module that is not partial carries no factory for its internal composite");
		}

		[Test]
		public async Task ReportsAnInternalOpenGenericImplementation()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter<T> { }
				internal sealed class Greeter<T> : IGreeter<T> { }

				[Module]
				[Singleton(typeof(Greeter<>), typeof(IGreeter<>))]
				public static class GreetingModule { }
				""", Consumer);

			await That(result.Diagnostics).Contains("*AWT230*GreetingModule*the open generic registration of 'Lib.Greeter<T>'*").AsWildcard();
		}

		[Test]
		public async Task DoesNotReportAPublicDecorator()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				public sealed class Greeter : IGreeter { }
				public sealed class LoudGreeter : IGreeter
				{
				    public LoudGreeter(IGreeter inner) { }
				}

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Decorate<LoudGreeter, IGreeter>]
				public static class GreetingModule { }
				""", Consumer);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task DoesNotReportAnInternalDecoratorOfASameAssemblyModule()
		{
			GeneratorResult result = Generator.Run("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				public sealed class Greeter : IGreeter { }
				internal sealed class LoudGreeter : IGreeter
				{
				    public LoudGreeter(IGreeter inner) { }
				}

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Decorate<LoudGreeter, IGreeter>]
				public static class GreetingModule { }

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("a container in the module's own assembly constructs the internal decorator itself");
		}
	}
}
