using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I7: the converted preview, the run, the report and "last used".
/// <para>
/// The load-bearing tests here are the ones about <b>identity of path</b>: the converted preview runs the real
/// pipeline (<see cref="ConvertedPreview_FollowsTheDeclaredCulture"/>), and "Validate" is the same run with a
/// different writer (<see cref="Validate_RunsTheSamePipeline_ButWritesNothing"/>). Those two are what make the
/// preview's promise ("this is what reaches the database") and the dry run's promise ("Validate says it is
/// fine") mean anything at all — a second conversion path would drift, and nothing would notice.
/// </para>
/// </summary>
public class DataImportRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "et-import-run-" + Guid.NewGuid().ToString("N"));

    public DataImportRunTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task SettleAsync(DataImportTabViewModel vm)
    {
        for (var i = 0; i < 10; i++)
        {
            var pending = vm.PendingRecalculation;
            if (pending is null) return;
            await pending.ConfigureAwait(false);
            if (ReferenceEquals(pending, vm.PendingRecalculation)) return;
        }
    }

    private static ImportTarget LabTarget() => new(
        "IMP_LAB",
        new[]
        {
            new ColumnSpec("ID", "INTEGER") { Identity = IdentityKind.Always },
            new ColumnSpec("KOD", "VARCHAR(20)", NotNull: true),
            new ColumnSpec("NAZWA", "VARCHAR(100)"),
            new ColumnSpec("SUMA", "NUMERIC(15,2)") { IsComputed = true },
        },
        Array.Empty<string>());

    /// <summary>
    /// A writer that records what it was given and can be told to fail one row — enough to prove the surface
    /// drives the pipeline and reads its outcome, without a database.
    /// </summary>
    private sealed class FakeWriter : IImportWriter
    {
        private readonly int _failAt;
        private int _sinceFlush;

        public FakeWriter(bool leaveTransactionOpen = true, int failAt = -1)
        {
            TransactionLeftOpen = leaveTransactionOpen;
            _failAt = failAt;
        }

        public bool TransactionLeftOpen { get; }
        public List<ImportRow> Rows { get; } = new();
        public bool Begun { get; private set; }

        public Task BeginAsync(ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken ct)
        {
            Begun = true;
            return Task.CompletedTask;
        }

        public Task WriteAsync(ImportRow row, CancellationToken ct)
        {
            Rows.Add(row);
            _sinceFlush++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken ct)
        {
            var first = Rows.Count - _sinceFlush;
            var results = new List<ImportBatchItemResult>();
            for (var i = 0; i < _sinceFlush; i++)
            {
                results.Add(first + i == _failAt
                    ? ImportBatchItemResult.Failure(ImportErrorKind.ServerUniqueViolation)
                    : ImportBatchItemResult.Success);
            }
            _sinceFlush = 0;
            return Task.FromResult<IReadOnlyList<ImportBatchItemResult>>(results);
        }

        public Task<ImportWriteSummary> CompleteAsync(CancellationToken ct)
            => Task.FromResult(new ImportWriteSummary(Rows.Count, 0, TransactionLeftOpen));
    }

    /// <summary>A surface wired for a run: a source, a target, a mapping, and whichever collaborators the test
    /// actually needs. Everything left null keeps proving that a surface with nothing behind it refuses rather
    /// than throwing.</summary>
    private async Task<(DataImportTabViewModel Vm, FakeWriter Writer)> RunnableVmAsync(
        string csv = "KOD;NAZWA\nA1;Widget\nA2;Gadget\n",
        FakeWriter? writer = null,
        Func<string, Task<bool>>? confirm = null,
        List<string>? emptied = null,
        List<string>? transactionActions = null,
        long targetRowCount = 7,
        ImportConfiguration? lastUsed = null,
        List<ImportConfiguration>? saved = null)
    {
        var target = LabTarget();
        var made = writer ?? new FakeWriter();

        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ListTablesAsync = _ => Task.FromResult<IReadOnlyList<string>>(new[] { target.TableName }),
            ReadTargetAsync = (_, _) => Task.FromResult<ImportTarget?>(target),
            CreateWriter = _ => made,
            CountTargetRowsAsync = (_, _) => Task.FromResult(targetRowCount),
            EmptyTargetAsync = (t, _) => { emptied?.Add(t); return Task.FromResult(targetRowCount); },
            CommitAsync = () => { transactionActions?.Add("commit"); return Task.CompletedTask; },
            RollbackAsync = () => { transactionActions?.Add("rollback"); return Task.CompletedTask; },
            LoadLastUsed = lastUsed is null ? null : () => lastUsed,
            SaveLastUsed = saved is null ? null : c => saved.Add(c),
        };

        var vm = new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };
        if (confirm is not null) vm.ConfirmRequested += confirm;

        await SettleAsync(vm);
        vm.Source.FilePath = WriteFile("run.csv", csv);
        await SettleAsync(vm);
        vm.Target.SelectedTable = target.TableName;
        await SettleAsync(vm);

        return (vm, made);
    }

    // ── The converted preview (§3.6) ────────────────────────────────────────────────────────────────────

    /// <summary>The grid shows the CONVERTED values, aligned to the mapped columns in mapping order.</summary>
    [Fact]
    public async Task ConvertedPreview_ShowsConvertedRows_ForTheMappedColumnsInOrder()
    {
        var (vm, _) = await RunnableVmAsync();

        Assert.Equal(new[] { "KOD", "NAZWA" }, vm.ConvertedPreview.Columns);
        Assert.Equal(2, vm.ConvertedPreview.Rows.Count);
        Assert.Equal("A1", vm.ConvertedPreview.Rows[0].ValueAt(0));
        Assert.False(vm.ConvertedPreview.HasProblems);
    }

    /// <summary>
    /// ⭐ A row that cannot be converted is marked and shows its RAW values — it has no converted ones, by
    /// construction (the pipeline stops a row at its first bad value), and the raw text is exactly what the
    /// user has to go and fix (§3.6 / §0.2).
    /// </summary>
    [Fact]
    public async Task ConvertedPreview_MarksAFailedRow_AndShowsItsRawValues()
    {
        // 25 characters into a VARCHAR(20): refused, never silently truncated (§0.2).
        var (vm, _) = await RunnableVmAsync("KOD;NAZWA\nA1;Widget\nTOO-LONG-VALUE-0123456789;Gadget\n");

        Assert.True(vm.ConvertedPreview.HasProblems);
        var problem = Assert.Single(vm.ConvertedPreview.Problems);
        Assert.Equal("KOD", problem.ColumnName);
        Assert.Equal("TOO-LONG-VALUE-0123456789", problem.RawValue);

        var failed = vm.ConvertedPreview.Rows.Single(r => r.IsFailed);
        Assert.Equal(0, failed.FailedColumnIndex);
        Assert.Equal("TOO-LONG-VALUE-0123456789", failed.ValueAt(0));

        // The Errors tab counts what it holds, rather than making the user open it to find out.
        Assert.Contains("1", vm.ConvertedPreview.ProblemsTabHeader, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ THE proof that the preview CONVERTS rather than displays: the same file, read with a different
    /// decimal separator, gives a different answer. §0.1 forbids guessing — under a '.' separator "1,5" is an
    /// error, never 15. If this ever passes with a hand-written "format for display" routine behind it, the
    /// preview has stopped predicting the import.
    /// </summary>
    [Fact]
    public async Task ConvertedPreview_FollowsTheDeclaredCulture()
    {
        var target = new ImportTarget(
            "IMP_NUM",
            new[] { new ColumnSpec("WARTOSC", "NUMERIC(15,2)") },
            Array.Empty<string>());

        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ListTablesAsync = _ => Task.FromResult<IReadOnlyList<string>>(new[] { target.TableName }),
            ReadTargetAsync = (_, _) => Task.FromResult<ImportTarget?>(target),
        };

        var vm = new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };
        await SettleAsync(vm);
        vm.Source.FilePath = WriteFile("num.csv", "WARTOSC\n1,5\n");
        await SettleAsync(vm);
        vm.Target.SelectedTable = target.TableName;
        await SettleAsync(vm);

        vm.Source.AutoDetectDelimiter = false;
        vm.Source.Delimiter = vm.Source.DelimiterOptions.Single(o => o.Value == ';');
        vm.Source.DecimalSeparator = ImportSourceSectionViewModel.DecimalSeparatorOptions.Single(o => o.Value == ',');
        await SettleAsync(vm);
        Assert.False(vm.ConvertedPreview.HasProblems);

        vm.Source.DecimalSeparator = ImportSourceSectionViewModel.DecimalSeparatorOptions.Single(o => o.Value == '.');
        await SettleAsync(vm);
        Assert.True(vm.ConvertedPreview.HasProblems);
    }

    // ── The two gates (§3.2) ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ I7.5 replaced the test that used to live here. It pinned "an open console transaction blocks the
    /// import but not the validation" — a split that only existed because the module wrote into the console's
    /// transaction. It owns its own now, so the console's state does not reach this surface at all, and both
    /// commands are available on their own merits.
    /// </summary>
    [Fact]
    public async Task TheConsolesTransaction_DoesNotReachThisSurface()
    {
        var (vm, _) = await RunnableVmAsync();

        Assert.True(vm.CanImport);
        Assert.True(vm.CanValidate);
        Assert.All(vm.Readiness.Items, i => Assert.NotEqual("IMP0021", i.Code));
    }

    /// <summary>A surface with no writer behind it refuses to run — it does not throw.</summary>
    [Fact]
    public async Task WithoutAWriter_ImportIsRefused()
    {
        var target = LabTarget();
        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ListTablesAsync = _ => Task.FromResult<IReadOnlyList<string>>(new[] { target.TableName }),
            ReadTargetAsync = (_, _) => Task.FromResult<ImportTarget?>(target),
        };

        var vm = new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };
        await SettleAsync(vm);
        vm.Source.FilePath = WriteFile("run.csv", "KOD;NAZWA\nA1;Widget\n");
        await SettleAsync(vm);
        vm.Target.SelectedTable = target.TableName;
        await SettleAsync(vm);

        Assert.False(vm.CanImport);

        await vm.ImportCommand.ExecuteAsync(null);
        Assert.False(vm.Report.HasReport);
    }

    // ── The run (§3.7) ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The import drives the real pipeline into the writer it was handed, and reports what came back.</summary>
    [Fact]
    public async Task Import_WritesTheRows_AndReportsThem()
    {
        var (vm, writer) = await RunnableVmAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(writer.Begun);
        Assert.Equal(2, writer.Rows.Count);
        Assert.True(vm.Report.HasReport);
        Assert.True(vm.Report.TransactionLeftOpen);
    }

    /// <summary>
    /// ⭐ §0.6 — the report does not lie. While the transaction is open the headline says so, instead of
    /// calling an unpersisted import a success.
    /// </summary>
    [Fact]
    public async Task Report_DoesNotClaimSuccessWhileTheTransactionIsOpen()
    {
        var (vm, _) = await RunnableVmAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains(UiStrings.ImportReportTransactionOpen, vm.Report.Headline, StringComparison.Ordinal);
        Assert.True(vm.CanFinishTransaction);
    }

    /// <summary>
    /// ⭐ "Validate" is the SAME run with a different writer — so it reaches the same rows and still leaves
    /// nothing to commit. That identity is the only thing that makes "Validate says it is fine" mean anything.
    /// </summary>
    [Fact]
    public async Task Validate_RunsTheSamePipeline_ButWritesNothing()
    {
        var (vm, writer) = await RunnableVmAsync();

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.False(writer.Begun);
        Assert.True(vm.Report.HasReport);
        Assert.False(vm.Report.TransactionLeftOpen);
        Assert.False(vm.CanFinishTransaction);
    }

    /// <summary>A run's problems reach the report naming the row the user can find in their file — never a
    /// batch index (the pipeline already translated that, and nothing downstream ever sees one).</summary>
    [Fact]
    public async Task Report_NamesTheSourceRow_ForAServerRejection()
    {
        var (vm, _) = await RunnableVmAsync(
            "KOD;NAZWA\nA1;Widget\nA2;Gadget\n", new FakeWriter(failAt: 1));

        await vm.ImportCommand.ExecuteAsync(null);

        var problem = Assert.Single(vm.Report.Problems);
        Assert.Equal(3, problem.SourceRowNumber);   // header is row 1, so the second data row is row 3
        Assert.Equal(UiStrings.ImportErrorServerUniqueViolation, problem.Reason);
    }

    // ── The transaction decision (§4.5 / §0.5) ──────────────────────────────────────────────────────────

    /// <summary>Commit on success means exactly that — nothing rejected. Otherwise the decision stays with the
    /// user, in front of the report's numbers.</summary>
    [Fact]
    public async Task AutoCommitOnSuccess_CommitsOnlyWhenEveryRowWentIn()
    {
        var clean = new List<string>();
        var (vm, _) = await RunnableVmAsync(transactionActions: clean);
        vm.TransactionMode = ImportTransactionMode.AutoCommitOnSuccess;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "commit" }, clean);

        var rejected = new List<string>();
        var (vm2, _) = await RunnableVmAsync(
            writer: new FakeWriter(failAt: 0), transactionActions: rejected);
        vm2.TransactionMode = ImportTransactionMode.AutoCommitOnSuccess;
        await SettleAsync(vm2);

        await vm2.ImportCommand.ExecuteAsync(null);
        Assert.Empty(rejected);
        Assert.True(vm2.Report.TransactionLeftOpen);
    }

    /// <summary>Manual leaves the decision to the report's own Commit/Rollback — which is the whole point of
    /// putting them there: the decision is taken where the numbers are (§3.7).</summary>
    [Fact]
    public async Task Manual_LeavesTheDecisionToTheReportsOwnButtons()
    {
        var acted = new List<string>();
        var (vm, _) = await RunnableVmAsync(transactionActions: acted);

        await vm.ImportCommand.ExecuteAsync(null);
        Assert.Empty(acted);

        await vm.CommitCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "commit" }, acted);
        Assert.False(vm.Report.TransactionLeftOpen);
    }

    /// <summary>The mode and the error policy are decisions like any other, so they live in the ONE record
    /// (§4.8.6) — and band H says which mode is in force.</summary>
    [Fact]
    public async Task TransactionModeAndErrorPolicy_ReachTheOneRecord()
    {
        var (vm, _) = await RunnableVmAsync();

        vm.TransactionMode = ImportTransactionMode.Batched;
        vm.ErrorPolicy = ImportErrorPolicy.SkipInvalidRows;
        await SettleAsync(vm);

        Assert.Equal(ImportTransactionMode.Batched, vm.CurrentConfiguration.Transaction);
        Assert.Equal(ImportErrorPolicy.SkipInvalidRows, vm.CurrentConfiguration.ErrorPolicy);
        Assert.Contains(UiStrings.ImportTransactionBatched, vm.DestinationStatus, StringComparison.Ordinal);
    }

    // ── Emptying the target (§0.5 — the I6 leftover closed here) ────────────────────────────────────────

    /// <summary>
    /// ⭐ Emptying the table destroys data, so it is confirmed with the NUMBER — read by the very transaction
    /// that is about to do the deleting. Declining runs nothing at all: a refused confirmation is a refusal,
    /// not "skip that bit and import anyway".
    /// </summary>
    [Fact]
    public async Task EmptyBeforeImport_ConfirmsWithTheRowCount_AndDecliningCancelsTheWholeRun()
    {
        var asked = new List<string>();
        var emptied = new List<string>();
        var (vm, writer) = await RunnableVmAsync(
            confirm: q => { asked.Add(q); return Task.FromResult(false); },
            emptied: emptied,
            targetRowCount: 7);

        vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("7", Assert.Single(asked), StringComparison.Ordinal);
        Assert.Empty(emptied);
        Assert.False(writer.Begun);
    }

    /// <summary>Confirming empties the table first — in the SAME transaction, so a Rollback takes it back
    /// together with the rows (decision D5).</summary>
    [Fact]
    public async Task EmptyBeforeImport_Confirmed_DeletesBeforeWriting()
    {
        var emptied = new List<string>();
        var (vm, writer) = await RunnableVmAsync(confirm: _ => Task.FromResult(true), emptied: emptied);

        vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "IMP_LAB" }, emptied);
        Assert.True(writer.Begun);
    }

    /// <summary>A validation writes nothing, so it must not delete anything either.</summary>
    [Fact]
    public async Task Validate_NeverEmptiesTheTarget()
    {
        var emptied = new List<string>();
        var (vm, _) = await RunnableVmAsync(confirm: _ => Task.FromResult(true), emptied: emptied);

        vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.Empty(emptied);
    }

    // ── "Last used" (§4.8.4) ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Recorded at the START of a run, not at the end: a run the user cancels or that fails still says what
    /// they asked for, and that is the configuration worth coming back to.
    /// </summary>
    [Fact]
    public async Task LastUsedConfiguration_IsRecordedWhenTheImportStarts()
    {
        var saved = new List<ImportConfiguration>();
        var (vm, _) = await RunnableVmAsync(saved: saved);

        await vm.ImportCommand.ExecuteAsync(null);

        var stored = Assert.Single(saved);
        Assert.Equal("IMP_LAB", stored.Target.TableName);
        Assert.NotEmpty(stored.Mapping);
    }

    /// <summary>A validation is not "what I last imported", so it must not overwrite the stored
    /// configuration.</summary>
    [Fact]
    public async Task LastUsedConfiguration_IsNotRecordedByAValidation()
    {
        var saved = new List<ImportConfiguration>();
        var (vm, _) = await RunnableVmAsync(saved: saved);

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.Empty(saved);
    }

    /// <summary>
    /// ⭐ A restored configuration goes through the SAME <c>ApplyConfiguration</c> a named profile will use in
    /// I11 — nothing else gets built for them — and the surface says out loud that it restored something,
    /// because an automatic restore the user cannot see is a configuration they did not choose.
    /// </summary>
    [Fact]
    public async Task LastUsedConfiguration_IsRestoredWhenTheTabOpens()
    {
        var last = ImportConfiguration.Empty with
        {
            Target = TargetDescriptor.Existing("IMP_LAB"),
            Transaction = ImportTransactionMode.AutoCommitOnSuccess,
            ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
        };

        var (vm, _) = await RunnableVmAsync(lastUsed: last);

        Assert.True(vm.RestoredLastConfiguration);
        Assert.Equal(ImportTransactionMode.AutoCommitOnSuccess, vm.TransactionMode);
        Assert.Equal(ImportErrorPolicy.SkipInvalidRows, vm.ErrorPolicy);
    }

    // ── The report's own text ───────────────────────────────────────────────────────────────────────────

    /// <summary>Where the engine reported numbers, the report uses them rather than parsing a message — I0
    /// measured that the truncation GDS vector carries the limit and the actual length as integers.</summary>
    [Fact]
    public void ProblemRow_UsesTheServersOwnNumbers()
    {
        var row = new ImportProblemRowViewModel(new ImportRowError(
            12, ImportErrorKind.ValueTooLong, "KOD", "EOP-375", Limit: 20, ActualLength: 26));

        Assert.Contains("26", row.Reason, StringComparison.Ordinal);
        Assert.Contains("20", row.Reason, StringComparison.Ordinal);
    }

    /// <summary>A shortened row went IN — so it is a warning carrying its original value, never a failure
    /// (§0.2). Folding it into the errors would say a written row was rejected.</summary>
    [Fact]
    public void Report_KeepsShortenedRowsApartFromRejections()
    {
        var report = new ImportRunReportViewModel();
        var outcome = new ImportOutcome(
            3, 3, 0, Array.Empty<ImportRowError>(), false, true, false, null)
        {
            Warnings = new[] { new ImportRowError(2, ImportErrorKind.ValueTooLong, "KOD", "original") },
        };

        report.Publish(outcome, validation: false, TimeSpan.FromSeconds(1));

        var warning = Assert.Single(report.Problems);
        Assert.True(warning.IsWarning);
        Assert.Equal("original", warning.RawValue);
        Assert.Contains(UiStrings.ImportReportTransactionOpen, report.Headline, StringComparison.Ordinal);
    }
}
