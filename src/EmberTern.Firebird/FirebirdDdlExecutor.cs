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
/// Multi-statement payloads are split on top-level semicolons. None of the
/// statements we emit contain literal strings, so a naive split is safe; the
/// FB engine does not accept multiple statements in a single <c>FbCommand</c>,
/// hence the per-statement loop.
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
    /// Splits a multi-statement DDL string on TOP-LEVEL semicolons.
    /// "Top-level" means outside a <c>BEGIN … END</c> / <c>CASE … END</c> block —
    /// PSQL bodies (CREATE TRIGGER / PROCEDURE / FUNCTION) have their own internal
    /// semicolons (e.g. statements inside BEGIN/END) that must NOT terminate the
    /// outer CREATE statement. The scanner tracks a single block-nesting counter
    /// (case-insensitive, word-boundary match) where BOTH <c>BEGIN</c> and
    /// <c>CASE</c> open and <c>END</c> closes — counting CASE is essential because a
    /// <c>CASE … END</c> expression in a body (e.g. inside a WHERE clause) ends with
    /// <c>END</c> too; without it that END would wrongly close the enclosing BEGIN
    /// and split the procedure mid-body at the next <c>;</c> (yielding a truncated
    /// statement → "Unexpected end of command"). String literals (<c>'…'</c>),
    /// quoted identifiers (<c>"…"</c>), and comments (<c>-- …</c>, <c>/* … */</c>)
    /// are copied verbatim and never inspected, so their semicolons / BEGIN / END /
    /// CASE words don't affect splitting or nesting.
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string sql)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) return result;

        var current = new System.Text.StringBuilder();
        var blockDepth = 0;
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];

            // Opaque runs — copy whole, never inspect for ';' / BEGIN / END / CASE.
            if (c == '\'') { i = CopyString(sql, i, current); continue; }
            if (c == '"') { i = CopyQuotedIdent(sql, i, current); continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { i = CopyLineComment(sql, i, current); continue; }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { i = CopyBlockComment(sql, i, current); continue; }

            if (c == ';' && blockDepth == 0)
            {
                AppendIfNonEmpty(current, result);
                current.Clear();
                i++;
                continue;
            }

            // Word-boundary BEGIN/CASE (open) / END (close). Match only when bounded
            // by non-identifier characters so the words inside a larger identifier
            // are ignored. BEGIN…END and CASE…END both terminate with END, so
            // counting CASE as an opener keeps the nesting balanced.
            if (IsWordBoundary(sql, i - 1))
            {
                if ((Matches(sql, i, "BEGIN") && IsWordEndAt(sql, i + 5))
                    || (Matches(sql, i, "CASE") && IsWordEndAt(sql, i + 4)))
                {
                    blockDepth++;
                }
                else if (Matches(sql, i, "END") && IsWordEndAt(sql, i + 3))
                {
                    if (blockDepth > 0) blockDepth--;
                }
            }

            current.Append(c);
            i++;
        }
        AppendIfNonEmpty(current, result);
        return result;
    }

    // Copies an opaque run starting at <paramref name="i"/> into <paramref name="sink"/>
    // and returns the index just past it. Each handles its own terminator.
    private static int CopyString(string s, int i, System.Text.StringBuilder sink)
    {
        int start = i++;
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; } // '' escape
                i++;
                break;
            }
            i++;
        }
        sink.Append(s, start, i - start);
        return i;
    }

    private static int CopyQuotedIdent(string s, int i, System.Text.StringBuilder sink)
    {
        int start = i++;
        while (i < s.Length && s[i] != '"') i++;
        if (i < s.Length) i++;
        sink.Append(s, start, i - start);
        return i;
    }

    private static int CopyLineComment(string s, int i, System.Text.StringBuilder sink)
    {
        int start = i;
        while (i < s.Length && s[i] != '\n') i++;
        sink.Append(s, start, i - start);
        return i;
    }

    private static int CopyBlockComment(string s, int i, System.Text.StringBuilder sink)
    {
        int start = i;
        i += 2;
        while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
        i = i + 1 < s.Length ? i + 2 : s.Length;
        sink.Append(s, start, i - start);
        return i;
    }

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

    private static void AppendIfNonEmpty(System.Text.StringBuilder builder, List<string> sink)
    {
        var trimmed = builder.ToString().Trim();
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
