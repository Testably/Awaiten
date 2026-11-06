using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     One closed constructor-produced lifetime registration declared on a <c>[Module]</c>: the attribute, its
	///     implementation and service, and where it is declared.
	/// </summary>
	private sealed record ModuleRegistration(AttributeData Attribute, INamedTypeSymbol Implementation, INamedTypeSymbol Service, Location? Location);

	/// <summary>
	///     Whether a module registration is self-compiled: the module and the service can be named from another
	///     assembly, but the implementation cannot be constructed there, because it or every constructor is not
	///     <c>public</c>. Its own build then emits a factory that constructs the implementation, so a container in
	///     another assembly can import the module without <c>InternalsVisibleTo</c>. A module another assembly cannot
	///     name, or a service it cannot name, never reaches such a container, so neither is self-compiled. The
	///     module's build and every importing container ask this same question.
	/// </summary>
	private static bool SelfCompilesRegistration(INamedTypeSymbol module, INamedTypeSymbol implementation, INamedTypeSymbol service)
		=> IsExternallyAccessible(module)
		   && IsExternallyAccessible(service)
		   && implementation is { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsUnboundGenericType: false, }
		   && !(IsExternallyAccessible(implementation)
		        && implementation.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public));

	/// <summary>
	///     The module's closed lifetime registrations that the container constructs (no <c>Factory</c> or
	///     <c>Instance</c>), from the generic form or a closed <c>typeof</c> pair. Malformed and open generic forms
	///     are left to the container, which reports them.
	/// </summary>
	private static IEnumerable<ModuleRegistration> ConstructedModuleRegistrations(INamedTypeSymbol module, Compilation compilation)
	{
		foreach (AttributeData attribute in module.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "SingletonAttribute" or "TransientAttribute" or "ScopedAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || ReadProduction(attribute).Production != ProductionKind.Constructor)
			{
				continue;
			}

			INamedTypeSymbol? implementation;
			INamedTypeSymbol? service;
			if (attributeClass.IsGenericType)
			{
				ImmutableArrayGuard(attributeClass.TypeArguments, out ITypeSymbol? implementationType, out ITypeSymbol? serviceType);
				implementation = implementationType as INamedTypeSymbol;
				service = (serviceType ?? implementationType) as INamedTypeSymbol;
			}
			else if (TryReadTypeofPair(attribute, out INamedTypeSymbol typeofImplementation, out INamedTypeSymbol typeofService)
			         && !typeofImplementation.IsUnboundGenericType
			         && !typeofService.IsUnboundGenericType
			         && IsAssignableRegistration(typeofImplementation, typeofService, compilation))
			{
				implementation = typeofImplementation;
				service = typeofService;
			}
			else
			{
				continue;
			}

			if (implementation is not null && service is not null)
			{
				yield return new ModuleRegistration(attribute, implementation, service, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
			}
		}
	}

	/// <summary>The first registration of <paramref name="module" /> that is self-compiled, or <see langword="null" />.</summary>
	private static ModuleRegistration? FirstSelfCompiledRegistration(INamedTypeSymbol module, Compilation compilation)
		=> ConstructedModuleRegistrations(module, compilation)
			.FirstOrDefault(registration => SelfCompilesRegistration(module, registration.Implementation, registration.Service));

	/// <summary>
	///     Builds the factory of every self-compiled registration of a module (see
	///     <see cref="SelfCompilesRegistration" />), in the module's own build. One factory per implementation
	///     constructs it through the constructor a self-compiled scan match would use and returns the implementation
	///     when another assembly can name it, else the registered service. A consumer finds the factory by its
	///     deterministic name and keeps reading every other option off the registration attribute itself. Each hook a
	///     registration names gets a public wrapper, so a consumer runs the module's possibly internal hook. Rejects
	///     what the factory cannot express: an inaccessible constructor parameter (AWT195), injection metadata
	///     (AWT200), hook parameters the wrapper cannot mirror (AWT203/AWT204), and an inaccessible implementation
	///     exposed under several services or also matched by one of the module's scans (AWT197), since one factory
	///     returns one service and two would split the shared instance.
	/// </summary>
	private static List<RegistrationFactory> CollectModuleRegistrationFactories(
		INamedTypeSymbol module,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics,
		IReadOnlyList<ModuleFactory> scanFactories)
	{
		ModuleScanContext context = new(module, compilation, diagnostics, new HashSet<string>(StringComparer.Ordinal));
		List<(ModuleRegistration First, List<ModuleRegistration> All)> implementations = new();
		foreach (ModuleRegistration registration in ConstructedModuleRegistrations(module, compilation))
		{
			if (!SelfCompilesRegistration(module, registration.Implementation, registration.Service))
			{
				continue;
			}

			int index = implementations.FindIndex(entry => SymbolEqualityComparer.Default.Equals(entry.First.Implementation, registration.Implementation));
			if (index < 0)
			{
				implementations.Add((registration, new List<ModuleRegistration> { registration, }));
			}
			else
			{
				implementations[index].All.Add(registration);
			}
		}

		List<RegistrationFactory> factories = new();
		foreach ((ModuleRegistration first, List<ModuleRegistration> all) in implementations)
		{
			if (RegistrationFactoryOf(first, all, context, scanFactories) is { } factory)
			{
				factories.Add(factory);
			}
		}

		return factories;
	}

	/// <summary>
	///     The factory of one self-compiled implementation from its registrations, or <see langword="null" /> after
	///     reporting why it cannot be built (see <see cref="CollectModuleRegistrationFactories" />).
	/// </summary>
	private static RegistrationFactory? RegistrationFactoryOf(
		ModuleRegistration first,
		List<ModuleRegistration> all,
		ModuleScanContext context,
		IReadOnlyList<ModuleFactory> scanFactories)
	{
		INamedTypeSymbol implementation = first.Implementation;
		string typeName = implementation.ToDisplayString(FullyQualified);
		SelfCompiledSubject subject = RegistrationSubject(typeName);
		INamedTypeSymbol returnType = IsExternallyAccessible(implementation) ? implementation : first.Service;
		string returnName = returnType.ToDisplayString(FullyQualified);

		bool exposable = true;
		foreach (ModuleRegistration registration in all)
		{
			string service = registration.Service.ToDisplayString(FullyQualified);
			if (!SymbolEqualityComparer.Default.Equals(returnType, implementation) && service != returnName)
			{
				ReportRegistrationExposures(subject, $"{Display(returnName)}, {Display(service)}", "register it under a single service, or make the implementation public", registration.Location, context.Diagnostics);
				exposable = false;
			}
		}

		if (scanFactories.FirstOrDefault(factory => factory.ImplementationType == typeName) is { } scanned)
		{
			ReportRegistrationExposures(subject, $"{Display(scanned.ServiceType)}, {Display(returnName)}", "remove the registration or exclude the type from the scan", first.Location, context.Diagnostics);
			exposable = false;
		}

		if (!TryMirrorFactoryParameters(implementation, context, first.Location, subject, out EquatableArray<FactoryParameter> parameters) || !exposable)
		{
			return null;
		}

		List<RegistrationHook> hooks = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (ModuleRegistration registration in all)
		{
			foreach (bool release in new[] { false, true, })
			{
				if (NamedArgument(registration.Attribute, release ? "OnRelease" : "OnActivated") is not { } hookName
				    || !seen.Add((release ? "OnRelease\0" : "OnActivated\0") + hookName))
				{
					continue;
				}

				ModuleHookRequest request = new(implementation, hookName, registration.Location, release, RegistrationHookWrapperName(implementation, release, hookName), subject);
				if (ResolveModuleHookCore(request, null, context) is { } resolved)
				{
					hooks.Add(new RegistrationHook(resolved.Wrapper, release));
				}
			}
		}

		return new RegistrationFactory(
			RegistrationFactoryName(implementation),
			returnName,
			typeName,
			parameters,
			new EquatableArray<RegistrationHook>(hooks.ToArray()));
	}

	private static void ReportRegistrationExposures(SelfCompiledSubject subject, string exposures, string remedy, Location? location, List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.ModuleScanMultipleExposures,
			LocationInfo.From(location),
			new EquatableArray<string>([subject.Subject, exposures, remedy,])));

	/// <summary>The subject of a self-compilation diagnostic about a module registration.</summary>
	private static SelfCompiledSubject RegistrationSubject(string typeName)
		=> new(typeName, $"'{Display(typeName)}' is self-compiled from a module registration", "make the implementation and its constructor public");

	/// <summary>
	///     A deterministic name for the factory of a self-compiled registration, derived from the implementation's
	///     fully-qualified name only, so the module's build and a consuming container agree on it and it survives
	///     unrelated changes to the library. Distinct from a scan factory's name (<see cref="ModuleFactoryName" />).
	/// </summary>
	private static string RegistrationFactoryName(INamedTypeSymbol implementation)
		=> $"Awaiten__Registration_{implementation.Name}_{Fnv1a(implementation.ToDisplayString(FullyQualified)):x8}";

	/// <summary>
	///     A deterministic name for the wrapper of one hook a self-compiled registration names, derived from the
	///     implementation, the slot and the hook's name, so two registrations naming different hooks get two wrappers
	///     and a consumer still sees them as different hooks.
	/// </summary>
	private static string RegistrationHookWrapperName(INamedTypeSymbol implementation, bool release, string hookName)
	{
		string slot = release ? "OnRelease" : "OnActivated";
		return $"Awaiten__Hook_{slot}_{implementation.Name}_{Fnv1a($"{implementation.ToDisplayString(FullyQualified)}\0{slot}\0{hookName}"):x8}";
	}

	private static uint Fnv1a(string text)
	{
		uint hash = 2166136261;
		foreach (char character in text)
		{
			hash = unchecked((hash ^ character) * 16777619);
		}

		return hash;
	}

	/// <summary>
	///     The generated factory of a self-compiled registration on an imported module of another assembly, or
	///     <see langword="null" /> when the module carries none (built without the generator, or by a version
	///     predating self-compiled registrations).
	/// </summary>
	private static IMethodSymbol? SelfCompiledFactory(INamedTypeSymbol module, INamedTypeSymbol implementation)
		=> module.GetMembers(RegistrationFactoryName(implementation))
			.OfType<IMethodSymbol>()
			.FirstOrDefault(method => method is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, ReturnType: INamedTypeSymbol, });

	/// <summary>Whether the container's compilation can construct <paramref name="implementation" /> itself.</summary>
	private static bool CanConstruct(INamedTypeSymbol implementation, Compilation compilation)
		=> implementation.InstanceConstructors.Any(constructor => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly));
}
