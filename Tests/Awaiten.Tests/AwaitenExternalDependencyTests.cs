namespace Awaiten.Tests;

/// <summary>
///     Value semantics of <see cref="AwaitenExternalDependency" />: it is the (service type, optional key) pair a
///     host verifies against its provider, so equality and hashing must be by value with the key participating.
/// </summary>
public sealed class AwaitenExternalDependencyTests
{
	[Fact]
	public async Task Constructor_NullServiceType_Throws()
		=> await That(() => _ = new AwaitenExternalDependency(null!)).Throws<ArgumentNullException>();

	[Fact]
	public async Task Constructor_ExposesServiceTypeAndKey()
	{
		AwaitenExternalDependency dependency = new(typeof(int), "utc");

		await That(dependency.ServiceType).IsEqualTo(typeof(int));
		await That(dependency.Key).IsEqualTo("utc");
	}

	[Fact]
	public async Task Key_IsNullWhenOmitted()
	{
		AwaitenExternalDependency dependency = new(typeof(int));

		await That(dependency.Key).IsNull();
	}

	[Fact]
	public async Task Equals_TrueForSameServiceTypeAndKey()
	{
		AwaitenExternalDependency a = new(typeof(int), "utc");
		AwaitenExternalDependency b = new(typeof(int), "utc");

		await That(a.Equals(b)).IsTrue();
		await That(a.Equals((object)b)).IsTrue();
		await That(a == b).IsTrue();
		await That(a != b).IsFalse();
		await That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Fact]
	public async Task Equals_TrueForTwoUnkeyedOfTheSameType()
	{
		AwaitenExternalDependency a = new(typeof(int));
		AwaitenExternalDependency b = new(typeof(int));

		await That(a == b).IsTrue();
		await That(a.GetHashCode()).IsEqualTo(b.GetHashCode())
			.Because("a null key hashes consistently");
	}

	[Fact]
	public async Task Equals_FalseForDifferentServiceType()
	{
		AwaitenExternalDependency a = new(typeof(int));
		AwaitenExternalDependency b = new(typeof(long));

		await That(a.Equals(b)).IsFalse();
		await That(a == b).IsFalse();
		await That(a != b).IsTrue();
	}

	[Fact]
	public async Task Equals_FalseForDifferentKey()
	{
		AwaitenExternalDependency a = new(typeof(int), "utc");
		AwaitenExternalDependency b = new(typeof(int), "local");

		await That(a.Equals(b)).IsFalse();
		await That(a != b).IsTrue();
	}

	[Fact]
	public async Task Equals_FalseWhenOneKeyIsNull()
	{
		AwaitenExternalDependency keyed = new(typeof(int), "utc");
		AwaitenExternalDependency unkeyed = new(typeof(int));

		await That(keyed.Equals(unkeyed)).IsFalse();
	}

	[Fact]
	public async Task Equals_Object_FalseForOtherType()
	{
		AwaitenExternalDependency dependency = new(typeof(int));

		await That(dependency.Equals("not a dependency")).IsFalse();
		await That(dependency.Equals(null)).IsFalse();
	}
}
