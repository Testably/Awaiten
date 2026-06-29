namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	// A relationship (Func/Lazy/Owned) whose target service is registered but whose implementation fails to
	// build (e.g. an interface registered directly -> AWT103) leaves the service in serviceToImpl but absent
	// from implToIndex. The diagnostic passes that walk the graph must guard that lookup rather than indexing
	// it blindly, or the generator crashes (CS8785 / KeyNotFoundException) and the real registration error is
	// lost.
	public class RelationshipOverFailedImplementation
	{
		[Theory]
		[InlineData("Func")]
		[InlineData("Lazy")]
		public async Task ReportsTheRegistrationError_RatherThanCrashingTheGenerator(string relationship)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;
			                                         using System;

			                                         namespace MyCode;

			                                         public interface IConnection { }

			                                         public sealed class Consumer { public Consumer({{relationship}}<IConnection> connection) { } }

			                                         [Container]
			                                         [Singleton<Consumer>]
			                                         [Singleton<IConnection>]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*AWT103*").AsWildcard()
				.Because("the non-instantiable interface registration is the real error to surface");
			await That(result.Diagnostics).DoesNotContain("*CS8785*").AsWildcard()
				.Because("the generator must not crash on a relationship over a failed implementation");
		}
	}
}
