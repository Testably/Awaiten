namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt111ConflictingProduction
	{
		[Test]
		public async Task ReportsWhenAnImplementationIsRegisteredWithDifferentProductionStrategies()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(Make))]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Service Make() => new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT111*").AsWildcard()
				.Because("coalescing keeps the first production strategy, so the contradicting one must not be silently dropped");
		}

		[Test]
		public async Task ReportsWhenTheSameImplementationNamesDifferentFactoryMembers()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Service>(Factory = nameof(MakeA))]
			                                       [Singleton<Service>(Factory = nameof(MakeB))]
			                                       [Singleton<Service>]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Service MakeA() => new Service();
			                                       	private static Service MakeB() => new Service();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT111*").AsWildcard()
				.Because("the constructor registration makes Service an implementation, so every registration of it is that one implementation and the factories contradict it");
		}

		[Test]
		public async Task ReportsWhenAnExplicitImplementationTypeIsConstructedInAModuleAndProducedByAFactoryInTheContainer()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>]
			                                       public static class ClockModule { }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<SystemClock, IClock>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static SystemClock Create() => new SystemClock();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT111*").AsWildcard()
				.Because("both registrations name SystemClock as the implementation, and the losing module registration stays a collection member, so there is no single way to produce it");
		}

		[Test]
		public async Task QualifiesEachMemberWithItsDeclaringType()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IClock { }
			                                       public sealed class SystemClock : IClock { }

			                                       [Module]
			                                       [Singleton<SystemClock, IClock>(Factory = nameof(Create))]
			                                       public static class ClockModule
			                                       {
			                                       	public static SystemClock Create() => new SystemClock();
			                                       }

			                                       [Container]
			                                       [Import(typeof(ClockModule))]
			                                       [Singleton<SystemClock, IClock>(Factory = nameof(Create))]
			                                       public static partial class MyContainer
			                                       {
			                                       	public static SystemClock Create() => new SystemClock();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT111*factory 'MyCode.MyContainer.Create' and factory 'MyCode.ClockModule.Create'*").AsWildcard()
				.Because("two members of the same name on different owners are different factories, so the message must tell them apart");
		}

		public class WithoutAnImplementationType
		{
			[Test]
			public async Task DoesNotReportWhenTheContainerRegistersAnInstanceOverAModuleFactory()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }

				                                       [Module]
				                                       [Singleton<IClock>(Factory = nameof(Create))]
				                                       public static class ClockModule
				                                       {
				                                       	public static IClock Create() => new SystemClock();
				                                       }

				                                       [Container]
				                                       [Import(typeof(ClockModule))]
				                                       [Singleton<IClock>(Instance = nameof(Clock))]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock Clock { get; } = new FixedClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("a Factory or Instance registration that names only the service is identified by its member, so the two are different implementations and the container's registration takes precedence");
			}

			[Test]
			public async Task DoesNotReportWhenTheContainerAndAModuleNameFactoriesOfTheSameName()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }

				                                       [Module]
				                                       [Singleton<IClock>(Factory = nameof(Create))]
				                                       public static class ClockModule
				                                       {
				                                       	public static IClock Create() => new SystemClock();
				                                       }

				                                       [Container]
				                                       [Import(typeof(ClockModule))]
				                                       [Singleton<IClock>(Factory = nameof(Create))]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock Create() => new FixedClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("the module's Create and the container's Create are different members, so they produce different implementations");
			}

			[Test]
			public async Task DoesNotReportForTwoFactoriesInOneContainer()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }

				                                       [Container]
				                                       [Singleton<IClock>(Factory = nameof(CreateA))]
				                                       [Singleton<IClock>(Factory = nameof(CreateB))]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock CreateA() => new SystemClock();
				                                       	public static IClock CreateB() => new FixedClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("two factories of one service are two implementations, like two constructed classes: the first wins single resolution and both are collection members");
			}

			[Test]
			public async Task DoesNotReportForTwoFactoriesOfAConcreteServiceThatIsNotRegisteredAsAnImplementation()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public sealed class Service { }

				                                       [Container]
				                                       [Singleton<Service>(Factory = nameof(MakeA))]
				                                       [Singleton<Service>(Factory = nameof(MakeB))]
				                                       public static partial class MyContainer
				                                       {
				                                       	private static Service MakeA() => new Service();
				                                       	private static Service MakeB() => new Service();
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("neither registration names an implementation type, so each factory is its own implementation");
			}

			[Test]
			public async Task DoesNotReportForKeyedFactoriesNamingDifferentMembers()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }

				                                       [Container]
				                                       [Singleton<IClock>(Factory = nameof(CreateA), Key = "a")]
				                                       [Singleton<IClock>(Factory = nameof(CreateB), Key = "b")]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock CreateA() => new SystemClock();
				                                       	public static IClock CreateB() => new FixedClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("each key is produced by its own factory, so the keyed registrations are different implementations");
			}

			[Test]
			public async Task DoesNotReportForAContextualFactoryBesideTheUnconditionalOne()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }
				                                       public sealed class Report
				                                       {
				                                       	public Report(IClock clock) { }
				                                       }

				                                       [Container]
				                                       [Singleton<IClock>(Factory = nameof(CreateA))]
				                                       [Singleton<IClock>(Factory = nameof(CreateB), WhenInjectedInto = typeof(Report))]
				                                       [Transient<Report>]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock CreateA() => new SystemClock();
				                                       	public static IClock CreateB() => new FixedClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).IsEmpty()
					.Because("the contextual registration names a different factory, so it is a different implementation");
			}

			[Test]
			public async Task ReportsDuplicateKeyInsteadForTwoFactoriesUnderTheSameKey()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }

				                                       [Container]
				                                       [Singleton<IClock>(Factory = nameof(CreateA), Key = "a")]
				                                       [Singleton<IClock>(Factory = nameof(CreateB), Key = "a")]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock CreateA() => new SystemClock();
				                                       	public static IClock CreateB() => new FixedClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*AWT117*").AsWildcard()
					.Because("two different implementations claim one key, which is the ambiguity AWT117 names");
				await That(result.Diagnostics).DoesNotContain("*AWT111*").AsWildcard()
					.Because("the two factories are different implementations, not one implementation produced two ways");
			}

			[Test]
			public async Task ReportsCrossModuleDuplicateInsteadForFactoriesInTwoModules()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }
				                                       public sealed class FixedClock : IClock { }

				                                       [Module]
				                                       [Singleton<IClock>(Factory = nameof(Create))]
				                                       public static class ModuleA
				                                       {
				                                       	public static IClock Create() => new SystemClock();
				                                       }

				                                       [Module]
				                                       [Singleton<IClock>(Factory = nameof(Create))]
				                                       public static class ModuleB
				                                       {
				                                       	public static IClock Create() => new FixedClock();
				                                       }

				                                       [Container]
				                                       [Import(typeof(ModuleA))]
				                                       [Import(typeof(ModuleB))]
				                                       public static partial class MyContainer
				                                       {
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*AWT155*IClock*ModuleA*ModuleB*").AsWildcard()
					.Because("two modules registering different implementations of one service at the same tier is the import-order ambiguity AWT155 names");
				await That(result.Diagnostics).DoesNotContain("*AWT111*").AsWildcard()
					.Because("the two modules' factories are different implementations");
			}

			[Test]
			public async Task ReportsForTheSameMemberNamedAsBothFactoryAndInstance()
			{
				GeneratorResult result = Generator.Run("""
				                                       using Awaiten;

				                                       namespace MyCode;

				                                       public interface IClock { }
				                                       public sealed class SystemClock : IClock { }

				                                       [Container]
				                                       [Singleton<IClock>(Factory = nameof(Clock))]
				                                       [Singleton<IClock>(Instance = nameof(Clock), Key = "k")]
				                                       public static partial class MyContainer
				                                       {
				                                       	public static IClock Clock() => new SystemClock();
				                                       }
				                                       """);

				await That(result.Diagnostics).Contains("*AWT111*").AsWildcard()
					.Because("both registrations name the same member, so they are one implementation produced two ways");
			}
		}

		[Test]
		public async Task ReportsWhenTheSameImplementationUnderDifferentServicesNamesDifferentFactoryMembers()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IRead { }
			                                       public interface IWrite { }
			                                       public sealed class Store : IRead, IWrite { }

			                                       [Container]
			                                       [Singleton<Store, IRead>(Factory = nameof(MakeA))]
			                                       [Singleton<Store, IWrite>(Factory = nameof(MakeB))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static Store MakeA() => new Store();
			                                       	private static Store MakeB() => new Store();
			                                       }
			                                       """);

			await That(result.Diagnostics).Contains("*AWT111*").AsWildcard()
				.Because("coalescing one implementation across services keeps the first factory, so a second, different one must not be silently dropped");
		}
	}
}
