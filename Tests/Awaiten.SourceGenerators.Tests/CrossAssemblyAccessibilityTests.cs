using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

/// <summary>
///     Accessibility across an assembly boundary: the container reaches an <c>internal</c> constructor or setter of
///     a referenced assembly exactly when that assembly grants it <c>[InternalsVisibleTo]</c>, and not otherwise.
///     The generated container lives in "TestAssembly", the referenced source compiles as "ReferencedAssembly".
/// </summary>
public class CrossAssemblyAccessibilityTests
{
	[Fact]
	public async Task InternalConstructorAcrossInternalsVisibleTo_IsSelected()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

			namespace Lib;

			public interface IEquipment { }
			public sealed class Roaster : IEquipment { internal Roaster() { } }
			""", """
			using Awaiten;

			namespace MyCode;

			[Container]
			[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
			public static partial class MyContainer
			{
			}
			""");

		// The scan admits Roaster (it asks Roslyn, which honors the grant), so constructor selection must agree:
		// rejecting the constructor here would raise AWT104 on a 'new' the generated code may legally emit. An
		// empty diagnostic set covers the emitted source too, so this also proves the construction compiles.
		await That(result.Diagnostics).IsEmpty()
			.Because("[InternalsVisibleTo] makes the internal constructor callable from the container's assembly");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Roaster()")
			.Because("the container constructs the scanned implementation through its internal constructor");
	}

	[Fact]
	public async Task InternalConstructorWithoutInternalsVisibleTo_ReportsAwt104()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			namespace Lib;

			public interface IEquipment { }
			public sealed class Roaster : IEquipment { internal Roaster() { } }
			""", """
			using Awaiten;

			namespace MyCode;

			[Container]
			[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).Contains("*AWT104*Lib.Roaster*").AsWildcard()
			.Because("without the grant the container's assembly cannot call the internal constructor");
	}

	[Fact]
	public async Task InternalImplementationAcrossInternalsVisibleTo_IsScannedAndConstructed()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

			namespace Lib;

			public interface IEquipment { }
			internal sealed class Roaster : IEquipment { }
			""", """
			using Awaiten;

			namespace MyCode;

			[Container]
			[Scan(typeof(Lib.IEquipment), InAssembliesOf = new[] { typeof(Lib.IEquipment) })]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the grant makes the internal implementation nameable from the container's assembly");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("new global::Lib.Roaster()")
			.Because("an internal type the container can see is constructed like a public one");
	}

	[Fact]
	public async Task InternalSetterAcrossInternalsVisibleTo_IsInjected()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			using Awaiten;

			[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TestAssembly")]

			namespace Lib;

			public sealed class Grinder { }
			public sealed class Roaster
			{
			    [Inject] public Grinder Grinder { get; internal set; }
			}
			""", """
			using Awaiten;

			namespace MyCode;

			[Container]
			[Transient<Lib.Grinder>]
			[Transient<Lib.Roaster>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).IsEmpty()
			.Because("the grant lets the container's object initializer assign the internal setter");
		await That(result.Sources["Awaiten.MyCode.MyContainer.g.cs"]).Contains("Grinder =")
			.Because("the injected member is filled from the object initializer");
	}

	[Fact]
	public async Task InternalSetterWithoutInternalsVisibleTo_ReportsAwt136()
	{
		GeneratorResult result = Generator.RunWithReferencedAssembly("""
			using Awaiten;

			namespace Lib;

			public sealed class Grinder { }
			public sealed class Roaster
			{
			    [Inject] public Grinder Grinder { get; internal set; }
			}
			""", """
			using Awaiten;

			namespace MyCode;

			[Container]
			[Transient<Lib.Grinder>]
			[Transient<Lib.Roaster>]
			public static partial class MyContainer
			{
			}
			""");

		await That(result.Diagnostics).Contains("*AWT136*Grinder*").AsWildcard()
			.Because("without the grant the setter is out of the container's reach, so it must surface as AWT136 rather than an inaccessible-setter error in generated code");
	}
}
