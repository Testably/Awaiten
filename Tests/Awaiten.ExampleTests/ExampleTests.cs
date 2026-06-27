using Awaiten.ExampleTests.TestHelpers;

namespace Awaiten.ExampleTests;

/// <summary>
///     End-to-end usage examples that double as documentation: declaring a <c>[Container]</c>
///     (see <see cref="Container" />) and resolving a registered service from it.
/// </summary>
public partial class ExampleTests
{
	[Fact]
	public async Task Container_ResolvesTheRegisteredService()
	{
		Container container = new();

		IMyService myService = container.Resolve<IMyService>();

		await That(myService).Is<MyService>();
	}

	/// <summary>
	///     A minimal composition root: <see cref="MyService" /> is registered as a singleton exposed
	///     through <see cref="IMyService" />. The source generator emits the resolution logic on this
	///     <see langword="partial" /> class.
	/// </summary>
	[Container]
	[Singleton<MyService, IMyService>]
	public partial class Container;
}
