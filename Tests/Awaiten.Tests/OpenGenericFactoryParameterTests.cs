using System;
using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of open generic expansion requested by factory parameters and by the wrapped parameter
///     shapes: a closing that only an open registration provides is constructed for them exactly as for a plain
///     constructor parameter.
/// </summary>
public partial class OpenGenericFactoryParameterTests
{
	public sealed class Client;

	public interface IGen<T>;

	public sealed class Gen<T> : IGen<T>;

	public sealed class OtherGen<T> : IGen<T>;

	public sealed class ThirdParty(params object[] dependencies)
	{
		public object[] Dependencies { get; } = dependencies;
	}

	public sealed class Consumer(
		Owned<IGen<Client>> owned,
		IAsyncEnumerable<IGen<Client>> all,
		Func<Task<IGen<Client>>> create)
	{
		public Owned<IGen<Client>> Owned { get; } = owned;

		public IAsyncEnumerable<IGen<Client>> All { get; } = all;

		public Func<Task<IGen<Client>>> Create { get; } = create;
	}

	public sealed class KeyedConsumer(IReadOnlyDictionary<string, IGen<Client>> gens)
	{
		public IReadOnlyDictionary<string, IGen<Client>> Gens { get; } = gens;
	}

	[Test]
	public async Task FactoryParameter_IsResolvedFromTheOpenRegistration()
	{
		using FactoryContainer.Root container = new();

		ThirdParty thirdParty = container.Resolve<ThirdParty>();

		await That(thirdParty.Dependencies.Single()).Is<Gen<Client>>()
			.Because("the factory's IGen<Client> parameter closes the open registration");
	}

	[Test]
	public async Task EagerFactoryParameter_IsResolvedFromTheOpenRegistration()
	{
		using EagerFactoryContainer.Root container = new();

		ThirdParty thirdParty = container.Resolve<ThirdParty>();

		await That(thirdParty.Dependencies.Single()).Is<Gen<Client>>()
			.Because("an eager factory's parameter closes the open registration too");
	}

	[Test]
	public async Task AsyncFactoryParameter_IsResolvedFromTheOpenRegistration()
	{
		await using AsyncFactoryContainer.Root container = new();

		ThirdParty thirdParty = await container.ResolveAsync<ThirdParty>();

		await That(thirdParty.Dependencies.Single()).Is<Gen<Client>>()
			.Because("an async factory's parameter closes the open registration like a synchronous one");
	}

	[Test]
	public async Task ModuleFactoryParameter_IsResolvedFromTheOpenRegistration()
	{
		using ModuleFactoryContainer.Root container = new();

		ThirdParty thirdParty = container.Resolve<ThirdParty>();

		await That(thirdParty.Dependencies.Single()).Is<Gen<Client>>()
			.Because("an imported module's factory parameter closes the container's open registration");
	}

	[Test]
	public async Task FromKeyFactoryParameter_IsResolvedFromTheKeyedOpenRegistration()
	{
		using KeyedFactoryContainer.Root container = new();

		ThirdParty thirdParty = container.Resolve<ThirdParty>();

		await That(thirdParty.Dependencies.Single()).Is<OtherGen<Client>>()
			.Because("the [FromKey(\"other\")] parameter closes the open registration keyed 'other'");
	}

	[Test]
	public async Task WrappedFactoryParameters_AreResolvedFromTheOpenRegistration()
	{
		using WrappedFactoryContainer.Root container = new();

		object[] dependencies = container.Resolve<ThirdParty>().Dependencies;

		await That(((Lazy<IGen<Client>>)dependencies[0]).Value).Is<Gen<Client>>()
			.Because("a Lazy<IGen<Client>> factory parameter closes the open registration");
		await That(((Func<IGen<Client>>)dependencies[1])()).Is<Gen<Client>>()
			.Because("a Func<IGen<Client>> factory parameter closes the open registration");
		await That(((IReadOnlyList<IGen<Client>>)dependencies[2]).Select(gen => gen.GetType())).IsEqualTo([typeof(Gen<Client>),])
			.Because("a collection factory parameter holds the closing instead of resolving empty");
	}

	[Test]
	public async Task WrappedConstructorParameters_AreResolvedFromTheOpenRegistration()
	{
		using WrappedConstructorContainer.Root container = new();

		Consumer consumer = container.Resolve<Consumer>();

		await That(consumer.Owned.Value).Is<Gen<Client>>()
			.Because("an Owned<IGen<Client>> parameter closes the open registration");
		List<IGen<Client>> all = [];
		await foreach (IGen<Client> gen in consumer.All)
		{
			all.Add(gen);
		}

		await That(all.Select(gen => gen.GetType())).IsEqualTo([typeof(Gen<Client>),])
			.Because("an IAsyncEnumerable<IGen<Client>> parameter holds the closing instead of resolving empty");
		await That(await consumer.Create()).Is<Gen<Client>>()
			.Because("a Func<Task<IGen<Client>>> parameter closes the open registration");
	}

	[Test]
	public async Task KeyedDictionaryParameter_HoldsEveryKeyedClosing()
	{
		using KeyedDictionaryContainer.Root container = new();

		KeyedConsumer consumer = container.Resolve<KeyedConsumer>();

		await That(consumer.Gens["a"]).Is<Gen<Client>>()
			.Because("the open registration keyed 'a' is closed for the keyed dictionary");
		await That(consumer.Gens["b"]).Is<OtherGen<Client>>()
			.Because("the open registration keyed 'b' is closed for the keyed dictionary");
	}

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<ThirdParty>(Factory = nameof(Create))]
	public static partial class FactoryContainer
	{
		private static ThirdParty Create(IGen<Client> gen) => new(gen);
	}

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<ThirdParty>(Factory = nameof(Create), Eager = true)]
	public static partial class EagerFactoryContainer
	{
		private static ThirdParty Create(IGen<Client> gen) => new(gen);
	}

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<ThirdParty>(Factory = nameof(CreateAsync))]
	public static partial class AsyncFactoryContainer
	{
		private static Task<ThirdParty> CreateAsync(IGen<Client> gen) => Task.FromResult(new ThirdParty(gen));
	}

	[Module]
	[Singleton<ThirdParty>(Factory = nameof(Create))]
	public static class ThirdPartyModule
	{
		public static ThirdParty Create(IGen<Client> gen) => new(gen);
	}

	[Container]
	[Import(typeof(ThirdPartyModule))]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	public static partial class ModuleFactoryContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Transient(typeof(OtherGen<>), typeof(IGen<>), Key = "other")]
	[Singleton<ThirdParty>(Factory = nameof(Create))]
	public static partial class KeyedFactoryContainer
	{
		private static ThirdParty Create([FromKey("other")] IGen<Client> gen) => new(gen);
	}

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<ThirdParty>(Factory = nameof(Create))]
	public static partial class WrappedFactoryContainer
	{
		private static ThirdParty Create(Lazy<IGen<Client>> lazy, Func<IGen<Client>> func, IReadOnlyList<IGen<Client>> all)
			=> new(lazy, func, all);
	}

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>))]
	[Singleton<Consumer>]
	public static partial class WrappedConstructorContainer;

	[Container]
	[Transient(typeof(Gen<>), typeof(IGen<>), Key = "a")]
	[Transient(typeof(OtherGen<>), typeof(IGen<>), Key = "b")]
	[Singleton<KeyedConsumer>]
	public static partial class KeyedDictionaryContainer;
}
