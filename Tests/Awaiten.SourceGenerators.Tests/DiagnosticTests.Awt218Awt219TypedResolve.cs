using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt218TypedResolveOfUnprovidedService
	{
		private const string OpenGenericContainer = """
		                                            using Awaiten;
		                                            using System;
		                                            using System.Collections.Generic;
		                                            using System.Threading.Tasks;

		                                            namespace MyCode;

		                                            public interface IGen<T>;
		                                            public sealed class Gen<T> : IGen<T>;
		                                            public interface IUnregistered;
		                                            public sealed class Consumer { public Consumer(IGen<int> numbers) { } }

		                                            [Container]
		                                            [Transient(typeof(Gen<>), typeof(IGen<>))]
		                                            [Transient<Consumer>]
		                                            public static partial class MyContainer;

		                                            """;

		private static Task<string[]> Run(string usage)
			=> Analyzer.Run<AwaitenAnalyzer>(OpenGenericContainer + usage);

		[Test]
		public async Task ReportsATypedResolveOfAClosingTheGraphNeverUses()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<IGen<string>>();
			                                 	}
			                                 }
			                                 """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT218")).ToArray();
			await That(reports).HasCount(1)
				.Because("the open registration is closed only over int, which Consumer injects, so resolving the string closing throws at run time");
			await That(reports[0]).Contains("(22,3)*'MyCode.IGen<string>'*MyCode.MyContainer*such as [Transient<MyCode.Gen<string>, MyCode.IGen<string>>]*").AsWildcard()
				.Because("the warning points at the call, names the service and the container, and names the closed registration that fixes it");
		}

		[Test]
		public async Task ReportsATypedResolveFromAChildScope()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		using MyContainer.Scope scope = root.CreateScope();
			                                 		scope.Resolve<IGen<string>>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Count(d => d.Contains("AWT218"))).IsEqualTo(1)
				.Because("a child scope serves the same registrations as its root");
		}

		[Test]
		public async Task ReportsATypedResolveAsync()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static async Task Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		await root.ResolveAsync<IGen<string>>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Count(d => d.Contains("AWT218"))).IsEqualTo(1)
				.Because("ResolveAsync throws for an unregistered service just like Resolve");
		}

		[Test]
		public async Task ReportsATypedResolveOfAnUnregisteredNonGenericService()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<IUnregistered>();
			                                 	}
			                                 }
			                                 """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT218")).ToArray();
			await That(reports).HasCount(1)
				.Because("a service with no registration at all fails at run time for the same reason the generator can see");
			await That(reports[0]).Contains("*register it on the [Container], or call TryResolve if it is optional").AsWildcard()
				.Because("a service that is no closing of an open generic registration gets the plain remedy");
		}

		[Test]
		public async Task ReportsATypedResolveOfACollectionWithNoMembers()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<IEnumerable<IUnregistered>>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Count(d => d.Contains("AWT218"))).IsEqualTo(1)
				.Because("only an injected collection falls back to an empty one, a by-type resolve of a collection without members throws");
		}

		[Test]
		public async Task DoesNotReportAClosingTheGraphUses()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<IGen<int>>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("Consumer injects IGen<int>, so the container generates that closing");
		}

		[Test]
		public async Task DoesNotReportAnExplicitlyRegisteredClosing()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                          using Awaiten;

			                                                          namespace MyCode;

			                                                          public interface IGen<T>;
			                                                          public sealed class Gen<T> : IGen<T>;

			                                                          [Container]
			                                                          [Transient(typeof(Gen<>), typeof(IGen<>))]
			                                                          [Transient<Gen<string>, IGen<string>>]
			                                                          public static partial class MyContainer;

			                                                          public static class Usage
			                                                          {
			                                                          	public static void Use()
			                                                          	{
			                                                          		using MyContainer.Root root = new();
			                                                          		root.Resolve<IGen<string>>();
			                                                          	}
			                                                          }
			                                                          """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("registering the closing explicitly next to the open registration is the remedy the warning names");
		}

		[Test]
		public async Task DoesNotReportRelationshipsOverAProvidedService()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<Func<IGen<int>>>();
			                                 		root.Resolve<Lazy<IGen<int>>>();
			                                 		root.Resolve<Owned<IGen<int>>>();
			                                 		root.Resolve<IEnumerable<IGen<int>>>();
			                                 		root.Resolve<IGen<int>[]>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("the container serves every relationship and collection shape over a service it provides");
		}

		[Test]
		public async Task DoesNotReportAnUntypedReceiver()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		IAwaitenRoot root = new MyContainer.Root();
			                                 		root.Resolve<IGen<string>>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("an IAwaitenRoot receiver does not reveal which container answers, so the call stays a run-time check");
		}

		[Test]
		public async Task DoesNotReportAGenericHelper()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static T Get<T>(MyContainer.Root root) => root.Resolve<T>();
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("the type argument is a type parameter, so the service is not known in source");
		}

		[Test]
		public async Task DoesNotReportTryResolve()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.TryResolve<IGen<string>>(out IGen<string>? _);
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("TryResolve reports absence by returning false, which is its purpose rather than a failure");
		}

		[Test]
		public async Task DoesNotReportWhenSuppressedInSource()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 #pragma warning disable AWT218
			                                 		root.Resolve<IGen<string>>();
			                                 #pragma warning restore AWT218
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("the warning is reported by the analyzer at the call, so a deliberate call can be suppressed in source");
		}

		[Test]
		public async Task DoesNotReportAVarianceCompatibleClosing()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                          using Awaiten;

			                                                          namespace MyCode;

			                                                          public class Animal;
			                                                          public sealed class Cat : Animal;
			                                                          public interface IHandler<in T>;
			                                                          public sealed class AnimalHandler : IHandler<Animal>;

			                                                          [Container]
			                                                          [Singleton<AnimalHandler, IHandler<Animal>>]
			                                                          public static partial class MyContainer;

			                                                          public static class Usage
			                                                          {
			                                                          	public static void Use()
			                                                          	{
			                                                          		using MyContainer.Root root = new();
			                                                          		root.Resolve<IHandler<Cat>>();
			                                                          	}
			                                                          }
			                                                          """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("the container routes a contravariant request to the registered IHandler<Animal> at run time");
		}

		[Test]
		public async Task DoesNotReportAnImportedService()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>("""
			                                                          using Awaiten;

			                                                          namespace MyCode;

			                                                          public interface IClock;
			                                                          public interface ILog<T>;
			                                                          public sealed class Barista { public Barista(IClock clock, ILog<Barista> log) { } }

			                                                          [Container]
			                                                          [ImportService<IClock>]
			                                                          [ImportService(typeof(ILog<>))]
			                                                          [Transient<Barista>]
			                                                          public static partial class MyContainer;

			                                                          public static class Usage
			                                                          {
			                                                          	public static void Use()
			                                                          	{
			                                                          		using MyContainer.Root root = new();
			                                                          		root.Resolve<IClock>();
			                                                          		root.Resolve<ILog<string>>();
			                                                          	}
			                                                          }
			                                                          """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("an imported service comes from the host, which the generator cannot see");
		}

		[Test]
		public async Task DoesNotReportWhenTheContainerImportsEveryUnresolvedService()
		{
			string[] diagnostics = await Run("""
			                                 [Container]
			                                 [ImportServices]
			                                 public static partial class HostedContainer;

			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using HostedContainer.Root root = new();
			                                 		root.Resolve<IUnregistered>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("[ImportServices] leaves every unregistered service to the host");
		}

		[Test]
		public async Task DoesNotReportAContainerFromAnotherAssembly()
		{
			string[] diagnostics = await Analyzer.RunWithReferencedAssembly<AwaitenAnalyzer>(OpenGenericContainer, """
				namespace Consuming;

				public static class Usage
				{
					public static void Use()
					{
						using MyCode.MyContainer.Root root = new();
						root.Resolve<MyCode.IGen<string>>();
					}
				}
				""");

			await That(diagnostics.Any(d => d.Contains("AWT218"))).IsFalse()
				.Because("the graph of a container from a referenced assembly cannot be rebuilt faithfully in the consuming compilation");
		}
	}

	public class Awt219TypedResolveOfWithheldService
	{
		private const string Services = """
		                                using Awaiten;
		                                using System;
		                                using System.Threading;
		                                using System.Threading.Tasks;

		                                namespace MyCode;

		                                public sealed class Database : IAsyncInitializable
		                                {
		                                	public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                }

		                                public sealed class Lease : IDisposable { public void Dispose() { } }

		                                """;

		private const string StrictContainer = Services + """
		                                                  [Container]
		                                                  [Singleton<Database>]
		                                                  [Transient<Lease>]
		                                                  public static partial class MyContainer;

		                                                  """;

		private static Task<string[]> Run(string usage)
			=> Analyzer.Run<AwaitenAnalyzer>(StrictContainer + usage);

		[Test]
		public async Task ReportsASynchronousResolveOfAnAsyncInitializedService()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<Database>();
			                                 	}
			                                 }
			                                 """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT219")).ToArray();
			await That(reports).HasCount(1)
				.Because("an async-initialized service has no synchronous resolution in the strict default");
			await That(reports[0]).Contains("*'MyCode.Database' requires asynchronous initialization*ResolveAsync*").AsWildcard()
				.Because("the warning carries the same guidance the run-time exception would");
		}

		[Test]
		public async Task ReportsASynchronousResolveOfAnAsyncInitializedServiceFromAChildScope()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		using MyContainer.Scope scope = root.CreateScope();
			                                 		scope.Resolve<Database>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Count(d => d.Contains("AWT219"))).IsEqualTo(1)
				.Because("no scope resolves an async-initialized service synchronously");
		}

		[Test]
		public async Task DoesNotReportResolveAsyncOfAnAsyncInitializedService()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static async Task Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		await root.ResolveAsync<Database>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT219") || d.Contains("AWT218"))).IsFalse()
				.Because("ResolveAsync awaits the initialization");
		}

		[Test]
		public async Task DoesNotReportASynchronousResolveUnderSyncAfterInit()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(Services + """
			                                                                     [Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
			                                                                     [Singleton<Database>]
			                                                                     public static partial class MyContainer;

			                                                                     public static class Usage
			                                                                     {
			                                                                     	public static void Use()
			                                                                     	{
			                                                                     		using MyContainer.Root root = new();
			                                                                     		root.Resolve<Database>();
			                                                                     	}
			                                                                     }
			                                                                     """);

			await That(diagnostics.Any(d => d.Contains("AWT219"))).IsFalse()
				.Because("AsyncSafety.SyncAfterInit allows a synchronous resolve once the container is warmed");
		}

		[Test]
		public async Task ReportsAResolveOfARootWithheldTransientOnTheRoot()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		root.Resolve<Lease>();
			                                 	}
			                                 }
			                                 """);

			string[] reports = diagnostics.Where(d => d.Contains("AWT219")).ToArray();
			await That(reports).HasCount(1)
				.Because("strict lifetime safety withholds a disposable transient from the Root");
			await That(reports[0]).Contains("*withheld*Owned<MyCode.Lease>*").AsWildcard()
				.Because("the guidance names the leak-free alternatives");
		}

		[Test]
		public async Task DoesNotReportAResolveOfARootWithheldTransientFromAChildScope()
		{
			string[] diagnostics = await Run("""
			                                 public static class Usage
			                                 {
			                                 	public static void Use()
			                                 	{
			                                 		using MyContainer.Root root = new();
			                                 		using MyContainer.Scope scope = root.CreateScope();
			                                 		scope.Resolve<Lease>();
			                                 		root.Resolve<Owned<Lease>>();
			                                 	}
			                                 }
			                                 """);

			await That(diagnostics.Any(d => d.Contains("AWT219") || d.Contains("AWT218"))).IsFalse()
				.Because("a child scope bounds the transient's lifetime, and Owned<T> hands it out with its own disposal");
		}

		[Test]
		public async Task DoesNotReportAResolveOfADisposableTransientUnderLooseLifetimeSafety()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(Services + """
			                                                                     [Container(LifetimeSafety = LifetimeSafety.Loose)]
			                                                                     [Transient<Lease>]
			                                                                     public static partial class MyContainer;

			                                                                     public static class Usage
			                                                                     {
			                                                                     	public static void Use()
			                                                                     	{
			                                                                     		using MyContainer.Root root = new();
			                                                                     		root.Resolve<Lease>();
			                                                                     	}
			                                                                     }
			                                                                     """);

			await That(diagnostics.Any(d => d.Contains("AWT219"))).IsFalse()
				.Because("LifetimeSafety.Loose serves a disposable transient from the Root");
		}
	}
}
