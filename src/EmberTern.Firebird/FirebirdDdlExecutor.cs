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

    // Krok 1: fixed NOWAIT (write + read_committed + rec_version + nowait) — the
    // explicit FbTransactionOptions the old transient path never supplied, which is
    // why configured profiles never reached Compile. Behaviour is identical to
    // before (NOWAIT, fail-fast). The Standard/Developer switch will choose NOWAIT
    // vs WAIT + lock timeout here in a later step; nothing else needs to change.
    private static FbTransactionOptions BuildDdlTransactionOptions() => new()
    {
        TransactionBehavior =
            FbTransactionBehavior.Write
            | FbTransactionBehavior.ReadCommitted
            | FbTransactionBehavior.RecVersion
            | FbTransactionBehavior.NoWait,
    };

    /// <summary>
    /// Splits a multi-statement DDL string on TOP-LEVEL semicolons.
    /// "Top-level" means outside a <c>BEGIN … END</c> PSQL block — CREATE
    /// TRIGGER bodies have their own internal semicolons (e.g. assignment
    /// statements inside BEGIN/END) that must NOT terminate the outer CREATE
    /// TRIGGER. The scanner tracks a single BEGIN/END nesting counter
    /// (case-insensitive, word-boundary match) — enough for the shapes
    /// EmberTern emits today (no nested triggers, no procedures).
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string sql)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) return result;

        var current = new System.Text.StringBuilder();
        var beginDepth = 0;
        for (int i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == ';' && beginDepth == 0)
            {
                AppendIfNonEmpty(current, result);
                current.Clear();
                continue;
            }
            current.Append(c);

            // Word-boundary BEGIN/END detection. Match only when surrounded by
            // non-identifier characters on both sides so 'BEGIN' inside an
            // identifier (extremely unlikely in our DDL but cheap to guard) is
            // ignored. Track the start position of each token.
            if (IsWordBoundary(sql, i) && i + 1 < sql.Length)
            {
                if (Matches(sql, i + 1, "BEGIN") && IsWordEndAt(sql, i + 1 + 5))
                {
                    beginDepth++;
                }
                else if (Matches(sql, i + 1, "END") && IsWordEndAt(sql, i + 1 + 3))
                {
                    if (beginDepth > 0) beginDepth--;
                }
            }
        }
        AppendIfNonEmpty(current, result);
        return result;
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
