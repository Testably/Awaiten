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
				await That(result.Sources.Values.Single()).Contains("public const string RegistrationSkipped_Greeter_")
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
			public async Task ReportsAnInaccessibleImplementationUnderSeveralServicesThatNoHandleCanBuild()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IGreeter { }
					public interface IFarewell { }
					internal sealed class Secret { }
					internal sealed class Greeter : IGreeter, IFarewell { public Greeter(Secret secret) { } }

					[Module]
					[Singleton<Greeter, IGreeter>]
					[Singleton<Greeter, IFarewell>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT197*Lib.Greeter*Lib.IGreeter, Lib.IFarewell*").AsWildcard()
					.Because("one factory returns one service, so a second service would split the singleton a container shares across both, and no handle can share it since the implementation cannot be built");
			}

			[Test]
			[Arguments("IInternal", "IPublic", false, false)]
			[Arguments("IInternal", "IOther", false, false)]
			[Arguments("IPublic", "IFarewell", false, false)]
			[Arguments("IInternal", "IPublic", true, false)]
			[Arguments("IInternal", "IOther", true, false)]
			[Arguments("IInternal", "IPublic", false, true)]
			[Arguments("IInternal", "IOther", false, true)]
			public async Task ReportsAPrivateImplementationUnderSeveralServicesWithItsReason(string first, string second, bool grant, bool container)
			{
				GeneratorResult result = Generator.Run($$"""
					using Awaiten;

					{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]" : "")}}

					namespace Lib;

					public interface IPublic { }
					public interface IFarewell { }
					internal interface IInternal { }
					internal interface IOther { }

					[Module]
					[Singleton<GreetingModule.Greeter, {{first}}>]
					[Singleton<GreetingModule.Greeter, {{second}}>]
					public static partial class GreetingModule
					{
					    private sealed class Greeter : IPublic, IFarewell, IInternal, IOther { }
					}

					{{(container ? "[Container]\n[Import(typeof(GreetingModule))]\npublic static partial class MyContainer;" : "")}}
					""");

				await That(result.Diagnostics).Contains($"*AWT197*'Lib.GreetingModule.Greeter' is private to the module*(Lib.{first}, Lib.{second})*make it internal*").AsWildcard()
					.Because("no container can name the private type to hold the one instance its services share, so the library build reports it whichever container imports the module");
			}

			[Test]
			public async Task SharesAnImplementationUnderSeveralServicesThroughAHandleWhenTheAssemblyGrantsInternalsVisibleTo()
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

				await That(result.Diagnostics).IsEmpty();
				await That(result.Sources.Values.Single()).Contains("HiddenRegistration_Greeter_")
					.And.DoesNotContain("RegistrationSkipped_")
					.Because("a handle shares the instance across both services for an assembly without InternalsVisibleTo, while a friend still constructs it itself");
			}

			[Test]
			public async Task SkipsBothFactoriesOfAScanOverlapWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IPlugin { }
					public interface IGreeter { }
					internal sealed class Greeter : IPlugin, IGreeter { }

					[Module]
					[Scan<IPlugin>(As = ScanAs.Marker)]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself and shares it across the registration and the scan");
				string source = result.Sources.Values.Single();
				await That(source).Contains("public const string RegistrationSkipped_Greeter_")
					.And.Contains("\"it is also matched by the module's [Scan]\"")
					.And.Contains("public const string ScanSkipped_Greeter_")
					.And.Contains("\"it is also registered explicitly by the module\"")
					.And.Contains("[global::Awaiten.GeneratedScanRegistrationAttribute<global::Lib.IPlugin>(\"Lib.Greeter\"")
					.Because("the module records both, so a consumer without access names why it cannot construct the implementation");
				await That(source).DoesNotContain("Registration_Greeter_").And.DoesNotContain("Scan_Greeter_")
					.Because("two factories would build two instances of what a container treats as one implementation");
			}

			[Test]
			public async Task SkipsTheScanFactoryOfAPublicImplementationTheModuleRegistersAndScansUnderSeveralServices()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					namespace Lib;

					public interface IPlugin { }
					public interface IHeater { }
					public interface IGreeter { }
					public sealed class Greeter : IPlugin, IHeater, IGreeter { }

					[Module]
					[Scan<IPlugin>(As = ScanAs.Marker)]
					[Scan<IHeater>(As = ScanAs.Marker)]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("every container constructs a public implementation for the registration and registers the match under each service itself");
				await That(result.Sources.Values.Single()).Contains("public const string ScanSkipped_Greeter_").And.DoesNotContain("Scan_Greeter_");
			}

			[Test]
			public async Task KeepsAnErrorOfAScanOverlapThatNoAssemblyCouldConstruct()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IPlugin { }
					public interface IGreeter { }
					internal sealed class Greeter : IPlugin, IGreeter
					{
					    private Greeter() { }
					}

					[Module]
					[Scan<IPlugin>(As = ScanAs.Marker)]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).Contains("*AWT104*Lib.Greeter*").AsWildcard()
					.Because("an assembly granted InternalsVisibleTo could not construct the implementation either, so skipping both factories would hide the error");
				await That(result.Sources.Values.Single()).DoesNotContain("RegistrationSkipped_Greeter_");
			}

			[Test]
			public async Task ReportsAScanOverlapWhenTheAssemblyGrantsInternalsVisibleToAPrivateType()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IPlugin { }
					public interface IGreeter { }

					[Module]
					[Scan<IPlugin>(As = ScanAs.Marker)]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule
					{
					    private sealed class Greeter : IPlugin, IGreeter { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT193*Greeter*").AsWildcard()
					.Because("no other assembly can construct a type private to the module, so the scan still yields to the registration's factory");
				await That(result.Sources.Values.Single()).Contains("Registration_Greeter_").And.DoesNotContain("Skipped_Greeter_");
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
			public async Task ReportsAnInitOnlyOptionalInjectProperty()
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
					.Because("no other assembly can ever construct the implementation, and the factory cannot assign an init-only property after construction");
			}

			[Test]
			public async Task ReportsAnInitOnlyDeferredInjectProperty()
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

			[Test]
			public async Task DoesNotReportAnInitOnlyOptionalInjectPropertyWhenTheAssemblyGrantsInternalsVisibleTo()
			{
				GeneratorResult result = Generator.Run("""
					using Awaiten;

					[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App")]

					namespace Lib;

					public interface IGreeter { }
					public interface IClock { }
					internal sealed class Greeter : IGreeter
					{
					    [Inject(Optional = true)]
					    public IClock Clock { get; init; } = null!;
					}

					[Module]
					[Singleton<Greeter, IGreeter>]
					public static partial class GreetingModule { }
					""");

				await That(result.Diagnostics).IsEmpty()
					.Because("an assembly granted InternalsVisibleTo constructs the implementation itself and honors the optional property");
				await That(result.Sources.Values.Single()).Contains("its property 'Clock' is marked [Inject(Optional = true)]");
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
					        [Inject(Optional = true)]
					        public IClock? Clock { get; init; }
					    }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT200*Greeter*Clock*[Inject(Optional = true)]*").AsWildcard()
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
					        [Inject(Optional = true)]
					        public IClock? Clock { get; init; }
					    }

					    [Module]
					    [Singleton<Greeter, IGreeter>]
					    public static partial class GreetingModule { }
					}
					""");

				await That(result.Diagnostics).Contains("*AWT200*Greeter*Clock*[Inject(Optional = true)]*").AsWildcard()
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
