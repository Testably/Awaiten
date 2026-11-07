using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt238HandleProviderNotImported
	{
		private const string Library = """
			using Awaiten;

			namespace Lib;

			public interface IGrinder { }
			public interface IRoaster { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder(BeanStore store) { } }
			internal sealed class Roaster : IRoaster { public Roaster(BeanStore store) { } }

			[Module]
			[Singleton<BeanStore>]
			public static partial class StorageModule { }

			[Module]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }

			[Module]
			[Singleton<BeanStore>]
			[Singleton<Roaster, IRoaster>]
			public static partial class TeaModule { }
			""";

		private static string Consumer(params string[] modules) => $$"""
			using Awaiten;
			using Lib;

			namespace MyCode;

			[Container]
			{{string.Join("\n", modules.Select(module => $"[Import(typeof({module}))]"))}}
			public static partial class MyContainer;
			""";

		[Test]
		public async Task TheModuleThatDeclaresTheHandleNotImported_ReportsAwt238()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("CoffeeModule"));

			await That(result.Diagnostics).Contains("*AWT238*'Lib.CoffeeModule'*'Lib.BeanStore'*'Lib.StorageModule'*import 'Lib.StorageModule' too*").AsWildcard();
			await That(result.Diagnostics).DoesNotContain("*AWT101*").AsWildcard()
				.Because("the missing import is the one cause, so the missing handle is not reported again");
		}

		[Test]
		public async Task ADecoratorOfAServiceOnlyTheModuleThatDeclaresTheHandleRegisters_ReportsAwt238Alone()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly("""
				using Awaiten;

				namespace Lib;

				internal interface IKettle { }
				internal sealed class Kettle : IKettle { }
				internal sealed class Logging : IKettle { public Logging(IKettle inner) { } }

				[Module]
				[Singleton<Kettle, IKettle>]
				public static partial class KettleModule { }

				[Module]
				[Decorate<Logging, IKettle>]
				public static partial class LoggingModule { }
				""", Consumer("LoggingModule"));

			await That(result.Diagnostics).HasSingle().Which.Contains("*AWT238*'Lib.LoggingModule'*'Lib.IKettle'*'Lib.KettleModule'*").AsWildcard()
				.Because("the missing import is the one cause, so the decorated handle without a registration is not reported again (AWT123)");
		}

		[Test]
		public async Task TheModuleThatDeclaresTheHandleImported_DoesNotReportAwt238()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("StorageModule", "CoffeeModule"));

			await That(result.Diagnostics).IsEmpty()
				.Because("the storage module registers the handle the coffee module's factory takes");
		}

		[Test]
		public async Task AnotherImportedModuleThatRegistersTheService_DoesNotReportAwt238()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("TeaModule", "CoffeeModule"));

			await That(result.Diagnostics).IsEmpty()
				.Because("the tea module registers the handle the coffee module's factory takes, though the storage module declares it");
		}

		[Test]
		public async Task AModuleThatRegistersTheServiceItself_DoesNotReportAwt238()
		{
			GeneratorResult result = Generator.RunWithGeneratedReferencedAssembly(Library, Consumer("TeaModule"));

			await That(result.Diagnostics).IsEmpty()
				.Because("the tea module registers the internal service itself, though the storage module declares its handle");
		}
	}
}
