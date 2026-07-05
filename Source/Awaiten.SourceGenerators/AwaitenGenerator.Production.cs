using Awaiten.SourceGenerators.Entities;
using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

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

		// A pre-built Instance is handed back from a container member, never constructed here. The
		// container does not own it, so it is not disposed; the registered type may legitimately be an
		// interface (so the not-instantiable check is skipped) and it contributes no graph edges.
		//
		// It is likewise never async-initialized: a pre-built Instance implementing IAsyncInitializable is
		// NOT awaited by the container and is NOT async-tainted, so it stays synchronously resolvable and is
		// handed out without InitializeAsync ever running. This mirrors the disposal contract above - a
		// pre-built instance is the caller's to construct, initialize and own; the container only hands back
		// what the member produced. A service that needs the container to drive its asynchronous
		// initialization must be registered for construction (or via a Factory whose concrete return type
		// implements IAsyncInitializable), not as a pre-built Instance.
		if (info.Production == ProductionKind.Instance)
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

			return new InstanceModel(
				info.ImplementationType,
				info.Symbol.Name,
				info.Lifetime,
				new EquatableArray<ServiceKey>(info.Services.ToArray()),
				new EquatableArray<ParameterModel>([]),
				false,
				info.Symbol.IsReferenceType,
				ProductionKind.Instance,
				QualifiedProductionMember(info));
		}

		// Select the producer: a container method (Factory) or the implementation's constructor (the
		// default). A null result means the registration is unusable and a diagnostic was already reported.
		IMethodSymbol? producer = SelectProducer(info, containerSymbol, compilation, serviceToImpl, context.ImportServices, diagnostics);
		if (producer is null)
		{
			return null;
		}

		// An asynchronous factory returns Task<T> / ValueTask<T>: the container awaits it, so the type it
		// actually owns is the awaited result T, not the Task. A synchronous factory owns its return type
		// directly, and a constructed implementation owns info.Symbol.
		bool asyncFactory = info.Production == ProductionKind.Factory
		                    && IsAsyncFactoryReturn(producer.ReturnType, compilation, out _);

		// A factory's parameters resolve from the graph exactly like a constructor's. An async factory
		// additionally forwards the resolve-time CancellationToken (the async creator's) into a matching
		// parameter rather than resolving it from the graph.
		List<ParameterModel> parameters = ClassifyParameters(producer, info, asyncFactory, context);

		// Property injection: after the constructor, fill opt-in [Inject] properties through an object
		// initializer. Only a constructed instance is filled - a factory or pre-built instance is produced
		// whole by its source. Each member edge is classified exactly like a Direct constructor parameter, so
		// it participates fully in cycle, captive and async-taint analysis.
		List<MemberModel> members = new();
		if (info.Production == ProductionKind.Constructor)
		{
			DiscoverInjectedMembers(info, containerSymbol, serviceToImpl, context.ConstraintRejected, members, diagnostics);
		}

		// Disposability follows the type the container actually owns: a factory's produced type (which may
		// implement IDisposable behind a non-disposable service interface; for an async factory this is the
		// awaited T, not the Task), or the constructed implementation type. Using info.Symbol for a factory
		// would miss a DisposableX behind an IX and leak it.
		ITypeSymbol disposalType = info.Production == ProductionKind.Factory
			? ProducedType(producer.ReturnType, compilation)
			: info.Symbol;
		bool disposable = wellKnown.Disposable is not null && ImplementsInterface(disposalType, wellKnown.Disposable);

		// Async disposal mirrors synchronous disposal: the container owns an IAsyncDisposable instance for
		// teardown too, and the drain awaits its DisposeAsync, preferring it over IDisposable when a type is
		// both. This is recognized only when the runtime exposes async disposal at all - otherwise the generated
		// container stays synchronous-dispose only.
		bool asyncDisposable = wellKnown.AsyncDisposable is not null && ImplementsInterface(disposalType, wellKnown.AsyncDisposable);

		// A factory's declared return type can hide a concrete IDisposable (or IAsyncDisposable) behind a
		// non-disposable service interface (or base class), which the static flags above miss. When that is
		// possible - the declared type is itself neither yet a subtype could be (an interface or a non-sealed
		// class) - the emitter tracks the realized instance for disposal behind a runtime
		// `is IDisposable or IAsyncDisposable` test instead. A sealed declared type that is neither cannot hide
		// one, so it needs no check (and the runtime test would not even compile). Constructed and pre-built
		// Instance production never lie: info.Symbol is the concrete type, and an Instance is not owned.
		bool runtimeDisposalCheck = info.Production == ProductionKind.Factory
		                            && !disposable
		                            && !asyncDisposable
		                            && CouldHideDisposable(disposalType);

		// Async initialization follows the type the container actually owns - a factory's concrete return type
		// (which may implement IAsyncInitializable behind a non-async service interface) or the constructed
		// implementation type - mirroring the disposal-type choice above. A pre-built Instance is returned
		// early above and is never initialized here (the caller owns it). An async factory is async-tainted
		// regardless of whether its produced type implements IAsyncInitializable: its result is reached only by
		// awaiting the Task (see the IsAsyncFactory seed in PropagateAsyncTaint). When the produced type IS
		// IAsyncInitializable, the container additionally awaits its InitializeAsync after the factory completes.
		bool asyncInit = wellKnown.AsyncInitializable is not null && ImplementsInterface(disposalType, wellKnown.AsyncInitializable);

		// Best-effort lint (AWT106): a synchronous factory whose declared return type hides the asynchronous
		// initialization its body provably produces. The container reads async-init taint off producer.ReturnType
		// (above), so a concrete IAsyncInitializable returned behind a plainer interface is never initialized.
		// An async Task<T>/ValueTask<T> factory owns its own initialization (the container awaits the factory),
		// and a hidden IDisposable is disposed at runtime via RuntimeDisposalCheck - neither is reported.
		if (info.Production == ProductionKind.Factory && !asyncFactory)
		{
			ReportFactoryHidingAsyncInitialization(producer, compilation, wellKnown.AsyncInitializable, diagnostics);
		}

		// A decorator chain link carries a synthetic ImplementationType identity (so one decorator type can be
		// several distinct instances); the real type to construct is then its symbol, kept apart in EmitType.
		string realType = info.Symbol.ToDisplayString(FullyQualified);
		string? emitType = info.ImplementationType == realType ? null : realType;

		// Lifecycle hooks (AWT164 when a named member is not a usable static void M(TImplementation)). Applied to
		// constructed and factory-produced instances - the ones the container owns; a pre-built Instance returns
		// above (the caller owns it, so activation/release do not apply).
		string? onActivated = ResolveHook(containerSymbol, info, info.OnActivated, compilation, diagnostics);
		string? onRelease = ResolveHook(containerSymbol, info, info.OnRelease, compilation, diagnostics);

		return new InstanceModel(
			info.ImplementationType,
			info.Symbol.Name,
			info.Lifetime,
			new EquatableArray<ServiceKey>(info.Services.ToArray()),
			new EquatableArray<ParameterModel>(parameters.ToArray()),
			disposable,
			info.Symbol.IsReferenceType,
			info.Production,
			QualifiedProductionMember(info),
			asyncInit,
			IsAsyncFactory: asyncFactory,
			RuntimeDisposalCheck: runtimeDisposalCheck,
			IsAsyncDisposable: asyncDisposable,
			EmitType: emitType,
			InjectedMembers: new EquatableArray<MemberModel>(members.ToArray()),
			// Eager build-time construction is the synchronous analog of InitializeAsync, which warms singletons,
			// so it applies to a singleton only. The attribute exposes Eager on [Singleton<…>] alone, but the flag
			// coalesces onto the implementation, so this guard keeps a coalesced non-singleton from carrying it.
			Eager: info.Eager && info.Lifetime == Lifetime.Singleton,
			OnActivated: onActivated,
			OnRelease: onRelease);

		static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol @interface)
		{
			return SymbolEqualityComparer.Default.Equals(type, @interface)
			       || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
		}

		// Whether a value of this declared type could be IDisposable at runtime through a subtype the
		// declaration does not reveal: an interface or type parameter (any implementer qualifies) or a
		// non-sealed class (a derived type may implement it). A sealed class or a struct that does not
		// itself implement IDisposable cannot, so a runtime `is IDisposable` test against it is pointless
		// (and, for a sealed class, a compile error - CS8121/CS0184).
		static bool CouldHideDisposable(ITypeSymbol type)
			=> type.TypeKind is TypeKind.Interface or TypeKind.TypeParameter
			   || (type.TypeKind == TypeKind.Class && !type.IsSealed);
	}

	/// <summary>
	///     Selects the method that produces an implementation: a container method for a <c>Factory</c>
	///     registration, or the implementation's own constructor otherwise. Returns <see langword="null" />
	///     when the registration is unusable - an unresolved factory (AWT108), a non-instantiable abstract or
	///     interface type (AWT103), or a type with no accessible constructor (AWT104) - having already appended
	///     the corresponding diagnostic. A factory produces the instance, so the registered type may be an
	///     interface and is not subject to the not-instantiable check a constructed type is.
	/// </summary>
	private static IMethodSymbol? SelectProducer(
		ImplInfo info,
		INamedTypeSymbol containerSymbol,
		Compilation compilation,
		Dictionary<ServiceKey, string> serviceToImpl,
		bool importServices,
		List<DiagnosticInfo> diagnostics)
	{
		if (info.Production == ProductionKind.Factory)
		{
			return ResolveFactory(containerSymbol, info, compilation, diagnostics);
		}

		// An abstract type or interface cannot be constructed; reject it instead of emitting a 'new'
		// against it (which would fail to compile in the generated source).
		if (info.Symbol.IsAbstract || info.Symbol.TypeKind == TypeKind.Interface)
		{
			diagnostics.Add(new DiagnosticInfo(
				Diagnostics.NotInstantiable,
				info.Location,
				new EquatableArray<string>([Display(info.ImplementationType),])));
			return null;
		}

		IMethodSymbol? constructor = SelectConstructor(info.Symbol, containerSymbol, serviceToImpl.Keys.Select(k => k.Service), importServices: importServices);
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
	///     Resolves an <c>OnActivated</c> / <c>OnRelease</c> lifecycle hook to a <c>static void M(TImplementation)</c>
	///     method on its owner - the container, or the module that declared the registration for an imported one
	///     (never falling back to the container) - returning the name the generated Root/Scope calls it by, or
	///     <see langword="null" /> when the registration named none. The owner is a static class, so the hook is a
	///     static method reached by simple name, exactly like a factory method - no receiver and no instance/static
	///     distinction; a module hook is qualified with the module type (the generated container is another class,
	///     so the simple name would not bind). The container's own members are reachable at any accessibility from
	///     the generated partial, so a <c>private</c> hook qualifies, but a module's are not: a module method that
	///     matches yet is inaccessible from the container is skipped (it cannot be called from the generated code),
	///     falling through to AWT164. Reports
	///     <see cref="Diagnostics.InvalidLifecycleHook">AWT164</see> and returns <see langword="null" /> when no
	///     accessible ordinary void method of that name accepts the implementation type.
	/// </summary>
	private static string? ResolveHook(
		INamedTypeSymbol containerSymbol,
		ImplInfo info,
		string? hookName,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		if (hookName is null)
		{
			return null;
		}

		foreach (ISymbol member in AccessibleMembers(info.Origin ?? containerSymbol, hookName))
		{
			// A module hook must also be accessible from the generated container (its own private members are
			// reachable from the partial, a module's are not); an inaccessible module method is not a usable hook.
			if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnsVoid: true, Parameters.Length: 1, } method
			    && compilation.HasImplicitConversion(info.Symbol, method.Parameters[0].Type)
			    && (info.Origin is null || compilation.IsSymbolAccessibleWithin(method, containerSymbol)))
			{
				return QualifiedHook(info, hookName);
			}
		}

		diagnostics.Add(new DiagnosticInfo(
			Diagnostics.InvalidLifecycleHook,
			info.Location,
			new EquatableArray<string>([Display(info.OwningServiceOrImpl), hookName, DescribeOwner(info),])));
		return null;
	}

	// A module's lifecycle hook is emitted qualified with the module type (the generated container is another
	// class, so the simple name would not bind); the container's own hooks stay unqualified - they are in scope
	// inside the generated partial. Mirrors QualifiedProductionMember for Factory/Instance members.
	private static string QualifiedHook(ImplInfo info, string hookName)
		=> info.Origin is { } origin ? $"{origin.ToDisplayString(FullyQualified)}.{hookName}" : hookName;

	/// <summary>
	///     Reports <see cref="Diagnostics.FactoryHidesAsyncInitialization">AWT106</see> when a synchronous
	///     factory method's body provably returns a concrete type that implements <c>IAsyncInitializable</c>
	///     while the method's declared return type does not - so the initialization is invisible to the
	///     container and never runs.
	/// </summary>
	/// <remarks>
	///     Conservative by design: it inspects only the producer's own <c>return</c> expressions (both
	///     expression-bodied and block-bodied), never descending into nested lambdas or local functions, and
	///     fires only when the statically determined type of the returned expression is a non-abstract,
	///     non-interface named type that is async-initializable. A metadata-only factory (no syntax) or an
	///     unresolved/unanalyzable return type yields no diagnostic. False negatives (helper-returned or
	///     runtime-selected implementations) are accepted; false positives are not. A hidden <c>IDisposable</c>
	///     is not reported (the container disposes factory outputs behind a runtime check); an asynchronous
	///     factory is excluded by the caller (it owns its own initialization).
	/// </remarks>
	private static void ReportFactoryHidingAsyncInitialization(
		IMethodSymbol producer,
		Compilation compilation,
		INamedTypeSymbol? asyncInitializableSymbol,
		List<DiagnosticInfo> diagnostics)
	{
		if (asyncInitializableSymbol is null)
		{
			return;
		}

		ITypeSymbol declaredReturnType = producer.ReturnType;

		// The container already sees the initialization when the declared return type is itself
		// async-initializable, so nothing it hides could be missed - there is no diagnostic to report.
		if (Implements(declaredReturnType, asyncInitializableSymbol))
		{
			return;
		}

		// Already-reported concrete types: a factory with several returns of the same hidden type should
		// surface a single diagnostic, not one per return.
		HashSet<ITypeSymbol> reported = new(SymbolEqualityComparer.Default);

		foreach (SyntaxReference reference in producer.DeclaringSyntaxReferences)
		{
			// A factory must be a method on the container; anything else (or metadata-only, no syntax) is
			// not analyzable here and is left silent.
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
				    || !reported.Add(concrete)
				    || !Implements(concrete, asyncInitializableSymbol))
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
					])));
			}
		}

		static bool Implements(ITypeSymbol type, INamedTypeSymbol @interface)
		{
			return SymbolEqualityComparer.Default.Equals(type, @interface)
			       || type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
		}
	}

	/// <summary>
	///     The expressions a method directly returns: the arrow expression of an expression-bodied method, or
	///     every <c>return x;</c> in a block body. Nested lambdas and local functions are not descended into,
	///     so their returns are never attributed to the enclosing factory.
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
	///     Resolves a <c>Factory</c> registration to the method that produces it - a container method, or a
	///     module method for a registration imported from a module (never falling back to the container). No
	///     method of that name returns the registered type → <see cref="Diagnostics.InvalidFactory">AWT108</see>
	///     naming the owner; a module method that matches but is not accessible from the generated container →
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
		List<IMethodSymbol> candidates = FindFactoryCandidates(
			owner, info.ProductionMember!, info.Symbol, compilation);

		// A container's own members are reachable by the generated partial at any accessibility, but a
		// module's members are called from outside the module, so only those the container can actually see
		// qualify; a match that exists on the module but is hidden from the container is its own error
		// (AWT153) rather than a confusing not-found AWT108.
		if (info.Origin is not null && candidates.Count > 0)
		{
			List<IMethodSymbol> accessible = candidates
				.Where(candidate => compilation.IsSymbolAccessibleWithin(candidate, containerSymbol))
				.ToList();
			if (accessible.Count == 0)
			{
				ReportInaccessibleModuleMember(info, diagnostics);
				return null;
			}

			candidates = accessible;
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
	///     Validates an <c>Instance</c> registration against the named member of its owner - the container,
	///     or the declaring module for an imported registration (never falling back to the container) -
	///     reporting <see cref="Diagnostics.InvalidInstance">AWT109</see> when no field or property of that
	///     name (on the owner or an accessible base type) holds the registered type, and
	///     <see cref="Diagnostics.InaccessibleModuleMember">AWT153</see> when a module member matches but is
	///     not accessible from the generated container.
	/// </summary>
	private static void ValidateInstanceMember(
		INamedTypeSymbol containerSymbol,
		ImplInfo info,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		bool inaccessibleMatch = false;
		foreach (ISymbol member in AccessibleMembers(info.Origin ?? containerSymbol, info.ProductionMember!))
		{
			ITypeSymbol? memberType = member switch
			{
				IFieldSymbol field => field.Type,
				IPropertySymbol property => property.Type,
				_ => null,
			};
			if (memberType is not null && compilation.HasImplicitConversion(memberType, info.Symbol))
			{
				if (info.Origin is null || compilation.IsSymbolAccessibleWithin(member, containerSymbol))
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

	// AWT153: the module declares a member that matches the Factory/Instance registration, but the generated
	// container cannot access it (e.g. a private member of a source module; a cross-assembly internal member
	// without InternalsVisibleTo is not even imported into the symbol tables and surfaces as AWT108/AWT109).
	private static void ReportInaccessibleModuleMember(ImplInfo info, List<DiagnosticInfo> diagnostics)
		=> diagnostics.Add(new DiagnosticInfo(
			Diagnostics.InaccessibleModuleMember,
			info.Location,
			new EquatableArray<string>([
				Display(info.OwningServiceOrImpl),
				Display(info.Origin!.ToDisplayString(FullyQualified)),
				info.ProductionMember!,
			])));

	// Names the owner of a Factory/Instance member in a diagnostic: the module that declared the
	// registration, or the container for its own registrations.
	private static string DescribeOwner(ImplInfo info)
		=> info.Origin is { } origin ? $"the module '{Display(origin.ToDisplayString(FullyQualified))}'" : "the container";

	// A module's Factory/Instance member is emitted qualified with the module type (the generated container
	// is another class, so the simple name would not bind); the container's own members stay unqualified -
	// they are in scope inside the generated partial.
	private static string? QualifiedProductionMember(ImplInfo info)
		=> info.ProductionMember is null || info.Origin is null
			? info.ProductionMember
			: $"{info.Origin.ToDisplayString(FullyQualified)}.{info.ProductionMember}";

	/// <summary>
	///     Chooses the constructor the container builds <paramref name="implementation" /> through: its single
	///     accessible constructor, or the greediest whose parameters are all satisfiable (falling back to the
	///     greediest so unresolved parameters surface as AWT101). <paramref name="additionallySatisfiable" />, when
	///     supplied, marks parameters the caller can satisfy beyond the registered set - open generic expansion
	///     passes it so a parameter whose closed generic is expanded on demand does not disqualify a constructor,
	///     letting the seed scan the same constructor the emitted container resolves.
	/// </summary>
	internal static IMethodSymbol? SelectConstructor(
		INamedTypeSymbol implementation,
		INamedTypeSymbol containerSymbol,
		IEnumerable<string> registeredServices,
		Func<IParameterSymbol, bool>? additionallySatisfiable = null,
		bool importServices = false)
	{
		List<IMethodSymbol> constructors = implementation.InstanceConstructors
			.Where(c => IsAccessibleConstructor(c, containerSymbol))
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
				// A collection - synchronous (Enumerable), asynchronous (AsyncEnumerable) or awaited (AwaitedEnumerable)
				// - is always satisfiable: an unregistered element type just yields an empty collection, so it never
				// disqualifies a constructor. A [FromServices] (External) parameter is always satisfiable too; with
				// [ImportServices] any direct dependency can fall through to the external provider, so it does not
				// disqualify a constructor either.
				ParameterModel parameter = ClassifyParameter(p, asyncFactory: false);
				return parameter.Kind is DependencyKind.Arg or DependencyKind.External
				       || IsSynthesizedCollection(parameter.Kind)
				       || (importServices && parameter.Kind == DependencyKind.Direct)
				       || registered.Contains(parameter.ServiceType)
				       || (additionallySatisfiable?.Invoke(p) ?? false);
			}))
			.OrderByDescending(c => c.Parameters.Length)
			.FirstOrDefault();

		// Fall back to the greediest constructor so its unresolved parameters surface as AWT101.
		return resolvable ?? constructors.OrderByDescending(c => c.Parameters.Length).First();

		static bool IsAccessibleConstructor(IMethodSymbol constructor, INamedTypeSymbol containerSymbol)
		{
			return constructor.DeclaredAccessibility switch
			{
				Accessibility.Public => true,
				Accessibility.Internal or Accessibility.ProtectedOrInternal =>
					SymbolEqualityComparer.Default.Equals(
						constructor.ContainingAssembly, containerSymbol.ContainingAssembly),
				_ => false,
			};
		}
	}

	/// <summary>
	///     The members named <paramref name="name" /> that the generated container partial can reach: the
	///     container's own members (any accessibility, since a partial can use its own private members)
	///     plus members inherited from base types that a derived type can actually access (everything but
	///     private, with internal / private-protected restricted to the same assembly).
	/// </summary>
	private static IEnumerable<ISymbol> AccessibleMembers(INamedTypeSymbol container, string name)
	{
		foreach (ISymbol member in container.GetMembers(name))
		{
			yield return member;
		}

		for (INamedTypeSymbol? baseType = container.BaseType; baseType is not null; baseType = baseType.BaseType)
		{
			foreach (ISymbol member in baseType.GetMembers(name).Where(m => IsAccessibleFromDerived(m, container)))
			{
				yield return member;
			}
		}

		static bool IsAccessibleFromDerived(ISymbol member, INamedTypeSymbol container)
			=> member.DeclaredAccessibility switch
			{
				Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
				Accessibility.Internal or Accessibility.ProtectedAndInternal =>
					SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, container.ContainingAssembly),
				_ => false,
			};
	}

	/// <summary>
	///     The ordinary methods named <paramref name="name" /> on the container (or an accessible base
	///     type) whose return type produces <paramref name="serviceType" /> - the candidate factory methods
	///     for a <c>Factory</c> registration. A synchronous factory's return type is implicitly convertible to
	///     the service type; an asynchronous factory returns <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>
	///     and is matched against the unwrapped <c>T</c> (the container awaits it). None means AWT108; more
	///     than one means an ambiguous factory (AWT112).
	/// </summary>
	private static List<IMethodSymbol> FindFactoryCandidates(
		INamedTypeSymbol container, string name, ITypeSymbol serviceType, Compilation compilation)
	{
		List<IMethodSymbol> candidates = new();
		foreach (ISymbol member in AccessibleMembers(container, name))
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
	///     The service type a factory's return type produces: the awaited result <c>T</c> for an asynchronous
	///     factory returning <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>, otherwise the return type
	///     itself. A non-generic <c>Task</c> / <c>ValueTask</c> (no result) is not unwrapped, so it is matched
	///     as-is and falls out as AWT108 (it produces no service).
	/// </summary>
	private static ITypeSymbol ProducedType(ITypeSymbol returnType, Compilation compilation)
		=> IsAsyncFactoryReturn(returnType, compilation, out ITypeSymbol produced) ? produced : returnType;

	/// <summary>
	///     Whether <paramref name="returnType" /> is an awaitable factory return - <c>Task&lt;T&gt;</c> or
	///     <c>ValueTask&lt;T&gt;</c> - yielding the produced result type <c>T</c>. Matched by the canonical
	///     metadata symbols so a user-defined <c>Task`1</c> in another namespace is not mistaken for one.
	///     <c>ValueTask&lt;T&gt;</c> is absent on netstandard2.0; <see cref="Compilation.GetTypeByMetadataName" />
	///     returns <see langword="null" /> there and that branch is simply skipped.
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
