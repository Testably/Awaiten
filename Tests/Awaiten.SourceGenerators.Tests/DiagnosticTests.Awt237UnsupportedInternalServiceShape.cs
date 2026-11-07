namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	public class Awt237UnsupportedInternalServiceShape
	{
		private static string Library(string parameter, bool grant = false) => $$"""
			using System;
			using System.Collections.Generic;
			using System.Threading.Tasks;
			using Awaiten;

			{{(grant ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Friend\")]" : "")}}

			namespace Lib;

			public interface IGrinder { }
			internal sealed class BeanStore { }
			internal sealed class Grinder : IGrinder { public Grinder({{parameter}} store) { } }

			[Module]
			[Singleton<BeanStore>]
			[Singleton<Grinder, IGrinder>]
			public static partial class CoffeeModule { }
			""";

		[Test]
		[Arguments("Task<BeanStore>", "System.Threading.Tasks.Task<Lib.BeanStore>")]
		[Arguments("Lazy<Task<BeanStore>>", "System.Lazy<System.Threading.Tasks.Task<Lib.BeanStore>>")]
		[Arguments("IReadOnlyDictionary<string, BeanStore>", "System.Collections.Generic.IReadOnlyDictionary<string, Lib.BeanStore>")]
		public async Task AnUnsupportedShape_ReportsAwt237(string parameter, string display)
		{
			GeneratorResult result = Generator.Run(Library(parameter));

			await That(result.Diagnostics).Contains($"*AWT237*'Lib.Grinder'*constructor parameter 'store'*'Lib.BeanStore'*'{display}'*").AsWildcard()
				.Because("the generated factory cannot pass the instance on from the handle in that shape");
		}

		[Test]
		[Arguments("BeanStore")]
		[Arguments("Func<BeanStore>")]
		[Arguments("Lazy<BeanStore>")]
		[Arguments("Owned<BeanStore>")]
		[Arguments("Func<Owned<BeanStore>>")]
		[Arguments("IEnumerable<BeanStore>")]
		[Arguments("BeanStore[]")]
		public async Task ASupportedShape_DoesNotReportAwt237(string parameter)
		{
			GeneratorResult result = Generator.Run(Library(parameter));

			await That(result.Diagnostics).IsEmpty()
				.Because("the generated factory takes the handle in the same shape and passes the instance on");
		}

		[Test]
		public async Task UnderAGrant_DoesNotReportAwt237()
		{
			GeneratorResult result = Generator.Run(Library("Task<BeanStore>", grant: true));

			await That(result.Diagnostics).DoesNotContain("*AWT237*").AsWildcard()
				.Because("an assembly granted InternalsVisibleTo constructs the implementation itself");
		}
	}
}
