using System.Collections.Immutable;
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
		private readonly Dictionary<INamedTypeSymbol, List<RegistrationFactory>> _contributions = new(SymbolEqualityComparer.Default);

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

		/// <summary>
		///     The factory named <paramref name="name" /> the module generates for a decorator or composite (see
		///     <see cref="CollectModuleContributionFactories" />), or <see langword="null" />.
		/// </summary>
		public RegistrationFactory? Contribution(INamedTypeSymbol module, string name)
		{
			if (!_contributions.TryGetValue(module, out List<RegistrationFactory>? factories))
			{
				_contributions[module] = factories = ReceivesGeneratedMembers(module)
					? CollectModuleContributionFactories(module, _compilation)
					: new List<RegistrationFactory>();
			}

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

	/// <summary>
	///     The public wrappers a module another assembly can name emits for the <c>Factory</c> and <c>Instance</c> members
	///     its closed registrations name when a member is not public but its assembly can reach it, so a container in
	///     another assembly calls the wrapper instead of the member it cannot see. A member gets one only when its
	///     signature is public throughout, it is not generic, and no parameter carries an attribute the wrapper cannot
	///     carry; otherwise a consumer reports the member as before (AWT108, AWT109).
	/// </summary>
	private static List<MemberWrapper> CollectModuleMemberWrappers(INamedTypeSymbol module, Compilation compilation)
	{
		List<MemberWrapper> wrappers = new();
		if (!IsExternallyAccessible(module))
		{
			return wrappers;
		}

		foreach (AttributeData attribute in module.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "SingletonAttribute" or "TransientAttribute" or "ScopedAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || ReadProduction(attribute) is not { Production: ProductionKind.Factory or ProductionKind.Instance, Member: { } member, Conflicting: false, } production
			    || ProducedRegistrationType(attribute, attributeClass) is not { } produced
			    || AccessibleMembers(module, member, compilation).Any(candidate => candidate.DeclaredAccessibility == Accessibility.Public))
			{
				continue;
			}

			MemberWrapper? wrapper = production.Production == ProductionKind.Factory
				? FactoryWrapper(member, produced)
				: InstanceWrapper(member, produced);
			if (wrapper is not null && wrappers.All(existing => existing.Name != wrapper.Name))
			{
				wrappers.Add(wrapper);
			}
		}

		return wrappers;

		MemberWrapper? FactoryWrapper(string member, INamedTypeSymbol produced)
		{
			List<IMethodSymbol> candidates = FindFactoryCandidates(module, member, produced, compilation)
				.Where(candidate => compilation.IsSymbolAccessibleWithin(candidate, module.ContainingAssembly))
				.ToList();
			return candidates.Count == 1
			       && candidates[0] is { IsStatic: true, IsGenericMethod: false, } method
			       && IsExternallyAccessible(method.ReturnType)
			       && method.Parameters.All(parameter => IsExternallyAccessible(parameter.Type) && parameter.RefKind == RefKind.None && UnmirroredAttribute(parameter.GetAttributes()) is null)
				? new MemberWrapper(MemberWrapperName("Factory", member, produced), method.ReturnType.ToDisplayString(FullyQualifiedWithNullability), member, MirroredParameters(method.Parameters))
				: null;
		}

		MemberWrapper? InstanceWrapper(string member, INamedTypeSymbol produced)
		{
			foreach (ISymbol candidate in AccessibleMembers(module, member, compilation))
			{
				ITypeSymbol? type = candidate switch
				{
					IFieldSymbol { IsStatic: true, } field => field.Type,
					IPropertySymbol { IsStatic: true, GetMethod: not null, } property => property.Type,
					_ => null,
				};
				if (type is not null
				    && compilation.IsSymbolAccessibleWithin(candidate, module.ContainingAssembly)
				    && compilation.HasImplicitConversion(type, produced)
				    && IsExternallyAccessible(type))
				{
					return new MemberWrapper(MemberWrapperName("Instance", member, produced), type.ToDisplayString(FullyQualifiedWithNullability), member, default, IsProperty: true);
				}
			}

			return null;
		}
	}

	/// <summary>
	///     The factories a module generates for its closed decorators whose type another assembly cannot construct (see
	///     <see cref="SelfCompilesRegistration" />), one per type and decorated service. Each mirrors the constructor and
	///     the injected properties like the factory of a registration, so its inner parameter stays the one a container
	///     redirects to the next link of the chain. A decorator the factory cannot express gets none and no error in the
	///     module's build: a container that cannot construct it reports it at its <c>[Import]</c> (AWT230), as before.
	/// </summary>
	private static List<RegistrationFactory> CollectModuleContributionFactories(INamedTypeSymbol module, Compilation compilation)
	{
		List<RegistrationFactory> factories = new();
		ModuleScanContext context = new(module, compilation, new List<DiagnosticInfo>(), new HashSet<string>(StringComparer.Ordinal));
		foreach (AttributeData attribute in module.GetAttributes())
		{
			if (attribute.AttributeClass is not { Name: "DecorateAttribute", } attributeClass
			    || attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace
			    || ContributedPair(attribute, attributeClass) is not ({ } type, { } service)
			    || !SelfCompilesRegistration(module, type, service, compilation))
			{
				continue;
			}

			string name = ContributionFactoryName(attributeClass.Name, type, service);
			SelfCompiledSubject subject = RegistrationSubject(type.ToDisplayString(FullyQualified));
			if (factories.Any(factory => factory.FactoryName == name)
			    || FactoryReturnType(type, [service,], context) is not { } returnType
			    || !TryMirrorFactoryParameters(type, context, null, subject, out EquatableArray<FactoryParameter> parameters, mirrorProperties: true)
			    || !TryMirrorInjectedProperties(type, context, null, subject, ref parameters, out EquatableArray<string> members))
			{
				continue;
			}

			factories.Add(new RegistrationFactory(name, returnType.ToDisplayString(FullyQualified), type.ToDisplayString(FullyQualified), parameters, default, members));
		}

		return factories;
	}

	/// <summary>
	///     The type and service of the closed, generic form of a <c>[Decorate]</c> or <c>[Composite]</c>, or
	///     <see langword="null" /> types for the <c>typeof</c> form, which names an open generic.
	/// </summary>
	private static (INamedTypeSymbol? Type, INamedTypeSymbol? Service) ContributedPair(AttributeData attribute, INamedTypeSymbol attributeClass)
		=> attributeClass is { IsGenericType: true, TypeArguments.Length: 2, }
			? (attributeClass.TypeArguments[0] as INamedTypeSymbol, attributeClass.TypeArguments[1] as INamedTypeSymbol)
			: (null, null);

	/// <summary>
	///     A deterministic name for the factory of a decorator or composite, derived from its type and the service it
	///     decorates or composes, so one type serving two services gets two factories and the owner's build and a
	///     consuming container agree on the name.
	/// </summary>
	private static string ContributionFactoryName(string attribute, INamedTypeSymbol type, INamedTypeSymbol service)
		=> ContributionFactoryName(attribute, type, service.ToDisplayString(FullyQualified));

	/// <inheritdoc cref="ContributionFactoryName(string, INamedTypeSymbol, INamedTypeSymbol)" />
	private static string ContributionFactoryName(string attribute, INamedTypeSymbol type, string service)
		=> $"Awaiten__{(attribute == "CompositeAttribute" ? "Composite" : "Decorator")}_{type.Name}_{Fnv1a($"{type.ToDisplayString(FullyQualified)}\0{service}"):x8}";

	/// <summary>
	///     The type a closed lifetime registration produces: the implementation of the generic form, or of a closed
	///     <c>typeof</c> pair, or <see langword="null" /> for an open or malformed one.
	/// </summary>
	private static INamedTypeSymbol? ProducedRegistrationType(AttributeData attribute, INamedTypeSymbol attributeClass)
	{
		if (attributeClass.IsGenericType)
		{
			return attributeClass.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
		}

		return TryReadTypeofPair(attribute, out INamedTypeSymbol implementation, out _) && !implementation.IsUnboundGenericType
			? implementation
			: null;
	}

	/// <summary>The first registration of <paramref name="module" /> that is self-compiled, or <see langword="null" />.</summary>
	private static ModuleRegistration? FirstSelfCompiledRegistration(INamedTypeSymbol module, Compilation compilation)
		=> ConstructedModuleRegistrations(module, compilation)
			.FirstOrDefault(registration => SelfCompilesRegistration(module, registration.Implementation, registration.Service, compilation));

	/// <summary>
	///     Builds the factory of every self-compiled registration of a module (see
	///     <see cref="SelfCompilesRegistration" />), in the module's own build. One factory per implementation
	///     constructs it through the constructor a self-compiled scan match would use, carrying the <c>[Arg]</c> and
	///     <c>[FromKey]</c> of its parameters and taking each <c>[Inject]</c> property as a parameter, and returns a type
	///     every service converts from (see <see cref="FactoryReturnType" />). A consumer finds the factory by its
	///     deterministic name and keeps reading every other option off the registration attribute itself. Each hook a
	///     registration names gets a public wrapper, so a consumer runs the module's possibly internal hook. Rejects
	///     what the factory cannot express: an inaccessible constructor parameter (AWT195), injection metadata a
	///     parameter cannot carry (AWT200), hook parameters the wrapper cannot mirror (AWT203/AWT204), and an
	///     inaccessible implementation under several services no accessible type covers, or also matched by one of the
	///     module's scans (AWT197), since two factories would split the shared instance. When the module's assembly grants
	///     <c>InternalsVisibleTo</c> to any assembly, an implementation that fails only with AWT195, AWT197 or AWT200
	///     gets no factory and no error: such an assembly constructs it itself. It lands in <paramref name="skipped" />
	///     with the reason, which a consumer that cannot construct it reports (AWT231). A type hidden in the module
	///     (see <see cref="TypeHiddenInModule" />) is never skipped, since no other assembly can construct it. A hook
	///     whose wrapper fails only with AWT203 or AWT204 is skipped the same way: the factory stays, and the hook lands
	///     in <paramref name="skipped" />, since such an assembly calls it directly.
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
		List<DiagnosticInfo> rejections = new();

		string[] services = all.Select(registration => registration.Service.ToDisplayString(FullyQualified)).Distinct().ToArray();
		INamedTypeSymbol? returnType = FactoryReturnType(implementation, all.Select(registration => registration.Service).ToList(), context);
		if (returnType is null)
		{
			ReportRegistrationExposures(
				subject,
				string.Join(", ", services.Select(Display)),
				"register it under a single service, give it a public base type or interface that covers its services, or make the implementation public",
				all.First(registration => !SymbolEqualityComparer.Default.Equals(registration.Service, first.Service)).Location,
				rejections);
		}

		string returnName = (returnType ?? first.Service).ToDisplayString(FullyQualified);

		// A scan factory builds the type for every consumer, so one constructing the registration itself would still split it.
		bool scannedToo = false;
		if (scanFactories.FirstOrDefault(factory => factory.ImplementationType == typeName && factory.Skip is null) is { } scanned)
		{
			ReportRegistrationExposures(subject, $"{Display(scanned.ServiceType)}, {Display(returnName)}", "remove the registration or exclude the type from the scan", first.Location, rejections);
			scannedToo = true;
		}

		EquatableArray<string> members = default;
		if (!TryMirrorFactoryParameters(implementation, context with { Diagnostics = rejections, }, first.Location, subject, out EquatableArray<FactoryParameter> parameters, mirrorProperties: true)
		    || !TryMirrorInjectedProperties(implementation, context with { Diagnostics = rejections, }, first.Location, subject, ref parameters, out members)
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

				ModuleHookRequest request = new(
					implementation, hookName, registration.Location, release, RegistrationHookWrapperName(implementation, release, hookName), subject,
					friendsCanConstruct && TypeHiddenInModule(implementation, context.Module, context.Compilation) is null);
				if (ResolveModuleHookCore(request, null, context) is not { } resolved)
				{
					continue;
				}

				if (resolved.Wrapper.SkipReason is { } reason)
				{
					skipped.Add(new SkippedRegistration(RegistrationHookSkippedName(implementation, release, hookName), reason, Hook: true));
				}
				else
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
			new EquatableArray<RegistrationHook>(hooks.ToArray()),
			members);
	}

	/// <summary>
	///     Appends a parameter per <c>[Inject]</c> property of <paramref name="type" /> to the mirrored constructor
	///     <paramref name="parameters" />, named like the property and carrying its <c>[FromKey]</c>, so the factory fills
	///     the property through an object initializer and a consumer resolves it like any other dependency. Reports
	///     AWT200 and returns <see langword="false" /> for what a factory parameter cannot express: a property assigned
	///     after construction (<c>Deferred</c>), one left out when its service is missing (<c>Optional</c>), one without a
	///     setter the module can reach, one named like a constructor parameter, and one whose type another assembly
	///     cannot name.
	/// </summary>
	private static bool TryMirrorInjectedProperties(
		INamedTypeSymbol type,
		ModuleScanContext context,
		Location? location,
		SelfCompiledSubject subject,
		ref EquatableArray<FactoryParameter> parameters,
		out EquatableArray<string> members)
	{
		members = default;
		List<FactoryParameter> mirrored = new(parameters.AsArray());
		List<string> names = new();
		foreach (IPropertySymbol property in InjectedProperties(type))
		{
			ImmutableArray<AttributeData> attributes = property.GetAttributes();
			string? reason = null;
			if (IsInjectDeferred(attributes))
			{
				reason = "is marked [Inject(Deferred = true)]";
			}
			else if (IsInjectOptional(attributes))
			{
				reason = "is marked [Inject(Optional = true)]";
			}
			else if (property.SetMethod is not { } setter || !context.Compilation.IsSymbolAccessibleWithin(setter, context.Module))
			{
				reason = "has no setter the module can reach";
			}
			else if (mirrored.Any(parameter => parameter.Name == property.Name))
			{
				reason = "has the name of a constructor parameter";
			}
			else if (HasAwaitenAttribute(attributes, "FromKeyAttribute") && FromKey(attributes) is null)
			{
				reason = "is marked [FromKey] with an unsupported key";
			}
			else if (!IsExternallyAccessible(property.Type) || TypeHiddenInModule(property.Type, context.Module, context.Compilation) is not null)
			{
				reason = $"has the type '{Display(property.Type.ToDisplayString(FullyQualified))}', which is not accessible outside the module's assembly";
			}

			if (reason is not null)
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ModuleScanInjectionMetadata,
					LocationInfo.From(location),
					new EquatableArray<string>([subject.Subject, $"its property '{property.Name}' {reason}", subject.Remedy,])));
				return false;
			}

			mirrored.Add(new FactoryParameter(property.Type.ToDisplayString(FullyQualifiedWithNullability), property.Name, KeyAttribute(FromKey(attributes))));
			names.Add(property.Name);
		}

		parameters = new EquatableArray<FactoryParameter>(mirrored.ToArray());
		members = new EquatableArray<string>(names.ToArray());
		return true;
	}

	/// <summary>
	///     The type the factory of <paramref name="implementation" /> returns, which every service it is registered under
	///     converts from, so one instance serves all of them: the implementation when another assembly can name it, else
	///     a registered service that covers the others, else the most derived base class, else the first interface in
	///     declaration order, among the types the factory may expose. <see langword="null" /> when none covers them all.
	/// </summary>
	private static INamedTypeSymbol? FactoryReturnType(INamedTypeSymbol implementation, List<INamedTypeSymbol> services, ModuleScanContext context)
	{
		if (IsExternallyAccessible(implementation))
		{
			return implementation;
		}

		bool hidden = IsHiddenInModule(implementation, context.Module, context.Compilation);
		return services
			.Concat(ServableTypes(implementation))
			.Where(candidate => IsExternallyAccessible(candidate) || (hidden && LessAccessibleThanContainer(candidate, context.Module) is null))
			.FirstOrDefault(candidate => services.All(service => context.Compilation.HasImplicitConversion(candidate, service)));
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

	/// <summary>
	///     Whether an assembly granted <c>InternalsVisibleTo</c> can construct every self-compiled registration of
	///     <paramref name="module" /> itself: the module's assembly grants it to any assembly, and no such registration
	///     is hidden in the module (see <see cref="TypeHiddenInModule" />).
	/// </summary>
	private static bool FriendsConstructEveryRegistration(INamedTypeSymbol module, Compilation compilation)
		=> GrantsInternalsVisibleTo(compilation.Assembly)
		   && !ConstructedModuleRegistrations(module, compilation).Any(registration =>
			   SelfCompilesRegistration(module, registration.Implementation, registration.Service, compilation)
			   && TypeHiddenInModule(registration.Implementation, module, compilation) is not null);

	/// <summary>
	///     A deterministic name for the internal type, in the module's namespace, that records a module the generator
	///     left to the assemblies granted <c>InternalsVisibleTo</c> because it is not partial. Its presence tells a
	///     consumer that the module skipped its factories on purpose (AWT231), not built without the generator (AWT154).
	/// </summary>
	private static string ModuleNotPartialName(INamedTypeSymbol module)
		=> $"Awaiten__ModuleNotPartial_{module.Name}_{Fnv1a(module.ToDisplayString(FullyQualified)):x8}";

	/// <summary>
	///     Why the imported module of another assembly can carry no generated factory, read from its metadata: it is
	///     generic, or it recorded that it is not partial (see <see cref="ModuleNotPartialName" />). Otherwise
	///     <see langword="null" />.
	/// </summary>
	private static string? ModuleShapeReason(INamedTypeSymbol module)
	{
		for (INamedTypeSymbol? type = module; type is not null; type = type.ContainingType)
		{
			if (type.IsGenericType)
			{
				return "the module is generic";
			}
		}

		INamedTypeSymbol definition = module.OriginalDefinition;
		string name = definition.ContainingNamespace is { IsGlobalNamespace: false, } ns
			? $"{ns.ToDisplayString()}.{ModuleNotPartialName(definition)}"
			: ModuleNotPartialName(definition);
		return module.ContainingAssembly.GetTypeByMetadataName(name) is not null
			? "the module, or a class that contains it, is not partial"
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

	/// <summary>
	///     A deterministic name for the constant that records why the module emits no wrapper for a hook of a
	///     self-compiled registration, derived like <see cref="RegistrationHookWrapperName" />.
	/// </summary>
	private static string RegistrationHookSkippedName(INamedTypeSymbol implementation, bool release, string hookName)
		=> "Awaiten__HookSkipped_" + RegistrationHookWrapperName(implementation, release, hookName).Substring("Awaiten__Hook_".Length);

	/// <summary>
	///     Why the imported module of another assembly generated no wrapper for a hook of a registration of
	///     <paramref name="implementation" />, or <see langword="null" /> when it records no such skip.
	/// </summary>
	private static string? SkippedHookReason(INamedTypeSymbol module, INamedTypeSymbol implementation, bool release, string hookName)
		=> module.GetMembers(RegistrationHookSkippedName(implementation, release, hookName))
			.OfType<IFieldSymbol>()
			.Select(field => field.ConstantValue as string)
			.FirstOrDefault(reason => reason is not null);

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
