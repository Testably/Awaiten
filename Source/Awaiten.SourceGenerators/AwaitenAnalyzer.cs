using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Awaiten.SourceGenerators;

/// <summary>
///     Reports <see cref="Diagnostics.RootAccumulatingFactory">AWT118</see> for a root-owned instance (a
///     singleton or pre-built instance) that, directly or through its transitive transient dependencies,
///     holds a plain <c>Func&lt;…&gt;</c> over a build-on-demand service (a transient or parameterized service)
///     whose construction tracks a fresh disposable on the root - the produced service is itself disposable, or
///     it transitively rebuilds a disposable transient. Such a factory is bound to the root, so every instance
///     it builds (and the disposables built with it) is tracked on the root and accumulates for the container's
///     lifetime; a <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> hands each instance back as a disposal handle and is not
///     reported.
/// </summary>
/// <remarks>
///     AWT118 is an analyzer (rather than a generator) diagnostic so that, under loose lifetime safety where
///     it is a warning, it can be suppressed in source with <c>#pragma warning disable AWT118</c> or
///     <c>[SuppressMessage]</c> - a generator-reported diagnostic cannot. Under strict lifetime safety (the
///     default) it is instead reported through <see cref="Diagnostics.RootAccumulatingFactoryStrict" />: an
///     error carrying <see cref="WellKnownDiagnosticTags.NotConfigurable" />, so it cannot be suppressed by
///     <c>#pragma</c>, <c>&lt;NoWarn&gt;</c> or an editorconfig severity override - the only opt-out is
///     <c>LifetimeSafety.Loose</c>. That non-suppressibility is what keeps the leak structurally impossible
///     under strict lifetime safety. The graph it walks is built by the same
///     <c>AwaitenGenerator.BuildGraph</c> the generator uses.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitenAnalyzer : DiagnosticAnalyzer
{
	private const string ContainerAttributeName = "Awaiten.ContainerAttribute";

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		ImmutableArray.Create(Diagnostics.RootAccumulatingFactory, Diagnostics.RootAccumulatingFactoryStrict);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.RegisterCompilationStartAction(static start =>
		{
			INamedTypeSymbol? containerAttribute = start.Compilation.GetTypeByMetadataName(ContainerAttributeName);
			if (containerAttribute is null)
			{
				return;
			}

			start.RegisterSymbolAction(
				ctx => Analyze(
					(INamedTypeSymbol)ctx.Symbol, containerAttribute, start.Compilation, ctx.ReportDiagnostic, ctx.CancellationToken),
				SymbolKind.NamedType);
		});
	}

	private static void Analyze(
		INamedTypeSymbol type,
		INamedTypeSymbol containerAttribute,
		Compilation compilation,
		Action<Diagnostic> report,
		CancellationToken cancellationToken)
	{
		if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, containerAttribute)))
		{
			return;
		}

		// Re-derive the object graph; the registration diagnostics produced while building it are the
		// generator's to report, so they are collected into a throwaway list and discarded here. Strict
		// lifetime safety reports the root-accumulating pattern through the non-suppressible error descriptor,
		// loose reports the plain suppressible warning.
		GraphModel graph = AwaitenGenerator.BuildGraph(type, compilation, new List<DiagnosticInfo>(), cancellationToken);
		bool strict = AwaitenGenerator.ReadStrict(type);

		foreach (DiagnosticInfo diagnostic in Detect(graph, strict))
		{
			report(ToDiagnostic(diagnostic, compilation));
		}
	}

	// Builds the reportable diagnostic. The location is reconstructed against the compilation's actual syntax
	// tree (matched by file path) rather than via LocationInfo.ToLocation(), which yields an external location
	// detached from any tree - and a diagnostic without a source-tree location cannot be suppressed by an
	// in-source #pragma warning disable / [SuppressMessage].
	private static Diagnostic ToDiagnostic(DiagnosticInfo info, Compilation compilation)
	{
		Location location = Location.None;
		if (info.Location is { } source)
		{
			SyntaxTree? tree = compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath == source.FilePath);
			location = tree is null ? source.ToLocation() : Location.Create(tree, source.TextSpan);
		}

		object?[] args = info.MessageArgs.AsArray().Cast<object?>().ToArray();
		return info.Severity is { } severity
			? Diagnostic.Create(info.Descriptor, location, severity, additionalLocations: null, properties: null, args)
			: Diagnostic.Create(info.Descriptor, location, args);
	}

	private static List<DiagnosticInfo> Detect(GraphModel graph, bool strict)
	{
		List<DiagnosticInfo> diagnostics = new();
		// A service-key -> instance-index lookup for the transitive-disposable walk (composed once from the
		// graph's service-to-implementation and implementation-to-index maps).
		Dictionary<ServiceKey, int> serviceToIndex = new();
		foreach (KeyValuePair<ServiceKey, string> entry in graph.ServiceToImpl)
		{
			if (graph.ImplToIndex.TryGetValue(entry.Value, out int index))
			{
				serviceToIndex[entry.Key] = index;
			}
		}

		// The collection membership by element service type, so the transitive-disposable walk can follow a
		// service's collection dependencies (a transient disposable member accumulates on the root just like a
		// direct transient one).
		Dictionary<string, List<int>> collectionMembers =
			AwaitenGenerator.CollectionMemberIndices(graph.Collections, graph.ImplToIndex);

		// One report per holder+service, even when several root-owned owners reach the same factory.
		HashSet<string> reported = new(StringComparer.Ordinal);
		for (int i = 0; i < graph.Instances.Count; i++)
		{
			if (IsRootOwned(graph.Instances[i]))
			{
				ReportFromOwner(i, graph, serviceToIndex, collectionMembers, strict, reported, diagnostics);
			}
		}

		return diagnostics;
	}

	private static void ReportFromOwner(
		int owner,
		GraphModel graph,
		Dictionary<ServiceKey, int> serviceToIndex,
		Dictionary<string, List<int>> collectionMembers,
		bool strict,
		HashSet<string> reported,
		List<DiagnosticInfo> diagnostics)
	{
		// Walk the owner's graph through its transient dependencies (which are baked into it); a Func held by
		// any of them is equally root-bound. Mirrors the captive-dependency walk, keyed on a Func over a
		// disposable build-on-demand service instead of a scoped lifetime.
		HashSet<int> visited = new();
		Stack<int> stack = new();
		stack.Push(owner);

		while (stack.Count > 0)
		{
			int node = stack.Pop();
			if (!visited.Add(node))
			{
				continue;
			}

			AddAccumulatingFuncs(node, graph, serviceToIndex, collectionMembers, strict, reported, diagnostics);
			PushTransientDependencies(node, graph, stack);
		}
	}

	private static void AddAccumulatingFuncs(
		int node,
		GraphModel graph,
		Dictionary<ServiceKey, int> serviceToIndex,
		Dictionary<string, List<int>> collectionMembers,
		bool strict,
		HashSet<string> reported,
		List<DiagnosticInfo> diagnostics)
	{
		// Strict lifetime safety reports the non-suppressible error variant at error severity; loose reports the
		// plain warning descriptor at its default warning severity (no override).
		DiagnosticDescriptor descriptor = strict ? Diagnostics.RootAccumulatingFactoryStrict : Diagnostics.RootAccumulatingFactory;
		DiagnosticSeverity? severity = strict ? DiagnosticSeverity.Error : null;

		InstanceModel holder = graph.Instances[node];
		foreach (ParameterModel parameter in holder.ConstructorParameters.AsArray())
		{
			if (!IsRootAccumulatingFunc(graph, serviceToIndex, collectionMembers, parameter) || !reported.Add($"{node}|{parameter.ServiceType}|{parameter.Key}"))
			{
				continue;
			}

			string service = AwaitenGenerator.Display(parameter.ServiceType);

			// The leak-free remedy differs by relationship. A synchronous Func is redirected to a
			// Func<…, Owned<T>> disposal handle; an async Func<…, Task<T>> cannot use Owned<T> (a synchronous
			// handle that cannot await initialization - AWT119), so it is redirected to the async owned form
			// Func<…, Task<Owned<T>>>, which async-resolves each instance into a throwaway scope.
			string remedy = parameter.Kind == DependencyKind.FuncTask
				? $"resolve it as Func<…, Task<Owned<{service}>>> for per-use disposal"
				: $"resolve it as Func<…, Owned<{service}>> for per-use disposal";

			diagnostics.Add(new DiagnosticInfo(
				descriptor,
				parameter.Location ?? graph.InstanceLocations[node],
				new EquatableArray<string>([
					service,
					AwaitenGenerator.Display(holder.ImplementationType),
					remedy,
				]),
				severity));
		}
	}

	// A plain Func<…> or its async form Func<…, Task<T>> (but not a Func<…, Owned<T>>) over a build-on-demand
	// service (a transient or parameterized service) whose construction tracks a fresh disposable on its owner -
	// the produced service itself is disposable, or it transitively rebuilds a disposable transient. Each call to
	// such a Func, bound to the root, builds and re-tracks those disposables on the root, so they accumulate for
	// the container's lifetime. The async resolver tracks disposables identically to the synchronous one, so the
	// async factory leaks the same way and is included here - and since Owned<T> is unavailable for an async
	// service, the async form is the only deferred factory that can reach an async-tainted target at all.
	private static bool IsRootAccumulatingFunc(GraphModel graph, Dictionary<ServiceKey, int> serviceToIndex, Dictionary<string, List<int>> collectionMembers, ParameterModel parameter)
	{
		if (parameter.Kind is not (DependencyKind.Func or DependencyKind.FuncTask) || parameter.ProducesOwned
		    || !graph.ServiceToImpl.TryGetValue(new ServiceKey(parameter.ServiceType, parameter.Key), out string? targetImpl)
		    || !graph.ImplToIndex.TryGetValue(targetImpl, out int targetIndex))
		{
			return false;
		}

		InstanceModel target = graph.Instances[targetIndex];
		return (target.Lifetime == Lifetime.Transient || target.IsParameterized)
		       && AwaitenGenerator.BuildsFreshDisposable(graph.Instances, serviceToIndex, collectionMembers, targetIndex);
	}

	private static void PushTransientDependencies(int node, GraphModel graph, Stack<int> stack)
	{
		foreach (int next in graph.Dependencies[node])
		{
			if (graph.Instances[next].Lifetime == Lifetime.Transient)
			{
				stack.Push(next);
			}
		}
	}

	private static bool IsRootOwned(InstanceModel instance)
		=> instance.Lifetime == Lifetime.Singleton || instance.Production == ProductionKind.Instance;
}
