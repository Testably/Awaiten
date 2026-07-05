namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of composites. The composite becomes the public single-dispatch winner for the
///     service, while every collection parameter materializes the other registrations, never the composite.
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

		await That(source).Contains("static __s => ResolveCompositeNotifier(__s)")
			.Because("the composite is the public single-dispatch winner for INotifier");
		await That(source).DoesNotContain("static __s => ResolveEmail(__s)")
			.Because("a bare channel is no longer publicly dispatched, only the composite is");

		await That(source).Contains("new global::MyCode.CompositeNotifier(new global::MyCode.INotifier[] { ResolveEmail(__s), ResolveSms(__s) })")
			.Because("the composite's collection parameter materializes the other members, excluding itself");

		await That(source).Contains("new global::MyCode.Host(new global::MyCode.INotifier[] { ResolveEmail(__s), ResolveSms(__s) })")
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

		await That(source).Contains("static __s => ResolveCompositeNotifier(__s)")
			.Because("the composite is still the public winner for INotifier");
		await That(source).Contains("new global::MyCode.CompositeNotifier(new global::MyCode.INotifier[]")
			.Because("the composite fans out to an empty array when there are no other registrations");
	}
}
