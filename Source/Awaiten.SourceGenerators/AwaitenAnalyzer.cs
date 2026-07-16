using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Awaiten.SourceGenerators;

/// <summary>
///     Reports <see cref="Diagnostics.RootAccumulatingFactory">AWT118</see> for a root-owned instance (a
///     singleton or pre-built instance) that, directly or through its transitive transient dependencies,
///     holds a plain <c>Func&lt;…&gt;</c> over a build-on-demand service (a transient or parameterized service)
///     whose construction tracks a fresh disposable on the root: the produced service is itself disposable, or
///     it transitively rebuilds a disposable transient. Such a factory is bound to the root, so every instance
///     it builds (and the disposables built with it) is tracked on the root and accumulates for the container's
///     lifetime; a <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> hands each instance back as a disposal handle and is not
///     reported. Also reports <see cref="Diagnostics.AsyncOnlyDisposal">AWT156</see> for a synchronous
///     <c>using</c> / <c>Dispose()</c> of a generated <c>Root</c> or <c>Scope</c> whose container owns a service
///     that implements <c>IAsyncDisposable</c> but not <c>IDisposable</c> (a disposal only <c>DisposeAsync</c> /
///     <c>await using</c> can carry out) that the disposed owner could actually track: a root-owned
///     instance (a singleton or pre-built instance) always lives on the <c>Root</c>, so it never faults a
///     child <c>Scope</c>'s disposal.
/// </summary>
/// <remarks>
///     AWT118 is an analyzer (rather than a generator) diagnostic so that, under loose lifetime safety where
///     it is a warning, it can be suppressed in source with <c>#pragma warning disable AWT118</c> or
///     <c>[SuppressMessage]</c>; a generator-reported diagnostic cannot. Under strict lifetime safety (the
///     default) it is instead reported through <see cref="Diagnostics.RootAccumulatingFactoryStrict" />: an
///     error carrying <see cref="WellKnownDiagnosticTags.NotConfigurable" />, so it cannot be suppressed by
///     <c>#pragma</c>, <c>&lt;NoWarn&gt;</c> or an editorconfig severity override; the only opt-out is
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
		ImmutableArray.Create(Diagnostics.RootAccumulatingFactory, Diagnostics.RootAccumulatingFactoryStrict, Diagnostics.AsyncOnlyDisposal);

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

			// One graph build per container per compilation, shared by the AWT118 symbol walk and the AWT156
			// disposal sites; one compilation can dispose the same container in many places. The Lazy
			// (ExecutionAndPublication) collapses concurrent callers into a single BuildGraph per container.
			ConcurrentDictionary<INamedTypeSymbol, Lazy<GraphModel>> graphByContainer =
				new(SymbolEqualityComparer.Default);

			start.RegisterSymbolAction(
				ctx => Analyze(
					(INamedTypeSymbol)ctx.Symbol, containerAttribute, start.Compilation, graphByContainer, ctx.ReportDiagnostic, ctx.CancellationToken),
				SymbolKind.NamedType);

			// AWT156 fires at each synchronous disposal site of a generated Root/Scope. A generated Root/Scope
			// is recognized by shape and name: a type named Root or Scope, implementing IAwaitenScope, nested in
			// a [Container] class. The name check matters: a user-authored type nested in the container class
			// can hand-implement the public IAwaitenScope, but it cannot coexist with the generated Root/Scope
			// under their names.
			INamedTypeSymbol? scopeInterface = start.Compilation.GetTypeByMetadataName("Awaiten.IAwaitenScope");
			if (scopeInterface is null)
			{
				return;
			}

			start.RegisterOperationAction(
				ctx => AnalyzeSynchronousUsing(ctx, containerAttribute, scopeInterface, start.Compilation, graphByContainer),
				OperationKind.Using, OperationKind.UsingDeclaration);
			start.RegisterOperationAction(
				ctx => AnalyzeSynchronousDisposeCall(ctx, containerAttribute, scopeInterface, start.Compilation, graphByContainer),
				OperationKind.Invocation);
		});
	}

	private static void Analyze(
		INamedTypeSymbol type,
		INamedTypeSymbol containerAttribute,
		Compilation compilation,
		ConcurrentDictionary<INamedTypeSymbol, Lazy<GraphModel>> graphByContainer,
		Action<Diagnostic> report,
		CancellationToken cancellationToken)
	{
		if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, containerAttribute)))
		{
			return;
		}

		// Re-derive the object graph (cached, shared with the AWT156 disposal sites). Strict lifetime safety
		// reports the root-accumulating pattern through the non-suppressible error descriptor, loose reports
		// the plain suppressible warning.
		GraphModel graph = CachedGraph(graphByContainer, type, compilation, cancellationToken);
		bool strict = AwaitenGenerator.ReadStrict(type);

		foreach (DiagnosticInfo diagnostic in Detect(graph, strict))
		{
			report(ToDiagnostic(diagnostic, compilation));
		}
	}

	// Builds the reportable diagnostic. The location is reconstructed against the compilation's actual syntax
	// tree (matched by file path) rather than via LocationInfo.ToLocation(), which yields an external location
	// detached from any tree, and a diagnostic without a source-tree location cannot be suppressed by an
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

	// Builds (or returns the cached) object graph of a container. The registration diagnostics produced
	// while building it are the generator's to report, so they are collected into a throwaway list and
	// discarded here.
	private static GraphModel CachedGraph(
		ConcurrentDictionary<INamedTypeSymbol, Lazy<GraphModel>> graphByContainer,
		INamedTypeSymbol container,
		Compilation compilation,
		CancellationToken cancellationToken)
		=> graphByContainer.GetOrAdd(
			container,
			c => new Lazy<GraphModel>(
				() => AwaitenGenerator.BuildGraph(c, compilation, new List<DiagnosticInfo>(), cancellationToken),
				LazyThreadSafetyMode.ExecutionAndPublication)).Value;

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

		// The collection membership (plain, by (element service type, key), and keyed, by service type) so the
		// transitive-disposable walk can follow a service's collection dependencies of either kind (a transient
		// disposable member accumulates on the root just like a direct transient one).
		CollectionMembership membership =
			AwaitenGenerator.MembershipIndices(graph.Collections, graph.KeyedCollections, graph.ImplToIndex);

		// One report per holder+service, even when several root-owned owners reach the same factory.
		HashSet<string> reported = new(StringComparer.Ordinal);
		for (int i = 0; i < graph.Instances.Count; i++)
		{
			if (IsRootOwned(graph.Instances[i]))
			{
				ReportFromOwner(i, graph, serviceToIndex, membership, strict, reported, diagnostics);
			}
		}

		return diagnostics;
	}

	private static void ReportFromOwner(
		int owner,
		GraphModel graph,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
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

			AddAccumulatingFuncs(node, graph, serviceToIndex, membership, strict, reported, diagnostics);
			PushTransientDependencies(node, graph, stack);
		}
	}

	private static void AddAccumulatingFuncs(
		int node,
		GraphModel graph,
		Dictionary<ServiceKey, int> serviceToIndex,
		CollectionMembership membership,
		bool strict,
		HashSet<string> reported,
		List<DiagnosticInfo> diagnostics)
	{
		// Strict lifetime safety reports the non-suppressible error variant at error severity; loose reports the
		// plain warning descriptor at its default warning severity (no override).
		DiagnosticDescriptor descriptor = strict ? Diagnostics.RootAccumulatingFactoryStrict : Diagnostics.RootAccumulatingFactory;
		DiagnosticSeverity? severity = strict ? DiagnosticSeverity.Error : null;

		// A lifecycle hook's Func<T> parameter is rooted too - a release hook captures it by value into the owner's
		// teardown closure - so an accumulating fresh-disposable Func reached through one is reported like a
		// constructor parameter's.
		InstanceModel holder = graph.Instances[node];
		foreach (ParameterModel parameter in holder.ConstructorParameters.AsArray().Concat(holder.HookParameters()))
		{
			if (!IsRootAccumulatingFunc(graph, serviceToIndex, membership, parameter) || !reported.Add($"{node}|{parameter.ServiceType}|{parameter.Key}"))
			{
				continue;
			}

			string service = AwaitenGenerator.Display(parameter.ServiceType);

			// The leak-free remedy differs by relationship. A synchronous Func is redirected to a
			// Func<…, Owned<T>> disposal handle; an async Func<…, Task<T>> cannot use Owned<T> (a synchronous
			// handle that cannot await initialization, AWT119), so it is redirected to the async owned form
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
	// async factory leaks the same way and is included here, and since Owned<T> is unavailable for an async
	// service, the async form is the only deferred factory that can reach an async-tainted target at all.
	private static bool IsRootAccumulatingFunc(GraphModel graph, Dictionary<ServiceKey, int> serviceToIndex, CollectionMembership membership, ParameterModel parameter)
	{
		if (parameter.Kind is not (DependencyKind.Func or DependencyKind.FuncTask) || parameter.ProducesOwned
		    || !graph.ServiceToImpl.TryGetValue(new ServiceKey(parameter.ServiceType, parameter.Key), out string? targetImpl)
		    || !graph.ImplToIndex.TryGetValue(targetImpl, out int targetIndex))
		{
			return false;
		}

		InstanceModel target = graph.Instances[targetIndex];
		return (target.Lifetime == Lifetime.Transient || target.IsParameterized)
		       && AwaitenGenerator.BuildsFreshDisposable(graph.Instances, serviceToIndex, membership, targetIndex);
	}

	// AWT156: a synchronous `using` (statement or declaration) whose resource is a generated Root/Scope of a
	// container that owns an async-only disposable. The asynchronous forms are the remedy, not the fault, so
	// `await using` is skipped.
	private static void AnalyzeSynchronousUsing(
		OperationAnalysisContext context,
		INamedTypeSymbol containerAttribute,
		INamedTypeSymbol scopeInterface,
		Compilation compilation,
		ConcurrentDictionary<INamedTypeSymbol, Lazy<GraphModel>> graphByContainer)
	{
		IOperation resources;
		switch (context.Operation)
		{
			case IUsingOperation { IsAsynchronous: false, } usingStatement:
				resources = usingStatement.Resources;
				break;
			case IUsingDeclarationOperation { IsAsynchronous: false, } usingDeclaration:
				resources = usingDeclaration.DeclarationGroup;
				break;
			default:
				return;
		}

		foreach ((ITypeSymbol? resourceType, SyntaxNode resourceSyntax) in Resources(resources))
		{
			ReportIfAsyncOnlyOwner(context, resourceType, resourceSyntax, containerAttribute, scopeInterface, compilation, graphByContainer);
		}
	}

	// Each resource a using operation disposes, paired with the syntax to report at: the declarators of a
	// declaration group (`using var root = …`, possibly several in one statement), or the expression itself
	// (`using (root)`). Reporting per declarator keeps a multi-declarator statement from stacking identical
	// whole-statement diagnostics, and keeps the squiggle of a `using (…) { }` statement off its body block.
	private static IEnumerable<(ITypeSymbol? Type, SyntaxNode Syntax)> Resources(IOperation resources)
	{
		if (resources is not IVariableDeclarationGroupOperation group)
		{
			return [(resources.Type, resources.Syntax),];
		}

		return group.Declarations
			.SelectMany(declaration => declaration.Declarators)
			.Select(declarator => ((ITypeSymbol?)declarator.Symbol.Type, declarator.Syntax));
	}

	// AWT156: an explicit synchronous Dispose() call on a receiver statically typed as a generated Root/Scope.
	// A call through IAwaitenScope / IDisposable (or from another assembly, or by a host framework) does not
	// reveal the container and is not reported; the generated drain's runtime throw remains the backstop there.
	private static void AnalyzeSynchronousDisposeCall(
		OperationAnalysisContext context,
		INamedTypeSymbol containerAttribute,
		INamedTypeSymbol scopeInterface,
		Compilation compilation,
		ConcurrentDictionary<INamedTypeSymbol, Lazy<GraphModel>> graphByContainer)
	{
		if (context.Operation is IInvocationOperation { TargetMethod: { Name: "Dispose", Parameters.Length: 0, }, Instance: { } receiver, })
		{
			ReportIfAsyncOnlyOwner(context, receiver.Type, context.Operation.Syntax, containerAttribute, scopeInterface, compilation, graphByContainer);
		}
	}

	// Reports AWT156 when the synchronously disposed type is a generated Root/Scope (a type named Root or
	// Scope, implementing IAwaitenScope, nested in a [Container] class) of a container that statically owns an
	// async-only disposable the disposed owner could track. IsAsyncDisposable is read off a registration's
	// declared/produced type; a factory output hiding one behind a non-disposable declared type is left to
	// the runtime backstop, and a pre-built Instance registration is never owned, so it carries neither flag
	// and is naturally exempt. A scoped/transient async-only service also warns on a Root using, since the
	// root is itself a scope and may track one; a root-owned one never warns on a child Scope using, since a
	// singleton always tracks on the Root (its resolver runs against the root even when first hit inside a
	// child scope), so a Scope's drain cannot reach it.
	private static void ReportIfAsyncOnlyOwner(
		OperationAnalysisContext context,
		ITypeSymbol? disposedType,
		SyntaxNode reportSyntax,
		INamedTypeSymbol containerAttribute,
		INamedTypeSymbol scopeInterface,
		Compilation compilation,
		ConcurrentDictionary<INamedTypeSymbol, Lazy<GraphModel>> graphByContainer)
	{
		if (disposedType is not INamedTypeSymbol { Name: "Root" or "Scope", } named
		    || named.ContainingType is not { } container
		    || !named.AllInterfaces.Contains(scopeInterface, SymbolEqualityComparer.Default)
		    || !container.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, containerAttribute)))
		{
			return;
		}

		// A container declared in a referenced assembly stays invisible to this check (a documented false
		// negative, like an interface-typed receiver): its graph cannot be rebuilt faithfully here, because
		// BuildGraph expands a default [Scan] over the current compilation's assembly, not the container's.
		// The runtime throw in the generated synchronous drain remains the backstop.
		if (!SymbolEqualityComparer.Default.Equals(container.ContainingAssembly, compilation.Assembly))
		{
			return;
		}

		GraphModel graph = CachedGraph(graphByContainer, container, compilation, context.CancellationToken);
		ImmutableArray<string> asyncOnly = AsyncOnlyDisposables(graph, includeRootOwned: named.Name == "Root");
		if (asyncOnly.IsEmpty)
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.AsyncOnlyDisposal,
			reportSyntax.GetLocation(),
			named.ToDisplayString(),
			string.Join("', '", asyncOnly)));
	}

	// The async-only disposable implementations (IAsyncDisposable without IDisposable) the disposed owner
	// could track: a child Scope's drain never reaches a root-owned instance, so those are filtered out
	// unless the Root itself is disposed. Deduped by display name: a decorator type can recur as several
	// chain-link instances, which would repeat one name (Distinct keeps the first occurrence, preserving
	// registration order). The cheap flag test runs before the display formatting, which allocates.
	private static ImmutableArray<string> AsyncOnlyDisposables(GraphModel graph, bool includeRootOwned)
		=> graph.Instances
			.Where(instance => instance.IsAsyncDisposable && !instance.IsDisposable
			                   && (includeRootOwned || !IsRootOwned(instance)))
			.Select(instance => AwaitenGenerator.DisplayInstance(instance.ImplementationType))
			.Distinct(StringComparer.Ordinal)
			.ToImmutableArray();

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
