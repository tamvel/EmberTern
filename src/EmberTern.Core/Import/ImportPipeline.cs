using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import;

/// <summary>
/// ⭐ <b>The one import.</b> Reads records, projects them through the mapping, converts, validates, batches them
/// into a writer, and reports what happened — once, for every source and every destination (design §4.4).
/// <para>
/// Two properties are the whole design, and both are structural rather than promised:
/// <list type="bullet">
/// <item><b>It does not know what it is reading.</b> CSV, a workbook and the clipboard differ only in which
/// <see cref="IImportProvider"/> produced the <see cref="RawRecord"/>s. Adding a format adds a provider and
/// nothing here.</item>
/// <item><b>It does not know whether it is writing.</b> "Validate" is <see cref="DryRunImportWriter"/> passed
/// instead of the Firebird one — a different argument, not a different mode. A dry run therefore cannot drift
/// away from the real import, because there is no second path to drift.</item>
/// </list>
/// </para>
/// <para>
/// ⭐ <b>It owns the "batch index → source row number" window</b> (decision D9). A batched write reports its
/// failures by POSITION IN THE BATCH, and the report must name the row the user can find in their file — so the
/// translation happens here, once, and nothing downstream ever sees a batch index.
/// </para>
/// <para>
/// <b>What it deliberately leaves to others:</b> creating a table (Ddl lane, before the run — gotcha #213),
/// emptying the target, and committing or rolling back. Hard rule #3 is not negotiable: the pipeline never
/// finalizes a transaction, and <see cref="ImportOutcome.TransactionLeftOpen"/> is how the report says so.
/// <c>Batched</c> commit-every-N likewise belongs to the writer, which is the only thing that has a
/// transaction.
/// </para>
/// </summary>
public static class ImportPipeline
{
    /// <summary>Rows between progress reports. Throttled like the Script Executor's: a per-row report on a
    /// million-row import costs more than the import.</summary>
    public const int ProgressRowInterval = 200;

    /// <summary>Minimum wall time between progress reports.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Runs one import to completion, to a stop, or to a cancellation.
    /// </summary>
    /// <param name="configuration">Every decision the user made — the same value a profile stores (§4.8.1).</param>
    /// <param name="target">The resolved target. A FACT read from the catalog, so it is a parameter rather than
    /// part of the configuration (§4.8.2).</param>
    /// <param name="provider">Reads the source. The pipeline never learns which one it got.</param>
    /// <param name="source">Where the bytes/text come from.</param>
    /// <param name="writer">Where rows go — Firebird, or the dry run.</param>
    /// <param name="connectionEncoding">The CONNECTION charset, built by <see cref="ImportCharsetGuard.Strict"/>.
    /// <c>null</c> skips the representability check; supply it for any run against a real connection, because
    /// without it an unrepresentable character is written as <c>?</c> with no error at all (design R1).</param>
    /// <param name="progress">Throttled live counters, or <c>null</c>.</param>
    public static async Task<ImportOutcome> RunAsync(
        ImportConfiguration configuration,
        ImportTarget target,
        IImportProvider provider,
        IImportSource source,
        IImportWriter writer,
        Encoding? connectionEncoding = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        var mapped = new List<ColumnMapping>();
        foreach (var mapping in configuration.MappedColumns()) mapped.Add(mapping);

        if (mapped.Count == 0)
        {
            throw new InvalidOperationException(
                "Nothing is mapped, so there is no import to run. ImportReadiness reports this as a blocking " +
                "item (IMP0019) and the surface must not start a run while it does.");
        }

        var plan = BuildColumnPlan(target, mapped);
        var batchSize = Math.Max(1, configuration.BatchSize);
        var stopOnFirstError = configuration.ErrorPolicy == ImportErrorPolicy.StopOnFirstError;

        var state = new RunState();
        var batchRows = new List<int>(batchSize);
        var clock = Stopwatch.StartNew();
        var lastReportAt = TimeSpan.Zero;
        long lastReportRow = 0;
        var cancelled = false;

        void Report(bool force)
        {
            if (progress is null) return;

            var elapsed = clock.Elapsed;
            if (!force
                && state.RowsRead - lastReportRow < ProgressRowInterval
                && elapsed - lastReportAt < ProgressInterval)
            {
                return;
            }

            lastReportRow = state.RowsRead;
            lastReportAt = elapsed;
            progress.Report(new ImportProgress(state.RowsRead, state.RowsWritten, state.RowsFailed, elapsed));
        }

        // Sends the queued rows and turns each result's BATCH POSITION back into the source row number the
        // user can find. Returns false when the error policy says the run must stop.
        async Task<bool> FlushAsync(CancellationToken token)
        {
            if (batchRows.Count == 0) return true;

            var results = await writer.FlushBatchAsync(token).ConfigureAwait(false);
            var keepGoing = true;

            // With StopOnFirstError the driver stops AT the offending row, so fewer results than queued rows
            // come back (I0 §2.3). The rows past that point were never attempted: they are neither written nor
            // failed, and the honest report is that Read exceeds Written + Failed.
            var count = Math.Min(results.Count, batchRows.Count);
            for (var i = 0; i < count; i++)
            {
                var result = results[i];
                if (result.IsSuccess)
                {
                    state.RowsWritten++;
                    continue;
                }

                state.RowsFailed++;
                state.AddError(new ImportRowError(
                    batchRows[i],
                    result.Kind,
                    ServerMessage: result.ServerMessage,
                    Limit: result.Limit,
                    ActualLength: result.ActualLength));

                if (stopOnFirstError) keepGoing = false;
            }

            batchRows.Clear();
            return keepGoing;
        }

        await writer.BeginAsync(target, mapped, cancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (var record in provider
                .ReadRecordsAsync(source, configuration, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                state.RowsRead++;

                var raw = ImportMappingPlanner.Project(record, mapped);
                var failure = BuildRow(record, raw, plan, configuration, connectionEncoding, state, out var values);

                if (failure is not null)
                {
                    state.RowsFailed++;
                    state.AddError(failure);
                    if (stopOnFirstError) break;
                }
                else
                {
                    await writer.WriteAsync(new ImportRow(record.SourceRowNumber, values), cancellationToken)
                        .ConfigureAwait(false);
                    batchRows.Add(record.SourceRowNumber);

                    if (batchRows.Count >= batchSize
                        && !await FlushAsync(cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }
                }

                Report(force: false);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        // ⭐ The tail runs on an UNCANCELLED token, deliberately. Rows the writer already accepted must stay
        // attributable to a source row number — abandoning the queue here would leave rows the report cannot
        // account for, which §0.6 forbids more strongly than it dislikes doing a little work after a Cancel.
        // (The same discipline as gotcha #253: let the in-flight operation finish rather than tearing it out
        // from under itself.)
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        var summary = await writer.CompleteAsync(CancellationToken.None).ConfigureAwait(false);

        Report(force: true);

        return new ImportOutcome(
            state.RowsRead,
            state.RowsWritten,
            state.RowsFailed,
            state.Errors,
            state.ErrorsTruncated,
            // Only the writer knows whether work is left pending — everything else the pipeline counted itself,
            // because those counts are the ones tied to source row numbers.
            summary.TransactionLeftOpen,
            cancelled,
            CreatedTable: null)
        {
            Warnings = state.Warnings,
            WarningsTruncated = state.WarningsTruncated,
        };
    }

    /// <summary>
    /// Converts and validates one record's values. Returns the failure that stopped it, or <c>null</c> when the
    /// whole row is good.
    /// <para>
    /// It stops at the FIRST bad value rather than collecting every fault in the row: the row is not going in
    /// either way, and one clear reason ("column X, value Y, not an integer") is more actionable than four.
    /// </para>
    /// </summary>
    private static ImportRowError? BuildRow(
        RawRecord record,
        object?[] raw,
        ColumnPlan[] plan,
        ImportConfiguration configuration,
        Encoding? connectionEncoding,
        RunState state,
        out object?[] values)
    {
        values = new object?[plan.Length];

        for (var i = 0; i < plan.Length; i++)
        {
            var column = plan[i];
            var rawText = ImportValueConverter.AsText(raw[i]);

            var converted = ImportValueConverter.Convert(raw[i], column.Type, configuration.Culture);
            if (!converted.IsSuccess)
                return new ImportRowError(record.SourceRowNumber, converted.Kind, column.Name, rawText);

            var validated = ImportRowValidator.Validate(
                converted.Value, column.Type, column.NotNull, configuration.Behavior, connectionEncoding, rawText);
            if (!validated.IsSuccess)
                return new ImportRowError(record.SourceRowNumber, validated.Kind, column.Name, rawText);

            // A shortened value is not a failure — the row goes in — but §0.2 requires every one of them to
            // reach the report, carrying the ORIGINAL text.
            if (validated.WasTrimmed)
            {
                state.AddWarning(new ImportRowError(
                    record.SourceRowNumber, ImportErrorKind.ValueTooLong, column.Name, validated.RawText));
            }

            values[i] = validated.Value;
        }

        return null;
    }

    /// <summary>
    /// Resolves each mapped column's spec and type ONCE, before the first row.
    /// <para>
    /// Not merely an optimization: doing it per row would re-parse <c>VARCHAR(20)</c> a million times, and it
    /// would also move the "this column does not exist" discovery into the middle of a run.
    /// </para>
    /// </summary>
    private static ColumnPlan[] BuildColumnPlan(ImportTarget target, List<ColumnMapping> mapped)
    {
        var plan = new ColumnPlan[mapped.Count];
        for (var i = 0; i < mapped.Count; i++)
        {
            var name = mapped[i].TargetColumnName;
            var column = target.FindColumn(name);

            if (column is null)
            {
                // Reachable only if the mapping was applied without going through ImportMappingPlanner, which
                // rebuilds it from the target's ACTUAL columns — the single path §4.8.5 requires even when a
                // profile is loaded. Failing loudly here beats letting the writer name a column that is not
                // there and reporting the server's confusion as a data error.
                throw new InvalidOperationException(
                    $"The mapping names target column '{name}', which table '{target.TableName}' does not " +
                    "have. A mapping must be produced by ImportMappingPlanner against the current target.");
            }

            plan[i] = new ColumnPlan(column.Name, ImportTargetType.Resolve(column), column.NotNull);
        }
        return plan;
    }

    /// <summary>One mapped column, resolved once.</summary>
    private readonly record struct ColumnPlan(string Name, ImportTargetType Type, bool NotNull);

    /// <summary>
    /// The run's counters and collected findings. A class rather than a pile of locals so the flush and the
    /// row builder can share them — a <c>ref bool</c> cannot be captured by a local function, and threading
    /// eight out-parameters around would obscure the algorithm it is meant to serve.
    /// </summary>
    private sealed class RunState
    {
        public long RowsRead;
        public long RowsWritten;
        public long RowsFailed;
        public bool ErrorsTruncated;
        public bool WarningsTruncated;

        public List<ImportRowError> Errors { get; } = new();
        public List<ImportRowError> Warnings { get; } = new();

        /// <summary>Keeps the list bounded while the COUNTERS stay exact — a malformed million-row file must
        /// not become a million-entry list, and the report says the list was truncated rather than implying it
        /// is complete.</summary>
        public void AddError(ImportRowError error)
        {
            if (Errors.Count < ImportOutcome.MaxCollectedErrors) Errors.Add(error);
            else ErrorsTruncated = true;
        }

        public void AddWarning(ImportRowError warning)
        {
            if (Warnings.Count < ImportOutcome.MaxCollectedErrors) Warnings.Add(warning);
            else WarningsTruncated = true;
        }
    }
}
