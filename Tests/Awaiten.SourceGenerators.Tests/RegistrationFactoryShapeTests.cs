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
			await That(ModuleSource(result)).Contains("public static global::Lib.IFormalGreeter Awaiten__Registration_Greeter_");
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
			await That(ModuleSource(result)).Contains("public static global::Lib.GreeterBase Awaiten__Registration_Greeter_");
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
		public async Task WithoutACoveringType_StillReportsAwt197()
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

			await That(result.Diagnostics).Contains("*AWT197*Lib.Greeter*Lib.IGreeter, Lib.IFarewell*covers*").AsWildcard()
				.Because("no type the factory may return converts to both services");
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
		public async Task AnOptionalInjectProperty_StillReportsAwt200()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       internal sealed class Greeter : IGreeter
			                                       {
			                                           [Inject(Optional = true)]
			                                           public IClock? Clock { get; set; }
			                                       }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*Clock*[Inject(Optional = true)]*").AsWildcard()
				.Because("a factory parameter cannot be left out when the dependency is not registered");
		}

		[Test]
		public async Task ADeferredInjectProperty_StillReportsAwt200()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace Lib;

			                                       public interface IGreeter { }
			                                       public interface IClock { }
			                                       internal sealed class Greeter : IGreeter
			                                       {
			                                           [Inject(Deferred = true)]
			                                           public IClock Clock { get; set; } = null!;
			                                       }

			                                       [Module]
			                                       [Singleton<Greeter, IGreeter>]
			                                       public static partial class GreetingModule { }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*Clock*[Inject(Deferred = true)]*").AsWildcard()
				.Because("a deferred property is assigned after construction, which a factory parameter cannot express");
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
				.Contains("""internal static global::Lib.IGreeter Awaiten__Registration_Greeter_""")
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
}
