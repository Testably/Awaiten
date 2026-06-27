namespace Awaiten.SourceGenerators.Tests;

public class GeneralTests
{
	[Fact]
	public async Task Generator_OnEmptyContainer_RunsWithoutDiagnostics()
	{
		GeneratorResult result = Generator.Run("""
			using Awaiten;

			namespace MyCode;

			[Container]
			public partial class MyContainer
			{
			}
			""");

		// The generator is currently a no-op skeleton: it must run cleanly and emit nothing yet.
		await That(result.Diagnostics.Length).IsEqualTo(0);
		await That(result.Sources.Count).IsEqualTo(0);
	}
}
