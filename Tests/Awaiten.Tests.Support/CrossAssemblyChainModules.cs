namespace Awaiten.Tests.Support;

/// <summary>A user of an internal kettle that one module registers, built from an internal bean a third module registers.</summary>
public interface IChainUser
{
	/// <summary>The internal bean the kettle was constructed with.</summary>
	object Bean { get; }
}

internal interface IChainKettle
{
	object Bean { get; }
}

internal sealed class ChainBean;

internal sealed class ChainKettle(ChainBean bean) : IChainKettle
{
	public object Bean => bean;
}

internal sealed class ChainUser(IChainKettle kettle) : IChainUser
{
	public object Bean => kettle.Bean;
}

/// <summary>Registers the internal kettle, which takes the internal bean of <see cref="CrossAssemblyChainBeanModule" />.</summary>
[Module]
[Singleton<ChainKettle, IChainKettle>]
public static partial class CrossAssemblyChainKettleModule;

/// <summary>Registers the public user, which takes the internal kettle of <see cref="CrossAssemblyChainKettleModule" />.</summary>
[Module]
[Singleton<ChainUser, IChainUser>]
public static partial class CrossAssemblyChainUserModule;

/// <summary>Registers the internal bean.</summary>
[Module]
[Singleton<ChainBean>]
public static partial class CrossAssemblyChainBeanModule;
