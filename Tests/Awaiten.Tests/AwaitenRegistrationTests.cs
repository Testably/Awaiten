using System.Linq;
using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Value semantics of <see cref="AwaitenRegistration" /> and its two constructors: the synchronous form the
///     generator emits for an ordinary service, and the asynchronous form carrying the closed <c>Task&lt;T&gt;</c>
///     type and converter the bridge projects an async-tainted service under. Equality is by the advertised
///     identity (service type, lifetime, async-ness, ownership, suppressed disposal, key); the derived async
///     projection members take no part in it.
/// </summary>
public sealed partial class AwaitenRegistrationTests
{
	private interface IFoo;

	[Test]
	public async Task SyncConstructor_NullServiceType_Throws()
		=> await That(() => _ = new AwaitenRegistration(null!, Lifetime.Singleton)).Throws<ArgumentNullException>();

	[Test]
	public async Task SyncConstructor_ExposesCoreMembers()
	{
		AwaitenRegistration registration = new(typeof(IFoo), Lifetime.Scoped, externallyOwned: true, key: "k");

		await That(registration.ServiceType).IsEqualTo(typeof(IFoo));
		await That(registration.Lifetime).IsEqualTo(Lifetime.Scoped);
		await That(registration.RequiresAsync).IsFalse();
		await That(registration.ExternallyOwned).IsTrue();
		await That(registration.Key).IsEqualTo("k");
		await That(registration.AsyncTaskType).IsNull();
		await That(registration.AsyncTaskConverter).IsNull();
	}

	[Test]
	public async Task FullSyncConstructor_ExposesDisposalSuppressed()
	{
		AwaitenRegistration registration = new(typeof(IFoo), Lifetime.Transient, externallyOwned: false, key: null, disposalSuppressed: true);

		await That(registration.DisposalSuppressed).IsTrue();
		await That(registration.ExternallyOwned).IsFalse();
		await That(registration.RequiresAsync).IsFalse();
	}

	[Test]
	public async Task AsyncConstructor_ExposesTheProjectionMembers()
	{
		Func<Task<object>, object> converter = AwaitenTaskProjection.AsTask<IFoo>;
		AwaitenRegistration registration =
			new(typeof(IFoo), Lifetime.Singleton, typeof(Task<IFoo>), converter, key: "k");

		await That(registration.RequiresAsync).IsTrue();
		await That(registration.ExternallyOwned).IsFalse();
		await That(registration.AsyncTaskType).IsEqualTo(typeof(Task<IFoo>));
		await That(registration.AsyncTaskConverter).IsSameAs(converter);
		await That(registration.Key).IsEqualTo("k");
	}

	[Test]
	public async Task AsyncConstructor_NullServiceType_Throws()
		=> await That(() => _ = new AwaitenRegistration(
				null!, Lifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>))
			.Throws<ArgumentNullException>();

	[Test]
	public async Task AsyncConstructor_NullTaskType_Throws()
		=> await That(() => _ = new AwaitenRegistration(
				typeof(IFoo), Lifetime.Singleton, null!, AwaitenTaskProjection.AsTask<IFoo>))
			.Throws<ArgumentNullException>();

	[Test]
	public async Task AsyncConstructor_NullConverter_Throws()
		=> await That(() => _ = new AwaitenRegistration(
				typeof(IFoo), Lifetime.Singleton, typeof(Task<IFoo>), null!))
			.Throws<ArgumentNullException>();

	[Test]
	public async Task Equals_TrueForSameIdentity()
	{
		AwaitenRegistration a = new(typeof(IFoo), Lifetime.Singleton, key: "k");
		AwaitenRegistration b = new(typeof(IFoo), Lifetime.Singleton, key: "k");

		await That(a.Equals(b)).IsTrue();
		await That(a.Equals((object)b)).IsTrue();
		await That(a == b).IsTrue();
		await That(a != b).IsFalse();
		await That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Test]
	public async Task Equals_IgnoresTheDerivedAsyncProjectionMembers()
	{
		// Two async registrations that agree on identity are equal even though each carries its own converter
		// delegate instance; the projection members are derived from the service type and take no part in equality.
		AwaitenRegistration a =
			new(typeof(IFoo), Lifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>);
		AwaitenRegistration b =
			new(typeof(IFoo), Lifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>);

		await That(a == b).IsTrue();
		await That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Test]
	public async Task Equals_FalseForDifferingLifetime()
	{
		AwaitenRegistration a = new(typeof(IFoo), Lifetime.Singleton);
		AwaitenRegistration b = new(typeof(IFoo), Lifetime.Scoped);

		await That(a == b).IsFalse();
		await That(a != b).IsTrue();
	}

	[Test]
	public async Task Equals_FalseForDifferingAsyncness()
	{
		AwaitenRegistration sync = new(typeof(IFoo), Lifetime.Singleton);
		AwaitenRegistration async =
			new(typeof(IFoo), Lifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>);

		await That(sync.Equals(async)).IsFalse();
	}

	[Test]
	public async Task Equals_FalseForDifferingOwnership()
	{
		AwaitenRegistration owned = new(typeof(IFoo), Lifetime.Singleton, externallyOwned: true);
		AwaitenRegistration constructed = new(typeof(IFoo), Lifetime.Singleton);

		await That(owned.Equals(constructed)).IsFalse();
	}

	[Test]
	public async Task Equals_FalseForDifferingDisposalSuppression()
	{
		AwaitenRegistration suppressed = new(typeof(IFoo), Lifetime.Singleton, externallyOwned: false, key: null, disposalSuppressed: true);
		AwaitenRegistration disposed = new(typeof(IFoo), Lifetime.Singleton);

		await That(suppressed.Equals(disposed)).IsFalse();
	}

	[Test]
	public async Task Equals_FalseForDifferingKey()
	{
		AwaitenRegistration a = new(typeof(IFoo), Lifetime.Singleton, key: "a");
		AwaitenRegistration b = new(typeof(IFoo), Lifetime.Singleton, key: "b");

		await That(a.Equals(b)).IsFalse();
	}

	[Test]
	public async Task Equals_Object_FalseForOtherType()
	{
		AwaitenRegistration registration = new(typeof(IFoo), Lifetime.Singleton);

		await That(registration.Equals("not a registration")).IsFalse();
	}

	/// <summary>
	///     What a generated container advertises as <see cref="AwaitenRegistration.DisposalSuppressed" />: a
	///     synchronously resolvable, possibly disposable service built with <c>SuppressDisposal = true</c>, and nothing else.
	/// </summary>
	public sealed class Advertised
	{
		[Test]
		[Arguments(typeof(SuppressedDisposable), true)]
		[Arguments(typeof(IHidden), true)]
		[Arguments(typeof(SuppressedPlain), false)]
		[Arguments(typeof(Disposable), false)]
		[Arguments(typeof(SuppressedAsync), false)]
		public async Task DisposalSuppressed_IsSetOnlyForASyncSuppressedPossiblyDisposableService(Type serviceType, bool expected)
		{
			using AdvertisingContainer.Root root = new();

			AwaitenRegistration registration = ((IAwaitenRoot)root).Registrations.Single(r => r.ServiceType == serviceType);

			await That(registration.DisposalSuppressed).IsEqualTo(expected)
				.Because("only a synchronously projected instance that may be disposable needs the host to keep its hands off");
		}

		[Test]
		public async Task DisposalSuppressed_IsSetOnAKeyedRegistrationAndKeepsTheKey()
		{
			using AdvertisingContainer.Root root = new();

			AwaitenRegistration registration = ((IAwaitenRoot)root).Registrations.Single(r => Equals(r.Key, "pool"));

			await That(registration.DisposalSuppressed).IsTrue();
			await That(registration.ServiceType).IsEqualTo(typeof(KeyedDisposable));
		}
	}

	public sealed class SuppressedDisposable : IDisposable
	{
		public void Dispose()
		{
		}
	}

	public interface IHidden;

	private sealed class Hidden : IHidden;

	public sealed class SuppressedPlain;

	public sealed class Disposable : IDisposable
	{
		public void Dispose()
		{
		}
	}

	public sealed class SuppressedAsync : IAsyncInitializable, IDisposable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public void Dispose()
		{
		}
	}

	public sealed class KeyedDisposable : IDisposable
	{
		public void Dispose()
		{
		}
	}

	[Container]
	[Singleton<SuppressedDisposable>(SuppressDisposal = true)]
	[Singleton<IHidden>(Factory = nameof(MakeHidden), SuppressDisposal = true)]
	[Singleton<SuppressedPlain>(SuppressDisposal = true)]
	[Singleton<Disposable>]
	[Singleton<SuppressedAsync>(SuppressDisposal = true)]
	[Transient<KeyedDisposable>(SuppressDisposal = true, Key = "pool")]
	public static partial class AdvertisingContainer
	{
#pragma warning disable CA1859
		// Declared as the interface on purpose: a factory whose declared type may hide a disposable counts as possibly disposable.
		private static IHidden MakeHidden() => new Hidden();
#pragma warning restore CA1859
	}
}
