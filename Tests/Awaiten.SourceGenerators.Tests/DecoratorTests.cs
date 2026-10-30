using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of decorators. Each chain link is a synthetic-keyed instance whose inner parameter
///     resolves the link below it; the public dispatch and every consumer reach the outermost decorator. The
///     collection view is rewritten to the decorated chain, so the decorator is unbypassable.
/// </summary>
public class DecoratorTests
{
	[Test]
	public async Task Decorator_EmitsTheChainedDispatchOutermostFirst()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real : IService { }
		                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }
		                                       public sealed class D2 : IService { public D2(IService inner) { } }
		                                       public sealed class Consumer { public Consumer(IService service) { } }

		                                       [Container]
		                                       [Transient<Real, IService>]
		                                       [Transient<Consumer>]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       [Decorate<D2, IService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The chain is built from synthetic-keyed links: D2(LoggingDecorator(Real)). Each link constructs the one below it.
		await That(source).Contains("new global::MyCode.D2(ResolveLoggingDecorator(__s))")
			.Because("the outermost decorator wraps the next-lower link");
		await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal(__s))")
			.Because("the inner decorator wraps the bare implementation");

		await That(source).Contains("static __s => ResolveD2(__s)")
			.Because("the public IService dispatch resolves the outermost decorator");
		await That(source).Contains("new global::MyCode.Consumer(ResolveD2(__s))")
			.Because("a consumer of IService receives the outermost decorator");

		await That(source).Contains("new global::MyCode.IService[] { ResolveD2(__s) }")
			.Because("the collection view yields the decorated chain, so the decorator is unbypassable");
		await That(source).DoesNotContain("static __s => ResolveReal(__s)")
			.Because("the bare implementation is no longer publicly dispatched, only the decorator chain is");
	}

	[Test]
	public async Task Decorator_WrapsEachRegistrationOfAMultiplyRegisteredService()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IService { }
		                                       public sealed class Real1 : IService { }
		                                       public sealed class Real2 : IService { }
		                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }
		                                       public sealed class Host { public Host(IEnumerable<IService> services) { } }

		                                       [Container]
		                                       [Transient<Real1, IService>]
		                                       [Transient<Real2, IService>]
		                                       [Transient<Host>]
		                                       [Decorate<LoggingDecorator, IService>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A multiply-registered service is decorated member by member: one distinct decorator instance per base impl, so the type is emitted twice.
		int decoratorConstructions = source.Split(new[] { "new global::MyCode.LoggingDecorator(", }, System.StringSplitOptions.None).Length - 1;
		await That(decoratorConstructions).IsEqualTo(2)
			.Because("each of the two registrations gets its own decorator instance");
		await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal1(__s))");
		await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal2(__s))");
	}

	public sealed class KeyedRegistrations
	{
		[Test]
		public async Task UnkeyedDecorator_WrapsUnkeyedCollectionAndContextualRegistrations_ButNotKeyedOnes()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Real1 : IService { }
			                                       public sealed class Real2 : IService { }
			                                       public sealed class Contextual : IService { }
			                                       public sealed class Backup : IService { }
			                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }
			                                       public sealed class Special { public Special(IService service) { } }
			                                       public sealed class Host { public Host(IEnumerable<IService> services) { } }

			                                       [Container]
			                                       [Transient<Real1, IService>]
			                                       [Transient<Real2, IService>]
			                                       [Transient<Contextual, IService>(WhenInjectedInto = typeof(Special))]
			                                       [Transient<Backup, IService>(Key = "backup")]
			                                       [Transient<Special>]
			                                       [Transient<Host>]
			                                       [Decorate<LoggingDecorator, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

			await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal1(__s))")
				.Because("the unkeyed registration is decorated");
			await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveReal2(__s))")
				.Because("every unkeyed collection member is decorated");
			await That(source).Contains("new global::MyCode.LoggingDecorator(ResolveContextual(__s))")
				.Because("a WhenInjectedInto registration is wrapped by the same chain as the default");
			await That(source).DoesNotContain("new global::MyCode.LoggingDecorator(ResolveBackup(__s))")
				.Because("an unkeyed decorator leaves a user-keyed registration undecorated");
		}

		[Test]
		public async Task UnkeyedDecorator_ReportsAwt123_WhenTheServiceIsOnlyRegisteredUnderAKey()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IService { }
			                                       public sealed class Backup : IService { }
			                                       public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }

			                                       [Container]
			                                       [Transient<Backup, IService>(Key = "backup")]
			                                       [Decorate<LoggingDecorator, IService>]
			                                       public static partial class MyContainer
			                                       {
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT123*").AsWildcard()
				.Because("an unkeyed decorator does not wrap keyed registrations, so it has nothing to decorate");
		}
	}
}
