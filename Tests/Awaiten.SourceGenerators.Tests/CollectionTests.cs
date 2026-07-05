namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     A collection-typed parameter materializes every unkeyed registration of the element type as an array, in
///     registration order. An empty collection is a legal empty array. An async-tainted member is rejected on the
///     synchronous shapes (AWT122) but legal, awaited, through the <c>IAsyncEnumerable&lt;T&gt;</c> shape.
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

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), Root.ResolveBeta(__s.__root) })")
			.Because("the collection dependency materializes every registration in registration order");
		await That(source).Contains("_alpha")
			.Because("the first (winning) registration is a real instance");
		await That(source).Contains("_beta")
			.Because("the losing registration is still built - it is reached through the collection");

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

		// Root-withheld: materializing the collection by type off the Root would accumulate the transient
		// disposables for the container's lifetime, so Resolve on the Root throws guidance; a child scope resolves it.
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

		// IEnumerable<IPlugin> is itself a registered service, so the parameter depends directly on that
		// registration, not the synthesized collection. Registering a collection type as an opaque value is supported.
		await That(source).Contains("new global::MyCode.Host(Root.ResolveBundle(__s.__root))")
			.Because("an explicitly registered collection type wins over the synthesized collection on injection (the singleton member routes through the root)");

		// All-or-nothing synthesis: once one shape of IPlugin is registered, no shape is synthesized.
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
		// injected IPlugin[] is an unregistered dependency (AWT101), not a synthesized collection that would
		// disagree with the registered IEnumerable<IPlugin>.
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

		// Only a rank-1 array is a collection shape, so an unregistered int[,] is a plain missing dependency
		// (AWT101), not a synthesized literal that would not compile.
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

		// The keyed collection materializes only the 'primary' member, not the unkeyed Plain.
		await That(source).Contains("new global::MyCode.Host(new global::MyCode.IPlugin[] { Root.ResolveKeyed(__s.__root) })")
			.Because("a keyed collection resolves exactly the registrations under that key");

		// The unkeyed collection holds only Plain; keyed and unkeyed buckets stay disjoint.
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

		// All members synchronous, so the async collection is built synchronously and wrapped in __AsyncArray<T>.
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

		// One member is async-tainted, so the collection is built on the async path: the async member is awaited,
		// the synchronous member resolved directly.
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

		// IAsyncEnumerable<IPlugin> is itself a registered service, so the parameter depends directly on it, not
		// the synthesized async collection.
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

		// A registered synchronous shape makes the whole IPlugin collection opaque (all-or-nothing), suppressing
		// the IAsyncEnumerable<IPlugin> view too, so injecting the unregistered async shape is AWT101.
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

		// All members synchronous, so IAsyncEnumerable<T> joins the synchronous shapes in the by-type dispatch.
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

		// An async-tainted member routes the IAsyncEnumerable<T> shape through an async dispatch arm that awaits each member.
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

		// The registered channel owns the slot on both surfaces: no async arm is synthesized behind it, so Resolve
		// and ResolveAsync hand back the same registered service, not two disagreeing collections.
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

		// The registered channel is async-tainted, so it is absent from the synchronous dispatch. The synthesized
		// IPlugin view must not claim its vacated slot: sync Resolve throws the channel's own guidance, ResolveAsync serves it.
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

		// The collection is never injected (no AWT122) but has an async-tainted member, so it has no synchronous
		// materialization. Its shapes carry AWT122-style guidance in __withheld instead of a generic no-registration error.
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

		// All members synchronous, so the awaited collection is a completed Task.FromResult, no async machinery.
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

		// The async member is awaited inside an immediately-invoked async lambda (default token, since the consumer
		// is built synchronously); the array is cast to the requested IReadOnlyList<T> to match the parameter.
		await That(source).Contains("((global::System.Func<global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>>)(async () => (global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>)new global::MyCode.IPlugin[] { Root.ResolveAlpha(__s.__root), await Root.ResolveAsyncPluginAsync(__s.__root, default).ConfigureAwait(false) }))()")
			.Because("the async-tainted member is awaited inside the produced task, in registration order");

		// Unlike IAsyncEnumerable<T>, the awaited collection launders its members' taint (they are awaited inside
		// the task, not at construction), so the host stays synchronously constructible.
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
	public async Task EmptyAwaitedCollection_InjectedIntoAProperty_IsNotAMissingDependency()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;
		                                       using System.Threading.Tasks;

		                                       namespace MyCode;

		                                       public interface IPlugin { }
		                                       public sealed class Host
		                                       {
		                                           [Inject]
		                                           public Task<IReadOnlyList<IPlugin>> Plugins { get; set; }
		                                       }

		                                       [Container]
		                                       [Singleton<Host>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		// An [Inject] member resolves like a constructor parameter: an awaited collection is always satisfiable,
		// so an unregistered element type yields a completed empty collection, never AWT101.
		await That(result.Diagnostics).IsEmpty()
			.Because("an [Inject] awaited collection over an unregistered element type is a completed empty collection, exactly like the constructor parameter");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("Plugins = global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>(")
			.Because("the member is filled with a completed empty awaited collection");
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

		// A stored ValueTask may only be awaited once, so ValueTask<C> is deliberately not synthesized (like the
		// bare ValueTask<T> relationship); it surfaces as an ordinary missing dependency.
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

		// A registered synchronous shape makes the whole IPlugin collection opaque, suppressing the awaited
		// Task<IReadOnlyList<IPlugin>> view too. The AWT101 names that exact awaited shape as the missing
		// dependency; no bare-Task fallback resolves it through the registered IReadOnlyList<IPlugin>.
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

		// Task<IReadOnlyList<IPlugin>> is itself a registered service, so the parameter depends directly on it,
		// not the synthesized awaited collection.
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

		// Every Task<C> shape gets a by-type dispatch slot, so Resolve<Task<IReadOnlyList<T>>>() and every sibling works.
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

		// Unlike the synchronous shapes and IAsyncEnumerable<T>, the awaited collection stays on the synchronous
		// by-type dispatch even with an async-tainted member: its forwarder hands back an already-started task
		// (default token) that awaits the async member behind it.
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

		// The registered Task<IReadOnlyList<IPlugin>> owns only its own slot; sibling awaited shapes still
		// synthesize (the awaitedShapeRegistered gate claims only the exact shape).
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::MyCode.IPlugin>>), static __s => Root.ResolvePluginTask(__s.__root)")
			.Because("the registered awaited shape is served by its own resolver, not a synthesized awaited collection");
		await That(source).Contains("typeof(global::System.Threading.Tasks.Task<global::MyCode.IPlugin[]>)")
			.Because("a sibling awaited shape that was not registered still synthesizes an awaited collection");
	}
}
