namespace Awaiten.Tests.Support;

/// <summary>A user of one implementation under two internal services.</summary>
public interface ITwoServicesUser
{
	/// <summary>The instance behind the first internal service.</summary>
	object First { get; }

	/// <summary>The instance behind the second internal service.</summary>
	object Second { get; }
}

internal interface ITwoServicesFirst;

internal interface ITwoServicesSecond;

internal sealed class TwoServicesImpl : ITwoServicesFirst, ITwoServicesSecond;

internal sealed class TwoServicesUser(ITwoServicesFirst first, ITwoServicesSecond second) : ITwoServicesUser
{
	public object First => first;

	public object Second => second;
}

internal sealed class TwoServicesDisposable : ITwoServicesFirst, ITwoServicesSecond, System.IDisposable
{
	public void Dispose() { }
}

/// <summary>Registers one singleton under two internal services, which the public user takes both of.</summary>
[Module]
[Singleton<TwoServicesImpl, ITwoServicesFirst>]
[Singleton<TwoServicesImpl, ITwoServicesSecond>]
[Transient<TwoServicesUser, ITwoServicesUser>]
public static partial class CrossAssemblyTwoInternalServicesModule;

/// <summary>Registers one transient disposable under two internal services, which the public user takes both of.</summary>
[Module]
[Transient<TwoServicesDisposable, ITwoServicesFirst>]
[Transient<TwoServicesDisposable, ITwoServicesSecond>]
[Transient<TwoServicesUser, ITwoServicesUser>]
public static partial class CrossAssemblyTwoInternalTransientServicesModule;
