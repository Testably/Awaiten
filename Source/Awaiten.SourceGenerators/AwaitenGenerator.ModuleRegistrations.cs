using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
	///     name, or a service it cannot name, never reaches such a container, so neither is self-compiled. An
	///     implementation hidden in the module (see <see cref="IsHiddenInModule" />) is self-compiled whatever the
	///     module's accessibility, since only the module can construct it, unless the factory, which is as visible as
	///     the module, could not return the service. The module's build and every importing container ask this same
	///     question.
	/// </summary>
	private static bool SelfCompilesRegistration(INamedTypeSymbol module, INamedTypeSymbol implementation, INamedTypeSymbol service, Compilation compilation)
		=> implementation is { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsUnboundGenericType: false, }
		   && ((IsExternallyAccessible(module)
		        && IsExternallyAccessible(service)
		        && !(IsExternallyAccessible(implementation)
		             && implementation.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public)))
		       || (IsHiddenInModule(implementation, module, compilation) && LessAccessibleThanContainer(service, module) is null));

	/// <summary>
	///     Whether <paramref name="module" /> can access <paramref name="type" /> but the rest of its assembly cannot,
	///     because the type is nested privately (or protected) in the module or in a class containing it, at any depth.
	///     A container constructs such a type only through the module's generated factory.
	/// </summary>
	private static bool IsHiddenInModule(INamedTypeSymbol type, INamedTypeSymbol module, Compilation compilation)
		=> !compilation.IsSymbolAccessibleWithin(type, module.ContainingAssembly)
		   && compilation.IsSymbolAccessibleWithin(type, module);

	/// <summary>
	///     The type hidden in <paramref name="module" /> (see <see cref="IsHiddenInModule" />) that
	///     <paramref name="type" /> is, or carries as an array element or type argument, or <see langword="null" />. A
	///     type argument is asked first, because a constructed type that carries a hidden one counts as hidden itself.
	/// </summary>
	private static INamedTypeSymbol? TypeHiddenInModule(ITypeSymbol type, INamedTypeSymbol module, Compilation compilation)
		=> type switch
		{
			IArrayTypeSymbol array => TypeHiddenInModule(array.ElementType, module, compilation),
			INamedTypeSymbol named => named.TypeArguments.Select(argument => TypeHiddenInModule(argument, module, compilation)).FirstOrDefault(hidden => hidden is not null)
			                          ?? (IsHiddenInModule(named, module, compilation) ? named : null),
			_ => null,
		};

	/// <summary>
	///     What the modules in a container's own assembly generate, which the container cannot see because it is output
	///     of the same generator run: the matches of their scans and the factories of their self-compiled registrations.
	///     Computed by the module pipeline's own logic, once per module and container build, with the diagnostics
	///     discarded, since the module's own build reports them at the same locations.
	/// </summary>
	private sealed class SameCompilationModules
	{
		private readonly Compilation _compilation;
		private readonly Dictionary<INamedTypeSymbol, List<ModuleScanExpansion>> _scans = new(SymbolEqualityComparer.Default);
		private readonly Dictionary<INamedTypeSymbol, List<RegistrationFactory>> _registrations = new(SymbolEqualityComparer.Default);

		public SameCompilationModules(Compilation compilation) => _compilation = compilation;

		/// <summary>The matches of the module's scans (see <see cref="CollectModuleScanFactories" />).</summary>
		public List<ModuleScanExpansion> Scans(INamedTypeSymbol module)
		{
			if (!_scans.TryGetValue(module, out List<ModuleScanExpansion>? scans))
			{
				_scans[module] = scans = CollectModuleScanFactories(module, _compilation, new List<DiagnosticInfo>(), CancellationToken.None);
			}

			return scans;
		}

		/// <summary>
		///     The factory the module generates for <paramref name="implementation" /> (see
		///     <see cref="CollectModuleRegistrationFactories" />), or <see langword="null" /> when it generates none, for a
		///     reason its own build reports.
		/// </summary>
		public RegistrationFactory? Registration(INamedTypeSymbol module, INamedTypeSymbol implementation)
		{
			if (!_registrations.TryGetValue(module, out List<RegistrationFactory>? factories))
			{
				_registrations[module] = factories = ReceivesGeneratedMembers(module)
					? CollectModuleRegistrationFactories(module, _compilation, new List<DiagnosticInfo>(), Scans(module).Select(expansion => expansion.Factory).ToList(), new List<SkippedRegistration>())
					: new List<RegistrationFactory>();
			}

			string name = RegistrationFactoryName(implementation);
			return factories.FirstOrDefault(factory => factory.FactoryName == name);
		}
	}

	/// <summary>
	///     Whether the module pipeline emits into <paramref name="module" />: a non-generic, static, <c>partial</c>
	///     module whose enclosing types are <c>partial</c> too (see <c>BuildModuleModel</c>, which reports otherwise).
	/// </summary>
	private static bool ReceivesGeneratedMembers(INamedTypeSymbol module)
		=> !HasOpenTypeParameters(module)
		   && module.IsStatic
		   && module.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ClassDeclarationSyntax declaration
		                                                        && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
		   && ContainingTypesArePartial(module);

	/// <summary>
	///     Whether the generated factory of <paramref name="implementation" /> carries a wrapper for the hook
	///     <paramref name="hookName" /> in the given slot, which it lacks when the module's own build rejected the hook.
	/// </summary>
	private static bool HasHookWrapper(RegistrationFactory factory, INamedTypeSymbol implementation, string? hookName, bool release)
		=> hookName is not null
		   && factory.Hooks.AsArray().Any(hook => hook.Release == release && hook.Hook.WrapperName == RegistrationHookWrapperName(implementation, release, hookName));

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
			.FirstOrDefault(registration => SelfCompilesRegistration(module, registration.Implementation, registration.Service, compilation));

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
	///     returns one service and two would split the shared instance. When the module's assembly grants
	///     <c>InternalsVisibleTo</c> to any assembly, an implementation that fails only with AWT195, AWT197 or AWT200
	///     gets no factory and no error: such an assembly constructs it itself. It lands in <paramref name="skipped" />
	///     with the reason, which a consumer that cannot construct it reports (AWT231). A type hidden in the module
	///     (see <see cref="TypeHiddenInModule" />) is never skipped, since no other assembly can construct it.
	/// </summary>
	private static List<RegistrationFactory> CollectModuleRegistrationFactories(
		INamedTypeSymbol module,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics,
		IReadOnlyList<ModuleFactory> scanFactories,
		List<SkippedRegistration> skipped)
	{
		ModuleScanContext context = new(module, compilation, diagnostics, new HashSet<string>(StringComparer.Ordinal));
		List<(ModuleRegistration First, List<ModuleRegistration> All)> implementations = new();
		foreach (ModuleRegistration registration in ConstructedModuleRegistrations(module, compilation))
		{
			if (!SelfCompilesRegistration(module, registration.Implementation, registration.Service, compilation))
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

		bool friendsCanConstruct = GrantsInternalsVisibleTo(compilation.Assembly);
		List<RegistrationFactory> factories = new();
		foreach ((ModuleRegistration first, List<ModuleRegistration> all) in implementations)
		{
			if (RegistrationFactoryOf(first, all, context, scanFactories, friendsCanConstruct, skipped) is { } factory)
			{
				factories.Add(factory);
			}
		}

		return factories;
	}

	/// <summary>
	///     The factory of one self-compiled implementation from its registrations, or <see langword="null" /> after
	///     reporting why it cannot be built or recording it in <paramref name="skipped" /> (see
	///     <see cref="CollectModuleRegistrationFactories" />).
	/// </summary>
	private static RegistrationFactory? RegistrationFactoryOf(
		ModuleRegistration first,
		List<ModuleRegistration> all,
		ModuleScanContext context,
		IReadOnlyList<ModuleFactory> scanFactories,
		bool friendsCanConstruct,
		List<SkippedRegistration> skipped)
	{
		INamedTypeSymbol implementation = first.Implementation;
		string typeName = implementation.ToDisplayString(FullyQualified);
		SelfCompiledSubject subject = RegistrationSubject(typeName);
		INamedTypeSymbol returnType = IsExternallyAccessible(implementation) ? implementation : first.Service;
		string returnName = returnType.ToDisplayString(FullyQualified);
		List<DiagnosticInfo> rejections = new();

		string[] services = all.Select(registration => registration.Service.ToDisplayString(FullyQualified)).Distinct().ToArray();
		foreach (ModuleRegistration registration in all)
		{
			string service = registration.Service.ToDisplayString(FullyQualified);
			if (!SymbolEqualityComparer.Default.Equals(returnType, implementation) && service != returnName)
			{
				ReportRegistrationExposures(subject, $"{Display(returnName)}, {Display(service)}", "register it under a single service, or make the implementation public", registration.Location, rejections);
			}
		}

		// A scan factory builds the type for every consumer, so one constructing the registration itself would still split it.
		bool scannedToo = false;
		if (scanFactories.FirstOrDefault(factory => factory.ImplementationType == typeName && factory.Skip is null) is { } scanned)
		{
			ReportRegistrationExposures(subject, $"{Display(scanned.ServiceType)}, {Display(returnName)}", "remove the registration or exclude the type from the scan", first.Location, rejections);
			scannedToo = true;
		}

		if (!TryMirrorFactoryParameters(implementation, context with { Diagnostics = rejections, }, first.Location, subject, out EquatableArray<FactoryParameter> parameters)
		    || rejections.Count > 0)
		{
			string?[] reasons = rejections.Select(rejection => SkipReason(rejection, services)).ToArray();
			if (!scannedToo
			    && friendsCanConstruct
			    && TypeHiddenInModule(implementation, context.Module, context.Compilation) is null
			    && reasons.All(reason => reason is not null))
			{
				skipped.Add(new SkippedRegistration(RegistrationSkippedName(implementation), reasons[0]!));
			}
			else
			{
				context.Diagnostics.AddRange(rejections);
			}

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

	/// <summary>
	///     Why the module generates no factory for an implementation, from the diagnostic the factory would have
	///     failed with, or <see langword="null" /> when that diagnostic is not one an assembly granted
	///     <c>InternalsVisibleTo</c> avoids by constructing the implementation itself.
	/// </summary>
	private static string? SkipReason(DiagnosticInfo rejection, string[] services)
	{
		if (rejection.Descriptor == Diagnostics.ModuleScanParameterInaccessible)
		{
			return $"its constructor parameter of type '{rejection.MessageArgs.AsArray()[1]}' is not accessible outside the module's assembly";
		}

		if (rejection.Descriptor == Diagnostics.ModuleScanInjectionMetadata)
		{
			return rejection.MessageArgs.AsArray()[1];
		}

		return rejection.Descriptor == Diagnostics.ModuleScanMultipleExposures
			? $"it is registered under several services ({string.Join(", ", services.Select(Display))})"
			: null;
	}

	/// <summary>Whether <paramref name="assembly" /> grants <c>InternalsVisibleTo</c> to any assembly.</summary>
	private static bool GrantsInternalsVisibleTo(IAssemblySymbol assembly)
		=> assembly.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InternalsVisibleToAttribute");

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
	///     A deterministic name for the constant that records why the module generates no factory for an
	///     implementation, derived like <see cref="RegistrationFactoryName" />. Its presence tells a consumer that the
	///     module was expanded and skipped the factory on purpose (AWT231), not built without the generator (AWT154).
	/// </summary>
	private static string RegistrationSkippedName(INamedTypeSymbol implementation)
		=> $"Awaiten__RegistrationSkipped_{implementation.Name}_{Fnv1a(implementation.ToDisplayString(FullyQualified)):x8}";

	/// <summary>
	///     Why the imported module of another assembly generated no factory for <paramref name="implementation" />,
	///     or <see langword="null" /> when it records no such skip.
	/// </summary>
	private static string? SkippedRegistrationReason(INamedTypeSymbol module, INamedTypeSymbol implementation)
		=> module.GetMembers(RegistrationSkippedName(implementation))
			.OfType<IFieldSymbol>()
			.Select(field => field.ConstantValue as string)
			.FirstOrDefault(reason => reason is not null);

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

	/// <summary>Whether the container's compilation can construct <paramref name="implementation" /> itself.</summary>
	private static bool CanConstruct(INamedTypeSymbol implementation, Compilation compilation)
		=> implementation.InstanceConstructors.Any(constructor => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly));
}
