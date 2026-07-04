using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt135ExternalDecoratorInner
	{
		[Fact]
		public async Task ReportsWhenTheDecoratorsInnerParameterIsMarkedFromServices()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // The only parameter that could receive the decorated inner instance is marked
			                                       // [FromServices], which would silently bypass the chain and resolve it externally.
			                                       public sealed class Deco : IService { public Deco([FromServices] IService inner) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<Deco, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT135*").AsWildcard()
				.And.Contains("*[FromServices]*").AsWildcard()
				.Because("the decorated inner instance must come from the decorator chain, not the external provider");
			await That(result.Diagnostics.Any(d => d.Contains("AWT124"))).IsFalse()
				.Because("the specific [FromServices] conflict replaces the generic missing-inner diagnostic");
		}

		[Fact]
		public async Task DoesNotReportForFromServicesOnANonInnerParameter()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public interface ILogger { }
			                                       public sealed class Real : IService { }
			                                       // The inner is received normally; the unrelated ILogger side-dependency is external.
			                                       public sealed class Deco : IService { public Deco(IService inner, [FromServices] ILogger logger) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<Deco, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a [FromServices] side-dependency of a decorator is legal; only the inner parameter is off-limits");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			await That(source).Contains("(global::MyCode.ILogger)__s.__ResolveExternal(typeof(global::MyCode.ILogger), null)");
		}

		[Fact]
		public async Task FromServicesSiblingOfTheServiceType_DoesNotMakeTheInnerAmbiguous()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real : IService { }
			                                       // Two IService parameters, but one is explicitly [FromServices]: it is a separate,
			                                       // externally-resolved dependency (like a [FromKey]-ed sibling), so the inner is
			                                       // unambiguous rather than AWT124.
			                                       public sealed class Deco : IService { public Deco(IService inner, [FromServices] IService hostService) { } }

			                                       [Container]
			                                       [Transient<Real, IService>]
			                                       [Decorate<Deco, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a [FromServices] sibling of the service type is a separate external dependency, not a second inner candidate");
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];
			// The chain is wired (the decorator wraps the base impl) while the sibling resolves externally.
			await That(source).Contains("(global::MyCode.IService)__s.__ResolveExternal(typeof(global::MyCode.IService), null)");
			await That(source).Contains("new global::MyCode.Real()");
		}
	}
}
