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
		private static ILogger CreateLogger([RequestingType] Type? requestingType, Prefix prefix)
			=> new Logger(requestingType?.FullName ?? "<root>", prefix);
	}
}
