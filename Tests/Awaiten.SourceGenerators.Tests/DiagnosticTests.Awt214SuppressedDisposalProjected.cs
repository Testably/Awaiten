using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// AWT214 fires where a container is projected into MS.DI (AddAwaitenContainer<TRoot> or
	// new AwaitenServiceProviderFactory<TRoot>) although it suppresses the disposal of a possibly disposable,
	// synchronously projected service. It is reported by AwaitenAnalyzer, which matches the bridge's entry points by
	// name, so these sources declare stand-ins for them instead of referencing the bridge.
	public class Awt214SuppressedDisposalProjected
	{
		private const string Bridge = """
		                              namespace Awaiten.Extensions.DependencyInjection
		                              {
		                              	public static class AwaitenServiceCollectionExtensions
		                              	{
		                              		public static object AddAwaitenContainer<TRoot>(this object services) where TRoot : class, new() => services;
		                              		public static object AddAwaitenContainer<TRoot>(this object services, TRoot root) where TRoot : class => services;
		                              	}

		                              	public sealed class AwaitenServiceProviderFactory<TRoot> where TRoot : class, new();
		                              }

		                              """;

		private static string Container(string registration, string types = "") => Bridge + $$"""
			namespace MyCode
			{
				using System;
				using System.Threading;
				using System.Threading.Tasks;
				using Awaiten;
				using Awaiten.Extensions.DependencyInjection;

				public sealed class Pooled : IDisposable { public void Dispose() { } }
				public sealed class Ticket;
				public sealed class AsyncPooled : IAsyncInitializable, IDisposable
				{
					public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
					public void Dispose() { }
				}
				{{types}}

				{{registration}}
				public static partial class MyContainer;

				public static class Startup
				{
					public static void Configure(object services)
					{
						services.AddAwaitenContainer<MyContainer.Root>();
					}
				}
			}
			""";

		[Test]
		public async Task ReportsForAProjectionOfASuppressedDisposableService()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(
				Container("[Container][Singleton<Pooled>(SuppressDisposal = true)]"));

			await That(diagnostics.Count(d => d.Contains("AWT214"))).IsEqualTo(1)
				.Because("MS.DI disposes every disposable a projected factory returns, so the suppression cannot hold");
			await That(diagnostics.Single(d => d.Contains("AWT214"))).Contains("*MyContainer.Root*Pooled*").AsWildcard();
		}

		[Test]
		public async Task ReportsForAKeyedSuppressedDisposableTransient()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(
				Container("""[Container][Transient<Pooled>(SuppressDisposal = true, Key = "pool")]"""));

			await That(diagnostics.Count(d => d.Contains("AWT214"))).IsEqualTo(1)
				.Because("a keyed registration is projected as a keyed MS.DI descriptor, which MS.DI disposes the same way");
		}

		[Test]
		public async Task ReportsForAFactoryWhoseDeclaredTypeMayHideADisposable()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(Container(
				"[Container][Singleton<IBuffer>(Factory = nameof(Make), SuppressDisposal = true)]",
				"""
				public interface IBuffer;
				internal sealed class Buffer : IBuffer, IDisposable { public void Dispose() { } }
				public static partial class MyContainer { private static IBuffer Make() => new Buffer(); }
				"""));

			await That(diagnostics.Count(d => d.Contains("AWT214"))).IsEqualTo(1)
				.Because("the realized instance behind the interface can be disposable, and MS.DI disposes it at run time");
		}

		[Test]
		public async Task ReportsForTheRootOverloadAndTheHostFactory()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(
				Container("[Container][Singleton<Pooled>(SuppressDisposal = true)]") + """
				namespace MyCode
				{
					using Awaiten.Extensions.DependencyInjection;

					public static class OtherStartup
					{
						public static void Configure(object services)
						{
							services.AddAwaitenContainer(new MyContainer.Root());
							_ = new AwaitenServiceProviderFactory<MyContainer.Root>();
						}
					}
				}
				""");

			await That(diagnostics.Count(d => d.Contains("AWT214"))).IsEqualTo(3)
				.Because("both AddAwaitenContainer overloads and the host factory project the same registrations");
		}

		[Test]
		public async Task ReportsForAnAsyncServiceWhenSyncAfterInitProjectsItSynchronously()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(Container(
				"[Container(AsyncSafety = AsyncSafety.SyncAfterInit)][Singleton<AsyncPooled>(SuppressDisposal = true)]"));

			await That(diagnostics.Count(d => d.Contains("AWT214"))).IsEqualTo(1)
				.Because("under AsyncSafety.SyncAfterInit an async-initialized service is projected as a bare instance, not a Task");
		}

		[Test]
		[Arguments("[Container][Singleton<Ticket>(SuppressDisposal = true)]")]
		[Arguments("[Container][Singleton<AsyncPooled>(SuppressDisposal = true)]")]
		[Arguments("[Container][Singleton<Pooled>]")]
		public async Task DoesNotReportWhenMsDiCannotOverrideTheContainer(string registration)
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(Container(registration));

			await That(diagnostics.Any(d => d.Contains("AWT214"))).IsFalse()
				.Because("a non-disposable instance, a Task-projected async service and an ordinarily disposed service are all safe to project");
		}

		[Test]
		public async Task DoesNotReportForASiteThatDoesNotNameTheRoot()
		{
			string[] diagnostics = await Analyzer.Run<AwaitenAnalyzer>(Bridge + """
				namespace MyCode
				{
					using System;
					using Awaiten;
					using Awaiten.Extensions.DependencyInjection;

					public sealed class Pooled : IDisposable { public void Dispose() { } }

					[Container][Singleton<Pooled>(SuppressDisposal = true)]
					public static partial class MyContainer;

					public static class Startup
					{
						public static void Configure<TRoot>(object services) where TRoot : class, new()
							=> services.AddAwaitenContainer<TRoot>();
					}
				}
				""");

			await That(diagnostics.Any(d => d.Contains("AWT214"))).IsFalse()
				.Because("a generic helper does not reveal the container; the bridge's startup check is the backstop there");
		}
	}
}
