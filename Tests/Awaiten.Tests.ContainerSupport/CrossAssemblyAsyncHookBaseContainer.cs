using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.ContainerSupport;

/// <summary>A service the production container builds from a private implementation and connects asynchronously.</summary>
public interface ISupportAsyncTerminal
{
	/// <summary>The token the base's private asynchronous hook received, or <see langword="null" /> before it ran.</summary>
	CancellationToken? Token { get; }
}

/// <summary>
///     A production container in a referenced assembly whose private implementation has an asynchronous activation hook,
///     so a test container in another assembly awaits it through the internal wrapper the base generates.
/// </summary>
[Container]
[Singleton<Terminal, ISupportAsyncTerminal>(OnActivated = nameof(Connect))]
public static partial class SupportAsyncHookBaseContainer
{
	private static async Task Connect(Terminal terminal, CancellationToken cancellationToken)
	{
		await Task.Yield();
		terminal.Token = cancellationToken;
	}

	private sealed class Terminal : ISupportAsyncTerminal
	{
		public CancellationToken? Token { get; set; }
	}
}
