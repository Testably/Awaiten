using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     The adapter convention documented under AWT135, over the compiled <see cref="AwaitenServiceLocator" />, so
///     the sample cannot drift from what the container does. What is pinned is its two answers that are not a plain
///     instance: a withheld service reaching the caller as the container's guidance rather than as the framework's
///     "not registered", and an unresolvable collection enumerating empty rather than coming back
///     <see langword="null" />.
/// </summary>
public partial class AdapterConventionTests
{
	public sealed class ResolvingASingleService
	{
		[Fact]
		public async Task HandsBackWhatTheContainerHas()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(locator.Resolve(typeof(Widget))).Is<Widget>();
		}

		[Fact]
		public async Task AnswersNullForATypeTheContainerDoesNotHave()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(locator.Resolve(typeof(Stranger))).IsNull()
				.Because("null is how the adapter tells the framework the type is not the container's, which is the answer a genuinely absent service earns");
		}

		[Fact]
		public async Task AnswersNullForANullType()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(locator.Resolve(null)).IsNull();
		}

		[Fact]
		public async Task ThrowsTheContainersGuidanceForAWithheldService()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			void ResolveWithheld() => locator.Resolve(typeof(Connection));

			await That(ResolveWithheld).Throws<InvalidOperationException>().WithMessage("*ResolveAsync*").AsWildcard()
				.Because("an async-initialized service reached synchronously exists but is withheld, and reporting that as null would let the framework bind it from elsewhere and fail far from the cause");
		}

		[Fact]
		public async Task DistinguishesWithheldFromAbsent()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(() => locator.Resolve(typeof(Connection))).Throws<InvalidOperationException>();
			await That(locator.Resolve(typeof(Stranger))).IsNull()
				.Because("both are the same false from TryResolve, and telling them apart is the whole point of asking for the withheld reason");
		}
	}

	public sealed class ResolvingACollection
	{
		[Fact]
		public async Task EnumeratesEmptyForAnUnregisteredElementType()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			object? resolved = locator.Resolve(typeof(IEnumerable<Stranger>));

			await That(resolved).IsNotNull()
				.Because("a framework that uses a collection as an extension point enumerates it without a null check, so 'no handlers' has to be an empty sequence");
			await That(((IEnumerable<Stranger>)resolved!).Any()).IsFalse();
		}

		[Fact]
		public async Task EnumeratesEmptyForAnElementTypeRegisteredOnlyUnderAKey()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			object? resolved = locator.Resolve(typeof(IEnumerable<IChannel>));

			await That(resolved).IsNotNull()
				.Because("keyed registrations are not members of an unkeyed collection, so the resolvability guard must not read the keyed slot as members that an empty sequence would drop");
			await That(((IEnumerable<IChannel>)resolved!).Any()).IsFalse();
		}

		[Fact]
		public async Task AnswersNullForAValueElementType()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(locator.Resolve(typeof(IEnumerable<int>))).IsNull()
				.Because("manifesting the empty array would need the value type's array type, which native AOT does not generate on demand, so the shape is reported unavailable rather than traded for a run-time crash");
		}

		[Fact]
		public async Task AnswersNullForACollectionShapeTheConventionDoesNotCover()
		{
			using LocatorContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(locator.Resolve(typeof(IReadOnlyList<Stranger>))).IsNull()
				.Because("the guarantee frameworks rely on covers IEnumerable<T> alone, exactly as in MS.DI");
		}

		[Fact]
		public async Task ThrowsRatherThanEnumeratingEmptyWhenMembersExistButTheShapeCannotBeBuilt()
		{
			using AsyncCollectionContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			void ResolveCollection() => locator.Resolve(typeof(IEnumerable<IHandler>));

			await That(ResolveCollection).Throws<InvalidOperationException>()
				.WithMessage("*async-tainted member*").AsWildcard()
				.Because("one member that needs async initialization means the synchronous collection cannot be built, and an empty sequence would silently drop the members that do exist, so the container's reason reaches the caller instead");
		}

		[Fact]
		public async Task AnswersNullRatherThanEmptyWhenTheElementTypeResolvesButTheCollectionShapeDoesNot()
		{
			using SuppressedCollectionContainer.Root container = new();
			AwaitenServiceLocator locator = new(container, container);

			await That(container.TryResolve(typeof(IEnumerable<IHandler>), out _)).IsFalse()
				.Because("an explicitly registered collection shape suppresses the synthesized siblings, so this shape is genuinely unresolvable");
			await That(container.WithheldReason(typeof(IEnumerable<IHandler>), null)).IsNull()
				.Because("the container is not withholding it either, which is what leaves the empty-collection answer as the only guard");

			await That(locator.Resolve(typeof(IEnumerable<IHandler>))).IsNull()
				.Because("the element type resolves, so members exist, and answering an empty sequence would silently drop them: the resolvability guard is the only thing standing between the convention and that");
		}
	}

	public interface IChannel;

	public interface IHandler;

	public sealed class Widget;

	public sealed class Stranger;

	public sealed class Fast : IChannel;

	public sealed class Connection : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public sealed class SyncHandler : IHandler;

	public sealed class AsyncHandler : IHandler, IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	/// <summary>An explicitly registered collection shape, which suppresses the synthesized siblings.</summary>
	public sealed class HandlerBag(IHandler handler) : IReadOnlyList<IHandler>
	{
		public int Count => 1;

		public IHandler this[int index] => index == 0 ? handler : throw new ArgumentOutOfRangeException(nameof(index));

		public IEnumerator<IHandler> GetEnumerator()
		{
			yield return handler;
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	[Container]
	[Transient<Widget>]
	[Singleton<Connection>]
	[Singleton<Fast, IChannel>(Key = "fast")]
	public static partial class LocatorContainer;

	[Container]
	[Singleton<SyncHandler, IHandler>]
	[Singleton<AsyncHandler, IHandler>]
	public static partial class AsyncCollectionContainer;

	[Container]
	[Singleton<SyncHandler, IHandler>]
	[Singleton<HandlerBag, IReadOnlyList<IHandler>>]
	public static partial class SuppressedCollectionContainer;
}
