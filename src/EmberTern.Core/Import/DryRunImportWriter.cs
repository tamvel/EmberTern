using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import;

/// <summary>
/// The writer behind the <b>"Validate"</b> button: it accepts every row the pipeline hands it and writes
/// nothing anywhere.
/// <para>
/// ⭐ <b>This is a product feature, not a test double</b> (design §4.3). That distinction is what makes the
/// whole thing work: because "Validate" is a real writer on the real pipeline, a dry run and a real import take
/// <em>literally the same path</em> — same provider, same mapping, same conversion, same validation, same
/// batching, same error policy, same report. A separate "validation mode" inside the pipeline would be a second
/// implementation of the import, and the two would drift until "Validate says fine" stopped meaning anything.
/// </para>
/// <para>
/// It also satisfies rule #2 honestly (<see cref="IImportWriter"/> has two production implementations from day
/// one), and it is why etaps I1–I3 deliver <b>complete functionality with no database and no UI</b>.
/// </para>
/// <para>
/// <b>Every row it reports is a success</b>, and that is correct rather than lazy: by the time a row reaches a
/// writer it has already passed conversion and validation, so the only failures a real writer adds are the
/// server's own (a constraint, a unique key, a trigger). A dry run cannot know those without asking the server,
/// and inventing a verdict would be exactly the guess §0 forbids. What it verifies is everything the client can
/// verify — which is the whole point of being able to fix the FILE before anything touches the database.
/// </para>
/// </summary>
public sealed class DryRunImportWriter : IImportWriter
{
    private readonly List<ImportRow> _batch = new();
    private long _rowsAccepted;
    private bool _begun;

    /// <summary>The target the run was prepared for; <c>null</c> before <see cref="BeginAsync"/>.</summary>
    public ImportTarget? Target { get; private set; }

    /// <summary>The mapping the run was prepared with — the columns an INSERT would have named.</summary>
    public IReadOnlyList<ColumnMapping> Mapping { get; private set; } = Array.Empty<ColumnMapping>();

    /// <summary>How many rows passed the whole client-side pipeline.</summary>
    public long RowsAccepted => _rowsAccepted;

    public Task BeginAsync(
        ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Target = target ?? throw new ArgumentNullException(nameof(target));
        Mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _begun = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ImportRow row, CancellationToken cancellationToken)
    {
        if (row is null) throw new ArgumentNullException(nameof(row));
        if (!_begun) throw new InvalidOperationException("BeginAsync must be called before the first row.");
        cancellationToken.ThrowIfCancellationRequested();

        _batch.Add(row);
        return Task.CompletedTask;
    }

    /// <summary>Reports one success per queued row, in queue order — the same 1:1 alignment a real batch
    /// returns, so the pipeline's "batch index → source row number" window is exercised by a dry run too
    /// rather than only on live data.</summary>
    public Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_batch.Count == 0)
            return Task.FromResult<IReadOnlyList<ImportBatchItemResult>>(Array.Empty<ImportBatchItemResult>());

        var results = new ImportBatchItemResult[_batch.Count];
        for (var i = 0; i < results.Length; i++) results[i] = ImportBatchItemResult.Success;

        _rowsAccepted += _batch.Count;
        _batch.Clear();

        return Task.FromResult<IReadOnlyList<ImportBatchItemResult>>(results);
    }

    /// <summary><see cref="ImportWriteSummary.TransactionLeftOpen"/> is <c>false</c>, and the report must be
    /// able to say so plainly: a dry run leaves nothing to Commit or Roll back, because it wrote nothing. A
    /// "Validate" that ended with an open transaction would be a lie of exactly the kind §0.6 forbids.</summary>
    public Task<ImportWriteSummary> CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Anything still queued was accepted; counting it keeps the totals exact even if the caller completes
        // without a final flush.
        _rowsAccepted += _batch.Count;
        _batch.Clear();

        return Task.FromResult(new ImportWriteSummary(_rowsAccepted, 0, TransactionLeftOpen: false));
    }
}
