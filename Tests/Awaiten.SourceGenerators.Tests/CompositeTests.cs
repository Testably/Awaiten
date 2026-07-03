namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of composites: <c>[Composite&lt;TComposite, TService&gt;]</c> is model-building over
///     the existing collection and single-dispatch plumbing — the composite becomes the public single-dispatch
///     winner for the service, while its own collection parameter (and any separate consumer's) materializes the
///     OTHER registrations, never the composite. No new emission is introduced.
/// </summary>
public class CompositeTests
{
	[Fact]
	public async Task Composite_IsThePublicWinner_AndFansOutToTheBareMembers()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface INotifier { }
		                                       public sealed class Email : INotifier { }
		                                       public sealed class Sms : INotifier { }
		                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(IEnumerable<INotifier> channels) { } }
		                                       public sealed class Host { public Host(IEnumerable<INotifier> all) { } }

		                                       [Container]
		                                       [Transient<Email, INotifier>]
		                                       [Transient<Sms, INotifier>]
		                                       [Transient<Host>]
		                                       [Composite<CompositeNotifier, INotifier>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The single INotifier dispatch resolves the composite, never a bare channel.
		await That(source).Contains("static __s => __s.ResolveCompositeNotifier()")
			.Because("the composite is the public single-dispatch winner for INotifier");
		await That(source).DoesNotContain("static __s => __s.ResolveEmail()")
			.Because("a bare channel is no longer publicly dispatched — only the composite is");

		// The composite fans out to the OTHER registrations (Email, Sms), never to itself.
		await That(source).Contains("new global::MyCode.CompositeNotifier(new global::MyCode.INotifier[] { ResolveEmail(), ResolveSms() })")
			.Because("the composite's collection parameter materializes the other members, excluding itself");

		// A separate IEnumerable<INotifier> consumer also gets the bare channels, not the composite.
		await That(source).Contains("new global::MyCode.Host(new global::MyCode.INotifier[] { ResolveEmail(), ResolveSms() })")
			.Because("the composite is excluded from every collection, so a separate consumer sees the bare members");
	}

	[Fact]
	public async Task CompositeOverZeroMembers_FansOutToAnEmptyArray()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System.Collections.Generic;

		                                       namespace MyCode;

		                                       public interface INotifier { }
		                                       public sealed class CompositeNotifier : INotifier { public CompositeNotifier(IEnumerable<INotifier> channels) { } }

		                                       [Container]
		                                       [Composite<CompositeNotifier, INotifier>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty()
			.Because("a composite over zero other registrations is legal");
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("static __s => __s.ResolveCompositeNotifier()")
			.Because("the composite is still the public winner for INotifier");
		await That(source).Contains("new global::MyCode.CompositeNotifier(new global::MyCode.INotifier[]")
			.Because("the composite fans out to an empty array when there are no other registrations");
	}
}
