namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt233InvalidAsyncInitializer
	{
		private static GeneratorResult RunWith(string type, string method = "StartAsync")
			=> Generator.Run($$"""
			                   using Awaiten;
			                   using System.Threading;
			                   using System.Threading.Tasks;

			                   namespace MyCode;

			                   {{type}}
			                   public sealed class Grinder { }

			                   [Container]
			                   [AsyncInitializer<IStartable>("{{method}}")]
			                   [Singleton<Grinder>]
			                   public static partial class MyContainer;
			                   """);

		[Test]
		public async Task ReportsAMissingMethod()
		{
			GeneratorResult result = RunWith("public interface IStartable { Task StartAsync(); }", "Start");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.Start*has no method 'Start'*").AsWildcard();
		}

		[Test]
		public async Task ReportsAnOverloadedMethod()
		{
			GeneratorResult result = RunWith("public interface IStartable { Task StartAsync(); Task StartAsync(CancellationToken cancellationToken); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*overloaded*").AsWildcard()
				.Because("the container cannot choose which overload initializes the service");
		}

		[Test]
		public async Task ReportsAStaticMethod()
		{
			GeneratorResult result = RunWith("public abstract class IStartable { public static Task StartAsync() => Task.CompletedTask; }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*static*").AsWildcard();
		}

		[Test]
		public async Task ReportsAGenericMethod()
		{
			GeneratorResult result = RunWith("public interface IStartable { Task StartAsync<T>(); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*generic*").AsWildcard();
		}

		[Test]
		public async Task ReportsAVoidMethod()
		{
			GeneratorResult result = RunWith("public interface IStartable { void StartAsync(); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*returns 'void'*Task or ValueTask*").AsWildcard();
		}

		[Test]
		public async Task ReportsAMethodReturningTaskOfT()
		{
			GeneratorResult result = RunWith("public interface IStartable { Task<bool> StartAsync(); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*returns 'System.Threading.Tasks.Task<bool>'*").AsWildcard()
				.Because("the container has no use for the result, as with an asynchronous OnActivated hook");
		}

		[Test]
		public async Task ReportsARefParameter()
		{
			GeneratorResult result = RunWith("public interface IStartable { Task StartAsync(ref int attempts); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*'attempts' is passed by reference*").AsWildcard();
		}

		[Test]
		public async Task ReportsAFromKeyParameter()
		{
			GeneratorResult result = RunWith("public sealed class Clock { } public interface IStartable { Task StartAsync([FromKey(\"utc\")] Clock clock); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*'clock' is marked [FromKey]*OnActivated hook*").AsWildcard()
				.Because("a convention keeps Awaiten out of the domain type, so a key belongs on a hook instead");
		}

		[Test]
		public async Task ReportsAMethodTheContainerCannotAccess()
		{
			GeneratorResult result = RunWith("public abstract class IStartable { protected abstract Task StartAsync(); }");

			await That(result.Diagnostics).Contains("*AWT233*IStartable.StartAsync*not accessible from 'MyCode.MyContainer'*").AsWildcard();
		}

		[Test]
		public async Task DoesNotReportAValidConvention()
		{
			GeneratorResult result = RunWith("public interface IStartable { ValueTask StartAsync(CancellationToken cancellationToken); }");

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task ReportsAModuleConventionOnceInTheModuleBuild()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IStartable { Task StartAsync(); }
			                                       public sealed class Grinder { }

			                                       [Module]
			                                       [AsyncInitializer<IStartable>("Start")]
			                                       [Singleton<Grinder>]
			                                       public static class GrinderModule;

			                                       [Container]
			                                       [Import(typeof(GrinderModule))]
			                                       public static partial class FirstContainer;

			                                       [Container]
			                                       [Import(typeof(GrinderModule))]
			                                       public static partial class SecondContainer;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT233*IStartable.Start*").AsWildcard().Exactly(1)
				.Because("the module's own build reports its convention, not every container that imports it");
		}

		[Test]
		public async Task ReportsABaseConventionOnlyOnTheBase()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Threading.Tasks;

			                                       namespace MyCode;

			                                       public interface IStartable { Task StartAsync(); }

			                                       [Container]
			                                       [AsyncInitializer<IStartable>("Start")]
			                                       public static partial class BaseShop;

			                                       [Container(Extends = typeof(BaseShop))]
			                                       public static partial class Shop;
			                                       """);

			await That(result.Diagnostics).Contains("*AWT233*IStartable.Start*").AsWildcard().Exactly(1)
				.Because("the extending container inherits the convention but the base reports it");
		}
	}
}
