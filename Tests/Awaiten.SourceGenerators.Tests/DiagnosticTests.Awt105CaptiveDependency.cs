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

			await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsFalse()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsFalse()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsFalse()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsFalse()
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
			await That(captive.Contains("MyCode.IWriter")).IsTrue()
				.Because("the diagnostic names the service alias the constructor referenced");
			await That(captive.Contains("MyCode.IReader")).IsFalse()
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsTrue();
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

			await That(result.Diagnostics.Any(d => d.Contains("AWT105"))).IsTrue();
		}
	}
}
