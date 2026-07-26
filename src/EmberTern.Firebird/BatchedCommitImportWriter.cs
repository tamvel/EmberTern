using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;

namespace EmberTern.Firebird;

/// <summary>
/// <see cref="ImportTransactionMode.Batched"/>: commits the working transaction every N accepted rows, then
/// opens a fresh one, so a million-row file does not need a transaction that lives for an hour (design §4.5 —
/// the very thing EmberTern's own Session Manager warns about).
/// <para>
/// ⭐ <b>A decorator, not a change to <see cref="FirebirdImportWriter"/>.</b> Writing rows and deciding the fate
/// of the user's transaction are two responsibilities, and the row writer is deliberately the one component in
/// this module that never commits. Keeping the commit here means <c>Manual</c> and <c>AutoCommitOnSuccess</c>
/// run through byte-identical code, and the mode that is not atomic is the only one carrying the machinery that
/// makes it not atomic.
/// </para>
/// <para>
/// ⚠ <b>The commit happens only at a FLUSH boundary, and that is load-bearing.</b> Right after
/// <see cref="IImportWriter.FlushBatchAsync"/> returns, the inner writer has disposed its <c>FbBatchCommand</c>
/// and holds no open batch; the next row builds a new one against whatever transaction is then active. Anywhere
/// else, a commit would pull the transaction out from under a live batch.
/// </para>
/// <para>
/// ⚠ <b>Consequence, measured live in I7: <c>commitEveryRows</c> is a FLOOR, not an exact multiple.</b> A commit
/// lands at the first flush boundary <em>at or past</em> N, so with the measured defaults (batch 500, commit
/// 10 000) it lands exactly on 10 000 — but a commit interval SMALLER than the batch size gives one commit per
/// batch, because a commit cannot cut a batch in half. The alternative would be to shrink the batch to match,
/// and I0 measured that batch size is the thing that actually costs throughput while commit frequency is very
/// nearly free — so the batch wins, and the commit interval bends. Stated here rather than smoothed over,
/// because the number the user sets is the number the report will be read against.
/// </para>
/// <para>
/// <b>Honesty about what this costs (§0.5):</b> committed rows survive a later failure and a later Rollback.
/// That is disclosed by the readiness strip before the run, and the report says how many rows were already
/// committed. <see cref="CompleteAsync"/> still reports whatever the inner writer says about the tail — the
/// rows since the last commit are genuinely still open.
/// </para>
/// </summary>
public sealed class BatchedCommitImportWriter : IImportWriter, IPartiallyCommittedImportWriter
{
    private readonly IImportWriter _inner;
    private readonly TransactionService _transactionService;
    private readonly long _commitEveryRows;

    private long _acceptedSinceCommit;

    public BatchedCommitImportWriter(
        IImportWriter inner, TransactionService transactionService, int commitEveryRows)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        _commitEveryRows = Math.Max(1, commitEveryRows);
    }

    /// <summary>Rows this writer has committed. The report needs it to say what a Rollback can no longer
    /// undo.</summary>
    public long RowsCommitted { get; private set; }

    public Task BeginAsync(
        ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken cancellationToken)
        => _inner.BeginAsync(target, mapping, cancellationToken);

    public Task WriteAsync(ImportRow row, CancellationToken cancellationToken)
        => _inner.WriteAsync(row, cancellationToken);

    public async Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken cancellationToken)
    {
        var results = await _inner.FlushBatchAsync(cancellationToken).ConfigureAwait(false);

        foreach (var result in results)
        {
            if (result.IsSuccess) _acceptedSinceCommit++;
        }

        if (_acceptedSinceCommit >= _commitEveryRows) await CommitAsync().ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Flushes the tail through the inner writer and reports its summary unchanged.
    /// <para>
    /// It deliberately does NOT commit the remainder: whether the last partial batch is kept is the user's
    /// decision, taken in front of the report's numbers, exactly as in <c>Manual</c>. Committing here would make
    /// the mode silently atomic-at-the-end and take that decision away.
    /// </para>
    /// </summary>
    public Task<ImportWriteSummary> CompleteAsync(CancellationToken cancellationToken)
        => _inner.CompleteAsync(cancellationToken);

    private async Task CommitAsync()
    {
        if (!_transactionService.IsActive) return;

        RowsCommitted += _acceptedSinceCommit;
        _acceptedSinceCommit = 0;

        await _transactionService.CommitAsync().ConfigureAwait(false);

        // Re-open immediately so the next row does not have to discover there is no transaction. The auto-begin
        // in the inner writer only runs once, before the first row.
        await _transactionService.BeginTransactionAsync().ConfigureAwait(false);
    }
}
