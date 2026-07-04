using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt105CaptiveDependency
	{
		[Fact]
		public async Task DoesNotReportWhenAScopedDependsOnScoped()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class ScopedDependency { }
			                                       public sealed class ScopedConsumer { public ScopedConsumer(ScopedDependency dependency) { } }

			                                       [Container]
			                                       [Scoped<ScopedConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT105*").AsWildcard()
				.Because("a scoped service sharing the scope's lifetime does not capture it");
		}

		[Fact]
		public async Task DoesNotReportWhenASingletonDependsOnASingleton()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Dependency { }
			                                       public sealed class Consumer { public Consumer(Dependency dependency) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Singleton<Dependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT105*").AsWildcard()
				.Because("a singleton depending on a same-or-longer lifetime is not captive");
		}

		[Fact]
		public async Task DoesNotReportWhenASingletonDependsOnScopedThroughAFunc()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class ScopedDependency { }
			                                       public sealed class SingletonConsumer { public SingletonConsumer(Func<ScopedDependency> dependency) { } }

			                                       [Container]
			                                       [Singleton<SingletonConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT105*").AsWildcard()
				.Because("a deferred Func<T> does not capture the scoped instance for the singleton's lifetime");
		}

		[Fact]
		public async Task DoesNotReportWhenATransientDependsOnScoped()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class ScopedDependency { }
			                                       public sealed class TransientConsumer { public TransientConsumer(ScopedDependency dependency) { } }

			                                       [Container]
			                                       [Transient<TransientConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT105*").AsWildcard()
				.Because("only a singleton outlives a scope and so captures it");
		}

		[Fact]
		public async Task NamesTheServiceAliasTheConstructorReferenced()
		{
			// Store is exposed as both IReader (first) and IWriter, but the singleton depends on IWriter, so
			// the diagnostic must name IWriter rather than the implementation's first service type.
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public interface IWriter { }
			                                       public sealed class Store : IReader, IWriter { }
			                                       public sealed class Consumer { public Consumer(IWriter writer) { } }

			                                       [Container]
			                                       [Singleton<Consumer>]
			                                       [Scoped<Store, IReader>]
			                                       [Scoped<Store, IWriter>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			string captive = result.Diagnostics.Single(d => d.Contains("AWT105"));
			await That(captive).Contains("MyCode.IWriter")
				.Because("the diagnostic names the service alias the constructor referenced");
			await That(captive).DoesNotContain("MyCode.IReader")
				.Because("not an arbitrary other service the implementation is registered as");
		}

		[Fact]
		public async Task ReportsWhenASingletonDependsOnScoped()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class ScopedDependency { }
			                                       public sealed class SingletonConsumer { public SingletonConsumer(ScopedDependency dependency) { } }

			                                       [Container]
			                                       [Singleton<SingletonConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT105*").AsWildcard();
		}

		[Fact]
		public async Task ReportsWhenASingletonDependsOnScopedThroughATransient()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class ScopedDependency { }
			                                       public sealed class TransientMiddle { public TransientMiddle(ScopedDependency dependency) { } }
			                                       public sealed class SingletonConsumer { public SingletonConsumer(TransientMiddle middle) { } }

			                                       [Container]
			                                       [Singleton<SingletonConsumer>]
			                                       [Transient<TransientMiddle>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT105*").AsWildcard();
		}

		[Fact]
		public async Task ReportsWhenASingletonCapturesScopedThroughADeferredProperty()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A deferred property is assigned after construction, but the reference it stores still lives for
			                                       // the singleton's whole lifetime, so it captures the scoped instance exactly like a constructor edge.
			                                       public sealed class ScopedDependency { }
			                                       public sealed class SingletonConsumer { [Inject(Deferred = true)] public ScopedDependency Dependency { get; set; } }

			                                       [Container]
			                                       [Singleton<SingletonConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT105*").AsWildcard()
				.Because("deferring the assignment does not shorten how long the singleton holds the scoped instance");
		}

		[Fact]
		public async Task DoesNotReportWhenASingletonHoldsScopedThroughADeferredFunc()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System;
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       // A deferred Func<T> member launders the capture exactly like a constructor Func<T>: it stores a
			                                       // factory, not the scoped instance, so nothing is captured for the singleton's lifetime.
			                                       public sealed class ScopedDependency { }
			                                       public sealed class SingletonConsumer { [Inject(Deferred = true)] public Func<ScopedDependency> Dependency { get; set; } }

			                                       [Container]
			                                       [Singleton<SingletonConsumer>]
			                                       [Scoped<ScopedDependency>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT105*").AsWildcard()
				.Because("a deferred Func<T> stores a factory rather than the scoped instance, so it does not capture it");
		}

		[Fact]
		public async Task ReportsWhenASingletonCapturesAScopedKeyedDictionaryMember()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IWork { }
			                                       public sealed class MainWork : IWork { }
			                                       public sealed class Host { public Host(IReadOnlyDictionary<string, IWork> work) { } }

			                                       [Container]
			                                       [Scoped<MainWork, IWork>(Key = "main")]
			                                       [Singleton<Host>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT105*").AsWildcard()
				.Because("a keyed dictionary captures its members eagerly, so a singleton holding one over a scoped member makes that member captive");
		}
	}
}
