namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Keyed registration. Implementations share one service type under different keys, and a
///     <c>[FromKey]</c> parameter wires to the matching resolver. Keyed registrations are reached only through
///     that injection, never the public unkeyed dispatch table or the typed resolver fast path.
/// </summary>
public class KeyedRegistrationTests
{
	[Fact]
	public async Task FromKeyParameter_DispatchesToTheMatchingImplementationsResolver()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class FastClock : IClock { }
		                                       public sealed class SlowClock : IClock { }
		                                       public sealed class Consumer { public Consumer([FromKey("slow")] IClock clock) { } }

		                                       [Container]
		                                       [Singleton<FastClock, IClock>(Key = "fast")]
		                                       [Singleton<SlowClock, IClock>(Key = "slow")]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Consumer(Root.ResolveSlowClock(__s.__root))")
			.Because("the keyed dependency is wired to the implementation registered under its key");
	}

	[Fact]
	public async Task KeyedServices_AreReachableThroughTheKeyedDispatchButNotTheUnkeyedOneOrTheTypedResolver()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class FastClock : IClock { }
		                                       public sealed class SlowClock : IClock { }
		                                       public sealed class Consumer { public Consumer([FromKey("slow")] IClock clock) { } }

		                                       [Container]
		                                       [Singleton<FastClock, IClock>(Key = "fast")]
		                                       [Singleton<SlowClock, IClock>(Key = "slow")]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The keyed implementations are registered and constructible, so their absence from the unkeyed dispatch
		// table is exclusion, not IClock going away.
		await That(source).Contains("ResolveFastClock")
			.Because("the keyed implementation is registered and constructible");
		await That(source).Contains("ResolveSlowClock")
			.Because("the keyed implementation is registered and constructible");

		// Imperative keyed resolution reaches each registration through the (service type, key) keyed table.
		await That(source).Contains("new __KeyedKey(typeof(global::MyCode.IClock), \"fast\")")
			.Because("the keyed 'fast' registration is reachable through the keyed dispatch table");
		await That(source).Contains("new __KeyedKey(typeof(global::MyCode.IClock), \"slow\")")
			.Because("the keyed 'slow' registration is reachable through the keyed dispatch table");

		await That(source).DoesNotContain("new __Bucket(typeof(global::MyCode.IClock)")
			.Because("a keyed service is never placed in the unkeyed by-type dispatch table");
		await That(source).DoesNotContain("global::Awaiten.IAwaitenResolver<global::MyCode.IClock>")
			.Because("a keyed service gets no typed resolution fast path");
	}

	[Fact]
	public async Task SyntheticDecoratorKeys_AreNeverExposedThroughTheKeyedDispatchOrMetadata()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class RealClock : IClock { }
		                                       public sealed class FastClock : IClock { }
		                                       public sealed class LoggingClock : IClock { public LoggingClock(IClock inner) { } }

		                                       [Container]
		                                       [Singleton<RealClock, IClock>]
		                                       [Singleton<FastClock, IClock>(Key = "fast")]
		                                       [Decorate<LoggingClock, IClock>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The decorator allocates internal __dec: keys in the same key space as the user "fast" key; those synthetic
		// keys are internal wiring and must never surface as a resolvable key or in the advertised registrations.
		await That(source).Contains("new __KeyedKey(typeof(global::MyCode.IClock), \"fast\")")
			.Because("the user-declared 'fast' key is reachable through the keyed dispatch");
		await That(source).DoesNotContain("__dec:")
			.Because("a synthetic decorator key is never emitted as a resolvable key or advertised registration");
		await That(source).DoesNotContain("__ctx:")
			.Because("a synthetic contextual key is never emitted as a resolvable key or advertised registration");
	}

	[Fact]
	public async Task FromKeyParameter_SelectsTheKeyedRegistrationThroughFuncAndLazyRelationships()
	{
		GeneratorResult result = Generator.Run("""
		                                       using System;
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class FastClock : IClock { }
		                                       public sealed class SlowClock : IClock { }
		                                       public sealed class Consumer
		                                       {
		                                           public Consumer([FromKey("slow")] Func<IClock> deferred, [FromKey("fast")] Lazy<IClock> lazy) { }
		                                       }

		                                       [Container]
		                                       [Singleton<FastClock, IClock>(Key = "fast")]
		                                       [Singleton<SlowClock, IClock>(Key = "slow")]
		                                       [Singleton<Consumer>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::System.Func<global::MyCode.IClock>(() => Root.ResolveSlowClock(__s.__root))")
			.Because("a [FromKey] Func<T> defers to the implementation registered under its key");
		await That(source).Contains("new global::System.Lazy<global::MyCode.IClock>(() => Root.ResolveFastClock(__s.__root))")
			.Because("a [FromKey] Lazy<T> defers to the implementation registered under its key");
	}

	[Fact]
	public async Task UnkeyedRegistration_CoexistsWithKeyedOnesOfTheSameServiceType()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class DefaultClock : IClock { }
		                                       public sealed class FastClock : IClock { }

		                                       [Container]
		                                       [Singleton<DefaultClock, IClock>]
		                                       [Singleton<FastClock, IClock>(Key = "fast")]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("typeof(global::MyCode.IClock)")
			.Because("the unkeyed registration is still resolvable by its service type");
		await That(source).Contains("global::Awaiten.IAwaitenResolver<global::MyCode.IClock>")
			.Because("the unkeyed registration keeps its typed resolution fast path");
	}
}
