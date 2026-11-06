using System;
using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open generic expansion requested by decorators and composites: a closing that only an open
///     registration provides is constructed for their constructor parameters and injected properties exactly as for a
///     registration's.
/// </summary>
public partial class OpenGenericDecoratorParameterTests
{
	public sealed class Client;

	public sealed class Order;

	public interface IGen<T>;

	public sealed class Gen<T> : IGen<T>;

	public sealed class OtherGen<T> : IGen<T>;

	public interface IBox;

	public sealed class Box : IBox;

	public sealed class BoxDecorator(IBox inner, IGen<Client> gen) : IBox
	{
		public IBox Inner { get; } = inner;

		public IGen<Client> Gen { get; } = gen;
	}

	public sealed class BoxComposite(IEnumerable<IBox> boxes, IGen<Client> gen) : IBox
	{
		public IEnumerable<IBox> Boxes { get; } = boxes;

		public IGen<Client> Gen { get; } = gen;
	}

	public sealed class WrappedBoxDecorator(
		IBox inner,
		Lazy<IGen<Client>> lazy,
		Func<IGen<Client>> func,
		IEnumerable<IGen<Client>> all,
		Owned<IGen<Client>> owned,
		IReadOnlyDictionary<string, IGen<Client>> keyed) : IBox
	{
		public IBox Inner { get; } = inner;

		public Lazy<IGen<Client>> Lazy { get; } = lazy;

		public Func<IGen<Client>> Func { get; } = func;

		public IEnumerable<IGen<Client>> All { get; } = all;

		public Owned<IGen<Client>> Owned { get; } = owned;

		public IReadOnlyDictionary<string, IGen<Client>> Keyed { get; } = keyed;
	}

	public sealed class InjectedBoxDecorator(IBox inner) : IBox
	{
		public IBox Inner { get; } = inner;

		[Inject]
		public IGen<Client> Gen { get; set; } = null!;
	}

	public interface IRep<T>;

	public sealed class Rep<T> : IRep<T>;

	public sealed class RepDecorator<T>(IRep<T> inner, IGen<T> gen) : IRep<T>
	{
		public IRep<T> Inner { get; } = inner;

		public IGen<T> Gen { get; } = gen;
	}

	public sealed class RepComposite<T>(IEnumerable<IRep<T>> all, IGen<T> gen) : IRep<T>
	{
		public IEnumerable<IRep<T>> All { get; } = all;

		public IGen<T> Gen { get; } = gen;
	}

	public interface ILog<T>;

	public sealed class Log<T> : ILog<T>;

	public sealed class GenDecorator<T>(IGen<T> inner, ILog<T> log) : IGen<T>
	{
		public IGen<T> Inner { get; } = inner;

		public ILog<T> Log { get; } = log;
	}

	public sealed class Shop(IRep<Order> orders)
	{
		public IRep<Order> Orders { get; } = orders;
	}

	[Test]
	public async Task ClosedDecoratorParameter_IsResolvedFromTheOpenRegistration()
	{
		using ClosedDecoratorContainer.Root container = new();

		BoxDecorator decorator = (BoxDecorator)container.Resolve<IBox>();

		await That(decorator.Gen).Is<Gen<Client>>()
			.Because("the decorator's IGen<Client> parameter closes the open registration");
		await That(decorator.Inner).Is<Box>()
			.Because("the decorator still wraps the registered box");
	}

	[Test]
	public async Task ClosedCompositeParameter_IsResolvedFromTheOpenRegistration()
	{
		using ClosedCompositeContainer.Root container = new();

		BoxComposite composite = (BoxComposite)container.Resolve<IBox>();

		await That(composite.Gen).Is<Gen<Client>>()
			.Because("the composite's IGen<Client> parameter closes the open registration");
		await That(composite.Boxes.Select(box => box.GetType())).IsEqualTo([typeof(Box),])
			.Because("the composite still fans out over the registered box");
	}

	[Test]
	public async Task OpenDecoratorParameter_IsResolvedFromTheOpenRegistrationPerClosing()
	{
		using OpenDecoratorContainer.Root container = new();

		RepDecorator<Order> decorator = (RepDecorator<Order>)container.Resolve<Shop>().Orders;

		await That(decorator.Gen).Is<Gen<Order>>()
			.Because("the decorator closed at Order requests IGen<Order>, which the open registration provides");
		await That(decorator.Inner).Is<Rep<Order>>()
			.Because("the decorator still wraps the closed repository");
	}

	[Test]
	public async Task OpenCompositeParameter_IsResolvedFromTheOpenRegistrationPerClosing()
	{
		using OpenCompositeContainer.Root container = new();

		RepComposite<Order> composite = (RepComposite<Order>)container.Resolve<Shop>().Orders;

		await That(composite.Gen).Is<Gen<Order>>()
			.Because("the composite closed at Order requests IGen<Order>, which the open registration provides");
		await That(composite.All.Select(rep => rep.GetType())).IsEqualTo([typeof(Rep<Order>),])
			.Because("the composite still fans out over the closed repository");
	}

	[Test]
	public async Task DecoratorOfASynthesizedClosing_IsResolvedFromTheOpenRegistration()
	{
		using ChainedDecoratorContainer.Root container = new();

		RepDecorator<Order> decorator = (RepDecorator<Order>)container.Resolve<Shop>().Orders;
		GenDecorator<Order> gen = (GenDecorator<Order>)decorator.Gen;

		await That(gen.Log).Is<Log<Order>>()
			.Because("IGen<Order>, closed for the repository decorator, is decorated in turn and its ILog<Order> is closed too");
		await That(gen.Inner).Is<Gen<Order>>()
			.Because("the generator decorator wraps the closed generator");
	}

	[Test]
	public async Task WrappedDecoratorParameters_AreResolvedFromTheOpenRegistration()
	{
		using WrappedDecoratorContainer.Root container = new();

		WrappedBoxDecorator decorator = (WrappedBoxDecorator)container.Resolve<IBox>();

		await That(decorator.Lazy.Value).Is<Gen<Client>>()
			.Because("a Lazy<IGen<Client>> decorator parameter closes the open registration");
		await That(decorator.Func()).Is<Gen<Client>>()
			.Because("a Func<IGen<Client>> decorator parameter closes the open registration");
		await That(decorator.All.Select(gen => gen.GetType())).IsEqualTo([typeof(Gen<Client>),])
			.Because("a collection decorator parameter holds the closing instead of resolving empty");
		await That(decorator.Owned.Value).Is<Gen<Client>>()
			.Because("an Owned<IGen<Client>> decorator parameter closes the open registration");
		await That(decorator.Keyed["other"]).Is<OtherGen<Client>>()
			.Because("a keyed dictionary decorator parameter closes the keyed open registration");
	}

	[Test]
	public async Task InjectedDecoratorProperty_IsResolvedFromTheOpenRegistration()
	{
		using InjectedDecoratorContainer.Root container = new();

		InjectedBoxDecorator decorator = (InjectedBoxDecorator)container.Resolve<IBox>();

		await That(decorator.Gen).Is<Gen<Client>>()
			.Because("the decorator's [Inject] property closes the open registration");
	}

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<Box, IBox>]
	[Decorate<BoxDecorator, IBox>]
	public static partial class ClosedDecoratorContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<Box, IBox>]
	[Composite<BoxComposite, IBox>]
	public static partial class ClosedCompositeContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Transient(typeof(Rep<>), typeof(IRep<>))]
	[Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
	[Singleton<Shop>]
	public static partial class OpenDecoratorContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Transient(typeof(Rep<>), typeof(IRep<>))]
	[Composite(typeof(RepComposite<>), typeof(IRep<>))]
	[Singleton<Shop>]
	public static partial class OpenCompositeContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Transient(typeof(Log<>), typeof(ILog<>))]
	[Transient(typeof(Rep<>), typeof(IRep<>))]
	[Decorate(typeof(RepDecorator<>), typeof(IRep<>))]
	[Decorate(typeof(GenDecorator<>), typeof(IGen<>))]
	[Singleton<Shop>]
	public static partial class ChainedDecoratorContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Transient(typeof(OtherGen<>), typeof(IGen<>), Key = "other")]
	[Singleton<Box, IBox>]
	[Decorate<WrappedBoxDecorator, IBox>]
	public static partial class WrappedDecoratorContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<Box, IBox>]
	[Decorate<InjectedBoxDecorator, IBox>]
	public static partial class InjectedDecoratorContainer;
}
