using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     The typed async fast path behind <c>ResolveAsync&lt;T&gt;</c> (<see cref="IAwaitenAsyncResolver{T}" />): the
///     successfully completed task of a singleton is cached on the root, a faulted or canceled one is not, and the
///     disposed guards still throw synchronously.
/// </summary>
public sealed partial class TypedAsyncResolutionTests
{
	private static CancellationToken Ct => TestContext.Current!.Execution.CancellationToken;

	[Test]
	public async Task WarmedAsyncSingleton_ReturnsTheSameCompletedTask()
	{
		using GateContainer.Root container = new();
		await container.InitializeAsync(Ct);

		Task<IGate> first = container.ResolveAsync<IGate>(Ct);
		Task<IGate> second = container.ResolveAsync<IGate>(Ct);

		await That(first.Status).IsEqualTo(TaskStatus.RanToCompletion);
		await That((object)second).IsSameAs(first).Because("the completed task of a warmed singleton is cached on the root");
	}

	[Test]
	public async Task SyncSingleton_FromAChildScope_SharesTheCachedTaskOfTheRoot()
	{
		using GateContainer.Root container = new();
		using GateContainer.Scope scope = container.CreateScope();

		Task<Plain> fromRoot = container.ResolveAsync<Plain>(Ct);
		Task<Plain> fromScope = scope.ResolveAsync<Plain>(Ct);

		await That((object)fromScope).IsSameAs(fromRoot).Because("a singleton's completed task is cached once per container");
	}

	[Test]
	public async Task PendingInitialization_CompletesAndIsCachedOnceCompleted()
	{
		using GateContainer.Root container = new();

		IYieldingGate resolved = await container.ResolveAsync<IYieldingGate>(Ct);
		Task<IYieldingGate> completed = container.ResolveAsync<IYieldingGate>(Ct);
		Task<IYieldingGate> cached = container.ResolveAsync<IYieldingGate>(Ct);

		await That(await completed).IsSameAs(resolved);
		await That((object)cached).IsSameAs(completed).Because("the task is cached once the initialization has completed");
	}

	[Test]
	public async Task FaultedInitialization_IsNotCached()
	{
		using FlakyContainer.Root container = new();

		await That(() => container.ResolveAsync<IFlakyGate>(Ct)).Throws<InvalidOperationException>();
		IFlakyGate second = await container.ResolveAsync<IFlakyGate>(Ct);

		await That(second.Initialized).IsTrue().Because("a faulted task is not cached, so the retry initializes a fresh instance");
	}

	[Test]
	public async Task CanceledInitialization_IsNotCached()
	{
		using GateContainer.Root container = new();
		using CancellationTokenSource cts = new();
		cts.Cancel();

		Task<IYieldingGate> canceled = container.ResolveAsync<IYieldingGate>(cts.Token);
		await That(() => canceled).Throws<OperationCanceledException>();
		IYieldingGate second = await container.ResolveAsync<IYieldingGate>(Ct);

		await That(canceled.IsCanceled).IsTrue().Because("the cancellation token still reaches the initialization");
		await That(second.Initialized).IsTrue().Because("a canceled task is not cached, so a later resolve initializes successfully");
	}

	[Test]
	public async Task DisposedRoot_ThrowsSynchronously_AlthoughTheTaskIsCached()
	{
		GateContainer.Root container = new();
		using GateContainer.Scope scope = container.CreateScope();
		await container.ResolveAsync<Plain>(Ct);
		await container.ResolveAsync<IGate>(Ct);

		container.Dispose();

		await That(() => { _ = container.ResolveAsync<Plain>(Ct); }).Throws<ObjectDisposedException>();
		await That(() => { _ = scope.ResolveAsync<IGate>(Ct); }).Throws<ObjectDisposedException>()
			.Because("a child scope must not hand out a singleton of a disposed root from the cache");
	}

	[Test]
	public async Task DisposedScope_ThrowsSynchronously()
	{
		using GateContainer.Root container = new();
		GateContainer.Scope scope = container.CreateScope();
		await scope.ResolveAsync<Plain>(Ct);

		scope.Dispose();

		await That(() => { _ = scope.ResolveAsync<Plain>(Ct); }).Throws<ObjectDisposedException>();
		await That(() => { _ = scope.ResolveAsync<IYieldingGate>(Ct); }).Throws<ObjectDisposedException>();
	}

	public interface IGate;

	public sealed class Gate : IGate, IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public interface IYieldingGate
	{
		bool Initialized { get; }
	}

	public sealed class YieldingGate : IYieldingGate, IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			Initialized = true;
		}
	}

	public interface IFlakyGate
	{
		bool Initialized { get; }
	}

	public sealed class Attempts
	{
		public int Count { get; set; }
	}

	public sealed class FlakyGate(Attempts attempts) : IFlakyGate, IAsyncInitializable
	{
		public bool Initialized { get; private set; }

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			await Task.Yield();
			if (++attempts.Count == 1)
			{
				throw new InvalidOperationException("first attempt fails");
			}

			Initialized = true;
		}
	}

	public sealed class Plain;

	[Container]
	[Singleton<Gate, IGate>]
	[Singleton<YieldingGate, IYieldingGate>]
	[Singleton<Plain>]
	public static partial class GateContainer;

	[Container]
	[Singleton<FlakyGate, IFlakyGate>]
	[Singleton<Attempts>]
	public static partial class FlakyContainer;
}
