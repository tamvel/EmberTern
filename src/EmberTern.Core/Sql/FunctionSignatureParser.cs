using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql;

/// <summary>Result of parsing a <c>CREATE [OR ALTER] FUNCTION</c> statement into its
/// editable parts. <see cref="Success"/> is false when the text doesn't match the
/// expected shape (e.g. a legacy external/UDF declaration with no PSQL body) — callers
/// keep their last-good model and surface a non-blocking notice rather than discarding
/// the user's edits.</summary>
public sealed class FunctionSignature
{
    public bool Success { get; init; }
    public string? Name { get; init; }
    /// <summary>Input arguments — same shape as procedure input parameters.</summary>
    public IReadOnlyList<ProcedureParameter> Arguments { get; init; } = Array.Empty<ProcedureParameter>();
    /// <summary>The single return type spec verbatim (e.g. <c>INTEGER</c>,
    /// <c>VARCHAR(50)</c>, a domain, or <c>TYPE OF COLUMN T.C</c>).</summary>
    public string ReturnType { get; init; } = string.Empty;
    /// <summary>True when the function is declared <c>DETERMINISTIC</c>.</summary>
    public bool Deterministic { get; init; }
    /// <summary>Everything after the header <c>AS</c> — the DECLARE…BEGIN…END body.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// Bounded parser for
/// <c>CREATE [OR ALTER] FUNCTION name [(args)] RETURNS &lt;type&gt; [DETERMINISTIC] AS body</c>.
/// Not a full PSQL grammar — it splits the fixed header (name + argument list + single
/// return type) from the body. Argument segments reuse
/// <see cref="ProcedureSignatureParser.ParseSegment"/>; the body is taken verbatim after
/// the top-level <c>AS</c>. Used for the Function Detail Source→Easy round-trip; pure +
/// testable without a DB.
/// </summary>
public static class FunctionSignatureParser
{
    public static FunctionSignature Parse(string? sql)
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
        if (!SqlScanHelpers.TryKeyword(s, ref i, "FUNCTION")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var name = ReadFoldedIdentifier(s, ref i);
        if (string.IsNullOrEmpty(name)) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var args = new List<ProcedureParameter>();
        if (i < s.Length && s[i] == '(')
        {
            var inner = SqlScanHelpers.ReadParenBlock(s, ref i);
            if (inner is null) return Fail();
            foreach (var seg in SqlScanHelpers.SplitTopLevelCommas(inner))
            {
                var p = ProcedureSignatureParser.ParseSegment(seg);
                if (p is not null) args.Add(p);
            }
            SqlScanHelpers.SkipTrivia(s, ref i);
        }

        if (!SqlScanHelpers.TryKeyword(s, ref i, "RETURNS")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        // Read the return-type text up to the top-level body 'AS'. The type spec may
        // carry parens (VARCHAR(50)), CHARACTER SET / COLLATE, TYPE OF COLUMN, and a
        // trailing DETERMINISTIC — all captured here, then DETERMINISTIC split off below.
        int returnStart = i;
        int asStart = -1;
        int depth = 0;
        int j = i;
        while (j < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref j); // also skips comments, so /* AS */ can't false-match
            if (j >= s.Length) break;
            if (SqlScanHelpers.TrySkipQuoted(s, ref j)) continue;
            char c = s[j];
            if (c == '(') { depth++; j++; continue; }
            if (c == ')') { if (depth > 0) depth--; j++; continue; }
            if (depth == 0 && SqlScanHelpers.IsIdentifierChar(c))
            {
                int w = j;
                var word = SqlScanHelpers.ReadWord(s, ref j).ToUpperInvariant();
                if (word == "AS") { asStart = w; break; }
                continue;
            }
            j++;
        }
        if (asStart < 0) return Fail();

        var returnText = s.Substring(returnStart, asStart - returnStart).Trim();
        var body = s.Substring(j).Trim();

        returnText = StripDeterministic(returnText, out bool deterministic);
        if (string.IsNullOrWhiteSpace(returnText)) return Fail();

        return new FunctionSignature
        {
            Success = true,
            Name = name,
            Arguments = args,
            ReturnType = returnText,
            Deterministic = deterministic,
            Body = body,
        };
    }

    private static FunctionSignature Fail() => new() { Success = false };

    // Firebird folds unquoted identifiers to uppercase; quoted identifiers keep their
    // literal case. Match that so parsed names align with the catalog.
    private static string? ReadFoldedIdentifier(string s, ref int i)
    {
        bool quoted = i < s.Length && s[i] == '"';
        var name = SqlScanHelpers.ReadIdentifier(s, ref i);
        if (name is null) return null;
        return quoted ? name : name.ToUpperInvariant();
    }

    // Detects + strips a trailing [NOT] DETERMINISTIC keyword from the return-type text.
    private static string StripDeterministic(string returnText, out bool deterministic)
    {
        deterministic = false;
        if (!EndsWithWord(returnText, "DETERMINISTIC")) return returnText;

        var head = StripTrailingWord(returnText, "DETERMINISTIC").TrimEnd();
        if (EndsWithWord(head, "NOT"))
        {
            deterministic = false;
            return StripTrailingWord(head, "NOT").Trim();
        }
        deterministic = true;
        return head.Trim();
    }

    private static bool EndsWithWord(string s, string word)
    {
        s = s.TrimEnd();
        if (s.Length < word.Length) return false;
        var tail = s.Substring(s.Length - word.Length);
        if (!string.Equals(tail, word, StringComparison.OrdinalIgnoreCase)) return false;
        int before = s.Length - word.Length - 1;
        return before < 0 || !SqlScanHelpers.IsIdentifierChar(s[before]);
    }

    private static string StripTrailingWord(string s, string word)
    {
        var cur = s.TrimEnd();
        if (cur.Length >= word.Length
            && string.Equals(cur.Substring(cur.Length - word.Length), word, StringComparison.OrdinalIgnoreCase))
        {
            return cur.Substring(0, cur.Length - word.Length);
        }
        return cur;
    }
}
