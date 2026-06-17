using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql;

/// <summary>One procedure parameter, mutable so the editable grids round-trip it.
/// <see cref="TypeText"/> is the raw type spec verbatim (e.g. <c>VARCHAR(50)</c>,
/// <c>NUMERIC(18,4)</c>, a domain name, or <c>TYPE OF COLUMN T.C</c>) — kept as
/// free text so every Firebird parameter type form survives without modelling.</summary>
public sealed class ProcedureParameter
{
    public string Name { get; set; } = string.Empty;
    public string TypeText { get; set; } = string.Empty;
    public bool NotNull { get; set; }
    public string? DefaultValue { get; set; }
}

/// <summary>Result of parsing a <c>CREATE [OR ALTER] PROCEDURE</c> statement into
/// its editable parts. <see cref="Success"/> is false when the text doesn't match
/// the expected shape — callers keep their last-good model and surface a
/// non-blocking notice rather than discarding the user's edits.</summary>
public sealed class ProcedureSignature
{
    public bool Success { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<ProcedureParameter> Inputs { get; init; } = Array.Empty<ProcedureParameter>();
    public IReadOnlyList<ProcedureParameter> Outputs { get; init; } = Array.Empty<ProcedureParameter>();
    /// <summary>Everything after the header <c>AS</c> — the DECLARE…BEGIN…END body.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// Bounded parser for <c>CREATE [OR ALTER] PROCEDURE name [(in)] [RETURNS (out)] AS body</c>.
/// Not a full PSQL grammar — it only splits the fixed header (name + param lists)
/// from the body. Used for the Source→Easy round-trip; pure + testable without a DB.
/// </summary>
public static class ProcedureSignatureParser
{
    public static ProcedureSignature Parse(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Fail();
        var s = sql!;
        int i = 0;

        SqlScanHelpers.SkipTrivia(s, ref i);
        if (!SqlScanHelpers.TryKeyword(s, ref i, "CREATE")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);
        if (SqlScanHelpers.TryKeyword(s, ref i, "OR"))
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (!SqlScanHelpers.TryKeyword(s, ref i, "ALTER")) return Fail();
            SqlScanHelpers.SkipTrivia(s, ref i);
        }
        if (!SqlScanHelpers.TryKeyword(s, ref i, "PROCEDURE")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var name = ReadFoldedIdentifier(s, ref i);
        if (string.IsNullOrEmpty(name)) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var inputs = new List<ProcedureParameter>();
        var outputs = new List<ProcedureParameter>();

        if (i < s.Length && s[i] == '(')
        {
            var inner = SqlScanHelpers.ReadParenBlock(s, ref i);
            if (inner is null) return Fail();
            ParseParamList(inner, inputs);
            SqlScanHelpers.SkipTrivia(s, ref i);
        }

        if (SqlScanHelpers.TryKeyword(s, ref i, "RETURNS"))
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (i >= s.Length || s[i] != '(') return Fail();
            var inner = SqlScanHelpers.ReadParenBlock(s, ref i);
            if (inner is null) return Fail();
            ParseParamList(inner, outputs);
            SqlScanHelpers.SkipTrivia(s, ref i);
        }

        if (!SqlScanHelpers.TryKeyword(s, ref i, "AS")) return Fail();

        var body = s.Substring(i).Trim();
        return new ProcedureSignature
        {
            Success = true,
            Name = name,
            Inputs = inputs,
            Outputs = outputs,
            Body = body,
        };
    }

    private static ProcedureSignature Fail() => new() { Success = false };

    private static void ParseParamList(string inner, List<ProcedureParameter> into)
    {
        foreach (var seg in SqlScanHelpers.SplitTopLevelCommas(inner))
        {
            var p = ParseSegment(seg);
            if (p is not null) into.Add(p);
        }
    }

    // Firebird folds unquoted identifiers to uppercase; quoted identifiers keep
    // their literal case. Match that so parsed names align with the catalog.
    private static string? ReadFoldedIdentifier(string s, ref int i)
    {
        bool quoted = i < s.Length && s[i] == '"';
        var name = SqlScanHelpers.ReadIdentifier(s, ref i);
        if (name is null) return null;
        return quoted ? name : name.ToUpperInvariant();
    }

    /// <summary>Parses one <c>name type [NOT NULL] [= default]</c> segment into a
    /// <see cref="ProcedureParameter"/> (also reused for variable declarations by the
    /// body splitter). Returns null when there's no identifier.</summary>
    internal static ProcedureParameter? ParseSegment(string seg)
    {
        int i = 0;
        SqlScanHelpers.SkipTrivia(seg, ref i);
        var name = ReadFoldedIdentifier(seg, ref i);
        if (string.IsNullOrEmpty(name)) return null;

        var rest = seg.Substring(i).Trim();

        // Split off the default: `= value` or `DEFAULT value` at top level.
        string? defaultValue = null;
        var (typeAndFlags, def) = SplitDefault(rest);
        if (def is not null) defaultValue = def.Trim();

        // Trailing NOT NULL on the type/flags part.
        bool notNull = false;
        var typeText = typeAndFlags.Trim();
        if (EndsWithWord(typeText, "NULL") && EndsWithWords(typeText, "NOT", "NULL"))
        {
            notNull = true;
            typeText = StripTrailingWords(typeText, "NOT", "NULL").Trim();
        }

        return new ProcedureParameter
        {
            Name = name!,
            TypeText = typeText,
            NotNull = notNull,
            DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue,
        };
    }

    // Returns (beforeDefault, defaultOrNull). Scans for a top-level '=' or the
    // DEFAULT keyword (string/paren-aware).
    private static (string, string?) SplitDefault(string s)
    {
        int i = 0;
        int depth = 0;
        while (i < s.Length)
        {
            if (SqlScanHelpers.TrySkipQuoted(s, ref i)) continue;
            char c = s[i];
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }
            if (depth == 0)
            {
                if (c == '=')
                {
                    return (s.Substring(0, i), s.Substring(i + 1));
                }
                if ((char.ToUpperInvariant(c) == 'D') && SqlScanHelpers.IsIdentifierChar(c))
                {
                    int j = i;
                    if (SqlScanHelpers.TryKeyword(s, ref j, "DEFAULT")
                        && IsWordBoundaryBefore(s, i))
                    {
                        return (s.Substring(0, i), s.Substring(j));
                    }
                }
            }
            i++;
        }
        return (s, null);
    }

    private static bool IsWordBoundaryBefore(string s, int i)
        => i == 0 || !SqlScanHelpers.IsIdentifierChar(s[i - 1]);

    private static bool EndsWithWord(string s, string word)
    {
        s = s.TrimEnd();
        if (s.Length < word.Length) return false;
        var tail = s.Substring(s.Length - word.Length);
        if (!string.Equals(tail, word, StringComparison.OrdinalIgnoreCase)) return false;
        int before = s.Length - word.Length - 1;
        return before < 0 || !SqlScanHelpers.IsIdentifierChar(s[before]);
    }

    private static bool EndsWithWords(string s, string first, string second)
    {
        if (!EndsWithWord(s, second)) return false;
        var trimmed = StripTrailingWords(s, second);
        return EndsWithWord(trimmed, first);
    }

    private static string StripTrailingWords(string s, params string[] words)
    {
        // Strip the trailing words right-to-left.
        var cur = s.TrimEnd();
        for (int k = words.Length - 1; k >= 0; k--)
        {
            var w = words[k];
            cur = cur.TrimEnd();
            if (cur.Length >= w.Length
                && string.Equals(cur.Substring(cur.Length - w.Length), w, StringComparison.OrdinalIgnoreCase))
            {
                cur = cur.Substring(0, cur.Length - w.Length);
            }
        }
        return cur;
    }
}
