namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt107ConflictingLifetime
	{
		[Fact]
		public async Task DoesNotReportForTheSameLifetimeAcrossServices()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public interface IWriter { }
			                                       public sealed class Store : IReader, IWriter { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Singleton<Store, IWriter>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT107*").AsWildcard()
				.Because("registering one implementation under several services with the same lifetime is valid");
		}

		[Fact]
		public async Task ReportsAcrossDifferentServiceTypes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public interface IWriter { }
			                                       public sealed class Store : IReader, IWriter { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Scoped<Store, IWriter>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT107*").AsWildcard();
		}

		[Fact]
		public async Task ReportsForAWinningDefaultThatContradictsWhatAStrongRegistrationFixed()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ICacheA { }
			                                       public interface ICacheB { }
			                                       public sealed class Cache : ICacheA, ICacheB { }

			                                       [Module]
			                                       [Transient<Cache, ICacheB>(Fallback = Fallback.Silent)]
			                                       public static class CacheModule { }

			                                       [Container]
			                                       [Singleton<Cache, ICacheA>]
			                                       [Import(typeof(CacheModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT107*").AsWildcard()
				.Because("the Fallback.Silent keeps its service key, so its declared Transient lifetime would be silently replaced by the Singleton the strong registration fixed - a contradiction, not a transparent override");
		}

		[Fact]
		public async Task ReportsForTwoDefaultsRegisteringTheSameImplementationWithDifferentLifetimes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SharedClock : IClock { }

			                                       [Module]
			                                       [Singleton<SharedClock, IClock>(Fallback = Fallback.Warn)]
			                                       public static class ModuleA { }

			                                       [Module]
			                                       [Transient<SharedClock, IClock>(Fallback = Fallback.Warn)]
			                                       public static class ModuleB { }

			                                       [Container]
			                                       [Import(typeof(ModuleA))]
			                                       [Import(typeof(ModuleB))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT107*").AsWildcard()
				.Because("the two defaults contradict each other's lifetime with nothing stronger to resolve them, and the same implementation rules out AWT148 - without AWT107 the loser's declared lifetime would be discarded silently");
		}

		[Fact]
		public async Task DoesNotReportForADefaultOverriddenByAStrongRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface ICache { }
			                                       public sealed class Cache : ICache { }

			                                       [Module]
			                                       [Transient<Cache, ICache>(Fallback = Fallback.Warn)]
			                                       public static class CacheModule { }

			                                       [Container]
			                                       [Singleton<Cache, ICache>]
			                                       [Import(typeof(CacheModule))]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a default losing its service key to a strong registration is replaced transparently - even by the same implementation with a different lifetime");
		}

		[Fact]
		public async Task ReportsForTheSameServiceTypeWithDifferentLifetimes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public sealed class Store : IReader { }

			                                       [Container]
			                                       [Singleton<Store, IReader>]
			                                       [Scoped<Store, IReader>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT107*").AsWildcard()
				.Because("re-registering the same service with a different lifetime is still a conflict, not a silent drop");
		}
	}
}
