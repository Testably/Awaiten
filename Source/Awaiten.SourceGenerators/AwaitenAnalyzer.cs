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

			// A pre-built Instance is owned by the caller and never disposed by the container, so it cannot
			// accumulate; skip it to match the container the generator emits (which does not register it).
			if (registration.Production == ProductionKind.Instance)
			{
				continue;
			}

			// AWT106 is about the type the container actually owns and disposes: a factory's concrete return
			// type (which may be disposable behind a non-disposable service interface), or the constructed
			// implementation. This mirrors the generator so the warning and the disposal stay in lock-step.
			ITypeSymbol? owned;
			if (registration.Production == ProductionKind.Factory)
			{
				List<IMethodSymbol> candidates = ContainerRegistrations.FindFactoryCandidates(
					type, registration.ProductionMember!, registration.Implementation, compilation);

				// Zero or several candidates is an AWT108/AWT112 error the generator reports; don't pile on.
				if (candidates.Count != 1)
				{
					continue;
				}

				owned = candidates[0].ReturnType;
			}
			else
			{
				// A transient that cannot be instantiated (an abstract type or interface) is rejected by the
				// generator as AWT103 and never built, so it cannot accumulate. Skip it here to match the
				// emitted container rather than stack a redundant AWT106 on the same already-erroring registration.
				if (registration.Implementation.IsAbstract)
				{
					continue;
				}

				owned = registration.Implementation;
			}

			if (registration.Lifetime != Lifetime.Transient || !ImplementsDisposable(owned, disposable))
			{
				continue;
			}

			report(Diagnostic.Create(
				Diagnostics.DisposableTransient,
				registration.Location ?? Location.None,
				Display(registration.ImplementationType)));
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
