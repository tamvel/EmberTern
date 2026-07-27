using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I3: the one pipeline, end to end, with no database and no UI.
/// <para>
/// The headline test is <see cref="BatchFailure_IsReportedAgainstTheSourceRow_NotTheBatchIndex"/>. A batched
/// write reports failures by POSITION IN THE BATCH (decision D9), and the whole value of the error report
/// depends on that position being translated into the row number the user can find in their file. The fixtures
/// below deliberately keep the two numbers different — a header row and a batch boundary — so a pipeline that
/// simply passed the index through would fail rather than coincidentally agree.
/// </para>
/// </summary>
public class ImportPipelineTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────

    private static ColumnSpec Col(string name, string type, bool notNull = false)
        => new(name, type, null, notNull);

    private static ImportTarget Target(params ColumnSpec[] columns)
        => new("T", columns.Length == 0 ? new[] { Col("A", "INTEGER"), Col("B", "VARCHAR(10)") } : columns,
            Array.Empty<string>());

    private static ImportConfiguration Config(
        ImportErrorPolicy policy = ImportErrorPolicy.SkipInvalidRows,
        int batchSize = ImportConfiguration.DefaultBatchSize,
        ImportBehaviorOptions? behavior = null,
        params string[] columns)
    {
        var names = columns.Length == 0 ? new[] { "A", "B" } : columns;
        return new ImportConfiguration
        {
            Source = SourceDescriptor.Clipboard(),
            Target = TargetDescriptor.Existing("T"),
            ErrorPolicy = policy,
            BatchSize = batchSize,
            Behavior = behavior ?? new ImportBehaviorOptions(),
            Mapping = names.Select((n, i) => new ColumnMapping
            {
                TargetColumnName = n,
                SourceFieldName = n,
                SourceFieldIndex = i,
            }).ToArray(),
        };
    }

    private static Task<ImportOutcome> Run(
        string csv,
        IImportWriter writer,
        ImportConfiguration? configuration = null,
        ImportTarget? target = null,
        System.Text.Encoding? connectionEncoding = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ImportPipeline.RunAsync(
            configuration ?? Config(),
            target ?? Target(),
            new DelimitedTextImportProvider(),
            new TextImportSource(csv),
            writer,
            connectionEncoding,
            progress,
            cancellationToken);

    // ── The happy path ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AGoodFile_ImportsEveryRow()
    {
        var writer = new DryRunImportWriter();
        var outcome = await Run("A;B\n1;x\n2;y\n3;z\n", writer);

        Assert.Equal(3, outcome.RowsRead);
        Assert.Equal(3, outcome.RowsWritten);
        Assert.Equal(0, outcome.RowsFailed);
        Assert.Empty(outcome.Errors);
        Assert.False(outcome.Cancelled);
    }

    /// <summary>The values reach the writer already CONVERTED — an INTEGER column receives an <c>int</c>, not
    /// the text that was in the file. That is the pipeline's job, done once, before anything is written.</summary>
    [Fact]
    public async Task ValuesReachTheWriterConverted()
    {
        var writer = new ScriptedWriter();
        await Run("A;B\n1;x\n", writer);

        var row = Assert.Single(writer.Written);
        Assert.Equal(2, row.SourceRowNumber);
        Assert.Equal(1, row.Values[0]);
        Assert.Equal("x", row.Values[1]);
    }

    /// <summary>⭐ "Validate" is the real pipeline with a different writer, not a second mode — so a dry run and
    /// a real run must agree on everything the client can decide. If these ever diverge, "Validate says fine"
    /// has stopped meaning anything.</summary>
    [Fact]
    public async Task ADryRunAndARealWriter_TakeTheSamePath()
    {
        const string csv = "A;B\n1;x\nbad;y\n3;z\n";

        var dry = await Run(csv, new DryRunImportWriter());
        var real = await Run(csv, new ScriptedWriter());

        Assert.Equal(dry.RowsRead, real.RowsRead);
        Assert.Equal(dry.RowsWritten, real.RowsWritten);
        Assert.Equal(dry.RowsFailed, real.RowsFailed);
        Assert.Equal(
            dry.Errors.Select(e => (e.SourceRowNumber, e.Kind)),
            real.Errors.Select(e => (e.SourceRowNumber, e.Kind)));
    }

    /// <summary>§0.6: a dry run wrote nothing, so it must not leave the report saying there is something to
    /// commit.</summary>
    [Fact]
    public async Task ADryRun_LeavesNoTransactionOpen()
        => Assert.False((await Run("A;B\n1;x\n", new DryRunImportWriter())).TransactionLeftOpen);

    [Fact]
    public async Task TheWriterDecidesWhetherWorkIsLeftPending()
        => Assert.True((await Run("A;B\n1;x\n", new ScriptedWriter())).TransactionLeftOpen);

    // ── Client-side failures ────────────────────────────────────────────────────────────────────────────

    /// <summary>The error names the row the user can find, the column at fault, and the value AS IT APPEARED IN
    /// THE SOURCE — a post-conversion approximation would be useless for fixing the file (§0.6).</summary>
    [Fact]
    public async Task AConversionFailure_NamesTheRow_TheColumn_AndTheOriginalValue()
    {
        var outcome = await Run("A;B\n1;x\n11 88x;y\n", new DryRunImportWriter());

        Assert.Equal(2, outcome.RowsRead);
        Assert.Equal(1, outcome.RowsWritten);
        Assert.Equal(1, outcome.RowsFailed);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal(3, error.SourceRowNumber);
        Assert.Equal("A", error.ColumnName);
        Assert.Equal("11 88x", error.RawValue);
        Assert.Equal(ImportErrorKind.NotAnInteger, error.Kind);
    }

    [Fact]
    public async Task ANotNullViolation_IsCaughtBeforeTheServerEverSeesIt()
    {
        var target = Target(Col("A", "INTEGER", notNull: true), Col("B", "VARCHAR(10)"));
        var outcome = await Run("A;B\n;x\n", new DryRunImportWriter(), target: target);

        Assert.Equal(ImportErrorKind.NullNotAllowed, Assert.Single(outcome.Errors).Kind);
    }

    /// <summary>⭐ Design R1 wired through the whole pipeline: without the connection encoding this row would be
    /// written with a <c>?</c> in it and no error anywhere.</summary>
    [Fact]
    public async Task AnUnrepresentableCharacter_FailsTheRowRatherThanBecomingAQuestionMark()
    {
        var outcome = await Run(
            "A;B\n1;Ж\n", new DryRunImportWriter(), connectionEncoding: ImportCharsetGuard.Strict("WIN1250"));

        Assert.Equal(1, outcome.RowsFailed);
        Assert.Equal(
            ImportErrorKind.NotRepresentableInConnectionCharset, Assert.Single(outcome.Errors).Kind);

        // …and the very same file is fine over a UTF8 connection, because the CONNECTION charset decides.
        var utf8 = await Run(
            "A;B\n1;Ж\n", new DryRunImportWriter(), connectionEncoding: ImportCharsetGuard.Strict("UTF8"));
        Assert.Equal(1, utf8.RowsWritten);
    }

    /// <summary>One clear reason beats four: the row is not going in either way, and "column A, '11 88x', not
    /// an integer" is what the user can act on.</summary>
    [Fact]
    public async Task ARowWithSeveralBadValues_ReportsTheFirst()
    {
        var target = Target(Col("A", "INTEGER"), Col("B", "INTEGER"));
        var outcome = await Run("A;B\nbad;alsobad\n", new DryRunImportWriter(), target: target);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal("A", error.ColumnName);
    }

    // ── Error policies ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SkipInvalidRows_KeepsGoing()
    {
        var outcome = await Run("A;B\nbad;x\n2;y\n3;z\n", new DryRunImportWriter());

        Assert.Equal(3, outcome.RowsRead);
        Assert.Equal(2, outcome.RowsWritten);
        Assert.Equal(1, outcome.RowsFailed);
    }

    /// <summary>The default (decision D4). Rows after the failure are never read, so <c>RowsRead</c> honestly
    /// reports where the run stopped rather than how big the file was.</summary>
    [Fact]
    public async Task StopOnFirstError_StopsAtTheFailure()
    {
        var configuration = Config(ImportErrorPolicy.StopOnFirstError);
        var outcome = await Run("A;B\n1;x\nbad;y\n3;z\n", new DryRunImportWriter(), configuration);

        Assert.Equal(2, outcome.RowsRead);
        Assert.Equal(1, outcome.RowsFailed);
    }

    /// <summary>Rows that were already valid and queued are still flushed when the run stops — they were
    /// accepted, so abandoning them would leave rows the report could not account for.</summary>
    [Fact]
    public async Task StopOnFirstError_StillFlushesTheRowsAlreadyAccepted()
    {
        var configuration = Config(ImportErrorPolicy.StopOnFirstError, batchSize: 100);
        var outcome = await Run("A;B\n1;x\n2;y\nbad;z\n", new DryRunImportWriter(), configuration);

        Assert.Equal(2, outcome.RowsWritten);
    }

    // ── ⭐ The batch-index → source-row window (decision D9) ─────────────────────────────────────────────

    /// <summary>
    /// ⭐ THE test this etap exists for. The writer fails position 1 of the SECOND batch; with a header row and
    /// a batch size of 2 that position corresponds to source row 5. A pipeline that reported the batch index
    /// would say "row 1", and the user would go and look at the wrong line of their file.
    /// </summary>
    [Fact]
    public async Task BatchFailure_IsReportedAgainstTheSourceRow_NotTheBatchIndex()
    {
        var writer = new ScriptedWriter
        {
            FailAt = (flush, position) => flush == 1 && position == 1,
        };
        var configuration = Config(batchSize: 2);

        var outcome = await Run("A;B\n1;a\n2;b\n3;c\n4;d\n", writer, configuration);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal(5, error.SourceRowNumber);      // NOT 1, and NOT 3
        Assert.Equal(3, outcome.RowsWritten);
        Assert.Equal(1, outcome.RowsFailed);
    }

    [Fact]
    public async Task RowsAreSentInBatchesOfTheConfiguredSize()
    {
        var writer = new ScriptedWriter();
        await Run("A;B\n1;a\n2;b\n3;c\n4;d\n5;e\n", writer, Config(batchSize: 2));

        // 2 + 2 + the tail of 1.
        Assert.Equal(new[] { 2, 2, 1 }, writer.BatchSizes);
    }

    /// <summary>With StopOnFirstError the driver stops AT the offending row, so fewer results come back than
    /// rows were queued (I0 §2.3). The rows past that point were never attempted — neither written nor failed —
    /// and the honest report is that Read exceeds Written + Failed.</summary>
    [Fact]
    public async Task ATruncatedBatchResult_DoesNotInventVerdictsForRowsNeverAttempted()
    {
        var writer = new ScriptedWriter
        {
            FailAt = (flush, position) => position == 1,
            TruncateAtFailure = true,
        };
        var configuration = Config(ImportErrorPolicy.StopOnFirstError, batchSize: 4);

        var outcome = await Run("A;B\n1;a\n2;b\n3;c\n4;d\n", writer, configuration);

        Assert.Equal(4, outcome.RowsRead);
        Assert.Equal(1, outcome.RowsWritten);       // position 0
        Assert.Equal(1, outcome.RowsFailed);        // position 1
        Assert.Equal(3, Assert.Single(outcome.Errors).SourceRowNumber);
    }

    /// <summary>A server-side refusal keeps the engine's own words and numbers — I0 measured that the
    /// truncation GDS vector carries the limit and the actual length, so "26 chars, limit 20" comes from
    /// Firebird rather than from parsing its message text.</summary>
    [Fact]
    public async Task AServerRefusal_KeepsTheEnginesMessageAndNumbers()
    {
        var writer = new ScriptedWriter
        {
            FailAt = (_, position) => position == 0,
            Failure = ImportBatchItemResult.Failure(
                ImportErrorKind.ServerStringTruncation, "arithmetic exception", limit: 20, actualLength: 26),
        };

        var outcome = await Run("A;B\n1;a\n", writer);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal(ImportErrorKind.ServerStringTruncation, error.Kind);
        Assert.Equal("arithmetic exception", error.ServerMessage);
        Assert.Equal(20, error.Limit);
        Assert.Equal(26, error.ActualLength);
    }

    // ── Trimming warnings (§0.2) ────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ A shortened row is neither a failure (it went in) nor silence (data was lost). It is a
    /// warning carrying the ORIGINAL value — and it must not inflate <c>RowsFailed</c>, or the report would
    /// claim a row failed that actually succeeded.</summary>
    [Fact]
    public async Task ATrimmedValue_IsWrittenAndReportedAsAWarningWithTheOriginal()
    {
        var target = Target(Col("A", "INTEGER"), Col("B", "VARCHAR(3)"));
        var configuration = Config(behavior: new ImportBehaviorOptions { TrimTooLongValues = true });

        var outcome = await Run("A;B\n1;abcdefg\n", new DryRunImportWriter(), configuration, target);

        Assert.Equal(1, outcome.RowsWritten);
        Assert.Equal(0, outcome.RowsFailed);
        Assert.Empty(outcome.Errors);

        var warning = Assert.Single(outcome.Warnings);
        Assert.Equal(2, warning.SourceRowNumber);
        Assert.Equal("B", warning.ColumnName);
        Assert.Equal("abcdefg", warning.RawValue);
    }

    /// <summary>Trimming is off by default, so the same file is a row error instead — the loss never happens
    /// unless it was asked for.</summary>
    [Fact]
    public async Task WithoutTrimming_TheSameValueIsARowError()
    {
        var target = Target(Col("A", "INTEGER"), Col("B", "VARCHAR(3)"));
        var outcome = await Run("A;B\n1;abcdefg\n", new DryRunImportWriter(), target: target);

        Assert.Equal(1, outcome.RowsFailed);
        Assert.Equal(ImportErrorKind.ValueTooLong, Assert.Single(outcome.Errors).Kind);
        Assert.Empty(outcome.Warnings);
    }

    // ── Caps ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A malformed million-row file must not become a million-entry list — but the COUNTERS stay
    /// exact, and the report says the list was truncated rather than implying it is complete.</summary>
    [Fact]
    public async Task TheErrorListIsCapped_ButTheCountersAreNot()
    {
        var rows = string.Join("\n", Enumerable.Range(1, ImportOutcome.MaxCollectedErrors + 50).Select(_ => "bad;x"));
        var outcome = await Run("A;B\n" + rows + "\n", new DryRunImportWriter());

        Assert.Equal(ImportOutcome.MaxCollectedErrors + 50, outcome.RowsFailed);
        Assert.Equal(ImportOutcome.MaxCollectedErrors, outcome.Errors.Count);
        Assert.True(outcome.ErrorsTruncated);
    }

    // ── Progress and cancellation ───────────────────────────────────────────────────────────────────────

    /// <summary>Throttled, but a final report is always forced — otherwise a short import would leave the
    /// counters showing whatever the last throttled tick happened to catch.</summary>
    [Fact]
    public async Task ProgressAlwaysEndsOnTheRealTotals()
    {
        var reports = new List<ImportProgress>();
        var progress = new SynchronousProgress(reports.Add);

        await Run("A;B\n1;a\n2;b\n", new DryRunImportWriter(), progress: progress);

        var last = Assert.Single(reports);           // 2 rows is under the throttle, so only the forced one
        Assert.Equal(2, last.RowsRead);
        Assert.Equal(2, last.RowsWritten);
    }

    [Fact]
    public async Task ProgressIsThrottled_NotPerRow()
    {
        var reports = new List<ImportProgress>();
        var progress = new SynchronousProgress(reports.Add);
        var rows = string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"{i};x"));

        await Run("A;B\n" + rows + "\n", new DryRunImportWriter(), progress: progress);

        Assert.InRange(reports.Count, 2, 50);        // nowhere near 1000
        Assert.Equal(1000, reports[^1].RowsRead);
    }

    /// <summary>
    /// ⭐ Cancelling stops reading, but does NOT abandon rows the writer already accepted: the tail flush runs
    /// on an uncancelled token so every accepted row stays attributable to a source row (§0.6). The report then
    /// says exactly what happened — "cancelled after N rows", with the transaction still open.
    /// </summary>
    [Fact]
    public async Task Cancelling_StopsReadingButStillAccountsForEveryAcceptedRow()
    {
        using var cts = new CancellationTokenSource();
        var writer = new ScriptedWriter { CancelAfterWrites = (2, cts) };

        var outcome = await Run("A;B\n1;a\n2;b\n3;c\n4;d\n", writer, Config(batchSize: 100), cancellationToken: cts.Token);

        Assert.True(outcome.Cancelled);
        Assert.Equal(2, outcome.RowsRead);
        Assert.Equal(2, outcome.RowsWritten);        // both flushed, both counted
        Assert.True(outcome.TransactionLeftOpen);
    }

    // ── Contract violations ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Readiness reports this as blocking (IMP0019); reaching the pipeline with it means the surface
    /// started a run it should not have.</summary>
    [Fact]
    public async Task RunningWithNothingMapped_IsRefused()
    {
        var configuration = Config() with { Mapping = Array.Empty<ColumnMapping>() };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Run("A;B\n1;x\n", new DryRunImportWriter(), configuration));
    }

    /// <summary>Failing loudly beats letting the writer name a column that is not there and then reporting the
    /// server's confusion as if it were a data error. Reachable only by applying a mapping without going
    /// through <c>ImportMappingPlanner</c>, which §4.8.5 requires even when loading a profile.</summary>
    [Fact]
    public async Task AMappingNamingAColumnTheTargetDoesNotHave_IsRefused()
    {
        var configuration = Config(columns: new[] { "A", "GONE" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Run("A;B\n1;x\n", new DryRunImportWriter(), configuration));

        Assert.Contains("GONE", error.Message, StringComparison.Ordinal);
    }

    // ── Test doubles ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A writer whose per-row verdicts can be scripted BY BATCH POSITION — which is the only way to prove the
    /// pipeline translates those positions into source row numbers rather than passing them through.
    /// <para>
    /// It is a test double, unlike <see cref="DryRunImportWriter"/>, and only because no production writer can
    /// fail a row without a server.
    /// </para>
    /// </summary>
    private sealed class ScriptedWriter : IImportWriter
    {
        private readonly List<ImportRow> _batch = new();
        private int _flush;
        private int _writes;

        /// <summary>(flush number, position in batch) → should this row fail?</summary>
        public Func<int, int, bool> FailAt { get; init; } = (_, _) => false;

        /// <summary>What a failure looks like.</summary>
        public ImportBatchItemResult Failure { get; init; } =
            ImportBatchItemResult.Failure(ImportErrorKind.ServerError, "refused");

        /// <summary>Emulates the driver's <c>MultiError=false</c>: the batch stops AT the offending row, so
        /// fewer results come back than rows were queued.</summary>
        public bool TruncateAtFailure { get; init; }

        /// <summary>Cancels the token after N accepted rows, to drive the cancellation test.</summary>
        public (int After, CancellationTokenSource Source)? CancelAfterWrites { get; init; }

        public List<int> BatchSizes { get; } = new();
        public List<ImportRow> Written { get; } = new();

        public Task BeginAsync(
            ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteAsync(ImportRow row, CancellationToken cancellationToken)
        {
            _batch.Add(row);
            _writes++;

            if (CancelAfterWrites is { } cancel && _writes >= cancel.After) cancel.Source.Cancel();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken cancellationToken)
        {
            var results = new List<ImportBatchItemResult>(_batch.Count);
            BatchSizes.Add(_batch.Count);

            for (var i = 0; i < _batch.Count; i++)
            {
                if (FailAt(_flush, i))
                {
                    results.Add(Failure);
                    if (TruncateAtFailure) break;
                    continue;
                }

                results.Add(ImportBatchItemResult.Success);
                Written.Add(_batch[i]);
            }

            _flush++;
            _batch.Clear();
            return Task.FromResult<IReadOnlyList<ImportBatchItemResult>>(results);
        }

        public Task<ImportWriteSummary> CompleteAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ImportWriteSummary(Written.Count, 0, TransactionLeftOpen: true));
    }

    /// <summary>Reports on the calling thread, so a test can assert what was reported without racing the
    /// default <see cref="Progress{T}"/>, which posts to a synchronization context.</summary>
    private sealed class SynchronousProgress : IProgress<ImportProgress>
    {
        private readonly Action<ImportProgress> _onReport;

        public SynchronousProgress(Action<ImportProgress> onReport) => _onReport = onReport;

        public void Report(ImportProgress value) => _onReport(value);
    }
}
