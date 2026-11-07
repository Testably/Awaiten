using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The equatable model of a <c>[Module]</c> that compiles its own <c>[Scan]</c> or its registrations of
///     implementations another assembly cannot construct: the partial class to re-open and the factories to emit
///     into it. The generator adds a partial declaration carrying a generated <c>public static</c> factory per scan
///     match (which <c>new</c>s the — possibly <c>internal</c> — implementation and returns its accessible exposure
///     interface) plus a <c>[GeneratedScanRegistration]</c> referencing that factory. A consuming container reads
///     that attribute like a container <c>[Scan]</c> match, so the matches collect and are overridable, as close to
///     a container scan as the assembly boundary allows. Each self-compiled registration gets a factory of its own
///     (<see cref="Registrations" />), which a consumer finds by name from the module's own registration attribute.
///     <see cref="DeclaresScan" /> decides whether the <c>[GeneratedScanExpansion]</c> marker is emitted.
///     <see cref="Skipped" /> records each implementation whose factory the module leaves out.
///     <see cref="NotPartialMarker" /> names the internal type that records a module left to the assemblies granted
///     <c>InternalsVisibleTo</c> because it is not partial.
/// </summary>
internal sealed record ModuleScanModel(
	string? Namespace,
	EquatableArray<TypeDeclaration> ContainingTypes,
	string TypeName,
	string HintName,
	bool Expanded,
	EquatableArray<ModuleFactory> Factories,
	EquatableArray<DiagnosticInfo> Diagnostics,
	bool DeclaresScan = true,
	EquatableArray<RegistrationFactory> Registrations = default,
	EquatableArray<SkippedRegistration> Skipped = default,
	string? NotPartialMarker = null,
	EquatableArray<MemberWrapper> Wrappers = default);

/// <summary>
///     A generated member that forwards to a <c>Factory</c> or <c>Instance</c> member or a lifecycle hook of its owner
///     (a module or a base container) that a container in another assembly cannot call: a method taking
///     <see cref="Parameters" /> and calling <see cref="Target" /> with them, or, for <see cref="IsProperty" />, a
///     property returning it.
/// </summary>
internal sealed record MemberWrapper(
	string Name,
	string ReturnType,
	string Target,
	EquatableArray<FactoryParameter> Parameters,
	bool IsProperty = false);

/// <summary>
///     An implementation the module generates no factory for, because an assembly granted <c>InternalsVisibleTo</c>
///     constructs it itself: the name of the emitted constant and the reason it holds, which a consumer that cannot
///     construct the implementation reports. <see cref="Hook" /> marks a hook whose wrapper the module leaves out
///     instead, beside a factory it still emits.
/// </summary>
internal sealed record SkippedRegistration(string MarkerName, string Reason, bool Hook = false);

/// <summary>
///     The generated factory of an implementation that only its owner can construct: a self-compiled module
///     registration, or an implementation private to a base container that an extending container builds. It holds the
///     emitted method name, the accessible type it returns, the implementation it constructs, the constructor
///     parameters mirrored onto its signature and the lifecycle-hook wrappers emitted beside it, one per hook a
///     registration of the implementation names. The last <see cref="Members" />.Count parameters fill the injected
///     properties of the same names through an object initializer.
/// </summary>
internal sealed record RegistrationFactory(
	string FactoryName,
	string ReturnType,
	string ImplementationType,
	EquatableArray<FactoryParameter> Parameters,
	EquatableArray<RegistrationHook> Hooks,
	EquatableArray<string> Members = default);

/// <summary>One lifecycle-hook wrapper of a self-compiled module registration and whether it is the release hook.</summary>
internal sealed record RegistrationHook(ModuleHook Hook, bool Release);

/// <summary>
///     What a consumer in another assembly must know about a self-compiled implementation it cannot see: whether it
///     implements <c>IAsyncInitializable</c>, <c>IDisposable</c> and <c>IAsyncDisposable</c>.
/// </summary>
internal readonly record struct ImplementationTraits(bool AsyncInitializable, bool Disposable, bool AsyncDisposable)
{
	/// <summary>Whether the implementation implements none of the interfaces.</summary>
	public bool IsEmpty => !AsyncInitializable && !Disposable && !AsyncDisposable;
}

/// <summary>
///     One generated module-scan factory: the emitted method name, the accessible exposure interface it returns
///     (also the service the registration exposes), the concrete implementation it constructs, the lifetime and
///     <c>SkipUnconstructable</c> opt-in the scan declared, and the constructor parameters mirrored onto the
///     factory signature (resolved from the consuming container's graph, exactly like a hand-written module
///     factory's parameters). <see cref="Traits" /> tells the consumer how the match it cannot see is initialized
///     and disposed. <see cref="Skip" /> marks a match the module emits no factory for.
/// </summary>
internal sealed record ModuleFactory(
	string FactoryName,
	string ServiceType,
	string ImplementationType,
	Lifetime Lifetime,
	bool SkipUnconstructable,
	EquatableArray<FactoryParameter> Parameters,
	ModuleHook? OnActivated = null,
	ModuleHook? OnRelease = null,
	ImplementationTraits Traits = default,
	ScanSkip? Skip = null);

/// <summary>
///     A module-scan match the module emits no factory for, because an assembly granted <c>InternalsVisibleTo</c>
///     constructs it itself: the name of the constant that holds the reason, the match's metadata name, which each
///     <c>[GeneratedScanRegistration]</c> names in place of a factory so a consumer finds the match, and every service
///     the module's scans expose it as.
/// </summary>
internal sealed record ScanSkip(string MarkerName, string ImplementationMetadataName, string Reason, EquatableArray<string> Services);

/// <summary>
///     A mirrored constructor parameter on a generated factory: its fully-qualified type, its name and the
///     <c>[FromKey]</c> or <c>[Arg]</c> attribute it carries (<c>[global::Awaiten.FromKey("utc")] </c>), so a consumer
///     resolves the parameter as the constructor would.
/// </summary>
internal readonly record struct FactoryParameter(string Type, string Name, string? Attributes = null);

/// <summary>
///     One generated lifecycle-hook wrapper the module emits beside a match's factory. The module resolves and
///     (for a generic hook) closes its own <c>OnActivated</c>/<c>OnRelease</c> hook at its build, then emits a
///     <c>public static</c> wrapper so a consumer can run it without naming the module's (possibly
///     <c>internal</c>) hook. The wrapper takes the accessible exposure interface as its first parameter, casts it
///     to <see cref="InstanceCast" /> (the concrete implementation, or the closed marker form for a generic hook)
///     and forwards it, plus <see cref="Parameters" /> (mirrored like factory parameters and resolved from the
///     consumer's graph), to <see cref="Target" /> - the user hook, carrying its bound type arguments for a generic
///     one. The consumer reads <see cref="WrapperName" /> off the <c>[GeneratedScanRegistration]</c> and runs it
///     through the ordinary hook pipeline. <see cref="ReturnType" /> is the user hook's (<c>void</c> for most), so the
///     wrapper of an asynchronous hook returns its task, and forwards a mirrored <c>CancellationToken</c> parameter,
///     for the consumer to await; a <c>void</c> wrapper would silently discard the task.
///     A hook with a <see cref="SkipReason" /> gets no wrapper, because an assembly granted <c>InternalsVisibleTo</c>
///     calls it directly: <see cref="WrapperName" /> is then the user hook's own name.
/// </summary>
internal sealed record ModuleHook(
	string WrapperName,
	string Target,
	string InstanceCast,
	EquatableArray<FactoryParameter> Parameters,
	string ReturnType,
	string? SkipReason = null);
