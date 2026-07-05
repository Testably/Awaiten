using Awaiten.ExampleTests.TestHelpers;

namespace Awaiten.ExampleTests;

/// <summary>
///     Usage example: declare a <c>[Container]</c> (see <see cref="Container" />) and resolve a registered
///     service from it.
/// </summary>
public partial class ExampleTests
{
	[Fact]
	public async Task Container_ResolvesTheRegisteredService()
	{
		Container.Root container = new();

		IMyService myService = container.Resolve<IMyService>();

		await That(myService).Is<MyService>();
	}

	/// <summary>
	///     A minimal composition root: <see cref="MyService" /> registered as a singleton exposed through
	///     <see cref="IMyService" />. The source generator emits the resolution logic on this partial class.
	/// </summary>
	[Container]
	[Singleton<MyService, IMyService>]
	public static partial class Container;
}
