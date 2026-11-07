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

	private static string Instance(string implementation)
		=> $"global::Lib.CoffeeModule.Internals.HiddenInstance_{SimpleName(implementation)}_{Fnv1a(implementation):x8}";

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
		string instance = Instance("global::Lib.BeanStore");
		await That(module).Contains($"public static {instance} {HiddenFactory("global::Lib.BeanStore")}() => new {instance}(new global::Lib.BeanStore());")
			.Because("the factory wraps the instance in the instance handle of the implementation");
		await That(module).Contains($"public static {handle} {Alias("global::Lib.BeanStore", "global::Lib.BeanStore")}({instance} @awaiten__handle) => new {handle}(@awaiten__handle.Value);")
			.Because("every service of the implementation is served from its instance handle");
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

	public class ScannedToo
	{
		private static string Library(string registrations, bool grant = false) => $$"""
			using Awaiten;

			{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")]" : "")}}

			namespace Lib;

			public interface IAppliance { }
			public interface IFront { }
			public interface IBack { }
			public interface IUser { }
			internal interface IHiddenKettle { }
			internal sealed class Kettle : IAppliance, IHiddenKettle, IFront, IBack { }
			internal sealed class User : IUser { public User(IHiddenKettle kettle) { } }

			[Module]
			[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			{{registrations}}
			public static partial class CoffeeModule { }
			""";

		[Test]
		[Arguments("[Singleton<Kettle, IHiddenKettle>]\n[Singleton<User, IUser>]")]
		[Arguments("[Singleton<Kettle, IFront>]\n[Singleton<Kettle, IBack>]")]
		public async Task AnImplementationTheModuleBuildsThroughAHandle_ReportsAwt197(string registrations)
		{
			GeneratorResult result = RunLibrary(Library(registrations));

			await That(result.Diagnostics).Contains("*AWT197*'Lib.Kettle'*Lib.IAppliance*").AsWildcard()
				.Because("the scan factory would build a second instance of what the handle shares");
		}

		[Test]
		public async Task AnImplementationUnderAnInternalService_NamesTheInternalService()
		{
			GeneratorResult result = RunLibrary(Library("[Singleton<Kettle, IHiddenKettle>]\n[Singleton<User, IUser>]"));

			await That(result.Diagnostics).Contains("*AWT197*'Lib.Kettle' is registered under the internal service 'Lib.IHiddenKettle'*(Lib.IAppliance, Lib.IHiddenKettle)*").AsWildcard();
		}

		[Test]
		[Arguments("[Singleton<Kettle, IHiddenKettle>]\n[Singleton<User, IUser>]")]
		[Arguments("[Singleton<Kettle, IFront>]\n[Singleton<Kettle, IBack>]")]
		public async Task UnderAGrant_SkipsTheScanFactoryAndTheHandleFactory(string registrations)
		{
			GeneratorResult result = RunLibrary(Library(registrations, grant: true));
			string module = ModuleSource(result);

			await That(result.Diagnostics).IsEmpty()
				.Because("an assembly granted InternalsVisibleTo constructs the implementation once for the scan and the registrations");
			await That(module).DoesNotContain($"{HiddenFactory("global::Lib.Kettle")}(")
				.And.DoesNotContain("public static global::Lib.IAppliance Scan_Kettle_")
				.Because("neither factory may build an instance of its own");
		}
	}

	[Test]
	public async Task InstanceHandleOfAnImplementationTheProviderDoesNotBuild_IsStillDeclaredByIt()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Friend")]

			namespace Lib;

			public interface IAppliance { }
			public interface IFront { }
			public interface IBack { }
			public interface IUser { }
			internal interface IHiddenKettle { }
			internal sealed class Kettle : IAppliance, IHiddenKettle, IFront, IBack { }
			internal sealed class User : IUser { public User(IHiddenKettle kettle) { } }

			[Module]
			[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
			[Singleton<Kettle, IFront>]
			[Singleton<Kettle, IBack>]
			public static partial class AModule { }

			[Module]
			[Singleton<Kettle, IHiddenKettle>]
			[Singleton<User, IUser>]
			public static partial class BModule { }
			""");
		string declaring = result.Sources.Single(source => source.Key.Contains("AModule")).Value;
		string user = result.Sources.Single(source => source.Key.Contains("BModule")).Value;

		await That(user).Contains($"global::Lib.AModule.Internals.HiddenInstance_Kettle_{Fnv1a("global::Lib.Kettle"):x8}")
			.Because("the module first by name declares the instance handle every other module builds the implementation into");
		await That(declaring).Contains($"public class HiddenInstance_Kettle_{Fnv1a("global::Lib.Kettle"):x8}")
			.Because("the declaring module emits the handle even when its own scan keeps it from building the implementation");
	}

	public class StartedSingletons
	{
		private static string Library(string registration, bool partial = true, bool grant = false) => $$"""
			using System.Threading.Tasks;
			using Awaiten;

			{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")]" : "")}}

			namespace Lib;

			public interface IClock { }
			public sealed class Clock : IClock { }
			internal interface IStartable { Task StartAsync(); }
			internal sealed class Warmer : IStartable { public Task StartAsync() => Task.CompletedTask; }

			[Module]
			[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			{{registration}}
			[Singleton<Clock, IClock>]
			public static {{(partial ? "partial " : "")}}class CoffeeModule { }
			""";

		[Test]
		[Arguments("[Singleton<Warmer>(Eager = true)]")]
		[Arguments("[Singleton<Warmer>]")]
		public async Task InternalSingletonAConsumerStarts_GetsAHandleInAModuleWithNothingElseToGenerate(string registration)
		{
			GeneratorResult result = RunLibrary(Library(registration));

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains(HiddenFactory("global::Lib.Warmer"))
				.Because("a consumer builds an eager or async-initialized singleton though nothing depends on it, so the module needs its handle even without any other generated member");
		}

		[Test]
		[Arguments("[Singleton<Warmer>(Eager = true)]", "eager")]
		[Arguments("[Singleton<Warmer>]", "async-initialized")]
		public async Task InternalSingletonAConsumerStarts_InAModuleThatIsNotPartial_ReportsAwt194(string registration, string kind)
		{
			GeneratorResult result = RunLibrary(Library(registration, partial: false));

			await That(result.Diagnostics).Contains($"*AWT194*'Lib.CoffeeModule' registers the {kind} internal singleton 'Lib.Warmer'*not partial*").AsWildcard()
				.Because("a module that cannot receive the handle would let a container in another assembly drop the singleton silently");
		}

		[Test]
		public async Task InternalSingletonAConsumerStarts_InAModuleThatIsNotPartialUnderAGrant_ReportsAwt231InAConsumerWithoutIt()
		{
			string library = Library("[Singleton<Warmer>(Eager = true)]", partial: false, grant: true);

			await That(RunLibrary(library).Diagnostics).IsEmpty()
				.Because("an assembly granted InternalsVisibleTo constructs the singleton itself");
			await That(Generator.RunWithGeneratedReferencedAssembly(library, Consumer.Container).Diagnostics)
				.Contains("*AWT231*'Lib.CoffeeModule' registers 'Lib.Warmer'*no handle for it because the module, or a class that contains it, is not partial*").AsWildcard()
				.Because("a container that can neither name nor construct the singleton reports it instead of silently never building it");
		}

		[Test]
		public async Task InternalSingletonNothingStarts_InAModuleThatIsNotPartial_DoesNotReportAwt194()
		{
			GeneratorResult result = RunLibrary(Library("[Transient<Warmer>]", partial: false));

			await That(result.Diagnostics).IsEmpty()
				.Because("nothing a consumer reaches needs the internal service, so the module needs no handle and no partial modifier");
		}
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
		await That(module).Contains($"=> new {Instance("global::Lib.PaperFilter")}.Disposable(new global::Lib.PaperFilter());");
		await That(module).Contains($"=> new {Instance("global::Lib.MetalFilter")}.AsyncDisposable(new global::Lib.MetalFilter());");
		await That(module).Contains($"=> new {Instance("global::Lib.ClothFilter")}(new global::Lib.ClothFilter());")
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
		await That(ModuleSource(result)).Contains($"public static {Instance("global::Lib.BeanStore")} {HiddenFactory("global::Lib.BeanStore")}({Handle("global::Lib.Silo")} @silo) => new {Instance("global::Lib.BeanStore")}(new global::Lib.BeanStore(@silo.Value));");
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
			.Because("the public service is served from the instance handle, so no second factory splits it");
		await That(module).DoesNotContain($"global::Lib.IStock {Factory("global::Lib.BeanStore")}");
		await That(module).Contains($"public static global::Lib.IStock {Alias("global::Lib.BeanStore", "global::Lib.IStock")}({Instance("global::Lib.BeanStore")} @awaiten__handle) => @awaiten__handle.Value;");
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
		string handle = Instance("global::Lib.Shared");

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
		string handle = Instance("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty();
		await That(module).Contains($"({handle} @awaiten__instance) => Activate((global::Lib.BeanStore)@awaiten__instance.Value);")
			.Because("the hook wrapper unwraps the handle for the module's own hook");
		await That(module).Contains($"({handle} @awaiten__instance, global::System.Threading.CancellationToken @cancellationToken) => ((global::Lib.IStartable)@awaiten__instance.Value).StartAsync(@cancellationToken);")
			.Because("the initializer wrapper runs the internal convention on the instance the handle holds");
	}

	[Test]
	public async Task TwoConventionsReachingOneMethod_GetOneInitializerWrapper()
	{
		const string library = """
			using System.Threading;
			using System.Threading.Tasks;
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal interface IStartable { Task StartAsync(CancellationToken cancellationToken); }
			internal interface IWarmable { Task StartAsync(CancellationToken cancellationToken); }
			internal sealed class BeanStore : IStartable, IWarmable { public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask; }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
			[AsyncInitializer<IWarmable>(nameof(IWarmable.StartAsync))]
			[Singleton<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""";
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(library, Consumer.Container);
		string module = ModuleSource(RunLibrary(library));
		string container = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(result.Diagnostics).IsEmpty();
		await That(module.Split("HiddenInitializer_BeanStore_").Length - 1).IsEqualTo(1)
			.Because("both conventions reach the one StartAsync of the internal bean store, which runs once, as in a container of the module's own assembly");
		await That(container.Split("HiddenInitializer_BeanStore_").Length - 1).IsEqualTo(1);
	}

	[Test]
	public async Task FactoryMember_IsWrappedIntoTheHandle()
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
			[Singleton<BeanStore>(Factory = nameof(CreateStore), OnActivated = nameof(Activate))]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule
			{
			    internal static BeanStore CreateStore(Silo silo) => new(silo);
			    internal static void Activate(BeanStore store) { }
			}
			""");
		string module = ModuleSource(result);
		string handle = Instance("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty()
			.Because("the module wraps its internal factory into the handle, so the public registration can take it");
		await That(module).Contains($"public static {handle} {HiddenFactory("global::Lib.BeanStore")}({Handle("global::Lib.Silo")} @silo) => {handle}.Wrap(CreateStore(@silo.Value));")
			.Because("the factory member's result is wrapped in the handle variant that matches the instance at run time");
		await That(module).Contains($"internal static {handle.Substring(handle.LastIndexOf('.') + 1)} Wrap(global::Lib.BeanStore value)")
			.Because("a factory may return a subtype, so the handle picks its disposal variant when it wraps the instance");
		await That(module).Contains($"({handle} @awaiten__instance) => Activate((global::Lib.BeanStore)@awaiten__instance.Value);");
		await That(module).Contains($"public static global::Lib.IGrinder {Factory("global::Lib.Grinder")}({Handle("global::Lib.BeanStore")} @store) => new global::Lib.Grinder(@store.Value);");
	}

	[Test]
	public async Task AsyncFactoryMember_IsAwaitedIntoTheHandle()
	{
		GeneratorResult result = RunLibrary("""
			using System.Threading.Tasks;
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[Singleton<BeanStore>(Factory = nameof(CreateStoreAsync))]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule
			{
			    internal static Task<BeanStore> CreateStoreAsync() => Task.FromResult(new BeanStore());
			}
			""");
		string handle = Instance("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty();
		await That(ModuleSource(result)).Contains($"public static async global::System.Threading.Tasks.Task<{handle}> {HiddenFactory("global::Lib.BeanStore")}() => {handle}.Wrap(await CreateStoreAsync().ConfigureAwait(false));")
			.Because("an async factory member gets an async wrapper, which a consumer awaits like any async factory");
	}

	[Test]
	public async Task InstanceMember_IsWrappedIntoTheHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

			[Module]
			[Singleton<BeanStore>(Instance = nameof(Store))]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule
			{
			    internal static readonly BeanStore Store = new();
			}
			""");
		string handle = Instance("global::Lib.BeanStore");

		await That(result.Diagnostics).IsEmpty();
		await That(ModuleSource(result)).Contains($"public static {handle} {HiddenFactory("global::Lib.BeanStore")} => new {handle}(Store);")
			.Because("a pre-built instance needs no disposal variant, since no container owns it");
	}

	[Test]
	public async Task DecoratorOfAnInternalService_IsBuiltIntoTheHandle()
	{
		GeneratorResult result = RunLibrary("""
			using System;
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal interface IBeanStore { }
			internal sealed class BeanStore : IBeanStore { }
			internal sealed class LoggingStore : IBeanStore, IDisposable { public LoggingStore(IBeanStore inner) { } public void Dispose() { } }
			internal sealed class Grinder : IGrinder { public Grinder(IBeanStore store) { } }

			[Module]
			[Singleton<BeanStore, IBeanStore>]
			[Singleton<Grinder, IGrinder>]
			[Decorate<LoggingStore, IBeanStore>]
			public static partial class CoffeeModule { }
			""");
		string handle = Handle("global::Lib.IBeanStore");
		string decorator = $"Decorator_LoggingStore_{Fnv1a("global::Lib.LoggingStore\0global::Lib.IBeanStore"):x8}";

		await That(result.Diagnostics).IsEmpty();
		await That(ModuleSource(result)).Contains($"public static {handle} {decorator}({handle} @inner) => new {handle}.Disposable(new global::Lib.LoggingStore(@inner.Value));")
			.Because("the decorator takes the inner link as a handle and is wrapped in a handle of its own, in its own disposal variant");
	}

	[Test]
	public async Task DecoratorOfAnInternalServiceWhoseImplementationHasAPublicServiceToo_IsBuiltIntoTheHandle()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IStock { }
			public interface IGrinder { }
			internal interface IBeanStore { }
			internal sealed class BeanStore : IBeanStore, IStock { }
			internal sealed class LoggingStore : IBeanStore { public LoggingStore(IBeanStore inner) { } }
			internal sealed class Grinder : IGrinder { public Grinder(IBeanStore store) { } }

			[Module]
			[Singleton<BeanStore, IBeanStore>]
			[Singleton<BeanStore, IStock>]
			[Singleton<Grinder, IGrinder>]
			[Decorate<LoggingStore, IBeanStore>]
			public static partial class CoffeeModule { }
			""");

		await That(result.Diagnostics).IsEmpty();
		await That(ModuleSource(result)).Contains("Decorator_LoggingStore_")
			.Because("every service is an alias of the instance handle, so decorating the internal service leaves the public one undecorated, as in a container of the module's own assembly");
	}

	[Test]
	public async Task OptionalAndDeferredProperties_AreMirroredOntoTheFactory()
	{
		GeneratorResult result = RunLibrary("""
			using Awaiten;

			namespace Lib;

			public interface IClock { }
			public interface IAudit { }
			public interface IGrinder { }
			internal sealed class Grinder : IGrinder
			{
			    [Inject(Optional = true)] public IClock Clock { get; set; }
			    [Inject(Deferred = true)] public IAudit Audit { get; set; } = null!;
			}

			[Module]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""");
		string module = ModuleSource(result);
		string factory = Factory("global::Lib.Grinder");

		await That(result.Diagnostics).IsEmpty()
			.Because("an optional property becomes an optional factory parameter and a deferred one gets a setter wrapper");
		await That(module).Contains($"public static global::Lib.IGrinder {factory}(global::Lib.IClock? @Clock = null)")
			.And.Contains("if (@Clock is not null)")
			.And.Contains("awaiten__instance.Clock = @Clock;")
			.Because("a consumer passes null when the optional service is missing, and the factory then leaves the property as it is");
		await That(module).Contains($"public static void Deferred_{factory}_Audit(global::Lib.IGrinder @awaiten__instance, global::Lib.IAudit @Audit) => ((global::Lib.Grinder)@awaiten__instance).Audit = @Audit;")
			.Because("a consumer assigns a deferred property after it built and cached the instance, so it can break a cycle");
	}

	[Test]
	public async Task InternalServiceOfAnotherModule_IsTakenThroughThatModulesHandle()
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
		string handle = $"global::Lib.StorageModule.Internals.Hidden_BeanStore_{Fnv1a("global::Lib.BeanStore"):x8}";

		await That(result.Diagnostics).IsEmpty()
			.Because("a module may take the handle another module of its assembly declares");
		await That(result.Sources.Single(source => source.Key.Contains("CoffeeModule")).Value)
			.Contains($"public static global::Lib.IGrinder {Factory("global::Lib.Grinder")}({handle} @store) => new global::Lib.Grinder(@store.Value);");
		await That(result.Sources.Single(source => source.Key.Contains("StorageModule")).Value)
			.Contains($"public static global::Lib.StorageModule.Internals.HiddenInstance_BeanStore_{Fnv1a("global::Lib.BeanStore"):x8} {HiddenFactory("global::Lib.BeanStore")}()")
			.Because("in an assembly with several modules, each module builds every internal service it can, since another module may need it");
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
		internal const string Container = """
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
		public async Task CaptiveDependency_NamesTheInternalService()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal sealed class BeanStore { }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Scoped<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""", Container);

			await That(result.Diagnostics).Contains("*AWT105*'Lib.BeanStore (internal to module Lib.CoffeeModule)'*").AsWildcard()
				.Because("the lifetime checks work through the handle and name the internal service instead of the generated type");
			await That(result.Diagnostics).DoesNotContain("*Hidden_*").AsWildcard();
		}

		[Test]
		public async Task MissingDependency_NamesTheInternalService()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IClock { }
				public interface IGrinder { }
				internal sealed class BeanStore { public BeanStore(IClock clock) { } }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }

				[Module]
				[Singleton<BeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""", Container);

			await That(result.Diagnostics).Contains("*AWT101*'Lib.BeanStore (internal to module Lib.CoffeeModule)'*Lib.IClock*").AsWildcard()
				.Because("the container resolves the internal service's dependencies from its own graph and names the service when one is missing");
			await That(result.Diagnostics).DoesNotContain("*Hidden_*").AsWildcard();
		}

		[Test]
		public async Task InternalServiceOfAnotherModule_IsResolvedFromThatModule()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(StorageAndCoffee, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(StorageModule))]
				[Import(typeof(CoffeeModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).IsEmpty()
				.Because("the storage module registers the handle the coffee module's factory takes");
		}

		private const string StorageAndCoffee = """
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
		                                        """;

		[Test]
		public async Task TwoModulesRegisteringOneInternalImplementation_ShareOneInstance()
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

			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(result.Diagnostics).IsEmpty()
				.Because("both registrations coalesce into one, as in a container of the modules' own assembly");
			await That(source).Contains($"global::Lib.CoffeeModule.Internals.{HiddenFactory("global::Lib.BeanStore")}()")
				.And.DoesNotContain($"global::Lib.RoastingModule.Internals.{HiddenFactory("global::Lib.BeanStore")}()")
				.Because("the module first by name declares the handle and builds the one shared instance");
		}

		[Test]
		public async Task FactoryAndInstanceMembers_AreRegisteredThroughTheirHandleWrappers()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using System.Threading.Tasks;
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				public interface IStock { }
				internal interface IClock { }
				internal sealed class Clock : IClock { }
				internal sealed class BeanStore : IStock { }
				internal sealed class Grinder : IGrinder { public Grinder(BeanStore store, IClock clock) { } }

				[Module]
				[Singleton<BeanStore>(Factory = nameof(CreateStoreAsync))]
				[Singleton<BeanStore, IStock>(Factory = nameof(CreateStoreAsync))]
				[Singleton<IClock>(Instance = nameof(Clock))]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule
				{
				    internal static readonly IClock Clock = new Clock();
				    internal static Task<BeanStore> CreateStoreAsync() => Task.FromResult(new BeanStore());
				}
				""", Container);
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(result.Diagnostics).IsEmpty();
			await That(source).Contains($"global::Lib.CoffeeModule.Internals.{HiddenFactory("global::Lib.BeanStore")}()")
				.And.Contains($"global::Lib.CoffeeModule.Internals.{HiddenFactory("global::Lib.IClock")}")
				.And.Contains($"global::Lib.CoffeeModule.Internals.{Alias("global::Lib.BeanStore", "global::Lib.IStock")}(")
				.Because("the container builds each internal service through the module's wrapper and serves the public service from the handle");
		}

		[Test]
		public async Task DecoratorsOfAnInternalService_DecorateItsHandleInOrder()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal interface IBeanStore { }
				internal sealed class BeanStore : IBeanStore { }
				internal sealed class LoggingStore : IBeanStore { public LoggingStore(IBeanStore inner) { } }
				internal sealed class CachingStore : IBeanStore { public CachingStore(IBeanStore inner) { } }
				internal sealed class Grinder : IGrinder { public Grinder(IBeanStore store) { } }

				[Module]
				[Singleton<BeanStore, IBeanStore>]
				[Singleton<Grinder, IGrinder>]
				[Decorate<LoggingStore, IBeanStore>]
				[Decorate<CachingStore, IBeanStore>]
				public static partial class CoffeeModule { }
				""", Container);
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			string handle = Handle("global::Lib.IBeanStore");
			string logging = $"Decorator_LoggingStore_{Fnv1a("global::Lib.LoggingStore\0global::Lib.IBeanStore"):x8}";
			string caching = $"Decorator_CachingStore_{Fnv1a("global::Lib.CachingStore\0global::Lib.IBeanStore"):x8}";

			await That(result.Diagnostics).IsEmpty();
			await That(source).Contains($"global::Lib.CoffeeModule.Internals.{logging}(")
				.And.Contains($"global::Lib.CoffeeModule.Internals.{caching}(")
				.Because("the container decorates the handle through the module's decorator factories");
			await That(source).DoesNotContain($"global::Awaiten.IAwaitenResolver<{handle}>")
				.Because("a decorated handle stays a dependency only");
		}

		[Test]
		public async Task DecoratorOfAnInternalServiceAnotherModuleRegisters_DecoratesItsHandle()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal interface IBeanStore { }
				internal sealed class BeanStore : IBeanStore { }
				internal sealed class LoggingStore : IBeanStore, System.IDisposable { public LoggingStore(IBeanStore inner) { } public void Dispose() { } }
				internal sealed class Grinder : IGrinder { public Grinder(IBeanStore store) { } }

				[Module]
				[Singleton<BeanStore, IBeanStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }

				[Module]
				[Decorate<LoggingStore, IBeanStore>]
				public static partial class LoggingModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(CoffeeModule))]
				[Import(typeof(LoggingModule))]
				public static partial class MyContainer;
				""");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			string logging = $"Decorator_LoggingStore_{Fnv1a("global::Lib.LoggingStore\0global::Lib.IBeanStore"):x8}";

			await That(result.Diagnostics).IsEmpty()
				.Because("a container in the modules' own assembly decorates the service another module registers");
			await That(source).Contains($"global::Lib.LoggingModule.Internals.{logging}(")
				.Because("the decorating module builds its decorator into the handle the registering module declares");
		}

		[Test]
		public async Task CompositeOfAnInternalService_ReportsAwt230()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using System.Collections.Generic;
				using Awaiten;

				namespace Lib;

				public interface IGrinder { }
				internal interface IBeanStore { }
				internal sealed class BeanStore : IBeanStore { }
				internal sealed class AllStores : IBeanStore { public AllStores(IEnumerable<IBeanStore> stores) { } }
				internal sealed class Grinder : IGrinder { public Grinder(IBeanStore store) { } }

				[Module]
				[Singleton<BeanStore, IBeanStore>]
				[Singleton<Grinder, IGrinder>]
				[Composite<AllStores, IBeanStore>]
				public static partial class CoffeeModule { }
				""", Container);

			await That(result.Diagnostics).Contains("*AWT230*AllStores*").AsWildcard()
				.Because("the module builds no composite into a handle, so a container in another assembly cannot construct it");
		}

		[Test]
		public async Task OptionalAndDeferredProperties_AreResolvedThroughTheFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IClock { }
				public interface IAudit { }
				public interface IGrinder { }
				internal sealed class Grinder : IGrinder
				{
				    [Inject(Optional = true)] public IClock Clock { get; set; }
				    [Inject(Deferred = true)] public IAudit Audit { get; set; } = null!;
				}

				[Module]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				public sealed class Audit : IAudit { public Audit(IGrinder grinder) { } }

				[Container]
				[Import(typeof(CoffeeModule))]
				[Transient<Audit, IAudit>]
				public static partial class MyContainer;
				""");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			string factory = Factory("global::Lib.Grinder");

			await That(result.Diagnostics).IsEmpty()
				.Because("the missing optional clock is no missing dependency, and the deferred audit breaks the cycle with the grinder");
			await That(source).Contains($"global::Lib.CoffeeModule.Internals.{factory}(default!)")
				.Because("the container passes null for an optional service it does not register");
			await That(source).Contains($"global::Lib.CoffeeModule.Internals.Deferred_{factory}_Audit(")
				.Because("the container assigns the deferred property through the module's setter wrapper");
		}

		[Test]
		public async Task TransientDisposableThroughAnAlias_IsWithheldOnTheRootUnderStrictLifetimeSafety()
		{
			const string library = """
				using Awaiten;

				namespace Lib;

				public interface IPublic { }
				internal interface IHidden { }
				internal sealed class Impl : IPublic, IHidden, System.IDisposable { public void Dispose() { } }

				[Module]
				[Transient<Impl, IPublic>]
				[Transient<Impl, IHidden>]
				public static partial class CoffeeModule { }
				""";
			GeneratorResult same = Generator.Run(library + """

				[Container]
				[Import(typeof(CoffeeModule))]
				public static partial class SameContainer;
				""");
			GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(library, Container);
			string sameSource = same.Sources["Awaiten.Lib.SameContainer.g.cs"];
			string crossSource = cross.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(sameSource).DoesNotContain("global::Awaiten.IAwaitenResolver<global::Lib.IPublic>")
				.And.Contains("'Lib.IPublic' is withheld from by-type resolution on the container root");
			await That(crossSource).DoesNotContain("global::Awaiten.IAwaitenResolver<global::Lib.IPublic>")
				.And.Contains("'Lib.IPublic' is withheld from by-type resolution on the container root")
				.Because("the alias builds a transient disposable through the handle, which accumulates on the root as the implementation does in the module's own assembly");
		}

		public class PublicImplementation
		{
			private const string Library = """
				using Awaiten;

				namespace Lib;

				internal interface IStore { }
				public sealed class Store : IStore { }
				public interface IGrinder { }
				internal sealed class Grinder : IGrinder { public Grinder(IStore store) { } }

				[Module]
				[Singleton<Store, IStore>]
				[Singleton<Grinder, IGrinder>]
				public static partial class CoffeeModule { }
				""";

			private static string Consumer(string registration) => $$"""
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(CoffeeModule))]
				{{registration}}
				public static partial class MyContainer;
				""";

			[Test]
			public async Task RegisteredByTheConsumerToo_SharesTheInstanceOfTheHandle()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("[Singleton<Store>]"));
				string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

				await That(result.Diagnostics).IsEmpty();
				await That(source).DoesNotContain("new global::Lib.Store(")
					.Because("the container's registration serves the one instance the handle holds, as a container in the module's own assembly coalesces both");
				await That(source).Contains($"global::Lib.CoffeeModule.Internals.{Alias("global::Lib.Store", "global::Lib.Store")}(");
			}

			[Test]
			public async Task RegisteredByAModuleOfTheConsumer_SharesTheInstanceOfTheHandle()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("[Import(typeof(StoreModule))]") + """

					[Module]
					[Singleton<Store>]
					public static class StoreModule { }
					""");
				string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

				await That(result.Diagnostics).IsEmpty();
				await That(source).DoesNotContain("new global::Lib.Store(")
					.Because("a module of another assembly that registers the public implementation shares the handle's instance too");
			}

			[Test]
			public async Task WithALifetimeOfItsOwn_ReportsAwt107()
			{
				GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("[Transient<Store>]"));

				await That(result.Diagnostics).Contains("*AWT107*'Lib.Store'*").AsWildcard()
					.Because("a container in the module's own assembly reports the conflicting lifetime of the one implementation");
			}
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

	public class Conflicts
	{
		private static string Library(string registrations) => $$"""
			using Awaiten;

			namespace Lib;

			public interface IPublic { }
			public interface IUser { }
			internal interface IInternal { }
			internal sealed class Impl : IInternal, IPublic { }
			internal sealed class User : IUser { public User(IInternal value) { } }

			[Module]
			{{registrations}}
			[Singleton<User, IUser>]
			public static partial class CoffeeModule
			{
			    internal static Impl Create() => new();
			    internal static void Activate(Impl impl) { }
			}
			""";

		[Test]
		public async Task LifetimeConflict_ReportsAwt107InTheLibrary()
		{
			GeneratorResult result = RunLibrary(Library("[Singleton<Impl, IInternal>]\n[Transient<Impl, IPublic>]"));

			await That(result.Diagnostics).Contains("*AWT107*'Lib.Impl'*Singleton*Transient*").AsWildcard()
				.Because("a container in the module's own assembly reports the conflict, while one in another assembly serves every service from one instance handle");
		}

		[Test]
		public async Task ProductionConflict_ReportsAwt111InTheLibrary()
		{
			GeneratorResult result = RunLibrary(Library("[Singleton<Impl, IInternal>]\n[Singleton<Impl, IPublic>(Factory = nameof(Create))]"));

			await That(result.Diagnostics).Contains("*AWT111*'Lib.Impl'*a constructor*factory 'Lib.CoffeeModule.Create'*").AsWildcard()
				.Because("the handle builds the implementation one way, so a second production would silently go its own way in another assembly");
		}

		[Test]
		[Arguments("[Singleton<Impl, IInternal>]\n[Singleton<Impl, IPublic>(SuppressDisposal = true)]", "SuppressDisposal")]
		[Arguments("[Singleton<Impl, IInternal>]\n[Singleton<Impl, IPublic>(Eager = true)]", "Eager")]
		[Arguments("[Singleton<Impl, IInternal>]\n[Singleton<Impl, IPublic>(OnActivated = nameof(Activate))]", "OnActivated")]
		public async Task DirectiveOnALaterRegistration_ReportsAwt166InTheLibrary(string registrations, string directive)
		{
			GeneratorResult result = RunLibrary(Library(registrations));

			await That(result.Diagnostics).Contains($"*AWT166*'Lib.Impl'*{directive}*").AsWildcard()
				.Because("the instance handle takes the directives of the first registration, as a container in the module's own assembly does, which reports the later one");
		}

		[Test]
		public async Task ConflictWithAContainerInTheLibrary_IsReportedOnce()
		{
			GeneratorResult result = RunLibrary(Library("[Singleton<Impl, IInternal>]\n[Transient<Impl, IPublic>]") + """

				[Container]
				[Import(typeof(CoffeeModule))]
				public static partial class LibContainer;
				""");

			await That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("AWT107"))).IsEqualTo(1)
				.Because("the container of the library reports the conflict at the same place, so the module's build leaves it to the container");
		}

		[Test]
		public async Task ConsistentRegistrations_DoNotReportAConflict()
		{
			GeneratorResult result = RunLibrary(Library("[Singleton<Impl, IInternal>(SuppressDisposal = true, OnActivated = nameof(Activate))]\n[Singleton<Impl, IPublic>]"));

			await That(result.Diagnostics).IsEmpty()
				.Because("a later registration that leaves a directive unset merges with the first one");
		}

		[Test]
		public async Task LifetimeConflictAcrossModules_ReportsAwt107InTheConsumer()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IFront { }
				public interface IBack { }
				internal interface IInternal { }
				internal sealed class Impl : IInternal, IFront, IBack { }

				[Module]
				[Singleton<Impl, IInternal>]
				[Singleton<Impl, IFront>]
				public static partial class CoffeeModule { }

				[Module]
				[Transient<Impl, IBack>]
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

			await That(result.Diagnostics).Contains("*AWT107*'Lib.Impl'*Singleton*Transient*").AsWildcard()
				.Because("the modules build the implementation into one instance handle, whose registrations the container checks like any other");
		}

		public class OverridableDefaults
		{
			private const string Library = """
				using Awaiten;

				namespace Lib;

				public interface IClock { }
				internal interface ITick { }
				internal sealed class Clock : IClock, ITick { }

				[Module]
				[Singleton<Clock, IClock>(Eager = true, Fallback = Fallback.Silent)]
				[Singleton<Clock, ITick>]
				public static partial class ClockModule { }
				""";

			private const string Container = """
				[Container]
				[Import(typeof(ClockModule))]
				public static partial class MyContainer;
				""";

			[Test]
			public async Task DirectiveOfASurvivingDefault_ReportsAwt166LikeTheModulesOwnAssembly()
			{
				GeneratorResult same = Generator.Run(Library + Container);
				GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(Library, "using Awaiten;\nusing Lib;\n\nnamespace MyCode;\n\n" + Container);

				await That(same.Diagnostics).Contains("*AWT166*'Lib.Clock'*Eager*").AsWildcard();
				await That(cross.Diagnostics).Contains("*AWT166*'Lib.Clock'*Eager*").AsWildcard()
					.Because("the strong registration comes first and keeps the clock lazy, so the surviving eager default contradicts it, as in the module's own assembly");
			}

			[Test]
			public async Task DirectiveOfAnOverriddenDefault_IsDropped()
			{
				GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(Library, """
					using Awaiten;
					using Lib;

					namespace MyCode;

					public sealed class MyClock : IClock { }

					[Container]
					[Import(typeof(ClockModule))]
					[Singleton<MyClock, IClock>]
					public static partial class MyContainer;
					""");

				await That(cross.Diagnostics).IsEmpty()
					.Because("the container overrides the eager default, which then contributes nothing, as in the module's own assembly");
			}
		}

		public class EveryRegistrationOverridden
		{
			private static string Library(string conventions = "") => $$"""
				using Awaiten;
				using System.Threading.Tasks;

				namespace Lib;

				public interface IA { }
				public interface IB { }
				public sealed class Dep { }
				internal interface IStartable { Task StartAsync(); }
				internal sealed class Impl : IA, IB, IStartable { public Impl(Dep dep) { } public Task StartAsync() => Task.CompletedTask; }

				[Module]
				{{conventions}}
				[Singleton<Impl, IA>(Fallback = Fallback.Silent)]
				[Singleton<Impl, IB>(Fallback = Fallback.Silent)]
				public static partial class AModule { }
				""";

			private const string Container = """
				public sealed class MyA : IA { }
				public sealed class MyB : IB { }

				[Container]
				[Import(typeof(AModule))]
				[Singleton<MyA, IA>]
				[Singleton<MyB, IB>]
				public static partial class MyContainer;
				""";

			[Test]
			public async Task DependencyOfTheImplementation_IsNotRequired()
			{
				GeneratorResult same = Generator.Run(Library() + Container);
				GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(Library(), "using Awaiten;\nusing Lib;\n\nnamespace MyCode;\n\n" + Container);

				await That(same.Diagnostics).IsEmpty();
				await That(cross.Diagnostics).IsEmpty()
					.Because("the container overrides every registration of the implementation, so, as in the module's own assembly, nothing builds it");
			}

			[Test]
			public async Task AsyncInitializedImplementation_IsNotBuiltAtStartup()
			{
				string library = Library("[AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]");
				GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(library, "using Awaiten;\nusing Lib;\n\nnamespace MyCode;\n\npublic sealed class MyDep { }\n" + Container.Replace("[Singleton<MyA, IA>]", "[Singleton<MyA, IA>]\n[Singleton<Dep>]"));

				await That(cross.Diagnostics).IsEmpty();
				await That(cross.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("HiddenRegistration_Impl")
					.Because("the handle of an implementation whose every registration is overridden is dropped with its initializer");
			}
		}

		public class OverridableDefaultsOfAModule
		{
			private const string Library = """
				using Awaiten;

				namespace Lib;

				public interface IA { }
				public interface IB { }
				internal interface IStore { }
				internal sealed class Store : IA, IB, IStore { }

				[Module]
				[Transient<Store, IA>(Fallback = Fallback.Silent)]
				[Transient<Store, IB>(Fallback = Fallback.Silent)]
				public static partial class AModule { }

				[Module]
				[Singleton<Store, IStore>]
				public static partial class BModule { }
				""";

			private static string Container(params string[] modules) => $$"""
				[Container]
				{{string.Join("\n", modules.Select(module => $"[Import(typeof({module}))]"))}}
				public static partial class MyContainer;
				""";

			[Test]
			[Arguments("AModule", "BModule")]
			[Arguments("BModule", "AModule")]
			public async Task LifetimeConflictWithAnotherModule_ReportsAwt107LikeTheModulesOwnAssembly(string first, string second)
			{
				GeneratorResult same = Generator.Run(Library + Container(first, second));
				GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(Library, "using Awaiten;\nusing Lib;\n\nnamespace MyCode;\n\n" + Container(first, second));

				await That(same.Diagnostics).Contains("*AWT107*'Lib.Store'*Singleton*Transient*").AsWildcard();
				await That(cross.Diagnostics).Contains("*AWT107*'Lib.Store'*Singleton*Transient*").AsWildcard()
					.Because("nothing contests the services of the overridable defaults, so they survive and contradict the singleton of the other module, as in the module's own assembly");
			}

			[Test]
			public async Task LifetimeConflictWithinTheModule_ReportsAwt107LikeTheModulesOwnAssembly()
			{
				const string library = """
					using Awaiten;

					namespace Lib;

					public interface IA { }
					internal interface IStore { }
					internal sealed class Store : IA, IStore { }

					[Module]
					[Transient<Store, IA>(Fallback = Fallback.Silent)]
					[Singleton<Store, IStore>]
					public static partial class AModule { }
					""";
				GeneratorResult same = Generator.Run(library + Container("AModule"));
				GeneratorResult cross = Generator.RunWithGeneratedReferencedAssembly(library, "using Awaiten;\nusing Lib;\n\nnamespace MyCode;\n\n" + Container("AModule"));

				await That(Generator.Run(library).Diagnostics).IsEmpty()
					.Because("only the importing container knows whether the overridable default survives");
				await That(same.Diagnostics).Contains("*AWT107*'Lib.Store'*Singleton*Transient*").AsWildcard();
				await That(cross.Diagnostics).Contains("*AWT107*'Lib.Store'*Singleton*Transient*").AsWildcard()
					.Because("the surviving default contradicts the lifetime of the strong registration, which the handle takes, as in the module's own assembly");
			}
		}

		private const string DirectiveModules = """
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			internal sealed class Store : System.IDisposable { public void Dispose() { } }
			internal sealed class Grinder : IGrinder { public Grinder(Store store) { } }

			[Module]
			[Singleton<Store>]
			public static partial class AModule { }

			[Module]
			[Singleton<Store>(OnActivated = nameof(Activate), SuppressDisposal = true)]
			[Singleton<Grinder, IGrinder>]
			public static partial class BModule
			{
			    internal static void Activate(Store store) { }
			}
			""";

		[Test]
		public async Task DirectivesOfALaterImport_ReportAwt166InTheConsumer()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(DirectiveModules, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(AModule))]
				[Import(typeof(BModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).Contains("*AWT166*'Lib.Store'*OnActivated*").AsWildcard()
				.And.Contains("*AWT166*'Lib.Store'*SuppressDisposal*").AsWildcard()
				.Because("the first import wins and the later one sets directives it does not, as in a container of the modules' own assembly");
			await That(result.Diagnostics).Contains("*AWT166*'Lib.BModule.Activate'*").AsWildcard()
				.And.DoesNotContain("*Hook_*").AsWildcard()
				.Because("the message names the hook the module declares, not its generated wrapper");
		}

		[Test]
		public async Task HookOfASelfCompiledRegistrationOfALaterImport_IsNamedInAwt166()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				public interface IFront { }
				public interface IBack { }
				internal sealed class Impl : IFront, IBack { }

				[Module]
				[Singleton<Impl, IFront>]
				public static partial class AModule { }

				[Module]
				[Singleton<Impl, IBack>(OnActivated = nameof(Activate))]
				public static partial class BModule
				{
				    internal static void Activate(Impl impl) { }
				}
				""", """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(AModule))]
				[Import(typeof(BModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).Contains("*AWT166*'Lib.Impl'*OnActivated*'Lib.BModule.Activate'*").AsWildcard()
				.And.DoesNotContain("*Hook_*").AsWildcard()
				.Because("the message names the hook the module declares, not its generated wrapper");
		}

		[Test]
		public async Task ProductionConflictAcrossModules_ReportsAwt111InTheConsumer()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(DirectiveModules.Replace("[Singleton<Store>(OnActivated = nameof(Activate), SuppressDisposal = true)]", "[Singleton<Store>(Factory = nameof(Create))]")
				.Replace("internal static void Activate(Store store) { }", "internal static Store Create() => new();"), """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(AModule))]
				[Import(typeof(BModule))]
				public static partial class MyContainer;
				""");

			await That(result.Diagnostics).Contains("*AWT111*'Lib.Store'*a constructor*factory 'Lib.BModule.Create'*").AsWildcard()
				.Because("the modules produce the implementation differently, which their generated factories hide");
		}

		[Test]
		public async Task DirectivesOfTheFirstImport_AreKeptWithoutAConflict()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(DirectiveModules, """
				using Awaiten;
				using Lib;

				namespace MyCode;

				[Container]
				[Import(typeof(BModule))]
				[Import(typeof(AModule))]
				public static partial class MyContainer;
				""");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(result.Diagnostics).IsEmpty()
				.Because("a later registration that leaves the directives unset merges with the first one");
			await That(source).Contains("global::Lib.BModule.Internals.HiddenHook_OnActivated_Store_")
				.Because("the hook of the first import runs through its own wrapper");
		}
	}

	public class Diagnostics
	{
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
