using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Executes DDL statements ("CREATE TABLE", "ALTER TABLE", "CREATE GENERATOR",
/// "CREATE TRIGGER", …) on the active <see cref="FirebirdConnectionService"/>.
///
/// DDL statements participate in the user's working transaction when one is
/// active (so a single Compile run can be Rolled Back in one shot), and run
/// without an explicit transaction when none is — Firebird auto-commits each
/// DDL command in that case via the managed driver's implicit per-command tx.
///
/// Multi-statement payloads are split into individual statements (the FB engine
/// does not accept multiple statements in a single <c>FbCommand</c>). The splitter
/// is PSQL-aware: a CREATE/ALTER/RECREATE of a PROCEDURE/TRIGGER/FUNCTION/PACKAGE is
/// kept whole including its DECLARE-section and body semicolons (see
/// <see cref="SplitStatements"/>); plain DDL/DML splits on top-level semicolons.
/// </summary>
public sealed class FirebirdDdlExecutor
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    // Krok 1: DDL/Compile executes on the MAIN connection (co-location with the
    // lane that runs Execute Procedure / F5) so a Compile of a just-executed object
    // no longer hits the cross-attachment "object is in use" self-block. The
    // TransactionService (the DATA lane) is consulted only to verify no working
    // transaction is active before we begin our own autonomous DDL tx (gotcha #89).
    public FirebirdDdlExecutor(FirebirdConnectionService connectionService, TransactionService? transactionService = null)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    /// <summary>
    /// Runs administrative maintenance statements (e.g. <c>SET STATISTICS INDEX</c>) in
    /// their own short, auto-committed transactions — independent of the working
    /// transaction, so nothing is left pending for the user to Commit (IBExpert-style).
    /// Delegates to <see cref="FirebirdConnectionService.ExecuteAdminBatchAsync"/>;
    /// returns per-statement results (null = ok, otherwise the error message).
    /// </summary>
    public Task<IReadOnlyList<string?>> ExecuteAutonomousBatchAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken = default)
        => _connectionService.ExecuteAdminBatchAsync(statements, cancellationToken);

    /// <summary>
    /// Splits <paramref name="sql"/> on top-level semicolons, then runs the whole
    /// batch in ONE transaction on the MAIN connection (co-location — see
    /// <see cref="FirebirdConnectionService.ExecuteDdlAsync"/>), auto-committing on
    /// success. The batch is atomic (e.g. ADD FIELD + CREATE GENERATOR + CREATE
    /// TRIGGER all-or-nothing). Uses an explicit NOWAIT TPB — identical to prior
    /// behaviour, but now genuinely explicit (the old autonomous path passed no
    /// FbTransactionOptions, so it silently ignored any configured profile).
    ///
    /// gotcha #89: one FbConnection allows one transaction at a time, so a data
    /// working transaction must be settled first. Surfaces a clear, actionable
    /// message instead of the raw "Parallel transactions are not supported". The
    /// self-block scenario (Execute Procedure → Commit/Rollback → Compile) has the
    /// working tx already settled, so this does not impede it.
    /// Throws <see cref="DdlExecutionException"/> with the server's message on the
    /// first FbException — the caller stops the Compile run at that point.
    /// </summary>
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        var statements = SplitStatements(sql);
        if (statements.Count == 0) return;

        if (_transactionService is { IsActive: true })
        {
            throw new DdlExecutionException(
                "Commit or roll back the active transaction before running DDL.");
        }

        try
        {
            await _connectionService
                .ExecuteDdlAsync(statements, BuildDdlTransactionOptions(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            throw new DdlExecutionException(ex.Message, ex);
        }
    }

    // Lock timeout (seconds) for Developer Mode's WAIT transactions — bounds the wait
    // so a Compile of a continuously-used object fails with a clear message instead of
    // hanging indefinitely.
    internal const int DdlLockTimeoutSeconds = 10;

    private FbTransactionOptions BuildDdlTransactionOptions()
        => BuildDdlTransactionOptions(_connectionService.ActiveProfile?.DeveloperMode ?? false);

    // Standard Mode → write + read_committed + rec_version + NOWAIT (fail-fast,
    // identical to prior behaviour). Developer Mode → the same isolation but WAIT +
    // a lock timeout, so DDL waits for an in-use object to be released rather than
    // returning "object is in use" immediately. Pure + internal so a unit test pins
    // both shapes without a live Firebird. Affects ONLY the DDL path; data ops are
    // always NOWAIT.
    internal static FbTransactionOptions BuildDdlTransactionOptions(bool developerMode)
    {
        var behavior =
            FbTransactionBehavior.Write
            | FbTransactionBehavior.ReadCommitted
            | FbTransactionBehavior.RecVersion
            | (developerMode ? FbTransactionBehavior.Wait : FbTransactionBehavior.NoWait);

        var options = new FbTransactionOptions { TransactionBehavior = behavior };
        if (developerMode)
        {
            options.WaitTimeout = TimeSpan.FromSeconds(DdlLockTimeoutSeconds);
        }
        return options;
    }

    /// <summary>
    /// Splits a multi-statement DDL string into individual statements for the
    /// one-statement-per-<c>FbCommand</c> loop. Two statement shapes:
    /// <list type="bullet">
    /// <item><b>Plain</b> DDL/DML (CREATE TABLE, ALTER TABLE, CREATE GENERATOR,
    /// CREATE VIEW … AS SELECT, COMMENT, …) terminates at the next TOP-LEVEL
    /// <c>;</c> (outside a BEGIN/CASE block; string/comment-aware).</item>
    /// <item><b>PSQL definitions</b> — <c>CREATE [OR ALTER] | ALTER | RECREATE</c>
    /// of a <c>PROCEDURE | TRIGGER | FUNCTION | PACKAGE</c> — are ONE statement
    /// whose body semicolons never split it. The body runs from the header <c>AS</c>
    /// through the <c>END</c> that closes the outermost <c>BEGIN</c>. Critically the
    /// <c>DECLARE</c> section (<c>DECLARE VARIABLE …;</c>) sits BEFORE that BEGIN, so
    /// its semicolons are at block-depth 0 — a plain top-level split would cut the
    /// statement there (→ "Unexpected end of command"). A leading PSQL header is
    /// therefore scanned as a unit (gotcha #140).</item>
    /// </list>
    /// CASE counts as a nested block opener inside the body so a <c>CASE … END</c>
    /// doesn't close the enclosing BEGIN early (gotchas #117/#128/#129). A FB3+
    /// subprogram (<c>DECLARE PROCEDURE/FUNCTION … BEGIN … END</c>) in the DECLARE
    /// section opens/closes its own BEGIN; the body terminator is detected by peeking
    /// the token after a depth-0 END (BEGIN/DECLARE → a subprogram closed, keep
    /// going; anything else → the main body closed). String literals, quoted
    /// identifiers, and comments are skipped verbatim throughout.
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string sql)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) return result;

        int i = 0, n = sql.Length;
        while (i < n)
        {
            i = SkipTriviaAndComments(sql, i);
            if (i >= n) break;
            int start = i;
            i = IsPsqlDefinitionStart(sql, i)
                ? ScanPsqlStatement(sql, i)
                : ScanPlainStatement(sql, i);
            AddStatement(sql.Substring(start, i - start), result);
        }
        return result;
    }

    // Plain statement: terminates at the next top-level ';' (block-depth 0). BEGIN/
    // CASE/END awareness is retained defensively so any begin/end content that isn't a
    // PSQL CREATE still isn't split mid-block. Returns the index just past the ';'.
    private static int ScanPlainStatement(string sql, int start)
    {
        int i = start, n = sql.Length, depth = 0;
        while (i < n)
        {
            char c = sql[i];
            if (c == '\'') { i = SkipString(sql, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(sql, i); continue; }
            if (c == '-' && i + 1 < n && sql[i + 1] == '-') { i = SkipLineComment(sql, i); continue; }
            if (c == '/' && i + 1 < n && sql[i + 1] == '*') { i = SkipBlockComment(sql, i); continue; }
            if (c == ';' && depth == 0) return i + 1;
            if (IsWordBoundary(sql, i - 1))
            {
                if ((Matches(sql, i, "BEGIN") && IsWordEndAt(sql, i + 5)) || (Matches(sql, i, "CASE") && IsWordEndAt(sql, i + 4))) depth++;
                else if (Matches(sql, i, "END") && IsWordEndAt(sql, i + 3)) { if (depth > 0) depth--; }
            }
            i++;
        }
        return i;
    }

    // PSQL definition (CREATE/ALTER/RECREATE PROCEDURE|TRIGGER|FUNCTION|PACKAGE): one
    // statement, body semicolons included. Phase 1 (before AS): skip balanced parens
    // (so an AS inside CAST(x AS y) / a param list isn't the body separator) and treat
    // a top-level ';' as a no-body terminator (UDR / EXTERNAL declarations). Phase 2
    // (after AS): track BEGIN/CASE/END depth — ';' is body-internal — and end at the
    // END closing the outermost BEGIN (peeking past a subprogram's END).
    private static int ScanPsqlStatement(string sql, int start)
    {
        int i = start, n = sql.Length;
        bool pastAs = false, bodyOpened = false;
        int depth = 0;
        while (i < n)
        {
            char c = sql[i];
            if (c == '\'') { i = SkipString(sql, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(sql, i); continue; }
            if (c == '-' && i + 1 < n && sql[i + 1] == '-') { i = SkipLineComment(sql, i); continue; }
            if (c == '/' && i + 1 < n && sql[i + 1] == '*') { i = SkipBlockComment(sql, i); continue; }

            if (!pastAs)
            {
                if (c == '(') { i = SkipParens(sql, i); continue; }
                if (KeywordAt(sql, i, "AS")) { pastAs = true; i += 2; continue; }
                if (c == ';') return i + 1; // header with no PSQL body (UDR / EXTERNAL)
                i++;
                continue;
            }

            if (IsWordBoundary(sql, i - 1))
            {
                if (Matches(sql, i, "BEGIN") && IsWordEndAt(sql, i + 5)) { depth++; bodyOpened = true; i += 5; continue; }
                if (Matches(sql, i, "CASE") && IsWordEndAt(sql, i + 4)) { if (depth > 0) depth++; i += 4; continue; }
                if (Matches(sql, i, "END") && IsWordEndAt(sql, i + 3))
                {
                    i += 3;
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0 && bodyOpened)
                        {
                            int j = SkipTriviaAndComments(sql, i);
                            // A subprogram's END (more DECLAREs / the main BEGIN follow) → keep scanning.
                            if (j < n && (KeywordAt(sql, j, "BEGIN") || KeywordAt(sql, j, "DECLARE"))) continue;
                            return j < n && sql[j] == ';' ? j + 1 : i; // main body closed
                        }
                    }
                    continue;
                }
            }
            i++;
        }
        return i;
    }

    // CREATE [OR ALTER] | ALTER | RECREATE  +  PROCEDURE | TRIGGER | FUNCTION | PACKAGE.
    // (ALTER TABLE / CREATE VIEW … AS SELECT / CREATE GENERATOR etc. are NOT PSQL.)
    private static bool IsPsqlDefinitionStart(string sql, int i)
    {
        int j = i;
        if (KeywordAt(sql, j, "CREATE"))
        {
            j = SkipWordAndTrivia(sql, j, "CREATE");
            if (KeywordAt(sql, j, "OR"))
            {
                j = SkipWordAndTrivia(sql, j, "OR");
                if (!KeywordAt(sql, j, "ALTER")) return false;
                j = SkipWordAndTrivia(sql, j, "ALTER");
            }
        }
        else if (KeywordAt(sql, j, "RECREATE")) { j = SkipWordAndTrivia(sql, j, "RECREATE"); }
        else if (KeywordAt(sql, j, "ALTER")) { j = SkipWordAndTrivia(sql, j, "ALTER"); }
        else return false;

        return KeywordAt(sql, j, "PROCEDURE") || KeywordAt(sql, j, "TRIGGER")
            || KeywordAt(sql, j, "FUNCTION") || KeywordAt(sql, j, "PACKAGE");
    }

    private static int SkipWordAndTrivia(string s, int i, string word) => SkipTriviaAndComments(s, i + word.Length);

    // ── opaque-run / trivia skippers (return the index just past the run) ──
    private static int SkipString(string s, int i)
    {
        int n = s.Length; i++;
        while (i < n)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < n && s[i + 1] == '\'') { i += 2; continue; } // '' escape
                return i + 1;
            }
            i++;
        }
        return i;
    }

    private static int SkipQuotedIdent(string s, int i)
    {
        int n = s.Length; i++;
        while (i < n && s[i] != '"') i++;
        return i < n ? i + 1 : i;
    }

    private static int SkipLineComment(string s, int i)
    {
        while (i < s.Length && s[i] != '\n') i++;
        return i;
    }

    private static int SkipBlockComment(string s, int i)
    {
        int n = s.Length; i += 2;
        while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) i++;
        return i + 1 < n ? i + 2 : n;
    }

    private static int SkipParens(string s, int i)
    {
        int n = s.Length, depth = 0;
        while (i < n)
        {
            char c = s[i];
            if (c == '\'') { i = SkipString(s, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(s, i); continue; }
            if (c == '-' && i + 1 < n && s[i + 1] == '-') { i = SkipLineComment(s, i); continue; }
            if (c == '/' && i + 1 < n && s[i + 1] == '*') { i = SkipBlockComment(s, i); continue; }
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { depth--; i++; if (depth == 0) return i; continue; }
            i++;
        }
        return i;
    }

    private static int SkipTriviaAndComments(string s, int i)
    {
        int n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < n && s[i + 1] == '-') { i = SkipLineComment(s, i); continue; }
            if (c == '/' && i + 1 < n && s[i + 1] == '*') { i = SkipBlockComment(s, i); continue; }
            break;
        }
        return i;
    }

    private static bool KeywordAt(string s, int i, string keyword)
        => IsWordBoundary(s, i - 1) && Matches(s, i, keyword) && IsWordEndAt(s, i + keyword.Length);

    private static bool IsWordBoundary(string s, int index)
    {
        if (index < 0) return true;
        var c = s[index];
        return !(char.IsLetterOrDigit(c) || c == '_' || c == '$');
    }

    private static bool IsWordEndAt(string s, int index)
        => index >= s.Length || !(char.IsLetterOrDigit(s[index]) || s[index] == '_' || s[index] == '$');

    private static bool Matches(string s, int start, string token)
    {
        if (start + token.Length > s.Length) return false;
        for (int i = 0; i < token.Length; i++)
        {
            if (char.ToUpperInvariant(s[start + i]) != token[i]) return false;
        }
        return true;
    }

    // Adds a scanned statement, stripping a single trailing terminator ';' + whitespace.
    private static void AddStatement(string raw, List<string> sink)
    {
        var trimmed = raw.Trim();
        if (trimmed.EndsWith(";", StringComparison.Ordinal)) trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
        if (trimmed.Length > 0) sink.Add(trimmed);
    }
}

public sealed class DdlExecutionException : Exception
{
    public DdlExecutionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
