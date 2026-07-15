using aweXpect.Reflection;
using CoffeeShop;

namespace Awaiten.ExampleTests;

/// <summary>
///     Architecture test from the "Design principles &amp; the composition root" docs: domain and application
///     code must never reference Awaiten. Only the composition root is allowed to. This turns the
///     convention into a red build the day someone crosses the line, even where AWT134 cannot see it.
/// </summary>
public class ArchitectureTests
{
	[Fact]
	public async Task Domain_has_no_reference_to_Awaiten()
	{
		// every type in the namespace that holds the coffee-shop domain and application code
		var domain = Types.InNamespace(typeof(Barista).Namespace!);

		await That(domain)
			.DoNotDependOn(Types.InNamespace("Awaiten"));
	}
}
