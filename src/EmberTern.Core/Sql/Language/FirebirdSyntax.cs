using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// Highlight/role category of a recognised Firebird keyword. The first five values
/// map 1:1 onto the five <c>&lt;Keywords color="…"&gt;</c> blocks of the XSHD syntax
/// definitions (verified by tests); <see cref="Keyword"/> is a recognised keyword that
/// carries no dedicated highlight colour (e.g. <c>ANY</c>, <c>ESCAPE</c>, <c>CASCADE</c>).
/// </summary>
public enum SqlKeywordCategory
{
    /// <summary>Query clause + logical/comparison + set-ops + pagination — SQL (blue).</summary>
    Dml,

    /// <summary>DML-action + DDL + transaction + constraints — SQL (blue, same accent as <see cref="Dml"/>).</summary>
    Statement,

    /// <summary>PSQL control-flow / routine-body keywords (BEGIN/END/IF/WHILE/FOR/DECLARE/SUSPEND/EXECUTE/…) —
    /// a second restrained accent (violet), distinct from the SQL blue so SQL and PSQL read as two groups (D15.1).</summary>
    Psql,

    /// <summary>Built-in data types — neutral foreground since D15.1.</summary>
    DataType,

    /// <summary>Built-in functions, CASE/WHEN family, context constants, window keywords — neutral since D15.1.</summary>
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

    // SQL statement keywords (DML-action + DDL + transaction + constraints). Same blue accent
    // as the Dml block — together they are "SQL". PSQL control-flow moved to PsqlWords (D15.1).
    private static readonly string[] StatementWords =
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "INTO", "VALUES", "SET",
        "USING", "MATCHED", "RETURNING",
        "CREATE", "RECREATE", "ALTER", "DROP",
        "TABLE", "VIEW", "INDEX", "SEQUENCE", "GENERATOR",
        "PROCEDURE", "FUNCTION", "TRIGGER", "DOMAIN", "EXCEPTION", "ROLE",
        "GRANT", "REVOKE",
        "COMMIT", "ROLLBACK", "SAVEPOINT", "TRANSACTION",
        "PRIMARY", "FOREIGN", "KEY", "REFERENCES", "CONSTRAINT", "UNIQUE", "CHECK", "DEFAULT",
        "NEXT", "VALUE",
    };

    // PSQL control-flow / routine-body keywords — a distinct restrained violet accent, so PSQL
    // reads as its own group beside the blue SQL keywords (D15.1). EXECUTE / BLOCK / STATEMENT are
    // here for EXECUTE BLOCK / EXECUTE PROCEDURE / EXECUTE STATEMENT; RETURNS/RETURN for the routine
    // signature + body. Kept disjoint from StatementWords (AddCategory enforces one category per word).
    private static readonly string[] PsqlWords =
    {
        "BEGIN", "END", "FOR", "WHILE", "DO", "IF",
        "EXIT", "SUSPEND", "LEAVE", "DECLARE", "VARIABLE", "CURSOR",
        "EXECUTE", "BLOCK", "STATEMENT", "RETURNS", "RETURN",
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

    // ── Non-reserved vocabulary — Firebird's own words that are NOT catalogued above ──────────
    //
    // ⭐⭐ THIS IS NOT A SECOND KEYWORD LIST, and the difference is the whole reason it exists.
    //
    // Firebird reserves very few words. Most of its vocabulary — MONTH, PLACING, UNBOUNDED, AUTONOMOUS,
    // … — is NON-RESERVED, deliberately: a user may legitimately name a column or a variable MONTH. So
    // these words must keep lexing as IDENTIFIERS (a word in the catalog above becomes
    // TokenKind.Keyword, which would colour and re-case every column called MONTH in the application),
    // and this set must never be fed to AddCategory.
    //
    // ⭐ Its ONE job is a NEGATIVE one: it answers "is this identifier a word Firebird itself uses?", and
    // the only permitted consequence is to STAY SILENT about it. An identifier that spells one of these
    // and resolves to nothing is not PROVABLY an unknown variable — it is far more likely a construct the
    // binder does not model yet — so the conservatism rule (prefer silence over false positives) applies.
    // It never suppresses a BINDING: a variable or column that really is called MONTH still resolves,
    // still colours, still hovers, still finds its references.
    //
    // ⚠ WHY A VOCABULARY AND NOT MORE POSITIONAL PREDICATES. The four fixes before this one were each a
    // positional predicate for the single construct reported (NEXT VALUE FOR, then GEN_ID's first
    // argument, then EXTRACT's first argument). Positional is the right tool where the consequence is to
    // RESOLVE a name — a generator must still be looked up, and an unknown one is a real ET0001 — and
    // FirebirdGrammar keeps doing exactly that. But as a strategy for the whole language it is an
    // allowlist of exceptions whose completeness is bounded by the bug reports that produced it. A
    // vocabulary is bounded by the LANGUAGE instead, which is finite, documented and does not grow with
    // usage.
    //
    // Transcribed from the Firebird 5 Language Reference appendix "Reserved words and keywords"
    // (non-reserved section), limited to words that can appear as a BARE identifier in a value or
    // statement position — a word only ever reachable as `NAME(` is skipped by both walkers already.
    private static readonly string[] NonReservedWords =
    {
        // Date/time parts — EXTRACT / DATEADD / DATEDIFF / FIRST_DAY / LAST_DAY operands.
        "YEAR", "MONTH", "DAY", "WEEK", "WEEKDAY", "YEARDAY", "QUARTER",
        "HOUR", "MINUTE", "SECOND", "MILLISECOND",
        "TIMEZONE_HOUR", "TIMEZONE_MINUTE", "ZONE", "LOCAL", "LOCALTIME", "LOCALTIMESTAMP",
        // String / expression syntax words.
        "PLACING", "OF", "AT", "NULLS", "LAST", "PRIOR", "SCROLL",
        // Window frame + named windows.
        "WINDOW", "RANGE", "GROUPS", "UNBOUNDED", "PRECEDING", "FOLLOWING",
        "EXCLUDE", "TIES", "OTHERS", "FILTER", "LATERAL",
        // Window / aggregate functions that may be written without parentheses in a frame clause context.
        "NTILE", "PERCENT_RANK", "CUME_DIST", "NTH_VALUE",
        // PSQL statement vocabulary.
        "AUTONOMOUS", "COMMON", "CALLER", "PRIVILEGES", "PRIVILEGE", "PASSWORD",
        "EXTERNAL", "SOURCE", "DATA", "ENGINE", "ENTRY_POINT", "MODULE_NAME",
        "RESETTING", "MESSAGE",
        // Cryptography — HASH / CRYPT_HASH / ENCRYPT / DECRYPT / RSA_* option words and algorithms.
        "MODE", "IV", "CTR_LENGTH", "COUNTER", "SALT_LENGTH", "SIGNATURE", "HASH",
        "CRC32", "MD5", "SHA1", "SHA256", "SHA512", "SHA3_224", "SHA3_256", "SHA3_384", "SHA3_512",
        "AES", "ANUBIS", "BLOWFISH", "KHAZAD", "RC4", "CHACHA20", "SOBER128",
        "CBC", "CFB", "CTR", "ECB", "OFB", "PKCS_1_5", "CTR_BIG_ENDIAN", "CTR_LITTLE_ENDIAN",
        // DDL vocabulary that appears as a bare word.
        "ACTIVE", "INACTIVE", "BEFORE", "AFTER", "ALWAYS", "GENERATED", "IDENTITY",
        "START", "RESTART", "INCREMENT", "COMPUTED", "DESCENDING", "ASCENDING",
        "OVERRIDING", "SYSTEM", "DATABASE", "SCHEMA", "COMMENT", "COLLATION", "TYPE",
        "OPTION", "PLUGIN", "DEFINER", "INVOKER", "SECURITY", "GRANTED", "USAGE",
        "TEMPORARY", "PRESERVE", "GLOBAL", "IDLE", "TIMEOUT", "LINGER",
        // Transaction / session vocabulary.
        "STABILITY", "CONSISTENCY", "COMMITTED", "UNCOMMITTED",
        "RECORD_VERSION", "NO_RECORD_VERSION", "NOWAIT", "LOCK", "SHARED", "PROTECTED",
        "RESERVING", "WORK", "RETAIN", "AUTO", "TWO_PHASE", "SESSION", "RESET", "BIND", "NATIVE",
        "LEGACY", "EXTENDED", "TIME_ZONE", "DECFLOAT", "TRAPS",
        // Miscellaneous non-reserved words that read as ordinary identifiers in a value position.
        "NAMES", "PAGE", "PAGES", "PAGE_SIZE", "LENGTH", "NUMBER",
        "STATISTICS", "SELECTIVE", "FORCE", "IGNORE", "INCLUDE", "MAXVALUE", "MINVALUE",
        "MATCHES", "SINGULAR", "SORT", "SPACE", "SQL", "TAGS", "TOTALORDER", "TRUSTED",
        "MANUAL", "MAPPING", "OLDEST", "REQUESTS", "SERVERWIDE", "UNDO", "VARBINARY", "CLEAR",
        "DEBUG", "DESCRIPTOR", "DISABLE", "ENABLE", "EXCESS", "FILE", "FORMAT", "FREE_IT",
        "INPUT_TYPE", "OUTPUT_TYPE", "OVERFLOW", "POOL", "SCALAR_ARRAY", "SHADOW", "SNAPSHOT_NUMBER",
    };

    // ── Context variables — a bare word that IS a complete value expression ───────────────────
    //
    // ⭐⭐ A SEPARATE SET FROM THE VOCABULARY ABOVE, AND THE DISTINCTION IS LOAD-BEARING. Every word above is
    // a piece of SYNTAX: `YEAR` on its own means nothing, so `v = year;` is a genuine unknown variable and
    // must keep reporting (a guard already pinned exactly that). These nine are the opposite — each is a
    // VALUE in its own right, so `v = row_count;` is correct Firebird and must never report.
    //
    // ⚠ The consequence is that the two sets are suppressed under different conditions: a syntax word only
    // when it stands inside a phrase (FirebirdGrammar.IsVocabularyInsidePhrase), a context variable always.
    // Collapsing them into one set makes one of those two cases wrong whichever condition you pick.
    //
    // (INSERTING / UPDATING / DELETING / RESETTING also resolve to a TriggerPredicateSymbol inside a
    // trigger; they are listed so a stray use elsewhere still stays quiet. The CURRENT_* family are
    // catalogued keywords and never reach an identifier check at all.)
    private static readonly string[] ContextVariableWords =
    {
        "ROW_COUNT", "SQLCODE", "GDSCODE", "SQLSTATE", "USER",
        "INSERTING", "UPDATING", "DELETING", "RESETTING",
    };

    private static readonly HashSet<string> ContextVariables =
        new(ContextVariableWords, StringComparer.OrdinalIgnoreCase);

    // The whole vocabulary: syntax words plus context variables. Context variables belong here too because
    // FirebirdGrammar's positional rules use this as their pre-filter, and a context variable is just as
    // legitimate inside a construct as any other word.
    private static readonly HashSet<string> NonReserved =
        new(NonReservedWords.Concat(ContextVariableWords), StringComparer.OrdinalIgnoreCase);

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
        AddCategory(PsqlWords, SqlKeywordCategory.Psql);
        AddCategory(DataTypeWords, SqlKeywordCategory.DataType);
        AddCategory(FunctionWords, SqlKeywordCategory.Function);

        foreach (var w in CompletionWords)
        {
            byWord[w] = byWord.TryGetValue(w, out var existing)
                ? existing with { InCompletion = true }
                : new SqlKeywordInfo(w, SqlKeywordCategory.Keyword, InCompletion: true);
        }

        // ⭐ A word lives in exactly ONE place. The two sets answer opposite questions — "does this lex as a
        // keyword?" versus "is this an identifier Firebird itself uses?" — so a word in both is a
        // contradiction, and the harmless-looking half of it (the catalogued word simply never reaching the
        // non-reserved check) is exactly what would let the duplicate sit there teaching the next reader that
        // membership means nothing. Same guard shape as AddCategory's.
        foreach (var w in NonReservedWords.Concat(ContextVariableWords))
        {
            if (byWord.ContainsKey(w))
            {
                throw new InvalidOperationException(
                    $"FirebirdSyntax: '{w}' is catalogued as a keyword AND listed as non-reserved. " +
                    "A word is one or the other — a catalogued word lexes as TokenKind.Keyword and can " +
                    "never reach IsNonReservedWord.");
            }
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

    /// <summary>
    /// True when <paramref name="word"/> is one of Firebird's <b>non-reserved</b> words — vocabulary the
    /// language itself uses (<c>MONTH</c>, <c>PLACING</c>, <c>UNBOUNDED</c>, <c>AUTONOMOUS</c>, …) but does
    /// NOT reserve, so it lexes as an ordinary identifier and may legally name a column or a variable.
    /// <para>
    /// ⭐ The only permitted use is to <b>stay silent</b>: an identifier that spells one of these and
    /// resolves to nothing is not provably an unknown variable, so no diagnostic may be raised about it.
    /// ⛔ It must never suppress a binding, never change lexing, and never gate completion — a variable
    /// genuinely named <c>MONTH</c> resolves, colours and navigates exactly as before.
    /// </para>
    /// </summary>
    public static bool IsNonReservedWord(string? word) => word is not null && NonReserved.Contains(word);

    /// <summary>
    /// True when <paramref name="word"/> is a Firebird <b>context variable</b> — a bare identifier that is a
    /// complete value expression on its own (<c>ROW_COUNT</c>, <c>SQLCODE</c>, <c>USER</c>, <c>INSERTING</c>, …).
    /// <para>
    /// ⚠ Distinct from <see cref="IsNonReservedWord"/> and suppressed under a different condition: a context
    /// variable is never an unknown variable ANYWHERE, whereas an ordinary vocabulary word standing alone
    /// (<c>v = year;</c>) still is one. Every context variable is also part of the vocabulary, not the reverse.
    /// </para>
    /// </summary>
    public static bool IsContextVariable(string? word) => word is not null && ContextVariables.Contains(word);

    /// <summary>The non-reserved vocabulary, for tests and tooling. Disjoint from <see cref="Keywords"/>.</summary>
    public static IReadOnlyCollection<string> NonReservedVocabulary => NonReserved;

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
        SqlKeywordCategory.Psql => PsqlWords,
        SqlKeywordCategory.DataType => DataTypeWords,
        SqlKeywordCategory.Function => FunctionWords,
        SqlKeywordCategory.Keyword => KeywordCategoryWords,
        _ => Array.Empty<string>(),
    };
}
