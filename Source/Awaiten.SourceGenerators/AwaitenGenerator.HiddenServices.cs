using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The services a module another assembly can name registers under internal types, which a container in another
	///     assembly cannot name, in the module's own build. Each such service gets a generated public handle (see
	///     <see cref="HiddenHandle" />) when something a consumer can reach needs it, so a consumer depends on the handle
	///     instead. <see cref="Services" /> maps each internal service to the implementations registered under it,
	///     <see cref="Implementations" /> holds each implementation the module builds through a handle, and
	///     <see cref="Used" /> the services whose handle the module emits, in the order they were first needed.
	/// </summary>
	private sealed class HiddenServices
	{
		private readonly HashSet<INamedTypeSymbol> _used = new(SymbolEqualityComparer.Default);

		public HiddenServices(INamedTypeSymbol module, bool friends)
		{
			Module = module;
			Friends = friends;
		}

		public INamedTypeSymbol Module { get; }

		/// <summary>Whether the module's assembly grants <c>InternalsVisibleTo</c>, so a failure may be left to such an assembly.</summary>
		public bool Friends { get; }

		public List<HiddenImplementation> Implementations { get; } = new();

		public Dictionary<INamedTypeSymbol, List<HiddenImplementation>> Services { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The internal services a factory cannot take, because an implementation registered under them cannot be built.</summary>
		public HashSet<INamedTypeSymbol> NonViable { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The non-viable services something the module emits needed, whose cause the module reports.</summary>
		public HashSet<INamedTypeSymbol> NeededNonViable { get; } = new(SymbolEqualityComparer.Default);

		public List<INamedTypeSymbol> Used { get; } = new();

		/// <summary>The disposal traits of each decorator the module builds into the handle of an internal service, which the handle needs variants for.</summary>
		public List<(INamedTypeSymbol Service, ImplementationTraits Traits)> DecoratorTraits { get; } = new();

		/// <summary>The initializer wrappers of the decorators the module builds into handles.</summary>
		public List<HiddenInitializer> DecoratorInitializers { get; } = new();

		/// <summary>The other modules of the assembly, looked up once and only when a member needs an internal service the module does not register.</summary>
		public List<INamedTypeSymbol>? OtherModules { get; set; }

		/// <summary>Whether <see cref="Use" /> records the services a mirrored member needs; off while viability is decided.</summary>
		public bool Tracking { get; set; }

		public void Use(INamedTypeSymbol service)
		{
			if (Tracking && _used.Add(service))
			{
				Used.Add(service);
			}
		}

		/// <summary>The fully qualified name of the handle of <paramref name="service" />.</summary>
		public string HandleType(INamedTypeSymbol service)
			=> $"{Module.ToDisplayString(FullyQualified)}.{InternalsClass}.{HiddenHandleName(service)}";

		/// <summary>Whether the module builds <paramref name="implementation" /> through a handle instead of a factory of its own.</summary>
		public bool Handles(INamedTypeSymbol implementation)
			=> Implementations.Any(candidate => candidate.Viable && SymbolEqualityComparer.Default.Equals(candidate.Implementation, implementation));
	}

	/// <summary>
	///     One implementation a module builds through a handle: its registrations in declaration order and the service
	///     whose handle owns its instance, the first internal service it is registered under, or the implementation
	///     itself when it is registered under several accessible services no accessible type covers.
	/// </summary>
	private sealed class HiddenImplementation(INamedTypeSymbol implementation, List<ModuleRegistration> registrations, INamedTypeSymbol owner)
	{
		public INamedTypeSymbol Implementation { get; } = implementation;

		public List<ModuleRegistration> Registrations { get; } = registrations;

		public INamedTypeSymbol Owner { get; } = owner;

		/// <summary>How the instance is produced: through the constructor, or the module's <c>Factory</c> or <c>Instance</c> <see cref="Member" />.</summary>
		public ProductionKind Production => Registrations[0].Production;

		public string? Member => Registrations[0].Member;

		/// <summary>Whether the implementation is registered under accessible services only, which its own handle shares.</summary>
		public bool Shared { get; init; }

		public bool Viable { get; set; } = true;

		public List<DiagnosticInfo> Rejections { get; set; } = new();

		/// <summary>The owner and every internal service the implementation is registered under.</summary>
		public IEnumerable<INamedTypeSymbol> HiddenServices(INamedTypeSymbol module, Compilation compilation)
			=> new[] { Owner, }
				.Concat(Registrations.Select(registration => registration.Service).Where(service => IsHiddenService(service, module, compilation)))
				.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);
	}

	/// <summary>
	///     A parameter mirrored onto a generated member through the handle of an internal service, or the reason it
	///     cannot be.
	/// </summary>
	private sealed record HiddenMirror(FactoryParameter Parameter, PendingDiagnostic? Error);

	/// <summary>A diagnostic about a mirrored member, completed with its subject, the member and its location once known.</summary>
	private sealed record PendingDiagnostic(DiagnosticDescriptor Descriptor, string[] Arguments)
	{
		public DiagnosticInfo WithLocation(Location? location, SelfCompiledSubject subject, string member)
			=> new(Descriptor, LocationInfo.From(location), new EquatableArray<string>([subject.Subject, member, ..Arguments,]));
	}

	/// <summary>How a member takes an internal service, each of which the generated member can pass on from its handle.</summary>
	private enum HiddenShape
	{
		Direct,
		Func,
		Lazy,
		Owned,
		FuncOwned,
		Collection,
	}

	/// <summary>
	///     The registrations of one implementation that one handle builds: all of them when they are produced the same
	///     way (the constructor, or one <c>Factory</c> or <c>Instance</c> member), else only the constructed ones, since
	///     a container keeps a constructor and a member of one implementation apart (AWT111). The module's build and a
	///     consuming container ask this same question.
	/// </summary>
	private static List<ModuleRegistration> HandledTogether(IEnumerable<ModuleRegistration> registrations)
	{
		List<ModuleRegistration> all = registrations.ToList();
		return all.All(registration => registration.Production == all[0].Production && registration.Member == all[0].Member)
			? all
			: all.Where(registration => registration.Production == ProductionKind.Constructor).ToList();
	}

	/// <summary>
	///     Whether <paramref name="service" /> is a service type no other assembly can name but the module's own
	///     assembly can, so a container in another assembly reaches it only through a handle.
	/// </summary>
	private static bool IsHiddenService(INamedTypeSymbol service, INamedTypeSymbol module, Compilation compilation)
		=> !service.IsUnboundGenericType
		   && !IsExternallyAccessible(service)
		   && compilation.IsSymbolAccessibleWithin(service, module.ContainingAssembly);

	/// <summary>
	///     The implementations of a module another assembly can name that it builds through a handle, and which of
	///     them can be built: those registered under an internal service (see <see cref="IsHiddenService" />), and those
	///     registered under several accessible services that no accessible type covers, so one handle shares their
	///     instance. The module drops such an implementation again when one of its scans matches it too, and leaves one
	///     private to the module to the factory a container in its own assembly calls. An implementation whose generated
	///     factory would fail is not viable, and nor is any internal service it is registered under, so a member that
	///     needs such a service falls back to what it did before (AWT195). Returns <see langword="null" /> for a module
	///     no other assembly can name.
	/// </summary>
	private static HiddenServices? CollectHiddenServices(INamedTypeSymbol module, Compilation compilation)
	{
		if (!IsExternallyAccessible(module))
		{
			return null;
		}

		HiddenServices hidden = new(module, GrantsInternalsVisibleTo(compilation.Assembly));
		ModuleScanContext context = new(module, compilation, new List<DiagnosticInfo>(), new HashSet<string>(StringComparer.Ordinal), hidden);
		foreach (IGrouping<INamedTypeSymbol, ModuleRegistration> group in ConstructedModuleRegistrations(module, compilation, members: true)
			         .GroupBy<ModuleRegistration, INamedTypeSymbol>(registration => registration.Implementation, SymbolEqualityComparer.Default))
		{
			INamedTypeSymbol implementation = group.Key;
			List<ModuleRegistration> registrations = HandledTogether(group);
			bool constructed = registrations.Count > 0 && registrations[0].Production == ProductionKind.Constructor;
			if (registrations.Count == 0
			    || implementation.IsUnboundGenericType
			    || (constructed && (implementation is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, }
			                        || !implementation.InstanceConstructors.Any(constructor => compilation.IsSymbolAccessibleWithin(constructor, module)))))
			{
				continue;
			}

			INamedTypeSymbol? owner = registrations.Select(registration => registration.Service).FirstOrDefault(service => IsHiddenService(service, module, compilation));
			if (owner is null)
			{
				List<INamedTypeSymbol> services = registrations.Select(registration => registration.Service).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
				if (!constructed
				    || services.Count < 2
				    || IsHiddenInModule(implementation, module, compilation)
				    || !registrations.All(registration => SelfCompilesRegistration(module, implementation, registration.Service, compilation))
				    || FactoryReturnType(implementation, services, context) is not null)
				{
					continue;
				}

				owner = implementation;
			}

			HiddenImplementation candidate = new(implementation, registrations, owner) { Shared = SymbolEqualityComparer.Default.Equals(owner, implementation) && !IsHiddenService(implementation, module, compilation), };
			hidden.Implementations.Add(candidate);
			foreach (INamedTypeSymbol service in registrations.Select(registration => registration.Service).Where(service => IsHiddenService(service, module, compilation)))
			{
				if (!hidden.Services.TryGetValue(service, out List<HiddenImplementation>? implementations))
				{
					hidden.Services[service] = implementations = new List<HiddenImplementation>();
				}

				if (!implementations.Contains(candidate))
				{
					implementations.Add(candidate);
				}
			}
		}

		bool changed = true;
		while (changed)
		{
			changed = false;
			foreach (HiddenImplementation candidate in hidden.Implementations.Where(candidate => candidate.Viable))
			{
				List<DiagnosticInfo> rejections = new();
				if (BuildHiddenFactory(candidate, context with { Diagnostics = rejections, FailedHooks = new HashSet<string>(StringComparer.Ordinal), }, new List<HiddenInitializer>()) is null
				    || rejections.Count > 0)
				{
					candidate.Viable = false;
					candidate.Rejections = rejections;
					hidden.NonViable.UnionWith(candidate.HiddenServices(module, compilation));
					changed = true;
				}
			}
		}

		hidden.NeededNonViable.Clear();
		hidden.Tracking = true;
		return hidden;
	}

	/// <summary>
	///     Completes the handles of a module once its scans and registrations were mirrored, which recorded the internal
	///     services they need: adds the services an implementation needs that is registered under accessible services
	///     too or shared through a handle of its own, and the eager and async-initialized singletons, then follows the
	///     dependencies of each implementation the handles build. Returns the factories, handles, aliases and initializer
	///     wrappers to emit. Reports why a needed service cannot be built, unless an assembly granted
	///     <c>InternalsVisibleTo</c> builds what needs it instead.
	/// </summary>
	private static (List<RegistrationFactory> Factories, List<HiddenHandle> Handles, List<HiddenAlias> Aliases, List<HiddenInitializer> Initializers) CompleteHiddenServices(
		HiddenServices hidden,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		INamedTypeSymbol module = hidden.Module;
		List<AsyncInitializerConvention> conventions = DeclaredAsyncInitializers(module, compilation, null);
		foreach (HiddenImplementation candidate in hidden.Implementations.Where(candidate => candidate.Viable))
		{
			if (candidate.Registrations.Any(registration => !IsHiddenService(registration.Service, module, compilation)))
			{
				hidden.Use(candidate.Owner);
			}

			bool asyncInitialized = MatchingAsyncInitializers(conventions, candidate.Implementation, convention => IsOfConventionType(candidate.Implementation, convention.Type)).Count > 0
			                        || candidate.Registrations.Any(registration => NamedArgument(registration.Attribute, "OnActivated") is { } hook
			                                                                       && AccessibleMembers(module, hook, compilation).OfType<IMethodSymbol>().Any(method => IsAsyncHookReturn(method.ReturnType, compilation)));
			foreach (ModuleRegistration registration in candidate.Registrations)
			{
				if (registration.Attribute.AttributeClass?.Name == "SingletonAttribute"
				    && IsHiddenService(registration.Service, module, compilation)
				    && !hidden.NonViable.Contains(registration.Service)
				    && (asyncInitialized || NamedFlag(registration.Attribute, "Eager")))
				{
					hidden.Use(registration.Service);
				}
			}
		}

		ModuleScanContext context = new(module, compilation, diagnostics, new HashSet<string>(StringComparer.Ordinal), hidden);
		List<RegistrationFactory> factories = new();
		List<HiddenAlias> aliases = new();
		List<HiddenInitializer> initializers = new(hidden.DecoratorInitializers);
		List<HiddenImplementation> built = new();
		for (int index = 0; index < hidden.Used.Count; index++)
		{
			INamedTypeSymbol service = hidden.Used[index];
			IEnumerable<HiddenImplementation> serving = hidden.Implementations.Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.Owner, service)
			                                                                                     || (hidden.Services.TryGetValue(service, out List<HiddenImplementation>? registered) && registered.Contains(candidate)));
			foreach (HiddenImplementation candidate in serving.Where(candidate => candidate.Viable && !built.Contains(candidate)).ToList())
			{
				built.Add(candidate);
				hidden.Use(candidate.Owner);
				if (BuildHiddenFactory(candidate, context, initializers) is { } factory)
				{
					factories.Add(factory);
				}

				aliases.AddRange(HiddenAliases(candidate, hidden, compilation));
			}
		}

		foreach (HiddenImplementation candidate in hidden.Implementations.Where(candidate => !candidate.Viable && !hidden.Friends
		                                                                                     && candidate.HiddenServices(module, compilation).Any(hidden.NeededNonViable.Contains)))
		{
			diagnostics.AddRange(candidate.Rejections.Where(rejection => !diagnostics.Contains(rejection)));
		}

		List<HiddenHandle> handles = hidden.Used
			.Select(service =>
			{
				List<HiddenImplementation> owned = built.Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.Owner, service)).ToList();
				ImplementationTraits[] traits = owned
					.Where(candidate => candidate.Production == ProductionKind.Constructor)
					.Select(candidate => TraitsOf(candidate.Implementation, []))
					.Concat(hidden.DecoratorTraits.Where(decorator => SymbolEqualityComparer.Default.Equals(decorator.Service, service)).Select(decorator => decorator.Traits))
					.ToArray();
				bool wrap = owned.Any(candidate => candidate.Production == ProductionKind.Factory);
				return new HiddenHandle(
					HiddenHandleName(service),
					service.ToDisplayString(FullyQualified),
					wrap || traits.Any(trait => trait is { Disposable: true, AsyncDisposable: false, }),
					wrap || traits.Any(trait => trait is { Disposable: false, AsyncDisposable: true, }),
					wrap || traits.Any(trait => trait is { Disposable: true, AsyncDisposable: true, }),
					wrap);
			})
			.ToList();
		return (factories, handles, aliases, initializers);
	}

	/// <summary>
	///     The factory of an implementation the module builds through a handle: it constructs the implementation like the
	///     factory of a self-compiled registration, taking each internal service it needs through its handle, and wraps it
	///     in the handle of its owner, in the variant that implements exactly the disposal interfaces of the implementation.
	///     Each hook its registrations name gets a wrapper that takes the handle, and each convention of the module that
	///     initializes it gets an initializer wrapper (added to <paramref name="initializers" />). Returns
	///     <see langword="null" /> after reporting into the context why it cannot be built.
	/// </summary>
	private static RegistrationFactory? BuildHiddenFactory(HiddenImplementation candidate, ModuleScanContext context, List<HiddenInitializer> initializers)
	{
		INamedTypeSymbol implementation = candidate.Implementation;
		HiddenServices hidden = context.Hidden!;
		string typeName = implementation.ToDisplayString(FullyQualified);
		SelfCompiledSubject subject = candidate.Shared
			? new SelfCompiledSubject(typeName, $"'{Display(typeName)}' is registered under several services no accessible type covers, which a consuming container shares through a generated handle", "register it in the module")
			: new SelfCompiledSubject(typeName, $"'{Display(typeName)}' is registered under the internal service '{Display(candidate.Owner.ToDisplayString(FullyQualified))}', which a consuming container reaches through a generated handle", "register it in the module");
		Location? location = candidate.Registrations[0].Location;
		EquatableArray<FactoryParameter> parameters;
		EquatableArray<string> members = default;
		string? awaited = null;
		bool instance = candidate.Production == ProductionKind.Instance;
		bool built = candidate.Production == ProductionKind.Constructor
			? TryMirrorFactoryParameters(implementation, context, location, subject, out parameters, mirrorProperties: true)
			  && TryMirrorInjectedProperties(implementation, context, location, subject, ref parameters, out members)
			: TryMirrorProducingMember(candidate, context, location, subject, out parameters, out awaited);
		if (!built)
		{
			return null;
		}

		string handle = hidden.HandleType(candidate.Owner);
		List<RegistrationHook> hooks = new();
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (ModuleRegistration registration in instance ? [] : candidate.Registrations)
		{
			foreach (bool release in new[] { false, true, })
			{
				if (NamedArgument(registration.Attribute, release ? "OnRelease" : "OnActivated") is not { } hookName
				    || !seen.Add((release ? "OnRelease\0" : "OnActivated\0") + hookName))
				{
					continue;
				}

				ModuleHookRequest request = new(implementation, hookName, registration.Location, release, HiddenHookName(implementation, release, hookName), subject);
				if (ResolveModuleHookCore(request, null, context) is not { } resolved)
				{
					return null;
				}

				hooks.Add(new RegistrationHook(resolved.Wrapper, release));
			}
		}

		if (!instance && !TryAddHiddenInitializers(implementation, handle, context, location, subject, initializers))
		{
			return null;
		}

		if (candidate.Production != ProductionKind.Constructor)
		{
			return new RegistrationFactory(
				HiddenFactoryName(implementation),
				handle,
				typeName,
				parameters,
				new EquatableArray<RegistrationHook>(hooks.ToArray()),
				default,
				instance ? $"new {handle}" : $"{handle}.Wrap",
				candidate.Member,
				awaited,
				instance);
		}

		return new RegistrationFactory(
			HiddenFactoryName(implementation),
			handle,
			typeName,
			parameters,
			new EquatableArray<RegistrationHook>(hooks.ToArray()),
			members,
			handle + HandleVariant(TraitsOf(implementation, [])));
	}

	/// <summary>The nested handle variant that implements exactly the disposal interfaces of <paramref name="traits" />.</summary>
	private static string HandleVariant(ImplementationTraits traits)
		=> (traits.Disposable, traits.AsyncDisposable) switch
		{
			(true, true) => ".BothDisposable",
			(true, false) => ".Disposable",
			(false, true) => ".AsyncDisposable",
			_ => string.Empty,
		};

	/// <summary>
	///     Adds an initializer wrapper to <paramref name="initializers" /> for each convention of the module that
	///     initializes <paramref name="implementation" />, which takes its <paramref name="handle" /> and runs the
	///     convention on the instance it holds. Returns <see langword="false" /> after reporting a convention parameter
	///     the wrapper cannot mirror.
	/// </summary>
	private static bool TryAddHiddenInitializers(
		INamedTypeSymbol implementation,
		string handle,
		ModuleScanContext context,
		Location? location,
		SelfCompiledSubject subject,
		List<HiddenInitializer> initializers)
	{
		foreach (AsyncInitializerConvention convention in MatchingAsyncInitializers(DeclaredAsyncInitializers(context.Module, context.Compilation, null), implementation, convention => IsOfConventionType(implementation, convention.Type)))
		{
			List<FactoryParameter> mirrored = new();
			foreach (IParameterSymbol parameter in convention.Method.Parameters)
			{
				if (MirrorHiddenService(parameter.Type, parameter.Name, parameter.GetAttributes(), context) is { } mirror)
				{
					if (mirror.Error is { } error)
					{
						context.Diagnostics.Add(error.WithLocation(location, subject, $"the parameter '{parameter.Name}' of its async initializer {convention.Display}"));
						return false;
					}

					mirrored.Add(mirror.Parameter);
				}
				else if (!FitsFactorySignature(parameter.Type, context))
				{
					context.Diagnostics.Add(new DiagnosticInfo(
						Diagnostics.InaccessibleAsyncInitializer,
						LocationInfo.From(location),
						new EquatableArray<string>([
							subject.Subject,
							Display(convention.Type.ToDisplayString(FullyQualified)),
							convention.Method.Name,
							$"its parameter of type '{Display(parameter.Type.ToDisplayString(FullyQualified))}' is not accessible outside the module's assembly, so a consumer in another assembly cannot resolve it",
							"widen the parameter type's accessibility or register it in the module",
						])));
					return false;
				}
				else
				{
					mirrored.Add(new FactoryParameter(parameter.Type.ToDisplayString(FullyQualifiedWithNullability), parameter.Name));
				}
			}

			initializers.Add(new HiddenInitializer(
				HiddenInitializerName(implementation, convention),
				handle,
				convention.Type.ToDisplayString(FullyQualified),
				convention.Method.Name,
				convention.Method.ReturnType.ToDisplayString(FullyQualified),
				new EquatableArray<FactoryParameter>(mirrored.ToArray())));
		}

		return true;
	}

	/// <summary>
	///     The factory of a decorator the module declares over an internal service it registers, or
	///     <see langword="null" /> when it cannot be built, so a consuming container reports the decorator as before
	///     (AWT230). It mirrors the constructor like any decorator factory, so the inner parameter takes the service's
	///     handle, and wraps the decorator in a handle of the same service, in the variant that matches its disposal.
	///     A consumer then keeps each link of the chain as an instance of its own, with its own disposal. Not for the
	///     service that owns an implementation also served under other services, whose aliases need the undecorated
	///     instance.
	/// </summary>
	private static RegistrationFactory? HiddenDecoratorFactory(INamedTypeSymbol decorator, INamedTypeSymbol service, string name, ModuleScanContext context)
	{
		HiddenServices hidden = context.Hidden!;
		if (hidden.NonViable.Contains(service)
		    || decorator is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsUnboundGenericType: false, }
		    || hidden.Implementations.Any(candidate => candidate.Viable
		                                               && SymbolEqualityComparer.Default.Equals(candidate.Owner, service)
		                                               && candidate.Registrations.Any(registration => !SymbolEqualityComparer.Default.Equals(registration.Service, service))))
		{
			return null;
		}

		string typeName = decorator.ToDisplayString(FullyQualified);
		SelfCompiledSubject subject = new(typeName, $"'{Display(typeName)}' decorates the internal service '{Display(service.ToDisplayString(FullyQualified))}', which a consuming container reaches through a generated handle", "register it in the module");
		string handle = hidden.HandleType(service);
		List<HiddenInitializer> initializers = new();
		if (!TryMirrorFactoryParameters(decorator, context, null, subject, out EquatableArray<FactoryParameter> parameters, mirrorProperties: true)
		    || !TryMirrorInjectedProperties(decorator, context, null, subject, ref parameters, out EquatableArray<string> members)
		    || !TryAddHiddenInitializers(decorator, handle, context, null, subject, initializers))
		{
			return null;
		}

		ImplementationTraits traits = TraitsOf(decorator, []);
		hidden.Use(service);
		hidden.DecoratorTraits.Add((service, traits));
		hidden.DecoratorInitializers.AddRange(initializers);
		return new RegistrationFactory(name, handle, typeName, parameters, default, members, handle + HandleVariant(traits));
	}

	/// <summary>
	///     Mirrors the <c>Factory</c> or <c>Instance</c> member that produces <paramref name="candidate" /> onto the
	///     wrapper that puts its result into the handle: a static field or property converting to the implementation, or
	///     one static, non-generic method, whose parameters the wrapper mirrors like a constructor's, and which may return
	///     <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c> (<paramref name="awaited" />). Returns <see langword="false" />
	///     for a member the wrapper cannot call, after reporting why a parameter cannot be mirrored.
	/// </summary>
	private static bool TryMirrorProducingMember(
		HiddenImplementation candidate,
		ModuleScanContext context,
		Location? location,
		SelfCompiledSubject subject,
		out EquatableArray<FactoryParameter> parameters,
		out string? awaited)
	{
		parameters = default;
		awaited = null;
		INamedTypeSymbol module = context.Module;
		Compilation compilation = context.Compilation;
		string member = candidate.Member!;
		if (candidate.Production == ProductionKind.Instance)
		{
			return AccessibleMembers(module, member, compilation).Any(symbol => symbol switch
			{
				IFieldSymbol { IsStatic: true, } field => compilation.HasImplicitConversion(field.Type, candidate.Implementation),
				IPropertySymbol { IsStatic: true, GetMethod: not null, } property => compilation.HasImplicitConversion(property.Type, candidate.Implementation),
				_ => false,
			});
		}

		List<IMethodSymbol> methods = FindFactoryCandidates(module, member, candidate.Implementation, compilation)
			.Where(method => compilation.IsSymbolAccessibleWithin(method, module))
			.ToList();
		if (methods.Count != 1 || methods[0] is not { IsStatic: true, IsGenericMethod: false, } factory || factory.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
		{
			return false;
		}

		if (IsAsyncFactoryReturn(factory.ReturnType, compilation, out _))
		{
			awaited = ((INamedTypeSymbol)factory.ReturnType).ConstructedFrom.ContainingNamespace.ToDisplayString(FullyQualified) + "." + factory.ReturnType.Name;
		}

		List<FactoryParameter> mirrored = new();
		foreach (IParameterSymbol parameter in factory.Parameters)
		{
			if (!TryMirrorParameter(parameter, context, location, subject, $"the parameter '{parameter.Name}' of its factory '{member}'", out FactoryParameter mirror))
			{
				return false;
			}

			mirrored.Add(mirror);
		}

		parameters = new EquatableArray<FactoryParameter>(mirrored.ToArray());
		return true;
	}

	/// <summary>
	///     The aliases of an implementation the module builds through a handle: one per further service it is registered
	///     under, returning its instance from the owner's handle, as the accessible service or wrapped in the handle of
	///     another internal service. The owner's handle is taken under the key of the first registration of the owner, so
	///     a keyed owner still serves its other services.
	/// </summary>
	private static IEnumerable<HiddenAlias> HiddenAliases(HiddenImplementation candidate, HiddenServices hidden, Compilation compilation)
	{
		INamedTypeSymbol implementation = candidate.Implementation;
		string value = SymbolEqualityComparer.Default.Equals(candidate.Owner, implementation)
			? "@awaiten__handle.Value"
			: $"(({implementation.ToDisplayString(FullyQualified)})@awaiten__handle.Value)";
		string? key = candidate.Registrations
			.Where(registration => SymbolEqualityComparer.Default.Equals(registration.Service, candidate.Owner))
			.Select(registration => KeyAttribute(NamedKeyArgument(registration.Attribute)))
			.FirstOrDefault();
		foreach (INamedTypeSymbol service in candidate.Registrations.Select(registration => registration.Service).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
		{
			if (SymbolEqualityComparer.Default.Equals(service, candidate.Owner))
			{
				continue;
			}

			bool internalService = IsHiddenService(service, hidden.Module, compilation);
			if (internalService)
			{
				hidden.Use(service);
			}

			string returnType = internalService ? hidden.HandleType(service) : service.ToDisplayString(FullyQualified);
			yield return new HiddenAlias(
				HiddenAliasName(implementation, service),
				returnType,
				hidden.HandleType(candidate.Owner),
				key,
				internalService ? $"new {returnType}({value})" : value);
		}
	}

	/// <summary>
	///     Mirrors a member that takes an internal service the module registers onto a generated member through the
	///     service's handle: the parameter takes the handle in the same shape (see <see cref="HiddenShape" />) and the
	///     generated member passes the instance on. Reports AWT237 for a shape it cannot pass on, and returns
	///     <see langword="null" /> when the member takes no such service, or one that cannot be built, which is then
	///     recorded so its cause is reported, while the member falls back to AWT195. Only in the module's own build.
	/// </summary>
	private static HiddenMirror? MirrorHiddenService(ITypeSymbol type, string name, ImmutableArray<AttributeData> attributes, ModuleScanContext context)
	{
		if (context.Hidden is not { } hidden || HasArgAttribute(attributes) || HasRequestingType(attributes))
		{
			return null;
		}

		(HiddenShape? shape, ITypeSymbol inner) = HiddenShapeOf(type);
		if (shape is { } known && inner is INamedTypeSymbol service && hidden.Services.ContainsKey(service))
		{
			if (hidden.NonViable.Contains(service))
			{
				hidden.NeededNonViable.Add(service);
				return null;
			}

			hidden.Use(service);
			string handle = hidden.HandleType(service);
			string value = service.ToDisplayString(FullyQualified);
			const string unwrap = "awaiten__handle => awaiten__handle.Value";
			(string parameterType, string argument) = known switch
			{
				HiddenShape.Func => ($"global::System.Func<{handle}>", $"() => @{name}().Value"),
				HiddenShape.Lazy => ($"global::System.Lazy<{handle}>", $"new global::System.Lazy<{value}>(() => @{name}.Value.Value)"),
				HiddenShape.Owned => ($"global::Awaiten.Owned<{handle}>", $"global::Awaiten.Internals.AwaitenOwnedProjection.Map(@{name}, {unwrap})"),
				HiddenShape.FuncOwned => ($"global::System.Func<global::Awaiten.Owned<{handle}>>", $"() => global::Awaiten.Internals.AwaitenOwnedProjection.Map(@{name}(), {unwrap})"),
				HiddenShape.Collection => (type is IArrayTypeSymbol ? $"{handle}[]" : $"global::System.Collections.Generic.{type.Name}<{handle}>",
					$"global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(@{name}, {unwrap}))"),
				_ => (handle, $"@{name}.Value"),
			};
			return new HiddenMirror(new FactoryParameter(parameterType, name, KeyAttribute(FromKey(attributes)), argument), null);
		}

		return ReferencedHiddenService(type, hidden) is { } referenced
			? new HiddenMirror(default, new PendingDiagnostic(Diagnostics.ModuleHiddenServiceShape, [Display(referenced.ToDisplayString(FullyQualified)), Display(type.ToDisplayString(FullyQualified)),]))
			: null;
	}

	/// <summary>
	///     The shape in which <paramref name="type" /> takes a service and that service, or a <see langword="null" />
	///     shape for a relationship or collection type whose service a generated member cannot pass on from a handle.
	/// </summary>
	private static (HiddenShape? Shape, ITypeSymbol Inner) HiddenShapeOf(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
		{
			return array.Rank == 1 ? (HiddenShape.Collection, array.ElementType) : (null, type);
		}

		if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named)
		{
			return (HiddenShape.Direct, type);
		}

		ITypeSymbol argument = named.TypeArguments[0];
		string? ns = named.ContainingNamespace?.ToDisplayString();
		return (ns, named.Name) switch
		{
			("System", "Func") => IsOwned(argument, out ITypeSymbol owned) ? (HiddenShape.FuncOwned, owned) : (HiddenShape.Func, argument),
			("System", "Lazy") => (HiddenShape.Lazy, argument),
			(AttributeNamespace, "Owned") => (HiddenShape.Owned, argument),
			("System.Collections.Generic", "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection") => (HiddenShape.Collection, argument),
			_ => (HiddenShape.Direct, type),
		};
	}

	/// <summary>An internal service of the module that <paramref name="type" /> carries as a type argument or array element, or <see langword="null" />.</summary>
	private static INamedTypeSymbol? ReferencedHiddenService(ITypeSymbol type, HiddenServices hidden)
		=> type switch
		{
			IArrayTypeSymbol array => ReferencedHiddenService(array.ElementType, hidden),
			INamedTypeSymbol named => hidden.Services.ContainsKey(named)
				? named
				: named.TypeArguments.Select(argument => ReferencedHiddenService(argument, hidden)).FirstOrDefault(service => service is not null),
			_ => null,
		};

	/// <summary>
	///     AWT236 when a member of the module takes an internal service that the module does not register but another
	///     module of its assembly does, since a handle serves only the module that declares it. Only in the module's own build.
	/// </summary>
	private static PendingDiagnostic? InternalServiceOfAnotherModule(ITypeSymbol type, ModuleScanContext context)
	{
		if (context.Hidden is not { } hidden
		    || HiddenShapeOf(type) is not ({ }, INamedTypeSymbol service)
		    || !IsHiddenService(service, context.Module, context.Compilation))
		{
			return null;
		}

		hidden.OtherModules ??= ModulesIn(context.Compilation.Assembly.GlobalNamespace)
			.Where(other => !SymbolEqualityComparer.Default.Equals(other, context.Module))
			.ToList();
		foreach (INamedTypeSymbol other in hidden.OtherModules)
		{
			if (ConstructedModuleRegistrations(other, context.Compilation, members: true).Any(registration => SymbolEqualityComparer.Default.Equals(registration.Service, service)))
			{
				return new PendingDiagnostic(Diagnostics.ModuleHiddenServiceInOtherModule, [
					Display(service.ToDisplayString(FullyQualified)),
					Display(other.ToDisplayString(FullyQualified)),
					Display(context.Module.ToDisplayString(FullyQualified)),
				]);
			}
		}

		return null;
	}

	/// <summary>The <c>[Module]</c> classes declared in <paramref name="ns" /> and its nested namespaces and types.</summary>
	private static IEnumerable<INamedTypeSymbol> ModulesIn(INamespaceOrTypeSymbol ns)
	{
		foreach (ISymbol member in ns.GetMembers())
		{
			if (member is INamespaceSymbol nested)
			{
				foreach (INamedTypeSymbol module in ModulesIn(nested))
				{
					yield return module;
				}
			}
			else if (member is INamedTypeSymbol type)
			{
				if (HasAwaitenAttribute(type.GetAttributes(), "ModuleAttribute"))
				{
					yield return type;
				}

				foreach (INamedTypeSymbol module in ModulesIn(type))
				{
					yield return module;
				}
			}
		}
	}

	/// <summary>
	///     A deterministic name for the handle of an internal service, derived from the service's fully-qualified name
	///     only, so the module's build and a consuming container agree on it.
	/// </summary>
	private static string HiddenHandleName(INamedTypeSymbol service)
		=> $"Hidden_{service.Name}_{Fnv1a(service.ToDisplayString(FullyQualified)):x8}";

	/// <summary>A deterministic name for the factory that builds an implementation into its handle.</summary>
	private static string HiddenFactoryName(INamedTypeSymbol implementation)
		=> $"HiddenRegistration_{implementation.Name}_{Fnv1a(implementation.ToDisplayString(FullyQualified)):x8}";

	/// <summary>A deterministic name for the alias that serves <paramref name="service" /> from the handle of <paramref name="implementation" />.</summary>
	private static string HiddenAliasName(INamedTypeSymbol implementation, INamedTypeSymbol service)
		=> $"Alias_{implementation.Name}_{Fnv1a($"{implementation.ToDisplayString(FullyQualified)}\0{service.ToDisplayString(FullyQualified)}"):x8}";

	/// <summary>A deterministic name for the wrapper of a hook that takes the handle of <paramref name="implementation" />.</summary>
	private static string HiddenHookName(INamedTypeSymbol implementation, bool release, string hookName)
		=> "Hidden" + RegistrationHookWrapperName(implementation, release, hookName);

	/// <summary>A deterministic name for the wrapper that runs <paramref name="convention" /> on the handle of <paramref name="implementation" />.</summary>
	private static string HiddenInitializerName(INamedTypeSymbol implementation, AsyncInitializerConvention convention)
		=> $"HiddenInitializer_{implementation.Name}_{Fnv1a($"{implementation.ToDisplayString(FullyQualified)}\0{convention.Type.ToDisplayString(FullyQualified)}\0{convention.Method.Name}"):x8}";
}
