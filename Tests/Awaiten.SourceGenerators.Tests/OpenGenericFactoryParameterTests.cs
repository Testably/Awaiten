namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Open generic expansion seeded from what actually produces a registration: the parameters of a <c>Factory</c>
///     close an open registration exactly like constructor parameters, the constructor of a factory- or
///     instance-produced type closes nothing, and every relationship and collection shape a parameter may use is
///     unwrapped to the service it resolves.
/// </summary>
public class OpenGenericFactoryParameterTests
{
	private const string Types = """
	                             using Awaiten;
	                             using System;
	                             using System.Collections.Generic;
	                             using System.Threading.Tasks;

	                             namespace MyCode;

	                             public sealed class Client { }
	                             public interface IGen<T> { }
	                             public sealed class Gen<T> : IGen<T> { }
	                             public interface IThirdParty { }
	                             public sealed class ThirdParty : IThirdParty { public ThirdParty(object dependency) { } }

	                             """;

	private const string ClosedGen = "new global::MyCode.Gen<global::MyCode.Client>";

	public sealed class Factory
	{
		[Test]
		public async Task Parameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<IThirdParty>(Factory = nameof(Create))]
			                                               public static partial class MyContainer
			                                               {
			                                                   private static IThirdParty Create(IGen<Client> gen) => new ThirdParty(gen);
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the factory parameter requests IGen<Client>, which the open registration provides");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains(ClosedGen)
				.Because("the closing is synthesized for the factory parameter");
		}

		[Test]
		[Arguments("Eager = true")]
		[Arguments("Key = \"third\"")]
		public async Task ParameterOfAFactoryWithOptions_ClosesTheOpenRegistration(string option)
		{
			GeneratorResult result = Generator.Run(Types + $$"""
			                                                 [Container]
			                                                 [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                                 [Singleton<IThirdParty>(Factory = nameof(Create), {{option}})]
			                                                 public static partial class MyContainer
			                                                 {
			                                                     private static IThirdParty Create(IGen<Client> gen) => new ThirdParty(gen);
			                                                 }
			                                                 """);

			await That(result.Diagnostics).IsEmpty()
				.Because($"the factory parameter closes the open registration also with {option}");
		}

		[Test]
		public async Task AsyncFactoryParameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<IThirdParty>(Factory = nameof(CreateAsync))]
			                                               public static partial class MyContainer
			                                               {
			                                                   private static Task<IThirdParty> CreateAsync(IGen<Client> gen)
			                                                       => Task.FromResult<IThirdParty>(new ThirdParty(gen));
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an async factory's parameters resolve like a synchronous factory's");
		}

		[Test]
		public async Task TypeofFactoryParameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton(typeof(IThirdParty), Factory = nameof(Create))]
			                                               public static partial class MyContainer
			                                               {
			                                                   private static IThirdParty Create(IGen<Client> gen) => new ThirdParty(gen);
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the closed typeof form produces through the same factory as the generic form");
		}

		[Test]
		public async Task ModuleFactoryParameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               [Module]
			                                               [Singleton<IThirdParty>(Factory = nameof(Create))]
			                                               public static partial class ThirdPartyModule
			                                               {
			                                                   public static IThirdParty Create(IGen<Client> gen) => new ThirdParty(gen);
			                                               }

			                                               [Container]
			                                               [Import(typeof(ThirdPartyModule))]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an imported module's factory parameter closes the container's open registration");
		}

		[Test]
		public async Task FromKeyParameter_ClosesTheKeyedOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>), Key = "fast")]
			                                               [Singleton<IThirdParty>(Factory = nameof(Create))]
			                                               public static partial class MyContainer
			                                               {
			                                                   private static IThirdParty Create([FromKey("fast")] IGen<Client> gen) => new ThirdParty(gen);
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a [FromKey] factory parameter closes the open registration under that key");
		}

		[Test]
		[Arguments("Lazy<IGen<Client>>")]
		[Arguments("Func<IGen<Client>>")]
		[Arguments("IEnumerable<IGen<Client>>")]
		[Arguments("IReadOnlyList<IGen<Client>>")]
		[Arguments("IGen<Client>[]")]
		public async Task WrappedParameter_ClosesTheOpenRegistration(string parameterType)
		{
			GeneratorResult result = Generator.Run(Types + $$"""
			                                                 [Container]
			                                                 [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                                 [Singleton<IThirdParty>(Factory = nameof(Create))]
			                                                 public static partial class MyContainer
			                                                 {
			                                                     private static IThirdParty Create({{parameterType}} gen) => new ThirdParty(gen);
			                                                 }
			                                                 """);

			await That(result.Diagnostics).IsEmpty()
				.Because($"a {parameterType} factory parameter resolves IGen<Client>");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains(ClosedGen)
				.Because("a collection parameter would otherwise silently resolve empty instead of reporting AWT101");
		}

		[Test]
		public async Task SelfCompiledModuleScanFactoryParameter_ClosesTheOpenRegistration()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public sealed class Client { }
				public interface IGen<T> { }
				public interface IPlugin { }

				internal sealed class Plugin : IPlugin
				{
				    public Plugin(IGen<Client> gen) { }
				}

				[Module]
				[Scan<IPlugin>(As = ScanAs.Marker)]
				public static partial class PluginModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				public sealed class Gen<T> : IGen<T> { }

				[Container]
				[Import(typeof(PluginModule))]
				[Transient(typeof(Gen<>), typeof(IGen<>))]
				public static partial class MyContainer { }
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("the generated module factory's parameters mirror the internal constructor the container cannot see");
		}

		[Test]
		public async Task MissingOpenRegistration_StillReportsAwt101()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public interface IOther<T> { }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                               [Singleton<IThirdParty>(Factory = nameof(Create))]
			                                               public static partial class MyContainer
			                                               {
			                                                   private static IThirdParty Create(IOther<Client> other) => new ThirdParty(other);
			                                               }
			                                               """);

			await That(result.Diagnostics).Contains("*AWT101*IOther<MyCode.Client>*").AsWildcard()
				.Because("no open registration provides IOther<>, so the factory parameter stays unresolved");
		}

		[Test]
		public async Task ConstraintViolation_ReportsAwt126Only()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGen<T> { }
			                                       public sealed class Gen<T> : IGen<T> where T : class { }
			                                       public interface IThirdParty { }
			                                       public sealed class ThirdParty : IThirdParty { }

			                                       [Container]
			                                       [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                       [Singleton<IThirdParty>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IThirdParty Create(IGen<int> gen) => new ThirdParty();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT126*").AsWildcard()
				.Because("Gen<int> violates where T : class also when a factory parameter requests it");
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the constraint-rejected closing is reported once as AWT126, as for a constructor parameter");
		}

		[Test]
		public async Task ArgParameter_DoesNotClose()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGen<T> { }
			                                       public sealed class Gen<T> : IGen<T> where T : class { }
			                                       public interface IThirdParty { }
			                                       public sealed class ThirdParty : IThirdParty { }

			                                       [Container]
			                                       [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                       [Transient<IThirdParty>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IThirdParty Create([Arg] IGen<int> gen) => new ThirdParty();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT126*").AsWildcard()
				.Because("an [Arg] parameter is supplied by the caller, so it requests no closing that could violate a constraint");
		}
	}

	public sealed class Production
	{
		[Test]
		[Arguments("Factory = nameof(Create)")]
		[Arguments("Instance = nameof(Existing)")]
		public async Task ConstructorOfAProducedImplementation_DoesNotClose(string production)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public interface IGen<T> { }
			                                         public sealed class Gen<T> : IGen<T> where T : class { }
			                                         public sealed class ThirdParty { public ThirdParty(IGen<int> gen) { } }

			                                         [Container]
			                                         [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                         [Singleton<ThirdParty>({{production}})]
			                                         public static partial class MyContainer
			                                         {
			                                             private static readonly ThirdParty Existing = new(null!);

			                                             private static ThirdParty Create() => new(null!);
			                                         }
			                                         """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the container never calls the constructor, so it requests no closing that could violate a constraint");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("Gen<int>")
				.Because("no closing is synthesized for a dependency nothing resolves");
		}

		[Test]
		public async Task ConstructorArgParameter_DoesNotClose()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IGen<T> { }
			                                       public sealed class Gen<T> : IGen<T> where T : class { }
			                                       public sealed class Widget { public Widget([Arg] IGen<int> gen) { } }

			                                       [Container]
			                                       [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                       [Transient<Widget>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT126*").AsWildcard()
				.Because("an [Arg] constructor parameter is supplied by the caller, so it requests no closing");
		}
	}

	public sealed class WrappedConstructorParameter
	{
		[Test]
		[Arguments("Owned<IGen<Client>>")]
		[Arguments("IAsyncEnumerable<IGen<Client>>")]
		[Arguments("Lazy<Task<IGen<Client>>>")]
		[Arguments("Func<Task<IGen<Client>>>")]
		[Arguments("Func<Owned<IGen<Client>>>")]
		[Arguments("Func<Task<Owned<IGen<Client>>>>")]
		[Arguments("Task<Owned<IGen<Client>>>")]
		public async Task Shape_ClosesTheOpenRegistration(string parameterType)
		{
			GeneratorResult result = Generator.Run(Types + $$"""
			                                                 public sealed class Consumer { public Consumer({{parameterType}} gen) { } }

			                                                 [Container]
			                                                 [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                                 [Singleton<Consumer>]
			                                                 public static partial class MyContainer
			                                                 {
			                                                 }
			                                                 """);

			await That(result.Diagnostics).IsEmpty()
				.Because($"a {parameterType} parameter resolves IGen<Client>");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains(ClosedGen)
				.Because("an async collection would otherwise silently resolve empty instead of reporting AWT101");
		}

		[Test]
		public async Task KeyedDictionary_ClosesEveryKeyedOpenRegistration()
		{
			GeneratorResult result = Generator.Run(Types + """
			                                               public sealed class OtherGen<T> : IGen<T> { }
			                                               public sealed class Consumer { public Consumer(IReadOnlyDictionary<string, IGen<Client>> gens) { } }

			                                               [Container]
			                                               [Transient(typeof(Gen<>), typeof(IGen<>), Key = "a")]
			                                               [Transient(typeof(OtherGen<>), typeof(IGen<>), Key = "b")]
			                                               [Singleton<Consumer>]
			                                               public static partial class MyContainer
			                                               {
			                                               }
			                                               """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the keyed dictionary resolves every keyed registration of IGen<Client>");
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains(ClosedGen)
				.And.Contains("new global::MyCode.OtherGen<global::MyCode.Client>")
				.Because("the dictionary would otherwise silently resolve empty");
		}
	}
}
