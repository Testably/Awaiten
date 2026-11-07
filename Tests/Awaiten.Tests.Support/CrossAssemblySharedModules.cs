using System;

namespace Awaiten.Tests.Support;

/// <summary>A chef whose internal implementation takes the internal pantry another module registers.</summary>
public interface ISharedChef
{
	/// <summary>The internal pantry the chef received.</summary>
	object Pantry { get; }
}

/// <summary>A bartender whose module registers the same internal pantry as the storage module.</summary>
public interface ISharedBartender
{
	/// <summary>The internal pantry the bartender received.</summary>
	object Pantry { get; }

	/// <summary>How often the container disposed the pantry.</summary>
	int PantryDisposals { get; }
}

internal sealed class SharedPantry : IDisposable
{
	public int Disposals { get; private set; }

	public void Dispose() => Disposals++;
}

internal sealed class SharedChef(SharedPantry pantry) : ISharedChef
{
	public object Pantry => pantry;
}

internal sealed class SharedBartender(SharedPantry pantry) : ISharedBartender
{
	public object Pantry => pantry;

	public int PantryDisposals => pantry.Disposals;
}

/// <summary>A <c>[Module]</c> that registers the internal pantry.</summary>
[Module]
[Singleton<SharedPantry>]
public static partial class CrossAssemblySharedStorageModule;

/// <summary>A <c>[Module]</c> whose chef takes the internal pantry, which only the storage module registers.</summary>
[Module]
[Singleton<SharedChef, ISharedChef>]
public static partial class CrossAssemblySharedKitchenModule;

/// <summary>A <c>[Module]</c> that registers the internal pantry too, for its bartender.</summary>
[Module]
[Singleton<SharedPantry>]
[Singleton<SharedBartender, ISharedBartender>]
public static partial class CrossAssemblySharedBarModule;