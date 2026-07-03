namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Generator behavior of modules: a container pulls a module's registrations in with
///     <c>[Import(typeof(Module))]</c>, the module's strong registrations are emitted like the container's
///     own, and the container's own registration overrides an imported overridable <c>Default</c> - so the
///     overridden default is not emitted at all.
/// </summary>
public class ModuleTests
{
	[Fact]
	public async Task Module_ImportsRegistrationsAndTheContainerOverridesOverridableDefaults()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }
		                                       public sealed class AppClock : IClock { }
		                                       public sealed class Logger { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Default = true)]
		                                       [Singleton<Logger>]
		                                       public sealed class InfrastructureModule { }

		                                       [Container]
		                                       [Import(typeof(InfrastructureModule))]
		                                       [Singleton<AppClock, IClock>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		// The module contributes Logger; the container's IClock overrides the module's overridable default,
		// so the overridden ModuleClock is not emitted at all.
		await That(source).Contains("global::MyCode.Logger")
			.Because("a module's strong registrations are pulled into the container");
		await That(source).Contains("global::MyCode.AppClock")
			.Because("the container's own registration wins the service");
		await That(source).DoesNotContain("ModuleClock")
			.Because("an overridden default is dropped in full");
	}

	[Fact]
	public async Task Module_ImportedDefault_IsEmittedWhenTheContainerDoesNotOverrideIt()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;

		                                       namespace MyCode;

		                                       public interface IClock { }
		                                       public sealed class ModuleClock : IClock { }

		                                       [Module]
		                                       [Singleton<ModuleClock, IClock>(Default = true)]
		                                       public sealed class InfrastructureModule { }

		                                       [Container]
		                                       [Import(typeof(InfrastructureModule))]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("global::MyCode.ModuleClock")
			.Because("an imported default fills the gap when the container provides no IClock of its own");
	}
}
