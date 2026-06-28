using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     The generated shape of <c>Owned&lt;T&gt;</c> resolution: a bare <c>Owned&lt;T&gt;</c>, a
///     <c>Func&lt;Owned&lt;T&gt;&gt;</c> factory and a parameterized <c>Func&lt;TArg…, Owned&lt;T&gt;&gt;</c>
///     all build their service in a throwaway child scope via the <c>__Owned&lt;T&gt;</c> helper and hand
///     back a disposal handle.
/// </summary>
public class OwnedTests
{
	[Fact]
	public async Task FuncOwned_EmitsAThrowawayScopeFactoryThroughTheOwnedHelper()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Widget : IDisposable { public void Dispose() { } }
		                                       public sealed class Workshop { public Workshop(Func<Owned<Widget>> widgets) { } }

		                                       [Container]
		                                       [Transient<Widget>]
		                                       [Singleton<Workshop>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("protected global::Awaiten.Owned<T> __Owned<T>(global::System.Func<Scope, T> __resolve)")
			.Because("the throwaway-scope owned helper is emitted on the base Scope");
		await That(source).Contains("Scope __owned = CreateScope();")
			.Because("the owned helper builds its value in a fresh child scope");
		await That(source).Contains("__Owned<global::MyCode.Widget>(__s => __s.Resolve<global::MyCode.Widget>())")
			.Because("the Func<Owned<Widget>> factory resolves the widget into the throwaway scope");
	}

	[Fact]
	public async Task BareOwnedAndFuncOwned_AreDispatchableByType()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Widget : IDisposable { public void Dispose() { } }

		                                       [Container]
		                                       [Transient<Widget>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("typeof(global::Awaiten.Owned<global::MyCode.Widget>)")
			.Because("a bare Owned<Widget> is resolvable by Type");
		await That(source).Contains("typeof(global::System.Func<global::Awaiten.Owned<global::MyCode.Widget>>)")
			.Because("a Func<Owned<Widget>> factory is resolvable by Type");
	}

	[Fact]
	public async Task ParameterizedFuncOwned_ForwardsRuntimeArgumentsIntoTheThrowawayScope()
	{
		GeneratorResult result = Generator.Run("""
		                                       using Awaiten;
		                                       using System;

		                                       namespace MyCode;

		                                       public sealed class Label : IDisposable { public Label([Arg] string text) { } public void Dispose() { } }

		                                       [Container]
		                                       [Transient<Label>]
		                                       public static partial class MyContainer
		                                       {
		                                       }
		                                       """);

		await That(result.Diagnostics.Where(d => d.Contains("error"))).IsEmpty();
		string source = result.Sources["Awaiten.MyCode.MyContainer.g.cs"];

		await That(source).Contains("typeof(global::System.Func<string, global::Awaiten.Owned<global::MyCode.Label>>)")
			.Because("the leak-free factory for a parameterized service is Func<TArg…, Owned<T>>");
		await That(source).Contains("__Owned<global::MyCode.Label>(__s => __s.ResolveLabel(a0))")
			.Because("the runtime argument flows into the parameterized resolver called on the throwaway scope");
	}
}
