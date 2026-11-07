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
	EquatableArray<MemberWrapper> Wrappers = default,
	EquatableArray<HiddenHandle> Handles = default,
	EquatableArray<HiddenAlias> Aliases = default,
	EquatableArray<HiddenInitializer> Initializers = default);

/// <summary>
///     The generated public handle of a service the module registers under an internal type: a class nested in the
///     module's <c>Internals</c> that holds the instance as <see cref="ValueType" /> behind an internal constructor and
///     property, so a container in another assembly depends on it without naming the service. A consumer registers it
///     as a dependency-only service. One sealed variant per disposal shape an implementation needs
///     (<see cref="Disposable" />, <see cref="AsyncDisposable" />, <see cref="BothDisposable" />) implements the
///     interfaces and forwards them, so a container disposes exactly what the implementation implements. With
///     <see cref="Wrap" />, a factory member's result may be any subtype, so the handle carries every variant and a
///     method that picks one by the instance's runtime type.
/// </summary>
internal sealed record HiddenHandle(string Name, string ValueType, bool Disposable, bool AsyncDisposable, bool BothDisposable, bool Wrap = false);

/// <summary>
///     A generated factory that serves one more service of an implementation registered under an internal service: it
///     takes the handle that owns the instance (with the key of its owning registration in <see cref="Attributes" />)
///     and returns <see cref="Body" />, the instance as <see cref="ReturnType" />, so every service shares one instance.
/// </summary>
internal sealed record HiddenAlias(string Name, string ReturnType, string HandleType, string? Attributes, string Body);

/// <summary>
///     A generated wrapper that runs one <c>[AsyncInitializer&lt;T&gt;]</c> convention of the module on the instance a
///     handle holds, so a container in another assembly initializes it without naming the service or the convention.
/// </summary>
internal sealed record HiddenInitializer(string Name, string HandleType, string ConventionType, string Method, string ReturnType, EquatableArray<FactoryParameter> Parameters);

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
///     properties of the same names through an object initializer. <see cref="Handle" /> is the handle type (or one of
///     its disposable variants) the factory wraps the instance in, for an implementation registered under an internal
///     service (see <see cref="HiddenHandle" />), and the hooks then take that handle. A factory over the module's own
///     <c>Factory</c> or <c>Instance</c> <see cref="Call" /> passes the member's result to <see cref="Handle" />, the
///     expression that wraps it, instead of constructing the implementation: awaited first for an <see cref="Awaited" />
///     task type, and as a property for an <see cref="IsProperty" /> instance. The last
///     <see cref="OptionalMembers" />.Count parameters are optional and fill their properties only when a consumer
///     passes a service, and each of the <see cref="DeferredMembers" /> gets a setter wrapper a consumer calls after
///     construction.
/// </summary>
internal sealed record RegistrationFactory(
	string FactoryName,
	string ReturnType,
	string ImplementationType,
	EquatableArray<FactoryParameter> Parameters,
	EquatableArray<RegistrationHook> Hooks,
	EquatableArray<string> Members = default,
	string? Handle = null,
	string? Call = null,
	string? Awaited = null,
	bool IsProperty = false,
	EquatableArray<string> OptionalMembers = default,
	EquatableArray<FactoryParameter> DeferredMembers = default);

/// <summary>One lifecycle-hook wrapper of a self-compiled module registration and whether it is the release hook.</summary>
internal sealed record RegistrationHook(ModuleHook Hook, bool Release);

/// <summary>
///     What a consumer in another assembly must know about a self-compiled implementation it cannot see: the types of
///     its owner's <c>[AsyncInitializer&lt;T&gt;]</c> conventions that run on it, by their fully qualified display, with
///     the name of each convention's method at the same position in <see cref="AsyncInitializerMethods" />, and whether
///     it implements <c>IDisposable</c> and <c>IAsyncDisposable</c>. A convention whose method another convention already
///     runs on the implementation is left out.
/// </summary>
internal readonly record struct ImplementationTraits(EquatableArray<string> AsyncInitializers, bool Disposable, bool AsyncDisposable, EquatableArray<string> AsyncInitializerMethods = default)
{
	/// <summary>Whether no convention matches the implementation and it implements neither interface.</summary>
	public bool IsEmpty => AsyncInitializers.Count == 0 && !Disposable && !AsyncDisposable;

	/// <summary>
	///     Whether the convention of <paramref name="type" /> with <paramref name="method" /> runs on the implementation,
	///     by its type alone when the methods are not recorded.
	/// </summary>
	public bool Runs(string type, string method)
	{
		string[] types = AsyncInitializers.AsArray();
		string[] methods = AsyncInitializerMethods.AsArray();
		for (int index = 0; index < types.Length; index++)
		{
			if (types[index] == type && (methods.Length != types.Length || methods[index] == method))
			{
				return true;
			}
		}

		return false;
	}
}

/// <summary>
///     One generated module-scan factory: the emitted method name, the accessible exposure interface it returns
///     (also the service the registration exposes), the concrete implementation it constructs, the lifetime and
///     <c>SkipUnconstructable</c> opt-in the scan declared, and the constructor parameters mirrored onto the
///     factory signature (resolved from the consuming container's graph, exactly like a hand-written module
///     factory's parameters). <see cref="Traits" /> tells the consumer how the match it cannot see is initialized
///     and disposed. <see cref="Skip" /> marks a match the module emits no factory for. <see cref="ImplementationMetadataName" />
///     lets the consumer share one instance with every other registration of the implementation.
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
	ScanSkip? Skip = null,
	string? ImplementationMetadataName = null);

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
///     resolves the parameter as the constructor would. <see cref="Argument" /> is the expression passed on in its
///     place, which unwraps a generated handle, or <see langword="null" /> to pass the parameter itself.
/// </summary>
internal readonly record struct FactoryParameter(string Type, string Name, string? Attributes = null, string? Argument = null)
{
	/// <summary>The expression the generated member passes on for this parameter.</summary>
	public string ArgumentExpression => Argument ?? $"@{Name}";
}

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
