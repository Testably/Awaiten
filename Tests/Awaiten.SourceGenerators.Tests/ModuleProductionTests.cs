namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     <c>Factory</c>/<c>Instance</c> members on module registrations: the named member is resolved against
///     the module that declared the registration (never the container), the generated code calls it qualified
///     with the module type, a missing member is AWT108/AWT109 naming the module, and a member that exists on
///     the module but is hidden from the container is AWT153.
/// </summary>
public class ModuleProductionTests
{
	[Fact]
	public async Task ModuleFactory_ResolvesAgainstTheModule_AndIsEmittedQualified()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Factory = nameof(CreateClock))]
		                                       public static class ClockModule
		                                       {
		                                           public static ModuleClock CreateClock() => new ModuleClock();
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.ClockModule.CreateClock(")
			.Because("a module's factory is called qualified with the module type - the generated container is another class");
	}

	[Fact]
	public async Task ModuleInstance_ResolvesAgainstTheModule_AndIsEmittedQualified()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Instance = nameof(Clock))]
		                                       public static class ClockModule
		                                       {
		                                           public static ModuleClock Clock { get; } = new ModuleClock();
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("return global::MyCode.ClockModule.Clock;")
			.Because("a module's pre-built instance member is read qualified with the module type");
	}

	[Fact]
	public async Task ModuleFactory_DoesNotFallBackToAContainerMemberOfTheSameName()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Factory = nameof(MyContainer.CreateClock))]
		                                       public static class ClockModule
		                                       {
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                           public static ModuleClock CreateClock() => new ModuleClock();
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT108*ClockModule*").AsWildcard()
			.Because("a module registration's factory must live on the module; a same-named container member is not a silent fallback");
	}

	[Fact]
	public async Task ModuleFactory_MissingMember_ReportsAwt108NamingTheModule()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Factory = "CreateClock")]
		                                       public static class ClockModule
		                                       {
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT108*the module 'MyCode.ClockModule' has no accessible method 'CreateClock'*").AsWildcard()
			.Because("the message points at the module the user declared the factory on, not at the container");
	}

	[Fact]
	public async Task ModuleInstance_MissingMember_ReportsAwt109NamingTheModule()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Instance = "Clock")]
		                                       public static class ClockModule
		                                       {
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT109*the module 'MyCode.ClockModule' has no accessible field or property 'Clock'*").AsWildcard()
			.Because("the message points at the module the user declared the instance member on");
	}

	[Fact]
	public async Task ModuleFactory_PrivateMember_ReportsAwt153()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Factory = nameof(CreateClock))]
		                                       public static class ClockModule
		                                       {
		                                           private static ModuleClock CreateClock() => new ModuleClock();
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT153*CreateClock*ClockModule*").AsWildcard()
			.Because("a private module member exists but cannot be called from the generated container");
	}

	[Fact]
	public async Task ModuleInstance_PrivateMember_ReportsAwt153()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Instance = "Clock")]
		                                       public static class ClockModule
		                                       {
		                                           private static ModuleClock Clock { get; } = new ModuleClock();
		                                       }

		                                       [Container]
		                                       [Import(typeof(ClockModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT153*Clock*ClockModule*").AsWildcard()
			.Because("a private module instance member exists but cannot be read from the generated container");
	}

	[Fact]
	public async Task ContainerFactory_MessageStillNamesTheContainer()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class SystemClock : IClock { }

		                                       [Container]
		                                       [Singleton<SystemClock, IClock>(Factory = "CreateClock")]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).Contains("*AWT108*the container has no accessible method 'CreateClock'*").AsWildcard()
			.Because("the container's own registrations keep the existing wording");
	}
}
