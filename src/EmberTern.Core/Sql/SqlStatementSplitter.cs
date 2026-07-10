using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language;

namespace EmberTern.Core.Sql;

/// <summary>
/// Splits a multi-statement SQL/DDL string into individual statements for the
/// one-statement-per-<c>FbCommand</c> execution loop. Rides the single statement-boundary
/// authority — <see cref="SqlParser"/> — instead of a private scanner (Etap 2; audit O5): the
/// parser's statement spans ARE the split points, and this class only applies the legacy
/// post-processing (trim, strip one trailing <c>;</c>, drop empties).
/// <para>
/// <b>§0 (Paramount Law):</b> this output is the exact DDL sent to the server, so it must stay
/// <b>byte-for-byte identical</b> to the previous char-based splitter. That equivalence is pinned
/// by a differential corpus test (old algorithm vs. this) plus the long-standing pinned splitter
/// cases; the migration was gated on that diff being clean.
/// </para>
/// </summary>
public static class SqlStatementSplitter
{
    /// <summary>
    /// Splits <paramref name="sql"/> into individual statements. Plain DDL/DML terminates at the
    /// next top-level <c>;</c>; a <c>CREATE/ALTER/RECREATE</c> of a <c>PROCEDURE/TRIGGER/FUNCTION/
    /// PACKAGE</c> stays whole (its DECLARE-section and body semicolons do not split it). Each
    /// result is trimmed, has a single trailing <c>;</c> stripped, and empty segments are dropped.
    /// </summary>
    public static IReadOnlyList<string> Split(string sql)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) return result;

        var root = SqlParser.Parse(sql).Root;
        foreach (var statement in root.Statements)
        {
            AddStatement(sql.Substring(statement.Start, statement.Length), result);
        }
        return result;
    }

    // Trims the raw statement text, strips a single trailing terminator ';' + surrounding
    // whitespace, and drops it when empty — identical to the legacy splitter's post-processing.
    private static void AddStatement(string raw, List<string> sink)
    {
        var trimmed = raw.Trim();
        if (trimmed.EndsWith(";", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
        }
        if (trimmed.Length > 0) sink.Add(trimmed);
    }
}
