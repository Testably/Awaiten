using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt192SuppressDisposalOnInstance
	{
		[Fact]
		public async Task ReportsWhenSuppressDisposalIsSetOnAPreBuiltInstance()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(Shared), SuppressDisposal = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static readonly Service Shared = new();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT192*").AsWildcard()
				.Because("the container never disposes a pre-built instance, so suppressing that disposal is a no-op");
		}

		[Fact]
		public async Task DoesNotReportForAPreBuiltInstanceWithoutSuppressDisposal()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Instance = nameof(Shared))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static readonly Service Shared = new();
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT192*").AsWildcard()
				.Because("a pre-built instance without SuppressDisposal is valid");
		}

		[Fact]
		public async Task DoesNotReportForSuppressDisposalOnAConstructedRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service : System.IDisposable
			                                       {
			                                       	public void Dispose() { }
			                                       }

			                                       [Container]
			                                       [Singleton<Service>(SuppressDisposal = true)]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).DoesNotContain("*AWT192*").AsWildcard()
				.Because("SuppressDisposal is meaningful on a container-constructed instance, which the container would otherwise dispose");
		}
	}
}
