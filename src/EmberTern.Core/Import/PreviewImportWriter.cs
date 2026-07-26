using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import;

/// <summary>
/// The writer behind the <b>converted preview</b> (§3.6): it keeps every row the pipeline hands it and writes
/// nothing anywhere.
/// <para>
/// ⭐ <b>The preview is the pipeline, not a second converter.</b> §3.6 promises the grid shows "exactly what
/// will reach the database" — a promise that can only be kept if the same converter, the same validator, the
/// same mapping and the same culture produce it. So the preview runs <see cref="ImportPipeline"/> with a
/// <see cref="Providers.BoundedImportProvider"/> in front and this writer behind: two different arguments, the
/// same one import. The alternative — a private "convert for display" routine — is exactly the second path that
/// would drift until the preview stopped predicting anything.
/// </para>
/// <para>
/// It differs from <see cref="DryRunImportWriter"/> in the one way that matters: a dry run validates a WHOLE
/// file and must therefore keep nothing, while a preview shows a bounded head and must keep everything it saw.
/// Folding the two together would mean a "Validate" over a million malformed rows retained a million rows.
/// </para>
/// <para>
/// <see cref="ImportWriteSummary.TransactionLeftOpen"/> is <c>false</c>: a preview leaves nothing to commit or
/// roll back, and saying otherwise would be the lie §0.6 forbids.
/// </para>
/// </summary>
public sealed class PreviewImportWriter : IImportWriter
{
    private readonly List<ImportRow> _rows = new();
    private readonly int _maxRows;
    private bool _begun;

    /// <param name="maxRows">Upper bound on retained rows. A belt-and-braces bound: the provider is normally
    /// bounded too, and keeping the cap here as well means a caller that forgets cannot turn a preview into a
    /// memory leak.</param>
    public PreviewImportWriter(int maxRows)
    {
        _maxRows = maxRows < 0 ? 0 : maxRows;
    }

    /// <summary>The rows exactly as they would have been sent, in source order, each carrying its own
    /// <see cref="ImportRow.SourceRowNumber"/> so the grid shows the number the user can find in their file.</summary>
    public IReadOnlyList<ImportRow> Rows => _rows;

    /// <summary>The mapping the run was prepared with — the columns, in the order the values are aligned to.</summary>
    public IReadOnlyList<ColumnMapping> Mapping { get; private set; } = Array.Empty<ColumnMapping>();

    public Task BeginAsync(
        ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (target is null) throw new ArgumentNullException(nameof(target));
        Mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _begun = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ImportRow row, CancellationToken cancellationToken)
    {
        if (row is null) throw new ArgumentNullException(nameof(row));
        if (!_begun) throw new InvalidOperationException("BeginAsync must be called before the first row.");
        cancellationToken.ThrowIfCancellationRequested();

        // ⚠ Queued and RETAINED are counted separately on purpose. The flush must report one result per row the
        // pipeline handed over, whether or not this writer kept it: returning fewer results is how a real batch
        // says "I stopped here", and the pipeline would honestly report the remainder as never attempted. A cap
        // on memory must not become a cap on what the run claims to have done.
        _queued++;
        if (_rows.Count < _maxRows) _rows.Add(row);
        return Task.CompletedTask;
    }

    /// <summary>Reports one success per row queued since the last flush — the same 1:1 alignment a real batch
    /// returns, so the pipeline's "batch index → source row number" window behaves identically here.</summary>
    public Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pending = _queued - _flushed;
        if (pending <= 0)
            return Task.FromResult<IReadOnlyList<ImportBatchItemResult>>(Array.Empty<ImportBatchItemResult>());

        var results = new ImportBatchItemResult[pending];
        for (var i = 0; i < results.Length; i++) results[i] = ImportBatchItemResult.Success;
        _flushed = _queued;

        return Task.FromResult<IReadOnlyList<ImportBatchItemResult>>(results);
    }

    private int _queued;
    private int _flushed;

    public Task<ImportWriteSummary> CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ImportWriteSummary(_queued, 0, TransactionLeftOpen: false));
    }
}
