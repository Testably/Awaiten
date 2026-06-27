using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The Awaiten incremental source generator. Analyzes types annotated with
///     <c>[Container]</c> and emits the compile-time-verified container
///     implementation. This is a skeleton; generation logic is added in
///     subsequent commits.
/// </summary>
[Generator]
public sealed class AwaitenGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// TODO: register syntax/semantic providers for [Container] types,
		// run graph analysis (AWT1xx/2xx/3xx diagnostics), and emit the container.
	}
}
