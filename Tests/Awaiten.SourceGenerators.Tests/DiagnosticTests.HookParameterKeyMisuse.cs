using System.Linq;

namespace Awaiten.SourceGenerators.Tests;

public partial class DiagnosticTests
{
	/// <summary>
	///     A lifecycle hook's graph-resolved parameters are classified exactly like constructor parameters, so the
	///     same key-misuse diagnostics apply to them: an unsupported <c>[FromKey]</c> constant (AWT170), an
	///     unsupported keyed-collection key type (AWT159), and a <c>[FromKey]</c> on a synthesized keyed collection
	///     (AWT160).
	/// </summary>
	public class HookParameterKeyMisuse
	{
		[Fact]
		public async Task FromKeyOfUnsupportedTypeOnAHookParameter_ReportsAwt170()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;

			                                       namespace MyCode;

			                                       public interface IChannel { }
			                                       public sealed class Fast : IChannel { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Fast, IChannel>(Key = "fast")]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, [FromKey(5)] IChannel channel) { }
			                                       }
			                                       """);

			// int is not a supported key type for a [FromKey], and a hook parameter is classified like a constructor
			// parameter, so it is rejected rather than silently treated as unkeyed.
			await That(result.Diagnostics.Any(d => d.Contains("AWT170"))).IsTrue()
				.Because("a hook parameter's [FromKey] is validated exactly like a constructor parameter's");
		}

		[Fact]
		public async Task KeyedCollectionHookParameterWithUnsupportedKeyType_ReportsAwt159()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IChannel { }
			                                       public sealed class Fast : IChannel { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Fast, IChannel>(Key = "fast")]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, IReadOnlyDictionary<int, IChannel> channels) { }
			                                       }
			                                       """);

			// int is neither string nor an enum, so a keyed dictionary cannot synthesize under it (AWT159), for a hook
			// parameter as for a constructor one.
			await That(result.Diagnostics.Any(d => d.Contains("AWT159"))).IsTrue()
				.Because("a keyed-collection hook parameter is validated like a constructor parameter's");
		}

		[Fact]
		public async Task FromKeyOnASynthesizedKeyedDictionaryHookParameter_ReportsAwt160()
		{
			GeneratorResult result = Generator.Run("""
			                                       using Awaiten;
			                                       using System.Collections.Generic;

			                                       namespace MyCode;

			                                       public interface IChannel { }
			                                       public sealed class Fast : IChannel { }
			                                       public sealed class Service { }

			                                       [Container]
			                                       [Singleton<Fast, IChannel>(Key = "fast")]
			                                       [Singleton<Service>(OnActivated = nameof(Started))]
			                                       public static partial class MyContainer
			                                       {
			                                       	private static void Started(Service service, [FromKey("fast")] IReadOnlyDictionary<string, IChannel> channels) { }
			                                       }
			                                       """);

			// A [FromKey] cannot select within the synthesized dictionary, so it is rejected (AWT160) on a hook
			// parameter exactly as on a constructor one.
			await That(result.Diagnostics.Any(d => d.Contains("AWT160"))).IsTrue()
				.Because("a [FromKey] on a synthesized keyed-collection hook parameter is rejected like a constructor parameter's");
		}
	}
}
