using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt106FactoryHidesAsyncInitialization
	{
		[Fact]
		public async Task ReportsWhenTheFactoryConstructsAnAsyncInitializableConcreteType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create() => new FooImpl();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT106*").AsWildcard()
				.Because("the factory provably constructs an IAsyncInitializable type its declared return type hides");

			string diagnostic = result.Diagnostics.Single(d => d.Contains("AWT106"));
			await That(diagnostic).Contains("MyCode.FooImpl")
				.Because("the message names the concrete type the author should return");
			await That(diagnostic).Contains("MyCode.IFoo")
				.Because("the message names the declared return type that hides the capability");
			await That(diagnostic).Contains("async-initialized")
				.Because("the message identifies the hidden capability as asynchronous initialization");
		}

		[Fact]
		public async Task ReportsWhenTheBodyReturnsALocalOfTheConcreteType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create()
			                                           {
			                                               FooImpl foo = new FooImpl();
			                                               return foo;
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT106*").AsWildcard()
				.Because("the returned local's inferred type is the hidden concrete type");
		}

		[Fact]
		public async Task ReportsASingleDiagnostic_WhenSeveralReturnsYieldTheSameHiddenType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create()
			                                           {
			                                               FooImpl foo = new FooImpl();
			                                               if (foo.ToString().Length > 100)
			                                               {
			                                                   return new FooImpl();
			                                               }

			                                               return foo;
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics.Count(d => d.Contains("AWT106"))).IsEqualTo(1)
				.Because("several returns of the same hidden concrete type surface one diagnostic, not one per return");
		}

		[Fact]
		public async Task DoesNotReport_WhenTheDeclaredReturnTypeAlreadyExposesTheCapability()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class FooImpl : IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<FooImpl>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static FooImpl Create() => new FooImpl();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_WhenTheDeclaredInterfaceExtendsIAsyncInitializable()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFooAsync : IAsyncInitializable { }

			                                       public sealed class FooImpl : IFooAsync
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFooAsync>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFooAsync Create() => new FooImpl();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_WhenTheBodyReturnsANonSpecialType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo { }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create() => new FooImpl();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_WhenTheConcreteTypeIsNotStaticallyProvableFromTheBody()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Build() => new FooImpl();
			                                           private static IFoo Create() => Build();
			                                       }
			                                       """);

			// The body returns the value of a helper call typed as IFoo; the concrete FooImpl is not provable
			// without interprocedural analysis, so the lint stays silent.
			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_ForAConstructedRegistrationWithoutAFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public sealed class FooImpl : IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<FooImpl>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			// No factory: the container constructs the type directly and already sees its capability.
			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_WhenANestedLambdaReturnsTheConcreteType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create()
			                                           {
			                                               Func<FooImpl> make = () => new FooImpl();
			                                               IFoo result = make();
			                                               return result;
			                                           }
			                                       }
			                                       """);

			// The 'new FooImpl()' belongs to the nested lambda, not the factory's own return; the factory
			// returns an IFoo-typed local, so nothing is provable.
			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_ForAnAsyncTaskFactory_WhichOwnsItsInitialization()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static async Task<IFoo> Create()
			                                           {
			                                               FooImpl foo = new FooImpl();
			                                               await foo.InitializeAsync(default);
			                                               return foo;
			                                           }
			                                       }
			                                       """);

			// An async Task<T> factory is the explicit manual-initialization escape hatch: the container awaits
			// the factory and does not drive InitializeAsync itself, so the lint must not nag a correct one.
			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_ForAnAsyncValueTaskFactory_WhichOwnsItsInitialization()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static async ValueTask<IFoo> Create()
			                                           {
			                                               FooImpl foo = new FooImpl();
			                                               await foo.InitializeAsync(default);
			                                               return foo;
			                                           }
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task DoesNotReport_ForAHiddenDisposable_SinceTheContainerDisposesFactoryOutputs()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IDisposable
			                                       {
			                                           public void Dispose() { }
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create() => new FooImpl();
			                                       }
			                                       """);

			// A hidden IDisposable is not reported: the container disposes factory outputs behind a runtime
			// `is IDisposable` check (RuntimeDisposalCheck), so there is no leak to warn about.
			await That(result.Diagnostics).DoesNotContain("*AWT106*").AsWildcard();
		}

		[Fact]
		public async Task ReportsAsyncInitialization_WhenTheHiddenTypeIsAlsoDisposable()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;
			                                       using System.Threading;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IFoo { }

			                                       public sealed class FooImpl : IFoo, IAsyncInitializable, IDisposable
			                                       {
			                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			                                           public void Dispose() { }
			                                       }

			                                       [Container]
			                                       [Singleton<IFoo>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                           private static IFoo Create() => new FooImpl();
			                                       }
			                                       """);

			string diagnostic = result.Diagnostics.Single(d => d.Contains("AWT106"));
			await That(diagnostic).Contains("async-initialized")
				.Because("async initialization - not disposal - is the unfixable capability the lint reports");
		}
	}
}
