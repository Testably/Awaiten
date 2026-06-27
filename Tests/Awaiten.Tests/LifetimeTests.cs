using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
// ReSharper disable AccessToDisposedClosure

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of scopes, lifetimes, disposal and multi-service single-instance
///     registration. The containers and services are nested types, so the enclosing class is
///     <c>partial</c>.
/// </summary>
public partial class LifetimeTests
{
	[Fact]
	public async Task Scoped_ReturnsOneInstancePerScope()
	{
		using LifetimeContainer container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		IScopedService a = scope1.Resolve<IScopedService>();
		IScopedService b = scope1.Resolve<IScopedService>();
		IScopedService c = scope2.Resolve<IScopedService>();

		await That(a).IsSameAs(b);
		await That(a).IsNotSameAs(c);
	}

	[Fact]
	public async Task Singleton_IsSharedAcrossScopesAndTheContainer()
	{
		using LifetimeContainer container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		ISingletonService fromContainer = container.Resolve<ISingletonService>();

		await That(scope1.Resolve<ISingletonService>()).IsSameAs(fromContainer);
		await That(scope2.Resolve<ISingletonService>()).IsSameAs(fromContainer);
	}

	[Fact]
	public async Task DisposingAScope_DisposesItsScopedInstances()
	{
		using LifetimeContainer container = new();
		ScopedService scoped;
		using (IAwaitenScope scope = container.CreateScope())
		{
			scoped = (ScopedService)scope.Resolve<IScopedService>();
			await That(scoped.Disposed).IsFalse();
		}

		await That(scoped.Disposed).IsTrue();
	}

	[Fact]
	public async Task DisposingAScope_DisposesTransientsCreatedWithinIt()
	{
		using LifetimeContainer container = new();
		TransientService transient;
		using (IAwaitenScope scope = container.CreateScope())
		{
			transient = scope.Resolve<TransientService>();
		}

		await That(transient.Disposed).IsTrue();
	}

	[Fact]
	public async Task DisposingTheContainer_DisposesSingletons()
	{
		SingletonService singleton;
		using (LifetimeContainer container = new())
		{
			singleton = (SingletonService)container.Resolve<ISingletonService>();
			await That(singleton.Disposed).IsFalse();
		}

		await That(singleton.Disposed).IsTrue();
	}

	[Fact]
	public async Task DisposingTheContainer_DisposesInReverseOrderOfCreation()
	{
		DisposalRecorder recorder;
		using (DisposalOrderContainer container = new())
		{
			recorder = container.Resolve<DisposalRecorder>();
			container.Resolve<Beta>();
		}

		// Beta is built after Alpha, so it is disposed first.
		await That(recorder.Order).HasCount(2);
		await That(recorder.Order[0]).IsEqualTo("Beta");
		await That(recorder.Order[1]).IsEqualTo("Alpha");
	}

	[Fact]
	public async Task MultiServiceRegistration_ResolvesOneSharedInstance()
	{
		using MultiServiceContainer container = new();

		IReader reader = container.Resolve<IReader>();
		IWriter writer = container.Resolve<IWriter>();

		await That((object)reader).IsSameAs(writer);
	}

	[Fact]
	public async Task Singleton_ResolvedConcurrently_IsCreatedExactlyOnce()
	{
		using ConcurrencyContainer container = new();

		const int threadCount = 64;
		CountedSingleton[] results = new CountedSingleton[threadCount];
		using ManualResetEventSlim start = new(false);
		Task[] workers = new Task[threadCount];
		for (int t = 0; t < threadCount; t++)
		{
			int index = t;
			workers[index] = Task.Run(() =>
			{
				start.Wait();
				results[index] = container.Resolve<CountedSingleton>();
			}, TestContext.Current.CancellationToken);
		}

		start.Set();
		await Task.WhenAll(workers);

		CountedSingleton first = results[0];
		await That(results).All().ComplyWith(r => r.IsSameAs(first))
			.Because("every thread observes the one cached singleton");
		await That(container.Resolve<ConstructionCounter>().Count).IsEqualTo(1)
			.Because("the singleton is constructed exactly once under the lock");
	}

	[Fact]
	public async Task Transient_ResolvedConcurrently_TracksEveryInstanceForDisposal()
	{
		const int threadCount = 16;
		const int perThread = 200;
		ConcurrentBag<CountedTransient> produced = new();
		CountedTransient[] captured;

		using (ConcurrencyContainer container = new())
		{
			using ManualResetEventSlim start = new(false);
			Task[] workers = new Task[threadCount];
			for (int t = 0; t < threadCount; t++)
			{
				workers[t] = Task.Run(() =>
				{
					start.Wait();
					for (int i = 0; i < perThread; i++)
					{
						produced.Add(container.Resolve<CountedTransient>());
					}
				}, TestContext.Current.CancellationToken);
			}

			start.Set();
			await Task.WhenAll(workers);

			captured = produced.ToArray();
			await That(captured.Length).IsEqualTo(threadCount * perThread)
				.Because("every resolution returns a fresh transient");
			await That(captured.All(c => !c.Disposed)).IsTrue()
				.Because("tracked transients are not disposed until the owner is");
		}

		// If the unsynchronized tracking list raced, some instances would be lost (never disposed) or
		// the resolution would have thrown; both surface here.
		await That(captured.All(c => c.Disposed)).IsTrue()
			.Because("every concurrently tracked transient is disposed with the container");
	}

	[Fact]
	public async Task ResolvingFromADisposedContainer_Throws()
	{
		LifetimeContainer container = new();
		container.Dispose();

		await That(() => container.Resolve<ISingletonService>()).Throws<ObjectDisposedException>();
	}

	[Fact]
	public async Task ResolvingFromADisposedScope_Throws()
	{
		using LifetimeContainer container = new();
		IAwaitenScope scope = container.CreateScope();
		scope.Dispose();

		await That(() => scope.Resolve<IScopedService>()).Throws<ObjectDisposedException>();
	}

	public interface ISingletonService;

	public sealed class SingletonService : ISingletonService, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public interface IScopedService;

	public sealed class ScopedService : IScopedService, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	public sealed class TransientService : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Singleton<SingletonService, ISingletonService>]
	[Scoped<ScopedService, IScopedService>]
	// AWT106: the disposable transient is registered deliberately so the tests can assert it is
	// tracked and disposed when its owning scope or the container is disposed.
#pragma warning disable AWT106
	[Transient<TransientService>]
#pragma warning restore AWT106
	public partial class LifetimeContainer;

	public interface IReader;

	public interface IWriter;

	public sealed class Store : IReader, IWriter;

	[Container]
	[Singleton<Store, IReader>]
	[Singleton<Store, IWriter>]
	public partial class MultiServiceContainer;

	public sealed class DisposalRecorder
	{
		public List<string> Order { get; } = new();
	}

	public sealed class Alpha : IDisposable
	{
		private readonly DisposalRecorder _recorder;

		public Alpha(DisposalRecorder recorder) => _recorder = recorder;

		public void Dispose() => _recorder.Order.Add("Alpha");
	}

	public sealed class Beta : IDisposable
	{
		private readonly DisposalRecorder _recorder;

		// ReSharper disable once UnusedParameter.Local
		public Beta(DisposalRecorder recorder, Alpha alpha) => _recorder = recorder;

		public void Dispose() => _recorder.Order.Add("Beta");
	}

	[Container]
	[Singleton<DisposalRecorder>]
	[Singleton<Alpha>]
	[Singleton<Beta>]
	public partial class DisposalOrderContainer;

	public sealed class ConstructionCounter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Increment() => Interlocked.Increment(ref _count);
	}

	public sealed class CountedSingleton
	{
		public CountedSingleton(ConstructionCounter counter) => counter.Increment();
	}

	public sealed class CountedTransient : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Container]
	[Singleton<ConstructionCounter>]
	[Singleton<CountedSingleton>]
	// AWT106: the disposable transient is registered deliberately so the tests can assert every
	// concurrently tracked instance is disposed with the container.
#pragma warning disable AWT106
	[Transient<CountedTransient>]
#pragma warning restore AWT106
	public partial class ConcurrencyContainer;
}
