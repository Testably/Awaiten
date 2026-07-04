using System.Threading;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of the requesting-type factory parameter: a <c>Factory =</c> method marked with a
///     <c>[RequestingType] Type</c> parameter receives, at each construction site, the <c>typeof(…)</c> of
///     the consumer being satisfied (the declaring type of the constructor parameter or <c>[Inject]</c>
///     property). The factory's other parameters resolve from the graph as usual, and a top-level resolve
///     (no consumer) passes <c>null</c>. The containers and services are nested types, so the enclosing
///     class is <c>partial</c>.
/// </summary>
public partial class RequestingTypeTests
{
	[Fact]
	public async Task RequestingType_IsFilledWithTheConsumerType()
	{
		using LoggerContainer.Root container = new();

		Alpha alpha = container.Resolve<Alpha>();

		await That(alpha.Logger.Category).IsEqualTo(typeof(Alpha).FullName);
	}

	[Fact]
	public async Task RequestingType_GivesEachConsumerItsOwnRequestingType()
	{
		using LoggerContainer.Root container = new();

		Alpha alpha = container.Resolve<Alpha>();
		Beta beta = container.Resolve<Beta>();

		// Two different consumers get differently-named loggers.
		await That(alpha.Logger.Category).IsEqualTo(typeof(Alpha).FullName);
		await That(beta.Logger.Category).IsEqualTo(typeof(Beta).FullName);
		await That(alpha.Logger.Category).IsNotEqualTo(beta.Logger.Category);
	}

	[Fact]
	public async Task RequestingType_FactoryStillResolvesItsOtherDependenciesFromTheGraph()
	{
		using LoggerContainer.Root container = new();

		Alpha alpha = container.Resolve<Alpha>();

		// The factory's non-[RequestingType] parameter (the prefix) resolved from the graph.
		await That(alpha.Logger.Prefix).IsSameAs(container.Resolve<Prefix>());
	}

	[Fact]
	public async Task RequestingType_FilledForAnInjectedProperty()
	{
		using LoggerContainer.Root container = new();

		Gamma gamma = container.Resolve<Gamma>();

		await That(gamma.Logger!.Category).IsEqualTo(typeof(Gamma).FullName);
	}

	[Fact]
	public async Task RequestingType_AtTheTopLevelReceivesNull()
	{
		using LoggerContainer.Root container = new();

		ILogger logger = container.Resolve<ILogger>();

		await That(logger.Category).IsEqualTo("<root>");
	}

	[Fact]
	public async Task RequestingType_ThroughAFuncReceivesTheConsumerType()
	{
		using LoggerContainer.Root container = new();

		Delta delta = container.Resolve<Delta>();

		await That(delta.LoggerFactory().Category).IsEqualTo(typeof(Delta).FullName);
	}

	public interface ILogger
	{
		string Category { get; }

		Prefix Prefix { get; }
	}

	public sealed class Prefix;

	public sealed class Logger : ILogger
	{
		public Logger(string category, Prefix prefix)
		{
			Category = category;
			Prefix = prefix;
		}

		public string Category { get; }

		public Prefix Prefix { get; }
	}

	public sealed class Alpha
	{
		public Alpha(ILogger logger) => Logger = logger;

		public ILogger Logger { get; }
	}

	public sealed class Beta
	{
		public Beta(ILogger logger) => Logger = logger;

		public ILogger Logger { get; }
	}

	public sealed class Gamma
	{
		[Inject]
		public ILogger? Logger { get; set; }
	}

	public sealed class Delta
	{
		public Delta(Func<ILogger> loggerFactory) => LoggerFactory = loggerFactory;

		public Func<ILogger> LoggerFactory { get; }
	}

	[Fact]
	public async Task AsyncTaintedRequestingType_IsFilledWithTheConsumerType()
	{
		using AsyncLoggerContainer.Root container = new();

		Epsilon epsilon = await container.ResolveAsync<Epsilon>(TestContext.Current.CancellationToken);

		// The factory is async-tainted (it awaits its AsyncPrefix dependency), so it is built through the async
		// resolver - which still embeds the consumer's typeof(…) per site.
		await That(epsilon.Logger.Category).IsEqualTo(typeof(Epsilon).FullName);
	}

	[Fact]
	public async Task AsyncTaintedRequestingType_AtTheTopLevelReceivesNull()
	{
		using AsyncLoggerContainer.Root container = new();

		ILogger logger = await container.ResolveAsync<ILogger>(TestContext.Current.CancellationToken);

		await That(logger.Category).IsEqualTo("<root>");
	}

	[Fact]
	public async Task AsyncFactoryRequestingType_IsFilledWithTheConsumerType()
	{
		using AsyncFactoryContainer.Root container = new();

		Epsilon epsilon = await container.ResolveAsync<Epsilon>(TestContext.Current.CancellationToken);

		// The factory itself is asynchronous (returns Task<Logger>); the requesting type still flows through.
		await That(epsilon.Logger.Category).IsEqualTo(typeof(Epsilon).FullName);
	}

	public sealed class AsyncPrefix : IAsyncInitializable
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	public sealed class Epsilon
	{
		public Epsilon(ILogger logger) => Logger = logger;

		public ILogger Logger { get; }
	}

	[Container]
	[Singleton<Prefix>]
	[Transient<ILogger>(Factory = nameof(CreateLogger))]
	[Transient<Alpha>]
	[Transient<Beta>]
	[Transient<Gamma>]
	[Transient<Delta>]
	public static partial class LoggerContainer
	{
		// The category is the requesting consumer's full name, or "<root>" when resolved at the top level.
		private static Logger CreateLogger([RequestingType] Type? requestingType, Prefix prefix)
			=> new Logger(requestingType?.FullName ?? "<root>", prefix);
	}

	[Container]
	[Singleton<Prefix>]
	[Singleton<AsyncPrefix>]
	[Transient<ILogger>(Factory = nameof(CreateLogger))]
	[Transient<Epsilon>]
	public static partial class AsyncLoggerContainer
	{
		// Async-tainted through its AsyncPrefix dependency (which the factory awaits), so the logger is reached
		// only through the async resolver - yet the requesting type is still supplied per consumer.
		private static Logger CreateLogger([RequestingType] Type? requestingType, Prefix prefix, AsyncPrefix asyncPrefix)
			=> new Logger(requestingType?.FullName ?? "<root>", prefix);
	}

	[Container]
	[Singleton<Prefix>]
	[Transient<ILogger>(Factory = nameof(CreateLogger))]
	[Transient<Epsilon>]
	public static partial class AsyncFactoryContainer
	{
		// An asynchronous factory (returns Task<Logger>) that also takes the requesting type.
		private static async Task<Logger> CreateLogger([RequestingType] Type? requestingType, Prefix prefix)
		{
			await Task.Yield();
			return new Logger(requestingType?.FullName ?? "<root>", prefix);
		}
	}
}
