namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A <c>Task&lt;IService&gt;</c> shaped dependency on an implementation registered under a service it implements.
///     <c>Task&lt;T&gt;</c> is not covariant, so the generated code must not hand the implementation's task to a parameter
///     that expects the service's task.
/// </summary>
public class TaskOfServiceConversionTests
{
	private static string Source(string lifetime, string parameter, bool asyncInitialized, string registration = "")
		=> $$"""
		     using Awaiten;
		     using System;
		     using System.Collections.Generic;
		     using System.Threading;
		     using System.Threading.Tasks;

		     namespace MyCode;

		     public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }
		     public interface IStore { }
		     public sealed class Store : IStore{{(asyncInitialized ? ", IInitializable" : "")}}
		     {
		         public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		     }
		     public sealed class Consumer
		     {
		         public Consumer({{parameter}} dependency) { }
		     }

		     [Container]
		     [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
		     [{{lifetime}}<Store, IStore>{{registration}}]
		     [Transient<Consumer>]
		     public static partial class MyContainer
		     {
		     }
		     """;

	[Test]
	[Arguments("Singleton", "Task<IStore>")]
	[Arguments("Scoped", "Task<IStore>")]
	[Arguments("Transient", "Task<IStore>")]
	[Arguments("Singleton", "Func<Task<IStore>>")]
	[Arguments("Scoped", "Func<Task<IStore>>")]
	[Arguments("Transient", "Func<Task<IStore>>")]
	[Arguments("Singleton", "Lazy<Task<IStore>>")]
	[Arguments("Scoped", "Lazy<Task<IStore>>")]
	[Arguments("Transient", "Lazy<Task<IStore>>")]
	[Arguments("Singleton", "Task<Owned<IStore>>")]
	[Arguments("Scoped", "Task<Owned<IStore>>")]
	[Arguments("Transient", "Task<Owned<IStore>>")]
	[Arguments("Singleton", "Func<Task<Owned<IStore>>>")]
	[Arguments("Scoped", "Func<Task<Owned<IStore>>>")]
	[Arguments("Transient", "Func<Task<Owned<IStore>>>")]
	[Arguments("Singleton", "Task<IEnumerable<IStore>>")]
	[Arguments("Transient", "Task<IReadOnlyList<IStore>>")]
	[Arguments("Scoped", "Task<IStore[]>")]
	[Arguments("Singleton", "IAsyncEnumerable<IStore>")]
	public async Task AsyncInitializedImplementation_Compiles(string lifetime, string parameter)
	{
		GeneratorResult result = Generator.Run(Source(lifetime, parameter, asyncInitialized: true));

		await That(result.Diagnostics).IsEmpty()
			.Because("the implementation's task is converted to the service's task instead of being passed as is");
	}

	[Test]
	[Arguments("Singleton", "Task<IStore>")]
	[Arguments("Scoped", "Func<Task<IStore>>")]
	[Arguments("Transient", "Lazy<Task<IStore>>")]
	[Arguments("Singleton", "Func<Task<Owned<IStore>>>")]
	public async Task SynchronousImplementation_Compiles(string lifetime, string parameter)
	{
		GeneratorResult result = Generator.Run(Source(lifetime, parameter, asyncInitialized: false));

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).DoesNotContain("__AsTask")
			.Because("a synchronous implementation is wrapped in a completed task of the service type");
	}

	[Test]
	[Arguments("Singleton", "Task<IStore?>", true)]
	[Arguments("Scoped", "Task<IStore?>", true)]
	[Arguments("Transient", "Task<IStore?>", true)]
	[Arguments("Singleton", "Func<Task<IStore?>>", true)]
	[Arguments("Scoped", "Func<Task<IStore?>>", true)]
	[Arguments("Transient", "Func<Task<IStore?>>", true)]
	[Arguments("Singleton", "Lazy<Task<IStore?>>", true)]
	[Arguments("Scoped", "Lazy<Task<IStore?>>", true)]
	[Arguments("Transient", "Lazy<Task<IStore?>>", true)]
	[Arguments("Singleton", "Task<Owned<IStore?>>", true)]
	[Arguments("Transient", "Func<Task<Owned<IStore?>>>", true)]
	[Arguments("Singleton", "Task<IStore?>", false)]
	[Arguments("Scoped", "Task<IStore?>", false)]
	[Arguments("Transient", "Task<IStore?>", false)]
	[Arguments("Singleton", "Func<Task<IStore?>>", false)]
	[Arguments("Scoped", "Lazy<Task<IStore?>>", false)]
	[Arguments("Transient", "Func<Task<Owned<IStore?>>>", false)]
	[Arguments("Singleton", "Lazy<IStore?>", false)]
	[Arguments("Scoped", "Owned<IStore?>", false)]
	[Arguments("Transient", "Func<Owned<IStore?>>", false)]
	public async Task NullableAnnotatedService_CompilesWithoutWarnings(string lifetime, string parameter, bool asyncInitialized)
	{
		GeneratorResult result = Generator.Run("#nullable enable\n" + Source(lifetime, parameter, asyncInitialized));

		await That(result.Diagnostics).IsEmpty()
			.Because("the generated relationship keeps the nullable annotation of the requested service, so no CS8620 breaks a build with warnings as errors");
	}

	[Test]
	[Arguments("Task<Store?>")]
	[Arguments("Func<Task<Store?>>")]
	[Arguments("Lazy<Task<Store?>>")]
	public async Task NullableAnnotatedImplementationType_CompilesWithoutWarnings(string parameter)
	{
		GeneratorResult result = Generator.Run("#nullable enable\n" + Source("Scoped", parameter, asyncInitialized: true)
			.Replace("[Scoped<Store, IStore>]", "[Scoped<Store>]"));

		await That(result.Diagnostics).IsEmpty()
			.Because("the task of the implementation is adapted to the task of its nullable-annotated type too");
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task NullableAnnotatedServiceOfARequestingTypeFactory_CompilesWithoutWarnings(bool asyncFactory)
	{
		string factory = asyncFactory
			? "private static Task<Store> Create([RequestingType] Type? requestingType) => Task.FromResult(new Store());"
			: "private static Store Create([RequestingType] Type? requestingType) => new Store();";
		GeneratorResult result = Generator.Run($$"""
		                                         #nullable enable
		                                         using Awaiten;
		                                         using System;
		                                         using System.Threading.Tasks;

		                                         namespace MyCode;

		                                         public interface IStore { }
		                                         public sealed class Store : IStore { }
		                                         public sealed class Consumer
		                                         {
		                                             public Consumer(Task<IStore?> task, Func<Task<IStore?>> factory, Lazy<Task<IStore?>> lazy) { }
		                                         }

		                                         [Container]
		                                         [Transient<Store, IStore>(Factory = nameof(Create))]
		                                         [Transient<Consumer>]
		                                         public static partial class MyContainer
		                                         {
		                                             {{factory}}
		                                         }
		                                         """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task SameServiceType_DoesNotConvert()
	{
		GeneratorResult result = Generator.Run(Source("Singleton", "Task<Store>", asyncInitialized: true)
			.Replace("[Singleton<Store, IStore>]", "[Singleton<Store>]"));

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("Root.ResolveStoreAsync(__s.__root, default)").And
			.DoesNotContain("__AsTask<global::MyCode.Store, global::MyCode.Store>(Root.ResolveStoreAsync(__s.__root, default))")
			.Because("the async resolver already returns the task of the requested type");
	}

	[Test]
	public async Task GeneratedCode_AdaptsTheTaskThroughTheHelper()
	{
		GeneratorResult result = Generator.Run(Source("Scoped", "Task<IStore>", asyncInitialized: true));

		await That(result.Diagnostics).IsEmpty();
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"])
			.Contains("__AsTask<global::MyCode.Store, global::MyCode.IStore>(ResolveStoreAsync(__s, default))").And
			.Contains("private protected static global::System.Threading.Tasks.Task<TService> __AsTask<TInstance, TService>(")
			.Because("only the derived Root calls the helper, so it stays out of the public API of a public container");
	}

	[Test]
	public async Task OpenGeneric_Compiles()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }
		                                       public interface IRepository<T> { }
		                                       public sealed class Repository<T> : IRepository<T>, IInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(Task<IRepository<int>> task, Func<Task<IRepository<string>>> factory) { }
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
		                                       [Scoped(typeof(Repository<>), typeof(IRepository<>))]
		                                       [Transient<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task Scanned_Compiles()
	{
		GeneratorResult result = Generator.Run(Source("Singleton", "Lazy<Task<IStore>>", asyncInitialized: true)
			.Replace("[Singleton<Store, IStore>]", "[Scan<IInitializable>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Scoped)]"));

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task Keyed_Compiles()
	{
		GeneratorResult result = Generator.Run(Source("Singleton", "[FromKey(\"main\")] Task<IStore>", asyncInitialized: true,
			"(Key = \"main\")"));

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task AwaitedKeyedCollection_Compiles()
	{
		GeneratorResult result = Generator.Run(Source("Singleton", "Task<IReadOnlyDictionary<string, IStore>>", asyncInitialized: true,
			"(Key = \"main\")"));

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task SyncAfterInit_Compiles()
	{
		GeneratorResult result = Generator.Run(Source("Singleton", "Func<Task<IStore>>", asyncInitialized: true)
			.Replace("[Container]", "[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]"));

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task ParameterizedFuncOfTask_Compiles()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }
		                                       public interface IRobot { }
		                                       public sealed class Robot : IRobot, IInitializable
		                                       {
		                                           public Robot([Arg] int id) { }
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Factory
		                                       {
		                                           public Factory(Func<int, Task<IRobot>> make, Func<int, Task<Owned<IRobot>>> owned) { }
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
		                                       [Transient<Robot, IRobot>]
		                                       [Singleton<Factory>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	[Arguments("Singleton")]
	[Arguments("Transient")]
	public async Task AsyncFactory_Compiles(string lifetime)
	{
		GeneratorResult result = Generator.Run($$"""
		                                         using Awaiten;
		                                         using System;
		                                         using System.Threading.Tasks;

		                                         namespace MyCode;

		                                         public interface IStore { }
		                                         public sealed class Store : IStore { }
		                                         public sealed class Consumer
		                                         {
		                                             public Consumer(Task<IStore> task, Func<Task<IStore>> factory, Lazy<Task<IStore>> lazy) { }
		                                         }

		                                         [Container]
		                                         [{{lifetime}}<Store, IStore>(Factory = nameof(CreateAsync))]
		                                         [Transient<Consumer>]
		                                         public static partial class MyContainer
		                                         {
		                                             private static Task<Store> CreateAsync() => Task.FromResult(new Store());
		                                         }
		                                         """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task RequestingTypeAsyncFactory_Compiles()
	{
		GeneratorResult result = Generator.Run("""
		                                       #nullable enable
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IStore { }
		                                       public sealed class Store : IStore { }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(Task<IStore> task, Func<Task<IStore>> factory, Lazy<Task<IStore>> lazy) { }
		                                       }

		                                       [Container]
		                                       [Transient<Store, IStore>(Factory = nameof(CreateAsync))]
		                                       [Transient<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                           private static Task<Store> CreateAsync([RequestingType] Type? requestingType) => Task.FromResult(new Store());
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task Decorator_Compiles()
	{
		GeneratorResult result = Generator.Run(Source("Singleton", "Task<IStore>", asyncInitialized: true)
			.Replace("[Transient<Consumer>]", "[Transient<Consumer>]\n[Decorate<LoggingStore, IStore>]")
			.Replace("namespace MyCode;", "namespace MyCode;\n\npublic sealed class LoggingStore(IStore inner) : IStore { public IStore Inner => inner; }"));

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task LifecycleHookAndInjectedProperty_Compile()
	{
		GeneratorResult result = Generator.Run("""
		                                       #nullable enable
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }
		                                       public interface IStore { }
		                                       public sealed class Store : IStore, IInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Consumer
		                                       {
		                                           public Func<Task<IStore>>? Store { get; set; }
		                                       }

		                                       [Container]
		                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
		                                       [Singleton<Store, IStore>]
		                                       [Transient<Consumer>(OnActivated = nameof(Activate))]
		                                       [InjectProperty<Consumer>(nameof(Consumer.Store))]
		                                       public static partial class MyContainer
		                                       {
		                                           private static void Activate(Consumer consumer, Lazy<Task<IStore>> store) { }
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	public async Task SameAssemblyModule_Compiles()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }
		                                       public interface IStore { }
		                                       public sealed class Store : IStore, IInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer(Task<IStore> task, Func<Task<IStore>> factory) { }
		                                       }

		                                       [Module]
		                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
		                                       [Scoped<Store, IStore>]
		                                       [Transient<Consumer>]
		                                       public static partial class StoreModule;

		                                       [Container]
		                                       [Import(typeof(StoreModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
	}

	[Test]
	[Arguments("public")]
	[Arguments("internal")]
	public async Task ModuleInAnotherAssembly_Compiles(string implementationAccessibility)
	{
		GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly($$"""
		                                                                       using Awaiten;
		                                                                       using System.Threading;
		                                                                       using System.Threading.Tasks;

		                                                                       namespace ModuleAssembly;

		                                                                       public interface IInitializable { Task InitializeAsync(CancellationToken cancellationToken); }
		                                                                       public interface IStore { }
		                                                                       {{implementationAccessibility}} sealed class Store : IStore, IInitializable
		                                                                       {
		                                                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                                                       }

		                                                                       [Module]
		                                                                       [AsyncInitializer<IInitializable>(nameof(IInitializable.InitializeAsync))]
		                                                                       [Singleton<Store, IStore>]
		                                                                       public static partial class StoreModule;
		                                                                       """, """
		                                                                            using Awaiten;
		                                                                            using ModuleAssembly;
		                                                                            using System;
		                                                                            using System.Threading.Tasks;

		                                                                            namespace MyCode;

		                                                                            public sealed class Consumer
		                                                                            {
		                                                                                public Consumer(Task<IStore> task, Func<Task<IStore>> factory, Lazy<Task<IStore>> lazy) { }
		                                                                            }

		                                                                            [Container]
		                                                                            [Import(typeof(StoreModule))]
		                                                                            [Transient<Consumer>]
		                                                                            public static partial class MyContainer
		                                                                            {
		                                                                            }
		                                                                            """);

		await That(result.Diagnostics).IsEmpty();
	}
}
