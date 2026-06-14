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
    /// Splits <paramref name="sql"/> on top-level semicolons, then executes each
    /// non-empty statement in order. Auto-begins the user's working transaction
    /// when none is active (mirrors <see cref="FirebirdQueryExecutor"/>'s F5
    /// path) so DDL participates in Commit / Rollback exactly like DML — the
    /// user can Add a field, see it appear, then Rollback to undo.
    /// Throws <see cref="DdlExecutionException"/> with the server's message on
    /// the first FbException — the caller is expected to stop the Compile run
    /// at that point.
    /// </summary>
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        var statements = SplitStatements(sql);
        if (statements.Count == 0) return;

        if (_transactionService is { IsActive: false })
        {
            try
            {
                await _transactionService.BeginTransactionAsync().ConfigureAwait(false);
            }
            catch (TransactionFailedException ex)
            {
                throw new DdlExecutionException(ex.Message, ex);
            }
        }

        // Run on this executor's lane (metadata in production). The connection, lock,
        // and transaction all come from the injected TransactionService so DDL lands on
        // the metadata attachment under the metadata profile.
        var connection = _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
        var commandLock = _transactionService?.CommandLock ?? _connectionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = statement;
                cmd.CommandTimeout = 0;
                if (_transactionService?.ActiveTransaction is { } tx)
                {
                    cmd.Transaction = tx;
                }
                try
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (FbException ex)
                {
                    throw new DdlExecutionException(ex.Message, ex);
                }
            }
        }
        finally
        {
            commandLock.Release();
        }

        // Counter tick after release so the transaction bar updates outside the
        // lock. One tick per Compile call regardless of statement count is the
        // expected UX — the user thinks of the whole batch as one structural
        // edit, not five separate statements.
        _transactionService?.NotifyStatementExecuted();
    }

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
