using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt117DuplicateKey
	{
		[Fact]
		public async Task ReportsWhenTwoImplementationsShareOneServiceTypeAndKey()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class FastClock : IClock { }
			                                       public sealed class OtherClock : IClock { }

			                                       [Container]
			                                       [Singleton<FastClock, IClock>(Key = "fast")]
			                                       [Singleton<OtherClock, IClock>(Key = "fast")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsTrue()
				.Because("two different implementations claim the same service type and key");
		}

		[Fact]
		public async Task DoesNotReportForTheSameServiceTypeUnderDifferentKeys()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class FastClock : IClock { }
			                                       public sealed class SlowClock : IClock { }

			                                       [Container]
			                                       [Singleton<FastClock, IClock>(Key = "fast")]
			                                       [Singleton<SlowClock, IClock>(Key = "slow")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsFalse()
				.Because("several implementations may share one service type under different keys");
		}

		[Fact]
		public async Task DoesNotReportForOneImplementationReRegisteredUnderTheSameKey()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IReader { }
			                                       public interface IWriter { }
			                                       public sealed class Store : IReader, IWriter { }

			                                       [Container]
			                                       [Singleton<Store, IReader>(Key = "main")]
			                                       [Singleton<Store, IWriter>(Key = "main")]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT117"))).IsFalse()
				.Because("registering one implementation under several service types with one key is a coalesce, not a conflict");
		}
	}
}
