using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmberTern.Core.Performance;

/// <summary>Parses a raw Firebird execution plan (Explain or Legacy form) into a
/// <see cref="PlanTree"/>. Tolerant by design: an unrecognized construct becomes an
/// <see cref="AccessMethod.Unknown"/> node that still retains its raw text, so parsing
/// never fails on an unexpected plan shape. Pure — no engine, no I/O.</summary>
public sealed class PlanParser
{
    public PlanTree Parse(RawPlanCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return capture.Dialect == PlanDialect.Legacy
            ? ParseLegacy(capture.PlanText)
            : ParseExplain(capture.PlanText);
    }

    // ---- Explain form ---------------------------------------------------------------
    // Select Expression [(line n, column m)]
    //     -> Nested Loop Join (inner)
    //         -> Table "PROJECT" Full Scan
    //         -> Filter
    //             -> Table "EMPLOYEE" Access By ID
    //                 -> Bitmap
    //                     -> Index "RDB$PRIMARY7" Unique Scan
    // A Firebird 6 plan may interleave "[cardinality=.., cost=..]" lines; those are
    // attached to the most recently opened node (null on FB3/4/5).
    private static PlanTree ParseExplain(string text)
    {
        var roots = new List<Builder>();
        var stack = new List<(int Indent, Builder Node)>();
        // Firebird 6 prints a "[cardinality=.., cost=..]" line immediately BEFORE the node
        // it annotates, so metrics are buffered and applied to the next node created.
        PlanNodeMetrics? pending = null;

        foreach (var rawLine in SplitLines(text))
        {
            var line = rawLine.TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var metrics = TryParseMetricsLine(line);
            if (metrics is not null)
            {
                pending = metrics;
                continue;
            }

            int indent = LeadingSpaces(line);
            var content = line.TrimStart();

            Builder node;
            if (content.StartsWith("->", StringComparison.Ordinal))
            {
                node = new Builder(content[2..].Trim());
                // Parent = nearest node with a strictly smaller indent.
                while (stack.Count > 0 && stack[^1].Indent >= indent)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                if (stack.Count > 0)
                {
                    stack[^1].Node.Children.Add(node);
                }
                else
                {
                    roots.Add(node); // arrow with no parent — treat as a root, be forgiving
                }
                stack.Add((indent, node));
            }
            else
            {
                // A non-arrow line starts a new root (e.g. "Select Expression").
                node = new Builder(content);
                roots.Add(node);
                stack.Clear();
                stack.Add((indent, node));
            }

            if (pending is not null)
            {
                node.Metrics = pending;
                pending = null;
            }
        }

        return new PlanTree
        {
            Dialect = PlanDialect.Explain,
            Roots = BuildAll(roots),
            RawText = text,
        };
    }

    // ---- Legacy form ----------------------------------------------------------------
    // PLAN (RDB$RELATIONS NATURAL)
    // PLAN JOIN (PROJECT NATURAL, EMPLOYEE INDEX (RDB$PRIMARY7))
    // Phase-1 pragmatic parse: extract the table -> access leaves (NATURAL / INDEX(...))
    // under a synthetic root. Explain is the primary path; legacy is the fallback, so a
    // faithful flat leaf list is enough to visualise access and flag full scans.
    private static PlanTree ParseLegacy(string text)
    {
        var root = new Builder("Select Expression");
        foreach (var (table, isNatural, indexList) in ScanLegacyLeaves(text))
        {
            var leaf = new Builder(table + (isNatural ? " NATURAL" : " INDEX (" + indexList + ")"))
            {
                Method = isNatural ? AccessMethod.FullScan : AccessMethod.IndexScan,
                TableName = table,
                IndexName = isNatural ? null : indexList,
                Detail = isNatural ? "NATURAL" : "INDEX (" + indexList + ")",
            };
            root.Children.Add(leaf);
        }

        return new PlanTree
        {
            Dialect = PlanDialect.Legacy,
            Roots = new List<PlanNode> { root.Build() },
            RawText = text,
        };
    }

    private static IEnumerable<(string Table, bool IsNatural, string IndexList)> ScanLegacyLeaves(string text)
    {
        // Walk tokens; on "<ident> NATURAL" or "<ident> INDEX ( ... )" emit a leaf.
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            if (!IsIdentStart(text[i]))
            {
                i++;
                continue;
            }
            int start = i;
            while (i < n && IsIdentChar(text[i]))
            {
                i++;
            }
            string ident = text[start..i];
            int j = SkipWs(text, i);
            if (Matches(text, j, "NATURAL"))
            {
                yield return (ident, true, string.Empty);
                i = j + "NATURAL".Length;
            }
            else if (Matches(text, j, "INDEX"))
            {
                int k = SkipWs(text, j + "INDEX".Length);
                if (k < n && text[k] == '(')
                {
                    int close = FindMatchingParen(text, k);
                    string list = close > k ? text[(k + 1)..close].Trim() : string.Empty;
                    yield return (ident, false, list);
                    i = close > k ? close + 1 : k + 1;
                }
            }
        }
    }

    // ---- helpers --------------------------------------------------------------------

    private static PlanNodeMetrics? TryParseMetricsLine(string line)
    {
        var t = line.Trim();
        if (t.Length < 2 || t[0] != '[' || t[^1] != ']')
        {
            return null;
        }
        double? card = TryReadNamed(t, "cardinality=");
        double? cost = TryReadNamed(t, "cost=");
        if (card is null && cost is null)
        {
            return null;
        }
        return new PlanNodeMetrics(card, cost);
    }

    private static double? TryReadNamed(string text, string key)
    {
        int at = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }
        int s = at + key.Length;
        int e = s;
        while (e < text.Length && (char.IsDigit(text[e]) || text[e] is '.' or '-' or '+' or 'e' or 'E'))
        {
            e++;
        }
        return double.TryParse(text[s..e], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int LeadingSpaces(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }
        return i;
    }

    private static IEnumerable<string> SplitLines(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static IReadOnlyList<PlanNode> BuildAll(List<Builder> builders)
    {
        var list = new List<PlanNode>(builders.Count);
        foreach (var b in builders)
        {
            list.Add(b.Build());
        }
        return list;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c is '_' or '$' or '"';
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '"' or '.';
    private static int SkipWs(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }
        return i;
    }

    private static bool Matches(string s, int at, string word)
        => at + word.Length <= s.Length
           && string.Compare(s, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
           && (at + word.Length == s.Length || !IsIdentChar(s[at + word.Length]));

    private static int FindMatchingParen(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '(')
            {
                depth++;
            }
            else if (s[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    /// <summary>Mutable tree node used only during parsing; converted to immutable
    /// <see cref="PlanNode"/> records at the end.</summary>
    private sealed class Builder
    {
        public Builder(string descriptor)
        {
            RawText = descriptor;
            var parsed = PlanNodeDescriptor.Parse(descriptor);
            Method = parsed.Method;
            TableName = parsed.TableName;
            Alias = parsed.Alias;
            IndexName = parsed.IndexName;
            Detail = parsed.Detail;
        }

        public AccessMethod Method { get; set; }
        public string? TableName { get; set; }
        public string? Alias { get; set; }
        public string? IndexName { get; set; }
        public string? Detail { get; set; }
        public string RawText { get; set; }
        public PlanNodeMetrics? Metrics { get; set; }
        public List<Builder> Children { get; } = new();

        public PlanNode Build()
        {
            var kids = new List<PlanNode>(Children.Count);
            foreach (var c in Children)
            {
                kids.Add(c.Build());
            }
            return new PlanNode
            {
                Method = Method,
                TableName = TableName,
                Alias = Alias,
                IndexName = IndexName,
                Detail = Detail,
                Metrics = Metrics,
                RawText = RawText,
                Children = kids,
            };
        }
    }
}
