using System;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// The two things an import does to the target table <b>around</b> the rows: counting what is already there, and
/// emptying it first.
/// <para>
/// Both run on the <b>Data lane, inside THE user's working transaction</b> (design §4.5). That is the whole
/// reason they are not on <c>FirebirdImportTargetReader</c>, which is a Metadata-lane reader: emptying a table is
/// data, not schema, so it must be rolled back together with the import — and the count that justifies it must
/// be read by the transaction that is going to do the deleting, or it would be answering about a different world
/// than the one the user is about to change.
/// </para>
/// <para>
/// Mechanically it is <c>FirebirdDataEditor</c>'s pattern, unchanged: auto-begin the working transaction (never
/// auto-commit — rule #3), hold the Data lane's <c>CommandLock</c> for the round trip with the accessor captured
/// into a local first (gotchas #98 / #120), and tick the statement counter afterwards so the transaction bar
/// reflects that this transaction now has work in it.
/// </para>
/// </summary>
public sealed class FirebirdImportTargetPreparer
{
    private readonly TransactionService _transactionService;

    public FirebirdImportTargetPreparer(TransactionService transactionService)
    {
        _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
    }

    /// <summary>
    /// How many rows the target holds right now, as the user's own transaction sees them.
    /// <para>
    /// Read ONCE, when a run is about to start — deliberately not on every target change. It exists to turn
    /// "empty the table first" from a checkbox into a sentence with a number in it ("this deletes N rows"), and
    /// that question is only asked at the moment the user commits to a run.
    /// </para>
    /// </summary>
    public Task<long> CountRowsAsync(string tableName, CancellationToken cancellationToken)
    {
        RequireTableName(tableName);
        return ExecuteAsync(
            "SELECT COUNT(*) FROM " + Quote(tableName),
            async cmd =>
            {
                var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return scalar is null or DBNull ? 0L : Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
            },
            countsAsWork: false,
            cancellationToken);
    }

    /// <summary>
    /// <c>DELETE FROM &lt;target&gt;</c> inside the working transaction (decision D5) — never a
    /// <c>TRUNCATE</c>-like shortcut, precisely so a Rollback takes the deletion with it.
    /// </summary>
    public Task<long> EmptyAsync(string tableName, CancellationToken cancellationToken)
    {
        RequireTableName(tableName);
        return ExecuteAsync(
            "DELETE FROM " + Quote(tableName),
            async cmd =>
            {
                var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (long)affected;
            },
            countsAsWork: true,
            cancellationToken);
    }

    private async Task<long> ExecuteAsync(
        string sql, Func<FbCommand, Task<long>> run, bool countsAsWork, CancellationToken cancellationToken)
    {
        if (!_transactionService.IsActive)
        {
            await _transactionService.BeginTransactionAsync().ConfigureAwait(false);
        }

        var connection = _transactionService.RequireOpenConnection();
        var commandLock = _transactionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        long result;
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService.ActiveTransaction;
            result = await run(cmd).ConfigureAwait(false);
        }
        finally
        {
            commandLock.Release();
        }

        // Ticked after the release, so the transaction-bar update does not sit behind the lock. Only a statement
        // that CHANGED something counts: a COUNT(*) leaves the transaction with nothing to commit, and claiming
        // otherwise would light the "this transaction has work in it" marker over an empty transaction.
        if (countsAsWork) _transactionService.NotifyStatementExecuted();
        return result;
    }

    private static void RequireTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("A target table name is required.", nameof(tableName));
    }

    /// <summary>Quotes the catalog-cased identifier. Internal and pure so it can be pinned without a server.</summary>
    internal static string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
