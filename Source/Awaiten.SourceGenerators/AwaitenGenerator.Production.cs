using System.Collections.Immutable;
using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Awaiten.SourceGenerators;

partial class AwaitenGenerator
{
	private static InstanceModel? BuildInstance(ImplInfo info, BuildContext context)
	{
		INamedTypeSymbol containerSymbol = context.ContainerSymbol;
		Compilation compilation = context.Compilation;
		Dictionary<ServiceKey, string> serviceToImpl = context.ServiceToImpl;
		WellKnownTypes wellKnown = context.WellKnown;
		List<DiagnosticInfo> diagnostics = context.Diagnostics;

		string? resolverType = null;
		if (info.ConstructVia is null
		    && LessAccessibleThanContainer(info.Symbol, containerSymbol) is { } narrower
		    && (resolverType = AccessibleResolverType(info, context)) is null)
		{
			ReportLessAccessibleImplementation(info, narrower, context);
			return null;
		}

		// Property injection: after the constructor, fill the opt-in [Inject] and container-side [InjectProperty]
		// members through an object initializer. Only a constructed instance is filled; a factory or pre-built
		// instance is produced whole by its source (an [InjectProperty] on it is AWT178). Each member edge is
		// classified like a Direct constructor parameter, so it participates fully in cycle, captive and async-taint
		// analysis. Computed before the Instance early-return so its AWT178 is still reported for one.
		List<MemberModel> members = BuildInjectedMembers(info, context);

		// A pre-built Instance is handed back from a container member, never constructed here. The container does
		// not own it: it is not disposed, may be an interface (no not-instantiable check), contributes no edges, and
		// is never async-initialized (InitializeAsync never runs). The caller owns construction and lifetime.
		if (info.Production == ProductionKind.Instance)
		{
			RecordBaseWrappers(info, null, null, null, context);
			return BuildPrebuiltInstance(info, containerSymbol, compilation, diagnostics) with { ResolverType = resolverType, Unlisted = info.Unlisted, };
		}

		// The producer is a container method (Factory) or the implementation's constructor (default). Null
		// means the registration is unusable and a diagnostic was already reported.
		IMethodSymbol? producer = SelectProducer(info, containerSymbol, compilation, serviceToImpl, context.External, diagnostics);
		if (producer is null)
		{
			return null;
		}

		if (producer.Parameters.Any(parameter => HasArgAttribute(parameter.GetAttributes())
		                                         && ReportLessAccessibleArg(info, parameter.Type, containerSymbol, diagnostics)))
		{
			return null;
		}

		members = WithDeferredSetters(members, info, producer, context);

		// An async factory returns Task<T> / ValueTask<T>: the container awaits it, so the owned type is the
		// awaited result T, not the Task. A sync factory owns its return type; a constructed impl owns info.Symbol.
		bool asyncFactory = info.Production == ProductionKind.Factory
		                    && IsAsyncFactoryReturn(producer.ReturnType, compilation, out _);

		// A factory's parameters resolve from the graph like a constructor's. An async factory additionally
		// forwards the resolve-time CancellationToken into a matching parameter instead of resolving it.
		List<ParameterModel> parameters = ClassifyParameters(producer, info, asyncFactory, context);

		// Disposability follows the owned type: a factory's produced type (for an async factory the awaited T,
		// not the Task), or the constructed implementation. Using info.Symbol for a factory would miss a
		// DisposableX behind an IX and leak it.
		ITypeSymbol disposalType = info.Production == ProductionKind.Factory
			? ProducedType(producer.ReturnType, compilation)
			: info.Symbol;
		// A self-compiled implementation hides behind the accessible type its module's generated factory returns, so
		// its traits come from the module instead, as if the container constructed the implementation itself.
		ImplementationTraits? traits = info.Traits;
		bool disposable = wellKnown.Disposable is not null && (traits?.Disposable ?? ImplementsInterface(disposalType, wellKnown.Disposable));

		// Async disposal mirrors sync disposal: the drain awaits DisposeAsync, preferring it over IDisposable
		// when a type is both. Recognized only when the runtime exposes async disposal; otherwise the generated
		// container stays sync-dispose only.
		bool asyncDisposable = wellKnown.AsyncDisposable is not null && (traits?.AsyncDisposable ?? ImplementsInterface(disposalType, wellKnown.AsyncDisposable));

		// A factory's declared return type can hide a concrete IDisposable (or IAsyncDisposable) behind a
		// non-disposable service interface or base class, which the static flags above miss. When a subtype
		// could be one (the declared type is an interface or a non-sealed class), the emitter tracks the
		// realized instance behind a runtime `is IDisposable or IAsyncDisposable` test. A sealed declared type
		// that is neither cannot hide one, and the runtime test would not compile. Constructed and pre-built
		// Instance production never lie: info.Symbol is the concrete type, and an Instance is not owned.
		bool runtimeDisposalCheck = info.Production == ProductionKind.Factory
		                            && !info.TraitsExact
		                            && !disposable
		                            && !asyncDisposable
		                            && CouldHideDisposable(disposalType);

		// Best-effort lint (AWT106): a sync factory whose declared return type hides the async initialization
		// its body provably produces. The conventions are matched against the owned type, so a concrete type a
		// convention initializes returned behind a plainer interface is never initialized. Not reported for an
		// async factory (it awaits its own initialization) nor a hidden IDisposable (disposed via RuntimeDisposalCheck).
		if (info.Production == ProductionKind.Factory && !asyncFactory && traits is null)
		{
			ReportFactoryHidingAsyncInitialization(producer, compilation, context.AsyncInitializers.Of(info.ConventionScope(containerSymbol)), diagnostics);
		}

		// Lifecycle hooks (AWT164 when a named member is not a usable static void M(TImplementation, …)). Applied to
		// constructed and factory-produced instances - the ones the container owns; a pre-built Instance returns
		// above (the caller owns it, so activation/release do not apply). A hook's parameters after the instance are
		// graph dependencies (AWT101 when unregistered, AWT189 for a runtime [Arg], AWT191 for a Func/Lazy on the
		// release hook), resolved like a constructor's.
		(string? onActivated, EquatableArray<ParameterModel> activationParameters, IMethodSymbol? activationHook) = ResolveHook(info, info.OnActivated, release: false, context);
		(string? onRelease, EquatableArray<ParameterModel> releaseParameters, IMethodSymbol? releaseHook) = ResolveHook(info, info.OnRelease, release: true, context);

		// Async initialization follows the owned type (a factory's concrete return type or the constructed impl),
		// mirroring the disposal-type choice above: the container awaits each matching convention after OnActivated.
		// (An async factory is async-tainted regardless; see PropagateAsyncTaint.)
		EquatableArray<AsyncInitializerModel> asyncInitializers = BuildAsyncInitializers(info, disposalType, context);

		RecordBaseFactory(new BaseFactoryRequest(info, producer, resolverType, members, activationHook, releaseHook), context);
		RecordBaseWrappers(info, producer, activationHook, releaseHook, context);

		// An implementation hidden in its module is built through the module's factory, which returns its service.
		bool viaModuleFactory = info.ConstructVia is not null;
		bool asyncRelease = releaseHook is not null && IsAsyncHookReturn(releaseHook.ReturnType, compilation);
		return new InstanceModel(
			info.ImplementationType,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<ServiceKey>(info.Services.ToArray()),
			new EquatableArray<ParameterModel>(parameters.ToArray()),
			disposable,
			info.Symbol.IsReferenceType,
			viaModuleFactory ? ProductionKind.Factory : info.Production,
			viaModuleFactory ? info.ConstructVia : ProducerCall(info, producer, compilation),
			asyncInitializers,
			IsAsyncFactory: asyncFactory,
			RuntimeDisposalCheck: runtimeDisposalCheck,
			IsAsyncDisposable: asyncDisposable,
			EmitType: viaModuleFactory ? info.ConstructViaType : EmitTypeOf(info),
			InjectedMembers: new EquatableArray<MemberModel>(members.ToArray()),
			// Eager build-time construction is the sync analog of InitializeAsync warming singletons, so it
			// applies to a singleton only. The attribute exposes Eager on [Singleton<…>] alone, but the flag
			// coalesces onto the implementation, so this guard keeps a coalesced non-singleton from carrying it.
			Eager: info.Eager && info.Lifetime == Lifetime.Singleton,
			OnActivated: onActivated,
			OnRelease: onRelease,
			ActivationParameters: activationParameters,
			ReleaseParameters: releaseParameters,
			SuppressDisposal: info.SuppressDisposal,
			PrivateServices: PrivateServices(info, containerSymbol),
			ResolverType: resolverType,
			IsAsyncActivation: activationHook is not null && IsAsyncHookReturn(activationHook.ReturnType, compilation),
			IsAsyncRelease: asyncRelease,
			AsyncReleaseHookName: asyncRelease && releaseHook is { } hook && hook.ContainingType.Name != InternalsClass ? hook.Name : null,
			Unlisted: info.Unlisted);

		static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol @interface)
		{
			return SymbolEqualityComparer.Default.Equals(type, @interface)
			       || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
		}

		// Whether a value of this declared type could be IDisposable at runtime through an unrevealed subtype:
		// an interface or type parameter (any implementer qualifies) or a non-sealed class (a derived type may
		// implement it). A sealed class or struct that does not itself implement IDisposable cannot, so a
		// runtime `is IDisposable` test against it is pointless (and a compile error for a sealed class, CS8121/CS0184).
		static bool CouldHideDisposable(ITypeSymbol type)
			=> type.TypeKind is TypeKind.Interface or TypeKind.TypeParameter
			   || (type.TypeKind == TypeKind.Class && !type.IsSealed);
	}

	/// <summary>
	///     The injected members of an implementation a module's generated factory builds, completed with the deferred
	///     ones, which the factory leaves to its setter wrappers (see <see cref="DeferredSetterName" />): read from the
	///     wrappers next to a factory from metadata, each classified like a deferred property, or, for a factory the
	///     module generates in the same run, the deferred members the container discovered pointed at their wrappers.
	/// </summary>
	private static List<MemberModel> WithDeferredSetters(List<MemberModel> members, ImplInfo info, IMethodSymbol producer, BuildContext context)
	{
		if (info.ConstructVia is { } via && producer.MethodKind != MethodKind.Ordinary)
		{
			string owner = via.Substring(0, via.LastIndexOf('.'));
			string factory = via.Substring(via.LastIndexOf('.') + 1);
			return members.Select(member => member.Deferred ? member with { Setter = $"{owner}.{DeferredSetterName(factory, member.MemberName)}", } : member).ToList();
		}

		if (!IsGeneratedOwnerFactory(producer))
		{
			return members;
		}

		string prefix = DeferredSetterName(producer.Name, string.Empty);
		foreach (IMethodSymbol setter in producer.ContainingType.GetMembers().OfType<IMethodSymbol>()
			         .Where(method => method is { IsStatic: true, Parameters.Length: 2, } && method.Name.StartsWith(prefix, StringComparison.Ordinal)))
		{
			ParameterModel dependency = ClassifyParameter(setter.Parameters[1], asyncFactory: false, context.External.ServiceTypes);
			ReportWhenUnregistered(dependency, info, context);
			members.Add(new MemberModel(setter.Name.Substring(prefix.Length), dependency, Deferred: true, $"{producer.ContainingType.ToDisplayString(FullyQualified)}.{setter.Name}"));
		}

		return members;
	}

	/// <summary>Whether <paramref name="method" /> is a factory a module or base container generated into its <c>Internals</c> class.</summary>
	private static bool IsGeneratedOwnerFactory(IMethodSymbol method)
		=> method is { MethodKind: MethodKind.Ordinary, ContainingType: { Name: InternalsClass, ContainingType: not null, }, };

	/// <summary>
	///     The property-injection members of an instance: its opt-in <c>[Inject]</c> properties plus the container's
	///     <c>[InjectProperty&lt;TImpl&gt;]</c> entries matched to it. Only a container-constructed instance is
	///     filled; a Factory/Instance-produced implementation is produced whole by its source, so an
	///     <c>[InjectProperty]</c> entry on it yields no member and is
	///     <see cref="Diagnostics.InjectPropertyOnNonConstructed">AWT178</see> (mirroring the
	///     <see cref="ProductionKind.Constructor" /> gate). Extracted from <see cref="BuildInstance" /> to keep its
	///     branching flat.
	/// </summary>
	private static List<MemberModel> BuildInjectedMembers(ImplInfo info, BuildContext context)
	{
		List<InjectPropertyEntry> injectProperties =
			context.InjectProperties.TryGetValue(RegisteredType(info.ImplementationType), out List<InjectPropertyEntry>? entries)
				? entries
				: new List<InjectPropertyEntry>();

		if (OpenInjectPropertyKey(info, context.Compilation) is { } openKey
		    && context.InjectProperties.TryGetValue(openKey, out List<InjectPropertyEntry>? openEntries))
		{
			injectProperties = MergeOpenInjectProperties(info, injectProperties, openEntries, context.Diagnostics);
		}

		List<MemberModel> members = new();

		// A factory read from its owner's metadata takes the injected properties as parameters of its own.
		if (info.Constructor is { MethodKind: MethodKind.Ordinary, })
		{
			return members;
		}

		if (info.Production != ProductionKind.Constructor)
		{
			foreach (InjectPropertyEntry entry in injectProperties)
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.InjectPropertyOnNonConstructed,
					entry.Location ?? info.Location,
					new EquatableArray<string>([entry.PropertyName, DisplayInstance(info.ImplementationType),])));
			}

			return members;
		}

		DiscoverInjectedMembers(
			info,
			new InjectionContext(context.ContainerSymbol, context.Compilation, context.ServiceToImpl, context.ConstraintRejected, context.ConsumedConditionals, context.External.ServiceTypes, context.DispatchCandidates, context.Diagnostics),
			injectProperties,
			members);
		return members;
	}

	/// <summary>
	///     The key of the <c>[InjectProperty(typeof(Definition&lt;&gt;), name)]</c> entries that apply to
	///     <paramref name="info" />: its generic definition when it is a closing constructed under its own type, or
	///     <see langword="null" />. A decorator chain link carries a synthetic identity and is skipped, exactly as a
	///     closed entry naming the decorator type does not reach it.
	/// </summary>
	private static string? OpenInjectPropertyKey(ImplInfo info, Compilation compilation)
		=> !SymbolEqualityComparer.Default.Equals(info.Symbol, info.Symbol.OriginalDefinition)
		   && RegisteredType(info.ImplementationType) == TypeIdentity(info.Symbol, compilation)
			? TypeIdentity(info.Symbol.OriginalDefinition, compilation)
			: null;

	/// <summary>
	///     Appends the open-generic entries for a closing to its closed entries. A closed entry is the more specific
	///     declaration, so an open entry for a property a closed entry already names is ignored for this closing. A
	///     closed entry that changes <c>Key</c>, <c>Optional</c> or <c>Deferred</c> specialises the closing on purpose;
	///     one that changes nothing is redundant and is reported as
	///     <see cref="Diagnostics.DuplicateInjectProperty">AWT179</see> at the open entry.
	/// </summary>
	private static List<InjectPropertyEntry> MergeOpenInjectProperties(
		ImplInfo info,
		List<InjectPropertyEntry> closedEntries,
		List<InjectPropertyEntry> openEntries,
		List<DiagnosticInfo> diagnostics)
	{
		List<InjectPropertyEntry> merged = new(closedEntries);
		foreach (InjectPropertyEntry entry in openEntries)
		{
			if (closedEntries.FirstOrDefault(closed => closed.PropertyName == entry.PropertyName) is { } closed)
			{
				if (closed.Optional == entry.Optional && closed.Deferred == entry.Deferred && closed.Key == entry.Key)
				{
					diagnostics.Add(new DiagnosticInfo(
						Diagnostics.DuplicateInjectProperty,
						entry.Location ?? info.Location,
						new EquatableArray<string>([entry.PropertyName, DisplayInstance(info.ImplementationType),])));
				}

				continue;
			}

			merged.Add(entry);
		}

		return merged;
	}

	/// <summary>
	///     Builds the model for a pre-built <c>Instance</c> registration - handed back from a container member,
	///     never constructed here. It validates the named member (AWT109/AWT153) and rejects a lifecycle hook
	///     (AWT165), since the container does not own the instance and so never runs a hook around it.
	/// </summary>
	private static InstanceModel BuildPrebuiltInstance(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		ValidateInstanceMember(containerSymbol, info, compilation, diagnostics);
		// AWT165: a lifecycle hook on a pre-built Instance is a silent no-op (the caller, not the container,
		// owns and tears down the instance), so reject it rather than construct with hooks that never run.
		if (info.OnActivated is not null || info.OnRelease is not null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.LifecycleHookOnInstance,
				info.Location,
				new EquatableArray<string>([Display(info.OwningServiceOrImpl),])));
		}

		// AWT192: the container never disposes a pre-built Instance (the caller owns it), so opting out of that
		// disposal is a silent no-op, rejected for the same reason as a lifecycle hook above.
		if (info.SuppressDisposal)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.SuppressDisposalOnInstance,
				info.Location,
				new EquatableArray<string>([Display(info.OwningServiceOrImpl),])));
		}

		return new InstanceModel(
			info.ImplementationType,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<ServiceKey>(info.Services.ToArray()),
			new EquatableArray<ParameterModel>([]),
			false,
			info.Symbol.IsReferenceType,
			ProductionKind.Instance,
			QualifiedProductionMember(info, compilation),
			EmitType: EmitTypeOf(info),
			Eager: info.Eager && info.Lifetime == Lifetime.Singleton,
			PrivateServices: PrivateServices(info, containerSymbol));
	}

	/// <summary>
	///     The service types of <paramref name="info" /> that are private or protected nested types the generated
	///     <c>Root</c> cannot expose (see <see cref="InstanceModel.PrivateServices" />), found among the
	///     implementation's own type, base types and interfaces.
	/// </summary>
	private static EquatableArray<string> PrivateServices(ImplInfo info, INamedTypeSymbol containerSymbol)
	{
		HashSet<string> services = new(info.Services.Select(service => service.Service), StringComparer.Ordinal);
		List<string> privateServices = new();
		for (INamedTypeSymbol? type = info.Symbol; type is not null; type = type.BaseType)
		{
			Add(type);
		}

		foreach (INamedTypeSymbol @interface in info.Symbol.AllInterfaces)
		{
			Add(@interface);
		}

		return new EquatableArray<string>(privateServices.ToArray());

		void Add(INamedTypeSymbol type)
		{
			if (LessAccessibleThanContainer(type, containerSymbol) is not null
			    && type.ToDisplayString(FullyQualified) is { } display
			    && services.Contains(display))
			{
				privateServices.Add(display);
			}
		}
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.RegisteredArgTypeLessAccessible">AWT216</see> and returns
	///     <see langword="true" /> when the <c>[Arg]</c> type <paramref name="type" /> is a private or protected nested
	///     type the parameterized resolvers cannot take.
	/// </summary>
	private static bool ReportLessAccessibleArg(ImplInfo info, ITypeSymbol type, INamedTypeSymbol containerSymbol, List<DiagnosticInfo> diagnostics)
	{
		if (LessAccessibleThanContainer(type, containerSymbol) is not { } narrower)
		{
			return false;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.RegisteredArgTypeLessAccessible,
			info.Location,
			new EquatableArray<string>([Display(info.Symbol.ToDisplayString(FullyQualified)), Display(narrower.ToDisplayString(FullyQualified)),])));
		return true;
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.RegisteredTypeLessAccessible">AWT216</see> for an implementation that is, or
	///     contains, the private or protected <paramref name="narrower" /> and has no accessible resolver type (see
	///     <see cref="AccessibleResolverType" />), naming the first reason that applies: the container cannot reach a
	///     type it registers itself, the type is a type argument every service carries, a service is itself private, the
	///     container cannot reach a type its module does not build for it (see <see cref="SelfCompilesRegistration" />),
	///     or no accessible type covers all of the services. For a struct the remedy says that implementations must be
	///     classes, since no access modifier makes a struct one.
	/// </summary>
	private static void ReportLessAccessibleImplementation(ImplInfo info, INamedTypeSymbol narrower, BuildContext context)
	{
		string implementation = Display(info.Symbol.ToDisplayString(FullyQualified));
		string type = Display(narrower.ToDisplayString(FullyQualified));
		(string reason, string remedy) = MissingResolverTypeReason(info.Symbol, narrower, ServicesOf(info, context), context.ContainerSymbol, context.Compilation, info.Origin is not null) switch
		{
			{ Unreachable: true, } when info.Origin is null => ("the generated container cannot reach it", $"nest the container beside it, or make '{type}' internal"),
			{ Unreachable: true, } => ("the generated container cannot reach it", $"make '{type}' internal"),
			{ TypeArgument: true, } => ($"'{implementation}' carries it as a type argument into every type it converts to", $"make '{type}' internal or public"),
			{ PrivateService: { } service, } => (RegisteredAsPrivateService(info.Symbol, service), $"register it under an accessible service, or make '{type}' internal or public"),
			_ => ("no accessible type it converts to covers all of its services", "register it under one service, or give it an accessible base type that covers its services"),
		};
		if (info.Symbol.IsValueType)
		{
			remedy = $"an implementation must be a class, so wrap '{implementation}' in a class, or register an interface it implements with a Factory or Instance that returns it";
		}

		context.Diagnostics.Add(new DiagnosticInfo(
			Diagnostics.RegisteredTypeLessAccessible,
			info.Location,
			new EquatableArray<string>([implementation, type, reason, remedy,])));
	}

	/// <summary>
	///     The reason fragment for an implementation registered under the private <paramref name="service" />, which
	///     names the service only when it is not the implementation itself.
	/// </summary>
	private static string RegisteredAsPrivateService(INamedTypeSymbol implementation, INamedTypeSymbol service)
		=> SymbolEqualityComparer.Default.Equals(implementation, service)
			? "it is registered as itself"
			: $"it is registered as the private service '{Display(service.ToDisplayString(FullyQualified))}'";

	/// <summary>
	///     Why <paramref name="implementation" />, which is or contains the private or protected
	///     <paramref name="narrower" />, has no accessible type to stand in for it under <paramref name="services" />
	///     (see <see cref="CoveringResolverType" />): the container cannot reach it, the private type is a type argument
	///     every type it converts to carries, or one of the services is itself private. All three unset means no
	///     accessible type covers all of the services. For a <paramref name="moduleRegistered" /> implementation, a
	///     private service wins over the container's reach, since the module builds the type for the container unless
	///     its factory cannot return the service.
	/// </summary>
	private static (bool Unreachable, bool TypeArgument, INamedTypeSymbol? PrivateService) MissingResolverTypeReason(
		INamedTypeSymbol implementation,
		INamedTypeSymbol narrower,
		HashSet<string> services,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		bool moduleRegistered)
	{
		bool reachable = compilation.IsSymbolAccessibleWithin(implementation, containerSymbol);
		if (!reachable && !moduleRegistered)
		{
			return (true, false, null);
		}

		bool nestingLevel = false;
		for (INamedTypeSymbol? level = implementation; level is not null; level = level.ContainingType)
		{
			nestingLevel |= SymbolEqualityComparer.Default.Equals(level, narrower);
		}

		if (!nestingLevel)
		{
			return (false, true, null);
		}

		INamedTypeSymbol? privateService = ServableTypes(implementation)
			.Prepend(implementation)
			.FirstOrDefault(candidate => services.Contains(candidate.ToDisplayString(FullyQualified))
			                             && LessAccessibleThanContainer(candidate, containerSymbol) is not null);
		return (!reachable && privateService is null, false, privateService);
	}

	/// <summary>
	///     The accessible type the generated resolvers return for an implementation that is private or protected to the
	///     container, or <see langword="null" /> when there is none. The resolver bodies sit inside the container, so they
	///     may construct and hold the implementation; only their signatures must not name it. The type converts to every
	///     service and collection the implementation serves, so all of them still resolve without a cast. In a fixed
	///     order, so the generated code is the same for every build: a registered service that covers the others, else
	///     the most derived accessible base class that covers them, else the first covering interface in the order the
	///     implementation declares its interfaces.
	/// </summary>
	private static string? AccessibleResolverType(ImplInfo info, BuildContext context)
		=> CoveringResolverType(info.Symbol, ServicesOf(info, context), context.ServedTypes.Symbols, context.ContainerSymbol, context.Compilation);

	/// <summary>
	///     The accessible type <paramref name="implementation" /> converts to that covers all of
	///     <paramref name="services" />, in the order <see cref="AccessibleResolverType" /> describes, or
	///     <see langword="null" />. <paramref name="symbols" /> maps a registered service to its symbol, so a service the
	///     implementation reaches only through variance still counts as covered.
	/// </summary>
	private static string? CoveringResolverType(
		INamedTypeSymbol implementation,
		HashSet<string> services,
		Dictionary<string, INamedTypeSymbol> symbols,
		INamedTypeSymbol containerSymbol,
		Compilation compilation)
	{
		if (services.Count == 0 || !compilation.IsSymbolAccessibleWithin(implementation, containerSymbol))
		{
			return null;
		}

		List<INamedTypeSymbol> candidates = ServableTypes(implementation)
			.Where(candidate => LessAccessibleThanContainer(candidate, containerSymbol) is null)
			.ToList();
		return candidates
			.Where(candidate => services.Contains(candidate.ToDisplayString(FullyQualified)))
			.Concat(candidates)
			.FirstOrDefault(candidate => services.All(service => Covers(candidate, service)))
			?.ToDisplayString(FullyQualified);

		bool Covers(INamedTypeSymbol candidate, string service)
			=> symbols.TryGetValue(service, out INamedTypeSymbol? symbol)
				? SatisfiesConstraintType(candidate, symbol, compilation)
				: candidate.ToDisplayString(FullyQualified) == service;
	}

	/// <summary>
	///     What <see cref="AccessibleResolverType" /> needs beyond an implementation's won services: the services of the
	///     collections and keyed collections each implementation is a member of, by implementation, and the symbol of
	///     each registered service, so a service the implementation reaches only through variance still counts as covered.
	/// </summary>
	private sealed record ServedTypes(Dictionary<string, HashSet<string>> Members, Dictionary<string, INamedTypeSymbol> Symbols);

	private static ServedTypes ServedTypesOf(
		List<RawRegistration> raw,
		Dictionary<ServiceKey, List<string>> serviceMembers,
		Dictionary<string, List<KeyedMember>> keyedMembers)
	{
		Dictionary<string, HashSet<string>> members = new(StringComparer.Ordinal);
		foreach ((string service, IEnumerable<string> implementations) in serviceMembers.Select(entry => (entry.Key.Service, (IEnumerable<string>)entry.Value))
			         .Concat(keyedMembers.Select(entry => (entry.Key, entry.Value.Select(member => member.Implementation)))))
		{
			foreach (string implementation in implementations)
			{
				if (!members.TryGetValue(implementation, out HashSet<string>? services))
				{
					members[implementation] = services = new HashSet<string>(StringComparer.Ordinal);
				}

				services.Add(service);
			}
		}

		return new ServedTypes(members, ServiceSymbolsOf(raw));
	}

	/// <summary>The symbol of each registered service, by its fully-qualified name.</summary>
	private static Dictionary<string, INamedTypeSymbol> ServiceSymbolsOf(List<RawRegistration> raw)
	{
		Dictionary<string, INamedTypeSymbol> symbols = new(StringComparer.Ordinal);
		foreach (RawRegistration registration in raw)
		{
			if (registration.ServiceSymbol is { } symbol)
			{
				symbols[registration.ServiceType] = symbol;
			}
		}

		return symbols;
	}

	/// <summary>
	///     The types an implementation converts to without a cast, in a fixed order: its base classes from the most
	///     derived, then its interfaces in declaration order.
	/// </summary>
	private static IEnumerable<INamedTypeSymbol> ServableTypes(INamedTypeSymbol implementation)
	{
		for (INamedTypeSymbol? type = implementation.BaseType; type is not null; type = type.BaseType)
		{
			yield return type;
		}

		foreach (INamedTypeSymbol @interface in implementation.AllInterfaces)
		{
			yield return @interface;
		}
	}

	/// <summary>
	///     Every service an implementation resolves as: the ones it won and the collections and keyed collections it is
	///     a member of.
	/// </summary>
	private static HashSet<string> ServicesOf(ImplInfo info, BuildContext context)
	{
		HashSet<string> services = new(info.Services.Select(service => service.Service), StringComparer.Ordinal);
		if (context.ServedTypes.Members.TryGetValue(info.ImplementationType, out HashSet<string>? memberServices))
		{
			services.UnionWith(memberServices);
		}

		return services;
	}

	/// <summary>
	///     The real type of an instance whose <c>ImplementationType</c> is a synthetic identity (a decorator chain link,
	///     so one decorator type can be several distinct instances, or a member-identified <c>Factory</c>/<c>Instance</c>
	///     registration, see <see cref="AssignMemberIdentities" />), or <see langword="null" /> when the identity is the type.
	/// </summary>
	private static string? EmitTypeOf(ImplInfo info)
	{
		string realType = info.Symbol.ToDisplayString(FullyQualified);
		return info.ImplementationType == realType ? null : realType;
	}

	/// <summary>
	///     Selects the method that produces an implementation: a container method for a <c>Factory</c>
	///     registration, or the implementation's own constructor otherwise. Returns <see langword="null" />
	///     when the registration is unusable, having already appended the diagnostic: an unresolved factory
	///     (AWT108), a non-instantiable abstract or interface type (AWT103), or a type with no accessible
	///     constructor (AWT104). A factory produces the instance, so the registered type may be an interface
	///     and skips the not-instantiable check a constructed type gets.
	/// </summary>
	private static IMethodSymbol? SelectProducer(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		ExternalSurface external,
		List<DiagnosticInfo> diagnostics)
	{
		if (info.Production == ProductionKind.Factory)
		{
			return ResolveFactory(containerSymbol, info, compilation, diagnostics);
		}

		// An abstract type or interface cannot be constructed; reject it rather than emit an uncompilable 'new'.
		if (info.Symbol.IsAbstract || info.Symbol.TypeKind == TypeKind.Interface)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NotInstantiable,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
			return null;
		}

		if (info.Constructor is { } mirrored)
		{
			return mirrored;
		}

		// A same-compilation module-scan match builds through the greediest accessible constructor - the one the
		// module's generated factory mirrors - so the match constructs identically wherever the module is
		// imported from, instead of quietly falling back to a smaller constructor the local graph happens to satisfy.
		IMethodSymbol? constructor = SelectConstructor(
			info.Symbol, info.ConstructVia is null ? containerSymbol : info.Origin!, compilation, serviceToImpl.Keys.Select(k => k.Service), external,
			info.GreedyConstructor ? _ => true : null);
		if (constructor is null)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NoAccessibleConstructor,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
		}

		return constructor;
	}

	/// <summary>
	///     Resolves an <c>OnActivated</c> / <c>OnRelease</c> lifecycle hook to a <c>static void M(TImplementation, …)</c>
	///     (or, for an awaited hook, a <c>static Task</c>/<c>ValueTask</c> one; other awaitable returns are
	///     <see cref="Diagnostics.UnsupportedAsyncLifecycleHook">AWT232</see>)
	///     method on its owner - the container, or the module whose registration named the hook, kept per slot on
	///     <c>ImplInfo</c> since two scan registrations with different origins can fill one implementation's two
	///     slots (never falling back to the container) - returning the name the generated Root/Scope calls it by and its
	///     graph-resolved parameters (every parameter after the instance), or <c>(null, empty)</c> when the
	///     registration named none. The first parameter is the instance and accepts the implementation type; each
	///     parameter after it is resolved from the object graph exactly like a constructor parameter (see
	///     <see cref="ClassifyHookParameters" />). The owner is a static class, so the hook is a static method
	///     reached by simple name, exactly like a factory method - no receiver and no instance/static distinction;
	///     a module hook is qualified with the module type (the generated container is another class, so the simple
	///     name would not bind). The container's own members are reachable at any accessibility from the generated
	///     partial, so a <c>private</c> hook qualifies, but a module's are not: a module method that matches yet is
	///     inaccessible from the container is skipped (it cannot be called from the generated code), falling through
	///     to AWT164. Reports <see cref="Diagnostics.InvalidLifecycleHook">AWT164</see> and returns
	///     <c>(null, empty)</c> when no accessible ordinary void method of that name accepts the implementation type
	///     as its first parameter, and <see cref="Diagnostics.AmbiguousLifecycleHook">AWT190</see> (also returning
	///     <c>(null, empty)</c>) when more than one does, so the choice would be order-dependent. A generic hook
	///     (named on an open-generic <c>[Scan]</c> marker) binds its type arguments from the closed marker forms
	///     kept per hook slot on <c>ImplInfo</c>: exactly one bindable form (matching arity, accessible type
	///     arguments, satisfied constraints, a first parameter accepting the match; see
	///     <see cref="BindableHookConstructions" />) dispatches the
	///     constructed method, none leaves it unusable like any other shape mismatch (falling through to AWT164),
	///     and more than one is reported as <see cref="Diagnostics.GenericHookAmbiguousMarker">AWT198</see> when it
	///     is the only usable overload (a non-generic hook, needing no arguments, is unaffected). A multi-form
	///     generic overload beside another usable overload is an overload collision, AWT190, since settling the
	///     closings would still leave two usable overloads.
	/// </summary>
	private static (string? Hook, EquatableArray<ParameterModel> Parameters, IMethodSymbol? Method) ResolveHook(
		ImplInfo info,
		string? hookName,
		bool release,
		BuildContext context)
	{
		if (hookName is null)
		{
			return (null, default, null);
		}

		// The hook's own origin (the registration that named it), not the implementation's winning Origin: two
		// scan registrations with different origins can fill the two slots, and each name binds only on its owner.
		INamedTypeSymbol? origin = release ? info.OnReleaseOrigin : info.OnActivatedOrigin;

		// An implementation hidden in its module or base container keeps a hook only when its owner generated the
		// wrapper (see SelfCompiledRegistration, SameCompilationScanRegistration and BuildThroughBaseFactories), so the
		// owner's own resolution found exactly one hook; the container runs that hook's wrapper, which takes the
		// factory's return type and passes the private type on. A base container wraps a scan match's hooks like an
		// explicit registration's, since it generates one factory per implementation.
		if (info.ConstructVia is not null && origin is not null)
		{
			List<INamedTypeSymbol>? markers = null;
			if (info.IsScan)
			{
				markers = release ? info.OnReleaseMarkers : info.OnActivatedMarkers;
			}

			IMethodSymbol hook = CollectModuleHookOverloads(
				info.Symbol, hookName, markers, new ModuleScanContext(origin, context.Compilation, new List<DiagnosticInfo>(), new HashSet<string>(StringComparer.Ordinal))).Matches[0];
			string wrapper = info.IsScan && !HasAwaitenAttribute(origin.GetAttributes(), "ContainerAttribute")
				? ModuleHookWrapperName(info.Symbol, release)
				: RegistrationHookWrapperName(info.Symbol, release, hookName);
			return ($"{origin.ToDisplayString(FullyQualified)}.{InInternals(wrapper)}", ClassifyHookParameters(hook, info, release, context), hook);
		}

		(List<IMethodSymbol> matches, List<IReadOnlyList<INamedTypeSymbol>> ambiguousMethods) = CollectHookOverloads(
			info.Symbol,
			hookName,
			origin,
			release ? info.OnReleaseMarkers : info.OnActivatedMarkers,
			context.ContainerSymbol,
			context.Compilation);

		if (matches.Count == 1 && ambiguousMethods.Count == 0)
		{
			if (UnsupportedAsyncHook(matches[0], release, context.Compilation) is { } reason)
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.UnsupportedAsyncLifecycleHook,
					info.Location,
					new EquatableArray<string>([Display(info.OwningServiceOrImpl), hookName, reason,])));
				return (null, default, null);
			}

			return (QualifiedHook(origin, hookName, matches[0]), ClassifyHookParameters(matches[0], info, release, context), matches[0]);
		}

		// A generic hook whose only obstacle is an ambiguous closed marker, with no other usable overload, is
		// AWT198, distinct from an unusable name (AWT164) or an overload the container cannot pick between (AWT190):
		// several closed forms could bind the hook, so its type arguments cannot be chosen. When another overload
		// is also usable the collision is between methods, not closings (settling the closings would still leave
		// two usable overloads), so it falls to AWT190 below - exactly as if the generic overload bound uniquely.
		if (matches.Count == 0 && ambiguousMethods.Count == 1)
		{
			context.Diagnostics.Add(new DiagnosticInfo(
				Diagnostics.GenericHookAmbiguousMarker,
				info.Location,
				new EquatableArray<string>([
					Display(info.ImplementationType),
					hookName,
					string.Join(", ", ambiguousMethods[0].Select(form => Display(form.ToDisplayString(FullyQualified)))),
				])));
			return (null, default, null);
		}

		// No usable match is AWT164 (unusable hook); more than one is AWT190 (an overload the container cannot pick
		// between), where a generic overload with several bindable closings counts as usable - it could dispatch,
		// so silently preferring its sibling would let an extra marker closing change which method runs. Either way
		// no hook is emitted - the error fails the build, and picking one arbitrarily would only add a confusing
		// secondary diagnostic from the parameters of the guessed overload.
		context.Diagnostics.Add(new DiagnosticInfo(
			matches.Count + ambiguousMethods.Count == 0 ? Diagnostics.InvalidLifecycleHook : Diagnostics.AmbiguousLifecycleHook,
			info.Location,
			new EquatableArray<string>([Display(info.OwningServiceOrImpl), hookName, DescribeOwner(origin),])));
		return (null, default, null);
	}

	/// <summary>
	///     Collects the usable overloads of <paramref name="hookName" /> for <see cref="ResolveHook" />: the
	///     dispatchable methods (non-generic ones whose first parameter accepts the instance, and generic ones bound
	///     to a single closed marker form), and separately the closed-form sets of any generic overload that several
	///     forms could bind, which stay usable (an AWT190 collision when a sibling also matches) yet cannot be
	///     dispatched (AWT198 when they are the only candidate). The forms of each ambiguous overload are kept so the
	///     AWT198 message can name them. Open generic expansion uses it too, so it seeds from the hook that is dispatched.
	/// </summary>
	private static (List<IMethodSymbol> Matches, List<IReadOnlyList<INamedTypeSymbol>> AmbiguousMethods) CollectHookOverloads(
		INamedTypeSymbol implementation,
		string hookName,
		INamedTypeSymbol? origin,
		IReadOnlyList<INamedTypeSymbol> closedMarkers,
		INamedTypeSymbol containerSymbol,
		Compilation compilation)
	{
		List<IMethodSymbol> matches = new();
		List<IReadOnlyList<INamedTypeSymbol>> ambiguousMethods = new();
		foreach (ISymbol member in AccessibleMembers(origin ?? containerSymbol, hookName, compilation))
		{
			// A module hook must also be accessible from the generated container (its own private members are
			// reachable from the partial, a module's are not); an inaccessible module method is not a usable hook.
			if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: true, Parameters.Length: >= 1, } method
			    || !IsHookReturn(method, compilation)
			    || (origin is not null && !compilation.IsSymbolAccessibleWithin(method, containerSymbol)))
			{
				continue;
			}

			// A non-generic method needs no type arguments (whatever the marker closings): it is a usable hook
			// whenever its first parameter accepts the instance.
			if (method.Arity == 0)
			{
				if (compilation.HasImplicitConversion(implementation, method.Parameters[0].Type))
				{
					matches.Add(method);
				}

				continue;
			}

			// A generic hook binds its type parameters from a closed marker form, so WireView<TViewModel> is
			// dispatched as WireView<IMainViewModel>. Exactly one bindable form is that dispatch; more than one
			// leaves the type arguments ambiguous (AWT198, with the forms recorded for the message) yet still a
			// usable overload, so it counts toward AWT190 when a sibling also matches; none means the method is
			// not a usable hook and falls through to AWT164 with any other shape mismatch.
			List<(INamedTypeSymbol Form, IMethodSymbol Constructed)> bindable = BindableHookConstructions(method, closedMarkers, implementation, containerSymbol, compilation);
			if (bindable.Count == 1)
			{
				matches.Add(bindable[0].Constructed);
			}
			else if (bindable.Count > 1)
			{
				ambiguousMethods.Add(bindable.Select(candidate => candidate.Form).ToList());
			}
		}

		return (matches, ambiguousMethods);
	}

	/// <summary>
	///     Classifies a lifecycle hook's parameters after the leading instance parameter into the graph
	///     dependencies the container resolves and supplies to the hook, mirroring the constructor/factory pipeline
	///     in <see cref="ClassifyParameters" /> (contextual binding, registered-collection suppression, variance and
	///     the <c>[ImportServices]</c> fall-through), and reporting <see cref="Diagnostics.MissingDependency">AWT101</see>
	///     for an unregistered one. The same key-misuse diagnostics as a constructor parameter apply: an unsupported
	///     <c>[FromKey]</c> constant type (<see cref="Diagnostics.UnsupportedKeyType">AWT170</see>), and on a synthesized
	///     keyed collection an unsupported key type (<see cref="Diagnostics.UnsupportedKeyedCollectionKey">AWT159</see>)
	///     or a stray <c>[FromKey]</c> (<see cref="Diagnostics.FromKeyOnKeyedCollection">AWT160</see>). A parameter
	///     marked <c>[Arg]</c> is rejected with <see cref="Diagnostics.HookParameterIsArg">AWT189</see> and dropped:
	///     runtime arguments flow only through a <c>Func&lt;…&gt;</c> factory into <c>[Arg]</c> constructor parameters,
	///     and a hook has no such call site. On a <paramref name="release" /> hook a <c>Func</c>/<c>Lazy</c> parameter
	///     is rejected with <see cref="Diagnostics.ReleaseHookDeferredParameter">AWT191</see>: the capture holds only
	///     a resolver delegate, and the hook runs during the owner's teardown, when the resolvers refuse - the
	///     deferred value could never produce its target. It is kept (it resolves like any graph dependency), so the
	///     emitted capture and every downstream pass stay coherent; the error already fails the build.
	///     <c>[RequestingType]</c> is a factory-only feature (a hook is invoked by the
	///     container for an instance, not requested by a consumer), so like a constructor parameter it is not honored
	///     here and a <c>System.Type</c> so marked surfaces as an unregistered dependency (AWT101).
	/// </summary>
	private static EquatableArray<ParameterModel> ClassifyHookParameters(IMethodSymbol hook, ImplInfo info, bool release, BuildContext context)
		// An awaited activation hook runs on the async path, so like an async factory it receives the resolve-time token.
		// A release hook's dependencies are captured at construction on either path, awaited or not.
		=> ClassifyHookParameterSymbols(hook.Parameters.Skip(1), !release && IsAsyncHookReturn(hook.ReturnType, context.Compilation), release, info, context);

	/// <summary>
	///     Classifies the graph-resolved <paramref name="hookParameters" /> of a lifecycle hook or an async initializer
	///     (see <see cref="ClassifyHookParameters" />). An <paramref name="asynchronous" /> one runs on the async path
	///     and receives the resolve-time token.
	/// </summary>
	private static EquatableArray<ParameterModel> ClassifyHookParameterSymbols(IEnumerable<IParameterSymbol> hookParameters, bool asynchronous, bool release, ImplInfo info, BuildContext context)
	{
		List<ParameterModel> parameters = new();
		foreach (IParameterSymbol parameter in hookParameters)
		{
			ParameterModel parameterModel = ClassifyParameter(parameter, asynchronous, context.External.ServiceTypes);

			// AWT189: a hook parameter cannot be a runtime [Arg]. Dropped so it contributes no (unresolvable) graph
			// edge and no emitted argument; the error already fails the build.
			if (parameterModel.Kind == DependencyKind.Arg)
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.HookParameterIsArg,
					parameterModel.Location ?? info.Location,
					new EquatableArray<string>([parameter.Name, DisplayInstance(info.ImplementationType),])));
				continue;
			}

			// AWT191: a release hook's Func/Lazy parameter (any of the four deferred-delegate kinds, covering their
			// Task and Owned forms, and the dispatch dictionary of Funcs) captures a resolver delegate that is dead by the
			// time the hook runs.
			if (release && parameterModel.Kind is DependencyKind.Func or DependencyKind.Lazy or DependencyKind.FuncTask or DependencyKind.LazyTask or DependencyKind.TypeDispatch)
			{
				context.Diagnostics.Add(new DiagnosticInfo(
					Diagnostics.ReleaseHookDeferredParameter,
					parameterModel.Location ?? info.Location,
					new EquatableArray<string>([parameter.Name, DisplayInstance(info.ImplementationType),])));
			}

			// AWT170: a [FromKey] whose constant is of an unsupported key type, reported exactly as for a constructor
			// parameter (a dropped [Arg] above never reaches here, so it is not doubly reported).
			ReportUnsupportedFromKey(parameter.GetAttributes(), parameterModel.Location ?? info.Location, DisplayInstance(info.ImplementationType), context.Diagnostics);

			parameterModel = RedirectContextualBinding(parameterModel, info, context.ServiceToImpl, context.ConsumedConditionals);
			parameterModel = SuppressRegisteredCollectionSynthesis(parameterModel, parameter.Type, context.ServiceToImpl);
			parameterModel = ResolveTypeDispatch(parameterModel, parameter.Type, info, context.DispatchCandidates, context.Diagnostics);

			// AWT159/AWT160: keyed-collection misuse (an unsupported key type, or a [FromKey] on a synthesized keyed
			// collection), reported only for a dictionary that stayed synthesized past the suppression above.
			ReportUnsupportedKeyedCollectionKey(parameterModel, parameter.Type, info, context.ServiceToImpl, context.Diagnostics);
			ReportFromKeyOnKeyedCollection(parameterModel, parameter.Type, info, context.Diagnostics);

			parameterModel = RedirectVariance(parameterModel, parameter, context.ServiceToImpl, context.Variance);
			RecordRequestedCollectionElement(parameterModel, parameter, context.Variance);

			if (context.External.ImportServices
			    && parameterModel is { Kind: DependencyKind.Direct, Key: null, }
			    && !context.ServiceToImpl.ContainsKey(KeyOf(parameterModel)))
			{
				parameterModel = parameterModel with { Kind = DependencyKind.External, };
			}

			parameters.Add(parameterModel);
			ReportWhenUnregistered(parameterModel, info, context);
		}

		return new EquatableArray<ParameterModel>(parameters.ToArray());
	}

	/// <summary>
	///     Whether <paramref name="method" /> returns what a lifecycle hook may: <c>void</c>, or a <c>Task</c> or
	///     <c>ValueTask</c> with or without a result. A result is still unusable, which
	///     <see cref="UnsupportedAsyncHook" /> reports precisely instead of falling through to AWT164.
	/// </summary>
	private static bool IsHookReturn(IMethodSymbol method, Compilation compilation)
		=> method.ReturnsVoid
		   || IsAsyncHookReturn(method.ReturnType, compilation)
		   || IsAsyncFactoryReturn(method.ReturnType, compilation, out _);

	/// <summary>
	///     Whether a hook returning <paramref name="returnType" /> is awaited: a non-generic <c>Task</c> or
	///     <c>ValueTask</c>, matched by the canonical metadata symbols like <see cref="IsAsyncFactoryReturn" />.
	/// </summary>
	private static bool IsAsyncHookReturn(ITypeSymbol returnType, Compilation compilation)
		=> SymbolEqualityComparer.Default.Equals(returnType, compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"))
		   || (compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask") is { } valueTask
		       && SymbolEqualityComparer.Default.Equals(returnType, valueTask));

	/// <summary>
	///     Why the container cannot run <paramref name="hook" /> (<see cref="Diagnostics.UnsupportedAsyncLifecycleHook">AWT232</see>),
	///     or <see langword="null" /> when it can: a hook's result has nowhere to go, and an asynchronous release hook
	///     has no token to receive, because <c>DisposeAsync</c> takes none.
	/// </summary>
	private static string? UnsupportedAsyncHook(IMethodSymbol hook, bool release, Compilation compilation)
	{
		if (IsAsyncFactoryReturn(hook.ReturnType, compilation, out _))
		{
			return $"it returns '{Display(hook.ReturnType.ToDisplayString(FullyQualified))}', but the container has no use for a hook's result; return Task or ValueTask instead";
		}

		return release
		       && IsAsyncHookReturn(hook.ReturnType, compilation)
		       && hook.Parameters.Any(parameter => parameter.Type.ToDisplayString(FullyQualified) == "global::System.Threading.CancellationToken")
			? "an asynchronous OnRelease hook cannot take a CancellationToken, because DisposeAsync has none to pass; remove the parameter"
			: null;
	}

	/// <summary>
	///     A module's lifecycle hook is emitted qualified with the module type (the generated container is another
	///     class, so the simple name would not bind); the container's own hooks stay unqualified, in scope inside
	///     the generated partial. <paramref name="origin" /> is the hook slot's own origin (see <c>ResolveHook</c>).
	///     Mirrors <c>QualifiedProductionMember</c> for Factory/Instance members. A generic
	///     hook carries its bound type arguments (<c>WireView&lt;global::App.IMainViewModel&gt;</c>) so the emitter,
	///     which writes the name verbatim, needs no generic awareness.
	/// </summary>
	private static string QualifiedHook(INamedTypeSymbol? origin, string hookName, IMethodSymbol hook)
	{
		string name = origin is { } owner ? $"{owner.ToDisplayString(FullyQualified)}.{hookName}" : hookName;
		if (hook.TypeArguments.Length == 0)
		{
			return name;
		}

		string arguments = string.Join(", ", hook.TypeArguments.Select(argument => argument.ToDisplayString(FullyQualified)));
		return $"{name}<{arguments}>";
	}

	/// <summary>
	///     The closed marker forms a generic hook method can actually bind, each paired with the method constructed
	///     from its type arguments. A form is bindable when its arity matches the method's, the form (including its
	///     type arguments, which become the constructed call's) is accessible from the generated container (an
	///     internal type argument from a scanned foreign assembly would otherwise leak CS0122 into the generated
	///     source), its type arguments satisfy the method's constraints (see <see cref="SatisfiesConstraints" />, so
	///     the constructed call compiles rather than erroring inside the generated source), and the constructed
	///     method's first parameter accepts the implementation. Two forms constructing the identical method (say
	///     <c>IView&lt;int&gt;</c> and <c>IEditor&lt;int&gt;</c> both yielding <c>Wire&lt;int&gt;</c>) count once:
	///     the dispatch is the same, so there is nothing to be ambiguous about. An empty result means the method is
	///     not a usable hook (AWT164); more than one entry is the ambiguity AWT198 reports. Like a constraint, the
	///     accessibility check can settle a family: an inaccessible closing beside an accessible one leaves a single
	///     bindable form rather than an ambiguity.
	/// </summary>
	private static List<(INamedTypeSymbol Form, IMethodSymbol Constructed)> BindableHookConstructions(
		IMethodSymbol method,
		IReadOnlyList<INamedTypeSymbol> closedMarkers,
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		Compilation compilation)
	{
		List<(INamedTypeSymbol Form, IMethodSymbol Constructed)> bindable = new();
		foreach (INamedTypeSymbol form in closedMarkers.Where(form =>
			         method.Arity == form.TypeArguments.Length
			         && compilation.IsSymbolAccessibleWithin(form, containerSymbol)
			         && SatisfiesConstraints(method, form.TypeArguments, compilation)))
		{
			IMethodSymbol constructed = method.Construct(form.TypeArguments.ToArray());
			if (compilation.HasImplicitConversion(implementation, constructed.Parameters[0].Type)
			    && !bindable.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.Constructed, constructed)))
			{
				bindable.Add((form, constructed));
			}
		}

		return bindable;
	}

	/// <summary>
	///     Whether the type arguments satisfy a generic hook method's constraints, checked before
	///     <see cref="IMethodSymbol.Construct(ITypeSymbol[])" /> (which does not validate them, so a violation would
	///     otherwise surface as a raw compiler error inside the generated source). Covers the constraint kinds C#
	///     has: <c>class</c>, <c>struct</c> (excluding <c>Nullable&lt;T&gt;</c>), <c>unmanaged</c>, <c>new()</c>,
	///     and constraint types, with the method's own type parameters substituted so <c>where T : IComparable&lt;T&gt;</c>
	///     is checked at the bound argument. A constraint type is satisfied only by an identity, implicit reference,
	///     or boxing conversion (see <see cref="SatisfiesConstraintType" />), the conversions the language admits for
	///     constraint satisfaction - notably <em>not</em> a user-defined implicit operator, which
	///     <c>HasImplicitConversion</c> would accept and which would leak CS0311 into the generated source.
	///     <c>notnull</c> is not checked: violating it is only a compiler warning, so the constructed call still
	///     compiles.
	/// </summary>
	private static bool SatisfiesConstraints(IMethodSymbol method, ImmutableArray<ITypeSymbol> arguments, Compilation compilation)
	{
		for (int index = 0; index < method.TypeParameters.Length; index++)
		{
			ITypeParameterSymbol parameter = method.TypeParameters[index];
			ITypeSymbol argument = arguments[index];

			if ((parameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
			    || (parameter.HasValueTypeConstraint && (!argument.IsValueType || argument.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
			    || (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
			    || (parameter.HasConstructorConstraint && !SatisfiesConstructorConstraint(argument)))
			{
				return false;
			}

			if (parameter.ConstraintTypes.Any(constraint =>
				    !SatisfiesConstraintType(argument, SubstituteTypeParameters(constraint, method, arguments, compilation), compilation)))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	///     Whether a type argument satisfies one constraint type under the language's constraint-satisfaction rules:
	///     an identity, implicit reference, or boxing conversion. A user-defined implicit operator does not count
	///     (the compiler rejects such a closing with CS0311), which is why this is not
	///     <c>Compilation.HasImplicitConversion</c>.
	/// </summary>
	private static bool SatisfiesConstraintType(ITypeSymbol argument, ITypeSymbol constraint, Compilation compilation)
	{
		Conversion conversion = ((CSharpCompilation)compilation).ClassifyConversion(argument, constraint);
		return conversion.IsIdentity || (conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing));
	}

	/// <summary>
	///     Whether a type argument satisfies <c>new()</c>: any value type, or a non-abstract type with a public
	///     parameterless instance constructor.
	/// </summary>
	private static bool SatisfiesConstructorConstraint(ITypeSymbol argument)
		=> argument.IsValueType
		   || (argument is INamedTypeSymbol { IsAbstract: false, } named
		       && named.InstanceConstructors.Any(constructor => constructor is { Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public, }));

	/// <summary>
	///     Substitutes a generic hook method's own type parameters with the bound arguments inside a constraint type,
	///     so <c>where T : IComparable&lt;T&gt;</c> becomes <c>IComparable&lt;int&gt;</c> for the conversion check in
	///     <see cref="SatisfiesConstraints" />. Descends into generic type arguments, array element types
	///     (<c>where T : IEnumerable&lt;T[]&gt;</c>), and the containing types of a nested constraint type
	///     (<c>where T : Outer&lt;T&gt;.IInner</c>, whose type parameter lives on the container rather than the
	///     nested type itself); anything else that is not the method's type parameter passes through unchanged.
	/// </summary>
	private static ITypeSymbol SubstituteTypeParameters(ITypeSymbol type, IMethodSymbol method, ImmutableArray<ITypeSymbol> arguments, Compilation compilation)
	{
		if (type is ITypeParameterSymbol parameter && SymbolEqualityComparer.Default.Equals(parameter.DeclaringMethod, method))
		{
			return arguments[parameter.Ordinal];
		}

		if (type is INamedTypeSymbol named)
		{
			return SubstituteNamedType(named, method, arguments, compilation);
		}

		if (type is IArrayTypeSymbol array)
		{
			return compilation.CreateArrayTypeSymbol(SubstituteTypeParameters(array.ElementType, method, arguments, compilation), array.Rank);
		}

		return type;
	}

	/// <summary>
	///     Substitutes through a named constraint type (see <see cref="SubstituteTypeParameters" />): the containing
	///     chain first, so a nested type's outer arguments close too (<c>Outer&lt;T&gt;.IInner</c> becomes
	///     <c>Outer&lt;int&gt;.IInner</c> by re-finding <c>IInner</c> on the substituted container), then the type's
	///     own arguments. A container lookup miss fails safe by returning the type unsubstituted - the conversion
	///     check in <see cref="SatisfiesConstraints" /> then simply rejects the closing (AWT164) instead of
	///     constructing a call that would not compile.
	/// </summary>
	private static INamedTypeSymbol SubstituteNamedType(INamedTypeSymbol named, IMethodSymbol method, ImmutableArray<ITypeSymbol> arguments, Compilation compilation)
	{
		INamedTypeSymbol generic;
		if (named.ContainingType is { } outer)
		{
			INamedTypeSymbol? member = SubstituteNamedType(outer, method, arguments, compilation)
				.GetTypeMembers(named.Name, named.Arity)
				.FirstOrDefault();
			if (member is null)
			{
				return named;
			}

			generic = member;
		}
		else
		{
			generic = named.ConstructedFrom;
		}

		return named.Arity == 0
			? generic
			: generic.Construct(named.TypeArguments
				.Select(argument => SubstituteTypeParameters(argument, method, arguments, compilation))
				.ToArray());
	}

	/// <summary>
	///     Reports <see cref="Diagnostics.FactoryHidesAsyncInitialization">AWT106</see> when a synchronous
	///     factory method's body provably returns a concrete type that a convention of the declaring scope
	///     (<paramref name="conventions" />) initializes while the declared return type is not of that convention's
	///     type, so the initialization is invisible to the container and never runs.
	/// </summary>
	/// <remarks>
	///     Conservative by design: it inspects only the producer's own <c>return</c> expressions, never descending
	///     into nested lambdas or local functions, and fires only when the returned static type is a non-abstract,
	///     non-interface type a convention matches. An unanalyzable return type yields no diagnostic. False negatives
	///     are accepted; false positives are not.
	/// </remarks>
	private static void ReportFactoryHidingAsyncInitialization(
		IMethodSymbol producer,
		Compilation compilation,
		List<AsyncInitializerConvention> conventions,
		List<DiagnosticInfo> diagnostics)
	{
		ITypeSymbol declaredReturnType = producer.ReturnType;

		// A convention the declared return type is already of runs anyway, so only the others can be hidden.
		List<AsyncInitializerConvention> hideable = conventions.FindAll(convention => !IsOfConventionType(declaredReturnType, convention.Type));
		if (hideable.Count == 0)
		{
			return;
		}

		// Already-reported concrete types: a factory with several returns of the same hidden type should
		// surface a single diagnostic, not one per return.
		HashSet<ITypeSymbol> reported = new(SymbolEqualityComparer.Default);

		foreach (SyntaxReference reference in producer.DeclaringSyntaxReferences)
		{
			// A factory must be a method on the container; anything else (or metadata-only, no syntax) is
			// not analyzable and is left silent.
			if (reference.GetSyntax() is not MethodDeclarationSyntax method)
			{
				continue;
			}

			SemanticModel model = compilation.GetSemanticModel(method.SyntaxTree);

			foreach (ExpressionSyntax returnExpression in CollectReturnExpressions(method))
			{
				ITypeSymbol? returnedType = model.GetTypeInfo(returnExpression).Type;
				if (returnedType is not INamedTypeSymbol concrete
				    || concrete.TypeKind == TypeKind.Interface
				    || concrete.IsAbstract
				    || concrete.TypeKind == TypeKind.Error
				    || hideable.Find(convention => IsOfConventionType(concrete, convention.Type)) is not { } hidden
				    || !reported.Add(concrete))
				{
					continue;
				}

				diagnostics.Add(new DiagnosticInfo(
					Diagnostics.FactoryHidesAsyncInitialization,
					LocationInfo.From(returnExpression.GetLocation()),
					new EquatableArray<string>([
						producer.Name,
						Display(concrete.ToDisplayString(FullyQualified)),
						Display(declaredReturnType.ToDisplayString(FullyQualified)),
						hidden.Display,
					])));
			}
		}
	}

	/// <summary>
	///     The expressions a method directly returns: the arrow expression of an expression-bodied method, or
	///     every <c>return x;</c> in a block body. Nested lambdas and local functions are not descended into,
	///     so their returns are not attributed to the enclosing factory.
	/// </summary>
	private static IEnumerable<ExpressionSyntax> CollectReturnExpressions(MethodDeclarationSyntax method)
	{
		if (method.ExpressionBody?.Expression is { } arrow)
		{
			yield return arrow;
			yield break;
		}

		if (method.Body is null)
		{
			yield break;
		}

		Stack<SyntaxNode> pending = new();
		pending.Push(method.Body);
		while (pending.Count > 0)
		{
			SyntaxNode node = pending.Pop();
			foreach (SyntaxNode child in node.ChildNodes())
			{
				// Do not cross into a nested function: its returns belong to it, not to the factory.
				if (child is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
				{
					continue;
				}

				if (child is ReturnStatementSyntax { Expression: { } returned })
				{
					yield return returned;
				}

				pending.Push(child);
			}
		}
	}

	/// <summary>
	///     Resolves a <c>Factory</c> registration to the method that produces it: a container method, or a
	///     module method for an imported registration (never falling back to the container). No method of that
	///     name returns the registered type → <see cref="Diagnostics.InvalidFactory">AWT108</see> naming the
	///     owner; a module method that matches but is inaccessible from the generated container →
	///     <see cref="Diagnostics.InaccessibleModuleMember">AWT153</see>; more than one accessible match (an
	///     overload) → <see cref="Diagnostics.AmbiguousFactory">AWT112</see>.
	/// </summary>
	private static IMethodSymbol? ResolveFactory(
		INamedTypeSymbol containerSymbol,
		ImplInfo info,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		INamedTypeSymbol owner = info.Origin ?? containerSymbol;
		List<IMethodSymbol> candidates = info.GenericFactory
			? FindGenericFactoryCandidates(owner, info.ProductionMember!, info.Symbol, compilation)
			: FindFactoryCandidates(owner, info.ProductionMember!, info.Symbol, compilation);

		// The generated partial reaches its container's own members at any accessibility, but a module's
		// members are called from outside, so only those the container can see qualify. A match hidden from
		// the container is its own error (AWT153), not a confusing not-found AWT108.
		if (info.Origin is not null && candidates.Count > 0)
		{
			List<IMethodSymbol> accessible = candidates
				.Where(candidate => compilation.IsSymbolAccessibleWithin(candidate, containerSymbol))
				.ToList();
			if (accessible.Count == 0 && SameCompilationMemberWrapper(info, compilation) is null)
			{
				ReportInaccessibleModuleMember(info, diagnostics);
				return null;
			}

			candidates = accessible.Count == 0 ? candidates : accessible;
		}

		if (candidates.Count == 1)
		{
			return candidates[0];
		}

		diagnostics.Add(new DiagnosticInfo(
			candidates.Count == 0 ? Diagnostics.InvalidFactory : Diagnostics.AmbiguousFactory,
			info.Location,
			new EquatableArray<string>([Display(info.OwningServiceOrImpl), info.ProductionMember!, DescribeOwner(info),])));
		return null;
	}

	/// <summary>
	///     Validates an <c>Instance</c> registration against the named member of its owner (the container, or
	///     the declaring module for an imported registration, never falling back to the container). Reports
	///     <see cref="Diagnostics.InvalidInstance">AWT109</see> when no field or property of that name (on the
	///     owner or an accessible base type) holds the registered type, and
	///     <see cref="Diagnostics.InaccessibleModuleMember">AWT153</see> when a module member matches but is
	///     inaccessible from the generated container.
	/// </summary>
	private static void ValidateInstanceMember(
		INamedTypeSymbol containerSymbol,
		ImplInfo info,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		bool inaccessibleMatch = false;
		foreach (ISymbol member in AccessibleMembers(info.Origin ?? containerSymbol, info.ProductionMember!, compilation))
		{
			ITypeSymbol? memberType = member switch
			{
				IFieldSymbol field => field.Type,
				IPropertySymbol property => property.Type,
				_ => null,
			};
			if (memberType is not null && compilation.HasImplicitConversion(memberType, info.Symbol))
			{
				if (info.Origin is null || compilation.IsSymbolAccessibleWithin(member, containerSymbol) || SameCompilationMemberWrapper(info, compilation) is not null)
				{
					return;
				}

				inaccessibleMatch = true;
			}
		}

		if (inaccessibleMatch)
		{
			ReportInaccessibleModuleMember(info, diagnostics);
			return;
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.InvalidInstance,
			info.Location,
			new EquatableArray<string>([Display(info.OwningServiceOrImpl), info.ProductionMember!, DescribeOwner(info),])));
	}

	/// <summary>
	///     AWT153: the module declares a member matching the Factory/Instance registration, but the generated
	///     container cannot access it (e.g. a private member of a source module). A cross-assembly internal member
	///     without InternalsVisibleTo is not imported into the symbol tables at all and surfaces as AWT108/AWT109.
	/// </summary>
	private static void ReportInaccessibleModuleMember(ImplInfo info, List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.InaccessibleModuleMember,
			info.Location,
			new EquatableArray<string>([
				Display(info.OwningServiceOrImpl),
				Display(info.Origin!.ToDisplayString(FullyQualified)),
				info.ProductionMember!,
			])));

	/// <summary>
	///     Names the owner of a Factory/Instance member in a diagnostic: the module that declared the
	///     registration, or the container for its own registrations.
	/// </summary>
	private static string DescribeOwner(ImplInfo info) => DescribeOwner(info.Origin);

	/// <summary>
	///     Names an owner in a diagnostic: the module, or the container when <paramref name="origin" /> is null or an
	///     extended base container, so a problem of an inherited registration reads exactly like the base's own report
	///     of it and is reported only once (see <c>RemoveBaseDiagnostics</c>).
	/// </summary>
	private static string DescribeOwner(INamedTypeSymbol? origin)
		=> origin is { } owner && !HasAwaitenAttribute(owner.GetAttributes(), "ContainerAttribute")
			? $"the module '{Display(owner.ToDisplayString(FullyQualified))}'"
			: "the container";

	/// <summary>
	///     A module's Factory/Instance member is emitted qualified with the module type (the generated container is
	///     another class, so the simple name would not bind); the container's own members stay unqualified, being
	///     in scope inside the generated partial. A private member of a module in the same assembly is called through the
	///     internal wrapper the module generates for it (see <see cref="SameCompilationMemberWrapper" />).
	/// </summary>
	private static string? QualifiedProductionMember(ImplInfo info, Compilation compilation)
		=> info.ProductionMember is null || info.Origin is null
			? info.ProductionMember
			: $"{info.Origin.ToDisplayString(FullyQualified)}.{SameCompilationMemberWrapper(info, compilation) ?? info.ProductionMember}";

	/// <summary>
	///     The internal wrapper that the module of <paramref name="info" />, in the container's own assembly, generates in
	///     the same run for the private <c>Factory</c> or <c>Instance</c> member of a registration under an internal service
	///     (see <see cref="CollectModuleMemberWrappers" />), relative to the module, or <see langword="null" /> when the
	///     container reaches the member itself or the module generates no such wrapper.
	/// </summary>
	private static string? SameCompilationMemberWrapper(ImplInfo info, Compilation compilation)
	{
		if (info is not { Origin: { } module, ProductionMember: { } member, Production: ProductionKind.Factory or ProductionKind.Instance, }
		    || !SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly)
		    || !HasAwaitenAttribute(module.GetAttributes(), "ModuleAttribute")
		    || AccessibleMembers(module, member, compilation).Any(candidate => compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly))
		    || !ReceivesGeneratedMembers(module))
		{
			return null;
		}

		string name = MemberWrapperName(info.Production == ProductionKind.Factory ? "Factory" : "Instance", member, info.Symbol);
		return CollectModuleMemberWrappers(module, compilation, new List<DiagnosticInfo>(), new List<SkippedRegistration>())
			.Any(wrapper => wrapper.IsInternal && wrapper.Name == name)
			? InInternals(name)
			: null;
	}

	/// <summary>
	///     The <see cref="QualifiedProductionMember" /> the generated code calls, carrying the bound type arguments of a
	///     generic factory (<c>Create&lt;global::App.Order&gt;</c>) so the emitter, which writes the name verbatim, needs
	///     no generic awareness.
	/// </summary>
	private static string? ProducerCall(ImplInfo info, IMethodSymbol producer, Compilation compilation)
	{
		string? member = QualifiedProductionMember(info, compilation);
		if (info.Production != ProductionKind.Factory || !producer.IsGenericMethod)
		{
			return member;
		}

		string arguments = string.Join(", ", producer.TypeArguments.Select(argument => argument.ToDisplayString(FullyQualified)));
		return $"{member}<{arguments}>";
	}

	/// <summary>
	///     Chooses the constructor the container builds <paramref name="implementation" /> through: its single
	///     accessible constructor, or the greediest whose parameters are all satisfiable (falling back to the
	///     greediest so unresolved parameters surface as AWT101). <paramref name="external" /> marks external
	///     ([ImportService&lt;T&gt;]/[ImportServices]) parameters as satisfiable. <paramref name="additionallySatisfiable" />,
	///     when supplied, marks parameters the caller can satisfy beyond the registered set. Open generic
	///     expansion passes it so a parameter whose closed generic is expanded on demand does not disqualify a
	///     constructor, letting the seed scan the same constructor the emitted container resolves.
	/// </summary>
	internal static IMethodSymbol? SelectConstructor(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		IEnumerable<string> registeredServices,
		ExternalSurface external,
		Func<IParameterSymbol, bool>? additionallySatisfiable = null)
	{
		// The generated partial emits the 'new' from inside the container, so what the container can reach is
		// exactly Roslyn's own accessibility question, which honors [InternalsVisibleTo]. A bare same-assembly
		// comparison would reject a referenced assembly's internal constructor the generated code could in fact
		// call. The module member checks ask it the same way (see ResolveFactory).
		List<IMethodSymbol> constructors = implementation.InstanceConstructors
			.Where(c => compilation.IsSymbolAccessibleWithin(c, containerSymbol))
			.ToList();
		if (constructors.Count <= 1)
		{
			return constructors.FirstOrDefault();
		}

		HashSet<string> registered = new(registeredServices, StringComparer.Ordinal);
		IMethodSymbol? resolvable = constructors
			.Where(c => c.Parameters.All(p =>
			{
				// Selecting a constructor, never an async factory, so no CancellationToken forwarding applies.
				// A collection (Enumerable, AsyncEnumerable or AwaitedEnumerable) is always satisfiable: an
				// unregistered element type just yields an empty collection. An external ([ImportService<T>])
				// parameter is always satisfiable too, and with [ImportServices] any direct dependency can fall
				// through to the external provider, so neither disqualifies a constructor.
				ParameterModel parameter = ClassifyParameter(p, asyncFactory: false, external.ServiceTypes);
				return parameter.Kind is DependencyKind.Arg or DependencyKind.External
				       || IsSynthesizedCollection(parameter.Kind)
				       || (external.ImportServices && parameter.Kind == DependencyKind.Direct)
				       || registered.Contains(parameter.ServiceType)
				       || (additionallySatisfiable?.Invoke(p) ?? false);
			}))
			.OrderByDescending(c => c.Parameters.Length)
			.FirstOrDefault();

		// Fall back to the greediest constructor so its unresolved parameters surface as AWT101.
		return resolvable ?? constructors.OrderByDescending(c => c.Parameters.Length).First();
	}

	/// <summary>
	///     The members named <paramref name="name" /> the generated container partial can reach: the
	///     container's own members (any accessibility, since a partial can use its own private members) plus
	///     inherited members the container can access, which is Roslyn's own check: a base type's protected member
	///     qualifies (the container derives from it) and an internal one does when the declaring assembly is the
	///     container's or grants it [InternalsVisibleTo].
	/// </summary>
	private static IEnumerable<ISymbol> AccessibleMembers(INamedTypeSymbol container, string name, Compilation compilation)
	{
		foreach (ISymbol member in MembersNamed(container, name))
		{
			yield return member;
		}

		for (INamedTypeSymbol? baseType = container.BaseType; baseType is not null; baseType = baseType.BaseType)
		{
			foreach (ISymbol member in baseType.GetMembers(name).Where(m => compilation.IsSymbolAccessibleWithin(m, container)))
			{
				yield return member;
			}
		}
	}

	/// <summary>
	///     The candidate factory methods for a <c>Factory</c> registration: the ordinary methods named
	///     <paramref name="name" /> on the container (or an accessible base type) whose return type produces
	///     <paramref name="serviceType" />. A sync factory's return type is implicitly convertible to the
	///     service type; an async factory returns <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c> and is
	///     matched against the unwrapped <c>T</c>. None means AWT108; more than one means an ambiguous
	///     factory (AWT112).
	/// </summary>
	private static List<IMethodSymbol> FindFactoryCandidates(
		INamedTypeSymbol container, string name, ITypeSymbol serviceType, Compilation compilation)
	{
		List<IMethodSymbol> candidates = new();
		foreach (ISymbol member in AccessibleMembers(container, name, compilation))
		{
			if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, } method
			    && compilation.HasImplicitConversion(ProducedType(method.ReturnType, compilation), serviceType))
			{
				candidates.Add(method);
			}
		}

		return candidates;
	}

	/// <summary>
	///     The candidate factory methods for a closing of an open generic <c>typeof</c> registration: the generic
	///     methods named <paramref name="name" /> with the closing's arity, constructed with its type arguments in
	///     declaration order when they satisfy the method's constraints, whose constructed return type produces
	///     <paramref name="closedImplementation" />. Each candidate is returned constructed, so its parameters and
	///     return type are the closing's. None means AWT108; more than one an ambiguous factory (AWT112).
	/// </summary>
	private static List<IMethodSymbol> FindGenericFactoryCandidates(
		INamedTypeSymbol container, string name, INamedTypeSymbol closedImplementation, Compilation compilation)
	{
		ImmutableArray<ITypeSymbol> arguments = closedImplementation.TypeArguments;
		List<IMethodSymbol> candidates = new();
		foreach (ISymbol member in AccessibleMembers(container, name, compilation))
		{
			if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, } method
			    && method.Arity == arguments.Length
			    && SatisfiesConstraints(method, arguments, compilation))
			{
				IMethodSymbol constructed = method.Construct(arguments.ToArray());
				if (compilation.HasImplicitConversion(ProducedType(constructed.ReturnType, compilation), closedImplementation))
				{
					candidates.Add(constructed);
				}
			}
		}

		return candidates;
	}

	/// <summary>
	///     The service type a factory's return type produces: the awaited result <c>T</c> for an async factory
	///     returning <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>, otherwise the return type itself. A
	///     non-generic <c>Task</c> / <c>ValueTask</c> is not unwrapped, so it is matched as-is and falls out as
	///     AWT108 (it produces no service).
	/// </summary>
	private static ITypeSymbol ProducedType(ITypeSymbol returnType, Compilation compilation)
		=> IsAsyncFactoryReturn(returnType, compilation, out ITypeSymbol produced) ? produced : returnType;

	/// <summary>
	///     Whether <paramref name="returnType" /> is an awaitable factory return (<c>Task&lt;T&gt;</c> or
	///     <c>ValueTask&lt;T&gt;</c>), yielding the produced result type <c>T</c>. Matched by the canonical
	///     metadata symbols so a user-defined <c>Task`1</c> in another namespace is not mistaken for one.
	///     <c>ValueTask&lt;T&gt;</c> is absent on netstandard2.0; <see cref="Compilation.GetTypeByMetadataName" />
	///     returns <see langword="null" /> there and that branch is skipped.
	/// </summary>
	private static bool IsAsyncFactoryReturn(ITypeSymbol returnType, Compilation compilation, out ITypeSymbol produced)
	{
		if (returnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1, } named)
		{
			INamedTypeSymbol definition = named.ConstructedFrom;
			INamedTypeSymbol? task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
			INamedTypeSymbol? valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
			if (SymbolEqualityComparer.Default.Equals(definition, task)
			    || (valueTask is not null && SymbolEqualityComparer.Default.Equals(definition, valueTask)))
			{
				produced = named.TypeArguments[0];
				return true;
			}
		}

		produced = returnType;
		return false;
	}
}
