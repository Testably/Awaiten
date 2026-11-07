using Awaiten.SourceGenerators.Entities;
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
		    || GeneratedFactoryNamed(module, HiddenFactoryName(implementation), compilation) is not { ReturnType: INamedTypeSymbol handle, } factory)
		{
			return null;
		}

		List<ModuleRegistration> registrations = ConstructedModuleRegistrations(module, compilation)
			.Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.Implementation, implementation))
			.ToList();
		if (CanConstruct(implementation, compilation)
		    && registrations.All(candidate => compilation.IsSymbolAccessibleWithin(candidate.Service, compilation.Assembly)))
		{
			return null;
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
				ImplementationType = $"{registration.ImplementationType}{HiddenKeyMarker}{module.ToDisplayString(FullyQualified)}",
				ServiceType = handle.ToDisplayString(FullyQualified),
				ServiceSymbol = handle,
				Implementation = handle,
				Production = ProductionKind.Factory,
				ProductionMember = InInternals(factory.Name),
				Key = key,
				Eager = registrations.Any(candidate => NamedFlag(candidate.Attribute, "Eager")),
				OnActivated = Hook(release: false),
				OnRelease = Hook(release: true),
				SuppressDisposal = NamedFlag(registrations[0].Attribute, "SuppressDisposal"),
				Traits = TraitsOf(implementation, DeclaredAsyncInitializers(module, compilation, null)),
				TraitsExact = true,
				Unlisted = true,
				Hidden = implementation,
			};

		string? Hook(bool release)
			=> registrations
				.Select(candidate => NamedArgument(candidate.Attribute, release ? "OnRelease" : "OnActivated"))
				.Where(hook => hook is not null)
				.Select(hook => HiddenHookName(implementation, release, hook!))
				.Where(wrapper => !InternalsMembers(module, wrapper).IsEmpty)
				.Select(InInternals)
				.FirstOrDefault();
	}
}
