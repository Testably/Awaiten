using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The base container named by <c>[Container(Extends = typeof(Base))]</c> and the location of that <c>Extends</c>
///     argument, where the inherited declarations that have no syntax of their own report.
/// </summary>
/// <remarks>An intermediate type carrying a live Roslyn <see cref="Location" />; see <see cref="RawRegistration" />.</remarks>
internal sealed record ExtendedBase(INamedTypeSymbol Symbol, Location? Location);

partial class AwaitenGenerator
{
	/// <summary>
	///     The <c>[Container]</c> options that decide how a graph is validated. A container with <c>Extends</c> inherits
	///     them from its base unchanged (AWT225 when it sets one).
	/// </summary>
	private static readonly string[] InheritedContainerOptions = ["AsyncSafety", "LifetimeSafety",];

	/// <summary>
	///     The base the container extends, or <see langword="null" /> when <c>Extends</c> is unset or names no usable base
	///     (see <see cref="ResolveExtends" />, which reports why). Reads symbols only and reports nothing, so the option
	///     readers and the analyzer can call it.
	/// </summary>
	internal static INamedTypeSymbol? ReadExtends(INamedTypeSymbol containerSymbol)
		=> ExtendsTarget(containerSymbol, out _) is INamedTypeSymbol target && ExtendsFault(containerSymbol, target, null) is null
			? target
			: null;

	/// <summary>
	///     Validates <c>[Container(Extends = typeof(Base))]</c> and returns the base, or <see langword="null" /> when the
	///     argument is unset or invalid. The base must be another non-generic <c>[Container]</c> (AWT222, AWT223) that
	///     does not extend a container itself (AWT224): extending is one level deep. A container with a base must not set
	///     the options that decide validation, which it inherits (AWT225).
	/// </summary>
	private static ExtendedBase? ResolveExtends(INamedTypeSymbol containerSymbol, List<DiagnosticInfo> diagnostics)
	{
		if (ExtendsTarget(containerSymbol, out AttributeData? attribute) is not { } target)
		{
			return null;
		}

		Location? location = NamedArgumentLocation(attribute!, "Extends");
		if (ExtendsFault(containerSymbol, target, LocationInfo.From(location)) is { } fault)
		{
			diagnostics.Add(fault);
			return null;
		}

		foreach (KeyValuePair<string, TypedConstant> argument in attribute!.NamedArguments)
		{
			if (InheritedContainerOptions.Contains(argument.Key))
			{
				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ExtendsOverridesOption,
					LocationInfo.From(NamedArgumentLocation(attribute, argument.Key)),
					new EquatableArray<string>([
						Display(containerSymbol.ToDisplayString(FullyQualified)),
						argument.Key,
						Display(target.ToDisplayString(FullyQualified)),
					])));
			}
		}

		return new ExtendedBase((INamedTypeSymbol)target, location);
	}

	/// <summary>The type named by the container's <c>Extends</c>, or <see langword="null" /> when unset.</summary>
	private static ITypeSymbol? ExtendsTarget(INamedTypeSymbol containerSymbol, out AttributeData? attribute)
	{
		if (TryGetAwaitenAttribute(containerSymbol.GetAttributes(), "ContainerAttribute", out attribute))
		{
			foreach (KeyValuePair<string, TypedConstant> argument in attribute!.NamedArguments)
			{
				if (argument.Key == "Extends" && argument.Value.Value is ITypeSymbol target)
				{
					return target;
				}
			}
		}

		return null;
	}

	/// <summary>Why <paramref name="target" /> cannot be extended (AWT222-AWT224), or <see langword="null" /> when it can.</summary>
	private static DiagnosticInfo? ExtendsFault(INamedTypeSymbol containerSymbol, ITypeSymbol target, LocationInfo? location)
	{
		string container = Display(containerSymbol.ToDisplayString(FullyQualified));
		string display = Display(target.ToDisplayString(FullyQualified));
		if (SymbolEqualityComparer.Default.Equals(target, containerSymbol))
		{
			return new DiagnosticInfo(Diagnostics.ExtendsItself, location, new EquatableArray<string>([container,]));
		}

		if (target is not INamedTypeSymbol named || !HasAwaitenAttribute(named.OriginalDefinition.GetAttributes(), "ContainerAttribute"))
		{
			return new DiagnosticInfo(Diagnostics.ExtendsNotAContainer, location, new EquatableArray<string>([container, display, "it is not marked [Container]",]));
		}

		for (INamedTypeSymbol? type = named; type is not null; type = type.ContainingType)
		{
			if (type.IsGenericType)
			{
				return new DiagnosticInfo(Diagnostics.ExtendsNotAContainer, location, new EquatableArray<string>([container, display, "it is generic",]));
			}
		}

		return ExtendsTarget(named, out _) is { } next
			? new DiagnosticInfo(Diagnostics.ExtendsChain, location, new EquatableArray<string>([container, display, Display(next.ToDisplayString(FullyQualified)),]))
			: null;
	}

	/// <summary>The location of the named argument <paramref name="name" /> of an attribute, else of the attribute itself.</summary>
	private static Location? NamedArgumentLocation(AttributeData attribute, string name)
	{
		SyntaxNode? syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
		return (syntax as AttributeSyntax)?.ArgumentList?.Arguments
		       .FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.Text == name)?.GetLocation()
		       ?? syntax?.GetLocation();
	}

	/// <summary>
	///     The contributions of a container that extends a base: the base itself, read like a module qualified on its own
	///     type, then the base's imports, all inherited, then the container's own imports the base does not already make.
	///     A module both import stays inherited, so the container's own registrations replace its registrations like the
	///     base's. An inherited declaration without syntax (read from a referenced assembly) reports at the container's
	///     <c>Extends</c>. The base's import faults are its own build's to report.
	/// </summary>
	private static List<ImportedModule> InheritBase(ExtendedBase extended, List<ImportedModule> ownModules)
	{
		List<ImportedModule> modules = [new(extended.Symbol, extended.Location, Inherited: true, IsBase: true),];
		modules.AddRange(CollectImportedModules(extended.Symbol, new List<DiagnosticInfo>())
			.Select(module => module with { ImportLocation = module.ImportLocation ?? extended.Location, Inherited = true, }));
		modules.AddRange(ownModules.Where(own => !modules.Any(module => SymbolEqualityComparer.Default.Equals(module.Symbol, own.Symbol))));
		return modules;
	}

	/// <summary>
	///     Drops every inherited registration whose service and key a registration declared directly on the container
	///     also provides, so the container's registration replaces it in full, collections included, like an overridden
	///     <c>Fallback</c> default. Runs before coalescing, so the dropped registration never meets the replacement
	///     (no AWT107/AWT111). A closed registration replaces closed ones and an open registration replaces open ones of
	///     the same definition; a closed registration still wins its closing over an inherited open one through the
	///     ordinary open generic expansion. A container <c>Fallback</c> registration only fills a gap and replaces
	///     nothing. A closed <c>[ImportService&lt;T&gt;]</c> declared on the container replaces every inherited
	///     registration of <c>T</c>, under any key, so the host supplies it instead.
	/// </summary>
	private static void ReplaceInherited(
		Inheritance inheritance,
		List<RawRegistration> own,
		List<OpenRegistration> ownOpen,
		List<RawRegistration> result,
		List<OpenRegistration> open)
	{
		HashSet<string> ownImports = OwnClosedImportServices(inheritance.Container);
		HashSet<ServiceKey> replaced = new(own
			.Where(registration => !registration.Weak)
			.Select(registration => new ServiceKey(registration.ServiceType, EffectiveKey(registration.Key, registration.WhenInjectedInto))));
		HashSet<ServiceKey> replacedOpen = new(ownOpen
			.Where(registration => !registration.Weak)
			.Select(OpenServiceKey));

		result.RemoveAll(registration => inheritance.Owns(registration.Origin)
		                                 && (ownImports.Contains(registration.ServiceType)
		                                     || replaced.Contains(new ServiceKey(registration.ServiceType, EffectiveKey(registration.Key, registration.WhenInjectedInto)))));
		open.RemoveAll(registration => inheritance.Owns(registration.Origin) && replacedOpen.Contains(OpenServiceKey(registration)));

		static string? EffectiveKey(string? key, string? whenInjectedInto)
			=> whenInjectedInto is { } consumer ? ContextKey(consumer) : key;

		static ServiceKey OpenServiceKey(OpenRegistration registration)
			=> new(registration.Service.ToDisplayString(FullyQualified), EffectiveKey(registration.Key, registration.WhenInjectedInto));
	}

	/// <summary>
	///     Removes from <paramref name="external" /> each closed <c>[ImportService&lt;T&gt;]</c> type that a registration
	///     declared directly on the extending container replaces: the container registers what its base drew from the
	///     host, so the type is Awaiten-owned again (no AWT175). A type the container itself declares external stays.
	/// </summary>
	private static void ReplaceInheritedImportServices(INamedTypeSymbol containerSymbol, ExternalServiceTypes external)
	{
		HashSet<string> ownImports = OwnClosedImportServices(containerSymbol);
		HashSet<string> ownRegistered = OwnRegisteredServices(containerSymbol);
		external.Closed.RemoveWhere(type => !ownImports.Contains(type) && ownRegistered.Contains(type));
	}

	/// <summary>The closed types declared with <c>[ImportService]</c> directly on <paramref name="container" />.</summary>
	private static HashSet<string> OwnClosedImportServices(INamedTypeSymbol container)
	{
		HashSet<string> types = new(StringComparer.Ordinal);
		foreach (AttributeData attribute in container.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "ImportServiceAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			ITypeSymbol? service = attributeClass is { IsGenericType: true, TypeArguments.Length: 1, }
				? attributeClass.TypeArguments[0]
				: attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;
			if (service is INamedTypeSymbol { IsUnboundGenericType: false, } closed)
			{
				types.Add(closed.ToDisplayString(FullyQualified));
			}
		}

		return types;
	}

	/// <summary>
	///     The closed service types registered with a non-<c>Fallback</c> lifetime attribute directly on
	///     <paramref name="container" />.
	/// </summary>
	private static HashSet<string> OwnRegisteredServices(INamedTypeSymbol container)
	{
		HashSet<string> types = new(StringComparer.Ordinal);
		foreach (AttributeData attribute in container.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "SingletonAttribute" or "ScopedAttribute" or "TransientAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
			{
				continue;
			}

			ITypeSymbol? service = attributeClass.IsGenericType
				? attributeClass.TypeArguments[attributeClass.TypeArguments.Length - 1]
				: attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[1].Value as ITypeSymbol : null;
			if (service is INamedTypeSymbol { IsUnboundGenericType: false, } closed && NamedFallback(attribute) == 0)
			{
				types.Add(closed.ToDisplayString(FullyQualified));
			}
		}

		return types;
	}

	/// <summary>
	///     Adds the base's <c>[InjectProperty]</c> entries to the container's, marked <see cref="InjectPropertyEntry.Inherited" />.
	///     An entry the container declares for the same property of the same implementation wins. The base's entry
	///     faults are its own build's to report.
	/// </summary>
	private static void InheritInjectProperties(Inheritance inheritance, Dictionary<string, List<InjectPropertyEntry>> injectProperties)
	{
		LocationInfo? fallback = LocationInfo.From(inheritance.Location);
		foreach (KeyValuePair<string, List<InjectPropertyEntry>> inherited in CollectInjectProperties(inheritance.Base, new List<DiagnosticInfo>()))
		{
			if (!injectProperties.TryGetValue(inherited.Key, out List<InjectPropertyEntry>? entries))
			{
				entries = new List<InjectPropertyEntry>();
				injectProperties[inherited.Key] = entries;
			}

			entries.AddRange(inherited.Value
				.Where(entry => entries.All(own => own.PropertyName != entry.PropertyName))
				.Select(entry => entry with { Location = entry.Location ?? fallback, Inherited = true, }));
		}
	}

	/// <summary>
	///     Reports AWT226 for every inherited registration that needs a type or member the container cannot access, and
	///     drops it: the generated container would otherwise not compile, or silently lack a scan match its base has.
	///     The dropped service no longer reports a missing dependency (AWT101) in its consumers, so the one cause is
	///     reported once.
	/// </summary>
	private static void DropInaccessibleInherited(
		Inheritance inheritance,
		List<RawRegistration> registrations,
		HashSet<string> constraintRejected,
		List<DiagnosticInfo> diagnostics)
	{
		registrations.RemoveAll(registration =>
		{
			if (!inheritance.Owns(registration.Origin) || InaccessiblePart(registration) is not { } subject)
			{
				return false;
			}

			inheritance.Report(subject, diagnostics);
			constraintRejected.Add(registration.ServiceType);
			return true;
		});

		string? InaccessiblePart(RawRegistration registration)
		{
			INamedTypeSymbol owner = registration.Origin!;
			string? subject = inheritance.InaccessibleType(registration.Implementation, registration.IsScan)
			                  ?? (registration.ServiceSymbol is { } service ? inheritance.InaccessibleType(service, registration.IsScan) : null);
			subject ??= registration.Production switch
			{
				ProductionKind.Factory => inheritance.InaccessibleMember(owner, registration.ProductionMember, "factory"),
				ProductionKind.Instance => inheritance.InaccessibleMember(owner, registration.ProductionMember, "instance member"),
				_ => inheritance.InaccessibleConstructor(registration.Implementation),
			};
			return subject
			       ?? inheritance.InaccessibleMember(owner, registration.OnActivated, "lifecycle hook")
			       ?? inheritance.InaccessibleMember(owner, registration.OnRelease, "lifecycle hook");
		}
	}

	/// <summary>
	///     Removes AWT176 for an <c>[ImportService]</c> only the base declares: the base reports its own unconsumed
	///     declarations, and one whose consumer the extending container replaced is meant to fall away.
	/// </summary>
	private static void RemoveInheritedUnconsumedExternalServices(Inheritance inheritance, List<ImportedModule> modules, Compilation compilation, List<DiagnosticInfo> diagnostics)
	{
		ExternalServiceTypes own = CollectExternalServiceTypes(inheritance.Container, modules.FindAll(module => !module.Inherited), compilation, new List<DiagnosticInfo>());
		HashSet<string> declared = new(own.Closed.Concat(own.Open).Select(Display), StringComparer.Ordinal);
		diagnostics.RemoveAll(diagnostic => ReferenceEquals(diagnostic.Descriptor, Diagnostics.UnconsumedExternalService)
		                                    && !declared.Contains(diagnostic.MessageArgs.AsArray()[0]));
	}

	/// <summary>
	///     Whether the decorator or composite an inherited <c>[Decorate]</c>/<c>[Composite]</c> names (its first type,
	///     generic or <c>typeof</c> form) cannot be constructed from the extending container, reporting AWT226 if so.
	/// </summary>
	private static bool InaccessibleInheritedType(
		AttributeData attribute,
		INamedTypeSymbol attributeClass,
		Inheritance inheritance,
		List<DiagnosticInfo> diagnostics)
	{
		ITypeSymbol? type = attributeClass.IsGenericType
			? attributeClass.TypeArguments.FirstOrDefault()
			: attribute.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol;
		if (type is not INamedTypeSymbol named || inheritance.InaccessibleConstructedType(named) is not { } subject)
		{
			return false;
		}

		inheritance.Report(subject, diagnostics);
		return true;
	}

	/// <summary>
	///     Removes from <paramref name="diagnostics" /> every diagnostic the base reports itself, compared by rule and
	///     message, not by location: an inherited declaration read from metadata reports at the <c>Extends</c> argument,
	///     not where the base reports it. A problem of the base is reported once, on the base; a problem the extending
	///     container introduces still differs from every base diagnostic and stays.
	/// </summary>
	internal static void RemoveBaseDiagnostics(List<DiagnosticInfo> diagnostics, IEnumerable<DiagnosticInfo> baseDiagnostics)
	{
		HashSet<DiagnosticInfo> inherited = new(baseDiagnostics.Select(diagnostic => diagnostic with { Location = null, }));
		diagnostics.RemoveAll(diagnostic => inherited.Contains(diagnostic with { Location = null, }));
	}

	/// <summary>
	///     A container that extends a base, together with what it inherits: the base and the modules the base imports
	///     (<see cref="Owns" />), and the location of the <c>Extends</c> argument. Answers whether an inherited type or
	///     member is accessible from the extending container, which calls it from its own generated code.
	/// </summary>
	private sealed class Inheritance
	{
		private readonly Compilation _compilation;
		private readonly HashSet<INamedTypeSymbol> _origins;

		public Inheritance(INamedTypeSymbol container, ExtendedBase extended, List<ImportedModule> modules, Compilation compilation)
		{
			Container = container;
			Base = extended.Symbol;
			Location = extended.Location;
			_compilation = compilation;
			_origins = new HashSet<INamedTypeSymbol>(
				modules.Where(module => module.Inherited).Select(module => module.Symbol),
				SymbolEqualityComparer.Default);
		}

		public INamedTypeSymbol Container { get; }

		public INamedTypeSymbol Base { get; }

		public Location? Location { get; }

		/// <summary>Whether <paramref name="origin" /> is the base or a module the base imports.</summary>
		public bool Owns(INamedTypeSymbol? origin) => origin is not null && _origins.Contains(origin);

		/// <summary>Reports AWT226 for <paramref name="subject" />, once.</summary>
		public void Report(string subject, List<DiagnosticInfo> diagnostics)
		{
			DiagnosticInfo diagnostic = new(
				Diagnostics.InheritedMemberInaccessible,
				LocationInfo.From(Location),
				new EquatableArray<string>([
					subject,
					Display(Container.ToDisplayString(FullyQualified)),
					Display(Base.ToDisplayString(FullyQualified)),
				]));
			if (!diagnostics.Contains(diagnostic))
			{
				diagnostics.Add(diagnostic);
			}
		}

		/// <summary>
		///     The AWT226 subject when <paramref name="type" /> is accessible from the base but not from the container,
		///     else <see langword="null" />.
		/// </summary>
		public string? InaccessibleType(INamedTypeSymbol type, bool scanned)
			=> Hidden([type,])
				? $"The {(scanned ? "scanned " : string.Empty)}type '{Display(type.ToDisplayString(FullyQualified))}'"
				: null;

		/// <summary>
		///     The AWT226 subject when the base can call a constructor of <paramref name="type" /> but the container
		///     cannot call any, else <see langword="null" />.
		/// </summary>
		public string? InaccessibleConstructor(INamedTypeSymbol type)
			=> !type.IsAbstract && type.TypeKind != TypeKind.Interface && Hidden(type.InstanceConstructors)
				? $"The constructor of '{Display(type.ToDisplayString(FullyQualified))}'"
				: null;

		/// <summary>
		///     The AWT226 subject when the base can access a member <paramref name="name" /> of <paramref name="owner" />
		///     but the container cannot access any, else <see langword="null" />.
		/// </summary>
		public string? InaccessibleMember(INamedTypeSymbol owner, string? name, string kind)
			=> !string.IsNullOrEmpty(name) && Hidden(owner.GetMembers(name!))
				? $"The {kind} '{Display(owner.ToDisplayString(FullyQualified))}.{name}'"
				: null;

		/// <summary>
		///     Whether the container can access none of <paramref name="candidates" /> although the base can access one.
		///     A base in another assembly compiled, so it reached what it names; the container may not even see such a
		///     member, which is then not imported and leaves no candidate. A base in the container's own assembly is
		///     checked directly: a candidate it cannot access either is its own problem, which it reports itself.
		/// </summary>
		private bool Hidden(IEnumerable<ISymbol> candidates)
		{
			List<ISymbol> symbols = candidates.ToList();
			if (symbols.Any(symbol => _compilation.IsSymbolAccessibleWithin(symbol, Container)))
			{
				return false;
			}

			return !SymbolEqualityComparer.Default.Equals(Base.ContainingAssembly, Container.ContainingAssembly)
			       || symbols.Any(symbol => _compilation.IsSymbolAccessibleWithin(symbol, Base));
		}

		/// <summary>
		///     The AWT226 subject when an inherited decorator or composite <paramref name="type" /> cannot be constructed
		///     from the container, else <see langword="null" />.
		/// </summary>
		public string? InaccessibleConstructedType(INamedTypeSymbol type)
			=> InaccessibleType(type.OriginalDefinition, scanned: false) ?? InaccessibleConstructor(type.OriginalDefinition);
	}
}
