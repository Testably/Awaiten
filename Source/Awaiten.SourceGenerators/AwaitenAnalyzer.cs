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
			INamedTypeSymbol? containerAttribute = start.Compilation.GetTypeByMetadataName(ContainerAttributeName);
			INamedTypeSymbol? disposable = start.Compilation.GetTypeByMetadataName("System.IDisposable");
			if (containerAttribute is null || disposable is null)
			{
				return;
			}

			start.RegisterSymbolAction(
				ctx => Analyze((INamedTypeSymbol)ctx.Symbol, containerAttribute, disposable, ctx.ReportDiagnostic),
				SymbolKind.NamedType);
		});
	}

	private static void Analyze(
		INamedTypeSymbol type,
		INamedTypeSymbol containerAttribute,
		INamedTypeSymbol disposable,
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

			if (registration.Lifetime != Lifetime.Transient || !ImplementsDisposable(registration.Implementation, disposable))
			{
				continue;
			}

			// A transient that cannot be instantiated (an abstract type or interface) is rejected by the
			// generator as AWT103 and never built, so it cannot accumulate. Skip it here to match the
			// emitted container rather than stack a redundant AWT106 on the same already-erroring registration.
			if (registration.Implementation.IsAbstract)
			{
				continue;
			}

			report(Diagnostic.Create(
				Diagnostics.DisposableTransient,
				registration.Location ?? Location.None,
				Display(registration.ImplementationType)));
		}
	}

	private static bool ImplementsDisposable(INamedTypeSymbol type, INamedTypeSymbol disposable)
		=> type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, disposable));

	private static string Display(string fullyQualified)
		=> fullyQualified.StartsWith("global::", StringComparison.Ordinal)
			? fullyQualified.Substring("global::".Length)
			: fullyQualified;
}
