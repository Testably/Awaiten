namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	private const string KeyedTypes = """
	                                  using Awaiten;
	                                  using System.Collections.Generic;

	                                  namespace MyCode;

	                                  public sealed class Order { }
	                                  public interface IService { }
	                                  public sealed class Real : IService { }
	                                  public sealed class Backup : IService { }
	                                  public sealed class LoggingDecorator : IService { public LoggingDecorator(IService inner) { } }
	                                  public sealed class CompositeService : IService { public CompositeService(IEnumerable<IService> all) { } }
	                                  public interface IHandler<T> { }
	                                  public sealed class Handler<T> : IHandler<T> { }
	                                  public sealed class LoggingBehavior<T> : IHandler<T> { public LoggingBehavior(IHandler<T> inner) { } }
	                                  public sealed class CompositeHandler<T> : IHandler<T> { public CompositeHandler(IEnumerable<IHandler<T>> all) { } }

	                                  """;

	public class Awt210DecoratedKeyNotRegistered
	{
		[Test]
		public async Task ReportsWhenAKeyInKeysHasNoRegistration()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Transient<Backup, IService>(Key = "backup")]
			                                                    [Decorate<LoggingDecorator, IService>(Keys = new object[] { "backup", "missing" })]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT210*missing*").AsWildcard()
				.Because("no registration of IService has the key 'missing'");
			await That(result.Diagnostics).DoesNotContain("*AWT210*backup*").AsWildcard()
				.Because("the key 'backup' is registered");
		}

		[Test]
		public async Task ReportsWhenANullEntryFindsNoUnkeyedRegistration()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Backup, IService>(Key = "backup")]
			                                                    [Decorate<LoggingDecorator, IService>(Keys = new object[] { null, "backup" })]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT210*null*").AsWildcard()
				.Because("the null entry selects the unkeyed registration, which does not exist");
		}

		[Test]
		public async Task ReportsWhenNoClosingOfAnOpenDecoratorHasTheKey()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Handler<Order>, IHandler<Order>>]
			                                                    [Decorate(typeof(LoggingBehavior<>), typeof(IHandler<>), Keys = new object[] { "missing" })]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT210*missing*").AsWildcard()
				.Because("no closing of IHandler<> is registered under the key 'missing'");
		}

		[Test]
		public async Task DoesNotReportWhenEverySelectedKeyIsRegistered()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Transient<Backup, IService>(Key = "backup")]
			                                                    [Decorate<LoggingDecorator, IService>(Keys = new object[] { null, "backup" })]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task DoesNotReportForAllKeys()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Backup, IService>(Key = "backup")]
			                                                    [Decorate<LoggingDecorator, IService>(AllKeys = true)]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).IsEmpty()
				.Because("AllKeys decorates whatever is registered and names no key that could be missing");
		}
	}

	public class Awt211DecoratorKeysWithAllKeys
	{
		[Test]
		public async Task ReportsWhenKeysAndAllKeysAreBothSet()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Decorate<LoggingDecorator, IService>(Keys = new object[] { null }, AllKeys = true)]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT211*").AsWildcard()
				.Because("Keys and AllKeys contradict each other");
		}

		[Test]
		public async Task ReportsForTheOpenGenericForm()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Handler<Order>, IHandler<Order>>]
			                                                    [Decorate(typeof(LoggingBehavior<>), typeof(IHandler<>), Keys = new object[] { null }, AllKeys = true)]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT211*").AsWildcard();
		}

		[Test]
		public async Task DoesNotReportForEitherAlone()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Decorate<LoggingDecorator, IService>(AllKeys = true)]
			                                                    [Decorate<LoggingDecorator, IService>(Keys = new object[] { null })]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).DoesNotContain("*AWT211*").AsWildcard();
		}
	}

	public class Awt212CompositeKeyNotRegistered
	{
		[Test]
		public async Task ReportsWhenTheCompositeKeyHasNoRegistration()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Composite<CompositeService, IService>(Key = "missing")]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT212*missing*").AsWildcard()
				.Because("no registration of IService has the key 'missing'");
		}

		[Test]
		public async Task ReportsWhenNoClosingOfAnOpenCompositeHasTheKey()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Handler<Order>, IHandler<Order>>]
			                                                    [Composite(typeof(CompositeHandler<>), typeof(IHandler<>), Key = "missing")]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT212*missing*").AsWildcard();
		}

		[Test]
		public async Task DoesNotReportWhenTheKeyIsRegistered()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Backup, IService>(Key = "backup")]
			                                                    [Composite<CompositeService, IService>(Key = "backup")]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).IsEmpty();
		}
	}

	public class Awt170KeyedDecoratorComposite
	{
		[Test]
		public async Task ReportsAnUnsupportedKeyTypeInDecoratorKeys()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Decorate<LoggingDecorator, IService>(Keys = new object[] { null, 1 })]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT170*").AsWildcard()
				.Because("an int is not a supported key type");
		}

		[Test]
		public async Task ReportsAnUnsupportedCompositeKeyType()
		{
			GeneratorResult result = Generator.Run(KeyedTypes + """
			                                                    [Container]
			                                                    [Transient<Real, IService>]
			                                                    [Composite<CompositeService, IService>(Key = 1)]
			                                                    public static partial class MyContainer
			                                                    {
			                                                    }
			                                                    """);

			await That(result.Diagnostics).Contains("*AWT170*").AsWildcard()
				.Because("an int is not a supported key type");
		}
	}
}
