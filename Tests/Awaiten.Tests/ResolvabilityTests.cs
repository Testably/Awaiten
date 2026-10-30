using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     <see cref="IAwaitenRoot.GetResolvability" /> over real containers: each status, unkeyed and keyed, and a
///     variance-compatible closing, with the reason matching what <c>Resolve</c> throws.
/// </summary>
public partial class ResolvabilityTests
{
	public sealed class Unkeyed
	{
		[Test]
		public async Task ReportsAnUnregisteredTypeAsNotRegistered()
		{
			using MixedContainer.Root container = new();

			AwaitenResolvability answer = container.GetResolvability(typeof(Unregistered), null);

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.NotRegistered);
			await That(answer.Reason).IsNull();
		}

		[Test]
		public async Task ReportsARegisteredServiceAsResolvable()
		{
			using MixedContainer.Root container = new();

			AwaitenResolvability answer = container.GetResolvability(typeof(Plain), null);

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.Resolvable);
			await That(answer.Reason).IsNull();
		}

		[Test]
		public async Task ReportsARelationshipShapeAsResolvable()
		{
			using MixedContainer.Root container = new();

			await That(container.GetResolvability(typeof(Func<Plain>), null).Status).IsEqualTo(AwaitenResolvabilityStatus.Resolvable)
				.Because("the probe reads the dispatch table, which serves the relationship shapes over a registration too");
		}

		[Test]
		public async Task ReportsAnAsyncInitializedServiceAsAsyncOnly()
		{
			using AsyncOnlyContainer.Root container = new();

			AwaitenResolvability answer = container.GetResolvability(typeof(Warming), null);

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly);
			await That(answer.Reason).IsEqualTo(RefusalMessage(container, typeof(Warming)))
				.Because("the reason is the message Resolve throws, made available without throwing");
			await That(answer.Reason).Contains("ResolveAsync");
		}

		[Test]
		public async Task ReportsADisposableTransientAsWithheldOnRoot()
		{
			using MixedContainer.Root container = new();
			using IAwaitenScope scope = container.CreateScope();

			AwaitenResolvability answer = container.GetResolvability(typeof(Disposable), null);

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.WithheldOnRoot);
			await That(answer.Reason).IsEqualTo(RefusalMessage(container, typeof(Disposable)))
				.Because("the strict lifetime default withholds a disposable transient on the root, and Resolve throws exactly that reason");
			await That(scope.Resolve<Disposable>()).IsNotNull()
				.Because("a child scope bounds the transient's lifetime, which is why the status is about the root alone");
		}

		[Test]
		public async Task ReportsNotRegisteredForAContainerWithNoTableToRead()
		{
			using EmptyContainer.Root container = new();

			await That(container.GetResolvability(typeof(Plain), null)).IsEqualTo(default(AwaitenResolvability));
		}
	}

	public sealed class Keyed
	{
		[Test]
		public async Task ReportsAnAbsentKeyAsNotRegistered()
		{
			using KeyedContainer.Root container = new();

			await That(container.GetResolvability(typeof(Plain), "absent").Status).IsEqualTo(AwaitenResolvabilityStatus.NotRegistered);
			await That(container.GetResolvability(typeof(Unregistered), "plain").Status).IsEqualTo(AwaitenResolvabilityStatus.NotRegistered);
		}

		[Test]
		public async Task ReportsAKeyedServiceAsResolvable()
		{
			using KeyedContainer.Root container = new();

			AwaitenResolvability answer = container.GetResolvability(typeof(Plain), "plain");

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.Resolvable);
			await That(answer.Reason).IsNull();
		}

		[Test]
		public async Task ReportsAKeyedAsyncInitializedServiceAsAsyncOnly()
		{
			using KeyedContainer.Root container = new();

			AwaitenResolvability answer = container.GetResolvability(typeof(Warming), "warming");

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.AsyncOnly);
			await That(answer.Reason).IsEqualTo(RefusalMessage(container, typeof(Warming), "warming"));
		}

		[Test]
		public async Task ReportsAKeyedDisposableTransientAsWithheldOnRoot()
		{
			using KeyedContainer.Root container = new();

			AwaitenResolvability answer = container.GetResolvability(typeof(Disposable), "disposable");

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.WithheldOnRoot);
			await That(answer.Reason).IsEqualTo(RefusalMessage(container, typeof(Disposable), "disposable"))
				.Because("the keyed Resolve throws the same targeted guidance as the unkeyed one");
		}
	}

	public sealed class VarianceClosing
	{
		[Test]
		public async Task ReportsAClosingOfAResolvableCandidateAsResolvable()
		{
			using VarianceContainer.Root container = new();

			await That(container.GetResolvability(typeof(IHandler<IEvent>), null).Status).IsEqualTo(AwaitenResolvabilityStatus.Resolvable)
				.Because("the variance fallback satisfies a differently-closed variant interface through its nearest candidate");
		}

		[Test]
		public async Task ReportsAClosingOfAWithheldCandidateAsWithheldOnRoot()
		{
			using WithheldVarianceContainer.Root container = new();
			Type closing = typeof(IHandler<IEvent>);

			AwaitenResolvability answer = container.GetResolvability(closing, null);

			await That(answer.Status).IsEqualTo(AwaitenResolvabilityStatus.WithheldOnRoot);
			await That(answer.Reason).IsEqualTo(RefusalMessage(container, closing))
				.Because("a variant closing has no entry of its own, so it answers like its nearest candidate, as Resolve does");
		}
	}

	private static string? RefusalMessage(IAwaitenResolver resolver, Type serviceType, object? key = null)
	{
		try
		{
			_ = key is null ? resolver.Resolve(serviceType) : resolver.Resolve(serviceType, key);
			return null;
		}
		catch (InvalidOperationException exception)
		{
			return exception.Message;
		}
	}

	public sealed class Plain;

	public sealed class Unregistered;

	public sealed class Disposable : IDisposable
	{
		public void Dispose()
		{
		}
	}

	public sealed class Warming : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public interface IEvent;

	public sealed class DerivedEvent : IEvent;

	public interface IHandler<out TEvent>;

	public sealed class Handler : IHandler<DerivedEvent>;

	public sealed class DisposableHandler : IHandler<DerivedEvent>, IDisposable
	{
		public void Dispose()
		{
		}
	}

	[Container]
	[Singleton<Plain>]
	[Transient<Disposable>]
	public static partial class MixedContainer;

	[Container]
	[Singleton<Warming>]
	public static partial class AsyncOnlyContainer;

	[Container]
	public static partial class EmptyContainer;

	[Container]
	[Singleton<Plain>(Key = "plain")]
	[Singleton<Warming>(Key = "warming")]
	[Transient<Disposable>(Key = "disposable")]
	public static partial class KeyedContainer;

	[Container]
	[Singleton<Handler, IHandler<DerivedEvent>>]
	public static partial class VarianceContainer;

	[Container]
	[Transient<DisposableHandler, IHandler<DerivedEvent>>]
	public static partial class WithheldVarianceContainer;
}
