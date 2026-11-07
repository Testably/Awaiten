using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>A sign whose internal decorator and composite are started by the module's async initializer convention.</summary>
public interface IConventionSign
{
	/// <summary>Whether the instance and everything it wraps was started.</summary>
	bool Started { get; }
}

internal sealed class PlainSign : IConventionSign
{
	public bool Started => true;
}

internal sealed class GlowingSign(IConventionSign inner) : IConventionSign, ISelfCompiledStartable
{
	private bool _started;

	public bool Started => _started && inner.Started;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_started = true;
		return Task.CompletedTask;
	}
}

/// <summary>A display whose internal composite is started by the module's async initializer convention.</summary>
public interface IConventionDisplay
{
	/// <summary>Whether the instance was started.</summary>
	bool Started { get; }
}

internal sealed class MenuDisplay : IConventionDisplay
{
	public bool Started => true;
}

internal sealed class AllDisplays(IEnumerable<IConventionDisplay> displays) : IConventionDisplay, ISelfCompiledStartable
{
	private bool _started;

	public bool Started => _started && displays.All(display => display.Started);

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_started = true;
		return Task.CompletedTask;
	}
}

/// <summary>
///     A <c>[Module]</c> whose internal decorator and composite another assembly builds through the generated factories,
///     and which its convention initializes there.
/// </summary>
[Module]
[AsyncInitializer<ISelfCompiledStartable>(nameof(ISelfCompiledStartable.StartAsync))]
[Singleton<PlainSign, IConventionSign>]
[Decorate<GlowingSign, IConventionSign>]
[Singleton<MenuDisplay, IConventionDisplay>]
[Composite<AllDisplays, IConventionDisplay>]
public static partial class ConventionShapeModule;
