using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// Highlight/role category of a recognised Firebird keyword. The first four values
/// map 1:1 onto the four <c>&lt;Keywords color="…"&gt;</c> blocks of the XSHD syntax
/// definitions (verified by tests); <see cref="Keyword"/> is a recognised keyword that
/// carries no dedicated highlight colour (e.g. <c>ANY</c>, <c>ESCAPE</c>, <c>CASCADE</c>).
/// </summary>
public enum SqlKeywordCategory
{
    /// <summary>Query clause + logical/comparison + set-ops + pagination (blue).</summary>
    Dml,

    /// <summary>DML-action + DDL + PSQL control-flow + transaction + constraints (purple).</summary>
    Statement,

    /// <summary>Built-in data types (teal).</summary>
    DataType,

    /// <summary>Built-in functions, CASE/WHEN family, context constants, window keywords (gold).</summary>
    Function,

    /// <summary>A recognised keyword with no dedicated highlight colour.</summary>
    Keyword,
}

/// <summary>One entry of the <see cref="FirebirdSyntax"/> catalog.</summary>
/// <param name="Word">The canonical (uppercase) spelling.</param>
/// <param name="Category">Its highlight/role category.</param>
/// <param name="InCompletion">Whether the editor autocomplete offers it (drives <see cref="SqlKeywords"/>).</param>
public readonly record struct SqlKeywordInfo(string Word, SqlKeywordCategory Category, bool InCompletion);

/// <summary>
/// The single Firebird keyword catalog — the one source of truth that unifies the previously
/// divergent keyword lists (the completion vocabulary, and the two XSHD highlighting keyword
/// blocks). Etap 1 of the editor rebuild.
/// <para>
/// It drives: the <see cref="SqlLexer"/> (a word is a <see cref="TokenKind.Keyword"/> iff it
/// is in this catalog), the completion vocabulary (<see cref="SqlKeywords"/> derives from
/// <see cref="CompletionKeywords"/>), and the highlighting keyword sets (the XSHD
/// <c>&lt;Keywords&gt;</c> blocks are pinned against <see cref="KeywordsInCategory"/> by
/// <c>FirebirdSyntaxTests</c>).
/// </para>
/// <para>
/// <b>Scope note (Etap 1):</b> the SQL <em>formatter</em>'s own keyword hashsets are NOT folded
/// in here yet — that migration rides with the AST formatter rewrite in Etap 3 (audit §4).
/// </para>
/// </summary>
public static class FirebirdSyntax
{
    // ── Highlight categories — transcribed 1:1 from the XSHD <Keywords> blocks ────────────
    // These four arrays are the authoritative highlighting keyword sets. FirebirdSyntaxTests
    // pins both FirebirdSql.xshd and FirebirdSql.Light.xshd against them so they cannot drift.

    private static readonly string[] DmlWords =
    {
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "HAVING", "ORDER", "ASC", "DESC",
        "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON",
        "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE", "STARTING", "CONTAINING",
        "SIMILAR", "TO", "IS", "NULL", "AS", "DISTINCT", "ALL",
        "UNION", "EXCEPT", "INTERSECT", "WITH", "RECURSIVE",
        "FIRST", "SKIP", "ROWS", "OFFSET", "FETCH", "LIMIT",
        "TRUE", "FALSE", "UNKNOWN", "PLAN", "NATURAL",
    };

    private static readonly string[] StatementWords =
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "INTO", "VALUES", "SET",
        "USING", "MATCHED", "RETURNING",
        "CREATE", "RECREATE", "ALTER", "DROP",
        "TABLE", "VIEW", "INDEX", "SEQUENCE", "GENERATOR",
        "PROCEDURE", "FUNCTION", "TRIGGER", "DOMAIN", "EXCEPTION", "ROLE",
        "GRANT", "REVOKE", "EXECUTE", "RETURNS", "RETURN",
        "BEGIN", "END", "FOR", "WHILE", "DO", "IF",
        "EXIT", "SUSPEND", "LEAVE", "DECLARE", "VARIABLE", "CURSOR", "BLOCK", "STATEMENT",
        "COMMIT", "ROLLBACK", "SAVEPOINT", "TRANSACTION",
        "PRIMARY", "FOREIGN", "KEY", "REFERENCES", "CONSTRAINT", "UNIQUE", "CHECK", "DEFAULT",
        "NEXT", "VALUE",
    };

    private static readonly string[] DataTypeWords =
    {
        "SMALLINT", "INTEGER", "INT", "BIGINT",
        "FLOAT", "DOUBLE", "PRECISION", "NUMERIC", "DECIMAL",
        "CHAR", "VARCHAR", "NCHAR", "CSTRING",
        "BLOB", "TEXT", "BINARY",
        "DATE", "TIME", "TIMESTAMP", "BOOLEAN",
        "CHARACTER", "COLLATE",
        "SUB_TYPE", "SEGMENT", "SIZE",
    };

    private static readonly string[] FunctionWords =
    {
        "CASE", "WHEN", "THEN", "ELSE",
        "CAST", "COALESCE", "NULLIF", "IIF", "DECODE", "CONVERT", "EXTRACT", "OVERLAY",
        "COUNT", "SUM", "AVG", "MIN", "MAX", "LIST",
        "UPPER", "LOWER", "TRIM", "SUBSTRING", "POSITION",
        "CHAR_LENGTH", "CHARACTER_LENGTH", "OCTET_LENGTH",
        "LPAD", "RPAD", "REPLACE", "REVERSE", "LEADING", "TRAILING", "BOTH",
        "ABS", "SIGN", "MOD", "POWER", "SQRT", "EXP", "LN", "LOG", "LOG10",
        "FLOOR", "CEIL", "CEILING", "ROUND", "PI", "RAND",
        "ASIN", "ACOS", "ATAN", "ATAN2", "SIN", "COS", "TAN",
        "DATEADD", "DATEDIFF",
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "CURRENT_USER", "CURRENT_ROLE", "CURRENT_CONNECTION", "CURRENT_TRANSACTION",
        "GEN_ID",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "FIRST_VALUE", "LAST_VALUE", "LAG", "LEAD",
        "OVER", "PARTITION",
    };

    // ── Completion vocabulary — the single-token list the editor autocomplete offers ──────
    // Historically SqlKeywords.Raw; now SqlKeywords.All derives from CompletionKeywords, so
    // this is the ONE place the completion word set lives. A word here that is not in a
    // highlight category above is catalogued as SqlKeywordCategory.Keyword (recognised, but
    // uncoloured). Kept as the exact historical set to avoid changing completion behaviour in
    // this plumbing etap (the context-ranked Completion Engine of Etap 5 revisits the subset).
    private static readonly string[] CompletionWords =
    {
        // DML
        "SELECT", "FROM", "WHERE", "HAVING", "GROUP", "BY", "ORDER", "ASC", "DESC",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "MERGE", "USING",
        "RETURNING", "DISTINCT", "ALL", "ANY", "SOME", "AS", "ON",
        // JOIN
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "NATURAL",
        // Logical / comparison
        "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN", "EXISTS",
        "CONTAINING", "STARTING", "WITH", "SIMILAR", "TO", "ESCAPE",
        // CTE / set ops
        "RECURSIVE", "UNION", "INTERSECT", "EXCEPT",
        // PSQL / control flow
        "BEGIN", "END", "DECLARE", "VARIABLE", "EXECUTE", "BLOCK", "STATEMENT",
        "IF", "THEN", "ELSE", "WHEN", "CASE", "WHILE", "DO", "FOR",
        "LEAVE", "BREAK", "CONTINUE", "SUSPEND", "EXIT",
        // DDL
        "CREATE", "ALTER", "DROP", "RECREATE", "TABLE", "VIEW", "INDEX",
        "PROCEDURE", "TRIGGER", "FUNCTION", "GENERATOR", "SEQUENCE", "DOMAIN",
        "EXCEPTION", "ROLE", "PACKAGE", "BODY",
        // Constraints
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "UNIQUE", "CHECK", "DEFAULT",
        "CONSTRAINT", "CASCADE", "RESTRICT", "ACTION",
        // Pagination
        "FIRST", "SKIP", "ROWS", "ROW", "ONLY", "FETCH", "OFFSET", "LIMIT", "NEXT",
        // Transaction
        "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE", "TRANSACTION", "READ",
        "WRITE", "ISOLATION", "LEVEL", "SNAPSHOT", "WAIT",
        // Types
        "INTEGER", "INT", "SMALLINT", "BIGINT", "FLOAT", "DOUBLE", "PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR", "NCHAR", "DATE", "TIME",
        "TIMESTAMP", "BLOB", "BOOLEAN", "TRUE", "FALSE", "CHARACTER", "COLLATE",
        // Datetime / context constants
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER",
        "CURRENT_ROLE", "CURRENT_CONNECTION", "CURRENT_TRANSACTION",
        // Common built-in functions
        "COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE", "NULLIF", "CAST",
        "EXTRACT", "SUBSTRING", "TRIM", "UPPER", "LOWER", "GEN_ID",
        "IIF", "ABS", "MOD", "POWER", "SQRT", "ROUND", "CEILING", "FLOOR",
    };

    private static readonly Dictionary<string, SqlKeywordInfo> ByWord;
    private static readonly IReadOnlyList<string> KeywordCategoryWords;

    static FirebirdSyntax()
    {
        var byWord = new Dictionary<string, SqlKeywordInfo>(StringComparer.OrdinalIgnoreCase);

        void AddCategory(string[] words, SqlKeywordCategory category)
        {
            foreach (var w in words)
            {
                if (byWord.ContainsKey(w))
                {
                    // A word must live in exactly one highlight category (the XSHD blocks are
                    // disjoint). This guards against a transcription slip.
                    throw new InvalidOperationException(
                        $"FirebirdSyntax: keyword '{w}' is listed in more than one category.");
                }
                byWord[w] = new SqlKeywordInfo(w, category, InCompletion: false);
            }
        }

        AddCategory(DmlWords, SqlKeywordCategory.Dml);
        AddCategory(StatementWords, SqlKeywordCategory.Statement);
        AddCategory(DataTypeWords, SqlKeywordCategory.DataType);
        AddCategory(FunctionWords, SqlKeywordCategory.Function);

        foreach (var w in CompletionWords)
        {
            byWord[w] = byWord.TryGetValue(w, out var existing)
                ? existing with { InCompletion = true }
                : new SqlKeywordInfo(w, SqlKeywordCategory.Keyword, InCompletion: true);
        }

        ByWord = byWord;
        KeywordCategoryWords = byWord.Values
            .Where(i => i.Category == SqlKeywordCategory.Keyword)
            .Select(i => i.Word)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CompletionKeywords = CompletionWords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The single-token completion vocabulary — deduped, alphabetised, uppercase.</summary>
    public static IReadOnlyList<string> CompletionKeywords { get; }

    /// <summary>All catalogued keyword spellings (every highlight category + uncoloured keywords).</summary>
    public static IReadOnlyCollection<string> Keywords => ByWord.Keys;

    /// <summary>True when <paramref name="word"/> is a recognised Firebird keyword (case-insensitive).</summary>
    public static bool IsKeyword(string? word) => word is not null && ByWord.ContainsKey(word);

    /// <summary>Looks up the catalog entry for <paramref name="word"/> (case-insensitive).</summary>
    public static bool TryGet(string? word, out SqlKeywordInfo info)
    {
        if (word is not null && ByWord.TryGetValue(word, out info))
        {
            return true;
        }
        info = default;
        return false;
    }

    /// <summary>The highlight/role category of <paramref name="word"/>, or null when it is not a keyword.</summary>
    public static SqlKeywordCategory? CategoryOf(string? word)
        => word is not null && ByWord.TryGetValue(word, out var i) ? i.Category : null;

    /// <summary>The set of keywords in a given category (the authoritative highlighting sets).</summary>
    public static IReadOnlyList<string> KeywordsInCategory(SqlKeywordCategory category) => category switch
    {
        SqlKeywordCategory.Dml => DmlWords,
        SqlKeywordCategory.Statement => StatementWords,
        SqlKeywordCategory.DataType => DataTypeWords,
        SqlKeywordCategory.Function => FunctionWords,
        SqlKeywordCategory.Keyword => KeywordCategoryWords,
        _ => Array.Empty<string>(),
    };
}
