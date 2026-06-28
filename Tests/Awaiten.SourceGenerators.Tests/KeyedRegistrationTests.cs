namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of keyed registration: several implementations share one service type under
///     different keys, and a <c>[FromKey]</c> parameter is wired to the matching implementation's
///     resolver. Keyed registrations are reached only through that injection - never the public unkeyed
///     dispatch table or the typed resolver fast path.
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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("new global::MyCode.Consumer(ResolveSlowClock())")
			.Because("the keyed dependency is wired to the implementation registered under its key");
	}

	[Fact]
	public async Task KeyedServices_AreNotExposedThroughThePublicDispatchOrTypedResolver()
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
		                                       public partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).DoesNotContain("typeof(global::MyCode.IClock)")
			.Because("a keyed service is reached only by [FromKey], never the public unkeyed dispatch table");
		await That(source).DoesNotContain("global::Awaiten.IAwaitenResolver<global::MyCode.IClock>")
			.Because("a keyed service gets no typed resolution fast path");
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
		                                       public partial class MyContainer
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
