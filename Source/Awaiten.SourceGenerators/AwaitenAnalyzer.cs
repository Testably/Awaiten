using System.Collections.Immutable;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Awaiten.SourceGenerators;

/// <summary>
///     Reports <see cref="Diagnostics.DisposableTransient">AWT106</see> for a disposable transient
///     registered on a container. It is an analyzer rather than a generator diagnostic so it can be
///     suppressed in-source at the registration with <c>#pragma warning disable AWT106</c> or
///     <c>[SuppressMessage]</c> - <c>#pragma</c> does not suppress generator-reported diagnostics.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitenAnalyzer : DiagnosticAnalyzer
{
	private const string ContainerAttributeName = "Awaiten.ContainerAttribute";

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		ImmutableArray.Create(Diagnostics.DisposableTransient);

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.RegisterCompilationStartAction(static start =>
		{
			Compilation compilation = start.Compilation;
			INamedTypeSymbol? containerAttribute = compilation.GetTypeByMetadataName(ContainerAttributeName);
			INamedTypeSymbol? disposable = compilation.GetTypeByMetadataName("System.IDisposable");
			if (containerAttribute is null || disposable is null)
			{
				return;
			}

			start.RegisterSymbolAction(
				ctx => Analyze((INamedTypeSymbol)ctx.Symbol, containerAttribute, disposable, compilation, ctx.ReportDiagnostic),
				SymbolKind.NamedType);
		});
	}

	private static void Analyze(
		INamedTypeSymbol type,
		INamedTypeSymbol containerAttribute,
		INamedTypeSymbol disposable,
		Compilation compilation,
		Action<Diagnostic> report)
	{
		if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, containerAttribute)))
		{
			return;
		}

		// Registrations of the same implementation are coalesced into one instance whose lifetime is the
		// first registration's, so AWT106 is decided once per implementation off that first registration -
		// matching the container the generator emits.
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in ContainerRegistrations.Collect(type))
		{
			if (!seen.Add(registration.ImplementationType))
			{
				continue;
			}

			ITypeSymbol? owned = OwnedType(registration, type, compilation);
			if (owned is null || registration.Lifetime != Lifetime.Transient || !ImplementsDisposable(owned, disposable))
			{
				continue;
			}

			report(Diagnostic.Create(
				Diagnostics.DisposableTransient,
				registration.Location ?? Location.None,
				Display(registration.ImplementationType)));
		}
	}

	/// <summary>
	///     The concrete type the container actually owns and disposes for a registration - a factory's
	///     return type or the constructed implementation - or <c>null</c> when nothing accumulates here: a
	///     caller-owned <c>Instance</c>, a not-instantiable implementation (rejected as AWT103) or a
	///     factory the generator already rejects (AWT108/AWT112). Mirrors the generator so AWT106 and the
	///     emitted disposal stay in lock-step.
	/// </summary>
	private static ITypeSymbol? OwnedType(RawRegistration registration, INamedTypeSymbol container, Compilation compilation)
	{
		switch (registration.Production)
		{
			case ProductionKind.Instance:
				return null;
			case ProductionKind.Factory:
			{
				List<IMethodSymbol> candidates = ContainerRegistrations.FindFactoryCandidates(
					container, registration.ProductionMember!, registration.Implementation, compilation);
				return candidates.Count == 1 ? candidates[0].ReturnType : null;
			}

			default:
				return registration.Implementation.IsAbstract ? null : registration.Implementation;
		}
	}

	private static bool ImplementsDisposable(ITypeSymbol type, INamedTypeSymbol disposable)
		=> SymbolEqualityComparer.Default.Equals(type, disposable)
		   || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, disposable));

	private static string Display(string fullyQualified)
		=> fullyQualified.StartsWith("global::", StringComparison.Ordinal)
			? fullyQualified.Substring("global::".Length)
			: fullyQualified;
}
