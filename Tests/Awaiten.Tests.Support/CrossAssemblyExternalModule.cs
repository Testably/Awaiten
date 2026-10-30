namespace Awaiten.Tests.Support;

/// <summary>A generic host-owned service that the module below imports as an open generic definition.</summary>
public interface ISupportLog<T>
{
	/// <summary>The key the external resolver received, asserted on to prove the closing was routed.</summary>
	object? Key { get; }
}

/// <summary>A service the module registers whose dependency is a closing of the imported <see cref="ISupportLog{T}" />.</summary>
public sealed class SupportAuditor
{
	public SupportAuditor(ISupportLog<SupportAuditor> log) => Log = log;

	/// <summary>The closing drawn from the external resolver.</summary>
	public ISupportLog<SupportAuditor> Log { get; }
}

/// <summary>
///     A <c>[Module]</c> in a referenced assembly that imports an open generic definition, so an importing container
///     reads the <c>typeof(ISupportLog&lt;&gt;)</c> argument back from metadata rather than from source.
/// </summary>
[Module]
[ImportService(typeof(ISupportLog<>))]
[Transient<SupportAuditor>]
public static class CrossAssemblyExternalModule;
