using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A <c>[Module]</c> that registers an implementation another assembly cannot construct compiles a factory for it
///     in its own build, so a consuming container in another assembly resolves the registered service without
///     <c>InternalsVisibleTo</c>, while a module of public types keeps generating nothing.
/// </summary>
public class SelfCompiledModuleRegistrationTests
{
	private static uint Fnv1a(string text)
	{
		uint hash = 2166136261;
		foreach (char character in text)
		{
			hash = unchecked((hash ^ character) * 16777619);
		}

		return hash;
	}

	private static string FactoryName(string fullyQualifiedName)
		=> $"Registration_{fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..]}_{Fnv1a(fullyQualifiedName):x8}";

	private static string HookName(string fullyQualifiedName, string slot, string hook)
		=> $"Hook_{slot}_{fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..]}_{Fnv1a($"{fullyQualifiedName}\0{slot}\0{hook}"):x8}";

	private static string ModuleSource(Microsoft.CodeAnalysis.GeneratorDriverRunResult run)
		=> run.Results.SelectMany(r => r.GeneratedSources).Single(s => s.HintName.Contains("ModuleScan")).SourceText.ToString();

	private const string LibrarySource = """
	                                     using Awaiten;

	                                     namespace Lib;

	                                     public interface IClock { }
	                                     public sealed class SystemClock : IClock { }
	                                     public interface IGreeter { }

	                                     internal sealed class Greeter : IGreeter
	                                     {
	                                         public Greeter(IClock clock) { }
	                                     }

	                                     [Module]
	                                     [Singleton<Greeter, IGreeter>]
	                                     public static partial class GreetingModule { }
	                                     """;

	private const string ConsumerSource = """
	                                      using Awaiten;
	                                      using Lib;

	                                      namespace MyCode;

	                                      [Container]
	                                      [Import(typeof(GreetingModule))]
	                                      [Singleton<SystemClock, IClock>]
	                                      public static partial class MyContainer
	                                      {
	                                      }
	                                      """;

	public class Module
	{
		[Test]
		public async Task EmitsAFactoryThatReturnsTheService()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator(LibrarySource, [], []);
			string module = ModuleSource(run);

			await That(module).Contains($"public static global::Lib.IGreeter {FactoryName("global::Lib.Greeter")}(global::Lib.IClock @clock) => new global::Lib.Greeter(@clock);")
				.Because("the factory constructs the internal implementation in its own assembly and returns the registered service");
			await That(module).DoesNotContain("GeneratedScanExpansionAttribute")
				.Because("the scan expansion marker belongs to a module that declares a [Scan]");
			await That(module).DoesNotContain("GeneratedScanRegistrationAttribute")
				.Because("a consumer keeps reading the registration attribute itself and finds the factory by name");
		}

		[Test]
		public async Task PublicImplementationWithAnInternalConstructor_ReturnsTheImplementation()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				public interface IFarewell { }

				public sealed class Greeter : IGreeter, IFarewell
				{
				    internal Greeter() { }
				}

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Singleton<Greeter, IFarewell>]
				public static partial class GreetingModule { }
				""", [], []);

			await That(run.Diagnostics).IsEmpty()
				.Because("a consumer can name the public implementation, so both services share the factory's one instance");
			await That(ModuleSource(run)).Contains($"public static global::Lib.Greeter {FactoryName("global::Lib.Greeter")}() => new global::Lib.Greeter();");
		}

		[Test]
		public async Task ModuleOfPublicTypes_GeneratesNothing()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				public sealed class Greeter : IGreeter { }
				internal interface IInternal { }
				internal sealed class Internal : IInternal { }

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Singleton<Internal, IInternal>]
				[Singleton<Greeter>(Factory = nameof(Create))]
				public static class GreetingModule
				{
				    public static Greeter Create() => new();
				}
				""", [], []);

			await That(run.Results.SelectMany(r => r.GeneratedSources)).IsEmpty()
				.Because("a public implementation and a service another assembly cannot name need no self-compiled factory, so nothing changes for them");
			await That(run.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task InternalModule_GeneratesNothing()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				internal sealed class Greeter : IGreeter { }

				[Module]
				[Singleton<Greeter, IGreeter>]
				internal static class GreetingModule { }
				""", [], []);

			await That(run.Results.SelectMany(r => r.GeneratedSources)).IsEmpty()
				.Because("another assembly cannot import an internal module without InternalsVisibleTo, which also lets it construct the implementation");
			await That(run.Diagnostics).IsEmpty()
				.Because("an internal module need not be partial");
		}

		[Test]
		public async Task EmitsAWrapperPerHook()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				internal sealed class Greeter : IGreeter { }

				[Module]
				[Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate))]
				public static partial class GreetingModule
				{
				    internal static void Activate(Greeter greeter) { }
				}
				""", [], []);

			await That(run.Diagnostics).IsEmpty();
			await That(ModuleSource(run)).Contains($"public static void {HookName("global::Lib.Greeter", "OnActivated", "Activate")}(global::Lib.IGreeter @awaiten__instance) => Activate((global::Lib.Greeter)@awaiten__instance);")
				.Because("the public wrapper casts to the internal implementation inside the module, so a consumer runs the internal hook");
		}

		[Test]
		public async Task ScanMatchCarriesItsTraits()
		{
			(_, Microsoft.CodeAnalysis.GeneratorDriverRunResult run) = Generator.RunGenerator("""
				using System;
				using System.Threading;
				using System.Threading.Tasks;
				using Awaiten;

				namespace Lib;

				public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }

				public interface IPlugin { }
				public interface IRoaster { }

				internal sealed class Roaster : IPlugin, IRoaster, IInitializable, IDisposable
				{
				    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
				    public void Dispose() { }
				}

				[Module]
				[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
				[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
				public static partial class PluginModule { }
				""", [], []);

			await That(ModuleSource(run)).Contains("AsyncInitializers = new global::System.Type[] { typeof(global::Lib.IInitializable) }, Disposable = true)")
				.Because("a consumer cannot see that the internal match is async-initialized and disposable, so the module tells it");
		}
	}

	public class Consumer
	{
		[Test]
		public async Task CallsTheGeneratedFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(LibrarySource, ConsumerSource);

			await That(result.Diagnostics).IsEmpty()
				.Because("the internal implementation resolves through its generated factory, without InternalsVisibleTo");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains($"global::Lib.GreetingModule.Internals.{FactoryName("global::Lib.Greeter")}(");
			await That(source).DoesNotContain("global::Lib.Greeter(")
				.Because("the consumer never names the internal implementation");
		}

		[Test]
		public async Task ReportsAwt154WhenTheModuleWasBuiltWithoutTheGenerator()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly(LibrarySource.Replace("partial ", ""), ConsumerSource);

			await That(result.Diagnostics).Contains("*AWT154*GreetingModule*Greeter*").AsWildcard()
				.Because("the module carries no generated factory for an implementation this container cannot construct");
			await That(result.Diagnostics).DoesNotContain("*AWT104*").AsWildcard()
				.Because("the version guard replaces the misleading missing-constructor error");
		}

		[Test]
		public async Task DropsARegistrationWhoseServiceItCannotName()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				internal interface ICache { }
				internal sealed class Cache : ICache { }
				internal sealed class Greeter : IGreeter { }

				[Module]
				[Singleton<Greeter, IGreeter>]
				[Singleton<Cache, ICache>]
				public static partial class GreetingModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("nothing in this container can resolve or depend on a service it cannot name, so the internal wiring stays in the library");
		}

		[Test]
		public async Task SameAssemblyContainer_ConstructsDirectly()
		{
			GeneratorResult result = Generator.Run(LibrarySource + """

				[Container]
				[Import(typeof(GreetingModule))]
				[Singleton<SystemClock, IClock>]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.Lib.MyContainer.g.cs"]).Contains("new global::Lib.Greeter(")
				.Because("with no assembly boundary the container constructs the implementation itself");
		}

		[Test]
		public async Task AsyncInitializableImplementation_IsInitialized()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using System;
				using System.Threading;
				using System.Threading.Tasks;
				using Awaiten;

				namespace Lib;

				public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }

				public interface IGreeter { }

				internal sealed class Greeter : IGreeter, IInitializable
				{
				    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
				}

				[Module]
				[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
				[Singleton<Greeter, IGreeter>]
				public static partial class GreetingModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("((global::Lib.IInitializable)created).InitializeAsync(cancellationToken)")
				.Because("the consumer matches the module's convention against the implementation's metadata and awaits it");
		}

		[Test]
		public async Task AsyncInitializableScanMatch_IsInitialized()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using System.Threading;
				using System.Threading.Tasks;
				using Awaiten;

				namespace Lib;

				public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }

				public interface IPlugin { }
				public interface IRoaster { }

				internal sealed class Roaster : IPlugin, IRoaster, IInitializable
				{
				    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
				}

				[Module]
				[AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
				[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
				public static partial class PluginModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(PluginModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("((global::Lib.IInitializable)created).InitializeAsync(cancellationToken)")
				.Because("the module records the conventions of the match the consumer cannot see, so the internal match is initialized as a container scan's would be");
		}
	}

	public class OutOfScope
	{
		[Test]
		public async Task OpenGenericInternalImplementation_ReportsAwt230AtTheConsumer()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IRepository<T> { }
				internal sealed class Repository<T> : IRepository<T> { }

				[Module]
				[Singleton(typeof(Repository<>), typeof(IRepository<>))]
				public static class RepositoryModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				public sealed class Order { }
				public sealed class Shop
				{
				    public Shop(IRepository<Order> orders) { }
				}

				[Container]
				[Import(typeof(RepositoryModule))]
				[Singleton<Shop>]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT230*").AsWildcard()
				.Because("an open generic registration is not self-compiled in v1, so the consumer reports the implementation it cannot construct");
			await That(result.Diagnostics).DoesNotContain("*AWT104*").AsWildcard()
				.Because("the clear diagnostic replaces the misleading constructor error, leaving only the missing dependency it causes");
		}

		[Test]
		public async Task InternalDecorator_ReportsAwt230AtTheConsumer()
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
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT230*").AsWildcard()
				.Because("an internal decorator is not self-compiled in v1, so the consumer reports the decorator it cannot construct");
			await That(result.Diagnostics).HasCount(1)
				.Because("the clear diagnostic replaces a misleading constructor error and generated code that would not compile");
		}

		[Test]
		public async Task InternalComposite_ReportsAwt230AtTheConsumer()
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
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT230*").AsWildcard()
				.Because("an internal composite is not self-compiled in v1, so the consumer reports the composite it cannot construct");
			await That(result.Diagnostics).HasCount(1)
				.Because("the clear diagnostic replaces a misleading constructor error and generated code that would not compile");
		}

		[Test]
		public async Task InternalFactoryMember_ReportsAwt108AtTheConsumer()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGreeter { }
				internal sealed class Greeter : IGreeter { }

				[Module]
				[Singleton<IGreeter>(Factory = nameof(Create))]
				public static class GreetingModule
				{
				    internal static IGreeter Create() => new Greeter();
				}
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(GreetingModule))]
				public static partial class MyContainer
				{
				}
				""");

			await That(result.Diagnostics).Contains("*AWT108*").AsWildcard()
				.Because("an internal Factory member of another assembly is not visible to the consumer, which reports it as not found");
		}
	}
}
