using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The registration shapes a module or a base container builds through a generated factory, so a container that
///     cannot construct the implementation still registers it: a module in the same assembly, a module in another
///     assembly with and without <c>InternalsVisibleTo</c>, and a base container with an extending container.
/// </summary>
public class RegistrationFactoryShapeTests
{
	private const string Consumer = """
	                                using Awaiten;
	                                using Lib;

	                                namespace App;

	                                [Container]
	                                [Import(typeof(GreetingModule))]
	                                public static partial class AppContainer
	                                {
	                                }
	                                """;

	private const string KeyedClockConsumer = """
	                                          using Awaiten;
	                                          using Lib;

	                                          namespace App;

	                                          public sealed class UtcClock : IClock { }

	                                          [Container]
	                                          [Import(typeof(GreetingModule))]
	                                          [Singleton<UtcClock, IClock>(Key = "utc")]
	                                          public static partial class AppContainer
	                                          {
	                                          }
	                                          """;

	private const string ClockConsumer = """
	                                     using Awaiten;
	                                     using Lib;

	                                     namespace App;

	                                     public sealed class SystemClock : IClock { }

	                                     [Container]
	                                     [Import(typeof(GreetingModule))]
	                                     [Singleton<SystemClock, IClock>]
	                                     public static partial class AppContainer
	                                     {
	                                     }
	                                     """;

	private static string Granted(string library)
		=> library.Replace("using Awaiten;", "using Awaiten;\n\n[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]");

	private static string ModuleSource(GeneratorResult result)
		=> result.Sources.Single(source => source.Key.StartsWith("Awaiten.ModuleScan.", StringComparison.Ordinal)).Value;

	public class SeveralServices
	{
		private const string CoveringInterface = """
		                                         using Awaiten;

		                                         namespace Lib;

		                                         public interface IGreeter { }
		                                         public interface IFormalGreeter : IGreeter { }
		                                         internal sealed class Greeter : IFormalGreeter { }

		                                         [Module]
		                                         [Singleton<Greeter, IGreeter>]
		                                         [Singleton<Greeter, IFormalGreeter>]
		                                         public static partial class GreetingModule { }
		                                         """;

		[Test]
		public async Task AModule_ReturnsTheRegisteredServiceThatCoversTheOthers()
		{
			GeneratorResult result = Generator.Run(CoveringInterface);

			await That(result.Diagnostics).IsEmpty()
				.Because("one factory returning a type that converts to both services shares one instance");
			await That(ModuleSource(result)).Contains("public static global::Lib.IFormalGreeter Registration_Greeter_");
		}

		[Test]
		public async Task AModule_ReturnsAnAccessibleBaseClassThatCoversTheServices()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IFarewell { }
			                                       public abstract class GreeterBase : IGreeter, IFarewell { }
			                                       internal sealed class Greeter : GreeterBase { }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       [Singleton<Greeter, IFarewell>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("public static global::Lib.GreeterBase Registration_Greeter_");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsImportedWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(CoveringInterface, Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the consumer calls the factory for both services");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsImportedWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(CoveringInterface), Consumer);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task AModuleHidingTheImplementation_BuildsItForAContainerInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IFormalGreeter : IGreeter { }

			                                       [Module]
			                                       [Singleton<GreetingModule.Greeter, IGreeter>]
			                                       [Singleton<GreetingModule.Greeter, IFormalGreeter>]
			                                       public static partial class GreetingModule
			                                       {
			                                           private sealed class Greeter : IFormalGreeter { }
			                                       }

			                                       [Container]
			                                       [Import(typeof(GreetingModule))]
			                                       public static partial class AppContainer { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task WithoutACoveringType_SharesTheInstanceThroughAHandle()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IFarewell { }
			                                       internal sealed class Greeter : IGreeter, IFarewell { }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       [Singleton<Greeter, IFarewell>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Values.Single()).Contains("HiddenRegistration_Greeter_").And.Contains("Alias_Greeter_")
				.Because("no type the factory may return converts to both services, so a handle owns the instance and both services are aliases of it");
		}
	}

	public class InjectionMetadata
	{
		private const string FromKeyModule = """
		                                     using Awaiten;

		                                     namespace Lib;

		                                     public interface IGreeter { }
		                                     public interface IClock { }
		                                     internal sealed class Greeter : IGreeter
		                                     {
		                                         public Greeter([FromKey("utc")] IClock clock) { }
		                                     }

		                                     [Module]
		                                     [Singleton<Greeter, IGreeter>]
		                                     public static partial class GreetingModule { }
		                                     """;

		private const string ArgModule = """
		                                 using Awaiten;

		                                 namespace Lib;

		                                 public interface IGreeter { }
		                                 internal sealed class Greeter : IGreeter
		                                 {
		                                     public Greeter([Arg] string name) { }
		                                 }

		                                 [Module]
		                                 [Transient<Greeter, IGreeter>]
		                                 public static partial class GreetingModule { }
		                                 """;

		private const string InjectModule = """
		                                    using Awaiten;

		                                    namespace Lib;

		                                    public interface IGreeter { }
		                                    public interface IClock { }
		                                    internal sealed class Greeter : IGreeter
		                                    {
		                                        [Inject]
		                                        public IClock Clock { get; set; } = null!;
		                                    }

		                                    [Module]
		                                    [Singleton<Greeter, IGreeter>]
		                                    public static partial class GreetingModule { }
		                                    """;

		[Test]
		public async Task AFromKeyParameter_IsMirroredOntoTheFactory()
		{
			GeneratorResult result = Generator.Run(FromKeyModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("""[global::Awaiten.FromKey("utc")] global::Lib.IClock @clock""")
				.Because("a consumer resolves the factory's parameter under the key the constructor asks for");
		}

		[Test]
		public async Task AnEnumFromKeyParameter_IsMirroredOntoTheFactory()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public enum Zone { Local, Utc, }
			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       internal sealed class Greeter : IGreeter
			                                       {
			                                           public Greeter([FromKey(Zone.Utc)] IClock clock) { }
			                                       }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("[global::Awaiten.FromKey(global::Lib.Zone.Utc)] global::Lib.IClock @clock");
		}

		[Test]
		public async Task AFromKeyParameter_IsHonoredByAConsumerWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(FromKeyModule, KeyedClockConsumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the consumer resolves the mirrored parameter under its key, which it registers");
		}

		[Test]
		public async Task AFromKeyParameter_IsHonoredByAConsumerWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(FromKeyModule), KeyedClockConsumer);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task AnArgParameter_IsMirroredOntoTheFactory()
		{
			GeneratorResult result = Generator.Run(ArgModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("[global::Awaiten.Arg] string @name");
		}

		[Test]
		public async Task AnArgParameter_IsHonoredByAConsumerWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(ArgModule, Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the mirrored [Arg] makes the registration take its runtime argument from the caller");
		}

		[Test]
		public async Task AnInjectProperty_BecomesAFactoryParameter()
		{
			GeneratorResult result = Generator.Run(InjectModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("(global::Lib.IClock @Clock) => new global::Lib.Greeter() { Clock = @Clock };");
		}

		[Test]
		public async Task AnInjectProperty_IsFilledByAConsumerWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(InjectModule, ClockConsumer);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task AnInjectProperty_IsFilledByAConsumerWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InjectModule), ClockConsumer);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task AKeyedInjectProperty_KeepsItsKey()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       internal sealed class Greeter : IGreeter
			                                       {
			                                           [Inject, FromKey("utc")]
			                                           public IClock Clock { get; init; } = null!;
			                                       }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("""([global::Awaiten.FromKey("utc")] global::Lib.IClock @Clock)""");
		}

		[Test]
		public async Task AnInjectPropertyOfAHiddenImplementation_IsFilledByAContainerInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { IClock Clock { get; } }
			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<GreetingModule.Greeter, IGreeter>]
			                                       public static partial class GreetingModule
			                                       {
			                                           private sealed class Greeter : IGreeter
			                                           {
			                                               [Inject]
			                                               public IClock Clock { get; set; } = null!;
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(GreetingModule))]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static partial class AppContainer { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("Clock: ")
				.Because("the container passes the property's value to the module's factory by name");
		}

		[Test]
		public async Task OptionalAndDeferredPropertiesOfAPrivateNestedType_AreFilledThroughTheFactoryInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       public interface IAudit { }
			                                       public sealed class Audit : IAudit { public Audit(IGreeter greeter) { } }

			                                       [Module]
			                                       [Singleton<GreetingModule.Greeter, IGreeter>]
			                                       [Transient<Audit, IAudit>]
			                                       public static partial class GreetingModule
			                                       {
			                                           private sealed class Greeter : IGreeter
			                                           {
			                                               [Inject(Optional = true)] public IClock Clock { get; set; } = null!;
			                                               [Inject(Deferred = true)] public IAudit Audit { get; set; } = null!;
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(GreetingModule))]
			                                       public static partial class GreetingContainer { }
			                                       """);
			string container = result.Sources.Single(source => source.Key.Contains("GreetingContainer")).Value;

			await That(result.Diagnostics).IsEmpty();
			await That(container).Contains("global::Lib.GreetingModule.Internals.Deferred_Registration_Greeter_*_Audit(*").AsWildcard()
				.Because("a container in the module's assembly cannot assign a property of a type private to the module, so it calls the setter wrapper");
		}

		[Test]
		public async Task AnInitOnlyOptionalInjectProperty_StillReportsAwt200()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       internal sealed class Greeter : IGreeter
			                                       {
			                                           [Inject(Optional = true)]
			                                           public IClock? Clock { get; init; }
			                                       }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*Clock*[Inject(Optional = true)]*").AsWildcard()
				.Because("the factory assigns an optional property only after construction, which an init-only accessor does not allow");
		}

		[Test]
		public async Task AnInitOnlyDeferredInjectProperty_StillReportsAwt200()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       internal sealed class Greeter : IGreeter
			                                       {
			                                           [Inject(Deferred = true)]
			                                           public IClock Clock { get; init; } = null!;
			                                       }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*Clock*[Inject(Deferred = true)]*").AsWildcard()
				.Because("a deferred property is assigned through a setter wrapper after construction, which an init-only accessor does not allow");
		}
	}

	public class MemberWrappers
	{
		private const string InternalMembersModule = """
		                                             using Awaiten;

		                                             namespace Lib;

		                                             public interface IClock { }
		                                             public interface IMenu { }
		                                             public interface ILog { }
		                                             internal sealed class Clock : IClock { }
		                                             internal sealed class Menu : IMenu { }

		                                             [Module]
		                                             [Singleton<IClock>(Factory = nameof(CreateClock))]
		                                             [Singleton<IMenu>(Instance = nameof(Menu))]
		                                             public static partial class GreetingModule
		                                             {
		                                                 internal static IMenu Menu { get; } = new Lib.Menu();

		                                                 internal static IClock CreateClock(ILog log) => new Clock();
		                                             }
		                                             """;

		private const string LogConsumer = """
		                                   using Awaiten;
		                                   using Lib;

		                                   namespace App;

		                                   public sealed class Log : ILog { }

		                                   [Container]
		                                   [Import(typeof(GreetingModule))]
		                                   [Singleton<Log, ILog>]
		                                   public static partial class AppContainer
		                                   {
		                                   }
		                                   """;

		[Test]
		public async Task AModule_WrapsItsInternalFactoryAndInstanceMembers()
		{
			GeneratorResult result = Generator.Run(InternalMembersModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result))
				.Contains("public static global::Lib.IClock Factory_CreateClock_")
				.And.Contains("(global::Lib.ILog @log) => CreateClock(@log);")
				.And.Contains("public static global::Lib.IMenu Instance_Menu_")
				.Because("a container in another assembly calls the public wrappers instead of the internal members");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsImportedWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(InternalMembersModule, LogConsumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the consumer calls the wrappers, which it can see, instead of the internal members it cannot");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsImportedWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InternalMembersModule), LogConsumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.GreetingModule.CreateClock(")
				.Because("a consumer that can reach the internal member calls it directly");
		}

		[Test]
		public async Task AMemberWithAnInternalParameterType_GetsNoWrapper()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			                                                                      using Awaiten;

			                                                                      namespace Lib;

			                                                                      public interface IClock { }
			                                                                      internal interface ISource { }
			                                                                      internal sealed class Clock : IClock { }

			                                                                      [Module]
			                                                                      [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                                                      public static partial class GreetingModule
			                                                                      {
			                                                                          internal static IClock CreateClock(ISource source) => new Clock();
			                                                                      }
			                                                                      """, Consumer);

			await That(result.Diagnostics).Contains("*AWT108*CreateClock*").AsWildcard()
				.Because("a public wrapper cannot expose the internal parameter type, so the consumer still cannot call the member");
		}

		[Test]
		public async Task ANonPartialModule_KeepsBuildingWithoutWrappers()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IClock { }
			                                       internal sealed class Clock : IClock { }

			                                       [Module]
			                                       [Singleton<IClock>(Factory = nameof(CreateClock))]
			                                       public static class GreetingModule
			                                       {
			                                           internal static IClock CreateClock() => new Clock();
			                                       }
			                                       """);

			await That(result.Diagnostics).IsEmpty()
				.Because("a module that cannot receive generated members built before wrappers existed and must keep building");
			await That(result.Sources).IsEmpty();
		}

		private const string PrivateMembersBase = """
		                                          using Awaiten;

		                                          namespace Lib;

		                                          public interface IClock { }
		                                          public interface IMenu { }
		                                          public sealed class Clock : IClock { }
		                                          public sealed class Menu : IMenu { }
		                                          public sealed class Grinder { }

		                                          [Container]
		                                          [Singleton<IClock>(Factory = nameof(CreateClock))]
		                                          [Singleton<IMenu>(Instance = nameof(TheMenu))]
		                                          [Singleton<Grinder>(OnActivated = nameof(Calibrate))]
		                                          public static partial class Shop
		                                          {
		                                              private static readonly IMenu TheMenu = new Menu();

		                                              private static IClock CreateClock() => new Clock();

		                                              private static void Calibrate(Grinder grinder, IClock clock) { }
		                                          }
		                                          """;

		[Test]
		public async Task ABaseGrantingInternalsVisibleTo_WrapsItsPrivateMembers()
		{
			GeneratorResult result = Generator.Run(Granted(PrivateMembersBase));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("Shop")).Value)
				.Contains("internal static global::Lib.IClock Factory_CreateClock_")
				.And.Contains("internal static global::Lib.IMenu Instance_TheMenu_")
				.And.Contains("(global::Lib.Grinder @grinder, global::Lib.IClock @clock) => Calibrate(@grinder, @clock);");
		}

		[Test]
		public async Task ABaseNotGrantingInternalsVisibleTo_GeneratesNoWrappers()
		{
			GeneratorResult result = Generator.Run(PrivateMembersBase);

			await That(result.Sources.Single(source => source.Key.Contains("Shop")).Value).DoesNotContain("Internals")
				.Because("no other assembly could call an internal wrapper");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_CallsTheWrappers()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(PrivateMembersBase), """
			                                                                                                   using Awaiten;
			                                                                                                   using Lib;

			                                                                                                   namespace App;

			                                                                                                   [Container(Extends = typeof(Shop))]
			                                                                                                   public static partial class TestShop { }
			                                                                                                   """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the extending container calls the base's internal wrappers instead of its private members");
		}

		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_StillReportsAwt226()
		{
			GeneratorResult result = Generator.Run(Granted(PrivateMembersBase) + """

			                                                                     [Container(Extends = typeof(Shop))]
			                                                                     public static partial class TestShop { }
			                                                                     """);

			await That(result.Diagnostics).Contains("*AWT226*The factory 'Lib.Shop.CreateClock'*").AsWildcard()
				.Because("in the base's own assembly, an internal member already reaches the extending container");
		}
	}

	public class Decorators
	{
		private const string DecoratorModule = """
		                                       using Awaiten;

		                                       namespace Lib;

		                                       public interface IGreeter { }
		                                       public interface ILog { }
		                                       internal sealed class Greeter : IGreeter { }
		                                       internal sealed class LoudGreeter : IGreeter
		                                       {
		                                           public LoudGreeter(IGreeter inner, ILog log) { }
		                                       }

		                                       [Module]
		                                       [Singleton<Greeter, IGreeter>]
		                                       [Decorate<LoudGreeter, IGreeter>]
		                                       public static partial class GreetingModule { }
		                                       """;

		private const string LogConsumer = """
		                                   using Awaiten;
		                                   using Lib;

		                                   namespace App;

		                                   public sealed class Log : ILog { }

		                                   [Container]
		                                   [Import(typeof(GreetingModule))]
		                                   [Singleton<Log, ILog>]
		                                   public static partial class AppContainer
		                                   {
		                                   }
		                                   """;

		[Test]
		public async Task AModule_GeneratesAFactoryThatTakesTheInnerService()
		{
			GeneratorResult result = Generator.Run(DecoratorModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("public static global::Lib.IGreeter Decorator_LoudGreeter_")
				.And.Contains("(global::Lib.IGreeter @inner, global::Lib.ILog @log) => new global::Lib.LoudGreeter(@inner, @log);");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsDecoratedWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(DecoratorModule, LogConsumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the consumer calls the decorator's factory with the inner service instead of reporting AWT230");
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.GreetingModule.Internals.Decorator_LoudGreeter_");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsDecoratedWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(DecoratorModule), LogConsumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("new global::Lib.LoudGreeter(")
				.Because("a consumer that can construct the decorator does so directly");
		}

		[Test]
		public async Task ADecoratorHiddenInTheModule_IsBuiltForAContainerInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public sealed class Greeter : IGreeter { }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       [Decorate<GreetingModule.LoudGreeter, IGreeter>]
			                                       public static partial class GreetingModule
			                                       {
			                                           private sealed class LoudGreeter : IGreeter
			                                           {
			                                               public LoudGreeter(IGreeter inner) { }
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(GreetingModule))]
			                                       public static partial class AppContainer { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.GreetingModule.Internals.Decorator_LoudGreeter_");
		}

		[Test]
		public async Task ADecoratorWithAnInternalParameterType_StillReportsAwt230()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			                                                                      using Awaiten;

			                                                                      namespace Lib;

			                                                                      public interface IGreeter { }
			                                                                      internal interface ILog { }
			                                                                      public sealed class Greeter : IGreeter { }
			                                                                      internal sealed class LoudGreeter : IGreeter
			                                                                      {
			                                                                          public LoudGreeter(IGreeter inner, ILog log) { }
			                                                                      }

			                                                                      [Module]
			                                                                      [Singleton<Greeter, IGreeter>]
			                                                                      [Decorate<LoudGreeter, IGreeter>]
			                                                                      public static partial class GreetingModule { }
			                                                                      """, Consumer);

			await That(result.Diagnostics).Contains("*AWT230*GreetingModule*the decorator 'Lib.LoudGreeter'*").AsWildcard()
				.Because("the module cannot expose the internal parameter type on a factory, so it generates none");
		}

		private const string PrivateDecoratorBase = """
		                                            using Awaiten;

		                                            namespace Lib;

		                                            public interface IGreeter { }
		                                            public sealed class Greeter : IGreeter { }

		                                            [Container]
		                                            [Singleton<Greeter, IGreeter>]
		                                            [Decorate<Shop.LoudGreeter, IGreeter>]
		                                            public static partial class Shop
		                                            {
		                                                private sealed class LoudGreeter : IGreeter
		                                                {
		                                                    public LoudGreeter(IGreeter inner) { }
		                                                }
		                                            }
		                                            """;

		[Test]
		public async Task ABase_GeneratesAFactoryForItsPrivateDecorator()
		{
			GeneratorResult result = Generator.Run(PrivateDecoratorBase);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("Shop")).Value)
				.Contains("internal static global::Lib.IGreeter Decorator_LoudGreeter_");
		}

		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_BuildsThePrivateDecorator()
		{
			GeneratorResult result = Generator.Run(PrivateDecoratorBase + """

			                                                              [Container(Extends = typeof(Shop))]
			                                                              public static partial class TestShop { }
			                                                              """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the extending container wraps the inherited registration through the base's decorator factory");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_BuildsThePrivateDecorator()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(PrivateDecoratorBase), """
			                                                                                                     using Awaiten;
			                                                                                                     using Lib;

			                                                                                                     namespace App;

			                                                                                                     [Container(Extends = typeof(Shop))]
			                                                                                                     public static partial class TestShop { }
			                                                                                                     """);

			await That(result.Diagnostics).IsEmpty();
		}
	}

	public class Composites
	{
		private const string CompositeModule = """
		                                       using System.Collections.Generic;
		                                       using Awaiten;

		                                       namespace Lib;

		                                       public interface IGreeter { }
		                                       internal sealed class Greeter : IGreeter { }
		                                       internal sealed class Farewell : IGreeter { }
		                                       internal sealed class AllGreeters : IGreeter
		                                       {
		                                           public AllGreeters(IEnumerable<IGreeter> greeters) { }
		                                       }

		                                       [Module]
		                                       [Singleton<Greeter, IGreeter>]
		                                       [Singleton<Farewell, IGreeter>]
		                                       [Composite<AllGreeters, IGreeter>]
		                                       public static partial class GreetingModule { }
		                                       """;

		[Test]
		public async Task AModule_GeneratesAFactoryThatTakesTheCollection()
		{
			GeneratorResult result = Generator.Run(CompositeModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("public static global::Lib.IGreeter Composite_AllGreeters_")
				.And.Contains("(global::System.Collections.Generic.IEnumerable<global::Lib.IGreeter> @greeters) => new global::Lib.AllGreeters(@greeters);");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsComposedWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(CompositeModule, Consumer);

			await That(result.Diagnostics).IsEmpty()
				.Because("the consumer calls the composite's factory with the collection instead of reporting AWT230");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsComposedWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(CompositeModule), Consumer);

			await That(result.Diagnostics).IsEmpty();
		}

		[Test]
		public async Task ACompositeHiddenInTheModule_IsBuiltForAContainerInTheSameAssembly()
		{
			GeneratorResult result = Generator.Run("""
			                                       using System.Collections.Generic;
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public sealed class Greeter : IGreeter { }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       [Composite<GreetingModule.AllGreeters, IGreeter>]
			                                       public static partial class GreetingModule
			                                       {
			                                           private sealed class AllGreeters : IGreeter
			                                           {
			                                               public AllGreeters(IEnumerable<IGreeter> greeters) { }
			                                           }
			                                       }

			                                       [Container]
			                                       [Import(typeof(GreetingModule))]
			                                       public static partial class AppContainer { }
			                                       """);

			await That(result.Diagnostics).IsEmpty();
		}

		private const string PrivateCompositeBase = """
		                                            using System.Collections.Generic;
		                                            using Awaiten;

		                                            namespace Lib;

		                                            public interface IGreeter { }
		                                            public sealed class Greeter : IGreeter { }

		                                            [Container]
		                                            [Singleton<Greeter, IGreeter>]
		                                            [Composite<Shop.AllGreeters, IGreeter>]
		                                            public static partial class Shop
		                                            {
		                                                private sealed class AllGreeters : IGreeter
		                                                {
		                                                    public AllGreeters(IEnumerable<IGreeter> greeters) { }
		                                                }
		                                            }
		                                            """;

		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_BuildsThePrivateComposite()
		{
			GeneratorResult result = Generator.Run(PrivateCompositeBase + """

			                                                              [Container(Extends = typeof(Shop))]
			                                                              public static partial class TestShop { }
			                                                              """);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.EndsWith("Lib.Shop.g.cs", StringComparison.Ordinal)).Value)
				.Contains("internal static global::Lib.IGreeter Composite_AllGreeters_");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_BuildsThePrivateComposite()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(PrivateCompositeBase), """
			                                                                                                     using Awaiten;
			                                                                                                     using Lib;

			                                                                                                     namespace App;

			                                                                                                     [Container(Extends = typeof(Shop))]
			                                                                                                     public static partial class TestShop { }
			                                                                                                     """);

			await That(result.Diagnostics).IsEmpty();
		}
	}

	public class Messages
	{
		[Test]
		public async Task AnInaccessibleMatchOfAContainerScan_SuggestsAModuleInItsAssembly()
		{
			GeneratorResult result = Generator.RunWithReferencedAssembly("""
			                                                            namespace Lib;

			                                                            public interface IPlugin { }
			                                                            internal sealed class Roaster : IPlugin { }
			                                                            """, """
			                                                                 using Awaiten;
			                                                                 using Lib;

			                                                                 namespace App;

			                                                                 [Container]
			                                                                 [Scan<IPlugin>(As = ScanAs.Marker, InAssembliesOf = [typeof(IPlugin)])]
			                                                                 public static partial class AppContainer { }
			                                                                 """);

			await That(result.Diagnostics).Contains("*warning AWT193*'Lib.Roaster'*not accessible outside its assembly 'ReferencedAssembly'*move the scan into a [Module] in 'ReferencedAssembly'*").AsWildcard()
				.Because("a module of the type's assembly builds its internal types for any container that imports it");
		}

		[Test]
		public async Task AHookParameterPrivateToTheModule_NamesTheRealReason()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       internal sealed class Greeter : IGreeter { }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate))]
			                                       public static partial class GreetingModule
			                                       {
			                                           private sealed class Log { }

			                                           internal static void Activate(Greeter greeter, Log log) { }
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT203*Lib.Greeter*OnActivated hook parameter 'log' needs 'Lib.GreetingModule.Log', which is private to the module*depend on an accessible service*").AsWildcard()
				.Because("widening a type that is meant to stay private is not the remedy");
		}

		private const string InternalModule = """
		                                      using Awaiten;

		                                      namespace Lib;

		                                      public interface IGreeter { }
		                                      internal interface IClock { }
		                                      internal sealed class Clock : IClock { }

		                                      [Module]
		                                      [Singleton<Clock, IClock>]
		                                      [Singleton<GreetingModule.Greeter, IGreeter>]
		                                      internal static partial class GreetingModule
		                                      {
		                                          private sealed class Greeter : IGreeter
		                                          {
		                                              public Greeter(IClock clock) { }
		                                          }
		                                      }
		                                      """;

		[Test]
		public async Task AnInternalModule_TakesInternalParameterTypesOnItsFactory()
		{
			GeneratorResult result = Generator.Run(InternalModule + """

			                                                        [Container]
			                                                        [Import(typeof(GreetingModule))]
			                                                        internal static partial class AppContainer { }
			                                                        """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the factory of an internal module is reached only where the internal parameter type is too");
			await That(ModuleSource(result)).Contains("(global::Lib.IClock @clock) => new global::Lib.GreetingModule.Greeter(@clock);");
		}

		[Test]
		public async Task AnInternalModule_IsImportedByAnAssemblyGrantedInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InternalModule), """
			                                                                                               using Awaiten;
			                                                                                               using Lib;

			                                                                                               namespace App;

			                                                                                               [Container]
			                                                                                               [Import(typeof(GreetingModule))]
			                                                                                               internal static partial class AppContainer { }
			                                                                                               """);

			await That(result.Diagnostics).IsEmpty();
		}
	}

	public class InternalModules
	{
		private const string InternalHookModule = """
		                                          using Awaiten;

		                                          namespace Lib;

		                                          public interface IPlugin { }
		                                          public interface IBell { }
		                                          internal interface ILog { }
		                                          internal sealed class Log : ILog { }
		                                          internal sealed class Roaster : IPlugin { }

		                                          [Module]
		                                          [Singleton<Log, ILog>]
		                                          [Singleton<PluginModule.Bell, IBell>(OnActivated = nameof(Ring))]
		                                          [Scan<IPlugin>(As = ScanAs.Marker, OnActivated = nameof(Wire))]
		                                          internal static partial class PluginModule
		                                          {
		                                              private sealed class Bell : IBell { }

		                                              private static void Ring(Bell bell, ILog log) { }

		                                              internal static void Wire(Roaster roaster, ILog log) { }
		                                          }
		                                          """;

		private const string InternalConsumer = """
		                                        using Awaiten;
		                                        using Lib;

		                                        namespace App;

		                                        [Container]
		                                        [Import(typeof(PluginModule))]
		                                        internal static partial class AppContainer { }
		                                        """;

		[Test]
		public async Task AnInternalModule_TakesInternalHookParameterTypesOnItsWrappers()
		{
			GeneratorResult result = Generator.Run(InternalHookModule + """

			                                                            [Container]
			                                                            [Import(typeof(PluginModule))]
			                                                            internal static partial class AppContainer { }
			                                                            """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the wrappers of an internal module are reached only where the internal parameter type is too");
			await That(ModuleSource(result))
				.Contains("(global::Lib.IPlugin @awaiten__instance, global::Lib.ILog @log) => Wire((global::Lib.Roaster)@awaiten__instance, @log);")
				.And.Contains("(global::Lib.IBell @awaiten__instance, global::Lib.ILog @log) => Ring((global::Lib.PluginModule.Bell)@awaiten__instance, @log);");
		}

		[Test]
		public async Task AnInternalModule_WithInternalHookParameterTypes_IsImportedByAnAssemblyGrantedInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InternalHookModule), InternalConsumer);

			await That(result.Diagnostics).IsEmpty();
		}

		private const string InternalExposureModule = """
		                                              using Awaiten;

		                                              namespace Lib;

		                                              internal interface IPlugin { }
		                                              internal interface IRoaster { }
		                                              internal sealed class Roaster : IPlugin, IRoaster { }

		                                              [Module]
		                                              [Scan<IPlugin>(As = ScanAs.MatchingInterface)]
		                                              internal static partial class PluginModule;
		                                              """;

		[Test]
		public async Task AnInternalModule_ExposesAMatchUnderAnInternalInterface()
		{
			GeneratorResult result = Generator.Run(InternalExposureModule + """

			                                                                [Container]
			                                                                [Import(typeof(PluginModule))]
			                                                                internal static partial class AppContainer { }
			                                                                """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an internal module is imported only where its internal interfaces can be named, so they are no reason for AWT196");
			await That(ModuleSource(result)).Contains("GeneratedScanRegistrationAttribute<global::Lib.IRoaster>");
		}

		[Test]
		public async Task AnInternalModule_ExposesAnInternalMatchAsItself()
		{
			GeneratorResult result = Generator.Run(InternalExposureModule.Replace("ScanAs.MatchingInterface", "ScanAs.Self"));

			await That(result.Diagnostics).IsEmpty()
				.Because("an internal module may register an internal match as itself");
			await That(ModuleSource(result)).Contains("GeneratedScanRegistrationAttribute<global::Lib.Roaster>");
		}

		[Test]
		public async Task AnInternalModule_WithInternalExposures_IsImportedByAnAssemblyGrantedInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InternalExposureModule), InternalConsumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.PluginModule.Internals.Scan_Roaster_")
				.Because("the consumer resolves the match under the internal interface through the module's factory");
		}

		[Test]
		public async Task APublicModule_StillSkipsAMatchWithOnlyInternalInterfaces()
		{
			GeneratorResult result = Generator.Run(InternalExposureModule.Replace("internal static partial class PluginModule", "public static partial class PluginModule"));

			await That(result.Diagnostics).Contains("*AWT196*Lib.Roaster*").AsWildcard()
				.Because("a container in another assembly could not name the internal interface");
		}

		private const string InternalConventionModule = """
		                                                using System.Threading;
		                                                using System.Threading.Tasks;
		                                                using Awaiten;

		                                                namespace Lib;

		                                                internal interface IPlugin { }
		                                                internal interface ILog { }
		                                                internal interface IStartable { Task StartAsync(ILog log, CancellationToken cancellationToken); }
		                                                internal sealed class Log : ILog { }
		                                                internal sealed class Roaster : IPlugin, IStartable
		                                                {
		                                                    public Task StartAsync(ILog log, CancellationToken cancellationToken) => Task.CompletedTask;
		                                                }

		                                                [Module]
		                                                [AsyncInitializer<IStartable>(nameof(IStartable.StartAsync))]
		                                                [Singleton<Log, ILog>]
		                                                [Scan<IPlugin>(As = ScanAs.Marker)]
		                                                internal static partial class PluginModule;
		                                                """;

		[Test]
		public async Task AnInternalModule_InitializesItsMatchesWithAnInternalConvention()
		{
			GeneratorResult result = Generator.Run(InternalConventionModule + """

			                                                                  [Container]
			                                                                  [Import(typeof(PluginModule))]
			                                                                  internal static partial class AppContainer { }
			                                                                  """);

			await That(result.Diagnostics).IsEmpty()
				.Because("an internal module is imported only where its internal convention and the convention's parameter types can be named, so they are no reason for AWT234");
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("await ((global::Lib.IStartable)");
		}

		[Test]
		public async Task AnInternalModule_WithAnInternalConvention_IsImportedByAnAssemblyGrantedInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InternalConventionModule), InternalConsumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value)
				.Contains("global::Lib.PluginModule.Internals.Scan_Roaster_").And.Contains("await ((global::Lib.IStartable)")
				.Because("the module builds the match through its factory instead of leaving it to the friend, which runs the internal convention on it");
		}
	}

	public class FactoryMemberHooks
	{
		private const string HookedFactoryModule = """
		                                           using Awaiten;

		                                           namespace Lib;

		                                           public interface IClock { }
		                                           public interface IKettle { }
		                                           public interface IMenu { }
		                                           internal sealed class Kettle : IKettle { }
		                                           internal sealed class Menu : IMenu { }

		                                           [Module]
		                                           [Singleton<IKettle>(Factory = nameof(CreateKettle), OnActivated = nameof(Boil))]
		                                           [Singleton<IMenu>(Factory = nameof(CreateMenu), OnRelease = nameof(Clear))]
		                                           public static partial class GreetingModule
		                                           {
		                                               public static IKettle CreateKettle() => new Kettle();

		                                               internal static IMenu CreateMenu() => new Menu();

		                                               internal static void Boil(IKettle kettle, IClock clock) { }

		                                               internal static void Clear(IMenu menu) { }
		                                           }
		                                           """;

		[Test]
		public async Task AModule_WrapsTheInternalHooksOfFactoryRegistrations()
		{
			GeneratorResult result = Generator.Run(HookedFactoryModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result))
				.Contains("public static void Hook_OnActivated_IKettle_*(global::Lib.IKettle @awaiten__instance, global::Lib.IClock @clock) => Boil(@awaiten__instance, @clock);").AsWildcard()
				.And.Contains("public static void Hook_OnRelease_IMenu_*(global::Lib.IMenu @awaiten__instance) => Clear(@awaiten__instance);").AsWildcard()
				.Because("a container in another assembly calls the public wrappers instead of the internal hooks");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsImportedWithoutInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(HookedFactoryModule, ClockConsumer);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources.Single(source => source.Key.Contains("AppContainer")).Value;
			await That(source).Contains("global::Lib.GreetingModule.Internals.Hook_OnActivated_IKettle_").And.Contains("global::Lib.GreetingModule.Internals.Hook_OnRelease_IMenu_")
				.Because("the consumer runs the wrappers, which it can see, instead of the internal hooks it cannot");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_IsImportedWithInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(HookedFactoryModule), ClockConsumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.GreetingModule.Boil(")
				.Because("a consumer that can reach the internal hook calls it directly");
		}

		private const string InternalParameterHookModule = """
		                                                   using Awaiten;

		                                                   namespace Lib;

		                                                   public interface IKettle { }
		                                                   internal sealed class Log { }
		                                                   internal sealed class Kettle : IKettle { }

		                                                   [Module]
		                                                   [Singleton<Log>]
		                                                   [Singleton<IKettle>(Factory = nameof(CreateKettle), OnActivated = nameof(Boil))]
		                                                   public static partial class GreetingModule
		                                                   {
		                                                       public static IKettle CreateKettle() => new Kettle();

		                                                       internal static void Boil(IKettle kettle, Log log) { }
		                                                   }
		                                                   """;

		[Test]
		public async Task AHookWithAnInternalParameterType_ReportsAwt203()
		{
			GeneratorResult result = Generator.Run(InternalParameterHookModule);

			await That(result.Diagnostics).Contains("*AWT203*OnActivated*Lib.Log*").AsWildcard()
				.Because("a public wrapper cannot expose the internal parameter type");
		}

		[Test]
		public async Task AHookWithAnInternalParameterType_IsCalledDirectlyByAnAssemblyGrantedInternalsVisibleTo()
		{
			GeneratorResult library = Generator.Run(Granted(InternalParameterHookModule));
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(InternalParameterHookModule), Consumer);

			await That(library.Diagnostics).IsEmpty()
				.Because("an assembly granted InternalsVisibleTo calls the hook directly, so the module needs no wrapper");
			await That(ModuleSource(library)).Contains("public const string HookSkipped_OnActivated_IKettle_").And.DoesNotContain("Hook_OnActivated_IKettle_");
			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.GreetingModule.Boil(");
		}

		[Test]
		public async Task AHookWithAnInternalParameterType_ReportsAwt231ToAnAssemblyNotGrantedInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(
				Granted(InternalParameterHookModule).Replace("\"TestAssembly\"", "\"OtherAssembly\""), Consumer);

			await That(result.Diagnostics).Contains("*AWT231*Lib.GreetingModule*OnActivated hook 'Boil'*Lib.Log*").AsWildcard()
				.Because("the consumer can neither call the internal hook nor a wrapper the module did not generate");
		}

		private const string AsyncHookedFactoryModule = """
		                                                using System.Threading;
		                                                using System.Threading.Tasks;
		                                                using Awaiten;

		                                                namespace Lib;

		                                                public interface IKettle { }
		                                                internal sealed class Kettle : IKettle { }

		                                                [Module]
		                                                [Singleton<IKettle>(Factory = nameof(CreateKettle), OnActivated = nameof(BoilAsync))]
		                                                public static partial class GreetingModule
		                                                {
		                                                    public static IKettle CreateKettle() => new Kettle();

		                                                    internal static Task BoilAsync(IKettle kettle, CancellationToken cancellationToken) => Task.CompletedTask;
		                                                }
		                                                """;

		[Test]
		public async Task AModule_WrapsTheInternalAsyncHookOfAFactoryRegistrationWithItsTaskAndToken()
		{
			GeneratorResult result = Generator.Run(AsyncHookedFactoryModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result))
				.Contains("public static global::System.Threading.Tasks.Task Hook_OnActivated_IKettle_*(global::Lib.IKettle @awaiten__instance, global::System.Threading.CancellationToken @cancellationToken) => BoilAsync(@awaiten__instance, @cancellationToken);").AsWildcard();
		}

		[Test]
		public async Task AModuleInAnotherAssembly_AwaitsTheWrappedHookWithTheResolveToken()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(AsyncHookedFactoryModule, Consumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value)
				.Contains("await global::Lib.GreetingModule.Internals.Hook_OnActivated_IKettle_*(*, cancellationToken)").AsWildcard();
		}

		private const string ConventionFactoryModule = """
		                                               using System.Threading;
		                                               using System.Threading.Tasks;
		                                               using Awaiten;

		                                               namespace Lib;

		                                               public interface IClock { }
		                                               public abstract class Appliance { public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask; }
		                                               public sealed class Kettle : Appliance { internal Kettle() { } }

		                                               [Module]
		                                               [AsyncInitializer<Appliance>(nameof(Appliance.StartAsync))]
		                                               [Singleton<Kettle>(Factory = nameof(CreateKettle), OnActivated = nameof(Boil))]
		                                               public static partial class GreetingModule
		                                               {
		                                                   public static Kettle CreateKettle() => new Kettle();

		                                                   internal static void Boil(Kettle kettle, IClock clock) { }
		                                               }
		                                               """;

		[Test]
		public async Task AModuleInAnotherAssembly_InitializesAFactoryRegistrationWithItsConvention()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(ConventionFactoryModule.Replace(", OnActivated = nameof(Boil)", ""), ClockConsumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("await ((global::Lib.Appliance)")
				.Because("the module's convention judges what the module's factory declares it returns");
		}

		[Test]
		public async Task AModuleInAnotherAssembly_InitializesAFactoryRegistrationWithItsConventionAfterTheWrappedHook()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(ConventionFactoryModule, ClockConsumer);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources.Single(source => source.Key.Contains("AppContainer")).Value;
			int hook = source.IndexOf("global::Lib.GreetingModule.Internals.Hook_OnActivated_Kettle_", StringComparison.Ordinal);
			int convention = source.IndexOf("await ((global::Lib.Appliance)", StringComparison.Ordinal);
			await That(hook).IsGreaterThan(-1)
				.Because("the consumer runs the internal hook through the module's wrapper");
			await That(convention).IsGreaterThan(hook)
				.Because("the module's convention still initializes the registration, after its wrapped hook");
		}

		[Test]
		public async Task AnInternalConventionOfAFactoryRegistrationWithAWrappedHook_ReportsAwt234AtTheContainer()
		{
			string library = ConventionFactoryModule.Replace("public virtual Task StartAsync", "internal virtual Task StartAsync");
			GeneratorResult own = Generator.Run(library);
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(library, ClockConsumer);

			await That(own.Diagnostics).IsEmpty()
				.Because("the container constructs a Factory registration itself, so the container judges the convention, as for any registration it builds");
			await That(ModuleSource(own)).Contains("Hook_OnActivated_Kettle_");
			await That(result.Diagnostics).HasSingle().Matching(diagnostic => diagnostic.Contains("AWT234") && diagnostic.Contains("not accessible from the container"))
				.Because("the hook wrapper makes the hook callable, but the container still cannot call the internal convention");
		}

		private const string SkippedHookConventionModule = """
		                                                   using System.Threading;
		                                                   using System.Threading.Tasks;
		                                                   using Awaiten;

		                                                   namespace Lib;

		                                                   public abstract class Appliance { internal virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask; }
		                                                   public sealed class Kettle : Appliance { internal Kettle() { } }
		                                                   internal sealed class Log { }

		                                                   [Module]
		                                                   [AsyncInitializer<Appliance>(nameof(Appliance.StartAsync))]
		                                                   [Singleton<Log>]
		                                                   [Singleton<Kettle>(Factory = nameof(CreateKettle), OnActivated = nameof(Boil))]
		                                                   public static partial class GreetingModule
		                                                   {
		                                                       public static Kettle CreateKettle() => new Kettle();

		                                                       internal static void Boil(Kettle kettle, Log log) { }
		                                                   }
		                                                   """;

		[Test]
		public async Task AnInternalConventionOfAFactoryRegistrationWithASkippedHook_IsCalledByAnAssemblyGrantedInternalsVisibleTo()
		{
			GeneratorResult library = Generator.Run(Granted(SkippedHookConventionModule));
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(SkippedHookConventionModule), Consumer);

			await That(library.Diagnostics).IsEmpty();
			await That(ModuleSource(library)).Contains("HookSkipped_OnActivated_Kettle_");
			await That(result.Diagnostics).IsEmpty()
				.Because("an assembly granted InternalsVisibleTo calls both the internal hook and the internal convention directly");
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value)
				.Contains("global::Lib.GreetingModule.Boil(").And.Contains("await ((global::Lib.Appliance)");
		}

		[Test]
		public async Task AnInternalConventionOfAFactoryRegistrationWithASkippedHook_ReportsOnlyAwt231ToAnAssemblyNotGrantedInternalsVisibleTo()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(
				Granted(SkippedHookConventionModule).Replace("\"TestAssembly\"", "\"OtherAssembly\""), Consumer);

			await That(result.Diagnostics).Contains("*AWT231*Lib.GreetingModule*OnActivated hook 'Boil'*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT234*").AsWildcard()
				.Because("the container leaves out the registration whose hook it cannot call, so it never judges the registration's convention");
		}

		private const string SharedHookModule = """
		                                        using Awaiten;

		                                        namespace Lib;

		                                        public sealed class Kettle { internal Kettle() { } }

		                                        [Module]
		                                        [Singleton<Kettle>(Key = "made", Factory = nameof(CreateKettle), OnActivated = nameof(Boil))]
		                                        [Singleton<Kettle>(Key = "built", OnActivated = nameof(Boil))]
		                                        public static partial class GreetingModule
		                                        {
		                                            public static Kettle CreateKettle() => new Kettle();

		                                            internal static void Boil(Kettle kettle) { }
		                                        }
		                                        """;

		[Test]
		public async Task AFactoryAndASelfCompiledRegistrationWithTheSameHook_ShareOneWrapper()
		{
			GeneratorResult result = Generator.Run(SharedHookModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result)).Contains("public static void Hook_OnActivated_Kettle_").Exactly(1)
				.Because("both registrations wrap the same hook of the same type under one name, so the module emits the wrapper once");
		}

		[Test]
		public async Task AFactoryAndASelfCompiledRegistrationWithTheSameHook_BothRunTheSharedWrapperInAnotherAssembly()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(SharedHookModule, Consumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value).Contains("global::Lib.GreetingModule.Internals.Hook_OnActivated_Kettle_").Exactly(2)
				.Because("the consumer runs the shared wrapper for the Factory registration and for the self-compiled one");
		}
	}

	public class BaseContainer
	{
		private const string PrivateBase = """
		                                   using Awaiten;

		                                   namespace Lib;

		                                   public interface IGreeter { }
		                                   public interface IClock { }
		                                   public sealed class UtcClock : IClock { }
		                                   public sealed class SystemClock : IClock { }

		                                   [Container]
		                                   [Singleton<UtcClock, IClock>(Key = "utc")]
		                                   [Singleton<SystemClock, IClock>]
		                                   [Transient<Shop.Greeter, IGreeter>]
		                                   public static partial class Shop
		                                   {
		                                       private sealed class Greeter : IGreeter
		                                       {
		                                           public Greeter([FromKey("utc")] IClock clock) { }

		                                           [Inject]
		                                           public IClock Clock { get; set; } = null!;
		                                       }
		                                   }
		                                   """;

		[Test]
		public async Task TheBase_MirrorsFromKeyAndInjectedPropertiesOntoItsFactory()
		{
			GeneratorResult result = Generator.Run(PrivateBase);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("Shop")).Value)
				.Contains("""internal static global::Lib.IGreeter Registration_Greeter_""")
				.And.Contains("""([global::Awaiten.FromKey("utc")] global::Lib.IClock @clock, global::Lib.IClock @Clock) => new global::Lib.Shop.Greeter(@clock) { Clock = @Clock };""");
		}

		[Test]
		public async Task AnExtendingContainerInTheSameAssembly_BuildsThePrivateImplementation()
		{
			GeneratorResult result = Generator.Run(PrivateBase + """

			                                                     [Container(Extends = typeof(Shop))]
			                                                     public static partial class TestShop { }
			                                                     """);

			await That(result.Diagnostics).IsEmpty()
				.Because("the base's factory takes the keyed parameter and the injected property");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_BuildsThePrivateImplementation()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(PrivateBase), """
			                                                                                               using Awaiten;
			                                                                                               using Lib;

			                                                                                               namespace App;

			                                                                                               [Container(Extends = typeof(Shop))]
			                                                                                               public static partial class TestShop { }
			                                                                                               """);

			await That(result.Diagnostics).IsEmpty();
		}
	}

	public class AsyncMembers
	{
		private const string AsyncPrivateMembersBase = """
		                                               using System.Threading;
		                                               using System.Threading.Tasks;
		                                               using Awaiten;

		                                               namespace Lib;

		                                               public interface IClock { }
		                                               public sealed class Clock : IClock { }
		                                               public sealed class Grinder { }

		                                               [Container]
		                                               [Singleton<IClock>(Factory = nameof(CreateClockAsync))]
		                                               [Singleton<Grinder>(OnActivated = nameof(CalibrateAsync))]
		                                               public static partial class Shop
		                                               {
		                                                   private static Task<IClock> CreateClockAsync(CancellationToken cancellationToken) => Task.FromResult<IClock>(new Clock());

		                                                   private static Task CalibrateAsync(Grinder grinder, CancellationToken cancellationToken) => Task.CompletedTask;
		                                               }
		                                               """;

		private const string ExtendingShop = """
		                                     using System;
		                                     using Awaiten;
		                                     using Lib;

		                                     namespace App;

		                                     [Container(Extends = typeof(Shop))]
		                                     public static partial class TestShop { }
		                                     """;

		[Test]
		public async Task ABase_WrapsItsPrivateAsyncFactoryAndHookWithTheirTasksAndTokens()
		{
			GeneratorResult result = Generator.Run(Granted(AsyncPrivateMembersBase));

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("Shop")).Value)
				.Contains("internal static global::System.Threading.Tasks.Task<global::Lib.IClock> Factory_CreateClockAsync_*(global::System.Threading.CancellationToken @cancellationToken) => CreateClockAsync(@cancellationToken);").AsWildcard()
				.And.Contains("internal static global::System.Threading.Tasks.Task Hook_OnActivated_Grinder_*(global::Lib.Grinder @grinder, global::System.Threading.CancellationToken @cancellationToken) => CalibrateAsync(@grinder, @cancellationToken);").AsWildcard()
				.Because("a wrapper returns the member's task and forwards the token, so the extending container awaits it with its own");
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_AwaitsTheWrappersWithTheResolveToken()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(AsyncPrivateMembersBase), ExtendingShop);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources.Single(source => source.Key.Contains("TestShop")).Value;
			await That(source).Contains("await global::Lib.Shop.Internals.Factory_CreateClockAsync_*(cancellationToken)").AsWildcard();
			await That(source).Contains("await global::Lib.Shop.Internals.Hook_OnActivated_Grinder_*(*, cancellationToken)").AsWildcard();
		}

		[Test]
		public async Task AnExtendingContainerInAnotherAssembly_TreatsTheWrappedAsyncHookAsAsyncActivation()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(AsyncPrivateMembersBase), ExtendingShop.Replace(
				"[Container(Extends = typeof(Shop))]",
				"public sealed class Barista { public Barista(Func<Grinder> grinder) { } }\n\n[Container(Extends = typeof(Shop))]\n[Singleton<Barista>]"));

			await That(result.Diagnostics).Contains("*AWT119*").AsWildcard()
				.Because("the extending container reads the asynchronous activation off the wrapper's Task return type");
		}

		private const string AsyncInternalFactoryModule = """
		                                                  using System.Threading;
		                                                  using System.Threading.Tasks;
		                                                  using Awaiten;

		                                                  namespace Lib;

		                                                  public interface IClock { }
		                                                  internal sealed class Clock : IClock { }

		                                                  [Module]
		                                                  [Singleton<IClock>(Factory = nameof(CreateClockAsync))]
		                                                  public static partial class GreetingModule
		                                                  {
		                                                      internal static Task<IClock> CreateClockAsync(CancellationToken cancellationToken) => Task.FromResult<IClock>(new Clock());
		                                                  }
		                                                  """;

		[Test]
		public async Task AModule_WrapsItsInternalAsyncFactoryWithItsTaskAndToken()
		{
			GeneratorResult result = Generator.Run(AsyncInternalFactoryModule);

			await That(result.Diagnostics).IsEmpty();
			await That(ModuleSource(result))
				.Contains("public static global::System.Threading.Tasks.Task<global::Lib.IClock> Factory_CreateClockAsync_*(global::System.Threading.CancellationToken @cancellationToken) => CreateClockAsync(@cancellationToken);").AsWildcard();
		}

		[Test]
		public async Task AModuleInAnotherAssembly_AwaitsTheWrappedFactoryWithTheResolveToken()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(AsyncInternalFactoryModule, Consumer);

			await That(result.Diagnostics).IsEmpty();
			await That(result.Sources.Single(source => source.Key.Contains("AppContainer")).Value)
				.Contains("await global::Lib.GreetingModule.Internals.Factory_CreateClockAsync_*(cancellationToken)").AsWildcard();
		}

		private const string AsyncHookedDecoratorBase = """
		                                                using System.Collections.Generic;
		                                                using System.Threading;
		                                                using System.Threading.Tasks;
		                                                using Awaiten;

		                                                namespace Lib;

		                                                public interface IGreeter { }
		                                                public sealed class Greeter : IGreeter { }
		                                                public sealed class Farewell : IGreeter { }

		                                                [Container]
		                                                [Singleton<Greeter, IGreeter>(OnActivated = nameof(WarmUpAsync))]
		                                                [Decorate<Shop.LoudGreeter, IGreeter>]
		                                                public static partial class Shop
		                                                {
		                                                    private static Task WarmUpAsync(Greeter greeter, CancellationToken cancellationToken) => Task.CompletedTask;

		                                                    private sealed class LoudGreeter : IGreeter
		                                                    {
		                                                        public LoudGreeter(IGreeter inner) { }
		                                                    }
		                                                }
		                                                """;

		[Test]
		public async Task APrivateDecoratorOfABase_IsAsyncWhenTheServiceItWrapsIsAsyncActivated()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(AsyncHookedDecoratorBase), ExtendingShop.Replace(
				"[Container(Extends = typeof(Shop))]",
				"public sealed class Barista { public Barista(Func<IGreeter> greeter) { } }\n\n[Container(Extends = typeof(Shop))]\n[Singleton<Barista>]"));

			await That(result.Diagnostics).Contains("*AWT120*").AsWildcard()
				.Because("the decorator built through the base's factory wraps the asynchronously activated greeter, so it is async too");
		}

		[Test]
		public async Task APrivateDecoratorOfABase_AwaitsTheInnerHookBeforeCallingItsFactory()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(AsyncHookedDecoratorBase), ExtendingShop);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources.Single(source => source.Key.Contains("TestShop")).Value;
			await That(source).Contains("await global::Lib.Shop.Internals.Hook_OnActivated_Greeter_*(*, cancellationToken)").AsWildcard();
			await That(source).Contains("global::Lib.Shop.Internals.Decorator_LoudGreeter_").AsWildcard();
		}

		private const string AsyncHookedCompositeBase = """
		                                                using System.Collections.Generic;
		                                                using System.Threading;
		                                                using System.Threading.Tasks;
		                                                using Awaiten;

		                                                namespace Lib;

		                                                public interface IGreeter { }
		                                                public sealed class Greeter : IGreeter { }
		                                                public sealed class Farewell : IGreeter { }

		                                                [Container(AsyncSafety = AsyncSafety.SyncAfterInit)]
		                                                [Singleton<Greeter, IGreeter>(OnActivated = nameof(WarmUpAsync))]
		                                                [Singleton<Farewell, IGreeter>]
		                                                [Composite<Shop.AllGreeters, IGreeter>]
		                                                public static partial class Shop
		                                                {
		                                                    private static Task WarmUpAsync(Greeter greeter, CancellationToken cancellationToken) => Task.CompletedTask;

		                                                    private sealed class AllGreeters : IGreeter
		                                                    {
		                                                        public AllGreeters(IEnumerable<IGreeter> greeters) { }
		                                                    }
		                                                }
		                                                """;

		[Test]
		public async Task APrivateCompositeOfABase_AwaitsTheHookOfAMemberItsFactoryReceives()
		{
			GeneratorResult baseResult = Generator.Run(Granted(AsyncHookedCompositeBase));
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Granted(AsyncHookedCompositeBase), ExtendingShop);

			await That(baseResult.Diagnostics).IsEmpty();
			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources.Single(source => source.Key.Contains("TestShop")).Value;
			await That(source).Contains("await global::Lib.Shop.Internals.Hook_OnActivated_Greeter_*(*, cancellationToken)").AsWildcard();
			await That(source).Contains("global::Lib.Shop.Internals.Composite_AllGreeters_").AsWildcard();
		}

		[Test]
		public async Task ADecoratorOfAModuleInAnotherAssembly_AwaitsTheInnerWrappedHook()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
			                                                                      using System.Threading;
			                                                                      using System.Threading.Tasks;
			                                                                      using Awaiten;

			                                                                      namespace Lib;

			                                                                      public interface IGreeter { }
			                                                                      internal sealed class Greeter : IGreeter { }
			                                                                      internal sealed class LoudGreeter : IGreeter
			                                                                      {
			                                                                          public LoudGreeter(IGreeter inner) { }
			                                                                      }

			                                                                      [Module]
			                                                                      [Singleton<Greeter, IGreeter>(OnActivated = nameof(WarmUpAsync))]
			                                                                      [Decorate<LoudGreeter, IGreeter>]
			                                                                      public static partial class GreetingModule
			                                                                      {
			                                                                          internal static Task WarmUpAsync(Greeter greeter, CancellationToken cancellationToken) => Task.CompletedTask;
			                                                                      }
			                                                                      """, Consumer);

			await That(result.Diagnostics).IsEmpty();
			string source = result.Sources.Single(source => source.Key.Contains("AppContainer")).Value;
			await That(source).Contains("await global::Lib.GreetingModule.Internals.Hook_OnActivated_Greeter_*(*, cancellationToken)").AsWildcard();
			await That(source).Contains("global::Lib.GreetingModule.Internals.Decorator_LoudGreeter_").AsWildcard();
		}
	}
}
