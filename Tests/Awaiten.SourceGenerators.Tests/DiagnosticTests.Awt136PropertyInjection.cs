using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt136PropertyInjection
	{
		[Test]
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

			await That(result.Diagnostics).Contains("*AWT136*").AsWildcard()
				.Because("an [Inject] property must have a set or init accessor the container can assign through");
		}

		[Test]
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

			await That(result.Diagnostics).Contains("*AWT136*").AsWildcard()
				.Because("a protected setter cannot be assigned from the container's object initializer, so it must surface as AWT136 rather than an inaccessible-setter error in generated code");
			await That(result.Diagnostics).DoesNotContain("*CS0272*").AsWildcard()
				.Because("AWT136 must be reported instead of leaking a compile error into the generated container");
		}

		[Test]
		public async Task ArgOnInjectProperty_IsACompilerError()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Consumer
			                                       {
			                                           [Inject] [Arg] public int Count { get; set; }
			                                       }

			                                       [Container]
			                                       [Transient<Consumer>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*CS0592*Arg*").AsWildcard()
				.Because("[Arg] is valid only on parameters, so the compiler itself rejects it on a property");
			await That(result.Diagnostics).DoesNotContain("*AWT137*").AsWildcard()
				.Because("AWT137 is retired now that the attribute target makes the mistake a compiler error");
		}

		[Test]
		public async Task ArgOnPositionalRecordParameter_TargetsTheConstructorParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Engine { }
			                                       public sealed record Ticket(Engine Engine, [Arg] string Code);
			                                       public sealed class Booth(Func<string, Ticket> tickets)
			                                       {
			                                           public Ticket Issue(string code) => tickets(code);
			                                       }

			                                       [Container]
			                                       [Singleton<Engine>]
			                                       [Transient<Ticket>]
			                                       [Singleton<Booth>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("[Arg] on a positional record parameter applies to the constructor parameter, so the record is a parameterized service behind Func<string, Ticket>");
		}

		[Test]
		public async Task PropertyTargetedArgOnPositionalRecordParameter_IsACompilerError()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed record Ticket([property: Arg] string Code);
			                                       """);

			await That(result.Diagnostics).Contains("*CS0592*Arg*").AsWildcard()
				.Because("the property target on a positional record parameter names the generated property, where [Arg] is not valid");
		}

		[Test]
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

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.Because("an injected property needs a registration to satisfy it, exactly like a constructor parameter");
		}

		[Test]
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

		[Test]
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
