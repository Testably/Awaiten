using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The marker inside the synthetic implementation identity of an alias registration
	///     (<c>&lt;service&gt;@alias:&lt;qualified alias&gt;</c>, see <see cref="ThroughHandle" />), which
	///     <c>DisplayInstance</c> trims from diagnostics.
	/// </summary>
	private const string AliasKeyMarker = "@alias:";

	/// <summary>
	///     The marker inside the implementation identity of a handle registration (<c>&lt;implementation&gt;@hidden:&lt;module&gt;</c>),
	///     which keeps the handles of two modules that register the same internal implementation apart, and which
	///     <c>DisplayInstance</c> trims from diagnostics.
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
				foreach (INamedTypeSymbol type in new[] { registration.Service, registration.Implementation, })
				{
					handles[$"{moduleName}.{InternalsClass}.{HiddenHandleName(type)}"] = (Display(type.ToDisplayString(FullyQualified)), moduleName);
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
				argument = argument == handle.Key
					? named
					: argument.Replace($"'{handle.Key}'", $"'{named}'").Replace(handle.Key, handle.Value.Service);
			}

			return argument;
		}
	}

	/// <summary>
	///     Reports AWT238 at the <c>[Import]</c> of a module of another assembly whose generated members take the handle
	///     of an internal service that another module of that assembly declares, when the imported module does not
	///     register the service itself and the container does not import that other module, and drops the AWT101 the
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
				    || ConstructedModuleRegistrations(module, compilation, members: true).Any(registration => HiddenHandleName(registration.Service) == handle.Name))
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
	///     other rules: the module has no such handle, or this container can name every service of the implementation and
	///     construct it, as with <c>InternalsVisibleTo</c>, so it builds the implementation itself. A registration under
	///     the service that owns the instance registers the handle, built by the module's factory, as a dependency-only
	///     service (<see cref="RawRegistration.Unlisted" />) with the implementation's own disposal and the hooks and
	///     async initializers through their generated wrappers. Every other service of the implementation is an alias
	///     with its own identity that returns the instance from the handle and owns nothing; one under another internal
	///     service is dependency-only too. An implementation whose every service is an alias gets one unkeyed handle
	///     registration of its own, from its first registration.
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
		    || ((registration.Production == ProductionKind.Constructor
			        ? CanConstruct(implementation, compilation)
			        : MembersNamed(module, registration.ProductionMember!).Any(member => compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)))
		        && registrations.All(candidate => compilation.IsSymbolAccessibleWithin(candidate.Service, compilation.Assembly))))
		{
			return null;
		}

		// Another module that declares the handle and registers the implementation the same way builds the one shared
		// instance, so both registrations coalesce into one, as they do in a container of the modules' own assembly.
		INamedTypeSymbol builder = module;
		List<ModuleRegistration> built = registrations;
		string factoryName = factory.Name;
		if (handle.ContainingType?.ContainingType is { } declaring
		    && !SymbolEqualityComparer.Default.Equals(declaring, module)
		    && HiddenProducer(declaring, HiddenFactoryName(implementation), compilation) is { Handle: { } shared, } declared
		    && SymbolEqualityComparer.Default.Equals(shared, handle)
		    && HandledTogether(ConstructedModuleRegistrations(declaring, compilation, members: true)
			    .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.Implementation, implementation))) is { Count: > 0, } declaredRegistrations
		    && declaredRegistrations[0].Production == registrations[0].Production
		    && declaredRegistrations[0].Member == registrations[0].Member)
		{
			builder = declaring;
			built = declaredRegistrations;
			factoryName = declared.Name;
		}

		List<RawRegistration> handled = new();
		if (Alias(service) is not { ReturnType: INamedTypeSymbol aliasType, } alias)
		{
			handled.Add(HandleRegistration(registration.Key));
			return handled;
		}

		if (registrations.All(candidate => Alias(candidate.Service) is not null) && implicitHandles.Add(implementation))
		{
			handled.Add(HandleRegistration(null) with { Weak = false, IsDefault = false, WhenInjectedInto = null, });
		}

		string aliasName = aliasType.ToDisplayString(FullyQualified);
		handled.Add(registration with
		{
			ServiceType = aliasName,
			ServiceSymbol = aliasType,
			ImplementationType = $"{aliasName}{AliasKeyMarker}{module.ToDisplayString(FullyQualified)}.{alias.Name}",
			Implementation = aliasType,
			Production = ProductionKind.Factory,
			ProductionMember = InInternals(alias.Name),
			Eager = false,
			OnActivated = null,
			OnRelease = null,
			SuppressDisposal = false,
			Traits = new ImplementationTraits(default, false, false),
			TraitsExact = true,
			Unlisted = !compilation.IsSymbolAccessibleWithin(service, compilation.Assembly),
		});
		return handled;

		IMethodSymbol? Alias(INamedTypeSymbol aliased)
			=> GeneratedFactoryNamed(module, HiddenAliasName(implementation, aliased), compilation);

		RawRegistration HandleRegistration(string? key)
			=> registration with
			{
				ImplementationType = $"{registration.ImplementationType}{HiddenKeyMarker}{(handle.ContainingType?.ContainingType ?? module).ToDisplayString(FullyQualified)}",
				ServiceType = handle.ToDisplayString(FullyQualified),
				ServiceSymbol = handle,
				Implementation = handle,
				Origin = builder,
				Production = registration.Production == ProductionKind.Instance ? ProductionKind.Instance : ProductionKind.Factory,
				ProductionMember = InInternals(factoryName),
				Key = key,
				Eager = registrations.Concat(built).Any(candidate => NamedFlag(candidate.Attribute, "Eager")),
				OnActivated = Hook(release: false),
				OnRelease = Hook(release: true),
				SuppressDisposal = NamedFlag(built[0].Attribute, "SuppressDisposal"),
				// A factory member may return any subtype, which the handle's variant reveals only at run time.
				Traits = registration.Production == ProductionKind.Constructor ? TraitsOf(implementation, DeclaredAsyncInitializers(builder, compilation, null)) : null,
				TraitsExact = registration.Production == ProductionKind.Constructor,
				Unlisted = true,
				Hidden = implementation,
			};

		string? Hook(bool release)
			=> built
				.Select(candidate => NamedArgument(candidate.Attribute, release ? "OnRelease" : "OnActivated"))
				.Where(hook => hook is not null)
				.Select(hook => HiddenHookName(implementation, release, hook!))
				.Where(wrapper => !InternalsMembers(builder, wrapper).IsEmpty)
				.Select(InInternals)
				.FirstOrDefault();
	}
}
