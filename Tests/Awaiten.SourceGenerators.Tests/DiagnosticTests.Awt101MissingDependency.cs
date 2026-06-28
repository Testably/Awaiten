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

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.And.Contains("*System.Func<System.Func<MyCode.Leaf>>*").AsWildcard()
				.Because("a relationship over another relationship is reported as the unregistered service type it is");
			await That(result.Diagnostics).DoesNotContain("*global::*").AsWildcard()
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

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard();
		}

		[Fact]
		public async Task ReportsWhenAFromKeyParameterHasNoMatchingKeyedRegistration()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class FastClock : IClock { }
			                                       public sealed class Consumer { public Consumer([FromKey("missing")] IClock clock) { } }

			                                       [Container]
			                                       [Singleton<FastClock, IClock>(Key = "fast")]
			                                       [Singleton<Consumer>]
			                                       public partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics.Any(d => d.Contains("AWT101"))).IsTrue()
				.Because("a [FromKey] with no registration under that key is a missing dependency");
			await That(result.Diagnostics.Any(d => d.Contains("AWT101") && d.Contains("key: missing"))).IsTrue()
				.Because("the missing dependency names the requested key");
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

			await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
				.Because("a Func<T> still requires its target T to be registered");
		}
	}
}
