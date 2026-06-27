using System.Collections.Generic;

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

		IScopedService a = scope1.Get<IScopedService>();
		IScopedService b = scope1.Get<IScopedService>();
		IScopedService c = scope2.Get<IScopedService>();

		await That(a).IsSameAs(b);
		await That(ReferenceEquals(a, c)).IsFalse();
	}

	[Fact]
	public async Task Singleton_IsSharedAcrossScopesAndTheContainer()
	{
		using LifetimeContainer container = new();
		using IAwaitenScope scope1 = container.CreateScope();
		using IAwaitenScope scope2 = container.CreateScope();

		ISingletonService fromContainer = container.Get<ISingletonService>();

		await That(scope1.Get<ISingletonService>()).IsSameAs(fromContainer);
		await That(scope2.Get<ISingletonService>()).IsSameAs(fromContainer);
	}

	[Fact]
	public async Task DisposingAScope_DisposesItsScopedInstances()
	{
		using LifetimeContainer container = new();
		ScopedService scoped;
		using (IAwaitenScope scope = container.CreateScope())
		{
			scoped = (ScopedService)scope.Get<IScopedService>();
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
			transient = scope.Get<TransientService>();
		}

		await That(transient.Disposed).IsTrue();
	}

	[Fact]
	public async Task DisposingTheContainer_DisposesSingletons()
	{
		SingletonService singleton;
		using (LifetimeContainer container = new())
		{
			singleton = (SingletonService)container.Get<ISingletonService>();
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
			recorder = container.Get<DisposalRecorder>();
			container.Get<Beta>();
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

		IReader reader = container.Get<IReader>();
		IWriter writer = container.Get<IWriter>();

		await That((object)reader).IsSameAs(writer);
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
	[Transient<TransientService>]
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

		public Beta(DisposalRecorder recorder, Alpha alpha) => _recorder = recorder;

		public void Dispose() => _recorder.Order.Add("Beta");
	}

	[Container]
	[Singleton<DisposalRecorder>]
	[Singleton<Alpha>]
	[Singleton<Beta>]
	public partial class DisposalOrderContainer;
}
