using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	/// <summary>
	///     The factories a container generates for the implementations private to it (see <see cref="RecordBaseFactory" />),
	///     by implementation, and why it generates none for the others, so an extending container in the same assembly
	///     can name the reason in AWT226.
	/// </summary>
	/// <remarks>Lives only inside one graph build and never reaches a model; the emitted factories travel as <see cref="ContainerModel.Factories" />.</remarks>
	internal sealed class BaseFactories
	{
		public List<RegistrationFactory> Factories { get; } = new();

		public Dictionary<string, FactoryFault> Faults { get; } = new(StringComparer.Ordinal);
	}

	/// <summary>Why an owner generates no factory for an implementation, and what the user can do about it.</summary>
	internal readonly record struct FactoryFault(string Reason, string Remedy);

	/// <summary>What <see cref="RecordBaseFactory" /> needs of one built instance.</summary>
	private readonly record struct BaseFactoryRequest(
		ImplInfo Info,
		IMethodSymbol Producer,
		string? ResolverType,
		List<MemberModel> Members,
		IMethodSymbol? OnActivated,
		IMethodSymbol? OnRelease);

	/// <summary>
	///     Records the factory of an implementation private to the container, or to a class containing it, that the
	///     container constructs itself. A container that extends this one cannot construct such a type from its own
	///     generated code, so this container emits an <c>internal</c> factory for it, named like a module's
	///     (<see cref="RegistrationFactoryName" />), that mirrors the constructor the container picked and returns the
	///     accessible resolver type, plus an <c>internal</c> wrapper per lifecycle hook. Only a container without
	///     <c>Extends</c> records any, since extending is one level deep. Decorators, composites and closings of an
	///     open generic registration are not built this way yet. What the factory cannot express (a generic type,
	///     injected properties, a constructor or hook parameter carrying <c>[Arg]</c>, <c>[FromKey]</c> or <c>[Inject]</c>,
	///     or a parameter type private to the container) is recorded as a fault instead, which an extending container
	///     reports as AWT226; the container itself reports nothing, since it builds the type.
	/// </summary>
	private static void RecordBaseFactory(BaseFactoryRequest request, BuildContext context)
	{
		ImplInfo info = request.Info;
		INamedTypeSymbol container = context.ContainerSymbol;
		if (context.Factories is not { } factories
		    || info.Origin is not null
		    || info.OnActivatedOrigin is not null
		    || info.OnReleaseOrigin is not null
		    || info.Production != ProductionKind.Constructor
		    || info.ConstructVia is not null
		    || request.ResolverType is null
		    || info.ImplementationType != info.Symbol.ToDisplayString(FullyQualified)
		    || info.IsComposite
		    || (info.OnActivated is not null && request.OnActivated is null)
		    || (info.OnRelease is not null && request.OnRelease is null)
		    || !IsHiddenInModule(info.Symbol.OriginalDefinition, container, context.Compilation))
		{
			return;
		}

		if (BaseFactoryFault(request, container, context.Compilation) is { } fault)
		{
			factories.Faults[info.ImplementationType] = fault;
			return;
		}

		List<RegistrationHook> hooks = new();
		foreach ((IMethodSymbol? hook, string? name, bool release) in new[] { (request.OnActivated, info.OnActivated, false), (request.OnRelease, info.OnRelease, true), })
		{
			if (hook is not null && name is not null)
			{
				string target = hook.TypeArguments.Length == 0
					? name
					: $"{name}<{string.Join(", ", hook.TypeArguments.Select(argument => argument.ToDisplayString(FullyQualified)))}>";
				hooks.Add(new RegistrationHook(
					new ModuleHook(RegistrationHookWrapperName(info.Symbol, release, name), target, hook.Parameters[0].Type.ToDisplayString(FullyQualified), MirroredParameters(hook.Parameters.Skip(1)), hook.ReturnType.ToDisplayString(FullyQualified)),
					release));
			}
		}

		factories.Factories.Add(new RegistrationFactory(
			RegistrationFactoryName(info.Symbol),
			request.ResolverType,
			info.ImplementationType,
			MirroredParameters(request.Producer.Parameters),
			new EquatableArray<RegistrationHook>(hooks.ToArray())));
	}

	/// <summary>Why <see cref="RecordBaseFactory" /> cannot generate a factory for the request, or <see langword="null" />.</summary>
	private static FactoryFault? BaseFactoryFault(BaseFactoryRequest request, INamedTypeSymbol container, Compilation compilation)
	{
		INamedTypeSymbol implementation = request.Info.Symbol;
		string remedy = $"make '{Display(implementation.OriginalDefinition.ToDisplayString(FullyQualified))}' internal";
		if (implementation.IsGenericType)
		{
			return new FactoryFault("it is generic", remedy);
		}

		if (request.Members.Count > 0)
		{
			return new FactoryFault($"its property '{request.Members[0].MemberName}' is injected", remedy);
		}

		return ParameterFault(request.Producer.Parameters, "its constructor parameter")
		       ?? (request.OnActivated is { } activation ? ParameterFault(activation.Parameters.Skip(1), "its OnActivated hook parameter") : null)
		       ?? (request.OnRelease is { } release ? ParameterFault(release.Parameters.Skip(1), "its OnRelease hook parameter") : null);

		FactoryFault? ParameterFault(IEnumerable<IParameterSymbol> parameters, string subject)
		{
			foreach (IParameterSymbol parameter in parameters)
			{
				if (MirroredAwayAttribute(parameter.GetAttributes()) is { } attribute)
				{
					return new FactoryFault($"{subject} '{parameter.Name}' is marked {attribute}", remedy);
				}

				if (TypeHiddenInModule(parameter.Type, container, compilation) is { } hidden)
				{
					string type = Display(hidden.ToDisplayString(FullyQualified));
					return new FactoryFault($"{subject} '{parameter.Name}' takes '{type}', which only '{Display(container.ToDisplayString(FullyQualified))}' can name", $"make '{type}' internal");
				}
			}

			return null;
		}
	}

	/// <summary>The parameters of a constructor or hook, mirrored onto a generated factory or wrapper.</summary>
	private static EquatableArray<FactoryParameter> MirroredParameters(IEnumerable<IParameterSymbol> parameters)
		=> new(parameters.Select(MirrorParameter).ToArray());

	/// <summary>One constructor parameter mirrored onto a generated factory.</summary>
	private static FactoryParameter MirrorParameter(IParameterSymbol parameter)
		=> new(parameter.Type.ToDisplayString(FullyQualifiedWithNullability), parameter.Name);

	/// <summary>
	///     A registration a container in the owner's own assembly builds through the factory the owner (a module or a
	///     base container) generates in the same run, found by its deterministic name: the container keeps the real
	///     implementation, so it reads its initialization and disposal off the type itself, calls the factory instead of
	///     the constructor it cannot reach, and runs the hooks through the generated wrappers. A hook the owner generated
	///     no wrapper for (because its own build rejected it) is dropped.
	/// </summary>
	private static RawRegistration ThroughSameCompilationFactory(RawRegistration registration, INamedTypeSymbol owner, RegistrationFactory factory)
		=> registration with
		{
			ConstructVia = $"{owner.ToDisplayString(FullyQualified)}.{factory.FactoryName}",
			ConstructViaType = factory.ReturnType,
			OnActivated = HasHookWrapper(factory, registration.Implementation, registration.OnActivated, release: false) ? registration.OnActivated : null,
			OnRelease = HasHookWrapper(factory, registration.Implementation, registration.OnRelease, release: true) ? registration.OnRelease : null,
		};

	/// <summary>
	///     A registration a container in another assembly builds through the factory the owner (a module or a base
	///     container) carries in its metadata: the factory produces the accessible type it returns, the hooks run through
	///     the generated wrappers, and the implementation's initialization and disposal are read off its metadata, so the
	///     container initializes, disposes and shares it exactly as it would a type it constructs. Every other option of
	///     the registration stays.
	/// </summary>
	private static RawRegistration ThroughGeneratedFactory(RawRegistration registration, IMethodSymbol factory)
	{
		INamedTypeSymbol implementation = registration.Implementation;
		return registration with
		{
			Implementation = (INamedTypeSymbol)factory.ReturnType,
			Production = ProductionKind.Factory,
			ProductionMember = factory.Name,
			OnActivated = registration.OnActivated is { } activation ? RegistrationHookWrapperName(implementation, release: false, activation) : null,
			OnRelease = registration.OnRelease is { } release ? RegistrationHookWrapperName(implementation, release: true, release) : null,
			Traits = TraitsOf(implementation),
			TraitsExact = true,
		};
	}

	/// <summary>
	///     The factory <paramref name="owner" />, a module or a base container, generated for
	///     <paramref name="implementation" /> in its own build, read from its metadata, or <see langword="null" /> when the
	///     container cannot see one: the owner carries none, or it is internal and the owner's assembly does not grant
	///     the container's assembly <c>InternalsVisibleTo</c>, so it is not even imported.
	/// </summary>
	private static IMethodSymbol? GeneratedFactory(INamedTypeSymbol owner, INamedTypeSymbol implementation, Compilation compilation)
		=> owner.GetMembers(RegistrationFactoryName(implementation))
			.OfType<IMethodSymbol>()
			.FirstOrDefault(method => method is { IsStatic: true, ReturnType: INamedTypeSymbol, } && compilation.IsSymbolAccessibleWithin(method, compilation.Assembly));
}
