using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt102DependencyCycle
	{
		[Fact]
		public async Task IsBrokenByAFuncDependency()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class A { public A(Func<B> b) { } }
			                                       public sealed class B { public B(A a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT102"))).IsFalse()
				.Because("the Func<B> defers resolution, so the A -> B -> A edge is not a hard cycle");
		}

		[Fact]
		public async Task ReportsWithThePath()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class A { public A(B b) { } }
			                                       public sealed class B { public B(A a) { } }

			                                       [Container]
			                                       [Singleton<A>]
			                                       [Singleton<B>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			string[] cycleDiagnostics = result.Diagnostics.Where(d => d.Contains("AWT102")).ToArray();
			await That(cycleDiagnostics).IsNotEmpty();
			await That(cycleDiagnostics.Any(d => d.Contains("->"))).IsTrue();
		}
	}
}
