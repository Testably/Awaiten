namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of collection dependencies: a collection-typed parameter (
///     <c>IEnumerable&lt;T&gt;</c> and friends, or <c>T[]</c>) is materialized as an array of every unkeyed
///     registration of <c>T</c>, in registration order, and <c>IEnumerable&lt;T&gt;</c> / <c>T[]</c> are
///     added to the public dispatch table. Every member is a real instance (the "losing" registration is not
///     dropped), an empty collection is a legal empty array, and an async-tainted member is rejected on the
///     synchronous shapes (AWT122) but legal - awaited - through the <c>IAsyncEnumerable&lt;T&gt;</c> shape.
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
		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), Root.ResolveBeta(__s.__root) })")
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
		await That(source).Contains("(Scope __s) => new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), Root.ResolveBeta(__s.__root) };")
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

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root) })")
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

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolveUnkeyed(__s.__root) })")
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

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolvePlain(__s.__root) })")
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
		await That(source).Contains("new global::MyCode.Host(Root.ResolveBundle(__s.__root))")
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
		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolveKeyed(__s.__root) })")
			.Because("a keyed collection resolves exactly the registrations under that key");

		// The unkeyed collection (publicly resolvable by type) holds only the unkeyed Plain - a keyed member is
		// never an unkeyed one, so the two buckets stay disjoint.
		await That(source).Contains("(Scope __s) => new global::MyCode.IPlugin[] { Root.ResolvePlain(__s.__root) };")
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
	public async Task AsyncEnumerableDependency_MaterializesMembersIntoTheAsyncArrayHelper()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Beta : IPlugin { }
		                                       public sealed class Host { public Host(IAsyncEnumerable<IPlugin> plugins) { } }

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

		// Every member is synchronous, so the async collection is a synchronous expression: the members are
		// materialized into a T[] in registration order and wrapped in the __AsyncArray<T> helper.
		await That(source).Contains("new __AsyncArray<global::MyCode.IPlugin>(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), Root.ResolveBeta(__s.__root) })")
			.Because("an async collection materializes every registration in registration order, wrapped as an IAsyncEnumerable<T>");
		await That(source).Contains("private sealed class __AsyncArray<T> : global::System.Collections.Generic.IAsyncEnumerable<T>, global::System.Collections.Generic.IAsyncEnumerator<T>")
			.Because("the __AsyncArray<T> backing type is emitted when an async collection is injected");
	}

	[Fact]
	public async Task AsyncEnumerableWithAnAsyncMember_AwaitsItInsteadOfReportingAwt122()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Host { public Host(IAsyncEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<AsyncPlugin, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).DoesNotContain("*AWT122*").AsWildcard()
			.Because("IAsyncEnumerable<T> awaits its members, so an async-tainted member is legal through it");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The host captured an async-tainted member, so it is built on the async path: the async member is awaited
		// through its async resolver (in registration order), the synchronous member resolved directly.
		await That(source).Contains("new __AsyncArray<global::MyCode.IPlugin>(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), await Root.ResolveAsyncPluginAsync(__s.__root, cancellationToken).ConfigureAwait(false) })")
			.Because("the async-tainted member is awaited while materializing the collection, the synchronous member resolved directly");
	}

	[Fact]
	public async Task EmptyAsyncEnumerable_MaterializesAnEmptyAsyncArrayWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Host { public Host(IAsyncEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an unregistered element type resolves to an empty async collection, not a missing-dependency error");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new __AsyncArray<global::MyCode.IPlugin>(new global::MyCode.IPlugin[] {  })")
			.Because("an element type with no registration materializes an empty async collection");
	}

	[Fact]
	public async Task ExplicitlyRegisteredAsyncEnumerable_WinsOverSynthesisOnInjection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Channel : IAsyncEnumerable<IPlugin>
		                                       {
		                                           public IAsyncEnumerator<IPlugin> GetAsyncEnumerator(CancellationToken cancellationToken = default) => null;
		                                       }
		                                       public sealed class Host { public Host(IAsyncEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Channel, IAsyncEnumerable<IPlugin>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// IAsyncEnumerable<IPlugin> is itself a registered service (an opaque channel), so the parameter is a direct
		// dependency on that registration - not the async collection synthesized from the IPlugin members.
		await That(source).Contains("new global::MyCode.Host(Root.ResolveChannel(__s.__root))")
			.Because("an explicitly registered IAsyncEnumerable<T> wins over the synthesized async collection on injection");
	}

	[Fact]
	public async Task ExplicitlyRegisteredSyncCollectionShape_SuppressesTheAsyncEnumerableViewOnInjectionToo()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Config : IReadOnlyList<IPlugin>
		                                       {
		                                           public IPlugin this[int i] => null;
		                                           public int Count => 0;
		                                           public IEnumerator<IPlugin> GetEnumerator() => null;
		                                           System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
		                                       }
		                                       public sealed class Host { public Host(IAsyncEnumerable<IPlugin> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Config, IReadOnlyList<IPlugin>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// An explicitly registered synchronous shape (IReadOnlyList<IPlugin>) makes the whole IPlugin collection an
		// opaque value, all-or-nothing. By type the IAsyncEnumerable<IPlugin> view is suppressed (SynthesisSuppressed),
		// so injecting the unregistered async shape is AWT101 - the same missing-dependency outcome - rather than a
		// second collection silently synthesized from the members behind the opaque IReadOnlyList<IPlugin>.
		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
			.Because("a registered synchronous collection shape suppresses the IAsyncEnumerable<T> view on injection too, matching the by-type SynthesisSuppressed gate");
		await That(result.Sources.TryGetValue("Awaiten.MyCode.MyContainer.g.cs", out string? source) ? source : string.Empty)
			.DoesNotContain("new __AsyncArray<global::MyCode.IPlugin>")
			.Because("the suppressed async view is not synthesized behind the opaque registration");
	}

	[Fact]
	public async Task SynchronousCollection_IsAlsoResolvableByTypeAsIAsyncEnumerable()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Beta : IPlugin { }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Beta, IPlugin>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Every member is synchronous, so IAsyncEnumerable<T> joins the synchronous shapes in the by-type dispatch,
		// wrapping the same materialized members in the __AsyncArray<T> replay enumerator.
		await That(source).Contains("typeof(global::System.Collections.Generic.IAsyncEnumerable<global::MyCode.IPlugin>)")
			.Because("a synchronous collection is also publicly resolvable as IAsyncEnumerable<T>");
		await That(source).Contains("new __AsyncArray<global::MyCode.IPlugin>(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), Root.ResolveBeta(__s.__root) })")
			.Because("the IAsyncEnumerable<T> dispatch wraps the synchronously materialized members");
	}

	[Fact]
	public async Task AsyncCollection_IsResolvableByTypeAsIAsyncEnumerableThroughResolveAsync()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
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

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The collection holds an async-tainted member, so its IAsyncEnumerable<T> shape is served by an async
		// dispatch arm routed to a generated async collection resolver that awaits each member.
		await That(source).Contains("typeof(global::System.Collections.Generic.IAsyncEnumerable<global::MyCode.IPlugin>), static (__s, __ct) => __AsObject(__ResolveAsyncCollection0(__s, __ct))")
			.Because("the async collection is resolvable by type through ResolveAsync");
		await That(source).Contains("return new __AsyncArray<global::MyCode.IPlugin>(new global::MyCode.IPlugin[] { await Root.ResolveAsyncPluginAsync(__s.__root, cancellationToken).ConfigureAwait(false) });")
			.Because("the generated async collection resolver materializes the stream, awaiting the async member");

		// Its synchronous Resolve steers to ResolveAsync rather than surfacing a generic no-registration error.
		await That(source).Contains("the async collection 'System.Collections.Generic.IAsyncEnumerable<MyCode.IPlugin>' has a member that requires asynchronous initialization")
			.Because("synchronous Resolve of the async collection shape throws guidance toward ResolveAsync");
	}

	[Fact]
	public async Task ExplicitlyRegisteredAsyncEnumerable_ClaimsTheByTypeSlotOnBothSurfaces()
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
		                                       public sealed class Channel : IAsyncEnumerable<IPlugin>
		                                       {
		                                           public IAsyncEnumerator<IPlugin> GetAsyncEnumerator(CancellationToken cancellationToken = default) => null;
		                                       }

		                                       [Container]
		                                       [Singleton<AsyncPlugin, IPlugin>]
		                                       [Singleton<Channel, IAsyncEnumerable<IPlugin>>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The registered channel owns typeof(IAsyncEnumerable<IPlugin>) on the synchronous dispatch, and no async
		// arm is synthesized behind it: ResolveAsync falls through to the same synchronous resolution, so Resolve
		// and ResolveAsync hand back the same registered service rather than two disagreeing collections.
		await That(source).Contains("typeof(global::System.Collections.Generic.IAsyncEnumerable<global::MyCode.IPlugin>), static __s => Root.ResolveChannel(__s.__root)")
			.Because("the explicitly registered IAsyncEnumerable<T> is dispatched as an ordinary service");
		await That(source).DoesNotContain("__ResolveAsyncCollection")
			.Because("no async collection arm is synthesized behind the registered async shape");
		await That(source).DoesNotContain("the async collection 'System.Collections.Generic.IAsyncEnumerable<MyCode.IPlugin>'")
			.Because("the synthesized view's guidance does not shadow a slot the registration owns");
	}

	[Fact]
	public async Task AsyncTaintedRegisteredAsyncEnumerable_IsNotShadowedByTheSynthesizedViewOnTheSyncDispatch()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Channel : IAsyncEnumerable<IPlugin>, IAsyncInitializable
		                                       {
		                                           public IAsyncEnumerator<IPlugin> GetAsyncEnumerator(CancellationToken cancellationToken = default) => null;
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Channel, IAsyncEnumerable<IPlugin>>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The registered channel is async-tainted, so it is absent from the synchronous dispatch - the synthesized
		// IPlugin view (whose members are all synchronous) must not claim its vacated slot: synchronous Resolve
		// throws the channel's own steer-to-ResolveAsync guidance, and ResolveAsync serves the channel.
		await That(source).DoesNotContain("new __AsyncArray<global::MyCode.IPlugin>")
			.Because("the synthesized async view is not emitted behind the registered async shape");
		await That(source).Contains("typeof(global::System.Collections.Generic.IAsyncEnumerable<global::MyCode.IPlugin>), static (__s, __ct) => __AsObject(Root.ResolveChannelAsync(__s.__root, __ct))")
			.Because("ResolveAsync serves the registered channel through its own async resolver");
		await That(source).Contains("'System.Collections.Generic.IAsyncEnumerable<MyCode.IPlugin>' requires asynchronous initialization")
			.Because("synchronous Resolve throws the registered service's guidance, not the synthesized view's");
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

	[Fact]
	public async Task AwaitedCollection_AllSynchronousMembers_MaterializesACompletedTask()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Beta : IPlugin { }
		                                       public sealed class Host { public Host(Task<IReadOnlyList<IPlugin>> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Beta, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("Task<IReadOnlyList<T>> is the awaited collection of T, not a missing dependency on the collection type");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Every member is synchronous, so the awaited collection is a completed Task.FromResult over the
		// synchronously materialized array - no async machinery at all.
		await That(source).Contains("global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), Root.ResolveBeta(__s.__root) })")
			.Because("an all-synchronous awaited collection is a completed task over the members in registration order");
	}

	[Fact]
	public async Task AwaitedCollectionWithAnAsyncMember_AwaitsItInsideTheProducedTaskWithoutTaintingTheConsumer()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }
		                                       public sealed class Host { public Host(Task<IReadOnlyList<IPlugin>> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<AsyncPlugin, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).DoesNotContain("*AWT122*").AsWildcard()
			.Because("an awaited collection awaits its members behind the returned task, so an async-tainted member is legal through it");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The async member is awaited inside an immediately-invoked async lambda (with no ambient token - the
		// consumer is built synchronously), the synchronous member resolved directly, and the array cast to the
		// requested IReadOnlyList<T> so the task's result type matches the parameter.
		await That(source).Contains("((global::System.Func<global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>>)(async () => (global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>)new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), await Root.ResolveAsyncPluginAsync(__s.__root, default).ConfigureAwait(false) }))()")
			.Because("the async-tainted member is awaited inside the produced task, in registration order");

		// Unlike IAsyncEnumerable<T>, the awaited collection launders its members' taint - the members are awaited
		// inside the task, not at construction - so the host stays synchronously constructible and dispatchable.
		await That(source).Contains("typeof(global::MyCode.Host), static __s => Root.ResolveHost(__s.__root)")
			.Because("a consumer of an awaited collection stays synchronously resolvable even when a member is async-tainted");
	}

	[Fact]
	public async Task EmptyAwaitedCollection_MaterializesACompletedEmptyTaskWithoutAwt101()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Host { public Host(Task<IPlugin[]> plugins) { } }

		                                       [Container]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an unregistered element type resolves to an empty awaited collection, not a missing-dependency error");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::System.Threading.Tasks.Task.FromResult<global::MyCode.IPlugin[]>(new global::MyCode.IPlugin[] {  })")
			.Because("an element type with no registration materializes a completed empty awaited collection");
	}

	[Fact]
	public async Task ValueTaskCollection_IsNotAnAwaitedCollection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Host { public Host(ValueTask<IReadOnlyList<IPlugin>> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// A stored ValueTask may only be awaited once, so - exactly like the bare ValueTask<T> relationship -
		// ValueTask<C> is deliberately not synthesized; it surfaces as an ordinary missing dependency.
		await That(result.Diagnostics).Contains("*AWT101*").AsWildcard()
			.Because("ValueTask<C> is not an awaited collection shape, matching the bare ValueTask<T> relationship decision");
	}

	[Fact]
	public async Task ExplicitlyRegisteredSyncCollectionShape_SuppressesTheAwaitedViewOnInjectionToo()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Config : IReadOnlyList<IPlugin>
		                                       {
		                                           public IPlugin this[int i] => null;
		                                           public int Count => 0;
		                                           public IEnumerator<IPlugin> GetEnumerator() => null;
		                                           System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
		                                       }
		                                       public sealed class Host { public Host(Task<IReadOnlyList<IPlugin>> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Config, IReadOnlyList<IPlugin>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// A registered synchronous shape makes the whole IPlugin collection an opaque value, all-or-nothing:
		// the awaited Task<IReadOnlyList<IPlugin>> view is suppressed alongside the sibling shapes, so injecting
		// the unregistered awaited shape is AWT101 rather than a second collection synthesized behind the opaque one.
		// The AWT101 names the awaited Task<C> parameter itself (not some other missing dependency): the awaited
		// view is suppressed to a direct dependency on the full Task<IReadOnlyList<IPlugin>> type, which is not
		// registered - and, notably, no bare-Task fallback resolves it through the registered IReadOnlyList<IPlugin>.
		await That(result.Diagnostics)
			.Contains("*AWT101*requires 'System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MyCode.IPlugin>>', which is not registered*").AsWildcard()
			.Because("suppressing the awaited view rewrites the parameter to a direct dependency on the full Task<IReadOnlyList<IPlugin>> type, so that exact awaited shape is the missing dependency - the registered IReadOnlyList<IPlugin> does not serve it through a bare Task relationship");
		await That(result.Sources.TryGetValue("Awaiten.MyCode.MyContainer.g.cs", out string? source) ? source : string.Empty)
			.DoesNotContain("Task.FromResult<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>")
			.Because("the suppressed awaited view is not synthesized behind the opaque registration");
	}

	[Fact]
	public async Task ExplicitlyRegisteredAwaitedShape_WinsOverSynthesisOnInjection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class PluginTask : Task<IReadOnlyList<IPlugin>>
		                                       {
		                                           public PluginTask() : base(() => null) { }
		                                       }
		                                       public sealed class Host { public Host(Task<IReadOnlyList<IPlugin>> plugins) { } }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<PluginTask, Task<IReadOnlyList<IPlugin>>>]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Task<IReadOnlyList<IPlugin>> is itself a registered service (an opaque, pre-built task), so the parameter
		// is a direct dependency on that registration - not the awaited collection synthesized from the members.
		await That(source).Contains("new global::MyCode.Host(Root.ResolvePluginTask(__s.__root))")
			.Because("an explicitly registered Task<C> claims its own exact shape, winning over the synthesized awaited collection");
		await That(source).DoesNotContain("Task.FromResult<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>")
			.Because("no awaited collection is synthesized behind the registered shape");
	}

	[Fact]
	public async Task OpenGenericRegistrations_ExpandIntoAnAwaitedClosedCollection()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public sealed class OrderPlaced { }
		                                       public interface IHandler<T> { }
		                                       public sealed class AuditHandler<T> : IHandler<T> { }
		                                       public sealed class ProjectionHandler<T> : IHandler<T> { }
		                                       public sealed class Dispatcher { public Dispatcher(Task<IReadOnlyList<IHandler<OrderPlaced>>> handlers) { } }

		                                       [Container]
		                                       [Transient(typeof(AuditHandler<>), typeof(IHandler<>))]
		                                       [Transient(typeof(ProjectionHandler<>), typeof(IHandler<>))]
		                                       [Transient<Dispatcher>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("an awaited collection of a closed generic seeds open generic expansion through its inner type (the Task unwrap in RequiredServiceType)");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Both open registrations expand at the closed argument and join the awaited collection, in declaration order.
		await That(source).Contains("global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IHandler<global::MyCode.OrderPlaced>>>(new global::MyCode.IHandler<global::MyCode.OrderPlaced>[] {")
			.Because("the awaited collection completes over the closed member array");
		await That(source).Contains("new global::MyCode.AuditHandler<global::MyCode.OrderPlaced>()")
			.Because("the first open registration is expanded at the closed argument");
		await That(source).Contains("new global::MyCode.ProjectionHandler<global::MyCode.OrderPlaced>()")
			.Because("the second open registration is expanded at the closed argument");
	}

	[Fact]
	public async Task AwaitedCollection_EveryTaskShape_JoinsTheByTypeDispatch()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class Beta : IPlugin { }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<Beta, IPlugin>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The awaited collection joins the by-type dispatch alongside the synchronous shapes and IAsyncEnumerable<T>:
		// every Task<C> shape gets a dispatch slot, so Resolve<Task<IReadOnlyList<T>>>() (and every sibling) works.
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>)")
			.Because("the awaited IReadOnlyList<T> shape is publicly resolvable by type");
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<global::MyCode.IPlugin>>)")
			.Because("the awaited IEnumerable<T> shape is publicly resolvable by type");
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::MyCode.IPlugin[]>)")
			.Because("the awaited array shape is publicly resolvable by type");
	}

	[Fact]
	public async Task AwaitedCollectionWithAnAsyncMember_JoinsTheSynchronousByTypeDispatch()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class AsyncPlugin : IPlugin, IAsyncInitializable
		                                       {
		                                           public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		                                       }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<AsyncPlugin, IPlugin>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// Unlike the synchronous shapes (withheld with AWT122-style guidance) and IAsyncEnumerable<T> (served by an
		// async arm), the awaited collection stays on the SYNCHRONOUS by-type dispatch even with an async-tainted
		// member: its forwarder hands back an already-started task (default token - no ambient token by type) that
		// awaits the async member behind it.
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>), static __s => __R")
			.Because("the awaited collection is a synchronous by-type dispatch entry even with an async member");
		await That(source).Contains("await Root.ResolveAsyncPluginAsync(__s.__root, default).ConfigureAwait(false)")
			.Because("the by-type awaited collection awaits its async member with the default token");
	}

	[Fact]
	public async Task ExplicitlyRegisteredAwaitedShape_ClaimsOnlyItsOwnByTypeSlot()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Alpha : IPlugin { }
		                                       public sealed class PluginTask : Task<IReadOnlyList<IPlugin>>
		                                       {
		                                           public PluginTask() : base(() => null) { }
		                                       }

		                                       [Container]
		                                       [Singleton<Alpha, IPlugin>]
		                                       [Singleton<PluginTask, Task<IReadOnlyList<IPlugin>>>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The registered Task<IReadOnlyList<IPlugin>> owns its own by-type slot (its bare resolver), and no awaited
		// collection is synthesized behind it - but the sibling awaited shapes still synthesize, exactly as on the
		// injection side (the awaitedShapeRegistered gate claims only the exact shape).
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>), static __s => Root.ResolvePluginTask(__s.__root)")
			.Because("the registered awaited shape is served by its own resolver, not a synthesized awaited collection");
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::MyCode.IPlugin[]>)")
			.Because("a sibling awaited shape that was not registered still synthesizes an awaited collection");
	}
}
