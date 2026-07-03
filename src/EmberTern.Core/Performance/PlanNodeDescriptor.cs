using System;

namespace EmberTern.Core.Performance;

/// <summary>Parses the descriptor text of a single Explain-plan node (the part after the
/// "-&gt;" arrow, or a root's text) into a coarse access method + the table/index/detail
/// it names. Internal + static so it is unit-testable in isolation.</summary>
internal static class PlanNodeDescriptor
{
    internal readonly record struct Parsed(
        AccessMethod Method,
        string? TableName,
        string? Alias,
        string? IndexName,
        string? Detail);

    internal static Parsed Parse(string descriptor)
    {
        var text = (descriptor ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new Parsed(AccessMethod.Unknown, null, null, null, null);
        }

        if (StartsWithWord(text, "Table"))
        {
            var name = ExtractQuoted(text, out int afterName);
            string rest = text[afterName..].Trim();
            string? alias = null;
            if (StartsWithWord(rest, "as"))
            {
                alias = ExtractQuoted(rest, out int afterAlias);
                rest = rest[afterAlias..].Trim();
            }
            var method = rest.Contains("Access By ID", StringComparison.OrdinalIgnoreCase)
                ? AccessMethod.AccessById
                : rest.Contains("Full Scan", StringComparison.OrdinalIgnoreCase)
                    ? AccessMethod.FullScan
                    : AccessMethod.Unknown;
            return new Parsed(method, name, alias, null, rest.Length > 0 ? rest : null);
        }

        if (StartsWithWord(text, "Index"))
        {
            var name = ExtractQuoted(text, out int afterName);
            string rest = text[afterName..].Trim();
            return new Parsed(AccessMethod.IndexScan, null, null, name, rest.Length > 0 ? rest : null);
        }

        if (StartsWithWord(text, "Procedure"))
        {
            var name = ExtractQuoted(text, out int afterName);
            string rest = text[afterName..].Trim();
            return new Parsed(AccessMethod.ProcedureScan, name, null, null, rest.Length > 0 ? rest : null);
        }

        // Keyword-led node kinds (longest / most specific first).
        var kind = MatchKind(text);
        if (kind is { } k)
        {
            string detail = text[k.Keyword.Length..].Trim();
            return new Parsed(k.Method, null, null, null, detail.Length > 0 ? detail : null);
        }

        return new Parsed(AccessMethod.Unknown, null, null, null, text);
    }

    private readonly record struct Kind(string Keyword, AccessMethod Method);

    private static Kind? MatchKind(string text)
    {
        // Order matters: check multi-word keys before their prefixes.
        ReadOnlySpan<Kind> kinds =
        [
            new("Nested Loop Join", AccessMethod.NestedLoopJoin),
            new("Hash Join", AccessMethod.HashJoin),
            new("Sort Merge", AccessMethod.MergeJoin),
            new("Merge Join", AccessMethod.MergeJoin),
            new("Merge", AccessMethod.MergeJoin),
            new("Record Buffer", AccessMethod.RecordBuffer),
            new("Select Expression", AccessMethod.SelectExpression),
            new("Aggregate", AccessMethod.Aggregate),
            new("Sort", AccessMethod.Sort),
            new("Filter", AccessMethod.Filter),
            new("Bitmap", AccessMethod.Bitmap),
            new("Union", AccessMethod.Union),
        ];
        foreach (var kind in kinds)
        {
            if (StartsWithWord(text, kind.Keyword))
            {
                return kind;
            }
        }
        return null;
    }

    private static bool StartsWithWord(string text, string word)
    {
        if (text.Length < word.Length
            || string.Compare(text, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }
        // The next char (if any) must not continue an identifier word.
        if (text.Length == word.Length)
        {
            return true;
        }
        char next = text[word.Length];
        return !(char.IsLetterOrDigit(next) || next is '_');
    }

    private static string? ExtractQuoted(string text, out int afterClosingQuote)
    {
        afterClosingQuote = text.Length;
        int open = text.IndexOf('"');
        if (open < 0)
        {
            afterClosingQuote = 0;
            return null;
        }
        int close = text.IndexOf('"', open + 1);
        if (close < 0)
        {
            afterClosingQuote = text.Length;
            return null;
        }
        afterClosingQuote = close + 1;
        return text[(open + 1)..close];
    }
}
