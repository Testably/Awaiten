using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
// ReSharper disable once RedundantUsingDirective
using System;
// ReSharper disable once RedundantUsingDirective
using System.Collections.Generic;

namespace Build;

/// <summary>
///     Pure logic for transforming BenchmarkDotNet GitHub-flavored markdown reports
///     into the raw results of the PR comment.
/// </summary>
static class BenchmarkReport
{
    public static readonly string[] DefaultColumnsToRemove = ["RatioSD", "Gen0", "Gen1", "Gen2",];

    /// <summary>
    ///     Renders each report as a collapsed section, named after its benchmark class.
    /// </summary>
    public static string BuildRawResults(IReadOnlyList<string> reportFiles, string[] columnsToRemove)
    {
        StringBuilder sb = new();
        foreach (var reportFile in reportFiles)
        {
            var count = 0;
            sb.AppendLine("<details>");
            sb.AppendLine($"<summary>Raw results of {BenchmarkClassName(reportFile)}</summary>");
            sb.AppendLine();
            BenchmarkTableParser parser = new(columnsToRemove);
            List<TableRow> tableBuffer = new();
            foreach (var line in File.ReadAllLines(reportFile))
            {
                if (line.StartsWith("```"))
                {
                    FlushTableBuffer(sb, tableBuffer);
                    count++;
                    if (count == 1)
                    {
                        sb.AppendLine("<pre>");
                    }
                    else if (count == 2)
                    {
                        sb.AppendLine("</pre>");
                        sb.AppendLine();
                    }

                    parser.Reset();
                    continue;
                }

                if (!parser.TryConsume(line, out var rowIndex, out _, out var filteredTokens))
                {
                    FlushTableBuffer(sb, tableBuffer);
                    sb.AppendLine(line);
                    continue;
                }

                tableBuffer.Add(new TableRow(rowIndex, filteredTokens));
            }

            FlushTableBuffer(sb, tableBuffer);
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Reads <c>ResolveBenchmarks</c> from <c>Awaiten.Benchmarks.ResolveBenchmarks-report-github.md</c>.
    /// </summary>
    internal static string BenchmarkClassName(string reportFile)
    {
        var name = Path.GetFileName(reportFile);
        name = name.Substring(0, name.IndexOf("-report", StringComparison.Ordinal));
        return name.Substring(name.LastIndexOf('.') + 1);
    }

    internal static void FlushTableBuffer(StringBuilder sb, List<TableRow> tableBuffer)
    {
        if (tableBuffer.Count == 0) return;

        var commonPrefix = FindCommonRowPrefix(tableBuffer);
        var headerRow = tableBuffer[0].RowIndex == 0 ? tableBuffer[0] : null;

        foreach (var row in tableBuffer)
        {
            if (row.RowIndex >= BenchmarkTableParser.DataRowStartIndex && row.Tokens.Length == 0)
            {
                if (headerRow != null && headerRow.Tokens.Length > 0)
                {
                    var empty = new string[headerRow.Tokens.Length];
                    Array.Fill(empty, string.Empty);
                    sb.AppendLine(JoinTokens(empty));
                }

                continue;
            }

            var displayLine = JoinTokens(ApplyHeaderAndPrefixStripping(row, commonPrefix));
            var isAwaitenRow = row.RowIndex >= BenchmarkTableParser.DataRowStartIndex
                                 && row.Tokens.Length > 0
                                 && row.Tokens[0].Contains("_Awaiten", StringComparison.OrdinalIgnoreCase);
            if (isAwaitenRow)
                MakeLineBold(sb, displayLine);
            else
                sb.AppendLine(displayLine);
        }

        tableBuffer.Clear();
    }

    internal static string[] ApplyHeaderAndPrefixStripping(TableRow row, string? commonPrefix)
    {
        if (commonPrefix == null || row.Tokens.Length == 0) return row.Tokens;

        var result = (string[])row.Tokens.Clone();
        if (row.RowIndex == 0)
            result[0] = commonPrefix.TrimEnd('_');
        else if (row.RowIndex >= BenchmarkTableParser.DataRowStartIndex
                 && result[0].StartsWith(commonPrefix, StringComparison.Ordinal))
            result[0] = result[0].Substring(commonPrefix.Length);

        return result;
    }

    internal static string? FindCommonRowPrefix(List<TableRow> rows)
    {
        string? prefix = null;
        foreach (var row in rows)
        {
            if (row.RowIndex < BenchmarkTableParser.DataRowStartIndex) continue;

            if (row.Tokens.Length == 0) continue;

            var name = row.Tokens[0];
            var underscoreIdx = name.IndexOf('_');
            if (underscoreIdx <= 0) return null;

            var thisPrefix = name.Substring(0, underscoreIdx + 1);
            if (prefix == null)
                prefix = thisPrefix;
            else if (!string.Equals(prefix, thisPrefix, StringComparison.Ordinal)) return null;
        }

        return prefix;
    }

    internal static string JoinTokens(string[] tokens)
    {
        StringBuilder sb = new();
        sb.Append('|');
        foreach (var token in tokens)
        {
            sb.Append(' ');
            sb.Append(token);
            sb.Append(" |");
        }

        return sb.ToString();
    }

    internal static int[] DetermineDroppedColumnIndices(string headerLine, string[] columnsToRemove)
    {
        var tokens = SplitTableRow(headerLine);
        List<int> indices = new();
        for (var i = 0; i < tokens.Length; i++)
            foreach (var columnName in columnsToRemove)
                if (string.Equals(tokens[i], columnName, StringComparison.OrdinalIgnoreCase))
                {
                    indices.Add(i);
                    break;
                }

        return indices.ToArray();
    }

    internal static string RemoveColumns(string line, int[] droppedColumnIndices)
    {
        if (droppedColumnIndices.Length == 0) return line;

        var tokens = SplitTableRow(line);
        StringBuilder result = new();
        result.Append('|');
        for (var i = 0; i < tokens.Length; i++)
        {
            if (Array.IndexOf(droppedColumnIndices, i) >= 0) continue;

            result.Append(' ');
            result.Append(tokens[i]);
            result.Append(" |");
        }

        return result.ToString();
    }

    internal static string StripMarkdownBold(string token)
    {
        if (token.Length >= 4 && token.StartsWith("**", StringComparison.Ordinal)
                              && token.EndsWith("**", StringComparison.Ordinal))
            return token.Substring(2, token.Length - 4);

        return token;
    }

    internal static string[] SplitTableRow(string line)
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return [];

        var tokens = new string[parts.Length - 2];
        Array.Copy(parts, 1, tokens, 0, tokens.Length);
        return tokens;
    }

    internal static void MakeLineBold(StringBuilder sb, string line)
    {
        var tokens = line.Split("|", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        sb.Append('|');
        foreach (var token in tokens)
        {
            sb.Append(" **");
            sb.Append(token);
            sb.Append("** |");
        }

        sb.AppendLine();
    }
}

sealed record TableRow(int RowIndex, string[] Tokens);

/// <summary>
///     Parses the markdown tables produced by BenchmarkDotNet's GitHub exporter. Tables are framed by a
///     header row (index 0), a separator row (index 1), and data rows (index >= <see cref="DataRowStartIndex" />).
///     Auto-resets on the first non-table line after a table, so one instance can walk a document with
///     multiple tables.
/// </summary>
sealed class BenchmarkTableParser
{
    public const int DataRowStartIndex = 2;

    // ReSharper disable InconsistentNaming
    readonly string[] _columnsToRemove;
    int[]? _droppedColumnIndices;
    int _nextRowIndex;
    // ReSharper restore InconsistentNaming

    public BenchmarkTableParser(string[] columnsToRemove)
    {
        _columnsToRemove = columnsToRemove;
    }

    public bool TryConsume(string line, out int rowIndex,
        [NotNullWhen(true)] out string? filteredLine,
        [NotNullWhen(true)] out string[]? tokens)
    {
        rowIndex = -1;
        if (!line.StartsWith('|') || !line.EndsWith('|'))
        {
            if (_nextRowIndex > 0) Reset();

            filteredLine = null;
            tokens = null;
            return false;
        }

        _droppedColumnIndices ??= BenchmarkReport.DetermineDroppedColumnIndices(line, _columnsToRemove);
        filteredLine = BenchmarkReport.RemoveColumns(line, _droppedColumnIndices);
        tokens = filteredLine.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++) tokens[i] = BenchmarkReport.StripMarkdownBold(tokens[i]);

        rowIndex = _nextRowIndex;
        _nextRowIndex++;
        return true;
    }

    public void Reset()
    {
        _droppedColumnIndices = null;
        _nextRowIndex = 0;
    }
}
