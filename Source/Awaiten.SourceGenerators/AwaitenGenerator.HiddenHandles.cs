using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The marker inside the synthetic implementation identity of an alias registration
	///     (<c>&lt;service&gt;@alias:&lt;implementation&gt;</c>, see <see cref="ThroughHandle" />), which
	///     <c>DisplayInstance</c> trims from diagnostics.
	/// </summary>
	private const string AliasKeyMarker = "@alias:";

	/// <summary>
	///     The marker inside the implementation identity of an instance handle registration
	///     (<c>&lt;implementation&gt;@hidden:&lt;module that declares the handle&gt;</c>), which keeps it apart from a
	///     registration that constructs the implementation, and which <c>DisplayInstance</c> trims from diagnostics.
	/// </summary>
	private const string HiddenKeyMarker = "@hidden:";

	/// <summary>
	///     Rewrites each diagnostic argument that names a generated handle of an imported module of another assembly to the
	///     internal service it holds, as <c>Lib.BeanStore (internal to module Lib.CoffeeModule)</c>, which the message
	///     quotes like any other type, so a message never names a type the user did not write. A handle inside a longer
	///     argument, such as a type argument, is named by the service alone.
	/// </summary>
	private static void NameHiddenServices(INamedTypeSymbol container, Compilation compilation, List<DiagnosticInfo> diagnostics)
	{
		List<ImportedModule> imported = CollectImportedModules(container, new List<DiagnosticInfo>());
		if (ReadExtends(container) is { } baseContainer)
		{
			imported.AddRange(CollectImportedModules(baseContainer, new List<DiagnosticInfo>()));
		}

		List<INamedTypeSymbol> modules = imported
			.Select(module => module.Symbol)
			.Where(module => !SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly))
			.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
			.ToList();
		List<INamedTypeSymbol> providers = ReportMissingHandleProviders(imported, modules, compilation, diagnostics);
		if (diagnostics.Count == 0)
		{
			return;
		}

		Dictionary<string, (string Service, string Module)> handles = new(StringComparer.Ordinal);
		foreach (INamedTypeSymbol module in modules.Concat(providers))
		{
			string moduleName = Display(module.ToDisplayString(FullyQualified));
			foreach (ModuleRegistration registration in ConstructedModuleRegistrations(module, compilation, members: true))
			{
				string service = Display(registration.Service.ToDisplayString(FullyQualified));
				handles[$"{moduleName}.{InternalsClass}.{HiddenHandleName(registration.Service)}"] = (service, moduleName);
				if (HiddenProducer(module, HiddenFactoryName(registration.Implementation), compilation) is { Handle: { } instance, })
				{
					handles[Display(instance.ToDisplayString(FullyQualified))] = (Display(registration.Implementation.ToDisplayString(FullyQualified)), moduleName);
				}
			}
		}

		if (handles.Count == 0)
		{
			return;
		}

		for (int index = 0; index < diagnostics.Count; index++)
		{
			DiagnosticInfo diagnostic = diagnostics[index];
			string[] arguments = diagnostic.MessageArgs.AsArray();
			string[] renamed = arguments.Select(Rename).ToArray();
			if (!renamed.SequenceEqual(arguments))
			{
				diagnostics[index] = diagnostic with { MessageArgs = new EquatableArray<string>(renamed), };
			}
		}

		string Rename(string argument)
		{
			foreach (KeyValuePair<string, (string Service, string Module)> handle in handles.Where(handle => argument.Contains(handle.Key)))
			{
				string named = $"{handle.Value.Service} (internal to module {handle.Value.Module})";
				foreach (string variant in new[] { ".BothDisposable", ".AsyncDisposable", ".Disposable", string.Empty, })
				{
					string type = handle.Key + variant;
					argument = argument == type
						? named
						: argument.Replace($"'{type}'", $"'{named}'").Replace(type, handle.Value.Service);
				}
			}

			return argument;
		}
	}

	/// <summary>
	///     Reports AWT238 at the <c>[Import]</c> of a module of another assembly whose generated members take the handle
	///     of an internal service that another module of that assembly declares, when no imported module of that assembly
	///     registers the service and the container does not import that other module, and drops the AWT101 the
	///     missing handle registration causes, since the import is the one fix.
	///     Returns the modules that declare a handle an imported module takes, which name those handles in diagnostics.
	/// </summary>
	private static List<INamedTypeSymbol> ReportMissingHandleProviders(List<ImportedModule> imported, List<INamedTypeSymbol> modules, Compilation compilation, List<DiagnosticInfo> diagnostics)
	{
		List<INamedTypeSymbol> providers = new();
		foreach (ImportedModule import in imported.Where(import => modules.Contains(import.Symbol, SymbolEqualityComparer.Default)))
		{
			INamedTypeSymbol module = import.Symbol;
			IEnumerable<ITypeSymbol> types = (module.GetTypeMembers(InternalsClass).FirstOrDefault()?.GetMembers() ?? ImmutableArray<ISymbol>.Empty)
				.OfType<IMethodSymbol>()
				.SelectMany(method => method.Parameters.Select(parameter => parameter.Type).Concat([method.ReturnType,]));
			foreach (INamedTypeSymbol handle in types.SelectMany(TypesIn).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
			{
				if (handle is not { ContainingType: { Name: InternalsClass, ContainingType: { } provider, }, }
				    || !handle.Name.StartsWith("Hidden_", StringComparison.Ordinal)
				    || SymbolEqualityComparer.Default.Equals(provider, module))
				{
					continue;
				}

				if (!providers.Contains(provider, SymbolEqualityComparer.Default))
				{
					providers.Add(provider);
				}

				if (modules.Contains(provider, SymbolEqualityComparer.Default)
				    || modules.Any(registering => SymbolEqualityComparer.Default.Equals(registering.ContainingAssembly, provider.ContainingAssembly)
				                                  && ConstructedModuleRegistrations(registering, compilation, members: true).Any(registration => HiddenHandleName(registration.Service) == handle.Name)))
				{
					continue;
				}

				string missing = Display(handle.ToDisplayString(FullyQualified));
				diagnostics.RemoveAll(diagnostic => diagnostic.Descriptor == Diagnostics.MissingDependency
				                                    && diagnostic.MessageArgs.AsArray().Any(argument => argument.StartsWith(missing, StringComparison.Ordinal)));
				string service = ConstructedModuleRegistrations(provider, compilation, members: true)
					.Select(registration => registration.Service)
					.FirstOrDefault(candidate => HiddenHandleName(candidate) == handle.Name)?.ToDisplayString(FullyQualified) ?? handle.ToDisplayString(FullyQualified);
				DiagnosticInfo diagnostic = new(
					Diagnostics.ModuleHandleProviderNotImported,
					LocationInfo.From(import.ImportLocation),
					new EquatableArray<string>([Display(module.ToDisplayString(FullyQualified)), Display(service), Display(provider.ToDisplayString(FullyQualified)),]));
				if (!diagnostics.Contains(diagnostic))
				{
					diagnostics.Add(diagnostic);
				}
			}
		}

		return providers;

		static IEnumerable<INamedTypeSymbol> TypesIn(ITypeSymbol type)
			=> type switch
			{
				IArrayTypeSymbol array => TypesIn(array.ElementType),
				INamedTypeSymbol named => new[] { named, }.Concat(named.TypeArguments.SelectMany(TypesIn)),
				_ => [],
			};
	}

	/// <summary>
	///     <paramref name="registration" /> served through <paramref name="alias" />, the generated member of
	///     <paramref name="module" /> that returns the instance of <paramref name="implementation" /> from its instance
	///     handle. Its identity names the alias's type and the implementation, and it owns nothing.
	/// </summary>
	private static RawRegistration ThroughAlias(RawRegistration registration, INamedTypeSymbol module, INamedTypeSymbol implementation, IMethodSymbol alias)
	{
		INamedTypeSymbol aliasType = (INamedTypeSymbol)alias.ReturnType;
		return registration with
		{
			ImplementationType = $"{aliasType.ToDisplayString(FullyQualified)}{AliasKeyMarker}{implementation.ToDisplayString(FullyQualified)}",
			Implementation = aliasType,
			Origin = module,
			Production = ProductionKind.Factory,
			ProductionMember = InInternals(alias.Name),
			Eager = false,
			OnActivated = null,
			OnRelease = null,
			OnActivatedMarkers = null,
			OnReleaseMarkers = null,
			SuppressDisposal = false,
			GreedyConstructor = false,
			GenericFactory = false,
			Traits = new ImplementationTraits(default, false, false),
			TraitsExact = true,
			ConstructVia = null,
			ConstructViaType = null,
			Constructor = null,
			ScanImplementation = null,
			Hidden = null,
			HiddenProduction = null,
		};
	}

	/// <summary>
	///     Lets a registration that constructs or produces an implementation a module of another assembly builds through
	///     a generated handle share the handle's instance, as a container in the module's own assembly coalesces both into
	///     one instance: a registration of the container, or of a module that can name the implementation. It is served
	///     through the alias that returns the implementation from its instance handle, with the handle's lifetime. The two
	///     no longer share an identity, so the checks coalescing would run between them run here (AWT107, AWT111, AWT166).
	///     A directive of the registration that the handle cannot run, or a missing alias, is AWT241.
	/// </summary>
	private static void ShareHandleWithRegistrations(List<RawRegistration> raw, List<(RawRegistration Handle, int Index)> handles, INamedTypeSymbol container, Compilation compilation, List<DiagnosticInfo> diagnostics)
	{
		HashSet<string> lifetimes = new(StringComparer.Ordinal);
		HashSet<string> productions = new(StringComparer.Ordinal);
		HashSet<string> directives = new(StringComparer.Ordinal);
		for (int index = 0; index < raw.Count; index++)
		{
			RawRegistration registration = raw[index];
			if (registration.Hidden is not null
			    || registration.ScanImplementation is not null
			    || handles.FirstOrDefault(entry => entry.Handle.Hidden!.ToDisplayString(FullyQualified) == registration.ImplementationType) is not ({ } handle, int handleIndex))
			{
				continue;
			}

			INamedTypeSymbol implementation = handle.Hidden!;
			if (!registration.IsScan)
			{
				RawRegistration plain = registration with { HiddenProduction = DescribeProduction(registration.Production, registration.ProductionMember, registration.Origin ?? container) };
				bool registrationFirst = (PrecedenceRank(registration), index).CompareTo((PrecedenceRank(handle), handleIndex)) < 0;
				ReportCoalescingConflicts(NewImplInfo(registrationFirst ? plain : handle), registrationFirst ? handle : plain, container, lifetimes, productions, directives, diagnostics);
				if (registrationFirst)
				{
					HashSet<string> conflicting = new(ConflictingDirectives(NewImplInfo(plain), handle, container).Select(directive => directive.Directive), StringComparer.Ordinal);
					foreach ((string directive, string _, string _) in ConflictingDirectives(NewImplInfo(handle), registration, container)
						         .Where(directive => directive.Directive != "Eager" && !conflicting.Contains(directive.Directive)))
					{
						Report($"it sets {directive}, which the handle cannot apply to the instance it builds", $"set {directive} on the module's registration, or remove it");
					}
				}
			}

			if (handles.Select(entry => entry.Handle.Origin).OfType<INamedTypeSymbol>()
				    .Select(module => (Module: module, Alias: GeneratedFactoryNamed(module, HiddenAliasName(implementation, implementation), compilation)))
				    .FirstOrDefault(candidate => candidate.Alias is not null) is not ({ } owner, { } alias))
			{
				Report("the module generated no member that returns the implementation from its handle", "remove the registration, or grant this assembly InternalsVisibleTo in the module's assembly");
				continue;
			}

			raw[index] = ThroughAlias(registration with { Lifetime = handle.Lifetime, }, owner, implementation, alias) with { Eager = registration.Eager, };

			void Report(string reason, string remedy)
				=> diagnostics.Add(new DiagnosticInfo(
					Diagnostics.RegistrationNotSharedWithHandle,
					LocationInfo.From(registration.Location),
					new EquatableArray<string>([
						Display(implementation.ToDisplayString(FullyQualified)),
						Display(handle.Origin!.ToDisplayString(FullyQualified)),
						reason,
						remedy,
					])));
		}
	}

	/// <summary>
	///     The generated member named <paramref name="name" /> that builds a handle, a factory method (possibly async) or
	///     the property over an <c>Instance</c>, and the handle it produces, or <see langword="null" />.
	/// </summary>
	private static (string Name, INamedTypeSymbol? Handle)? HiddenProducer(INamedTypeSymbol owner, string name, Compilation compilation)
		=> InternalsMembers(owner, name).FirstOrDefault(member => member.IsStatic && compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)) switch
		{
			IMethodSymbol method => (method.Name, ProducedType(method.ReturnType, compilation) as INamedTypeSymbol),
			IPropertySymbol property => (property.Name, property.Type as INamedTypeSymbol),
			_ => null,
		};

	/// <summary>
	///     Rewrites a registration read off a module of another assembly whose implementation the module builds through
	///     a generated handle (see <see cref="HiddenHandle" />), or returns <see langword="null" /> to leave it to the
	///     other rules: the module has no such handle, or the module's assembly grants this container's assembly
	///     <c>InternalsVisibleTo</c> and the container can name every service of the implementation and construct it, so
	///     it builds the implementation itself. The first registration of the implementation also registers its instance
	///     handle, built by the module's factory, as an unkeyed dependency-only service (<see cref="RawRegistration.Unlisted" />)
	///     with the implementation's own disposal and the hooks and async initializers through their generated wrappers.
	///     Its identity names the implementation and the module that declares the handle, so the registrations of every
	///     module of that assembly coalesce into one instance. Every service of the implementation is an alias that
	///     returns the instance from the handle and owns nothing, with an identity of its service and implementation;
	///     one under an internal service is dependency-only too.
	/// </summary>
	private static List<RawRegistration>? ThroughHandle(RawRegistration registration, Compilation compilation, HashSet<INamedTypeSymbol> implicitHandles)
	{
		INamedTypeSymbol module = registration.Origin!;
		INamedTypeSymbol implementation = registration.Implementation;
		if (registration.ServiceSymbol is not { } service
		    || HiddenProducer(module, HiddenFactoryName(implementation), compilation) is not { Handle: { } handle, } factory)
		{
			return null;
		}

		List<ModuleRegistration> registrations = HandledTogether(ConstructedModuleRegistrations(module, compilation, members: true)
			.Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.Implementation, implementation)));
		if (registrations.Count == 0
		    || registrations[0].Production != registration.Production
		    || registrations[0].Member != registration.ProductionMember
		    || Alias(service) is not { ReturnType: INamedTypeSymbol aliasType, } alias
		    || (module.ContainingAssembly.GivesAccessTo(compilation.Assembly)
		        && (registration.Production == ProductionKind.Constructor
			        ? CanConstruct(implementation, compilation)
			        : MembersNamed(module, registration.ProductionMember!).Any(member => compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)))
		        && registrations.All(candidate => compilation.IsSymbolAccessibleWithin(candidate.Service, compilation.Assembly))))
		{
			return null;
		}

		List<RawRegistration> handled = new();
		if (implicitHandles.Add(implementation))
		{
			handled.Add(HandleRegistration() with { Weak = false, IsDefault = false, WhenInjectedInto = null, });
		}

		handled.Add(ThroughAlias(
			registration with
			{
				ServiceType = aliasType.ToDisplayString(FullyQualified),
				ServiceSymbol = aliasType,
				Unlisted = !compilation.IsSymbolAccessibleWithin(service, compilation.Assembly),
			},
			module,
			implementation,
			alias));
		return handled;

		IMethodSymbol? Alias(INamedTypeSymbol aliased)
			=> GeneratedFactoryNamed(module, HiddenAliasName(implementation, aliased), compilation);

		RawRegistration HandleRegistration()
			=> registration with
			{
				ImplementationType = $"{registration.ImplementationType}{HiddenKeyMarker}{(handle.ContainingType?.ContainingType ?? module).ToDisplayString(FullyQualified)}",
				ServiceType = handle.ToDisplayString(FullyQualified),
				ServiceSymbol = handle,
				Implementation = handle,
				Production = registration.Production == ProductionKind.Instance ? ProductionKind.Instance : ProductionKind.Factory,
				ProductionMember = InInternals(factory.Name),
				HiddenProduction = DescribeProduction(registrations[0].Production, registrations[0].Member, module),
				Key = null,
				Eager = NamedFlag(registrations[0].Attribute, "Eager"),
				OnActivated = Hook(release: false),
				OnRelease = Hook(release: true),
				SuppressDisposal = NamedFlag(registrations[0].Attribute, "SuppressDisposal"),
				// A factory member may return any subtype, which the handle's variant reveals only at run time.
				Traits = registration.Production == ProductionKind.Constructor ? TraitsOf(implementation, DeclaredAsyncInitializers(module, compilation, null)) : null,
				TraitsExact = registration.Production == ProductionKind.Constructor,
				Unlisted = true,
				Hidden = implementation,
			};

		string? Hook(bool release)
			=> NamedArgument(registrations[0].Attribute, release ? "OnRelease" : "OnActivated") is { } hook
			   && HiddenHookName(implementation, release, hook) is { } wrapper
			   && !InternalsMembers(module, wrapper).IsEmpty
				? InInternals(wrapper)
				: null;
	}
}
