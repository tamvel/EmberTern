using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Writes converted rows into the user's working transaction, in batches.
/// <para>
/// ⭐ <b>Batched because it was MEASURED, not assumed.</b> I0 clocked <see cref="FbBatchCommand"/> at ~121 000
/// rows/s against 7 313 for a prepared loop — 16× — and, more importantly, it passed the <em>blocking</em>
/// condition: it reports WHICH row failed, aligned 1:1 with the order rows were queued. A faster path that
/// could not name the offending row would have been rejected, because a report that cannot point at a row is
/// useless (§3.7).
/// </para>
/// <para>
/// ⭐ <b>The error policy is enforced by the server round trip, not re-implemented here.</b> I0 found that
/// <c>MultiError</c> maps 1:1 onto <see cref="ImportErrorPolicy"/>: <c>false</c> stops the batch AT the
/// offending row (<see cref="ImportErrorPolicy.StopOnFirstError"/>), <c>true</c> continues and reports every
/// failing index (<see cref="ImportErrorPolicy.SkipInvalidRows"/>). The policy the user picked is therefore one
/// flag, not a client-side loop pretending to be one.
/// </para>
/// <para>
/// <b>Transaction discipline (hard rule #3).</b> Since I7.5 it runs in <b>the import module's OWN</b>
/// transaction (<see cref="ImportSessionConnection"/>), not the console's: a Commit here must persist this
/// import and nothing else, and while the module shared the user's working transaction it could also persist
/// whatever the SQL Editor had left uncommitted. It auto-<em>begins</em> and never commits.
/// <see cref="CompleteAsync"/> therefore always reports <c>TransactionLeftOpen: true</c>: the user reviews the
/// report and then presses Commit or Rollback. (<c>Batched</c> commit-every-N is the coordinator's business in
/// etap I7, because it is a decision about the user's transaction, not about writing rows.)
/// </para>
/// <para>
/// <b>Locking:</b> the Data lane's <c>CommandLock</c> is held for the duration of each batch — one
/// acquire/release per round trip, captured into a local first (gotchas #98 / #120 / #236). I0 measured the
/// cost as below the noise floor, so correctness here is free.
/// </para>
/// </summary>
public sealed class FirebirdImportWriter : IImportWriter
{
    private readonly ImportSessionConnection _session;
    private readonly ImportErrorPolicy _errorPolicy;

    private FbBatchCommand? _batch;
    private int _queued;
    private long _rowsWritten;
    private long _rowsFailed;
    private string _insertSql = string.Empty;

    public FirebirdImportWriter(ImportSessionConnection session, ImportErrorPolicy errorPolicy)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _errorPolicy = errorPolicy;
    }

    /// <summary>The INSERT this writer sends, for the audit/diagnostic surfaces. Empty before
    /// <see cref="BeginAsync"/>.</summary>
    public string InsertSql => _insertSql;

    /// <summary>
    /// ⭐ The driver flag the error policy becomes. Exposed so a test can assert the mapping without a server,
    /// because getting it backwards would silently change what a run does: <c>StopOnFirstError</c> would sail
    /// past the bad rows, or <c>SkipInvalidRows</c> would stop dead on the first one.
    /// </summary>
    public static bool MultiErrorFor(ImportErrorPolicy policy)
        => policy == ImportErrorPolicy.SkipInvalidRows;

    public async Task BeginAsync(
        ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken cancellationToken)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));

        _insertSql = BuildInsertSql(target, mapping);

        // Auto-BEGIN, never auto-commit (rule #3) — into the MODULE's own transaction, which is the whole
        // point of I7.5: nothing this writer does can settle work the import did not perform.
        await _session.BeginAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task WriteAsync(ImportRow row, CancellationToken cancellationToken)
    {
        if (row is null) throw new ArgumentNullException(nameof(row));
        if (_insertSql.Length == 0)
            throw new InvalidOperationException("BeginAsync must be called before the first row.");

        cancellationToken.ThrowIfCancellationRequested();

        var batch = EnsureBatch();
        var parameters = batch.AddBatchParameters();
        for (var i = 0; i < row.Values.Length; i++)
        {
            parameters.AddWithValue(
                "@v" + i.ToString(CultureInfo.InvariantCulture), row.Values[i] ?? DBNull.Value);
        }
        _queued++;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends the queued rows and returns one result per row, in queue order.
    /// <para>
    /// With <c>MultiError = false</c> the driver stops AT the offending row, so FEWER results come back than
    /// rows were queued — measured, and deliberately passed through unchanged: the pipeline reads "these are
    /// the rows that were attempted", and the ones past that point are honestly neither written nor failed.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_batch is null || _queued == 0) return Array.Empty<ImportBatchItemResult>();

        var batch = _batch;
        _batch = null;
        var queued = _queued;
        _queued = 0;

        // Capture the lock ONCE — re-evaluating the accessor at Release can leak a semaphore (gotchas #98/#120).
        var commandLock = _session.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            batch.Transaction = _session.Transaction;
            var results = Interpret(batch.ExecuteNonQuery(), queued);
            return results;
        }
        catch (FbException ex)
        {
            // The batch itself was refused, so no per-row verdicts exist. Every queued row carries the same
            // cause rather than being silently dropped — the pipeline must be able to account for each one.
            var failure = FirebirdImportErrorMapper.Map(ex);
            var results = new ImportBatchItemResult[queued];
            for (var i = 0; i < results.Length; i++) results[i] = failure;
            _rowsFailed += queued;
            return results;
        }
        finally
        {
            commandLock.Release();
            await batch.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<ImportWriteSummary> CompleteAsync(CancellationToken cancellationToken)
    {
        // Anything still queued has not been sent; flushing it here keeps the contract's promise that
        // CompleteAsync leaves nothing pending.
        if (_batch is not null && _queued > 0)
        {
            await FlushBatchAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_batch is not null)
        {
            await _batch.DisposeAsync().ConfigureAwait(false);
            _batch = null;
        }

        // Always TRUE: this writer never commits (rule #3). The report says "N rows inserted — transaction
        // open, commit or roll back", which is the honest sentence §0.6 demands instead of "import succeeded".
        return new ImportWriteSummary(_rowsWritten, _rowsFailed, TransactionLeftOpen: true);
    }

    private FbBatchCommand EnsureBatch()
    {
        if (_batch is not null) return _batch;

        var connection = _session.Connection;
        _batch = new FbBatchCommand(_insertSql, connection, _session.Transaction)
        {
            // The measured 1:1 mapping onto the user's chosen policy (I0 §2.3).
            MultiError = MultiErrorFor(_errorPolicy),
        };
        return _batch;
    }

    private ImportBatchItemResult[] Interpret(FbBatchNonQueryResult result, int queued)
    {
        var count = Math.Min(result.Count, queued);
        var results = new ImportBatchItemResult[count];

        for (var i = 0; i < count; i++)
        {
            var item = result[i];
            if (item.IsSuccess)
            {
                results[i] = ImportBatchItemResult.Success;
                _rowsWritten++;
                // The session tracks what is unsettled, so the close guard can say how much would be lost.
                _session.CountWritten(1);
                continue;
            }

            results[i] = FirebirdImportErrorMapper.Map(item.Exception);
            _rowsFailed++;
        }

        return results;
    }

    /// <summary>
    /// Builds the INSERT for the mapped columns.
    /// <para>
    /// ⭐ <b><c>OVERRIDING SYSTEM VALUE</c> is not optional.</b> Firebird REJECTS an INSERT that names a
    /// <c>GENERATED ALWAYS</c> identity column without it — so a mapping that deliberately writes such a column
    /// would otherwise fail on the very first row with a message about a clause the user never heard of
    /// (design R10). The fact lives in <see cref="ColumnSpec.Identity"/>, which is why that enum distinguishes
    /// ALWAYS from BY DEFAULT rather than collapsing both into a bool.
    /// </para>
    /// <para>
    /// Internal and pure so it can be pinned without a server.
    /// </para>
    /// </summary>
    internal static string BuildInsertSql(ImportTarget target, IReadOnlyList<ColumnMapping> mapping)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ");
        AppendIdentifier(sb, target.TableName);
        sb.Append(" (");

        var needsOverride = false;
        for (var i = 0; i < mapping.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            AppendIdentifier(sb, mapping[i].TargetColumnName);

            var column = target.FindColumn(mapping[i].TargetColumnName);
            if (column is not null && ImportTarget.RequiresOverridingSystemValue(column)) needsOverride = true;
        }

        sb.Append(')');
        if (needsOverride) sb.Append(" OVERRIDING SYSTEM VALUE");

        sb.Append(" VALUES (");
        for (var i = 0; i < mapping.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("@v").Append(i.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(')');

        return sb.ToString();
    }

    private static void AppendIdentifier(StringBuilder sb, string identifier)
        => sb.Append('"').Append(identifier.Replace("\"", "\"\"")).Append('"');
}
