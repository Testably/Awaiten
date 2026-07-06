namespace Awaiten.Tests;

/// <summary>
///     The registration attributes are compile-time inputs the source generator reads via Roslyn; these pin that
///     their constructors and settable properties round-trip the values a user writes.
/// </summary>
public sealed class AttributeTests
{
	private interface IService;

	private sealed class Impl : IService;

	[Fact]
	public async Task OpenGenericLifetimeAttributes_ExposeImplementationServiceAndKey()
	{
		await That(new SingletonAttribute(typeof(Impl)).Service).IsEqualTo(typeof(Impl));
		await That(new ScopedAttribute(typeof(Impl), typeof(IService)) { Key = "s" }.Key).IsEqualTo("s");

		TransientAttribute transient = new(typeof(Impl), typeof(IService));
		await That(transient.Implementation).IsEqualTo(typeof(Impl));
		await That(transient.Service).IsEqualTo(typeof(IService));
	}

	[Fact]
	public async Task SingletonAttribute_Generic_RoundTripsAllOptions()
	{
		SingletonAttribute<Impl, IService> attribute = new()
		{
			Factory = "Make", Instance = "Field", Key = "k", WhenInjectedInto = typeof(Impl),
			Fallback = Fallback.Warn, Eager = true, OnActivated = "OnUp", OnRelease = "OnDown",
		};

		await That(attribute.Factory).IsEqualTo("Make");
		await That(attribute.Instance).IsEqualTo("Field");
		await That(attribute.Eager).IsTrue();
		await That(attribute.Fallback).IsEqualTo(Fallback.Warn);
		await That(attribute.WhenInjectedInto).IsEqualTo(typeof(Impl));
		await That(attribute.OnActivated).IsEqualTo("OnUp");
		await That(attribute.OnRelease).IsEqualTo("OnDown");
		await That(attribute.Key).IsEqualTo("k");
	}

	[Fact]
	public async Task ScopedAndTransientAttributes_Generic_RoundTripTheirOptions()
	{
		ScopedAttribute<Impl> scoped = new() { Factory = "S", Key = 1, Fallback = Fallback.Silent };
		await That(scoped.Factory).IsEqualTo("S");
		await That(scoped.Fallback).IsEqualTo(Fallback.Silent);

		TransientAttribute<Impl, IService> transient = new() { OnActivated = "T", WhenInjectedInto = typeof(Impl) };
		await That(transient.OnActivated).IsEqualTo("T");
		await That(transient.WhenInjectedInto).IsEqualTo(typeof(Impl));
	}

	[Fact]
	public async Task ScanAttribute_RoundTripsMarkerAndFilters()
	{
		ScanAttribute scan = new(typeof(IService))
		{
			Lifetime = AwaitenLifetime.Scoped, As = ScanAs.SelfAndMarker, SkipUnconstructable = true,
			InAssembliesOf = [typeof(Impl)], NamePatterns = ["*Service"], NamespacePatterns = ["App.**"],
			Exclude = [typeof(Impl)],
		};

		await That(scan.AssignableTo).IsEqualTo(typeof(IService));
		await That(scan.Lifetime).IsEqualTo(AwaitenLifetime.Scoped);
		await That(scan.As).IsEqualTo(ScanAs.SelfAndMarker);
		await That(scan.SkipUnconstructable).IsTrue();
		await That(scan.InAssembliesOf).IsEqualTo(new[] { typeof(Impl) });
		await That(scan.NamePatterns).IsEqualTo(new[] { "*Service" });
		await That(scan.NamespacePatterns).IsEqualTo(new[] { "App.**" });
		await That(scan.Exclude).IsEqualTo(new[] { typeof(Impl) });
	}

	[Fact]
	public async Task ScanAttribute_Generic_RoundTripsFilters()
	{
		ScanAttribute<IService> scan = new()
		{
			Lifetime = AwaitenLifetime.Singleton, As = ScanAs.Marker, SkipUnconstructable = true,
			InAssembliesOf = [typeof(Impl)], NamePatterns = ["A*"], NamespacePatterns = ["N.*"], Exclude = [typeof(Impl)],
		};

		await That(scan.Lifetime).IsEqualTo(AwaitenLifetime.Singleton);
		await That(scan.As).IsEqualTo(ScanAs.Marker);
		await That(scan.SkipUnconstructable).IsTrue();
		await That(scan.Exclude).IsEqualTo(new[] { typeof(Impl) });
	}

	[Fact]
	public async Task CompositeAttributes_ExposeTheirTypesAndLifetime()
	{
		CompositeAttribute<Impl, IService> generic = new() { Lifetime = AwaitenLifetime.Singleton };
		await That(generic.Lifetime).IsEqualTo(AwaitenLifetime.Singleton);

		CompositeAttribute open = new(typeof(Impl), typeof(IService)) { Lifetime = AwaitenLifetime.Scoped };
		await That(open.Composite).IsEqualTo(typeof(Impl));
		await That(open.Service).IsEqualTo(typeof(IService));
		await That(open.Lifetime).IsEqualTo(AwaitenLifetime.Scoped);
	}

	[Fact]
	public async Task DecorateAttributes_ExposeTheirTypesAndOrder()
	{
		DecorateAttribute<Impl, IService> generic = new() { Order = 3 };
		await That(generic.Order).IsEqualTo(3);

		DecorateAttribute open = new(typeof(Impl), typeof(IService)) { Order = 5 };
		await That(open.Decorator).IsEqualTo(typeof(Impl));
		await That(open.Service).IsEqualTo(typeof(IService));
		await That(open.Order).IsEqualTo(5);
	}

	[Fact]
	public async Task FromKeyAttribute_AcceptsStringAndObjectKeys()
	{
		await That(new FromKeyAttribute("utc").Key).IsEqualTo("utc");
		await That(new FromKeyAttribute((object)AwaitenLifetime.Scoped).Key).IsEqualTo(AwaitenLifetime.Scoped);
	}

	[Fact]
	public async Task ImportAndInjectAttributes_RoundTrip()
	{
		await That(new ImportAttribute(typeof(Impl)).Module).IsEqualTo(typeof(Impl));

		InjectAttribute inject = new() { Deferred = true, Optional = true };
		await That(inject.Deferred).IsTrue();
		await That(inject.Optional).IsTrue();
	}

	[Fact]
	public async Task ContainerAttribute_RoundTripsItsOptions()
	{
		ContainerAttribute attribute = new() { SyncResolveAfterInit = true, LifetimeSafety = LifetimeSafety.Loose };

		await That(attribute.SyncResolveAfterInit).IsTrue();
		await That(attribute.LifetimeSafety).IsEqualTo(LifetimeSafety.Loose);
	}
}
