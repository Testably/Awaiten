using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt136Awt137PropertyInjection
	{
		[Fact]
		public async Task ReportsAwt136WhenInjectPropertyHasNoAccessibleSetter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           // [Inject] on a get-only property: there is no accessor to assign through.
			                                           [Inject] public Bus Bus { get; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT136"))).IsTrue()
				.Because("an [Inject] property must have a set or init accessor the container can assign through");
		}

		[Fact]
		public async Task ReportsAwt136WhenInjectPropertyHasProtectedSetter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public class Consumer
			                                       {
			                                           // A protected setter is out of reach from the container's object initializer (not a derived context).
			                                           [Inject] public Bus Bus { get; protected set; }
			                                       }

			                                       [Container]
			                                       [Transient<Bus>]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT136"))).IsTrue()
				.Because("a protected setter cannot be assigned from the container's object initializer, so it must surface as AWT136 rather than an inaccessible-setter error in generated code");
			await That(result.Diagnostics.Any(d => d.Contains("CS0272"))).IsFalse()
				.Because("AWT136 must be reported instead of leaking a compile error into the generated container");
		}

		[Fact]
		public async Task ReportsAwt137WhenInjectPropertyIsMarkedArg()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Consumer
			                                       {
			                                           // Runtime arguments flow only through a Func<…> factory, never through property injection.
			                                           [Inject] [Arg] public int Count { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT137"))).IsTrue()
				.Because("runtime arguments are supplied only to constructor parameters, never to an injected property");
		}

		[Fact]
		public async Task ReportsAwt101WhenInjectPropertyHasNoRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Bus { }
			                                       public sealed class Consumer
			                                       {
			                                           [Inject] public Bus Bus { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsTrue()
				.Because("an injected property needs a registration to satisfy it, exactly like a constructor parameter");
		}

		[Fact]
		public async Task ReportsAwt105WhenASingletonInjectsAScopedProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class ScopedDependency { }
			                                       public sealed class SingletonConsumer
			                                       {
			                                           [Inject] public ScopedDependency Dependency { get; set; }
			                                       }

			                                       [Container]
			                                       [Singleton<SingletonConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT105*").AsWildcard()
				.Because("an injected property is a captive-analysis edge exactly like a constructor parameter: a singleton capturing a scoped through it is still captive");
		}

		[Fact]
		public async Task ReportsAwt102WhenInjectedPropertiesFormACycle()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class A { [Inject] public B B { get; set; } }
			                                       public sealed class B { [Inject] public A A { get; set; } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT102*").AsWildcard()
				.Because("an injected property is resolved at construction through the object initializer, so an A -> B -> A property cycle is a real cycle, exactly like a constructor one");
		}
	}
}
