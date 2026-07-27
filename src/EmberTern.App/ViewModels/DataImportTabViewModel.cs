using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Controls;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;

namespace EmberTern.App.ViewModels;

/// <summary>One raw record in the source-preview grid — the provider's own values, before conversion.</summary>
public sealed class ImportSourceRecordRowViewModel
{
    public ImportSourceRecordRowViewModel(RawRecord record)
    {
        SourceRowNumber = record.SourceRowNumber;
        Values = record.Values;
    }

    public int SourceRowNumber { get; }
    public object?[] Values { get; }

    /// <summary>
    /// True when this record's field count differs from what the rest of the file does — the instant tell for
    /// a wrong separator or a stray quote, shown as a gutter marker so it is seen rather than counted (§3.6).
    /// <para>
    /// Stamped after the whole preview is read, against the MOST COMMON width — deliberately not against the
    /// schema's, which is the WIDEST record so that every column stays mappable. Comparing to the widest would
    /// invert the signal: one row with an extra field would set the width and mark all two hundred good rows
    /// as odd.
    /// </para>
    /// </summary>
    public bool IsRagged { get; internal set; }

    public object? ValueAt(int index) => index >= 0 && index < Values.Length ? Values[index] : null;
}

/// <summary>
/// The Data Import working surface (§3) — <b>one surface, not a wizard</b> (decision D7).
/// <para>
/// ⭐ <b>This VM is the single owner of <see cref="ImportConfiguration"/>.</b> Section VMs read their slice and
/// produce a new one; the ONLY place UI state becomes the record (and back) is
/// <see cref="BuildConfiguration"/> / <see cref="ApplyConfiguration"/>. That is §4.8.6, and it is what lets
/// named profiles arrive in I11 as pure UI over an existing store instead of a two-way mapper over forty
/// scattered properties — the rebuild the design exists to avoid.
/// </para>
/// <para>
/// <b>Recalculation (§4.7)</b> is one chain, re-run after any change, lazy and cancellable: a newer edit
/// cancels the in-flight schema read (the CTS idiom the editor's language service uses). Readiness is cheap
/// and computed synchronously; reading the source is not, and goes to a background thread.
/// </para>
/// <para>
/// <b>Etap I5 scope:</b> the frame, the readiness strip and the Source-and-format section. Target, mapping,
/// preview-after-conversion and the run itself are I6/I7 — until then readiness honestly reports "no target",
/// which is the correct answer rather than a placeholder.
/// </para>
/// </summary>
public sealed partial class DataImportTabViewModel : ViewModelBase
{
    /// <summary>Records held for the source preview. A million-row file must not become a million rows in
    /// memory (design R8); the preview is a diagnostic, not the data.</summary>
    public const int SourcePreviewRows = 200;

    private readonly IImportProvider _delimitedProvider = new DelimitedTextImportProvider();

    /// <summary>Everything outside this surface, as delegates — so the VM stays testable without a database and
    /// no Firebird or Avalonia type reaches a ViewModel (rule #1).</summary>
    private readonly DataImportEnvironment _environment;

    private ImportConfiguration _configuration = ImportConfiguration.Empty;
    private SourceSchema? _schema;
    private ImportTarget? _target;
    private bool _sourceExists = true;
    private bool _sourceReadable = true;
    private bool _tablesLoaded;
    private CancellationTokenSource? _recalculation;

    /// <summary>
    /// True once the user has expanded the format options by hand, which suspends auto-collapse (U11) until
    /// they collapse them again. An automat that closes a panel the user just opened is worse than no automat
    /// at all (§2.2 point 2).
    /// </summary>
    private bool _formatOptionsHeldOpen;

    public DataImportTabViewModel(DataImportEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

        Source = new ImportSourceSectionViewModel();
        Source.Changed += (_, _) => QueueRecalculate();

        Target = new ImportTargetSectionViewModel();
        Target.Changed += (_, _) => QueueRecalculate();

        Mapping = new ImportMappingPanelViewModel();
        Mapping.Changed += (_, _) => OnMappingEdited();
        Mapping.StrategyRequested += (_, strategy) => ApplyMappingStrategy(strategy);

        Readiness = new ImportReadinessViewModel();
        ConvertedPreview = new ImportConvertedPreviewViewModel();
        Report = new ImportRunReportViewModel();
        Timer = new ExecutionTimer();
        PreviewRows = new ObservableCollection<ImportSourceRecordRowViewModel>();
        PreviewFields = new ObservableCollection<SourceField>();

        // ⭐ "Last used" (§4.8.4). Restoring is the SAME path a named profile will take in I11 — the whole point
        // of §4.8.1 is that there is nothing else to build for it. It goes through ApplyConfiguration, so the
        // world is re-read and anything that no longer fits shows up in the readiness strip rather than being
        // silently applied (§0.7 / §4.8.5).
        var restored = _environment.LoadLastUsed?.Invoke();
        if (restored is not null)
        {
            RestoredLastConfiguration = true;
            ApplyConfiguration(restored);
        }
        else
        {
            Recalculate();
        }
    }

    /// <summary>The Source and format section.</summary>
    public ImportSourceSectionViewModel Source { get; }

    /// <summary>The Target section (§3.4) — existing table; the new-table variant is etap I8.</summary>
    public ImportTargetSectionViewModel Target { get; }

    /// <summary>The Mapping panel (§3.5).</summary>
    public ImportMappingPanelViewModel Mapping { get; }

    /// <summary>The readiness strip (§3.2).</summary>
    public ImportReadinessViewModel Readiness { get; }

    /// <summary>The converted preview (§3.6) — the values as they would reach the database.</summary>
    public ImportConvertedPreviewViewModel ConvertedPreview { get; }

    /// <summary>What the last run did (§3.7).</summary>
    public ImportRunReportViewModel Report { get; }

    /// <summary>The shared live elapsed indicator, docked right in the command bar so it never shifts the
    /// buttons — the Script Executor's pattern.</summary>
    public ExecutionTimer Timer { get; }

    /// <summary>True when the surface opened with the previous configuration restored (§4.8.4). The status line
    /// says so quietly, with a way to forget it — an automatic restore the user cannot see is a configuration
    /// they did not choose.</summary>
    [ObservableProperty] private bool _restoredLastConfiguration;

    /// <summary>Raw records as the provider produced them — the "Source preview" bottom tab.</summary>
    public ObservableCollection<ImportSourceRecordRowViewModel> PreviewRows { get; }

    /// <summary>The fields the preview grid builds its columns from.</summary>
    public ObservableCollection<SourceField> PreviewFields { get; }

    /// <summary>Raised when the preview's shape changed, so the view can rebuild its dynamic columns.</summary>
    public event EventHandler? PreviewSchemaChanged;

    /// <summary>The view supplies a file picker; the VM never touches a dialog type (rule #1).</summary>
    public event Func<Task<string?>>? FilePickRequested;

    /// <summary>The view supplies the clipboard text. App owns Avalonia's clipboard, Core gets a string —
    /// which is exactly why the clipboard is not a second parser (§1.5).</summary>
    public event Func<Task<string?>>? ClipboardReadRequested;

    /// <summary>Asks the view to expand and focus a section (a readiness chip was clicked).</summary>
    public event EventHandler<ImportSection>? SectionFocusRequested;

    /// <summary>
    /// Asks the user to confirm an action that destroys data, returning their answer. The view owns the dialog;
    /// the VM owns the question (rule #1).
    /// <para>
    /// Used for exactly one thing: "empty the table first" is about to delete N rows. §0 gives every place the
    /// module would otherwise guess two options — ask, or refuse with a reason — and this is the ask.
    /// </para>
    /// </summary>
    public event Func<string, Task<bool>>? ConfirmRequested;

    /// <summary>Asks the view to open the shared export dialog over the report's problem list.</summary>
    public event Func<IReadOnlyList<ImportProblemRowViewModel>, Task>? ExportReportRequested;

    /// <summary>Asks the view to put text on the clipboard.</summary>
    public event Action<string>? CopyToClipboardRequested;

    /// <summary>Asks the converted-preview grid to scroll to a row (a problem row was double-clicked).</summary>
    public event EventHandler<int>? PreviewRowRevealRequested;

    // ── Band C: the one message surface ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty] private MessageSeverity _statusSeverity = MessageSeverity.Info;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    // ── Band H: the surface status line — numbers, never adjectives (§9.1 point 4) ──────────────────────

    [ObservableProperty] private string _surfaceStatus = string.Empty;

    /// <summary>Where the rows are going: the active connection and the lane that carries them (U9).</summary>
    [ObservableProperty] private string _destinationStatus = string.Empty;

    /// <summary>True while a source read is in flight.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Bottom panel collapse state (§3.1 band G).</summary>
    [ObservableProperty] private bool _isBottomPanelCollapsed;

    /// <summary>
    /// Remembered height of the bottom panel, in pixels. It lives on the VM rather than in the view because
    /// the import tab is transient — the view is gone before the workspace is written — and it is persisted
    /// globally (<c>WorkspaceState.ImportPreviewPanelHeight</c>), the way the SQL editor's results panel is.
    /// </summary>
    [ObservableProperty] private double _bottomPanelHeight = 190;

    [RelayCommand]
    private void ToggleBottomPanel() => IsBottomPanelCollapsed = !IsBottomPanelCollapsed;

    /// <summary>
    /// Manual expand/collapse of the format options. A manual toggle always wins over any automatic
    /// collapsing — an automat that fights the user is worse than none (§2.2 point 2) — so opening them by
    /// hand pins them open until they are closed again.
    /// </summary>
    [RelayCommand]
    private void ToggleFormatOptions()
    {
        Source.IsExpanded = !Source.IsExpanded;
        _formatOptionsHeldOpen = Source.IsExpanded;
    }

    // ══ Band B — the command bar (§3.1) ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// What happens to the user's working transaction (§4.5). <c>Manual</c> is the default and always will be:
    /// the module never finishes a transaction the user did not ask it to (rule #3).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransactionModeDescription))]
    private ImportTransactionMode _transactionMode = ImportTransactionMode.Manual;

    partial void OnTransactionModeChanged(ImportTransactionMode value)
    {
        OnPropertyChanged(nameof(TransactionModeIndex));
        OnCommandBarEdited();
    }

    /// <summary>Plain-language consequence of the selected mode, shown where the mode is picked — the Script
    /// Executor's <c>Sequenced</c> precedent: non-atomicity is disclosed where the decision is taken, never
    /// discovered in the report (§0.5).</summary>
    public string TransactionModeDescription => TransactionMode switch
    {
        ImportTransactionMode.AutoCommitOnSuccess => UiStrings.ImportTransactionAutoCommitDescription,
        ImportTransactionMode.Batched => string.Format(
            CultureInfo.CurrentCulture, UiStrings.ImportTransactionBatchedDescriptionFormat, _configuration.CommitEveryRows),
        _ => UiStrings.ImportTransactionManualDescription,
    };

    [ObservableProperty] private ImportErrorPolicy _errorPolicy = ImportErrorPolicy.StopOnFirstError;

    partial void OnErrorPolicyChanged(ImportErrorPolicy value)
    {
        OnPropertyChanged(nameof(ErrorPolicyIndex));
        OnCommandBarEdited();
    }

    /// <summary>A command-bar decision is still a decision: it goes into the ONE record and re-evaluates
    /// readiness, but it moves nothing upstream — the source has not changed, so re-reading it would be work
    /// nobody asked for.</summary>
    private void OnCommandBarEdited()
    {
        if (_suspendCommandBarNotification) return;

        _configuration = BuildConfiguration();
        PublishReadiness();
    }

    private bool _suspendCommandBarNotification;

    /// <summary>True while an import or a validation is on the wire.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    private bool _isRunning;

    /// <summary>
    /// Configuration is <b>read-only</b> during a run, not hidden and not greyed into illegibility (§3.7): it is
    /// the thing that explains what is happening.
    /// </summary>
    public bool IsConfigurationEnabled => !IsRunning;

    /// <summary>Live progress line: rows read / total-if-known, written, failed.</summary>
    [ObservableProperty] private string _progressText = string.Empty;

    /// <summary>0–100 while a total is known. A streaming import cannot know its row count without reading the
    /// file twice, so when it is unknown the bar runs indeterminate rather than inventing a percentage.</summary>
    [ObservableProperty] private double _progressPercent;

    [ObservableProperty] private bool _isProgressIndeterminate = true;

    /// <summary>
    /// ComboBox index ⇄ <see cref="ImportTransactionMode"/>. The index is presentation; the mode is the
    /// decision, and it lives in the ONE record (§4.8.6). Mirrors the Script Executor's mode picker.
    /// </summary>
    public int TransactionModeIndex
    {
        get => TransactionMode switch
        {
            ImportTransactionMode.AutoCommitOnSuccess => 1,
            ImportTransactionMode.Batched => 2,
            _ => 0,
        };
        set => TransactionMode = value switch
        {
            1 => ImportTransactionMode.AutoCommitOnSuccess,
            2 => ImportTransactionMode.Batched,
            _ => ImportTransactionMode.Manual,
        };
    }

    public int ErrorPolicyIndex
    {
        get => ErrorPolicy == ImportErrorPolicy.SkipInvalidRows ? 1 : 0;
        set => ErrorPolicy = value == 1 ? ImportErrorPolicy.SkipInvalidRows : ImportErrorPolicy.StopOnFirstError;
    }

    public bool CanImport => !IsRunning && Readiness.CanRun && _environment.CreateWriter is not null;

    /// <summary>Validate needs no transaction — it writes nowhere — so its gate is Core's weaker
    /// <c>CanValidate</c> rather than a second opinion computed here.</summary>
    public bool CanValidate => !IsRunning && Readiness.CanValidate;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task ImportAsync() => RunGuardedAsync(validation: false);

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private Task ValidateAsync() => RunGuardedAsync(validation: true);

    /// <summary>
    /// ⭐ <b>The command boundary — the last place a failure can stop, and therefore the place that guarantees
    /// an import can never take the application down with it.</b>
    /// <para>
    /// This is not belt-and-braces around code that already handles its errors; it is load-bearing, and it is
    /// here because of a specific, measured defect. <c>AsyncRelayCommand</c> rethrows a faulted command's
    /// exception <b>on the dispatcher</b>, where nothing is left to catch it — so any exception this module
    /// fails to handle does not produce a bad report, it terminates the process.
    /// </para>
    /// <para>
    /// ⚠ <b>Why a catch-all rather than a list of expected types.</b> This VM reaches the world exclusively
    /// through <see cref="DataImportEnvironment"/>'s delegates, precisely so no Firebird type reaches a
    /// ViewModel (rule #1). That erasure cuts both ways: <b>a component that talks to the world through
    /// delegates cannot enumerate the exceptions the world throws.</b> An allow-list here is unknowable by
    /// construction, and it duly failed — <c>FbException</c> and <c>DdlExecutionException</c>, the two most
    /// likely failures in a database module, were on none of them.
    /// </para>
    /// <para>
    /// A cancellation is let through untouched: it is the user's decision, not a fault, and
    /// <see cref="RunAsync"/> already reports it as such (gotcha #253).
    /// </para>
    /// </summary>
    private async Task RunGuardedAsync(bool validation)
    {
        try
        {
            await RunAsync(validation).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Already reported where it happened; nothing here to add.
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    /// <summary>
    /// The ONE way this surface reports a failure it did not anticipate: as a message the user can read and
    /// copy, on the shared banner, with the run left in a clean state.
    /// </summary>
    private void ReportUnexpected(Exception ex)
    {
        Timer.Stop();
        IsRunning = false;
        ProgressText = string.Empty;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;

        SetStatus(ex.Message, MessageSeverity.Error);
        PublishReadiness();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void CancelRun() => _run?.Cancel();

    private CancellationTokenSource? _run;

    /// <summary>
    /// ⭐ <b>ONE run.</b> Import and Validate differ by the writer and by nothing else — the same discipline
    /// <c>ImportPipeline</c> itself is built on, one level up. There is no second path for a dry run to drift
    /// away from, which is what makes "Validate says it is fine" mean something.
    /// </summary>
    private async Task RunAsync(bool validation)
    {
        if (IsRunning) return;

        var configuration = BuildConfiguration();
        _configuration = configuration;

        var target = _target;
        var source = TryCreateSource(configuration);
        if (target is null || source is null) return;

        // §0.5 — "empty the table first" destroys data, so the confirmation carries the NUMBER, read by the very
        // transaction that is about to do the deleting. A real import only; a validation writes nothing and must
        // not delete anything either.
        var emptyFirst = !validation && configuration.Behavior.EmptyTargetBeforeImport;
        if (emptyFirst && !await ConfirmEmptyAsync(target.TableName).ConfigureAwait(true)) return;

        var writer = validation
            ? new DryRunImportWriter()
            : _environment.CreateWriter?.Invoke(configuration);
        if (writer is null) return;

        // ⭐ The new table is created HERE — before the writer touches anything, on the Ddl lane, committed.
        // A validation deliberately creates nothing: a dry run against the PROJECTION is the whole reason the
        // projection exists, and it is the one answer that is still free (§0.5 / gotcha #213).
        string? createdTable = null;
        if (!validation && configuration.Target.Kind == ImportTargetKind.NewTable)
        {
            createdTable = await CreateTargetTableAsync(configuration).ConfigureAwait(true);
            if (createdTable is null) return;

            // The writer must work against what Firebird actually BUILT, not against what we asked for. The
            // projection is a prediction; the catalog is the fact, and a domain, a charset or a rounded
            // precision could make them differ.
            target = await ReadCreatedTargetAsync(createdTable).ConfigureAwait(true) ?? target;
            _target = target;
        }

        // Recorded at START, not at the end: a run the user cancels or that fails still says what they asked
        // for, and that is the configuration worth coming back to. One owner of persistence (§4.8.6).
        if (!validation) _environment.SaveLastUsed?.Invoke(configuration);

        _run?.Dispose();
        _run = new CancellationTokenSource();
        var token = _run.Token;

        IsRunning = true;
        Report.Clear();
        SetStatus(string.Empty, MessageSeverity.Info);
        Timer.Start();
        var clock = Stopwatch.StartNew();

        var progress = new Progress<ImportProgress>(ShowProgress);

        try
        {
            if (emptyFirst && _environment.EmptyTargetAsync is not null)
            {
                await _environment.EmptyTargetAsync(target.TableName, token).ConfigureAwait(true);
            }

            var outcome = await Task.Run(
                () => ImportPipeline.RunAsync(
                    configuration, target, _delimitedProvider, source, writer,
                    ImportCharsetGuard.Strict(_environment.ConnectionCharset?.Invoke()),
                    progress, token),
                token).ConfigureAwait(true);

            clock.Stop();

            // The pipeline does not create tables, so it cannot know one was created (§4.5) — the coordinator
            // that did it fills the fact in, and the report then states the one effect a Rollback cannot undo.
            outcome = outcome with { CreatedTable = createdTable };

            Report.Publish(outcome, validation, clock.Elapsed, RowsCommittedBy(writer));
            await FinishTransactionIfRequestedAsync(configuration, outcome, validation).ConfigureAwait(true);
            await DropCreatedTableIfFailedAsync(configuration, createdTable, outcome).ConfigureAwait(true);
            ReportReady?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // A cancel is the user's decision, never a fault — reporting it as one would be the fabricated
            // failure gotcha #253 is about. Rows already written stay in the open transaction, and the pipeline's
            // own outcome would have said so; here the run did not get that far.
            clock.Stop();
            SetStatus(UiStrings.ImportRunCancelled, MessageSeverity.Warning);
            await DropCreatedTableIfFailedAsync(configuration, createdTable, failed: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Everything that is not a cancellation is a failure of the RUN, and the run is the thing that
            // knows a created table may now need undoing. The type is deliberately not narrowed: the writer,
            // the target and the transaction all arrive as delegates, so their exceptions are not this VM's to
            // enumerate (see RunGuardedAsync).
            clock.Stop();
            SetStatus(ex.Message, MessageSeverity.Error);
            await DropCreatedTableIfFailedAsync(configuration, createdTable, failed: true).ConfigureAwait(true);
        }
        finally
        {
            Timer.Stop();
            IsRunning = false;
            ProgressText = string.Empty;
            ProgressPercent = 0;
            IsProgressIndeterminate = true;
            PublishReadiness();
        }
    }

    /// <summary>Raised when a run finished and the report has something to show, so the view can bring the
    /// Report tab forward (§3.7).</summary>
    public event EventHandler? ReportReady;

    // ── Creating and dropping the new table (etap I8) ───────────────────────────────────────────────────

    /// <summary>
    /// Runs the <c>CREATE TABLE</c> on the Ddl lane. Returns the table's name on success, <c>null</c> when the
    /// run must not continue.
    /// <para>
    /// ⚠ <b>The one ordering rule of this etap:</b> it happens BEFORE the first row and is COMMITTED, because
    /// a Firebird transaction cannot use an object whose DDL it has not committed (gotcha #213). Everything the
    /// surface says about Rollback not removing the table follows from this line.
    /// </para>
    /// </summary>
    private async Task<string?> CreateTargetTableAsync(ImportConfiguration configuration)
    {
        var tableName = configuration.Target.TableName.Trim();

        if (_environment.CreateTableAsync is null || tableName.Length == 0) return null;

        SetStatus(
            string.Format(CultureInfo.CurrentCulture, UiStrings.ImportCreatingTableFormat, tableName),
            MessageSeverity.Info);

        try
        {
            var sql = ImportNewTable.BuildCreateSql(tableName, configuration.Target.NewTableColumns);
            await _environment.CreateTableAsync(sql, CancellationToken.None).ConfigureAwait(true);

            // The table exists now and is committed (Ddl lane), so say so — the metadata tree adds it without
            // re-reading the catalog. Reported here rather than at the end of the run on purpose: the table
            // survives a failed import (§0.5, gotcha #213), so its existence is not conditional on the rows.
            _environment.TableCreated?.Invoke(tableName);

            SetStatus(
                string.Format(CultureInfo.CurrentCulture, UiStrings.ImportCreatedTableFormat, tableName),
                MessageSeverity.Success);

            return tableName;
        }
        catch (Exception ex)
        {
            // Nothing has been written yet, so refusing here costs the user nothing — which is exactly why the
            // CREATE goes first rather than somewhere in the middle.
            // ⚠ Not narrowed by type on purpose: the executor reaches this VM as a delegate, and what it
            // actually throws (DdlExecutionException) is a Firebird type a ViewModel may not name (rule #1).
            // An allow-list here would have turned every refused CREATE into a closed application.
            SetStatus(
                string.Format(CultureInfo.CurrentCulture, UiStrings.ImportCreateTableFailedFormat, tableName, ex.Message),
                MessageSeverity.Error);
            return null;
        }
    }

    /// <summary>Re-reads a freshly created table from the catalog. A failure here is not fatal — the projection
    /// still describes it — so it degrades to null and the caller keeps what it had.</summary>
    private async Task<ImportTarget?> ReadCreatedTargetAsync(string tableName)
    {
        if (_environment.ReadTargetAsync is null) return null;

        try
        {
            return await _environment.ReadTargetAsync(tableName, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Degrading to the projection is fine — it describes the table we just asked for. Failing the whole
            // run because the catalog read stumbled would not be.
            SetStatus(ex.Message, MessageSeverity.Warning);
            return null;
        }
    }

    private Task DropCreatedTableIfFailedAsync(
        ImportConfiguration configuration, string? createdTable, ImportOutcome outcome)
        => DropCreatedTableIfFailedAsync(
            configuration, createdTable, outcome.Cancelled || outcome.RowsFailed > 0);

    /// <summary>
    /// Offers to undo a table this run created, when the import into it did not succeed (§0.5).
    /// <para>
    /// ⚠ <b>Two effects, one question.</b> The rows have to be gone before the table can be, so this rolls the
    /// import's own transaction back and only then drops. The confirmation says both out loud rather than
    /// mentioning the drop and performing the rollback quietly — a dialog that under-describes what it is about
    /// to do is how uncommitted work disappears.
    /// </para>
    /// <para>
    /// The checkbox arms it; the confirmation is still asked, because the box may have been ticked long before
    /// and dropping an object is not something to do from memory.
    /// </para>
    /// </summary>
    private async Task DropCreatedTableIfFailedAsync(
        ImportConfiguration configuration, string? createdTable, bool failed)
    {
        if (createdTable is null || !failed) return;
        if (!configuration.Behavior.DropTableOnFailure) return;
        if (_environment.DropTableAsync is null || ConfirmRequested is null) return;

        var question = string.Format(
            CultureInfo.CurrentCulture, UiStrings.ImportConfirmDropTableFormat, createdTable);

        if (!await ConfirmRequested.Invoke(question).ConfigureAwait(true)) return;

        try
        {
            if (_environment.RollbackAsync is not null)
            {
                await _environment.RollbackAsync().ConfigureAwait(true);
                Report.TransactionLeftOpen = false;
            }

            await _environment.DropTableAsync(
                ImportNewTable.BuildDropSql(createdTable), CancellationToken.None).ConfigureAwait(true);

            // Undone — and the tree is told, so it does not keep a leaf for a table that is gone.
            _environment.TableDropped?.Invoke(createdTable);

            SetStatus(
                string.Format(CultureInfo.CurrentCulture, UiStrings.ImportDroppedTableFormat, createdTable),
                MessageSeverity.Success);
        }
        catch (Exception ex)
        {
            // The table stays, and the status says so. Throwing out of a clean-up that runs during another
            // failure's unwind would replace one problem the user can read with a window that is simply gone.
            SetStatus(
                string.Format(CultureInfo.CurrentCulture, UiStrings.ImportDropTableFailedFormat, createdTable, ex.Message),
                MessageSeverity.Error);
        }
        finally
        {
            PublishReadiness();
        }
    }

    private async Task<bool> ConfirmEmptyAsync(string tableName)
    {
        if (ConfirmRequested is null) return true;

        long? rows = null;
        if (_environment.CountTargetRowsAsync is not null)
        {
            try
            {
                rows = await _environment.CountTargetRowsAsync(tableName, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // A count we could not take is not a reason to skip the question — only a reason to ask it
                // without the number. ⚠ This is the line the I8 crash came out of: the target did not exist
                // yet, the engine said so with an FbException, and an allow-list that did not name that type
                // let the exception escape the command and take the process down.
                SetStatus(ex.Message, MessageSeverity.Warning);
            }
        }

        var question = rows is { } count
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.ImportConfirmEmptyCountFormat, count, tableName)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.ImportConfirmEmptyFormat, tableName);

        return await ConfirmRequested.Invoke(question).ConfigureAwait(true);
    }

    /// <summary>
    /// <c>AutoCommitOnSuccess</c> means exactly that — success, with nothing rejected and nothing cancelled.
    /// Anything else leaves the decision with the user, in front of the report's numbers.
    /// </summary>
    private async Task FinishTransactionIfRequestedAsync(
        ImportConfiguration configuration, ImportOutcome outcome, bool validation)
    {
        if (validation || !outcome.TransactionLeftOpen) return;
        if (configuration.Transaction != ImportTransactionMode.AutoCommitOnSuccess) return;
        if (outcome.Cancelled || outcome.RowsFailed > 0) return;

        await CommitAsync().ConfigureAwait(true);
    }

    private static long RowsCommittedBy(IImportWriter writer)
        => writer is IPartiallyCommittedImportWriter partial ? partial.RowsCommitted : 0;

    private void ShowProgress(ImportProgress progress)
    {
        ProgressText = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.ImportProgressFormat,
            progress.RowsRead, progress.RowsWritten, progress.RowsFailed);

        var total = _schema?.EstimatedRows;
        IsProgressIndeterminate = total is not > 0;
        ProgressPercent = IsProgressIndeterminate ? 0 : Math.Min(100d, progress.RowsRead * 100d / total!.Value);
    }

    // ── The transaction decision, taken where the numbers are (§3.7) ────────────────────────────────────

    public bool CanFinishTransaction => !IsRunning && Report.TransactionLeftOpen;

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (_environment.CommitAsync is null) return;

        try
        {
            await _environment.CommitAsync().ConfigureAwait(true);
            Report.TransactionLeftOpen = false;
            SetStatus(UiStrings.ImportCommitted, MessageSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            PublishReadiness();
        }
    }

    [RelayCommand]
    private async Task RollbackAsync()
    {
        if (_environment.RollbackAsync is null) return;

        try
        {
            await _environment.RollbackAsync().ConfigureAwait(true);
            Report.TransactionLeftOpen = false;
            SetStatus(UiStrings.ImportRolledBack, MessageSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            PublishReadiness();
        }
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (ExportReportRequested is null || Report.Problems.Count == 0) return;

        try
        {
            await ExportReportRequested.Invoke(Report.Problems).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    [RelayCommand]
    private void CopyReport() => CopyToClipboardRequested?.Invoke(Report.ToClipboardText());

    /// <summary>Double-clicking a problem takes the user to that row in the converted preview (§3.7) — the
    /// report names a row, and the surface can show it.</summary>
    [RelayCommand]
    private void RevealProblem(ImportProblemRowViewModel? problem)
    {
        if (problem is null) return;
        PreviewRowRevealRequested?.Invoke(this, problem.SourceRowNumber);
    }

    /// <summary>Forgets the restored configuration — the „Wyczyść" beside the quiet restore note (§4.8.4).</summary>
    [RelayCommand]
    private void ForgetLastConfiguration()
    {
        RestoredLastConfiguration = false;
        ApplyConfiguration(ImportConfiguration.Empty);
    }

    // ── Source commands ─────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (FilePickRequested is null) return;

        try
        {
            var path = await FilePickRequested.Invoke().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path)) return;

            Source.UseFile = true;
            Source.FilePath = path;   // raises Changed → recalculation
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    [RelayCommand]
    private async Task UseClipboardAsync()
    {
        if (ClipboardReadRequested is null) return;

        try
        {
            var text = await ClipboardReadRequested.Invoke().ConfigureAwait(true);
            Source.ClipboardText = text ?? string.Empty;
            Source.UseFile = false;
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    /// <summary>A readiness chip was clicked — expand and focus the section that caused it. The advantage
    /// over a wizard: every gap is visible AND reachable in one click (§3.2).</summary>
    /// <summary>
    /// A readiness chip (or finding) was clicked — <b>go to the thing it is talking about</b>.
    /// <para>
    /// ⭐ One behaviour for all five sections, because a row of controls that each react differently cannot be
    /// read: the user would have to learn, chip by chip, whether this one is a filter, a tab, a shortcut or a
    /// status light. So every chip does exactly one thing — put the caret in the control that section owns —
    /// and the ONE section that has something foldable (Format) additionally <b>toggles</b> it, rather than
    /// only ever opening it.
    /// </para>
    /// <para>
    /// ⚠ Deliberately NOT touching the "only unmapped" filter any more. Flipping a checkbox the user can see
    /// but did not click is the kind of surprise that makes a control unreadable — the chip navigates, and
    /// filtering stays the user's decision.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void FocusSection(ImportSection section)
    {
        if (section is ImportSection.Format)
        {
            // Toggle, not open: clicking the same chip twice must undo itself, or the chip is a one-way door.
            Source.IsExpanded = !Source.IsExpanded;
            _formatOptionsHeldOpen = Source.IsExpanded;
        }

        SectionFocusRequested?.Invoke(this, section);
    }

    /// <summary>
    /// ⭐ U11 — the format options collapse themselves once they are settled, which is what makes the repeat
    /// import cheap (§2.2): the picker stays live, so the next file is one click and <c>F5</c>, and the
    /// options the user set months ago do not occupy the surface for the rest of the session.
    /// <para>
    /// Deliberately conservative about when it may act: only after a source has actually been read (fields
    /// exist), and never when the user has opened the options by hand.
    /// </para>
    /// </summary>
    private void AutoCollapseFormatOptionsIfSettled()
    {
        if (_formatOptionsHeldOpen) return;
        if (_schema is null || _schema.Fields.Count == 0) return;

        Source.IsExpanded = false;
    }

    // ── The one translation point (§4.8.6) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles the current UI state into the ONE record.
    /// <para>
    /// Sections that do not exist yet (a NEW table's columns — etap I8) pass their part through from the held
    /// configuration unchanged, so a restored profile keeps decisions this build cannot yet edit rather than
    /// silently dropping them.
    /// </para>
    /// </summary>
    public ImportConfiguration BuildConfiguration()
    {
        var descriptor = Source.BuildSource(ResolveFileKind(Source.FilePath));
        var isSpreadsheet = descriptor.Kind is ImportSourceKind.Xlsx or ImportSourceKind.Xls;

        return _configuration with
        {
            Source = descriptor,
            // Exactly one options block is set, matching the source kind — the invariant
            // ImportConfiguration.MatchesSourceKind checks and readiness reports.
            Delimited = isSpreadsheet ? null : Source.BuildDelimited(),
            Spreadsheet = isSpreadsheet ? _configuration.Spreadsheet ?? new SpreadsheetOptions() : null,
            Culture = Source.BuildCulture(),
            Target = Target.BuildTarget(_configuration.Target),
            // The grid is authoritative once it has rows; before that the held mapping passes through, so a
            // restored profile keeps its pairing until the target it refers to has actually been read.
            Mapping = Mapping.Rows.Count > 0 ? Mapping.BuildMapping() : _configuration.Mapping,
            Behavior = Target.BuildBehavior(_configuration.Behavior),
            // The command bar's two decisions. They belong to the record like every other one — a transaction
            // mode that lived only on the toolbar would be missing from a saved profile, which is precisely the
            // omission the reflection round-trip guard exists to catch (§4.8.6).
            Transaction = TransactionMode,
            ErrorPolicy = ErrorPolicy,
        };
    }

    /// <summary>Loads a configuration into the surface — the path a restored profile takes (§4.8.5).</summary>
    public void ApplyConfiguration(ImportConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Source.Apply(_configuration);
        Target.Apply(_configuration);

        // The applied configuration may carry a new table the user designed earlier. Those columns are their
        // decision, so the first inference pass adopts them instead of proposing over the top of them.
        _inferredFor = null;
        _adoptRestoredColumns = _configuration.Target.Kind == ImportTargetKind.NewTable
            && _configuration.Target.NewTableColumns.Count > 0;

        // Suspended while this VM writes to itself — without it, applying a configuration would restart the very
        // chain that is about to run. The same guard the section VMs use, for the same reason.
        _suspendCommandBarNotification = true;
        try
        {
            TransactionMode = _configuration.Transaction;
            ErrorPolicy = _configuration.ErrorPolicy;
        }
        finally
        {
            _suspendCommandBarNotification = false;
        }

        Recalculate();
    }

    /// <summary>The configuration as it currently stands. Test seam and the future profile-save source.</summary>
    public ImportConfiguration CurrentConfiguration => _configuration;

    // ── The recalculation chain (§4.7) ──────────────────────────────────────────────────────────────────

    private void QueueRecalculate() => Recalculate();

    private void Recalculate()
    {
        _configuration = BuildConfiguration();

        // A newer change cancels the in-flight read rather than racing it — the CTS idiom the editor's
        // language service uses.
        _recalculation?.Cancel();
        _recalculation?.Dispose();
        var cts = new CancellationTokenSource();
        _recalculation = cts;

        UpdateFileFacts();
        PendingRecalculation = RunGuardedChainAsync(cts.Token);
    }

    /// <summary>
    /// The chain's own boundary. Every link already degrades in place, but the chain is started
    /// <b>fire-and-forget</b> — nobody awaits <see cref="PendingRecalculation"/> outside the tests — so an
    /// exception escaping it would become an <c>UnobservedTaskException</c> and be rethrown by the finalizer
    /// thread. Recalculating is background work the user did not ask for by name; it must never be able to
    /// close the window.
    /// </summary>
    private async Task RunGuardedChainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunChainAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit — the normal way a chain ends.
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, MessageSeverity.Error);
        }
    }

    /// <summary>
    /// The chain of §4.7, in the one order that is not a guess resting on a guess: <b>source → target →
    /// mapping → readiness → converted preview</b>. Each link consumes what the previous one established, and
    /// every link is cancellable, so a newer edit supersedes the whole tail rather than racing it.
    /// <para>
    /// The preview comes last because it is the most expensive link and the only one that needs all the others
    /// to have settled: it is a real (bounded) import, so it needs a source, a target and a mapping.
    /// </para>
    /// </summary>
    private async Task RunChainAsync(CancellationToken cancellationToken)
    {
        await EnsureTablesLoadedAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        await ReadSourceAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        await InferNewTableColumnsAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        await ReadTargetAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        PlanMapping();
        PublishReadiness();

        await RefreshConvertedPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// How long the surface waits before re-converting the preview. §3.6 asks for ~150 ms so that changing the
    /// decimal separator is felt immediately without re-reading the file on every keystroke.
    /// <para>
    /// It is a delay on the chain's own cancellable token, not a timer: a newer edit cancels it like every other
    /// link, and the whole thing stays awaitable from a test. A <c>DispatcherTimer</c> here would re-introduce a
    /// path no headless test can reach (gotcha #251).
    /// </para>
    /// </summary>
    internal TimeSpan PreviewDebounce { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// ⭐ Runs the ONE import, bounded, into a writer that keeps the rows instead of sending them.
    /// <para>
    /// This is the whole of §3.6's promise that the grid shows "exactly what will reach the database": the
    /// values come from the same converter, validator, mapping and culture a real run would use, because they
    /// come from a real run. There is deliberately no "convert for display" routine to drift away from it.
    /// </para>
    /// </summary>
    private async Task RefreshConvertedPreviewAsync(CancellationToken cancellationToken)
    {
        var configuration = _configuration;
        var target = _target;
        var source = TryCreateSource(configuration);

        var columns = new List<string>();
        foreach (var mapping in configuration.MappedColumns()) columns.Add(mapping.TargetColumnName);

        if (target is null || source is null || columns.Count == 0)
        {
            ConvertedPreview.Clear();
            return;
        }

        try
        {
            if (PreviewDebounce > TimeSpan.Zero)
            {
                await Task.Delay(PreviewDebounce, cancellationToken).ConfigureAwait(true);
            }

            ConvertedPreview.IsBusy = true;

            var writer = new PreviewImportWriter(ImportConvertedPreviewViewModel.MaxRows);
            var provider = new BoundedImportProvider(_delimitedProvider, ImportConvertedPreviewViewModel.MaxRows);

            var outcome = await Task.Run(
                () => ImportPipeline.RunAsync(
                    configuration, target, provider, source, writer,
                    ImportCharsetGuard.Strict(_environment.ConnectionCharset?.Invoke()),
                    progress: null, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested) return;

            ConvertedPreview.Publish(columns, writer.Rows, outcome, RawValuesForRow);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit — not a failure, and nothing to report.
        }
        catch (Exception ex)
        {
            ConvertedPreview.Clear();
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) ConvertedPreview.IsBusy = false;
        }
    }

    /// <summary>
    /// The RAW values of one source row, projected into mapped-column order — what a failed row shows.
    /// <para>
    /// Reuses the records the source preview already read (they are the same bounded head of the same file) and
    /// <see cref="ImportMappingPlanner.Project"/>, the one owner of "which field feeds which column". Reading
    /// the file a third time, or re-deriving the projection here, would be a second answer to a settled
    /// question.
    /// </para>
    /// </summary>
    private object?[]? RawValuesForRow(int sourceRowNumber)
    {
        foreach (var row in PreviewRows)
        {
            if (row.SourceRowNumber != sourceRowNumber) continue;

            var mapped = new List<ColumnMapping>();
            foreach (var mapping in _configuration.MappedColumns()) mapped.Add(mapping);

            return ImportMappingPlanner.Project(new RawRecord(sourceRowNumber, row.Values), mapped);
        }
        return null;
    }

    // ── New-table type inference (etap I8) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The signature the current type grid was inferred from: the source's field names plus the culture the
    /// values were read under. Both genuinely change what the right types are — different fields describe a
    /// different file, and a different decimal separator changes what counts as a number.
    /// </summary>
    private string? _inferredFor;

    /// <summary>
    /// ⭐ Proposes the new table's columns from the source — <b>scanning the whole thing</b> (REK-7 / R19).
    /// <para>
    /// <b>When it runs</b> follows the module's own rule of provable preservation (§4.7): the grid is re-inferred
    /// when the source's FIELDS or the CULTURE change, and left alone otherwise. So a restored profile's types
    /// survive a source whose fields still match them — the same "the name is the proof" test the mapping
    /// planner uses — while a genuinely different file gets types that describe it. A user's edit likewise
    /// stands until the ground under it moves.
    /// </para>
    /// <para>
    /// It is the most expensive link in the chain and rides the same cancellable token as the rest: a newer
    /// edit abandons an in-flight scan rather than racing it. That is what makes a full-source scan affordable
    /// on a surface the user is still typing into.
    /// </para>
    /// </summary>
    private async Task InferNewTableColumnsAsync(CancellationToken cancellationToken)
    {
        if (_configuration.Target.Kind != ImportTargetKind.NewTable) return;

        if (_schema is null || _schema.Fields.Count == 0)
        {
            _inferredFor = null;
            return;
        }

        var signature = BuildInferenceSignature(_schema, _configuration);
        if (string.Equals(_inferredFor, signature, StringComparison.Ordinal)) return;

        // ⚠ A restored configuration's columns are the user's own decisions, and they are adopted for the
        // source as it first reads rather than immediately overwritten by a proposal — that would be the
        // "an older build quietly robbed the profile" defect §4.8.6 exists to prevent, in a new disguise.
        // From here on the ordinary rule applies: change the fields or the culture and the types are proposed
        // afresh, because the ground they stood on has moved.
        if (_adoptRestoredColumns && _configuration.Target.NewTableColumns.Count > 0)
        {
            _adoptRestoredColumns = false;
            _inferredFor = signature;
            return;
        }

        _adoptRestoredColumns = false;

        var source = TryCreateSource(_configuration);
        if (source is null) return;

        Target.IsInferring = true;
        try
        {
            var configuration = _configuration;
            var inference = await Task.Run(
                    () => ColumnTypeInferencer.InferAsync(
                        _schema, _delimitedProvider, source, configuration,
                        ColumnTypeInferencer.DefaultScanLimit, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested) return;

            Target.ShowInferredColumns(inference);
            _inferredFor = signature;
            _configuration = BuildConfiguration();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit — not a failure, and nothing to report (gotcha #253).
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) Target.IsInferring = false;
        }
    }

    /// <summary>What the current types were inferred from. Nothing else belongs in it: the file's SIZE or its
    /// timestamp would re-infer on every save of an unchanged shape, and the target's name has no bearing on
    /// what type a column should be.</summary>
    private static string BuildInferenceSignature(SourceSchema schema, ImportConfiguration configuration)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var field in schema.Fields) builder.Append(field.Name).Append('');

        var culture = configuration.Culture;
        builder.Append('|')
            .Append(culture.DecimalSeparator)
            .Append(culture.ThousandsSeparator)
            .Append(culture.DateOrder)
            .Append(culture.DateSeparator)
            .Append(culture.TimeSeparator);

        return builder.ToString();
    }

    /// <summary>
    /// True until a restored configuration's own new-table columns have been adopted for the source it names.
    /// <para>
    /// It exists because "the types have not been inferred for this source yet" and "the user already told us
    /// what these columns are" look identical from inside the chain, and treating the second as the first
    /// would overwrite a saved decision with a proposal the moment the tab opened.
    /// </para>
    /// </summary>
    private bool _adoptRestoredColumns;

    /// <summary>Loads the table list once per tab. The list is a fact about the database, not a user decision,
    /// so re-reading it on every keystroke would be work nobody asked for.</summary>
    private async Task EnsureTablesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_tablesLoaded || _environment.ListTablesAsync is null || !_environment.IsConnected()) return;

        try
        {
            var tables = await _environment.ListTablesAsync(cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested) return;

            Target.ShowTables(tables);
            _tablesLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // Superseded — not a failure.
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, MessageSeverity.Error);
        }
    }

    /// <summary>
    /// Reads the chosen table's columns and BEFORE INSERT triggers. Refusing with a reason is §0-compliant;
    /// pretending a table exists is not, so a target that cannot be read becomes a null target and readiness
    /// says so.
    /// <para>
    /// ⭐ For a table that does not exist yet there is nothing to read, so the target is
    /// <see cref="ImportNewTable.Project">projected</see> from the columns the user is about to create. That is
    /// what makes mapping, the converted preview and — most valuably — <b>"Validate"</b> work on a new table:
    /// the dry run answers "will these inferred types actually hold my file?" at the one moment the answer is
    /// still free, because after the <c>CREATE</c> the table is committed and beyond a Rollback (§0.5).
    /// </para>
    /// </summary>
    private async Task ReadTargetAsync(CancellationToken cancellationToken)
    {
        var tableName = _configuration.Target.TableName;

        if (_configuration.Target.Kind == ImportTargetKind.NewTable)
        {
            _target = string.IsNullOrWhiteSpace(tableName) || _configuration.Target.NewTableColumns.Count == 0
                ? null
                : ImportNewTable.Project(tableName, _configuration.Target.NewTableColumns);

            // The facts line describes a table that HAS a shape; a new one shows its inference basis instead.
            Target.ShowFacts(null);
            return;
        }

        if (_environment.ReadTargetAsync is null || string.IsNullOrWhiteSpace(tableName))
        {
            _target = null;
            Target.ShowFacts(null);
            return;
        }

        Target.IsBusy = true;
        try
        {
            _target = await _environment.ReadTargetAsync(tableName, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested) return;

            Target.ShowFacts(_target);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _target = null;
            Target.ShowFacts(null);
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) Target.IsBusy = false;
        }
    }

    /// <summary>
    /// Re-plans the mapping through <see cref="ImportMappingPlanner"/> — the ONE owner of "which field feeds
    /// which column". The previous mapping is handed in so everything provably still correct survives; a
    /// changed TARGET hands in nothing, because a different table is a different identity (§4.7).
    /// </summary>
    private void PlanMapping()
    {
        if (_target is null || _schema is null)
        {
            // ⚠ Clear the GRID, never the record. There is nothing to show without both sides, but the held
            // mapping is a set of user decisions: dropping it here would mean a restored profile loses its
            // pairing merely because the target has not been read back yet — the "an older build quietly
            // robbed the profile" defect §4.8.6 exists to prevent. Re-choosing the same table restores it.
            Mapping.Update(null, _schema, ImportMappingPlan.Empty);
            return;
        }

        var previous = string.Equals(_mappedTable, _target.TableName, StringComparison.OrdinalIgnoreCase)
            ? _configuration.Mapping
            : null;

        var plan = ImportMappingPlanner.Plan(_target, _schema, previous);
        _mappedTable = _target.TableName;

        Mapping.Update(_target, _schema, plan);
        _configuration = _configuration with { Mapping = Mapping.BuildMapping() };
    }

    /// <summary>The table the current grid belongs to — the fact that decides whether a previous mapping may
    /// be carried over at all.</summary>
    private string? _mappedTable;

    /// <summary>A grid edit changes the record and the readiness, but nothing upstream: the source has not
    /// moved, so re-reading it would be work the user did not ask for.</summary>
    private void OnMappingEdited()
    {
        _configuration = BuildConfiguration();
        PublishReadiness();
    }

    /// <summary>Re-plans with a different strategy, then adopts the result into the existing rows so the grid
    /// does not jump under the user.</summary>
    private void ApplyMappingStrategy(ImportMappingStrategy strategy)
    {
        if (_target is null) return;

        var plan = strategy switch
        {
            ImportMappingStrategy.ByPosition when _schema is not null
                => ImportMappingPlanner.MatchByPosition(_target, _schema),
            ImportMappingStrategy.Clear => ImportMappingPlanner.Clear(_target),
            _ => null,
        };

        if (plan is null) return;

        Mapping.AdoptPlan(plan);
        _configuration = BuildConfiguration();
        PublishReadiness();
    }

    /// <summary>
    /// The in-flight recalculation, so a test can await the chain instead of sleeping on it.
    /// <para>
    /// Kept as a real field rather than fire-and-forget because "start work and hope" is untestable, and an
    /// untestable path is one nobody has tested (gotcha #251).
    /// </para>
    /// </summary>
    internal Task? PendingRecalculation { get; private set; }

    private async Task ReadSourceAsync(CancellationToken cancellationToken)
    {
        var configuration = _configuration;
        var source = TryCreateSource(configuration);

        if (source is null)
        {
            _schema = null;
            _sourceReadable = true;   // nothing to read is not "unreadable"
            return;                   // the chain still runs on — a target can be chosen before a file is
        }

        IsBusy = true;
        try
        {
            // Detection runs FIRST and writes into the declared values, because those are what the reader
            // then uses (§0.4 — the detector proposes, it does not maintain a second hidden setting).
            using (Source.SuspendChangeNotifications())
            {
                await RunDetectionAsync(source, cancellationToken).ConfigureAwait(true);
            }
            if (cancellationToken.IsCancellationRequested) return;

            // Re-assemble with whatever detection settled on, so schema and preview are read exactly the way
            // the user now sees the section configured.
            _configuration = BuildConfiguration();
            configuration = _configuration;

            var schema = await _delimitedProvider
                .ReadSchemaAsync(source, configuration, cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested) return;

            _schema = schema;
            _sourceReadable = true;
            SetStatus(string.Empty, MessageSeverity.Info);
            await LoadPreviewAsync(source, configuration, schema, cancellationToken).ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested) AutoCollapseFormatOptionsIfSettled();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change — not a failure, and nothing to report.
            return;
        }
        catch (Exception ex)
        {
            _schema = null;
            _sourceReadable = false;
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsBusy = false;
        }
    }

    private async Task LoadPreviewAsync(
        IImportSource source,
        ImportConfiguration configuration,
        SourceSchema schema,
        CancellationToken cancellationToken)
    {
        PreviewRows.Clear();
        PreviewFields.Clear();
        foreach (var field in schema.Fields) PreviewFields.Add(field);
        PreviewSchemaChanged?.Invoke(this, EventArgs.Empty);

        var taken = 0;
        await foreach (var record in _delimitedProvider
                           .ReadRecordsAsync(source, configuration, cancellationToken)
                           .ConfigureAwait(true))
        {
            PreviewRows.Add(new ImportSourceRecordRowViewModel(record));
            if (++taken >= SourcePreviewRows) break;
        }

        MarkRaggedRows(PreviewRows);
    }

    /// <summary>
    /// Flags the records that disagree with the rest of the file about how many fields there are.
    /// <para>
    /// The reference is the MOST COMMON width, not the schema's. The schema reports the WIDEST record so that
    /// every column the file contains stays mappable — but using that as the reference here would invert the
    /// signal: a single row with one extra field would set the width and mark every other row as the odd one.
    /// The useful statement is "this row disagrees with the others", and the majority is what "the others"
    /// means.
    /// </para>
    /// </summary>
    internal static void MarkRaggedRows(IReadOnlyList<ImportSourceRecordRowViewModel> rows)
    {
        if (rows.Count == 0) return;

        var counts = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            counts.TryGetValue(row.Values.Length, out var seen);
            counts[row.Values.Length] = seen + 1;
        }

        var common = 0;
        var best = -1;
        foreach (var pair in counts)
        {
            // Ties go to the wider shape: if half the file has 3 fields and half has 2, the 2-field rows are
            // the ones missing something.
            if (pair.Value > best || (pair.Value == best && pair.Key > common))
            {
                best = pair.Value;
                common = pair.Key;
            }
        }

        foreach (var row in rows) row.IsRagged = row.Values.Length != common;
    }

    /// <summary>
    /// Runs the detectors and writes their proposals into the DECLARED values, with the evidence beside them.
    /// A proposal that cannot say why it was made is a silent decision, which §0.4 forbids.
    /// <para>
    /// Order matters and is not incidental: the ENCODING is proposed from raw bytes first, because the
    /// delimiter can only be looked for in text that has already been decoded — proposing a delimiter from
    /// mis-decoded bytes would be a guess resting on a guess.
    /// </para>
    /// </summary>
    private async Task RunDetectionAsync(IImportSource source, CancellationToken cancellationToken)
    {
        var delimited = _configuration.Delimited;
        if (delimited is null) return;

        if (delimited.AutoDetectEncoding && source is FileImportSource file)
        {
            var proposal = EncodingDetector.Propose(file.ReadDetectionSample());
            Source.ApplyEncodingProposal(proposal.CharsetName, DescribeEncodingBasis(proposal));
            delimited = Source.BuildDelimited();
        }

        if (!delimited.AutoDetectDelimiter) return;

        var sample = await ReadTextSampleAsync(source, delimited, cancellationToken).ConfigureAwait(true);
        if (sample.Length == 0) return;

        var delimiterProposal = DelimiterDetector.Propose(sample, delimited);
        if (delimiterProposal is null) return;

        Source.ApplyDelimiterProposal(
            delimiterProposal.Delimiter,
            string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportDelimiterEvidenceFormat,
                delimiterProposal.ConsistentRecords,
                delimiterProposal.SampledRecords,
                delimiterProposal.FieldCount));
    }

    /// <summary>Reads a bounded head of the source as text, for the delimiter detector. Bounded because a
    /// detector needs a sample, not a file (design R8).</summary>
    private static async Task<string> ReadTextSampleAsync(
        IImportSource source, DelimitedOptions options, CancellationToken cancellationToken)
    {
        using var reader = await source
            .OpenTextAsync(EmberTern.Core.Connections.CharsetCatalog.Resolve(options.EncodingName), cancellationToken)
            .ConfigureAwait(true);

        var buffer = new char[DetectionSampleChars];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
        return read <= 0 ? string.Empty : new string(buffer, 0, read);
    }

    /// <summary>Characters of the source handed to the delimiter detector.</summary>
    private const int DetectionSampleChars = 64 * 1024;

    private static string DescribeEncodingBasis(EncodingProposal proposal) => proposal.Basis switch
    {
        EncodingDetectionBasis.ByteOrderMark => UiStrings.ImportEncodingEvidenceBom,
        EncodingDetectionBasis.AsciiOnly => UiStrings.ImportEncodingEvidenceAscii,
        _ => UiStrings.ImportEncodingEvidenceHeuristic,
    };

    private void PublishReadiness()
    {
        var input = new ImportReadinessInput
        {
            Configuration = _configuration,
            Schema = _schema,
            SourceExists = _sourceExists,
            SourceReadable = _sourceReadable,
            Target = _target,
            IsConnected = _environment.IsConnected(),
            NewTableNameTaken = IsNewTableNameTaken(),
        };

        Readiness.Update(ImportReadiness.Evaluate(input), PreviewRows.Count);
        UpdateSurfaceStatus();

        // The run buttons are gated on the strip, so every republication has to re-ask them. This is the one
        // place readiness changes, which is why it is the one place that says so (gotcha #179 — computing the
        // value correctly is not enough if nothing tells the binding to re-query it).
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanValidate));
        OnPropertyChanged(nameof(CanFinishTransaction));
        ImportCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Whether the name chosen for a new table already belongs to one. Answered from the table list this tab
    /// already loaded — a fact about the world, so readiness takes it as an input rather than looking it up
    /// (§4.8.2), and it is what turns a raw server error at run time into a blocking item beforehand.
    /// </summary>
    private bool IsNewTableNameTaken()
    {
        if (_configuration.Target.Kind != ImportTargetKind.NewTable) return false;

        var name = _configuration.Target.TableName.Trim();
        if (name.Length == 0 || !_tablesLoaded) return false;

        foreach (var table in Target.Tables)
        {
            if (string.Equals(table, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void UpdateSurfaceStatus()
    {
        UpdateDestinationStatus();

        if (_schema is null || _schema.Fields.Count == 0)
        {
            SurfaceStatus = UiStrings.ImportSurfaceStatusNoSource;
            return;
        }

        SurfaceStatus = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.ImportSurfaceStatusFormat,
            _schema.Fields.Count,
            PreviewRows.Count,
            PreviewRows.Count >= SourcePreviewRows ? UiStrings.ImportSurfaceStatusMore : string.Empty);
    }

    /// <summary>
    /// ⭐ Band H's left half — <b>where the rows are going and on which connection lane</b> (U9). It used to
    /// sit in a header band that otherwise only repeated the tab's own title; moved here because this is the
    /// line that answers "where does this land", and in I7 the transaction mode joins it.
    /// <para>
    /// The lane is a constant on purpose: rows always go to the <b>Data</b> lane as the ONE user working
    /// transaction (§4.5). Saying so out loud is the point — a module that writes to a database should not
    /// make the user guess which transaction it joins.
    /// </para>
    /// </summary>
    private void UpdateDestinationStatus()
    {
        var connection = _environment.ConnectionName();

        DestinationStatus = connection.Length == 0
            ? UiStrings.ImportDestinationNotConnected
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportDestinationWithModeFormat,
                connection,
                UiStrings.ImportDestinationDataLane,
                DescribeTransactionMode(TransactionMode));
    }

    private void UpdateFileFacts()
    {
        if (!Source.UseFile || Source.FilePath.Length == 0)
        {
            _sourceExists = true;
            Source.FileFacts = string.Empty;
            return;
        }

        try
        {
            var info = new FileInfo(Source.FilePath);
            _sourceExists = info.Exists;
            Source.FileFacts = info.Exists
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.ImportFileFactsFormat,
                    info.Length / 1024d,
                    info.LastWriteTime)
                : UiStrings.ImportFileMissing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _sourceExists = false;
            Source.FileFacts = UiStrings.ImportFileMissing;
        }
    }

    private IImportSource? TryCreateSource(ImportConfiguration configuration)
    {
        if (configuration.Source.Kind == ImportSourceKind.Clipboard)
        {
            return Source.ClipboardText.Length == 0 ? null : new TextImportSource(Source.ClipboardText);
        }

        var path = configuration.Source.Path;
        if (string.IsNullOrWhiteSpace(path) || !_sourceExists) return null;

        // A spreadsheet has no provider until etap I9. Refusing with a reason is §0-compliant; pretending to
        // read it would not be.
        if (configuration.Source.Kind is ImportSourceKind.Xlsx or ImportSourceKind.Xls)
        {
            SetStatus(
                string.Format(CultureInfo.CurrentCulture, UiStrings.ImportFormatNotYetSupportedFormat, Path.GetExtension(path)),
                MessageSeverity.Warning);
            _sourceReadable = false;
            return null;
        }

        return new FileImportSource(path);
    }

    private void SetStatus(string message, MessageSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    /// <summary>The transaction mode in one word, for band H — the line that says where the rows land and what
    /// then happens to them.</summary>
    internal static string DescribeTransactionMode(ImportTransactionMode mode) => mode switch
    {
        ImportTransactionMode.AutoCommitOnSuccess => UiStrings.ImportTransactionAutoCommit,
        ImportTransactionMode.Batched => UiStrings.ImportTransactionBatched,
        _ => UiStrings.ImportTransactionManual,
    };

    /// <summary>Extension → source kind. The picker shows the resolved kind, so an automatic decision is
    /// visible and overridable rather than silent.</summary>
    internal static ImportSourceKind ResolveFileKind(string? path)
    {
        var extension = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ImportSourceKind.Xlsx,
            ".xls" => ImportSourceKind.Xls,
            ".txt" => ImportSourceKind.Text,
            _ => ImportSourceKind.Csv,
        };
    }
}
