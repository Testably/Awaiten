namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of collection dependencies: a collection-typed parameter (
///     <c>IEnumerable&lt;T&gt;</c> and friends, or <c>T[]</c>) is materialized as an array of every unkeyed
///     registration of <c>T</c>, in registration order, and <c>IEnumerable&lt;T&gt;</c> / <c>T[]</c> are
///     added to the public dispatch table. Every member is a real instance (the "losing" registration is not
///     dropped), an empty collection is a legal empty array, and an async-tainted member is rejected.
/// </summary>
public class CollectionTests
{
	[Fact]
	public async Task CollectionDependency_MaterializesAllRegistrationsAsAnArray()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Beta : IPlugin { }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Beta, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The collection is an array of every member's resolver, in registration order; both members get their
		// own backing field (no second instance is fabricated for the "losing" registration).
		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { ResolveAlpha(), ResolveBeta() })")
			.Because("the collection dependency materializes every registration in registration order");
		await That(source).Contains("_alpha")
			.Because("the first (winning) registration is a real instance");
		await That(source).Contains("_beta")
			.Because("the losing registration is still built - it is reached through the collection");

		// IEnumerable<T> and T[] are added to the public dispatch table; the single IPlugin still dispatches to
		// the winner.
		await That(source).Contains("typeof(global::System.Collections.Generic.IEnumerable<global::MyCode.IPlugin>)")
			.Because("the collection is publicly resolvable as IEnumerable<T>");
		await That(source).Contains("typeof(global::MyCode.IPlugin[])")
			.Because("the collection is publicly resolvable as T[]");
		await That(source).Contains("instance = new global::MyCode.IPlugin[] { ResolveAlpha(), ResolveBeta() }; return true;")
			.Because("the public collection dispatch returns the same materialized array");
		await That(source).Contains("typeof(global::MyCode.IPlugin)")
			.Because("the single IPlugin resolution still dispatches to the winning registration");
	}

	[Fact]
	public async Task ArrayParameter_IsAlsoRecognizedAsACollection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Host { public Host(IPlugin[] plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { ResolveAlpha() })")
			.Because("an array parameter resolves to the collection of registrations, not a single registration");
	}

	[Fact]
	public async Task EmptyCollection_MaterializesAnEmptyArrayWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Host { public Host(IReadOnlyList<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an unregistered element type resolves to an empty collection, not a missing-dependency error");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] {  })")
			.Because("an element type with no registration materializes an empty array");
	}

	[Fact]
	public async Task KeyedRegistrations_AreNotCollectionMembers()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Unkeyed : IPlugin { }
		                                       public sealed class Keyed : IPlugin { }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Unkeyed, IPlugin>]
		                                       [Singleton<Keyed, IPlugin>(Key = "special")]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { ResolveUnkeyed() })")
			.Because("only the unkeyed registration is a collection member; the keyed one is reached only by [FromKey]");
	}

	[Fact]
	public async Task ParameterizedService_IsNotACollectionMember()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Plain : IPlugin { }
		                                       public sealed class WithArg : IPlugin { public WithArg([Arg] int id) { } }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Plain, IPlugin>]
		                                       [Transient<WithArg, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { ResolvePlain() })")
			.Because("a parameterized [Arg] service is reachable only through its Func<TArg…, T> factory, never a collection");
	}

	[Fact]
	public async Task CaptiveCollection_ReportsAwt105WhenASingletonHoldsScopedMembers()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class ScopedPlugin : IPlugin { }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Scoped<ScopedPlugin, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT105*").AsWildcard()
			.Because("a collection captures its members eagerly, so a singleton holding a collection of scoped services is a captive dependency");
	}

	[Fact]
	public async Task CollectionWithADisposableTransientMember_IsWithheldFromRootByTypeResolution()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Disposable : IPlugin, IDisposable { public void Dispose() { } }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Transient<Disposable, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// A collection of a build-on-demand disposable member is root-withheld: materializing it by type off the
		// Root would accumulate the transient disposables for the container's lifetime, so Resolve on the Root
		// throws the collection-specific guidance while a child scope still resolves it.
		await That(source).Contains("the collection 'System.Collections.Generic.IEnumerable<MyCode.IPlugin>' has a build-on-demand disposable member")
			.Because("the IEnumerable<T> collection dispatch is withheld from the Root when a member is a disposable transient");
		await That(source).Contains("the collection 'MyCode.IPlugin[]' has a build-on-demand disposable member")
			.Because("the T[] collection dispatch is withheld from the Root too");
	}

	[Fact]
	public async Task ExplicitlyRegisteredCollectionType_WinsOverSynthesisOnInjection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Bundle : List<IPlugin> { }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Bundle, IEnumerable<IPlugin>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// IEnumerable<IPlugin> is itself a registered service, so the parameter is a direct dependency on that
		// registration - not the collection synthesized from the IPlugin members. Registering a collection type
		// as an opaque value (e.g. a string[] of command-line arguments) is therefore supported.
		await That(source).Contains("new global::MyCode.Host(__root.ResolveBundle())")
			.Because("an explicitly registered collection type wins over the synthesized collection on injection (the singleton member routes through the root)");
	}

	[Fact]
	public async Task CollectionOfNonDisposableMembers_IsNotWithheld()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Plain : IPlugin { }
		                                       public sealed class Host { public Host(IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Transient<Plain, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).DoesNotContain("has a build-on-demand disposable member")
			.Because("a collection whose members are not build-on-demand disposables leaks nothing on the Root, so it stays resolvable there");
	}
}
