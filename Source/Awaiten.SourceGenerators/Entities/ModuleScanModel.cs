using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The equatable model of a <c>[Module]</c> that compiles its own <c>[Scan]</c>: the partial class to
///     re-open and the factories to emit into it, one per scan match. The generator adds a partial declaration
///     carrying a generated <c>public static</c> factory per match (which <c>new</c>s the — possibly
///     <c>internal</c> — implementation and returns its accessible exposure interface) plus a
///     <c>[GeneratedScanRegistration]</c> referencing that factory. A consuming container reads that attribute
///     like a container <c>[Scan]</c> match, so the matches collect and are overridable, as close to a container
///     scan as the assembly boundary allows.
/// </summary>
internal sealed record ModuleScanModel(
	string? Namespace,
	EquatableArray<TypeDeclaration> ContainingTypes,
	string TypeName,
	string HintName,
	bool Expanded,
	EquatableArray<ModuleFactory> Factories,
	EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
///     One generated module-scan factory: the emitted method name, the accessible exposure interface it returns
///     (also the service the registration exposes), the concrete implementation it constructs, the lifetime and
///     <c>SkipUnconstructable</c> opt-in the scan declared, and the constructor parameters mirrored onto the
///     factory signature (resolved from the consuming container's graph, exactly like a hand-written module
///     factory's parameters).
/// </summary>
internal sealed record ModuleFactory(
	string FactoryName,
	string ServiceType,
	string ImplementationType,
	Lifetime Lifetime,
	bool SkipUnconstructable,
	EquatableArray<FactoryParameter> Parameters,
	ModuleHook? OnActivated = null,
	ModuleHook? OnRelease = null);

/// <summary>A mirrored constructor parameter on a generated factory: its fully-qualified type and its name.</summary>
internal readonly record struct FactoryParameter(string Type, string Name);

/// <summary>
///     One generated lifecycle-hook wrapper the module emits beside a match's factory. The module resolves and
///     (for a generic hook) closes its own <c>OnActivated</c>/<c>OnRelease</c> hook at its build, then emits a
///     <c>public static void</c> wrapper so a consumer can run it without naming the module's (possibly
///     <c>internal</c>) hook. The wrapper takes the accessible exposure interface as its first parameter, casts it
///     to <see cref="InstanceCast" /> (the concrete implementation, or the closed marker form for a generic hook)
///     and forwards it, plus <see cref="Parameters" /> (mirrored like factory parameters and resolved from the
///     consumer's graph), to <see cref="Target" /> - the user hook, carrying its bound type arguments for a generic
///     one. The consumer reads <see cref="WrapperName" /> off the <c>[GeneratedScanRegistration]</c> and runs it
///     through the ordinary hook pipeline.
/// </summary>
internal sealed record ModuleHook(
	string WrapperName,
	string Target,
	string InstanceCast,
	EquatableArray<FactoryParameter> Parameters);
