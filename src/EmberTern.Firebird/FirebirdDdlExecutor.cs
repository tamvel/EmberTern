using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Executes DDL statements ("CREATE TABLE", "ALTER TABLE", "CREATE GENERATOR",
/// "CREATE TRIGGER", …) in ONE autonomous, auto-committed transaction on the dedicated
/// DDL attachment (<see cref="ConnectionRole.Ddl"/>).
///
/// <para>The DDL attachment carries nothing but DDL and never holds a working transaction,
/// so Compile is INDEPENDENT of whatever the user left open elsewhere — an un-committed
/// SELECT in the SQL editor (Data lane) no longer blocks compiling a trigger. The old
/// "Commit or roll back the active transaction before running DDL." guard existed only
/// because DDL shared the Data connection (one FbConnection = one transaction, gotcha #89);
/// with its own attachment that constraint is gone and the guard is deleted.</para>
///
/// <para>The cross-attachment "object … is in use" that once forced DDL onto the main
/// connection is a TRANSIENT metadata-cache lock (measured on FB5): it bites only a NOWAIT
/// transaction, and a WAIT transaction clears it in ~10 ms. So the DDL transaction always
/// uses WAIT with a bounded lock timeout — see <see cref="BuildDdlTransactionOptions(bool)"/>.
/// That, not co-location, is the actual fix.</para>
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

    // The TransactionService (DATA lane) is consulted ONLY in degraded mode — i.e. when the
    // dedicated DDL attachment failed to open and DDL falls back onto the Data connection,
    // where one-tx-per-connection (gotcha #89) still applies.
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
        CancellationToken cancellationToken = default,
        IProgress<(int Index, string? Error)>? progress = null)
        // Apply the Developer-Mode-aware DDL TPB (WAIT + lock timeout in Dev Mode, else
        // NOWAIT) so recompile honours the connection's mode instead of the driver default.
        => _connectionService.ExecuteAdminBatchAsync(statements, cancellationToken, progress, BuildDdlTransactionOptions());

    /// <summary>
    /// Splits <paramref name="sql"/> on top-level semicolons, then runs the whole batch in ONE
    /// autonomous transaction on the dedicated DDL attachment, auto-committing on success. The
    /// batch is atomic (e.g. ADD FIELD + CREATE GENERATOR + CREATE TRIGGER all-or-nothing).
    ///
    /// No working transaction anywhere can block this: the DDL attachment holds none. The only
    /// exception is DEGRADED mode (the DDL attachment failed to open → we fall back to the Data
    /// connection); there, and only there, an active data working transaction still has to be
    /// settled first, and we say so plainly rather than surfacing the driver's raw
    /// "Parallel transactions are not supported".
    ///
    /// Throws <see cref="DdlExecutionException"/> with the server's message on the
    /// first FbException — the caller stops the Compile run at that point.
    /// </summary>
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        var statements = SplitStatements(sql);
        if (statements.Count == 0) return;

        // Degraded mode ONLY: without its own attachment, DDL shares the Data connection, which
        // allows one transaction at a time (gotcha #89).
        if (!_connectionService.DdlIsIndependent && _transactionService is { IsActive: true })
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
        catch (CharsetRepresentationException ex)
        {
            // Translated into this module's own exception so every existing Compile error surface keeps
            // working unchanged. ⚠ The original stays as InnerException, so its localized form is still
            // reachable — see CharsetRepresentationException.
            throw new DdlExecutionException(ex.Message, ex);
        }
    }

    /// <summary>Lock timeout for Developer Mode — DDL waits this long for an object another
    /// SESSION is using before giving up.</summary>
    internal const int DdlLockTimeoutSeconds = 10;

    /// <summary>Lock timeout for Standard Mode. Short on purpose: it is long enough to absorb
    /// the ~10 ms metadata-cache release from OUR OWN other attachments (the Data lane that
    /// executed the routine — the transient cross-attachment "object is in use"), while still
    /// failing fast against an object another session genuinely holds.</summary>
    internal const int DdlSelfReleaseTimeoutSeconds = 3;

    private FbTransactionOptions BuildDdlTransactionOptions()
        => BuildDdlTransactionOptions(_connectionService.ActiveProfile?.DeveloperMode ?? false);

    // write + read_committed + rec_version + WAIT, bounded by a lock timeout.
    //
    // WAIT (not NOWAIT) is the actual fix for the cross-attachment "object … is in use":
    // measured on FB5, that lock is TRANSIENT — the attachment holding the routine in its
    // metadata cache releases it on demand, so a WAIT transaction succeeds in ~10 ms whereas
    // NOWAIT fails instantly. NOWAIT is what forced DDL to be co-located on the Data
    // connection (and thus forced the "settle your transaction first" guard). With WAIT, DDL
    // runs happily on its own attachment.
    //
    // The two modes differ only in HOW LONG they wait: Standard fails fast against another
    // session; Developer Mode waits longer. Pure + internal so a unit test pins both shapes
    // without a live Firebird. Affects ONLY the DDL path; data ops are unchanged.
    internal static FbTransactionOptions BuildDdlTransactionOptions(bool developerMode)
    {
        var behavior =
            FbTransactionBehavior.Write
            | FbTransactionBehavior.ReadCommitted
            | FbTransactionBehavior.RecVersion
            | FbTransactionBehavior.Wait;

        return new FbTransactionOptions
        {
            TransactionBehavior = behavior,
            WaitTimeout = TimeSpan.FromSeconds(
                developerMode ? DdlLockTimeoutSeconds : DdlSelfReleaseTimeoutSeconds),
        };
    }

    /// <summary>
    /// Splits a multi-statement DDL string into individual statements for the
    /// one-statement-per-<c>FbCommand</c> loop. Delegates to
    /// <see cref="SqlStatementSplitter"/> (Etap 2), which rides the shared parser's
    /// statement boundaries — the single authority for "what is a statement". Plain
    /// DDL/DML terminates at the next top-level <c>;</c>; a <c>CREATE/ALTER/RECREATE</c>
    /// of a <c>PROCEDURE/TRIGGER/FUNCTION/PACKAGE</c> stays whole (its DECLARE-section +
    /// body semicolons never split it — gotchas #55/#117/#128/#140/#152).
    /// <para>
    /// §0 (Paramount Law): the output is the exact DDL sent to the server, so it is
    /// byte-for-byte identical to the previous char-based splitter — pinned by a
    /// differential corpus test (old algorithm vs. the parser-backed splitter).
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string sql) => SqlStatementSplitter.Split(sql);
}

public sealed class DdlExecutionException : Exception
{
    public DdlExecutionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
