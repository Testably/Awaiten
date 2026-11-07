using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	/// <summary>
	///     The self-compiled module scan diagnostics, generalized to a module registration of an implementation another
	///     assembly cannot construct, which the module's own build self-compiles the same way.
	/// </summary>
	public class SelfCompiledModuleRegistrations
	{
		private const string Consumer = """
		                                using Awaiten;
		                                using Lib;

		                                namespace MyCode;

		                                [Container]
		                                [Import(typeof(GreetingModule))]
		                                public static partial class MyContainer
		                                {
		                                }
		                                """;

		public class Awt154
		{
			[Test]
			public async Task ReportsWhenTheModuleAssemblyWasBuiltWithoutTheGenerator()
			{
				GeneratorResult result = Generator.RunWithReferencedAssembly("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }
					internal sealed class Farewell : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					[Singleton<Farewell, IGreeter>]
					public static class GreetingModule { }
					""", Consumer);

				await That(result.Diagnostics.Where(diagnostic => diagnostic.Contains("AWT154"))).HasCount(1)
					.Because("the module carries no generated factory for an implementation this container cannot construct, reported once per module");
				await That(result.Diagnostics).Contains("*AWT154*GreetingModule*registers 'Lib.Greeter'*").AsWildcard();
			}

			[Test]
			public async Task DoesNotReportForAPublicImplementation()
			{
				GeneratorResult result = Generator.RunWithReferencedAssembly("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static class GreetingModule { }
					""", Consumer);

				await That(result.Diagnostics).IsEmpty()
					.Because("the consumer constructs a public implementation itself and needs no generated factory");
			}

			[Test]
			public async Task DoesNotReportWhenTheModuleGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.RunWithReferencedAssembly("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static class GreetingModule { }
					""", Consumer);

				await That(result.Diagnostics).IsEmpty()
					.Because("InternalsVisibleTo lets the consumer construct the implementation itself, as before");
			}
		}

		public class Awt194
		{
			[Test]
			public async Task ReportsWhenAModuleWithASelfCompiledRegistrationIsNotPartial()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT194*GreetingModule*registers 'Lib.Greeter'*").AsWildcard()
					.Because("the generator cannot add the factory another assembly needs to a module that is not partial");
			}

			[Test]
			public async Task DoesNotReportForAModuleOfPublicImplementations()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("a module whose registrations another assembly can construct needs no factory, so it need not be partial");
			}

			[Test]
			public async Task DoesNotReportForAnInternalModule()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					internal static class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("another assembly can only import an internal module through InternalsVisibleTo, which lets it construct the implementation");
			}
		}

		public class Awt195
		{
			[Test]
			public async Task ReportsAnInaccessibleConstructorParameter()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal interface IClock { }
					internal sealed class Greeter : IGreeter
					{
					    public Greeter(IClock clock) { }
					}

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT195*Lib.Greeter*self-compiled from a module registration*Lib.IClock*").AsWildcard()
					.Because("the public factory would expose a parameter type another assembly cannot name");
			}

			[Test]
			public async Task DoesNotReportWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					internal interface IClock { }
					internal sealed class Greeter : IGreeter
					{
					    public Greeter(IClock clock) { }
					}

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself, so the module only records why it generated no factory");
				await That(result.Sources.Values.Single()).Contains("public const string Awaiten__RegistrationSkipped_Greeter_")
					.And.Contains("its constructor parameter of type 'Lib.IClock' is not accessible outside the module's assembly");
			}

			[Test]
			public async Task DoesNotReportForAccessibleParameters()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public interface IClock { }
					internal sealed class Greeter : IGreeter
					{
					    public Greeter(IClock clock) { }
					}

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty();
			}
		}

		public class Awt197
		{
			[Test]
			public async Task ReportsAnInaccessibleImplementationUnderSeveralServices()
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

				await That(result.Diagnostics).Contains("*AWT197*Lib.Greeter*Lib.IGreeter, Lib.IFarewell*").AsWildcard()
					.Because("one factory returns one service, so a second service would split the singleton a container shares across both");
			}

			[Test]
			public async Task DoesNotReportSeveralServicesWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					public interface IFarewell { }
					internal sealed class Greeter : IGreeter, IFarewell { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					[Singleton<Greeter, IFarewell>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself and shares it across both services");
				await That(result.Sources.Values.Single()).Contains("it is registered under several services (Lib.IGreeter, Lib.IFarewell)")
					.And.DoesNotContain("Awaiten__Registration_");
			}

			[Test]
			public async Task ReportsAScanOverlapEvenWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IPlugin { }
					public interface IGreeter { }
					internal sealed class Greeter : IPlugin, IGreeter { }

					[Module]
					[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT197*Lib.Greeter*self-compiled from a module registration*").AsWildcard()
					.Because("the scan's factory builds the type for every consumer, so a consumer constructing the registration itself would still split the instance");
			}

			[Test]
			public async Task ReportsAnImplementationAlsoMatchedByAScanOfTheModule()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IPlugin { }
					public interface IGreeter { }
					internal sealed class Greeter : IPlugin, IGreeter { }

					[Module]
					[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT197*Lib.Greeter*self-compiled from a module registration*").AsWildcard()
					.Because("the scan's factory and the registration's factory would build two instances of what a container treats as one implementation");
			}

			[Test]
			public async Task DoesNotReportForOneServiceUnderSeveralKeys()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					[Singleton<Greeter, IGreeter>(Key = "formal")]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("both registrations return the same service, so they share the one factory and the one instance");
			}

			[Test]
			public async Task DoesNotReportForAPublicImplementationUnderSeveralServices()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public interface IFarewell { }
					public sealed class Greeter : IGreeter, IFarewell
					{
					    internal Greeter() { }
					}

					[Module]
					[Singleton<Greeter, IGreeter>]
					[Singleton<Greeter, IFarewell>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("the factory returns the public implementation, which serves every service from one instance");
			}
		}

		public class Awt200
		{
			[Test]
			public async Task ReportsAnInjectProperty()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public interface IClock { }
					internal sealed class Greeter : IGreeter
					{
					    [Inject]
					    public IClock? Clock { get; set; }
					}

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*Clock*[Inject]*").AsWildcard()
					.Because("the generated factory cannot fill the property the way a container would");
			}

			[Test]
			public async Task ReportsAFromKeyConstructorParameter()
			{
				GeneratorResult result = Generator.Run("""
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
					""");

				await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*clock*[FromKey]*").AsWildcard()
					.Because("the factory's bare parameter would resolve the unkeyed service in another assembly");
			}

			[Test]
			public async Task ReportsAnArgParameterWithoutAnInternalsVisibleToGrant()
			{
				GeneratorResult result = Generator.Run("""
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
					""");

				await That(result.Diagnostics).Contains("*AWT200*Lib.Greeter*name*[Arg]*").AsWildcard()
					.Because("no other assembly can ever construct the implementation, so the missing factory is an error in the module's own build");
			}

			[Test]
			public async Task DoesNotReportAnArgParameterWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter
					{
					    public Greeter([Arg] string name) { }
					}

					[Module]
					[Transient<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself and honors the [Arg]");
				await That(result.Sources.Values.Single()).Contains("its constructor parameter 'name' is marked [Arg]");
			}

			[Test]
			public async Task ReportsAPrivateNestedImplementationEvenWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					public interface IClock { }

					[Module]
					[Singleton<GreetingModule.Greeter, IGreeter>]
					public static partial class GreetingModule
					{
					    private sealed class Greeter : IGreeter
					    {
					        public Greeter([FromKey("utc")] IClock clock) { }
					    }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT200*Greeter*clock*[FromKey]*").AsWildcard()
					.Because("InternalsVisibleTo does not open a private nested type, so no other assembly could construct it");
			}

			[Test]
			public async Task ReportsATypePrivateToAClassContainingTheModuleEvenWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					public interface IClock { }

					public static partial class Greetings
					{
					    private sealed class Greeter : IGreeter
					    {
					        public Greeter([FromKey("utc")] IClock clock) { }
					    }

					    [Module]
					    [Singleton<Greeter, IGreeter>]
					    public static partial class GreetingModule { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT200*Greeter*clock*[FromKey]*").AsWildcard()
					.Because("a type hidden in the module always needs the module's factory, whatever InternalsVisibleTo grants");
			}
		}

		public class Awt201
		{
			[Test]
			public async Task ReportsAGenericModule()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule<T> { }
					""");

				await That(result.Diagnostics).Contains("*AWT201*GreetingModule*registers 'Lib.Greeter'*").AsWildcard()
					.Because("no closed module exists to carry the factory a consumer would call");
			}
		}

		public class Awt203Awt204
		{
			[Test]
			public async Task Awt203_ReportsAnInaccessibleHookParameter()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					internal sealed class Greeter : IGreeter { }
					internal sealed class Log { }

					[Module]
					[Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate))]
					public static partial class GreetingModule
					{
					    internal static void Activate(Greeter greeter, Log log) { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT203*Lib.Greeter*self-compiled from a module registration*OnActivated*Lib.Log*").AsWildcard()
					.Because("the public hook wrapper would expose a parameter type another assembly cannot name");
			}

			[Test]
			public async Task Awt204_ReportsAFromKeyHookParameter()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public interface ILog { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>(OnRelease = nameof(Release))]
					public static partial class GreetingModule
					{
					    internal static void Release(Greeter greeter, [FromKey("audit")] ILog log) { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT204*Lib.Greeter*OnRelease*log*[FromKey]*").AsWildcard()
					.Because("the wrapper's bare parameter would drop the key");
			}

			[Test]
			public async Task DoesNotReportAPublicHookParameter()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public interface ILog { }
					internal sealed class Greeter : IGreeter { }

					[Module]
					[Singleton<Greeter, IGreeter>(OnActivated = nameof(Activate))]
					public static partial class GreetingModule
					{
					    internal static void Activate(Greeter greeter, ILog log) { }
					}
					""");

				await That(result.Diagnostics).IsEmpty();
			}
		}
	}
}
