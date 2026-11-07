using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
	///     instead, and each implementation the module builds through a handle gets an instance handle of its own, which
	///     holds the one instance every service of the implementation is served from. <see cref="Services" /> maps each
	///     internal service to the implementations registered under it, <see cref="Implementations" /> holds each
	///     implementation the module builds through a handle, and <see cref="Used" /> the services whose handle the module
	///     emits, in the order they were first needed.
	/// </summary>
	private sealed class HiddenServices
	{
		private readonly HashSet<INamedTypeSymbol> _used = new(SymbolEqualityComparer.Default);

		public HiddenServices(INamedTypeSymbol module, bool friends, Compilation compilation, bool crossModule)
		{
			Module = module;
			Friends = friends;
			Compilation = compilation;
			CrossModule = crossModule;
			Assembly = AssemblyModuleIndex.Of(compilation);
		}

		public INamedTypeSymbol Module { get; }

		public Compilation Compilation { get; }

		/// <summary>The modules of the module's assembly and their registrations.</summary>
		public AssemblyModuleIndex Assembly { get; }

		/// <summary>Whether a member may take an internal service another module of the assembly provides; off while probing another module's viability.</summary>
		public bool CrossModule { get; }

		/// <summary>Whether the assembly has more than one module that receives generated members, so another one may share a handle of this module.</summary>
		public bool SeveralModules => Assembly.Modules.Count > 1;

		/// <summary>
		///     The module that declares the handle of <paramref name="service" />, which every module of the assembly uses
		///     (see <see cref="AssemblyModuleIndex.ServiceProviders" />), or <see langword="null" /> when none does.
		/// </summary>
		public INamedTypeSymbol? Provider(INamedTypeSymbol service)
			=> Assembly.ServiceProviders.TryGetValue(service, out INamedTypeSymbol? provider) ? provider : null;

		/// <summary>
		///     The module that declares the instance handle of <paramref name="implementation" />, which every module of the
		///     assembly builds it into (see <see cref="AssemblyModuleIndex.InstanceProviders" />), or <see langword="null" />
		///     when none does.
		/// </summary>
		public INamedTypeSymbol? InstanceProvider(INamedTypeSymbol implementation)
			=> Assembly.InstanceProviders.TryGetValue(implementation, out INamedTypeSymbol? provider) ? provider : null;

		/// <summary>
		///     Whether another module of the assembly builds <paramref name="implementation" /> through a handle, so this
		///     module builds it into the same instance handle, even when it registers it under accessible services only,
		///     and a consuming container keeps one instance, as a container in the assembly does.
		/// </summary>
		public bool HandledElsewhere(INamedTypeSymbol implementation)
			=> Assembly.HandledBy.TryGetValue(implementation, out List<INamedTypeSymbol>? modules)
			   && modules.Any(other => !SymbolEqualityComparer.Default.Equals(other, Module));

		/// <summary>
		///     Whether a member of this module may take <paramref name="service" /> through the handle of the other module
		///     that provides it: the module does not register the service itself, and the provider builds it on its own,
		///     judged without its own cross-module dependencies so the question never recurses.
		/// </summary>
		public bool ProvidedElsewhere(INamedTypeSymbol service)
		{
			if (!CrossModule
			    || Services.ContainsKey(service)
			    || !IsHiddenService(service, Module, Compilation)
			    || Provider(service) is not { } provider
			    || SymbolEqualityComparer.Default.Equals(provider, Module))
			{
				return false;
			}

			return Assembly.Probe(provider) is { } probe && probe.Services.ContainsKey(service) && !probe.NonViable.Contains(service);
		}

		/// <summary>Whether the module's assembly grants <c>InternalsVisibleTo</c>, so a failure may be left to such an assembly.</summary>
		public bool Friends { get; }

		public List<HiddenImplementation> Implementations { get; } = new();

		public Dictionary<INamedTypeSymbol, List<HiddenImplementation>> Services { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The internal services a factory cannot take, because an implementation registered under them cannot be built.</summary>
		public HashSet<INamedTypeSymbol> NonViable { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The non-viable services something the module emits needed, whose cause the module reports.</summary>
		public HashSet<INamedTypeSymbol> NeededNonViable { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The internal services a factory being built for viability takes, or <see langword="null" /> when none is.</summary>
		public HashSet<INamedTypeSymbol>? Referenced { get; set; }

		public List<INamedTypeSymbol> Used { get; } = new();

		/// <summary>The disposal traits of each decorator the module builds into the handle of an internal service, which the handle needs variants for.</summary>
		public List<(INamedTypeSymbol Service, ImplementationTraits Traits)> DecoratorTraits { get; } = new();

		/// <summary>The initializer wrappers of the decorators the module builds into handles.</summary>
		public List<HiddenInitializer> DecoratorInitializers { get; } = new();

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
			=> $"{(Provider(service) ?? Module).ToDisplayString(FullyQualified)}.{InternalsClass}.{HiddenHandleName(service)}";

		/// <summary>Whether this module declares the handle of <paramref name="service" /> (see <see cref="Provider" />).</summary>
		public bool Declares(INamedTypeSymbol service)
			=> SymbolEqualityComparer.Default.Equals(Provider(service) ?? Module, Module);

		/// <summary>The fully qualified name of the instance handle of <paramref name="implementation" />.</summary>
		public string InstanceHandleType(INamedTypeSymbol implementation)
			=> $"{(InstanceProvider(implementation) ?? Module).ToDisplayString(FullyQualified)}.{InternalsClass}.{HiddenInstanceName(implementation)}";

		/// <summary>Whether this module declares the instance handle of <paramref name="implementation" /> (see <see cref="InstanceProvider" />).</summary>
		public bool DeclaresInstance(INamedTypeSymbol implementation)
			=> SymbolEqualityComparer.Default.Equals(InstanceProvider(implementation) ?? Module, Module);

		/// <summary>Whether the module builds <paramref name="implementation" /> through a handle instead of a factory of its own.</summary>
		public bool Handles(INamedTypeSymbol implementation)
			=> Implementations.Any(candidate => candidate.Viable && SymbolEqualityComparer.Default.Equals(candidate.Implementation, implementation));
	}

	/// <summary>
	///     The <c>[Module]</c>s of a compilation's assembly and what every module's build asks about them, read once per
	///     compilation and shared by the builds of all its modules, since the assembly walk would otherwise repeat for
	///     each. It holds symbols only while the compilation lives and never reaches a model, so incremental caching is
	///     unaffected.
	/// </summary>
	private sealed class AssemblyModuleIndex
	{
		private static readonly ConditionalWeakTable<Compilation, AssemblyModuleIndex> Cache = new();

		private readonly Compilation _compilation;
		private readonly ConcurrentDictionary<INamedTypeSymbol, Lazy<HiddenServices?>> _probes = new(SymbolEqualityComparer.Default);

		private AssemblyModuleIndex(Compilation compilation)
		{
			_compilation = compilation;
			foreach (INamedTypeSymbol module in ModulesIn(compilation.Assembly.GlobalNamespace))
			{
				Registrations[module] = ConstructedModuleRegistrations(module, compilation, members: true).ToList();
			}

			Modules = Registrations.Keys
				.Where(module => IsExternallyAccessible(module) && ReceivesGeneratedMembers(module))
				.OrderBy(module => module.ToDisplayString(FullyQualified), StringComparer.Ordinal)
				.ToList();
			foreach (INamedTypeSymbol module in Modules)
			{
				foreach (ModuleRegistration registration in Registrations[module])
				{
					if (!ServiceProviders.ContainsKey(registration.Service))
					{
						ServiceProviders[registration.Service] = module;
					}

					if (!InstanceProviders.ContainsKey(registration.Implementation))
					{
						InstanceProviders[registration.Implementation] = module;
					}
				}

				foreach (IGrouping<INamedTypeSymbol, ModuleRegistration> group in Registrations[module].GroupBy<ModuleRegistration, INamedTypeSymbol>(registration => registration.Implementation, SymbolEqualityComparer.Default))
				{
					if (HandledShape(module, group.Key, HandledTogether(group), compilation))
					{
						if (!HandledBy.TryGetValue(group.Key, out List<INamedTypeSymbol>? handling))
						{
							HandledBy[group.Key] = handling = new List<INamedTypeSymbol>();
						}

						handling.Add(module);
					}
				}
			}
		}

		/// <summary>Every <c>[Module]</c> of the assembly with its closed lifetime registrations, members included.</summary>
		public Dictionary<INamedTypeSymbol, List<ModuleRegistration>> Registrations { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The modules another assembly can name that receive generated members, so they can provide handles, by fully qualified name.</summary>
		public List<INamedTypeSymbol> Modules { get; }

		/// <summary>
		///     The module that declares the handle of each internal service, which every module of the assembly uses: the
		///     first of <see cref="Modules" /> that registers the service. One handle type per service lets a module take a
		///     service another module registers.
		/// </summary>
		public Dictionary<INamedTypeSymbol, INamedTypeSymbol> ServiceProviders { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>
		///     The module that declares the instance handle of each implementation: the first of <see cref="Modules" /> that
		///     registers it. One instance handle per implementation lets a consuming container share one instance when
		///     several modules register the implementation, under whichever services.
		/// </summary>
		public Dictionary<INamedTypeSymbol, INamedTypeSymbol> InstanceProviders { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>The modules that build each implementation through a handle on their own account (see <see cref="HandledShape" />).</summary>
		public Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> HandledBy { get; } = new(SymbolEqualityComparer.Default);

		public static AssemblyModuleIndex Of(Compilation compilation)
			=> Cache.GetValue(compilation, static created => new AssemblyModuleIndex(created));

		/// <summary>The registrations of <paramref name="module" />, read once when it belongs to the assembly.</summary>
		public List<ModuleRegistration> RegistrationsOf(INamedTypeSymbol module)
			=> Registrations.TryGetValue(module, out List<ModuleRegistration>? registrations)
				? registrations
				: ConstructedModuleRegistrations(module, _compilation, members: true).ToList();

		/// <summary>
		///     The internal services <paramref name="module" /> can build on its own, judged without its cross-module
		///     dependencies, once per compilation, so whether another module may take one of its handles never recurses.
		/// </summary>
		public HiddenServices? Probe(INamedTypeSymbol module)
			=> _probes.GetOrAdd(module, key => new Lazy<HiddenServices?>(() => CollectHiddenServices(key, _compilation, crossModule: false))).Value;
	}

	/// <summary>
	///     One implementation a module builds through a handle: its registrations in declaration order. Its factory wraps
	///     the instance in the instance handle of the implementation, and every service it is registered under is served
	///     from that handle through an alias.
	/// </summary>
	private sealed class HiddenImplementation(INamedTypeSymbol implementation, List<ModuleRegistration> registrations)
	{
		public INamedTypeSymbol Implementation { get; } = implementation;

		public List<ModuleRegistration> Registrations { get; } = registrations;

		/// <summary>How the instance is produced: through the constructor, or the module's <c>Factory</c> or <c>Instance</c> <see cref="Member" />.</summary>
		public ProductionKind Production => Registrations[0].Production;

		public string? Member => Registrations[0].Member;

		/// <summary>Whether the implementation is registered under accessible services only.</summary>
		public bool Shared { get; init; }

		public bool Viable { get; set; } = true;

		public List<DiagnosticInfo> Rejections { get; set; } = new();

		/// <summary>The internal services its factory took when its viability was last judged, so it is judged again only when one of them is rejected.</summary>
		public HashSet<INamedTypeSymbol> Needs { get; } = new(SymbolEqualityComparer.Default);

		/// <summary>Every internal service the implementation is registered under.</summary>
		public IEnumerable<INamedTypeSymbol> HiddenServices(INamedTypeSymbol module, Compilation compilation)
			=> Registrations.Select(registration => registration.Service).Where(service => IsHiddenService(service, module, compilation))
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
	///     Whether anything the module declares could be built through a handle, in its own module or another: a
	///     <c>[Scan]</c>, whose matches may take internal services, or a registration, decorator or composite that names an
	///     internal service, or a type a container in another assembly cannot construct or that converts to an internal
	///     type. A module that declares none of these never reads the modules of its assembly.
	/// </summary>
	private static bool MayBuildThroughHandles(INamedTypeSymbol module, Compilation compilation)
	{
		ImmutableArray<AttributeData> attributes = module.GetAttributes();
		return HasAwaitenAttribute(attributes, "ScanAttribute")
		       || ConstructedModuleRegistrations(module, compilation, members: true).Any(registration => !IsExternallyAccessible(registration.Service) || Concealed(registration.Implementation))
		       || attributes.Any(attribute => attribute.AttributeClass is { Name: "DecorateAttribute" or "CompositeAttribute", } attributeClass
		                                      && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace
		                                      && ContributedPair(attribute, attributeClass) is ({ } type, { } service)
		                                      && (!IsExternallyAccessible(service) || Concealed(type)));

		static bool Concealed(INamedTypeSymbol type)
			=> !ConstructibleEverywhere(type) || ServableTypes(type).Any(servable => !IsExternallyAccessible(servable));
	}

	/// <summary>
	///     Whether the module can build <paramref name="implementation" /> from <paramref name="registrations" /> (see
	///     <see cref="HandledTogether" />): a closed type, and for a constructed one a concrete class with a constructor
	///     the module can reach.
	/// </summary>
	private static bool Buildable(INamedTypeSymbol module, INamedTypeSymbol implementation, List<ModuleRegistration> registrations, Compilation compilation)
		=> registrations.Count > 0
		   && !implementation.IsUnboundGenericType
		   && (registrations[0].Production != ProductionKind.Constructor
		       || (implementation is { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, }
		           && implementation.InstanceConstructors.Any(constructor => compilation.IsSymbolAccessibleWithin(constructor, module))));

	/// <summary>
	///     Whether the module builds <paramref name="implementation" /> through a handle on its own account: it registers
	///     it under an internal service (see <see cref="IsHiddenService" />), or constructs it and registers it under
	///     several accessible services that no accessible type covers, which one instance handle shares.
	/// </summary>
	private static bool HandledShape(INamedTypeSymbol module, INamedTypeSymbol implementation, List<ModuleRegistration> registrations, Compilation compilation)
	{
		if (!Buildable(module, implementation, registrations, compilation))
		{
			return false;
		}

		if (registrations.Any(registration => IsHiddenService(registration.Service, module, compilation)))
		{
			return true;
		}

		List<INamedTypeSymbol> services = registrations.Select(registration => registration.Service).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
		return registrations[0].Production == ProductionKind.Constructor
		       && services.Count >= 2
		       && !IsHiddenInModule(implementation, module, compilation)
		       && registrations.All(registration => SelfCompilesRegistration(module, implementation, registration.Service, compilation))
		       && FactoryReturnType(implementation, services, new ModuleScanContext(module, compilation, new List<DiagnosticInfo>(), new HashSet<string>(StringComparer.Ordinal))) is null;
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
	///     them can be built: those it builds through a handle on its own account (see <see cref="HandledShape" />), and
	///     in an assembly with several modules those another module builds through a handle, so all of them share one
	///     instance handle. The module drops such an implementation again when one of its scans matches it too, and leaves one
	///     private to the module to the factory a container in its own assembly calls. An implementation whose generated
	///     factory would fail is not viable, and nor is any internal service it is registered under, so a member that
	///     needs such a service falls back to what it did before (AWT195). Returns <see langword="null" /> for a module
	///     no other assembly can name.
	/// </summary>
	private static HiddenServices? CollectHiddenServices(INamedTypeSymbol module, Compilation compilation, bool crossModule = true)
	{
		if (!IsExternallyAccessible(module) || !MayBuildThroughHandles(module, compilation))
		{
			return null;
		}

		HiddenServices hidden = new(module, GrantsInternalsVisibleTo(compilation.Assembly), compilation, crossModule);
		ModuleScanContext context = new(module, compilation, new List<DiagnosticInfo>(), new HashSet<string>(StringComparer.Ordinal), hidden);
		foreach (IGrouping<INamedTypeSymbol, ModuleRegistration> group in hidden.Assembly.RegistrationsOf(module)
			         .GroupBy<ModuleRegistration, INamedTypeSymbol>(registration => registration.Implementation, SymbolEqualityComparer.Default))
		{
			INamedTypeSymbol implementation = group.Key;
			List<ModuleRegistration> registrations = HandledTogether(group);
			if (!Buildable(module, implementation, registrations, compilation)
			    || (!HandledShape(module, implementation, registrations, compilation) && !(hidden.SeveralModules && hidden.HandledElsewhere(implementation))))
			{
				continue;
			}

			HiddenImplementation candidate = new(implementation, registrations) { Shared = !registrations.Any(registration => IsHiddenService(registration.Service, module, compilation)), };
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

		List<HiddenImplementation> pending = hidden.Implementations.ToList();
		while (pending.Count > 0)
		{
			HashSet<INamedTypeSymbol> rejected = new(SymbolEqualityComparer.Default);
			foreach (HiddenImplementation candidate in pending.Where(candidate => candidate.Viable))
			{
				List<DiagnosticInfo> rejections = new();
				candidate.Needs.Clear();
				hidden.Referenced = candidate.Needs;
				if (BuildHiddenFactory(candidate, context with { Diagnostics = rejections, FailedHooks = new HashSet<string>(StringComparer.Ordinal), }, new List<HiddenInitializer>()) is null
				    || rejections.Count > 0)
				{
					candidate.Viable = false;
					candidate.Rejections = rejections;
					hidden.NonViable.UnionWith(candidate.HiddenServices(module, compilation));
					rejected.UnionWith(candidate.HiddenServices(module, compilation));
				}
			}

			hidden.Referenced = null;
			pending = hidden.Implementations.Where(candidate => candidate.Viable && candidate.Needs.Overlaps(rejected)).ToList();
		}

		hidden.NeededNonViable.Clear();
		hidden.Tracking = true;
		return hidden;
	}

	/// <summary>
	///     Completes the handles of a module once its scans and registrations were mirrored, which recorded the internal
	///     services they need: builds every implementation registered under accessible services too, every eager or
	///     async-initialized singleton, and in an assembly with several modules every implementation it can, then follows
	///     the services each built implementation needs. Returns the factories, handles, aliases and initializer wrappers
	///     to emit. Reports why a needed service cannot be built, unless an assembly granted <c>InternalsVisibleTo</c>
	///     builds what needs it instead.
	/// </summary>
	private static (List<RegistrationFactory> Factories, List<HiddenHandle> Handles, List<HiddenAlias> Aliases, List<HiddenInitializer> Initializers) CompleteHiddenServices(
		HiddenServices hidden,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		INamedTypeSymbol module = hidden.Module;
		foreach (HiddenImplementation candidate in hidden.Implementations)
		{
			ReportHandledConflicts(candidate.Implementation, module, compilation, diagnostics);
		}

		List<AsyncInitializerConvention> conventions = DeclaredAsyncInitializers(module, compilation, null);
		List<HiddenImplementation> pending = hidden.Implementations
			.Where(candidate => candidate.Viable
			                    && (hidden.SeveralModules
			                        || candidate.Registrations.Any(registration => !IsHiddenService(registration.Service, module, compilation))
			                        || (candidate.Registrations[0].Attribute.AttributeClass?.Name == "SingletonAttribute" && (candidate.Registrations.Any(registration => NamedFlag(registration.Attribute, "Eager")) || IsHiddenAsyncInitialized(candidate.Implementation, candidate.Registrations, module, compilation, conventions)))))
			.ToList();

		ModuleScanContext context = new(module, compilation, diagnostics, new HashSet<string>(StringComparer.Ordinal), hidden);
		List<RegistrationFactory> factories = new();
		List<HiddenAlias> aliases = new();
		List<HiddenInitializer> initializers = new(hidden.DecoratorInitializers);
		List<HiddenImplementation> built = new();
		int used = 0;
		while (true)
		{
			foreach (HiddenImplementation candidate in pending.Where(candidate => !built.Contains(candidate)).ToList())
			{
				built.Add(candidate);
				if (BuildHiddenFactory(candidate, context, initializers) is { } factory)
				{
					factories.Add(factory);
				}

				aliases.AddRange(HiddenAliases(candidate, hidden, compilation));
			}

			pending.Clear();
			for (; used < hidden.Used.Count; used++)
			{
				if (hidden.Services.TryGetValue(hidden.Used[used], out List<HiddenImplementation>? serving))
				{
					pending.AddRange(serving.Where(candidate => candidate.Viable && !built.Contains(candidate)));
				}
			}

			if (pending.Count == 0)
			{
				break;
			}
		}

		foreach (HiddenImplementation candidate in hidden.Implementations.Where(candidate => !candidate.Viable && !hidden.Friends
		                                                                                     && candidate.HiddenServices(module, compilation).Any(hidden.NeededNonViable.Contains)))
		{
			diagnostics.AddRange(candidate.Rejections.Where(rejection => !diagnostics.Contains(rejection)));
		}

		IEnumerable<INamedTypeSymbol> declared = hidden.SeveralModules
			? hidden.Used.Concat(hidden.Services.Keys.Where(service => !hidden.Used.Contains(service, SymbolEqualityComparer.Default)))
			: hidden.Used;
		List<HiddenHandle> handles = declared
			.Where(hidden.Declares)
			.Select(service =>
			{
				ImplementationTraits[] traits = hidden.DecoratorTraits
					.Where(decorator => SymbolEqualityComparer.Default.Equals(decorator.Service, service))
					.Select(decorator => decorator.Traits)
					.Concat(hidden.SeveralModules ? OtherModulesDecoratorTraits(service, hidden) : [])
					.ToArray();
				return new HiddenHandle(
					HiddenHandleName(service),
					service.ToDisplayString(FullyQualified),
					traits.Any(trait => trait is { Disposable: true, AsyncDisposable: false, }),
					traits.Any(trait => trait is { Disposable: false, AsyncDisposable: true, }),
					traits.Any(trait => trait is { Disposable: true, AsyncDisposable: true, }));
			})
			.ToList();

		IEnumerable<INamedTypeSymbol> instances = hidden.SeveralModules
			? hidden.Assembly.RegistrationsOf(module).Select(registration => registration.Implementation).Where(hidden.Assembly.HandledBy.ContainsKey)
			: built.Select(candidate => candidate.Implementation);
		foreach (INamedTypeSymbol implementation in instances.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).Where(hidden.DeclaresInstance))
		{
			(bool constructed, bool wrap) = InstanceHandleNeeds(implementation, hidden);
			ImplementationTraits traits = TraitsOf(implementation, []);
			handles.Add(new HiddenHandle(
				HiddenInstanceName(implementation),
				implementation.ToDisplayString(FullyQualified),
				wrap || (constructed && traits is { Disposable: true, AsyncDisposable: false, }),
				wrap || (constructed && traits is { Disposable: false, AsyncDisposable: true, }),
				wrap || (constructed && traits is { Disposable: true, AsyncDisposable: true, }),
				wrap));
		}

		return (factories, handles, aliases, initializers);
	}

	/// <summary>
	///     Whether a convention of the module initializes <paramref name="implementation" />, or one of its
	///     <paramref name="registrations" /> names an asynchronous <c>OnActivated</c> hook, so a container initializes
	///     a singleton of it though nothing depends on it.
	/// </summary>
	private static bool IsHiddenAsyncInitialized(
		INamedTypeSymbol implementation,
		IEnumerable<ModuleRegistration> registrations,
		INamedTypeSymbol module,
		Compilation compilation,
		List<AsyncInitializerConvention> conventions)
		=> MatchingAsyncInitializers(conventions, implementation, convention => IsOfConventionType(implementation, convention.Type)).Count > 0
		   || registrations.Any(registration => NamedArgument(registration.Attribute, "OnActivated") is { } hook
		                                        && AccessibleMembers(module, hook, compilation).OfType<IMethodSymbol>().Any(method => IsAsyncHookReturn(method.ReturnType, compilation)));

	/// <summary>
	///     The first singleton a module another assembly can name registers under an internal service that a container
	///     builds or initializes though nothing depends on it, because it is eager or async-initialized, and whether it
	///     is eager, or <see langword="null" />. The module needs its handle even without any other generated member.
	/// </summary>
	private static (ModuleRegistration Registration, bool Eager)? StartedHiddenSingleton(INamedTypeSymbol module, Compilation compilation)
	{
		if (!IsExternallyAccessible(module))
		{
			return null;
		}

		List<AsyncInitializerConvention>? conventions = null;
		foreach (ModuleRegistration registration in ConstructedModuleRegistrations(module, compilation, members: true))
		{
			if (registration.Attribute.AttributeClass?.Name != "SingletonAttribute" || !IsHiddenService(registration.Service, module, compilation))
			{
				continue;
			}

			if (NamedFlag(registration.Attribute, "Eager"))
			{
				return (registration, true);
			}

			conventions ??= DeclaredAsyncInitializers(module, compilation, null);
			if (IsHiddenAsyncInitialized(registration.Implementation, [registration,], module, compilation, conventions))
			{
				return (registration, false);
			}
		}

		return null;
	}

	/// <summary>
	///     Reports what a container in the module's own assembly reports when the registrations of an implementation the
	///     module builds through a handle contradict each other: a different lifetime (AWT107), a different production
	///     (AWT111), or a later registration that sets a directive the first one does not (AWT166). A container in another
	///     assembly serves every registration from one instance handle with the lifetime and directives of the first one,
	///     so it would silently differ instead. An overridable default is left out, since only the importing container
	///     knows whether it yields.
	/// </summary>
	private static void ReportHandledConflicts(INamedTypeSymbol implementation, INamedTypeSymbol module, Compilation compilation, List<DiagnosticInfo> diagnostics)
	{
		List<ModuleRegistration> registrations = ConstructedModuleRegistrations(module, compilation, members: true)
			.Where(registration => SymbolEqualityComparer.Default.Equals(registration.Implementation, implementation) && NamedFallback(registration.Attribute) == 0)
			.ToList();
		if (registrations.Count < 2)
		{
			return;
		}

		ModuleRegistration first = registrations[0];
		string subject = Display(implementation.ToDisplayString(FullyQualified));
		bool lifetimeReported = false;
		bool productionReported = false;
		HashSet<string> directivesReported = new(StringComparer.Ordinal);
		foreach (ModuleRegistration registration in registrations.Skip(1))
		{
			if (!lifetimeReported && LifetimeOf(registration) != LifetimeOf(first))
			{
				lifetimeReported = true;
				Report(Diagnostics.ConflictingLifetime, registration, LifetimeOf(first), LifetimeOf(registration));
			}

			if (!productionReported && (registration.Production != first.Production || registration.Member != first.Member))
			{
				productionReported = true;
				Report(Diagnostics.ConflictingProduction, registration, DescribeProduction(first.Production, first.Member, module), DescribeProduction(registration.Production, registration.Member, module));
			}

			foreach (string hook in new[] { "OnActivated", "OnRelease", })
			{
				if (NamedArgument(registration.Attribute, hook) is { } named && named != NamedArgument(first.Attribute, hook) && directivesReported.Add(hook))
				{
					Report(Diagnostics.ConflictingLifecycleDirectives, registration, hook, DescribeHook(NamedArgument(first.Attribute, hook)), DescribeHook(named));
				}
			}

			foreach (string flag in new[] { "Eager", "SuppressDisposal", })
			{
				if (NamedFlag(registration.Attribute, flag) && !NamedFlag(first.Attribute, flag) && directivesReported.Add(flag))
				{
					Report(Diagnostics.ConflictingLifecycleDirectives, registration, flag, "false", "true");
				}
			}
		}

		void Report(DiagnosticDescriptor descriptor, ModuleRegistration registration, params string[] arguments)
			=> diagnostics.Add(new DiagnosticInfo(descriptor, LocationInfo.From(registration.Location), new EquatableArray<string>([subject, ..arguments,])));

		string DescribeHook(string? hook)
			=> hook is null ? "unset" : $"'{Display(QualifiedMember(module, hook))}'";

		static string LifetimeOf(ModuleRegistration registration)
			=> registration.Attribute.AttributeClass!.Name.Substring(0, registration.Attribute.AttributeClass.Name.Length - "Attribute".Length);
	}

	/// <summary>
	///     Whether the instance handle of <paramref name="implementation" />, which every module of the assembly that registers
	///     the implementation builds it into, needs the variants of a constructed implementation, and whether it needs to
	///     wrap the result of a <c>Factory</c> member, whose disposal shows only at run time.
	/// </summary>
	private static (bool Constructed, bool Wrap) InstanceHandleNeeds(INamedTypeSymbol implementation, HiddenServices hidden)
	{
		List<ProductionKind> all = (hidden.SeveralModules ? hidden.Assembly.Modules : [hidden.Module,])
			.Select(other => HandledTogether(hidden.Assembly.RegistrationsOf(other)
				.Where(registration => SymbolEqualityComparer.Default.Equals(registration.Implementation, implementation))))
			.Where(registrations => registrations.Count > 0)
			.Select(registrations => registrations[0].Production)
			.ToList();
		return (all.Contains(ProductionKind.Constructor), all.Contains(ProductionKind.Factory));
	}

	/// <summary>
	///     The disposal traits of every decorator the other modules of the assembly declare over
	///     <paramref name="service" />, which they build into its handle, so the handle carries a variant for each.
	/// </summary>
	private static IEnumerable<ImplementationTraits> OtherModulesDecoratorTraits(INamedTypeSymbol service, HiddenServices hidden)
		=> hidden.Assembly.Modules
			.Where(other => !SymbolEqualityComparer.Default.Equals(other, hidden.Module))
			.SelectMany(other => other.GetAttributes())
			.Where(attribute => attribute.AttributeClass is { Name: "DecorateAttribute", } attributeClass
			                    && attributeClass.ContainingNamespace?.ToDisplayString() == AttributeNamespace
			                    && ContributedPair(attribute, attributeClass) is ({ }, { } decorated)
			                    && SymbolEqualityComparer.Default.Equals(decorated, service))
			.Select(attribute => TraitsOf(ContributedPair(attribute, attribute.AttributeClass!).Item1!, []));

	/// <summary>
	///     The factory of an implementation the module builds through a handle: it constructs the implementation like the
	///     factory of a self-compiled registration, taking each internal service it needs through its handle, and wraps it
	///     in its instance handle, in the variant that implements exactly the disposal interfaces of the implementation.
	///     Each hook its registrations name gets a wrapper that takes the handle, and each convention of the module that
	///     initializes it gets an initializer wrapper (added to <paramref name="initializers" />). Returns
	///     <see langword="null" /> after reporting into the context why it cannot be built.
	/// </summary>
	private static RegistrationFactory? BuildHiddenFactory(HiddenImplementation candidate, ModuleScanContext context, List<HiddenInitializer> initializers)
	{
		INamedTypeSymbol implementation = candidate.Implementation;
		HiddenServices hidden = context.Hidden!;
		string typeName = implementation.ToDisplayString(FullyQualified);
		SelfCompiledSubject subject = HiddenSubject(candidate, context.Module, context.Compilation);
		Location? location = candidate.Registrations[0].Location;
		EquatableArray<FactoryParameter> parameters;
		MirroredMembers members = default;
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

		string handle = hidden.InstanceHandleType(implementation);
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
			members.Required,
			handle + HandleVariant(TraitsOf(implementation, [])),
			OptionalMembers: members.Optional,
			DeferredMembers: members.Deferred);
	}

	/// <summary>How a diagnostic about an implementation the module builds through a handle names it.</summary>
	private static SelfCompiledSubject HiddenSubject(HiddenImplementation candidate, INamedTypeSymbol module, Compilation compilation)
	{
		string typeName = candidate.Implementation.ToDisplayString(FullyQualified);
		return candidate.HiddenServices(module, compilation).FirstOrDefault() is { } service
			? new SelfCompiledSubject(typeName, $"'{Display(typeName)}' is registered under the internal service '{Display(service.ToDisplayString(FullyQualified))}', which a consuming container reaches through a generated handle", "register it in the module")
			: new SelfCompiledSubject(typeName, $"'{Display(typeName)}' is registered under services whose one instance a consuming container shares through a generated handle", "register it in the module");
	}

	/// <summary>
	///     Rejects every implementation the module builds through a handle that one of its scans matches too, since the
	///     scan's factory would build a second instance of what the handle shares (AWT197). One registered under
	///     accessible services only leaves the handles to the self-compiled registrations, which report it themselves.
	///     One under an internal service is no longer viable, so nothing takes its handle; it is reported unless the
	///     module's assembly grants <c>InternalsVisibleTo</c>, whose assemblies construct it once for both, or unless a
	///     self-compiled registration of it reports it already.
	/// </summary>
	private static void RejectScannedHiddenImplementations(HiddenServices hidden, List<ModuleFactory> scanFactories, List<DiagnosticInfo> diagnostics)
	{
		INamedTypeSymbol module = hidden.Module;
		Compilation compilation = hidden.Compilation;
		hidden.Implementations.RemoveAll(candidate => candidate.Shared && scanFactories.Any(factory => factory.ImplementationType == candidate.Implementation.ToDisplayString(FullyQualified)));
		foreach (HiddenImplementation candidate in hidden.Implementations.Where(candidate => candidate.Viable))
		{
			if (scanFactories.FirstOrDefault(factory => factory.ImplementationType == candidate.Implementation.ToDisplayString(FullyQualified)) is not { } scanned)
			{
				continue;
			}

			INamedTypeSymbol service = candidate.HiddenServices(module, compilation).First();
			DiagnosticInfo rejection = new(
				Diagnostics.ModuleScanMultipleExposures,
				LocationInfo.From(candidate.Registrations[0].Location),
				new EquatableArray<string>([
					HiddenSubject(candidate, module, compilation).Subject,
					$"{Display(scanned.ServiceType)}, {Display(service.ToDisplayString(FullyQualified))}",
					"remove the registration or exclude the type from the scan",
				]));
			candidate.Viable = false;
			candidate.Rejections = [rejection,];
			hidden.NonViable.UnionWith(candidate.HiddenServices(module, compilation));
			if (!hidden.Friends && !candidate.Registrations.Any(registration => SelfCompilesRegistration(module, candidate.Implementation, registration.Service, compilation)))
			{
				diagnostics.Add(rejection);
			}
		}
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
	///     A consumer then keeps each link of the chain as an instance of its own, with its own disposal.
	/// </summary>
	private static RegistrationFactory? HiddenDecoratorFactory(INamedTypeSymbol decorator, INamedTypeSymbol service, string name, ModuleScanContext context)
	{
		HiddenServices hidden = context.Hidden!;
		if (hidden.NonViable.Contains(service)
		    || decorator is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsUnboundGenericType: false, })
		{
			return null;
		}

		string typeName = decorator.ToDisplayString(FullyQualified);
		SelfCompiledSubject subject = new(typeName, $"'{Display(typeName)}' decorates the internal service '{Display(service.ToDisplayString(FullyQualified))}', which a consuming container reaches through a generated handle", "register it in the module");
		string handle = hidden.HandleType(service);
		List<HiddenInitializer> initializers = new();
		if (!TryMirrorFactoryParameters(decorator, context, null, subject, out EquatableArray<FactoryParameter> parameters, mirrorProperties: true)
		    || !TryMirrorInjectedProperties(decorator, context, null, subject, ref parameters, out MirroredMembers members)
		    || !TryAddHiddenInitializers(decorator, handle, context, null, subject, initializers))
		{
			return null;
		}

		ImplementationTraits traits = TraitsOf(decorator, []);
		hidden.Use(service);
		hidden.DecoratorTraits.Add((service, traits));
		hidden.DecoratorInitializers.AddRange(initializers);
		return new RegistrationFactory(name, handle, typeName, parameters, default, members.Required, handle + HandleVariant(traits), OptionalMembers: members.Optional, DeferredMembers: members.Deferred);
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
	///     The aliases of an implementation the module builds through a handle: one per service it is registered under,
	///     returning its instance from its instance handle, as the accessible service or wrapped in the handle of the
	///     internal service. A public implementation gets one under its own type too, through which a container in another
	///     assembly serves its own registration of the implementation from the same instance.
	/// </summary>
	private static IEnumerable<HiddenAlias> HiddenAliases(HiddenImplementation candidate, HiddenServices hidden, Compilation compilation)
	{
		INamedTypeSymbol implementation = candidate.Implementation;
		IEnumerable<INamedTypeSymbol> services = candidate.Registrations.Select(registration => registration.Service);
		if (IsExternallyAccessible(implementation))
		{
			services = services.Concat([implementation,]);
		}

		foreach (INamedTypeSymbol service in services.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
		{
			bool internalService = IsHiddenService(service, hidden.Module, compilation);
			if (internalService)
			{
				hidden.Use(service);
			}

			string returnType = internalService ? hidden.HandleType(service) : service.ToDisplayString(FullyQualified);
			yield return new HiddenAlias(
				HiddenAliasName(implementation, service),
				returnType,
				hidden.InstanceHandleType(implementation),
				internalService ? $"new {returnType}(@awaiten__handle.Value)" : "@awaiten__handle.Value");
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
		if (shape is { } known && inner is INamedTypeSymbol service && (hidden.Services.ContainsKey(service) || hidden.ProvidedElsewhere(service)))
		{
			hidden.Referenced?.Add(service);
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

		foreach (KeyValuePair<INamedTypeSymbol, List<ModuleRegistration>> other in hidden.Assembly.Registrations)
		{
			if (!SymbolEqualityComparer.Default.Equals(other.Key, context.Module)
			    && other.Value.Any(registration => SymbolEqualityComparer.Default.Equals(registration.Service, service)))
			{
				return new PendingDiagnostic(Diagnostics.ModuleHiddenServiceInOtherModule, [
					Display(service.ToDisplayString(FullyQualified)),
					Display(other.Key.ToDisplayString(FullyQualified)),
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

	/// <summary>
	///     A deterministic name for the instance handle of an implementation, derived from its fully-qualified name only,
	///     so every module of its assembly and a consuming container agree on it.
	/// </summary>
	private static string HiddenInstanceName(INamedTypeSymbol implementation)
		=> $"HiddenInstance_{implementation.Name}_{Fnv1a(implementation.ToDisplayString(FullyQualified)):x8}";

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
