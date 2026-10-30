using System.Collections.Generic;
using System.Linq;

namespace Awaiten.Docs.Tests;

/// <summary>
///     A <c>```csharp</c> code block of a documentation page, starting at the one-based <paramref name="Line" />.
/// </summary>
/// <param name="Expect">
///     The comma-separated diagnostic ids that the block shows on purpose, from an <c>expect=</c> marker, or empty
///     when the block has to compile cleanly.
/// </param>
internal sealed record CodeBlock(string Page, int Line, string Code, string Expect = "")
{
	/// <summary>
	///     The info-string marker that excludes a block from the compile check.
	/// </summary>
	public const string NoCompile = "no-compile";

	/// <summary>
	///     The info-string prefix of the diagnostic ids that a block reports on purpose, e.g. <c>expect=AWT101,AWT102</c>.
	/// </summary>
	public const string ExpectPrefix = "expect=";

	/// <summary>
	///     The number of lines of the <see cref="Code" />.
	/// </summary>
	public int LineCount => Code.Split('\n').Length;

	/// <summary>
	///     The distinct diagnostic ids of the <see cref="Expect" /> marker.
	/// </summary>
	public IReadOnlyCollection<string> ExpectedIds
		=> Expect.Split(',').Where(id => id.Length > 0).Distinct().ToArray();

	/// <summary>
	///     Extracts the <c>```csharp</c> blocks from the <paramref name="lines" /> of the <paramref name="page" />,
	///     except the ones marked with <see cref="NoCompile" />.
	/// </summary>
	public static List<CodeBlock> Extract(string page, string[] lines)
	{
		List<CodeBlock> blocks = [];
		for (int i = 0; i < lines.Length; i++)
		{
			string fence = lines[i].TrimStart();
			if (!fence.StartsWith("```"))
			{
				continue;
			}

			string[] info = fence.Substring(3).Split(' ');
			int start = i + 1;
			do
			{
				i++;
			} while (i < lines.Length && lines[i].Trim() != "```");

			if (info[0] == "csharp" && !info.Contains(NoCompile))
			{
				string expect = info.FirstOrDefault(part => part.StartsWith(ExpectPrefix))?.Substring(ExpectPrefix.Length)
				                ?? "";
				blocks.Add(new CodeBlock(page, start + 1, string.Join("\n", lines, start, i - start), expect));
			}
		}

		return blocks;
	}
}
