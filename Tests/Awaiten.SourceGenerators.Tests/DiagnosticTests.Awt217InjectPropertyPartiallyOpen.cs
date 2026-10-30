namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt217InjectPropertyPartiallyOpen
	{
		[Test]
		[Arguments("typeof(Outer<int>.Inner<>), \"Clock\"")]
		[Arguments("typeof(Outer<>.Inner<int>), \"Clock\"")]
		[Arguments("propertyName: \"Clock\", implementation: typeof(Outer<int>.Inner<>)")]
		public async Task ReportsAwt217WhenTheTypeofTargetIsPartiallyOpen(string arguments)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public sealed class Clock { }
			                                         public sealed class Outer<T>
			                                         {
			                                             public sealed class Inner<U>
			                                             {
			                                                 public Clock? Clock { get; set; }
			                                             }
			                                         }

			                                         [Container]
			                                         [Singleton<Clock>]
			                                         [InjectProperty({{arguments}})]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).Contains("*error AWT217*").AsWildcard()
				.Because("a partially open target is neither a closed type nor an unbound generic definition, so no closing can match it");
			await That(result.Diagnostics).DoesNotContain("*AWT180*").AsWildcard()
				.Because("the entry is rejected as partially open instead of being reported as never applied");
		}

		[Test]
		[Arguments("typeof(Outer<>.Inner<>)")]
		[Arguments("typeof(Outer<int>.Inner<string>)")]
		[Arguments("typeof(Outer<>.Leaf)")]
		[Arguments("typeof(Outer<int>.Leaf)")]
		public async Task DoesNotReportAwt217ForAClosedTypeOrAnUnboundDefinition(string target)
		{
			GeneratorResult result = Generator.Run($$"""
			                                         using Awaiten;

			                                         namespace MyCode;

			                                         public sealed class Clock { }
			                                         public sealed class Outer<T>
			                                         {
			                                             public sealed class Inner<U>
			                                             {
			                                                 public Clock? Clock { get; set; }
			                                             }

			                                             public sealed class Leaf
			                                             {
			                                                 public Clock? Clock { get; set; }
			                                             }
			                                         }

			                                         [Container]
			                                         [Singleton<Clock>]
			                                         [Singleton<Outer<int>.Inner<string>>]
			                                         [Singleton<Outer<int>.Leaf>]
			                                         [InjectProperty({{target}}, "Clock")]
			                                         public static partial class MyContainer
			                                         {
			                                         }
			                                         """);

			await That(result.Diagnostics).DoesNotContain("*AWT*").AsWildcard()
				.Because("a closed type and a fully unbound definition (including a type nested in one) are valid targets that match a constructed closing");
		}
	}
}
