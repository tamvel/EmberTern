using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql;

/// <summary>One occurrence of a named parameter (<c>:name</c> or <c>@name</c>) in a SQL
/// statement — its name, source offset, total length (marker + name), and marker char.</summary>
public sealed record SqlParameter(string Name, int Offset, int Length, char Marker);

/// <summary>
/// Extracts named parameters (<c>:name</c> / <c>@name</c>) from a SQL statement for the "Smart SQL
/// Parameters" feature. Built on the shared <see cref="SqlLexer"/> (Etap 2): parameters are simply
/// the lexer's <see cref="TokenKind.Parameter"/> tokens, so string literals (<c>'…'</c>), quoted
/// identifiers (<c>"…"</c>) and comments are already opaque, and <c>::</c> is the cast operator
/// (not a parameter). EXECUTE BLOCK is excluded because its <c>:vars</c> are block locals, not
/// input parameters.
/// </summary>
public static class SqlParameterScanner
{
    /// <summary>Every <c>:name</c> / <c>@name</c> occurrence in order, with offsets. Positional
    /// <c>?</c> markers (which have no name) and everything inside literals/comments/quoted
    /// identifiers are excluded — a direct consequence of the lexer's token kinds.</summary>
    public static IReadOnlyList<SqlParameter> Scan(string? sql)
    {
        var result = new List<SqlParameter>();
        if (string.IsNullOrEmpty(sql)) return result;

        foreach (var token in SqlLexer.Tokenize(sql!))
        {
            if (token.Kind != TokenKind.Parameter || token.Length < 2) continue;
            char marker = token.Text[0];
            if (marker is ':' or '@')
            {
                result.Add(new SqlParameter(token.Text.Substring(1), token.Start, token.Length, marker));
            }
        }
        return result;
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
    /// NOT input parameters, so it must be excluded from named-parameter collection. Determined
    /// from the parsed statement kind.</summary>
    public static bool IsExecuteBlock(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return false;
        var statements = SqlParser.Parse(sql!).Root.Statements;
        return statements.Count > 0 && statements[0] is ExecuteBlockStatement;
    }

    /// <summary>If the statement is <c>EXECUTE PROCEDURE name …</c>, returns the procedure name so
    /// its catalog parameter types can be resolved (unquoted names are upper-cased to match the
    /// catalog; a quoted name keeps its case); otherwise null. Read from the parsed statement.</summary>
    public static string? TryExtractExecuteProcedureName(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return null;
        var statements = SqlParser.Parse(sql!).Root.Statements;
        if (statements.Count == 0 || statements[0] is not ExecuteProcedureStatement ep) return null;
        // A package-qualified call keeps its qualifier so the catalog lookup can find the package member
        // (PKG.PROC), not a nonexistent standalone routine named PROC (Stage X / D11).
        return ep.PackageName is null ? ep.ProcedureName : ep.PackageName + "." + ep.ProcedureName;
    }
}
