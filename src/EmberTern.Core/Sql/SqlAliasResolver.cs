using System;
using System.Collections.Generic;
using Lang = EmberTern.Core.Sql.Language;

namespace EmberTern.Core.Sql;

/// <summary>
/// Extracts table aliases from a SQL statement so the editor's dot autocomplete
/// can resolve <c>ALIAS.</c> back to a table name. Pure string processing — no
/// FB driver involvement.
/// </summary>
/// <remarks>
/// Handles:
///   <list type="bullet">
///     <item><c>FROM TABLE</c></item>
///     <item><c>FROM TABLE alias</c></item>
///     <item><c>FROM TABLE AS alias</c></item>
///     <item><c>FROM A a, B b, C c</c></item>
///     <item><c>JOIN T t ON ...</c> (all JOIN flavors)</item>
///   </list>
/// Skips string literals, comments, and parenthesized subqueries (the subquery's
/// own FROM list isn't surfaced — keeps alias scope simple and predictable for V1).
/// Quoted identifiers are unwrapped (<c>"My Table"</c> → <c>My Table</c>); their
/// case is preserved. Unquoted identifiers are returned uppercased so callers
/// can match against Firebird's catalog convention.
/// </remarks>
public static class SqlAliasResolver
{
    // Keywords that end an alias-list segment without consuming their token as an
    // alias. Matched case-insensitively. Anything not in here that scans as an
    // identifier IS taken as an alias.
    private static readonly HashSet<string> AliasTerminators = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "INTERSECT", "EXCEPT",
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "NATURAL",
        "RETURNING", "FOR", "INTO", "SET", "VALUES", "USING", "WITH", "LIMIT",
        "OFFSET", "FETCH", "ROWS", "PLAN", "AND", "OR",
    };

    // Keywords that start a fresh "table list" segment to be parsed.
    private static readonly HashSet<string> TableListStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "JOIN", "UPDATE", "INTO", "TABLE",
    };

    /// <summary>
    /// Given a dot context (qualifier left of <c>.</c>) and the known set of
    /// table/view names, returns the canonical table name to query columns
    /// against — or null when the qualifier matches neither a known table nor
    /// a parsed alias. Pure resolution: no DB access.
    /// </summary>
    public static string? ResolveTableForQualifier(
        string sql,
        string qualifier,
        IReadOnlyCollection<string> knownTables)
        => ResolveTableForQualifier(ParseAliases(sql), qualifier, knownTables);

    /// <summary>
    /// Same resolution as the <c>(sql, …)</c> overload but against a
    /// <b>pre-computed</b> alias map. The editor caches
    /// <see cref="ParseAliases"/> off the keystroke and resolves dot-completion
    /// qualifiers through this overload, so no whole-document re-tokenize runs
    /// while the user types. Pure — no DB access, no scanning.
    /// </summary>
    public static string? ResolveTableForQualifier(
        IReadOnlyDictionary<string, string> aliases,
        string qualifier,
        IReadOnlyCollection<string> knownTables)
    {
        if (string.IsNullOrEmpty(qualifier)) return null;

        // Direct table-name hit wins — fully-qualified TABLE.column references
        // shouldn't be hijacked by an unrelated alias.
        foreach (var t in knownTables)
        {
            if (string.Equals(t, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        if (aliases is null || !aliases.TryGetValue(qualifier, out var aliased)) return null;

        foreach (var t in knownTables)
        {
            if (string.Equals(t, aliased, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns a case-insensitive map of alias → table name. When a table is
    /// listed without an alias, the table name itself maps to the table (so
    /// fully-qualified <c>TABLE.column</c> still resolves).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseAliases(string sql)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(sql)) return aliases;

        var tokens = Tokenize(sql);
        int idx = 0;
        while (idx < tokens.Count)
        {
            var t = tokens[idx];

            // Subqueries at the outer scan level must be skipped wholesale —
            // otherwise their inner FROM would be scooped up into the outer
            // alias map. The contents (incl. nested parens) are handled by
            // SkipParenBlock; aliases declared inside aren't visible outside.
            if (t.Kind == TokenKind.LParen)
            {
                idx = SkipParenBlock(tokens, idx);
                continue;
            }

            if (t.Kind == TokenKind.Word && TableListStarters.Contains(t.Value))
            {
                idx = ParseTableList(tokens, idx + 1, aliases);
                continue;
            }

            idx++;
        }

        return aliases;
    }

    private static int ParseTableList(IReadOnlyList<Token> tokens, int start, Dictionary<string, string> aliases)
    {
        int i = start;
        while (i < tokens.Count)
        {
            // Subquery: skip the entire parenthesised block. Don't try to extract
            // aliases from within — they'd belong to a different scope anyway.
            if (tokens[i].Kind == TokenKind.LParen)
            {
                i = SkipParenBlock(tokens, i);
                // After a subquery we may still see "AS alias" / "alias".
                i = MaybeConsumeAlias(tokens, i, table: null, aliases);
                if (!ConsumeComma(tokens, ref i)) return i;
                continue;
            }

            if (tokens[i].Kind != TokenKind.Word)
            {
                return i;
            }

            // A terminator keyword means the table list is over without a table.
            if (AliasTerminators.Contains(tokens[i].Value))
            {
                return i;
            }

            var table = tokens[i].Value;
            i++;

            // Handle "SCHEMA.TABLE" — Firebird doesn't have schemas, but be safe:
            // consume "DOT word" pairs and treat the last word as the table.
            while (i + 1 < tokens.Count
                   && tokens[i].Kind == TokenKind.Dot
                   && tokens[i + 1].Kind == TokenKind.Word
                   && !AliasTerminators.Contains(tokens[i + 1].Value))
            {
                table = tokens[i + 1].Value;
                i += 2;
            }

            i = MaybeConsumeAlias(tokens, i, table, aliases);

            if (!ConsumeComma(tokens, ref i)) return i;
        }
        return i;
    }

    // Try to read "[AS] alias" off the current position; record the resulting
    // alias→table mapping. When <paramref name="table"/> is null (we just stepped
    // out of a subquery), the alias is recorded mapping to itself — there's no
    // real table to query columns against, but at least we don't crash.
    private static int MaybeConsumeAlias(IReadOnlyList<Token> tokens, int i, string? table, Dictionary<string, string> aliases)
    {
        bool sawAsKeyword = false;
        if (i < tokens.Count && tokens[i].Kind == TokenKind.Word
            && string.Equals(tokens[i].Value, "AS", StringComparison.OrdinalIgnoreCase))
        {
            sawAsKeyword = true;
            i++;
        }

        if (i < tokens.Count && tokens[i].Kind == TokenKind.Word
            && !AliasTerminators.Contains(tokens[i].Value))
        {
            var alias = tokens[i].Value;
            if (table is not null)
            {
                aliases[alias] = table;
            }
            i++;
        }
        else if (!sawAsKeyword && table is not null)
        {
            // No alias — let the table name itself act as the qualifier.
            aliases[table] = table;
        }
        else if (table is not null)
        {
            // "AS" with no following identifier — pathological, but still
            // make the table queryable by its own name.
            aliases[table] = table;
        }

        return i;
    }

    private static bool ConsumeComma(IReadOnlyList<Token> tokens, ref int i)
    {
        if (i < tokens.Count && tokens[i].Kind == TokenKind.Comma)
        {
            i++;
            return true;
        }
        return false;
    }

    private static int SkipParenBlock(IReadOnlyList<Token> tokens, int i)
    {
        // Caller verified tokens[i] is "(". Walk until the matching ")".
        int depth = 0;
        for (; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.LParen) depth++;
            else if (tokens[i].Kind == TokenKind.RParen)
            {
                depth--;
                if (depth == 0) return i + 1;
            }
        }
        return i;
    }

    // -- Tokenizer --------------------------------------------------------------
    //
    // The resolver walks its own tiny Word/Comma/Dot/LParen/RParen/Other token shape (below).
    // Tokenization is delegated to the shared Firebird lexer (Etap 1); we project its rich
    // token stream onto that shape, preserving the historical behaviour this resolver relies
    // on: string literals are invisible (contribute no token), unquoted words are uppercased
    // to match Firebird's catalog convention, quoted identifiers keep their literal case.

    private enum TokenKind { Word, Comma, Dot, LParen, RParen, Other }

    private readonly record struct Token(TokenKind Kind, string Value);

    private static List<Token> Tokenize(string sql)
    {
        var result = new List<Token>();
        foreach (var t in Lang.SqlLexer.Tokenize(sql))
        {
            switch (t.Kind)
            {
                case Lang.TokenKind.EndOfFile:
                case Lang.TokenKind.StringLiteral:
                    // Trivia is attached to tokens (never emitted); string literals are opaque
                    // and must not start a table-list parse — both contribute no token.
                    break;
                case Lang.TokenKind.Keyword:
                case Lang.TokenKind.Identifier:
                    result.Add(new Token(TokenKind.Word, t.Text.ToUpperInvariant()));
                    break;
                case Lang.TokenKind.QuotedIdentifier:
                    result.Add(new Token(TokenKind.Word, t.Value)); // decoded, case-preserved
                    break;
                case Lang.TokenKind.Comma:
                    result.Add(new Token(TokenKind.Comma, ","));
                    break;
                case Lang.TokenKind.Dot:
                    result.Add(new Token(TokenKind.Dot, "."));
                    break;
                case Lang.TokenKind.LParen:
                    result.Add(new Token(TokenKind.LParen, "("));
                    break;
                case Lang.TokenKind.RParen:
                    result.Add(new Token(TokenKind.RParen, ")"));
                    break;
                default:
                    // Numbers, operators, parameters, ';', unknown — opaque to alias parsing.
                    result.Add(new Token(TokenKind.Other, t.Text));
                    break;
            }
        }
        return result;
    }
}
