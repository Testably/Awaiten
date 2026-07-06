namespace Awaiten.Tests;

/// <summary>
///     Value semantics of <see cref="AwaitenRegistration" /> and its two constructors: the synchronous form the
///     generator emits for an ordinary service, and the asynchronous form carrying the closed <c>Task&lt;T&gt;</c>
///     type and converter the bridge projects an async-tainted service under. Equality is by the advertised
///     identity (service type, lifetime, async-ness, ownership, key); the derived async projection members take no
///     part in it.
/// </summary>
public sealed class AwaitenRegistrationTests
{
	private interface IFoo;

	[Fact]
	public async Task SyncConstructor_NullServiceType_Throws()
		=> await That(() => _ = new AwaitenRegistration(null!, AwaitenLifetime.Singleton)).Throws<ArgumentNullException>();

	[Fact]
	public async Task SyncConstructor_ExposesCoreMembers()
	{
		AwaitenRegistration registration = new(typeof(IFoo), AwaitenLifetime.Scoped, externallyOwned: true, key: "k");

		await That(registration.ServiceType).IsEqualTo(typeof(IFoo));
		await That(registration.Lifetime).IsEqualTo(AwaitenLifetime.Scoped);
		await That(registration.RequiresAsync).IsFalse();
		await That(registration.ExternallyOwned).IsTrue();
		await That(registration.Key).IsEqualTo("k");
		await That(registration.AsyncTaskType).IsNull();
		await That(registration.AsyncTaskConverter).IsNull();
	}

	[Fact]
	public async Task AsyncConstructor_ExposesTheProjectionMembers()
	{
		Func<Task<object>, object> converter = AwaitenTaskProjection.AsTask<IFoo>;
		AwaitenRegistration registration =
			new(typeof(IFoo), AwaitenLifetime.Singleton, typeof(Task<IFoo>), converter, key: "k");

		await That(registration.RequiresAsync).IsTrue();
		await That(registration.ExternallyOwned).IsFalse();
		await That(registration.AsyncTaskType).IsEqualTo(typeof(Task<IFoo>));
		await That(registration.AsyncTaskConverter).IsSameAs(converter);
		await That(registration.Key).IsEqualTo("k");
	}

	[Fact]
	public async Task AsyncConstructor_NullServiceType_Throws()
		=> await That(() => _ = new AwaitenRegistration(
				null!, AwaitenLifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>))
			.Throws<ArgumentNullException>();

	[Fact]
	public async Task AsyncConstructor_NullTaskType_Throws()
		=> await That(() => _ = new AwaitenRegistration(
				typeof(IFoo), AwaitenLifetime.Singleton, null!, AwaitenTaskProjection.AsTask<IFoo>))
			.Throws<ArgumentNullException>();

	[Fact]
	public async Task AsyncConstructor_NullConverter_Throws()
		=> await That(() => _ = new AwaitenRegistration(
				typeof(IFoo), AwaitenLifetime.Singleton, typeof(Task<IFoo>), null!))
			.Throws<ArgumentNullException>();

	[Fact]
	public async Task Equals_TrueForSameIdentity()
	{
		AwaitenRegistration a = new(typeof(IFoo), AwaitenLifetime.Singleton, key: "k");
		AwaitenRegistration b = new(typeof(IFoo), AwaitenLifetime.Singleton, key: "k");

		await That(a.Equals(b)).IsTrue();
		await That(a.Equals((object)b)).IsTrue();
		await That(a == b).IsTrue();
		await That(a != b).IsFalse();
		await That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Fact]
	public async Task Equals_IgnoresTheDerivedAsyncProjectionMembers()
	{
		// Two async registrations that agree on identity are equal even though each carries its own converter
		// delegate instance; the projection members are derived from the service type and take no part in equality.
		AwaitenRegistration a =
			new(typeof(IFoo), AwaitenLifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>);
		AwaitenRegistration b =
			new(typeof(IFoo), AwaitenLifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>);

		await That(a == b).IsTrue();
		await That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Fact]
	public async Task Equals_FalseForDifferingLifetime()
	{
		AwaitenRegistration a = new(typeof(IFoo), AwaitenLifetime.Singleton);
		AwaitenRegistration b = new(typeof(IFoo), AwaitenLifetime.Scoped);

		await That(a == b).IsFalse();
		await That(a != b).IsTrue();
	}

	[Fact]
	public async Task Equals_FalseForDifferingAsyncness()
	{
		AwaitenRegistration sync = new(typeof(IFoo), AwaitenLifetime.Singleton);
		AwaitenRegistration async =
			new(typeof(IFoo), AwaitenLifetime.Singleton, typeof(Task<IFoo>), AwaitenTaskProjection.AsTask<IFoo>);

		await That(sync.Equals(async)).IsFalse();
	}

	[Fact]
	public async Task Equals_FalseForDifferingOwnership()
	{
		AwaitenRegistration owned = new(typeof(IFoo), AwaitenLifetime.Singleton, externallyOwned: true);
		AwaitenRegistration constructed = new(typeof(IFoo), AwaitenLifetime.Singleton);

		await That(owned.Equals(constructed)).IsFalse();
	}

	[Fact]
	public async Task Equals_FalseForDifferingKey()
	{
		AwaitenRegistration a = new(typeof(IFoo), AwaitenLifetime.Singleton, key: "a");
		AwaitenRegistration b = new(typeof(IFoo), AwaitenLifetime.Singleton, key: "b");

		await That(a.Equals(b)).IsFalse();
	}

	[Fact]
	public async Task Equals_Object_FalseForOtherType()
	{
		AwaitenRegistration registration = new(typeof(IFoo), AwaitenLifetime.Singleton);

		await That(registration.Equals("not a registration")).IsFalse();
	}
}
