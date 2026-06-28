namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the generated dispatch table: a container whose resolution is routed through a
///     static <see cref="System.Collections.Generic.Dictionary{TKey,TValue}" /> + <c>switch</c>. It proves the
///     table resolves the first, middle and last registrations, shares singletons, falls through for
///     unregistered types, and is reused by a created scope.
/// </summary>
public partial class LargeContainerDispatchTests
{
	[Fact]
	public async Task DirectServices_ResolveAcrossTheWholeTable()
	{
		LargeContainer.Root container = new();

		await That(container.Resolve<S00>()).IsNotNull()
			.Because("the first registration exercises one end of the switch");
		await That(container.Resolve<S19>()).IsNotNull()
			.Because("the last registration exercises the other end of the switch");
		// Resolve a middle registration by runtime Type so the Dictionary + switch dispatch table is
		// exercised (the generic Resolve<T> takes the typed fast path and never touches the table).
		await That(container.TryResolve(typeof(S10), out object? middle)).IsTrue();
		await That(middle).Is<S10>();
	}

	[Fact]
	public async Task Scope_ResolvesThroughTheSharedTable()
	{
		LargeContainer.Root container = new();
		using IAwaitenScope scope = container.CreateScope();

		await That(scope.Resolve<S03>()).IsNotNull();
		await That(scope.TryResolve(typeof(S15), out object? service)).IsTrue();
		await That(service).Is<S15>();
	}

	[Fact]
	public async Task Singleton_ReturnsTheSameInstanceThroughTheTable()
	{
		LargeContainer.Root container = new();

		await That(container.Resolve<S07>()).IsSameAs(container.Resolve<S07>());
	}

	[Fact]
	public async Task UnregisteredType_FallsThroughToFalse()
	{
		LargeContainer.Root container = new();

		bool resolved = container.TryResolve(typeof(string), out object? instance);

		await That(resolved).IsFalse();
		await That(instance).IsNull();
	}

	public sealed class S00;

	public sealed class S01;

	public sealed class S02;

	public sealed class S03;

	public sealed class S04;

	public sealed class S05;

	public sealed class S06;

	public sealed class S07;

	public sealed class S08;

	public sealed class S09;

	public sealed class S10;

	public sealed class S11;

	public sealed class S12;

	public sealed class S13;

	public sealed class S14;

	public sealed class S15;

	public sealed class S16;

	public sealed class S17;

	public sealed class S18;

	public sealed class S19;

	// A container with many registrations, resolved through the generated static Dictionary<Type, int> +
	// switch dispatch table.
	[Container]
	[Singleton<S00>]
	[Singleton<S01>]
	[Singleton<S02>]
	[Singleton<S03>]
	[Singleton<S04>]
	[Singleton<S05>]
	[Singleton<S06>]
	[Singleton<S07>]
	[Singleton<S08>]
	[Singleton<S09>]
	[Singleton<S10>]
	[Singleton<S11>]
	[Singleton<S12>]
	[Singleton<S13>]
	[Singleton<S14>]
	[Singleton<S15>]
	[Singleton<S16>]
	[Singleton<S17>]
	[Singleton<S18>]
	[Singleton<S19>]
	public static partial class LargeContainer;
}
