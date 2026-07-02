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
		await That(source).Contains("() => new global::MyCode.IPlugin[] { ResolveAlpha(), ResolveBeta() };")
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

		// All-or-nothing synthesis: because a shape of IPlugin (IEnumerable<IPlugin>) is registered, no shape is
		// synthesized - the IPlugin[] shape is not added to the public dispatch as a synthesized collection.
		await That(source).DoesNotContain("typeof(global::MyCode.IPlugin[])")
			.Because("registering one collection shape of IPlugin suppresses synthesis for every shape of IPlugin");
	}

	[Fact]
	public async Task ExplicitCollectionRegistration_SuppressesSynthesisOfEverySiblingShape()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Bundle : List<IPlugin> { }
		                                       // Injects a *different* collection shape than the one registered.
		                                       public sealed class Host { public Host(IPlugin[] plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Bundle, IEnumerable<IPlugin>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// All-or-nothing synthesis: registering IEnumerable<IPlugin> suppresses synthesis for IPlugin[] too, so an
		// injected IPlugin[] is an unregistered direct dependency (AWT101) rather than a silently synthesized
		// collection that would disagree with the registered IEnumerable<IPlugin>.
		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
			.Because("an unregistered sibling collection shape is a missing dependency once any shape of the element type is explicitly registered");
	}

	[Fact]
	public async Task MultidimensionalArrayParameter_IsNotACollection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public sealed class Host { public Host(int[,] grid) { } }

		                                       [Container]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// A multidimensional array is not a collection shape (only a rank-1 array is), so an unregistered one is a
		// plain missing dependency rather than a synthesized rank-1 literal that would not even compile.
		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
			.Because("a multidimensional array (int[,]) is an ordinary direct dependency, so an unregistered one is AWT101 - not broken collection codegen");
	}

	[Fact]
	public async Task KeyedCollection_ResolvesOnlyTheMembersUnderThatKey()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Keyed : IPlugin { }
		                                       public sealed class Plain : IPlugin { }
		                                       public sealed class Host { public Host([FromKey("primary")] IEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Keyed, IPlugin>(Key = "primary")]
		                                       [Singleton<Plain, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The [FromKey("primary")] collection materializes only the 'primary' member, not the unkeyed Plain. (A
		// key identifies at most one registration per service type here, so the keyed collection holds one member.)
		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { ResolveKeyed() })")
			.Because("a keyed collection resolves exactly the registrations under that key");

		// The unkeyed collection (publicly resolvable by type) holds only the unkeyed Plain - a keyed member is
		// never an unkeyed one, so the two buckets stay disjoint.
		await That(source).Contains("() => new global::MyCode.IPlugin[] { ResolvePlain() };")
			.Because("the public unkeyed collection resolves only the unkeyed registration");
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

	[Fact]
	public async Task PubliclyRequestedAsyncCollection_ThrowsGuidanceRatherThanBeingSilentlyUnresolvable()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [Singleton<AsyncPlugin, IPlugin>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// The collection is never injected, so there is no AWT122; but it holds an async-tainted member, so it has
		// no synchronous materialization. Rather than surfacing a generic "no registration", its shapes carry
		// AWT122-style guidance in the __withheld table, and none is added to the synchronous dispatch.
		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("the collection 'System.Collections.Generic.IEnumerable<MyCode.IPlugin>' has an async-tainted member")
			.Because("a publicly requested async collection surfaces guidance instead of a generic no-registration error");
		await That(source).DoesNotContain("new global::MyCode.IPlugin[] { ResolveAsyncPlugin() }")
			.Because("an async-tainted member has no synchronous resolver, so no synchronous collection literal is emitted for it");
	}
}
