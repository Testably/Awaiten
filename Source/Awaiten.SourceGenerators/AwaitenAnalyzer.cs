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
///     Reports <see cref="Diagnostics.RootAccumulatingFactory">AWT117</see> for a root-owned instance (a
///     singleton or pre-built instance) that, directly or through its transitive transient dependencies,
///     holds a plain <c>Func&lt;…&gt;</c> over a disposable build-on-demand service (a disposable transient
///     or parameterized service). Such a factory is bound to the root, so every instance it builds is tracked
///     on the root and accumulates for the container's lifetime; a <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> hands
///     each instance back as a disposal handle and is not reported.
/// </summary>
/// <remarks>
///     AWT117 is an analyzer (rather than a generator) diagnostic so that, under loose lifetime safety where
///     it is a warning, it can be suppressed in source with <c>#pragma warning disable AWT117</c> or
///     <c>[SuppressMessage]</c> - a generator-reported diagnostic cannot. Under strict lifetime safety (the
///     default) it is escalated to an error, which is not suppressible, keeping the leak structurally
///     impossible. The graph it walks is built by the same <c>AwaitenGenerator.BuildGraph</c> the generator uses.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitenAnalyzer : DiagnosticAnalyzer
{
	private const string ContainerAttributeName = "Awaiten.ContainerAttribute";

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		ImmutableArray.Create(Diagnostics.RootAccumulatingFactory);

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
		// lifetime safety makes the root-accumulating pattern a hard error; loose leaves it a (suppressible)
		// warning by passing no severity override (the descriptor's default Warning applies).
		GraphModel graph = AwaitenGenerator.BuildGraph(type, compilation, new List<DiagnosticInfo>(), cancellationToken);
		DiagnosticSeverity? severity = AwaitenGenerator.ReadStrict(type) ? DiagnosticSeverity.Error : null;

		foreach (DiagnosticInfo diagnostic in Detect(graph, severity))
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

	private static List<DiagnosticInfo> Detect(GraphModel graph, DiagnosticSeverity? severity)
	{
		List<DiagnosticInfo> diagnostics = new();
		// One report per holder+service, even when several root-owned owners reach the same factory.
		HashSet<string> reported = new(StringComparer.Ordinal);
		for (int i = 0; i < graph.Instances.Count; i++)
		{
			if (IsRootOwned(graph.Instances[i]))
			{
				ReportFromOwner(i, graph, severity, reported, diagnostics);
			}
		}

		return diagnostics;
	}

	private static void ReportFromOwner(
		int owner,
		GraphModel graph,
		DiagnosticSeverity? severity,
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

			AddAccumulatingFuncs(node, graph, severity, reported, diagnostics);
			PushTransientDependencies(node, graph, stack);
		}
	}

	private static void AddAccumulatingFuncs(
		int node,
		GraphModel graph,
		DiagnosticSeverity? severity,
		HashSet<string> reported,
		List<DiagnosticInfo> diagnostics)
	{
		InstanceModel holder = graph.Instances[node];
		foreach (ParameterModel parameter in holder.ConstructorParameters.AsArray())
		{
			if (!IsRootAccumulatingFunc(graph, parameter) || !reported.Add($"{node}|{parameter.ServiceType}"))
			{
				continue;
			}

			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.RootAccumulatingFactory,
				parameter.Location ?? graph.InstanceLocations[node],
				new EquatableArray<string>([
					AwaitenGenerator.Display(parameter.ServiceType),
					AwaitenGenerator.Display(holder.ImplementationType),
				]),
				severity));
		}
	}

	// A plain Func<…> (not a Func<…, Owned<T>>) over a disposable build-on-demand service - a disposable
	// transient or parameterized service. Each call builds a fresh instance the owner tracks, so they accumulate.
	private static bool IsRootAccumulatingFunc(GraphModel graph, ParameterModel parameter)
	{
		if (parameter.Kind != DependencyKind.Func || parameter.ProducesOwned
		    || !graph.ServiceToImpl.TryGetValue(parameter.ServiceType, out string? targetImpl)
		    || !graph.ImplToIndex.TryGetValue(targetImpl, out int targetIndex))
		{
			return false;
		}

		InstanceModel target = graph.Instances[targetIndex];
		return target.IsDisposable && (target.Lifetime == Lifetime.Transient || target.IsParameterized);
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
