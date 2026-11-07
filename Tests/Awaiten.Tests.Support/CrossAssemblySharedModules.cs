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

/// <summary>A service both <see cref="CrossAssemblySharedMenuModule" /> and <see cref="CrossAssemblySharedPriceModule" /> register their board under.</summary>
public interface ISharedBoard;

/// <summary>A service only <see cref="CrossAssemblySharedMenuModule" /> registers the board under.</summary>
public interface ISharedMenu;

/// <summary>A service only <see cref="CrossAssemblySharedPriceModule" /> registers the board under.</summary>
public interface ISharedPrices;

internal sealed class SharedBoard : ISharedBoard, ISharedMenu, ISharedPrices;

/// <summary>A <c>[Module]</c> that registers the internal board under two public services no type covers.</summary>
[Module]
[Singleton<SharedBoard, ISharedBoard>]
[Singleton<SharedBoard, ISharedMenu>]
public static partial class CrossAssemblySharedMenuModule;

/// <summary>A <c>[Module]</c> that registers the internal board under two other public services no type covers.</summary>
[Module]
[Singleton<SharedBoard, ISharedBoard>]
[Singleton<SharedBoard, ISharedPrices>]
public static partial class CrossAssemblySharedPriceModule;

/// <summary>A public service <see cref="CrossAssemblySharedTillModule" /> registers its till under.</summary>
public interface ISharedTill;

/// <summary>A public service <see cref="CrossAssemblySharedCashModule" /> registers the same till under.</summary>
public interface ISharedCash;

internal interface ISharedTillDrawer;

internal interface ISharedCashDrawer;

internal sealed class SharedTill : ISharedTill, ISharedCash, ISharedTillDrawer, ISharedCashDrawer;

/// <summary>A <c>[Module]</c> that registers the internal till under an internal and a public service.</summary>
[Module]
[Singleton<SharedTill, ISharedTillDrawer>]
[Singleton<SharedTill, ISharedTill>]
public static partial class CrossAssemblySharedTillModule;

/// <summary>A <c>[Module]</c> that registers the internal till under another internal and another public service.</summary>
[Module]
[Singleton<SharedTill, ISharedCashDrawer>]
[Singleton<SharedTill, ISharedCash>]
public static partial class CrossAssemblySharedCashModule;

/// <summary>A public service <see cref="CrossAssemblySharedSafeModule" /> registers the safe under alone.</summary>
public interface ISharedSafe;

/// <summary>A public service <see cref="CrossAssemblySharedVaultModule" /> registers the safe under beside an internal one.</summary>
public interface ISharedVault;

internal interface ISharedLocker;

internal sealed class SharedSafe : ISharedSafe, ISharedVault, ISharedLocker;

/// <summary>A <c>[Module]</c> that registers the internal safe under a single public service only.</summary>
[Module]
[Singleton<SharedSafe, ISharedSafe>]
public static partial class CrossAssemblySharedSafeModule;

/// <summary>A <c>[Module]</c> that registers the internal safe under an internal and a public service.</summary>
[Module]
[Singleton<SharedSafe, ISharedLocker>]
[Singleton<SharedSafe, ISharedVault>]
public static partial class CrossAssemblySharedVaultModule;