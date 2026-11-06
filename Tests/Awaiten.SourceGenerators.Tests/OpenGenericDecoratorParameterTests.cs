using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Open generic expansion seeded from decorators and composites: their constructor parameters and injected
///     properties close an open registration exactly like a registration's, for the closed forms directly and for the
///     open forms once per closing they expand onto, while the decorated inner parameter closes nothing.
/// </summary>
public class OpenGenericDecoratorParameterTests
{
	private const string Types = """
	                             using Awaiten;
	                             using System;
	                             using System.Collections.Generic;
	                             using System.Threading.Tasks;

	                             namespace MyCode;

	                             public sealed class Client { }
	                             public sealed class Order { }
	                             public interface IGen<T> { }
	                             public sealed class Gen<T> : IGen<T> { }
	                             public interface IBox { }
	                             public sealed class Box : IBox { }
	                             public interface IRep<T> { }
	                             public sealed class Rep<T> : IRep<T> { }
	                             public sealed class Shop { public Shop(IRep<Order> orders) { } }

	                             """;

	private const string ContainerSource = "Awaiten.MyCode.MyContainer.g.cs";

	private const string ClosedGen = "new global::MyCode.Gen<global::MyCode.Client>";

	private const string ClosedOrderGen = "new global::MyCode.Gen<global::MyCode.Order>";

	public sealed class ClosedDecorator
	{
		[Test]
		public async Task Parameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class BoxDecorator : IBox { public BoxDecorator(IBox inner, IGen<Client> gen) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<Box, IBox>]
			                                               [Decorate<BoxDecorator, IBox>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the decorator's IGen<Client> parameter requests a closing the open registration provides");
			await That(result.Sources[ContainerSource]).Contains(ClosedGen)
				.Because("the closing is synthesized for the decorator parameter");
		}

		[Test]
		[Arguments("Lazy<IGen<Client>>")]
		[Arguments("Func<IGen<Client>>")]
		[Arguments("IEnumerable<IGen<Client>>")]
		[Arguments("IGen<Client>[]")]
		[Arguments("Owned<IGen<Client>>")]
		[Arguments("Task<IGen<Client>>")]
		[Arguments("Func<Task<IGen<Client>>>")]
		public async Task WrappedParameter_ClosesTheOpenRegistration(string parameterType)
		{
			GeneratorResult result = Generator.Run(Types + $$"""
			                                                 public sealed class BoxDecorator : IBox { public BoxDecorator(IBox inner, {{parameterType}} gen) { } }

			                                                 [Container]
			                                                 [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                                 [Singleton<Box, IBox>]
			                                                 [Decorate<BoxDecorator, IBox>]
			                                                 public static partial class MyContainer
			                                                 {
			                                                 }
			                                                 """);

			await That(result.Diagnostics).IsEmpty()
				.Because($"a {parameterType} decorator parameter resolves IGen<Client>");
			await That(result.Sources[ContainerSource]).Contains(ClosedGen)
				.Because("a collection parameter would otherwise silently resolve empty instead of reporting AWT101");
		}

		[Test]
		public async Task KeyedDictionaryParameter_ClosesTheKeyedOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class BoxDecorator : IBox { public BoxDecorator(IBox inner, IReadOnlyDictionary<string, IGen<Client>> gens) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>), Key = "a")]
			                                               [Singleton<Box, IBox>]
			                                               [Decorate<BoxDecorator, IBox>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the keyed dictionary resolves every keyed registration of IGen<Client>");
			await That(result.Sources[ContainerSource]).Contains(ClosedGen)
				.Because("the dictionary would otherwise silently resolve empty");
		}

		[Test]
		public async Task InjectedProperty_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class BoxDecorator : IBox
			                                               {
			                                                   public BoxDecorator(IBox inner) { }

			                                                   [Inject] public IGen<Client> Gen { get; set; } = null!;
			                                               }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<Box, IBox>]
			                                               [Decorate<BoxDecorator, IBox>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the decorator's [Inject] property requests IGen<Client> like a constructor parameter");
			await That(result.Sources[ContainerSource]).Contains(ClosedGen)
				.Because("the closing is synthesized for the injected property");
		}

		[Test]
		public async Task InnerParameter_DoesNotClose()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepDecorator : IRep<Order> { public RepDecorator(IRep<Order> inner) { } }

			                                               [Container]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Decorate<RepDecorator, IRep<Order>>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).Contains("*AWT123*").AsWildcard()
				.Because("nothing asks for IRep<Order>, so the decorator has nothing to wrap");
			await That(result.Sources[ContainerSource]).DoesNotContain("Rep<global::MyCode.Order>()")
				.Because("the decorated inner parameter must not synthesize the closing it decorates");
		}

		[Test]
		public async Task MissingDependency_StillReportsAwt101()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public interface IOther<T> { }
			                                               public sealed class BoxDecorator : IBox { public BoxDecorator(IBox inner, IOther<Client> other) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<Box, IBox>]
			                                               [Decorate<BoxDecorator, IBox>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).Contains("*AWT101*IOther<MyCode.Client>*").AsWildcard()
				.Because("no open registration provides IOther<>, so the decorator parameter stays unresolved");
		}

		[Test]
		public async Task ConstraintViolation_ReportsAwt126Once()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGen<T> { }
			                                       public sealed class Gen<T> : IGen<T> where T : class { }
			                                       public interface IBox { }
			                                       public sealed class Box : IBox { }
			                                       public sealed class BoxDecorator : IBox { public BoxDecorator(IBox inner, IGen<int> gen) { } }
			                                       public sealed class BoxComposite : IBox { public BoxComposite(IBox[] boxes, IGen<int> gen) { } }

			                                       [Container]
			                                       [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                       [Singleton<Box, IBox>]
			                                       [Decorate<BoxDecorator, IBox>]
			                                       [Composite<BoxComposite, IBox>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(d => d.Contains("AWT126"))).IsEqualTo(1)
				.Because("Gen<int> violates where T : class, reported once however many decorators and composites request it");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the constraint-rejected closing is reported as AWT126, as for a constructor parameter");
		}
	}

	public sealed class ClosedComposite
	{
		[Test]
		public async Task Parameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class BoxComposite : IBox { public BoxComposite(IEnumerable<IBox> boxes, IGen<Client> gen) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<Box, IBox>]
			                                               [Composite<BoxComposite, IBox>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the composite's IGen<Client> parameter requests a closing the open registration provides");
			await That(result.Sources[ContainerSource]).Contains(ClosedGen)
				.Because("the closing is synthesized for the composite parameter");
		}

		[Test]
		public async Task InjectedProperty_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class BoxComposite : IBox
			                                               {
			                                                   public BoxComposite(IEnumerable<IBox> boxes) { }

			                                                   [Inject] public IGen<Client> Gen { get; set; } = null!;
			                                               }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<Box, IBox>]
			                                               [Composite<BoxComposite, IBox>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the composite's [Inject] property requests IGen<Client> like a constructor parameter");
			await That(result.Sources[ContainerSource]).Contains(ClosedGen)
				.Because("the closing is synthesized for the injected property");
		}

		[Test]
		public async Task CollectionParameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepComposite : IRep<Order> { public RepComposite(IEnumerable<IRep<Order>> all) { } }

			                                               [Container]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Composite<RepComposite, IRep<Order>>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the composite's collection parameter is a dependency like any other collection");
			await That(result.Sources[ContainerSource]).Contains("new global::MyCode.Rep<global::MyCode.Order>")
				.Because("the composite would otherwise silently fan out over an empty collection");
		}
	}

	public sealed class OpenDecorator
	{
		[Test]
		public async Task Parameter_ClosesTheOpenRegistrationPerClosing()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepDecorator<T> : IRep<T> { public RepDecorator(IRep<T> inner, IGen<T> gen) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
			                                               [Singleton<Shop>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the decorator closed at Order requests IGen<Order>, which the open registration provides");
			await That(result.Sources[ContainerSource]).Contains(ClosedOrderGen)
				.Because("the closing is synthesized for the open decorator's closing");
		}

		[Test]
		public async Task Parameter_OfAnExplicitlyRegisteredClosing_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepDecorator<T> : IRep<T> { public RepDecorator(IRep<T> inner, IGen<T> gen) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Transient<Rep<Order>, IRep<Order>>]
			                                               [Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the decorator expands onto the explicit IRep<Order> and requests IGen<Order>");
			await That(result.Sources[ContainerSource]).Contains(ClosedOrderGen)
				.Because("the closing is synthesized for the open decorator's closing");
		}

		[Test]
		public async Task ClosingNeededByAnotherDecorator_IsExpandedToAFixpoint()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public interface ILog<T> { }
			                                               public sealed class Log<T> : ILog<T> { }
			                                               public sealed class RepDecorator<T> : IRep<T> { public RepDecorator(IRep<T> inner, IGen<T> gen) { } }
			                                               public sealed class GenDecorator<T> : IGen<T> { public GenDecorator(IGen<T> inner, ILog<T> log) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Transient(typeof(Log<>), typeof(ILog<>))]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
			                                               [Decorate(typeof(GenDecorator<>), typeof(IGen<>))]
			                                               [Singleton<Shop>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("IGen<Order>, closed for the repository decorator, brings in the generator decorator, which requests ILog<Order>");
			await That(result.Sources[ContainerSource]).Contains("new global::MyCode.Log<global::MyCode.Order>")
				.Because("the closing a decorator of a synthesized closing requests is synthesized as well");
		}

		[Test]
		public async Task Parameter_OfAKeyedClosing_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepDecorator<T> : IRep<T> { public RepDecorator(IRep<T> inner, IGen<T> gen) { } }
			                                               public sealed class KeyedShop { public KeyedShop([FromKey("a")] IRep<Order> orders) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>), Key = "a")]
			                                               [Decorate(typeof(RepDecorator<>), typeof(IRep<>), AllKeys = true)]
			                                               [Singleton<KeyedShop>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the decorator selects every key, so it wraps IRep<Order> under 'a' and requests IGen<Order>");
			await That(result.Sources[ContainerSource]).Contains(ClosedOrderGen)
				.Because("the closing is synthesized for the decorator of the keyed closing");
		}

		[Test]
		public async Task UnselectedKey_DoesNotClose()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepDecorator<T> : IRep<T> { public RepDecorator(IRep<T> inner, IGen<T> gen) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Transient<Rep<Order>, IRep<Order>>(Key = "a")]
			                                               [Decorate(typeof(RepDecorator<>), typeof(IRep<>), Keys = new object[] { "b" })]
			                                               [Singleton<Shop>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Sources[ContainerSource]).DoesNotContain(ClosedOrderGen)
				.Because("the decorator selects only key 'b', so it never wraps IRep<Order> and needs no IGen<Order>");
		}

		[Test]
		public async Task ClosingViolatingTheDecoratorsConstraints_DoesNotClose()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGen<T> { }
			                                       public sealed class Gen<T> : IGen<T> where T : class { }
			                                       public interface IRep<T> { }
			                                       public sealed class Rep<T> : IRep<T> { }
			                                       public sealed class RepDecorator<T> : IRep<T> where T : class { public RepDecorator(IRep<T> inner, IGen<T> gen) { } }
			                                       public sealed class Shop { public Shop(IRep<int> numbers) { } }

			                                       [Container]
			                                       [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                       [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                       [Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
			                                       [Singleton<Shop>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT171*").AsWildcard()
				.Because("RepDecorator<int> violates where T : class, so the decorator skips that closing");
			await That(result.Diagnostics).DoesNotContain("*AWT126*").AsWildcard()
				.Because("a skipped decorator closing requests nothing, so IGen<int> is never closed");
		}

		[Test]
		public async Task SelfGrowingDecorator_ReportsAwt129()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class Wrap<T> { }
			                                               public sealed class RepDecorator<T> : IRep<T> { public RepDecorator(IRep<T> inner, IRep<Wrap<T>> next) { } }

			                                               [Container]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
			                                               [Singleton<Shop>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).Contains("*AWT129*").AsWildcard()
				.Because("each decorator closing requests an ever larger closing, which the depth limit stops");
		}
	}

	public sealed class OpenComposite
	{
		[Test]
		public async Task Parameter_ClosesTheOpenRegistrationPerClosing()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class RepComposite<T> : IRep<T> { public RepComposite(IEnumerable<IRep<T>> all, IGen<T> gen) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Transient(typeof(Rep<>), typeof(IRep<>))]
			                                               [Composite(typeof(RepComposite<>), typeof(IRep<>))]
			                                               [Singleton<Shop>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the composite closed at Order requests IGen<Order>, which the open registration provides");
			await That(result.Sources[ContainerSource]).Contains(ClosedOrderGen)
				.Because("the closing is synthesized for the open composite's closing");
		}
	}
}
