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
/// Data Import — etap I8: importing into a table that does not exist yet.
/// <para>
/// Three properties carry the whole etap, and each has a test named after it:
/// <list type="number">
/// <item><b>The types are inferred, shown, and EDITABLE before anything is created</b> — because after the
/// <c>CREATE</c> the table is committed and beyond a Rollback (§0.5 / gotcha #213), so the grid is the last
/// moment a wrong type costs nothing.</item>
/// <item><b>"Validate" creates nothing</b> — it runs against the projection, which is what makes it worth
/// running at the one moment the decision is still reversible.</item>
/// <item><b>The <c>CREATE</c> happens BEFORE the first row and on the Ddl lane</b>, and the report says the
/// table was created whether or not the import then worked.</item>
/// </list>
/// </para>
/// </summary>
public class DataImportNewTableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "et-import-new-" + Guid.NewGuid().ToString("N"));

    public DataImportNewTableTests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Records what reached the database and in which order — the only way to prove the CREATE came
    /// before the first row rather than merely alongside it.</summary>
    private sealed class Ledger
    {
        public List<string> Ddl { get; } = new();
        public List<string> Steps { get; } = new();
    }

    private sealed class FakeWriter : IImportWriter
    {
        private readonly Ledger _ledger;
        private readonly int _failAt;
        private int _sinceFlush;

        private readonly Exception? _throwOnBegin;

        public FakeWriter(Ledger ledger, int failAt = -1, Exception? throwOnBegin = null)
        {
            _ledger = ledger;
            _failAt = failAt;
            _throwOnBegin = throwOnBegin;
        }

        public List<ImportRow> Rows { get; } = new();
        public ImportTarget? Target { get; private set; }

        public Task BeginAsync(ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken ct)
        {
            if (_throwOnBegin is not null) throw _throwOnBegin;

            Target = target;
            _ledger.Steps.Add("begin");
            return Task.CompletedTask;
        }

        public Task WriteAsync(ImportRow row, CancellationToken ct)
        {
            Rows.Add(row);
            _sinceFlush++;
            _ledger.Steps.Add("row");
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
            => Task.FromResult(new ImportWriteSummary(Rows.Count - Math.Max(0, _failAt >= 0 ? 1 : 0), _failAt >= 0 ? 1 : 0, true));
    }

    /// <summary>
    /// A surface aimed at a NEW table: a source, the new-table variant, a name, and whatever the test needs.
    /// The catalog holds one unrelated table, so "the name is free" is a fact rather than an absence of data.
    /// </summary>
    /// <summary>
    /// An exception type nobody would ever put on an allow-list of "expected" failures — which is the point.
    /// The real crash came through <c>FbException</c>, a Firebird type a ViewModel may not even name (rule #1);
    /// this stands in for it and for every other type the delegates might one day throw.
    /// </summary>
    private sealed class ProbeFailure : Exception
    {
        public ProbeFailure(string where) : base($"boom in {where}") { }
    }

    private async Task<(DataImportTabViewModel Vm, FakeWriter Writer, Ledger Ledger)> NewTableVmAsync(
        string csv = "KOD;ILOSC\nA1;5\nA2;12\n",
        string name = "IMP_NEW",
        int failAt = -1,
        bool ddlFails = false,
        Func<ConfirmRequest, Task<bool>>? confirm = null,
        List<string>? transactionActions = null,
        ImportTarget? existing = null,
        List<string>? counted = null,
        string? throwFrom = null)
    {
        var ledger = new Ledger();
        var writer = new FakeWriter(ledger, failAt, throwFrom == "write" ? new ProbeFailure("write") : null);
        var created = new List<ImportTarget>();
        if (existing is not null) created.Add(existing);

        void FailIf(string stage)
        {
            if (throwFrom == stage) throw new ProbeFailure(stage);
        }

        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ListTablesAsync = _ =>
            {
                FailIf("tables");
                return Task.FromResult<IReadOnlyList<string>>(
                    (existing is null ? new[] { "SOMETHING_ELSE" } : new[] { "SOMETHING_ELSE", existing.TableName }).ToList());
            },

            // The catalog answers only for tables that exist. A new one appears here once its CREATE has run —
            // which is what lets a test prove the writer works against the CATALOG, not the projection.
            ReadTargetAsync = (table, _) =>
            {
                FailIf("read");
                return Task.FromResult(
                    created.FirstOrDefault(t => string.Equals(t.TableName, table, StringComparison.OrdinalIgnoreCase)));
            },

            CreateTableAsync = (sql, _) =>
            {
                FailIf("create");
                if (ddlFails) throw new InvalidOperationException("Token unknown - line 1");

                ledger.Ddl.Add(sql);
                ledger.Steps.Add("create");

                // What Firebird would report back afterwards — deliberately NOT the projection, so a test can
                // tell which of the two the writer was handed.
                // ⚠ Deliberately DIFFERENT from what the projection would say (the file's longest KOD is 2
                // characters, so the projection proposes VARCHAR(2)). Without that difference the assertion
                // below would pass whichever of the two the writer was handed, and prove nothing.
                created.Add(new ImportTarget(
                    name,
                    new[] { new ColumnSpec("KOD", "VARCHAR(999)"), new ColumnSpec("ILOSC", "INTEGER") },
                    Array.Empty<string>()));
                return Task.CompletedTask;
            },
            DropTableAsync = (sql, _) =>
            {
                FailIf("drop");
                ledger.Ddl.Add(sql);
                ledger.Steps.Add("drop");
                return Task.CompletedTask;
            },

            CreateWriter = _ => writer,

            // The count behind the "empty the table first" confirmation — the very call that crashed I8.
            CountTargetRowsAsync = (table, _) =>
            {
                counted?.Add(table);
                FailIf("count");
                return Task.FromResult(3L);
            },
            EmptyTargetAsync = (table, _) => { ledger.Steps.Add("empty"); return Task.FromResult(3L); },

            CommitAsync = () => { FailIf("commit"); transactionActions?.Add("commit"); return Task.CompletedTask; },
            RollbackAsync = () => { FailIf("rollback"); transactionActions?.Add("rollback"); return Task.CompletedTask; },
        };

        var vm = new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };
        if (confirm is not null) vm.ConfirmRequested += confirm;

        await SettleAsync(vm);
        vm.Source.FilePath = WriteFile("new.csv", csv);
        await SettleAsync(vm);
        vm.Target.IsNewTable = true;
        vm.Target.NewTableName = name;
        await SettleAsync(vm);

        return (vm, writer, ledger);
    }

    // ── 1. Inference reaches the grid, and the grid reaches the record ──────────────────────────────────

    /// <summary>
    /// ⭐ The types are proposed from the source and land in the ONE record — so a new table's design is part
    /// of a saved profile from day one, which is what §4.8 promises and what etap I11 will rely on.
    /// </summary>
    [Fact]
    public async Task TheColumnsAreInferredFromTheSource_AndReachTheRecord()
    {
        var (vm, _, _) = await NewTableVmAsync();

        Assert.Equal(2, vm.Target.NewColumns.Count);
        Assert.Equal("KOD", vm.Target.NewColumns[0].Name);
        Assert.Equal("VARCHAR", vm.Target.NewColumns[0].Type);
        Assert.Equal("ILOSC", vm.Target.NewColumns[1].Name);
        Assert.Equal("INTEGER", vm.Target.NewColumns[1].Type);

        var configuration = vm.BuildConfiguration();
        Assert.Equal(ImportTargetKind.NewTable, configuration.Target.Kind);
        Assert.Equal("IMP_NEW", configuration.Target.TableName);
        Assert.Equal(new[] { "KOD", "ILOSC" }, configuration.Target.NewTableColumns.Select(c => c.Name));

        // ⭐ Always visible: the number of rows the types rest on (§3.4 / REK-7).
        Assert.Contains("2", vm.Target.InferenceBasisText, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ §0.3, end to end through the surface: a mixed column becomes VARCHAR rather than an INTEGER that
    /// would fail the import on the offending row — <em>after</em> the table had been created and committed.
    /// That is R19, and it is the reason the scan covers the whole file.
    /// </summary>
    [Fact]
    public async Task AMixedColumn_BecomesTextWithItsReason_NotAnIntegerTimebomb()
    {
        var (vm, _, _) = await NewTableVmAsync(csv: "KOD;ILOSC\nA1;5\nA2;12\nA3;nie wiem\n");

        var column = vm.Target.NewColumns[1];
        Assert.Equal("VARCHAR", column.Type);

        // The basis names the value AND the row, so the user can open their own file at that line (§0.6).
        Assert.Contains("nie wiem", column.Basis, StringComparison.Ordinal);
        Assert.Contains("4", column.Basis, StringComparison.Ordinal);
    }

    /// <summary>An edit to a type is a decision: it reaches the record and the DDL, exactly like choosing a
    /// different table would. This is what "shown AND editable" (§0.3) has to mean.</summary>
    [Fact]
    public async Task EditingAType_ReachesTheRecordAndTheDdl()
    {
        var (vm, _, _) = await NewTableVmAsync();

        vm.Target.NewColumns[0].Size = 120;
        vm.Target.NewColumns[1].Type = "BIGINT";
        await SettleAsync(vm);

        var columns = vm.BuildConfiguration().Target.NewTableColumns;
        Assert.Equal(120, columns[0].Size);
        Assert.Equal("BIGINT", columns[1].BasicType);

        Assert.Contains("\"KOD\" VARCHAR(120)", vm.Target.CreateTableSql, StringComparison.Ordinal);
        Assert.Contains("\"ILOSC\" BIGINT", vm.Target.CreateTableSql, StringComparison.Ordinal);
    }

    /// <summary>⚠ A restored configuration's columns are the user's own decisions and are adopted as they are —
    /// overwriting them with a fresh proposal the moment the tab opened would be the "an older build quietly
    /// robbed the profile" defect §4.8.6 exists to prevent, wearing a new hat.</summary>
    [Fact]
    public async Task ARestoredConfigurationsColumns_AreNotOverwrittenByAFreshInference()
    {
        var (vm, _, _) = await NewTableVmAsync();

        var restored = ImportConfiguration.Empty with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Csv, WriteFile("new.csv", "KOD;ILOSC\nA1;5\n")),
            Target = TargetDescriptor.New("IMP_NEW", new[]
            {
                new ImportColumnDefinition { Name = "KOD", BasicType = "VARCHAR", Size = 200 },
                new ImportColumnDefinition { Name = "ILOSC", BasicType = "BIGINT" },
            }),
        };

        vm.ApplyConfiguration(restored);
        await SettleAsync(vm);

        Assert.Equal(200, vm.Target.NewColumns[0].Size);
        Assert.Equal("BIGINT", vm.Target.NewColumns[1].Type);
    }

    /// <summary>
    /// ⭐ A basis that is the same for every column is a fact about the GRID, so it is said once — on the
    /// section's line — instead of once per row.
    /// <para>
    /// The case is the ordinary one: every restored column reports „from the restored configuration", and a
    /// column as wide as the column-name column then repeated one identical sentence for as many rows as the
    /// table has. That is the defect I11 fixed for <c>IMP0018</c> (one fact stated twice trains the user to read
    /// neither), multiplied by the row count.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ABasisSharedByEveryColumn_IsSaidOnceForTheSection_NotOncePerRow()
    {
        var (vm, _, _) = await NewTableVmAsync();

        vm.ApplyConfiguration(ImportConfiguration.Empty with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Csv, WriteFile("new.csv", "KOD;ILOSC\nA1;5\n")),
            Target = TargetDescriptor.New("IMP_NEW", new[]
            {
                new ImportColumnDefinition { Name = "KOD", BasicType = "VARCHAR", Size = 200 },
                new ImportColumnDefinition { Name = "ILOSC", BasicType = "BIGINT" },
            }),
        });

        Assert.False(vm.Target.HasPerColumnBasis);
        Assert.All(vm.Target.NewColumns, row => Assert.Equal(string.Empty, row.Basis));
        Assert.Equal(UiStrings.ImportNewTableBasisRestored, vm.Target.InferenceBasisText);
    }

    /// <summary>The other half: where the evidence really is per column (R19's mixed columns — the measured
    /// norm) it stays beside the type it explains, and the grid's „Basis" column stays.</summary>
    [Fact]
    public async Task AnEvidenceThatDiffersPerColumn_StaysInTheGrid()
    {
        var (vm, _, _) = await NewTableVmAsync(csv: "KOD;ILOSC\nA1;5\nA2;12\nA3;nie wiem\n");

        Assert.True(vm.Target.HasPerColumnBasis);
        Assert.All(vm.Target.NewColumns, row => Assert.NotEqual(string.Empty, row.Basis));

        // And the section line keeps saying what it always said — how many rows the proposal rests on.
        Assert.Contains("3", vm.Target.InferenceBasisText, StringComparison.Ordinal);
    }

    // ── 2. Readiness, and the dry run that creates nothing ──────────────────────────────────────────────

    /// <summary>
    /// ⭐ The <c>CREATE</c> is the first thing a run does, so a name that is already taken has to be refused
    /// BEFORE the run — otherwise the user meets a raw server error immediately after being told everything
    /// was ready (§0).
    /// </summary>
    [Fact]
    public async Task ATakenName_BlocksBeforeTheRunRatherThanFailingDuringIt()
    {
        var existing = new ImportTarget("IMP_NEW", new[] { new ColumnSpec("A", "VARCHAR(10)") }, Array.Empty<string>());
        var (vm, _, ledger) = await NewTableVmAsync(existing: existing);

        Assert.Contains(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.NewTableAlreadyExists);
        Assert.False(vm.Readiness.CanRun);
        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.Empty(ledger.Ddl);
    }

    /// <summary>
    /// ⭐ A table this import CREATED is in the „Existing table" list immediately — the surface caches that list
    /// (it is a fact about the database, read once per tab), and an import that creates a table changes that
    /// fact from the inside.
    /// <para>
    /// Reported by the user as a picker that stayed empty until the tab was reopened. The picker was the visible
    /// half; the test below pins the graver one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ATableThisImportCreated_AppearsInTheExistingTableListAtOnce()
    {
        var (vm, _, ledger) = await NewTableVmAsync();

        Assert.DoesNotContain("IMP_NEW", vm.Target.Tables);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.NotEmpty(ledger.Ddl);
        Assert.Contains("IMP_NEW", vm.Target.Tables);

        // At its sorted position, so a name that arrived this way is indistinguishable from one the catalog
        // returned — appended at the end would be the tell that it came in by a different door.
        Assert.Equal(vm.Target.Tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase), vm.Target.Tables);
    }

    /// <summary>
    /// ⭐⭐ The graver half of the same staleness: with the list not knowing about the table it had just
    /// created, <c>IMP0028</c> could no longer see that the name was taken — so re-running the very same import
    /// showed a GREEN readiness strip and then failed with a raw server error, which is the exact state
    /// IMP0028 exists to prevent.
    /// </summary>
    [Fact]
    public async Task ReRunningTheSameNewTableImport_IsRefusedByIMP0028_NotByTheServer()
    {
        var (vm, _, _) = await NewTableVmAsync();

        await vm.ImportCommand.ExecuteAsync(null);
        await SettleAsync(vm);

        // Re-run the chain the way any decision would, without a Refresh — a Refresh would re-read the fake
        // catalog, which does not model the creation, and would prove nothing about the in-place update.
        vm.ApplyConfiguration(vm.BuildConfiguration());
        await SettleAsync(vm);

        Assert.Contains(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.NewTableAlreadyExists);
        Assert.False(vm.Readiness.CanRun);
    }

    /// <summary>⭐ The module's most important honest warning reaches the strip: the table will be committed,
    /// and a Rollback will not remove it (§0.5). A warning, never a block — it is a consequence to know, not a
    /// mistake to fix.</summary>
    [Fact]
    public async Task TheStripSaysARollbackWillNotRemoveTheTable()
    {
        var (vm, _, _) = await NewTableVmAsync();

        var item = vm.Readiness.Items.Single(i => i.Item.Code == ImportDiagnosticCode.NewTableWillBeCommitted);
        Assert.False(item.Item.IsBlocking);
        Assert.True(vm.Readiness.CanRun);
    }

    /// <summary>
    /// ⭐⭐ The point of the projection. "Validate" answers "will these inferred types hold my file?" at the one
    /// moment the answer is still free — <b>before</b> anything is created. A dry run that created the table
    /// first would make the question worthless, because the cost it exists to avoid would already be paid.
    /// </summary>
    [Fact]
    public async Task Validate_AnswersTheQuestion_WithoutCreatingAnything()
    {
        var (vm, writer, ledger) = await NewTableVmAsync();

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.Empty(ledger.Ddl);
        Assert.Empty(writer.Rows);
        Assert.Contains("2", vm.Report.Headline, StringComparison.Ordinal);
    }

    /// <summary>And it really validates against the proposed types: a value that the inferred column could not
    /// hold is reported, before the table exists to reject it.</summary>
    [Fact]
    public async Task Validate_MeasuresTheFileAgainstTheProposedTypes()
    {
        var (vm, _, ledger) = await NewTableVmAsync();

        // Narrow the column by hand to something the file does not fit — the correction the grid exists for,
        // used in reverse to prove the dry run is measuring against the grid.
        vm.Target.NewColumns[0].Size = 1;
        await SettleAsync(vm);

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.Empty(ledger.Ddl);
        Assert.True(vm.Report.HasProblems);
    }

    // ── 3. The run: CREATE first, on the Ddl lane, and the report says so ───────────────────────────────

    /// <summary>
    /// ⭐⭐ gotcha #213 as an ordering assertion. A Firebird transaction cannot use an object whose DDL it has
    /// not committed, so the <c>CREATE</c> must be the FIRST thing that happens — not merely something that
    /// also happens. Every sentence the surface says about Rollback follows from this order.
    /// </summary>
    [Fact]
    public async Task TheTableIsCreatedBeforeTheFirstRow()
    {
        var (vm, writer, ledger) = await NewTableVmAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("create", ledger.Steps[0]);
        Assert.Equal("begin", ledger.Steps[1]);
        Assert.Equal(2, writer.Rows.Count);

        var sql = Assert.Single(ledger.Ddl);
        Assert.Contains("CREATE TABLE \"IMP_NEW\"", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ After the CREATE the writer works against what Firebird actually BUILT, not against what we asked
    /// for. The projection is a prediction; the catalog is the fact, and a charset, a domain or a rounded
    /// precision could make them differ.
    /// </summary>
    [Fact]
    public async Task TheWriterIsGivenTheCatalogsTable_NotTheProjection()
    {
        var (vm, writer, _) = await NewTableVmAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        // The projection would say VARCHAR(2) — the longest KOD in the file. The fake catalog says
        // VARCHAR(999), so this assertion can only pass if the writer was handed the CATALOG's answer.
        Assert.Equal("VARCHAR(2)", ImportNewTable.TypeText(vm.BuildConfiguration().Target.NewTableColumns[0]));

        Assert.NotNull(writer.Target);
        Assert.Equal("IMP_NEW", writer.Target!.TableName);
        Assert.Equal("VARCHAR(999)", writer.Target.Columns[0].Type);
    }

    /// <summary>⭐ §0.5 / §0.6: the report never leaves the created table unsaid, and it says the one thing a
    /// Rollback cannot do.</summary>
    [Fact]
    public async Task TheReportNamesTheCreatedTable_AndWhatRollbackCannotUndo()
    {
        var (vm, _, _) = await NewTableVmAsync();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("IMP_NEW", vm.Report.Note, StringComparison.Ordinal);
        Assert.Contains("rollback", vm.Report.Note, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A CREATE that fails costs the user nothing, because nothing has been written yet — which is
    /// exactly why it goes first. The run stops, with the server's own message.</summary>
    [Fact]
    public async Task AFailedCreate_StopsTheRunBeforeAnyRowIsWritten()
    {
        var (vm, writer, ledger) = await NewTableVmAsync(ddlFails: true);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Empty(ledger.Steps);
        Assert.Empty(writer.Rows);
        Assert.Contains("Token unknown", vm.StatusMessage, StringComparison.Ordinal);
    }

    // ── The clean-up offered when it did not work (§0.5) ────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>Two effects, one question.</b> The rows have to be gone before the table can be, so the offer
    /// rolls the import's own transaction back and only then drops — and the confirmation says both, rather
    /// than mentioning the drop and performing the rollback quietly.
    /// </summary>
    [Fact]
    public async Task OnFailure_TheOfferedCleanupRollsBackFirst_ThenDrops()
    {
        var asked = new List<string>();
        var actions = new List<string>();

        var (vm, _, ledger) = await NewTableVmAsync(
            failAt: 0,
            confirm: r => { asked.Add(r.Message); return Task.FromResult(true); },
            transactionActions: actions);

        vm.Target.DropTableOnFailure = true;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "rollback" }, actions);
        Assert.Contains("drop", ledger.Steps);
        Assert.True(ledger.Steps.IndexOf("drop") > ledger.Steps.IndexOf("create"));
        Assert.Contains(ledger.Ddl, sql => sql.Contains("DROP TABLE \"IMP_NEW\"", StringComparison.Ordinal));

        // The question describes both effects — a dialog that under-describes what it is about to do is how
        // uncommitted work disappears.
        var question = Assert.Single(asked);
        Assert.Contains("roll back", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP", question, StringComparison.Ordinal);
    }

    /// <summary>The checkbox arms the offer; the confirmation still decides. Declining leaves the table — and
    /// the report has already said it is there.</summary>
    [Fact]
    public async Task DecliningTheCleanup_LeavesTheTable()
    {
        var (vm, _, ledger) = await NewTableVmAsync(failAt: 0, confirm: _ => Task.FromResult(false));

        vm.Target.DropTableOnFailure = true;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.DoesNotContain("drop", ledger.Steps);
        Assert.Contains("IMP_NEW", vm.Report.Note, StringComparison.Ordinal);
    }

    /// <summary>A successful import never offers to undo itself, whatever the checkbox says.</summary>
    [Fact]
    public async Task ASuccessfulImport_NeverDropsTheTable()
    {
        var (vm, _, ledger) = await NewTableVmAsync(confirm: _ => Task.FromResult(true));

        vm.Target.DropTableOnFailure = true;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.DoesNotContain("drop", ledger.Steps);
    }

    /// <summary>Off by default — it destroys an object, and every option in this module that destroys
    /// something defaults to the conservative answer (§0).</summary>
    [Fact]
    public async Task TheCleanupIsOffByDefault()
    {
        var (vm, _, ledger) = await NewTableVmAsync(failAt: 0, confirm: _ => Task.FromResult(true));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.False(vm.BuildConfiguration().Behavior.DropTableOnFailure);
        Assert.DoesNotContain("drop", ledger.Steps);
    }

    // ══ THE I8 CRASH — the two defects behind it, each pinned separately ════════════════════════════════
    //
    // Reported after the I8 review: create a new table, Validate passes, press Import — and the whole
    // application closes. The log named it exactly: SELECT COUNT(*) against a table that did not exist yet,
    // FbException -204, escaping the command and taking the process down.

    /// <summary>
    /// ⭐⭐ <b>Defect 1 — hiding a control does not retract the decision it carries.</b>
    /// <para>
    /// "Empty the table before importing" is meaningless for a table the import is about to CREATE, and the
    /// surface hides its checkbox in that variant. But the VALUE stayed in the record: a user who ticked the
    /// box on the existing-table variant and then switched to "new table" left <c>true</c> sitting there,
    /// invisible — and the run then read a row count from a table that did not exist.
    /// </para>
    /// <para>
    /// The tick itself is deliberately NOT cleared, so switching back finds it where it was left. What the
    /// record must not carry is a decision that does not apply.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ANewTable_NeverCarriesEmptyTheTableFirst_EvenIfItWasTickedEarlier()
    {
        var (vm, _, _) = await NewTableVmAsync();

        // Exactly the user's path: tick it on the existing-table variant, then switch.
        vm.Target.IsExistingTable = true;
        vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);
        Assert.True(vm.BuildConfiguration().Behavior.EmptyTargetBeforeImport);

        vm.Target.IsNewTable = true;
        await SettleAsync(vm);

        Assert.False(vm.BuildConfiguration().Behavior.EmptyTargetBeforeImport);

        // ...and the tick survives for the variant it belongs to.
        vm.Target.IsExistingTable = true;
        await SettleAsync(vm);
        Assert.True(vm.Target.EmptyBeforeImport);
    }

    /// <summary>The consequence, end to end: the run never asks a table that does not exist how many rows it
    /// has. This is the exact call that produced the crash.</summary>
    [Fact]
    public async Task ANewTableRun_NeverCountsRowsInATableThatDoesNotExistYet()
    {
        var counted = new List<string>();
        var (vm, _, _) = await NewTableVmAsync(counted: counted);

        vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Empty(counted);
    }

    /// <summary>
    /// ⭐⭐ <b>Defect 2 — an import must never be able to close the application.</b>
    /// <para>
    /// <c>AsyncRelayCommand</c> rethrows a faulted command's exception on the dispatcher, where nothing is left
    /// to catch it, so an unhandled exception here does not produce a bad report — it ends the process. The
    /// catch clauses were allow-lists of exception TYPES, and this VM reaches the world only through delegates,
    /// which means the types are not knowable here by construction. They duly missed the two most likely
    /// failures a database module has.
    /// </para>
    /// <para>
    /// The test throws a type nobody would ever put on such a list, from every collaborator in turn. It fails
    /// the moment anyone narrows one of those catches again.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("create")]
    [InlineData("read")]
    [InlineData("count")]
    [InlineData("write")]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("drop")]
    [InlineData("tables")]
    public async Task NoCollaboratorCanTakeTheApplicationDown(string failing)
    {
        var (vm, _, _) = await NewTableVmAsync(throwFrom: failing, confirm: _ => Task.FromResult(true));

        vm.Target.DropTableOnFailure = true;
        if (failing == "count") vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);

        // The assertion is the absence of an escape: an unhandled exception here fails the test exactly as it
        // would have closed the application.
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.ValidateCommand.ExecuteAsync(null);
        await vm.CommitCommand.ExecuteAsync(null);
        await vm.RollbackCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
    }

    /// <summary>And the failure is not merely survived — it is REPORTED. A run that dies quietly is only
    /// marginally better than one that closes the window (§9.1: a refusal always carries its reason).</summary>
    [Fact]
    public async Task AFailingCollaborator_LeavesAMessageTheUserCanRead()
    {
        var (vm, _, _) = await NewTableVmAsync(throwFrom: "create");

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.HasStatusMessage);
        Assert.Equal(EmberTern.App.Controls.MessageSeverity.Error, vm.StatusSeverity);
        Assert.Contains("boom", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Switching between the two variants ──────────────────────────────────────────────────────────────

    /// <summary>The two variants are a choice, not a mode: switching back to an existing table produces an
    /// existing-table target, and nothing of the other variant leaks into it.</summary>
    [Fact]
    public async Task SwitchingBackToAnExistingTable_ProducesAnExistingTarget()
    {
        var (vm, _, _) = await NewTableVmAsync();

        vm.Target.IsExistingTable = true;
        await SettleAsync(vm);

        var configuration = vm.BuildConfiguration();
        Assert.Equal(ImportTargetKind.ExistingTable, configuration.Target.Kind);
        Assert.Empty(configuration.Target.NewTableColumns);
    }
}
