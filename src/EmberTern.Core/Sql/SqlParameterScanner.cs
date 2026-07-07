using System;
using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql;

/// <summary>One occurrence of a named parameter (<c>:name</c> or <c>@name</c>) in a SQL
/// statement — its name, source offset, total length (marker + name), and marker char.</summary>
public sealed record SqlParameter(string Name, int Offset, int Length, char Marker);

/// <summary>
/// Extracts named parameters (<c>:name</c> / <c>@name</c>) from a SQL statement for the "Smart SQL
/// Parameters" feature. Built entirely on <see cref="SqlScanHelpers"/> — <b>no regex</b>: string
/// literals (<c>'…'</c>), quoted identifiers (<c>"…"</c>) and line/block comments are skipped, and
/// <c>::</c> is treated as the cast operator (not a parameter). EXECUTE BLOCK is excluded because
/// its <c>:vars</c> are block locals, not input parameters.
/// </summary>
public static class SqlParameterScanner
{
    /// <summary>Every <c>:name</c> / <c>@name</c> occurrence in order, with offsets. Literals,
    /// quoted identifiers and comments are skipped; <c>::</c> is not a parameter.</summary>
    public static IReadOnlyList<SqlParameter> Scan(string? sql)
    {
        var result = new List<SqlParameter>();
        if (string.IsNullOrEmpty(sql)) return result;

        int i = 0, n = sql!.Length;
        while (i < n)
        {
            SqlScanHelpers.SkipTrivia(sql, ref i);         // whitespace + -- and /* */ comments
            if (i >= n) break;
            if (SqlScanHelpers.TrySkipQuoted(sql, ref i)) continue; // '…' string / "…" identifier

            char c = sql[i];
            if (c == ':')
            {
                if (i + 1 < n && sql[i + 1] == ':') { i += 2; continue; } // :: cast — not a parameter
                int marker = i;
                i++;
                var name = ReadName(sql, ref i);
                if (name is not null) result.Add(new SqlParameter(name, marker, i - marker, ':'));
                continue; // a lone ':' just advances past the colon
            }
            if (c == '@')
            {
                int marker = i;
                i++;
                var name = ReadName(sql, ref i);
                if (name is not null) result.Add(new SqlParameter(name, marker, i - marker, '@'));
                continue; // a lone '@' advances past it
            }
            i++;
        }
        return result;
    }

    // A parameter name starts with a letter or underscore, then identifier chars (letters/digits/_/$).
    private static string? ReadName(string s, ref int i)
    {
        if (i >= s.Length) return null;
        char first = s[i];
        if (!(char.IsLetter(first) || first == '_')) return null;
        int start = i;
        while (i < s.Length && SqlScanHelpers.IsIdentifierChar(s[i])) i++;
        return s.Substring(start, i - start);
    }

    /// <summary>
    /// Rewrites every scanned <c>:name</c> / <c>@name</c> to the driver's <c>@name</c> marker,
    /// normalizing case-insensitive-equal names to the first occurrence's spelling, and returns the
    /// rewritten SQL plus the ordered unique parameter names (without the <c>@</c>). Literals and
    /// comments are untouched (they were never scanned). No parameters → the SQL is returned as-is.
    /// </summary>
    public static (string Sql, IReadOnlyList<string> Names) RewriteToDriverMarkers(string? sql)
    {
        var occurrences = Scan(sql);
        if (occurrences.Count == 0) return (sql ?? string.Empty, Array.Empty<string>());

        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var p in occurrences)
        {
            if (!canonical.ContainsKey(p.Name)) { canonical[p.Name] = p.Name; order.Add(p.Name); }
        }

        var sb = new StringBuilder(sql!.Length + occurrences.Count);
        int prev = 0;
        foreach (var p in occurrences) // ascending offset
        {
            sb.Append(sql, prev, p.Offset - prev);
            sb.Append('@').Append(canonical[p.Name]);
            prev = p.Offset + p.Length;
        }
        sb.Append(sql, prev, sql.Length - prev);
        return (sb.ToString(), order);
    }

    /// <summary>True when the statement is an EXECUTE BLOCK — its <c>:vars</c> are block locals,
    /// NOT input parameters, so it must be excluded from named-parameter collection.</summary>
    public static bool IsExecuteBlock(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return false;
        int i = 0;
        SqlScanHelpers.SkipTrivia(sql!, ref i);
        if (!SqlScanHelpers.TryKeyword(sql!, ref i, "EXECUTE")) return false;
        SqlScanHelpers.SkipTrivia(sql!, ref i);
        return SqlScanHelpers.TryKeyword(sql!, ref i, "BLOCK");
    }

    /// <summary>If the statement is <c>EXECUTE PROCEDURE name …</c>, returns the procedure name so
    /// its catalog parameter types can be resolved (unquoted names are upper-cased to match the
    /// catalog; a quoted name keeps its case); otherwise null.</summary>
    public static string? TryExtractExecuteProcedureName(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return null;
        int i = 0;
        SqlScanHelpers.SkipTrivia(sql!, ref i);
        if (!SqlScanHelpers.TryKeyword(sql!, ref i, "EXECUTE")) return null;
        SqlScanHelpers.SkipTrivia(sql!, ref i);
        if (!SqlScanHelpers.TryKeyword(sql!, ref i, "PROCEDURE")) return null;
        SqlScanHelpers.SkipTrivia(sql!, ref i);

        bool quoted = i < sql!.Length && sql[i] == '"';
        var name = SqlScanHelpers.ReadIdentifier(sql!, ref i);
        if (string.IsNullOrEmpty(name)) return null;
        return quoted ? name! : name!.ToUpperInvariant();
    }
}
