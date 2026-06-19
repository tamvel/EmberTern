using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql;

/// <summary>Result of parsing a <c>CREATE [OR ALTER] VIEW</c> statement into its
/// editable parts for the View Detail Easy mode. <see cref="Success"/> is false when
/// the text doesn't match the expected shape — callers keep their last-good model and
/// surface a non-blocking notice rather than discarding the user's edits (same
/// contract as <see cref="ProcedureSignature"/>).</summary>
public sealed class ViewSignature
{
    public bool Success { get; init; }
    public string? Name { get; init; }

    /// <summary>True when the source used <c>CREATE OR ALTER VIEW</c> (vs a plain
    /// <c>CREATE VIEW</c>). Preserved so Source → Easy → Source keeps the original
    /// verb instead of silently rewriting it.</summary>
    public bool OrAlter { get; init; }

    /// <summary>The explicit column list, folded to Firebird's catalog form (unquoted
    /// → upper, quoted → literal). Empty when the source had NO <c>(...)</c> list — the
    /// rebuild then omits the clause, so both <c>CREATE VIEW V AS …</c> and
    /// <c>CREATE VIEW V (A, B) AS …</c> round-trip without gaining or losing a list.</summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    /// <summary>Everything after the header <c>AS</c> — the SELECT body, verbatim
    /// (trimmed of surrounding whitespace).</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// Bounded parser for <c>CREATE [OR ALTER] VIEW name [(c1, c2, …)] AS body</c>. Not a
/// full grammar — it only splits the fixed header (name + optional column list) from
/// the body. Used for the View Detail Source→Easy round-trip; pure + testable without
/// a DB. Mirrors <see cref="ProcedureSignatureParser"/> and reuses the same
/// <see cref="SqlScanHelpers"/> primitives.
/// </summary>
public static class ViewSignatureParser
{
    public static ViewSignature Parse(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Fail();
        var s = sql!;
        int i = 0;

        SqlScanHelpers.SkipTrivia(s, ref i);
        if (!SqlScanHelpers.TryKeyword(s, ref i, "CREATE")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        bool orAlter = false;
        if (SqlScanHelpers.TryKeyword(s, ref i, "OR"))
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (!SqlScanHelpers.TryKeyword(s, ref i, "ALTER")) return Fail();
            orAlter = true;
            SqlScanHelpers.SkipTrivia(s, ref i);
        }

        if (!SqlScanHelpers.TryKeyword(s, ref i, "VIEW")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var name = ReadFoldedIdentifier(s, ref i);
        if (string.IsNullOrEmpty(name)) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var columns = new List<string>();
        if (i < s.Length && s[i] == '(')
        {
            var inner = SqlScanHelpers.ReadParenBlock(s, ref i);
            if (inner is null) return Fail();
            ParseColumnList(inner, columns);
            SqlScanHelpers.SkipTrivia(s, ref i);
        }

        if (!SqlScanHelpers.TryKeyword(s, ref i, "AS")) return Fail();

        var body = s.Substring(i).Trim();
        return new ViewSignature
        {
            Success = true,
            Name = name,
            OrAlter = orAlter,
            Columns = columns,
            Body = body,
        };
    }

    private static ViewSignature Fail() => new() { Success = false };

    private static void ParseColumnList(string inner, List<string> into)
    {
        foreach (var seg in SqlScanHelpers.SplitTopLevelCommas(inner))
        {
            int k = 0;
            var name = ReadFoldedIdentifier(seg, ref k);
            if (!string.IsNullOrEmpty(name)) into.Add(name!);
        }
    }

    // Firebird folds unquoted identifiers to uppercase; quoted identifiers keep their
    // literal case. Match that so parsed names align with the catalog.
    private static string? ReadFoldedIdentifier(string s, ref int i)
    {
        SqlScanHelpers.SkipTrivia(s, ref i);
        bool quoted = i < s.Length && s[i] == '"';
        var name = SqlScanHelpers.ReadIdentifier(s, ref i);
        if (name is null) return null;
        return quoted ? name : name.ToUpperInvariant();
    }
}
