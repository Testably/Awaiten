using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt101MissingDependency
	{
		[Fact]
		public async Task ReportsTheFullTypeForANestedRelationship()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public sealed class Leaf { }
			                                       public sealed class Service { public Service(Func<Func<Leaf>> nested) { } }

			                                       [Container]
			                                       [Transient<Leaf>]
			                                       [Transient<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT101") && d.Contains("System.Func<System.Func<MyCode.Leaf>>"))).IsTrue()
				.Because("a relationship over another relationship is reported as the unregistered service type it is");
			await That(result.Diagnostics.Any(d => d.Contains("global::"))).IsFalse()
				.Because("diagnostics strip the global:: alias, including nested generic arguments");
		}

		[Fact]
		public async Task ReportsWhenAConstructorParameterIsNotRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IMissing { }
			                                       public sealed class Service { public Service(IMissing missing) { } }

			                                       [Container]
			                                       [Transient<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsTrue();
		}

		[Fact]
		public async Task ReportsWhenAFuncTargetIsNotRegistered()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System;

			                                       namespace MyCode;

			                                       public interface IMissing { }
			                                       public sealed class Service { public Service(Func<IMissing> missing) { } }

			                                       [Container]
			                                       [Transient<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsTrue()
				.Because("a Func<T> still requires its target T to be registered");
		}
	}
}
