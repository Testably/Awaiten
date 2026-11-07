using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A <c>[Module]</c> another assembly can name compiles a public handle for each service it registers under an
///     internal type that something a consumer reaches needs, so a container in another assembly depends on the handle
///     instead of the service it cannot name.
/// </summary>
public class ModuleHiddenServiceTests
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

	private static string SimpleName(string fullyQualifiedName)
		=> fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..];

	private static string Handle(string service)
		=> $"global::Lib.CoffeeModule.Internals.Hidden_{SimpleName(service)}_{Fnv1a(service):x8}";

	private static string HiddenFactory(string implementation)
		=> $"HiddenRegistration_{SimpleName(implementation)}_{Fnv1a(implementation):x8}";

	private static string Factory(string implementation)
		=> $"Registration_{SimpleName(implementation)}_{Fnv1a(implementation):x8}";

	private static string Alias(string implementation, string service)
		=> $"Alias_{SimpleName(implementation)}_{Fnv1a($"{implementation}\0{service}"):x8}";

	private static string ModuleSource(GeneratorResult result)
		=> result.Sources.Single(source => source.Key.Contains("ModuleScan")).Value;

	private static GeneratorResult RunLibrary(string library) => Generator.Run(library);

	[Test]
	public async Task PublicRegistration_TakesTheInternalServiceThroughItsHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[Singleton<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");
		string module = ModuleSource(result);
		string handle = Handle("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty()
			.Because("the module registers the internal service the public registration needs");
		await That(module).Contains($"public class Hidden_BeanStore_{Fnv1a("global::Lib.BeanStore"):x8}")
			.Because("the handle is public, so a consumer in another assembly can depend on it");
		await That(module).Contains("internal global::Lib.BeanStore Value { get; }")
			.Because("only the module's assembly can unwrap the handle");
		await That(module).Contains($"public static {handle} {HiddenFactory("global::Lib.BeanStore")}() => new {handle}(new global::Lib.BeanStore());");
		await That(module).Contains($"public static global::Lib.IGrinder {Factory("global::Lib.Grinder")}({handle} @store) => new global::Lib.Grinder(@store.Value);")
			.Because("the factory takes the handle and passes the instance it holds to the constructor");
	}

	[Test]
	public async Task InternalServiceNothingReachableNeeds_GetsNoHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { }

			[Module]
			[Singleton<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");

		await That(ModuleSource(result)).DoesNotContain("Hidden_")
			.Because("no public registration needs the internal service, so a consumer never depends on it");
	}

	[Test]
	public async Task EagerInternalSingleton_GetsAHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class Warmer { }
			internal sealed class Grinder : IGrinder { }

			[Module]
			[Singleton<Warmer>(Eager = true)]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");

		await That(ModuleSource(result)).Contains(HiddenFactory("global::Lib.Warmer"))
			.Because("a consumer builds an eager singleton at startup even though nothing depends on it");
	}

	[Test]
	public async Task DisposableImplementation_IsWrappedInTheMatchingVariant()
	{
		GeneratorResult result = RunLibrary("""
			using System;
			using System.Threading.Tasks;
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal interface IFilter { }
			internal sealed class PaperFilter : IFilter, IDisposable { public void Dispose() { } }
			internal sealed class MetalFilter : IFilter, IAsyncDisposable { public ValueTask DisposeAsync() => default; }
			internal sealed class ClothFilter : IFilter { }
			internal sealed class Grinder : IGrinder { public Grinder(System.Collections.Generic.IEnumerable<IFilter> filters) { } }

			[Module]
			[Transient<PaperFilter, IFilter>]
			[Transient<MetalFilter, IFilter>]
			[Transient<ClothFilter, IFilter>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");
		string module = ModuleSource(result);
		string handle = Handle("global::Lib.IFilter");

		await That(result.Diagnostics).IsEmpty();
		await That(module).Contains($"=> new {handle}.Disposable(new global::Lib.PaperFilter());");
		await That(module).Contains($"=> new {handle}.AsyncDisposable(new global::Lib.MetalFilter());");
		await That(module).Contains($"=> new {handle}(new global::Lib.ClothFilter());")
			.Because("each implementation gets the variant that implements exactly its disposal interfaces");
		await That(module).Contains($"public static global::Lib.IGrinder {Factory("global::Lib.Grinder")}(global::System.Collections.Generic.IEnumerable<{handle}> @filters) => new global::Lib.Grinder(global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(@filters, awaiten__handle => awaiten__handle.Value)));");
	}

	[Test]
	public async Task RelationshipShapes_AreUnwrappedInTheFactory()
	{
		GeneratorResult result = RunLibrary("""
			using System;
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder
			{
			    public Grinder(Func<BeanStore> func, Lazy<BeanStore> lazy, Owned<BeanStore> owned, Func<Owned<BeanStore>> factory) { }
			}

			[Module]
			[Transient<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");
		string module = ModuleSource(result);
		string handle = Handle("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty();
		await That(module).Contains($"(global::System.Func<{handle}> @func, global::System.Lazy<{handle}> @lazy, global::Awaiten.Owned<{handle}> @owned, global::System.Func<global::Awaiten.Owned<{handle}>> @factory)");
		await That(module).Contains("new global::Lib.Grinder(() => @func().Value, new global::System.Lazy<global::Lib.BeanStore>(() => @lazy.Value.Value), global::Awaiten.Internals.AwaitenOwnedProjection.Map(@owned, awaiten__handle => awaiten__handle.Value), () => global::Awaiten.Internals.AwaitenOwnedProjection.Map(@factory(), awaiten__handle => awaiten__handle.Value))");
	}

	[Test]
	public async Task HiddenChain_TakesHandleIntoHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class Silo { }
			internal sealed class BeanStore { public BeanStore(Silo silo) { } }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[Singleton<Silo>]
			[Singleton<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");

		await That(result.Diagnostics).IsEmpty();
		await That(ModuleSource(result)).Contains($"public static {Handle("global::Lib.BeanStore")} {HiddenFactory("global::Lib.BeanStore")}({Handle("global::Lib.Silo")} @silo) => new {Handle("global::Lib.BeanStore")}(new global::Lib.BeanStore(@silo.Value));");
	}

	[Test]
	public async Task ImplementationUnderAnInternalAndAPublicService_SharesOneInstanceThroughAnAlias()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IStock { }
			public interface IGrinder { }
			internal interface IBeanStore { }
			internal sealed class BeanStore : IBeanStore, IStock { }
			internal sealed class Grinder : IGrinder { public Grinder(IBeanStore store) { } }

			[Module]
			[Singleton<BeanStore, IBeanStore>]
			[Singleton<BeanStore, IStock>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");
		string module = ModuleSource(result);

		await That(result.Diagnostics).IsEmpty()
			.Because("the public service is served from the handle that owns the instance, so no second factory splits it");
		await That(module).DoesNotContain($"global::Lib.IStock {Factory("global::Lib.BeanStore")}");
		await That(module).Contains($"public static global::Lib.IStock {Alias("global::Lib.BeanStore", "global::Lib.IStock")}({Handle("global::Lib.IBeanStore")} @awaiten__handle) => ((global::Lib.BeanStore)@awaiten__handle.Value);");
	}

	[Test]
	public async Task SeveralPublicServicesWithoutACoveringType_ShareOneInstanceThroughAHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IFront { }
			public interface IBack { }
			internal sealed class Shared : IFront, IBack { }

			[Module]
			[Singleton<Shared, IFront>]
			[Singleton<Shared, IBack>]
			public static partial class CoffeeModule { }
			""");
		string module = ModuleSource(result);
		string handle = Handle("global::Lib.Shared");

		await That(result.Diagnostics).IsEmpty()
			.Because("one handle owns the instance and both services are aliases of it, so no covering type is needed");
		await That(module).Contains($"public static {handle} {HiddenFactory("global::Lib.Shared")}() => new {handle}(new global::Lib.Shared());");
		await That(module).Contains($"public static global::Lib.IFront {Alias("global::Lib.Shared", "global::Lib.IFront")}({handle} @awaiten__handle) => @awaiten__handle.Value;");
		await That(module).Contains($"public static global::Lib.IBack {Alias("global::Lib.Shared", "global::Lib.IBack")}({handle} @awaiten__handle) => @awaiten__handle.Value;");
	}

	[Test]
	public async Task HooksAndConventions_TakeTheHandle()
	{
		GeneratorResult result = RunLibrary("""
			using System.Threading;
			using System.Threading.Tasks;
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal interface IStartable { Task StartAsync(CancellationToken cancellationToken); }
			internal sealed class BeanStore : IStartable { public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask; }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			[Singleton<BeanStore>(OnActivated = nameof(Activate), OnRelease = nameof(Release))]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule
			{
			    internal static void Activate(BeanStore store) { }
			    internal static void Release(BeanStore store) { }
			}
			""");
		string module = ModuleSource(result);
		string handle = Handle("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty();
		await That(module).Contains($"({handle} @awaiten__instance) => Activate((global::Lib.BeanStore)@awaiten__instance.Value);")
			.Because("the hook wrapper unwraps the handle for the module's own hook");
		await That(module).Contains($"({handle} @awaiten__instance, global::System.Threading.CancellationToken @cancellationToken) => ((global::Lib.IStartable)@awaiten__instance.Value).StartAsync(@cancellationToken);")
			.Because("the initializer wrapper runs the internal convention on the instance the handle holds");
	}

	[Test]
	public async Task SameAssemblyContainer_ConstructsTheInternalServiceDirectly()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[Singleton<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }

			[Container]
			[Import(typeof(CoffeeModule))]
			public static partial class CoffeeContainer { }
			""");
		string container = result.Sources.Single(source => source.Key.Contains("CoffeeContainer")).Value;

		await That(result.Diagnostics).IsEmpty();
		await That(container).DoesNotContain("Hidden_")
			.Because("a container in the module's own assembly names the internal service and never uses a handle");
	}

	public class Consumer
	{
		private const string Container = """
		                                 using Awaiten;
		                                 using Lib;

		                                 namespace MyCode;

		                                 [Container]
		                                 [Import(typeof(CoffeeModule))]
		                                 public static partial class MyContainer;
		                                 """;

		[Test]
		public async Task RegistersTheHandleAsADependencyOnly()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal sealed class BeanStore { }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""", Container);
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			string handle = Handle("global::Lib.BeanStore");

			await That(result.Diagnostics).IsEmpty();
			await That(source).Contains($"global::Lib.CoffeeModule.Internals.{HiddenFactory("global::Lib.BeanStore")}()")
				.Because("the container builds the internal service through the module's handle factory");
			await That(source).Contains("global::Awaiten.IAwaitenResolver<global::Lib.IGrinder>");
			await That(source).DoesNotContain($"global::Awaiten.IAwaitenResolver<{handle}>")
				.And.DoesNotContain($"typeof({handle})")
				.And.DoesNotContain($"<{handle}>>")
				.Because("a handle is never offered for resolution, advertised as a registration or dispatched by type");
		}

		[Test]
		public async Task TwoModulesRegisteringOneInternalImplementation_KeepTheirHandlesApart()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				public interface IRoaster { }
				internal sealed class BeanStore { }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }
				internal sealed class Roaster : IRoaster { public Roaster(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Roaster, IRoaster>]
				public static partial class RoastingModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(CoffeeModule))]
				[Import(typeof(RoastingModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("each module's handle is a registration of its own, so the two never conflict");
		}

		[Test]
		public async Task ConsumerGrantedInternalsVisibleTo_ConstructsDirectly()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

				namespace Lib;

				public interface IGrinder { }
				internal sealed class BeanStore { }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""", Container);
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(result.Diagnostics).IsEmpty();
			await That(source).Contains("new global::Lib.BeanStore()").And.DoesNotContain("Hidden_")
				.Because("a container that can name the internal service constructs it itself and ignores the handles");
		}
	}

	public class Diagnostics
	{
		[Test]
		public async Task UnsupportedShape_ReportsAwt237()
		{
			GeneratorResult result = RunLibrary("""
				using System.Threading.Tasks;
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal sealed class BeanStore { }
				internal sealed class Grinder : IGrinder { public Grinder(Task<BeanStore> store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""");

			await That(result.Diagnostics).Contains("*AWT237*'Lib.Grinder'*constructor parameter 'store'*'Lib.BeanStore'*'System.Threading.Tasks.Task<Lib.BeanStore>'*").AsWildcard();
		}

		[Test]
		public async Task InternalServiceOfAnotherModule_ReportsAwt236()
		{
			GeneratorResult result = RunLibrary("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal sealed class BeanStore { }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				public static partial class StorageModule { }

				[Module]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""");

			await That(result.Diagnostics).Contains("*AWT236*'Lib.BeanStore'*only the module 'Lib.StorageModule'*register 'Lib.BeanStore' in 'Lib.CoffeeModule' too*").AsWildcard();
		}

		[Test]
		public async Task InternalServiceThatCannotBeBuilt_ReportsItsCause()
		{
			GeneratorResult result = RunLibrary("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal sealed class Silo { }
				internal sealed class BeanStore { public BeanStore(Silo silo) { } }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""");

			await That(result.Diagnostics).Contains("*AWT195*'Lib.BeanStore' is registered under the internal service*'Lib.Silo'*register it in the module*").AsWildcard()
				.Because("the internal service needs an internal type the module does not register");
		}

		[Test]
		public async Task InternalServiceThatCannotBeBuilt_WithInternalsVisibleTo_IsLeftToTheFriendAssembly()
		{
			GeneratorResult result = RunLibrary("""
				using Awaiten;

				[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Friend")]

				namespace Lib;

				public interface IGrinder { }
				internal sealed class Silo { }
				internal sealed class BeanStore { public BeanStore(Silo silo) { } }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("an assembly granted InternalsVisibleTo constructs both itself, as before");
		}
	}
}
