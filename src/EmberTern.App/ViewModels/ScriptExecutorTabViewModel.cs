using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Scripting;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Backs the Script Executor workspace tab: an editor whose whole script is run as ONE
/// caller-controlled transaction (via <see cref="FirebirdScriptExecutor"/>), with a per-
/// statement results grid. Default mode leaves the transaction OPEN after the run so the
/// user reviews the results and then Commits or Rolls back (hard rule #3 — no autocommit);
/// the optional Auto-commit-on-success mode commits when nothing failed, else rolls back.
/// Splitting/classification comes from the driver's <c>FbScript</c> via
/// <see cref="FirebirdScriptParser"/> (SET TERM / PSQL / EXECUTE BLOCK aware).
/// </summary>
public partial class ScriptExecutorTabViewModel : ViewModelBase
{
    private readonly FirebirdScriptParser _parser;
    private readonly FirebirdScriptExecutor _executor;
    private readonly TransactionService _transactionService;

    private readonly List<ScriptResultRowViewModel> _allRows = new();
    private IReadOnlyList<ScriptStatement> _lastStatements = Array.Empty<ScriptStatement>();
    // For a Sequenced run, statement index → 1-based committed-step number (empty otherwise). Built
    // from the SAME planner the engine ran, so the displayed boundaries match what actually committed.
    private int[] _segmentMap = Array.Empty<int>();
    private CancellationTokenSource? _cts;

    public ScriptExecutorTabViewModel(
        FirebirdScriptParser parser, FirebirdScriptExecutor executor, TransactionService transactionService)
    {
        _parser = parser;
        _executor = executor;
        _transactionService = transactionService;
        Rows = new ObservableCollection<ScriptResultRowViewModel>();
        StatusText = UiStrings.ScriptStatusReady;
        _transactionService.TransactionStateChanged += OnTransactionStateChanged;
    }

    /// <summary>The filtered result rows the grid binds to (All / Success / Failed).</summary>
    public ObservableCollection<ScriptResultRowViewModel> Rows { get; }

    // Editor content — pushed in from the view's TextChanged (two-way TextEditor.Text is flaky).
    [ObservableProperty] private string _scriptText = string.Empty;

    // 0 = Manual (review then commit, DEFAULT), 1 = Auto-commit on success, 2 = Sequenced (deployment).
    [NotifyPropertyChangedFor(nameof(SelectedModeDescription))]
    [ObservableProperty] private int _transactionModeIndex;
    [ObservableProperty] private bool _stopOnError = true;

    /// <summary>The selected execution mode's description — surfaced on the picker so the Sequenced
    /// (non-atomic) trade-off is stated where the mode is chosen. Recomputed on selection change.</summary>
    public string SelectedModeDescription => ResolveModeDescription(TransactionModeIndex);

    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitScriptCommand))]
    [NotifyCanExecuteChangedFor(nameof(RollbackScriptCommand))]
    [ObservableProperty] private bool _isRunning;

    /// <summary>Live elapsed timer for the running script — the SAME shared mechanism the SQL Editor
    /// and Execute Procedure/Function use. Driven off <see cref="IsRunning"/> so every exit path
    /// (finally, "nothing to run", <c>Fail</c>) stops it with no scattering.</summary>
    public ExecutionTimer ExecutionTimer { get; } = new();

    partial void OnIsRunningChanged(bool value)
    {
        if (value) ExecutionTimer.Start();
        else ExecutionTimer.Stop();
    }

    // True after a Manual run left the transaction open — drives the Commit/Rollback buttons.
    // Cleared when the transaction settles (here or via the SQL Editor's transaction bar).
    [NotifyCanExecuteChangedFor(nameof(CommitScriptCommand))]
    [NotifyCanExecuteChangedFor(nameof(RollbackScriptCommand))]
    [ObservableProperty] private bool _transactionOpen;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failedCount;

    [ObservableProperty] private int _selectedFilterIndex; // 0 all, 1 success, 2 failed
    partial void OnSelectedFilterIndexChanged(int value) => RebuildRows();

    public bool HasResults => _allRows.Count > 0;

    private ScriptTransactionMode Mode => ResolveMode(TransactionModeIndex);

    // Pure picker-index → mode mapping (0 Manual · 1 Auto-commit · 2 Sequenced; anything else Manual).
    // Internal + static so the mapping is unit-pinned without the VM's services.
    internal static ScriptTransactionMode ResolveMode(int index) => index switch
    {
        1 => ScriptTransactionMode.AutoCommitOnSuccess,
        2 => ScriptTransactionMode.Sequenced,
        _ => ScriptTransactionMode.Manual,
    };

    // Pure picker-index → description mapping (same order as ResolveMode).
    internal static string ResolveModeDescription(int index) => index switch
    {
        1 => UiStrings.ScriptModeAutoCommitDescription,
        2 => UiStrings.ScriptModeSequencedDescription,
        _ => UiStrings.ScriptModeManualDescription,
    };

    /// <summary>The view writes the TSV to the clipboard (VM holds no clipboard type).</summary>
    public event Action<string>? CopyToClipboardRequested;

    /// <summary>Raised when the user activates a result row — the view selects + scrolls the
    /// editor to (offset, length) of the source statement.</summary>
    public event Action<int, int>? NavigateToStatementRequested;

    // ─── Run / Stop ───────────────────────────────────────────────────────────
    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        // Pre-flight (Etap 3): a script runs in ONE transaction started here. If a transaction
        // is already open — a prior script run left it open, or an uncommitted SQL Editor
        // statement — block with a targeted message and keep the current results + open-tx state
        // (so the Commit/Rollback buttons stay actionable). This is the "no other working tx"
        // gate; the executor also guards as a backstop.
        var block = ResolveRunBlock(_transactionService.IsActive, TransactionOpen);
        if (block is not null)
        {
            HasError = true;
            StatusText = block;
            return;
        }

        IsRunning = true;
        HasError = false;
        ClearRows();
        StatusText = UiStrings.ScriptStatusRunning;

        IReadOnlyList<ScriptStatement> statements;
        try
        {
            statements = _parser.Parse(ScriptText);
        }
        catch (Exception ex)
        {
            Fail(string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusParseErrorFormat, ex.Message));
            return;
        }

        if (statements.Count == 0)
        {
            HasError = false;
            StatusText = UiStrings.ScriptStatusNothingToRun;
            IsRunning = false;
            return;
        }

        var disallowed = ScriptValidation.FindDisallowed(statements);
        if (disallowed.Count > 0)
        {
            Fail(BuildDisallowedMessage(disallowed));
            return;
        }

        // A single-transaction mode (Manual / Auto-commit) cannot run a mixed DDL+DML migration
        // (gotcha #213). Stop BEFORE the first statement with a message that points at Sequenced,
        // rather than letting the executor fail on a later statement with "Table unknown".
        var mixedBlock = ResolveMixedScriptBlock(statements, Mode);
        if (mixedBlock is not null)
        {
            Fail(mixedBlock);
            return;
        }

        _lastStatements = statements;
        _segmentMap = BuildSegmentMap(statements, Mode);
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScriptStatementResult>(AddResultRow);
        try
        {
            var outcome = await _executor
                .RunAsync(statements, Mode, StopOnError, progress, _cts.Token)
                .ConfigureAwait(true);
            TransactionOpen = outcome.TransactionLeftOpen && _transactionService.IsActive;
            HasError = outcome.AnyFailed;
            StatusText = BuildOutcomeStatus(outcome, Mode);
            // Sequenced only (no-op otherwise): now the run is done, stamp each row with its step's
            // commit/rollback outcome so the grid can show which steps persisted, then append a muted
            // row for every statement a stop-on-error / cancellation left unexecuted.
            ApplyStepStatuses(_allRows, _segmentMap, outcome.Results);
            AppendNotRunRows(outcome.Results);
        }
        catch (OperationCanceledException)
        {
            TransactionOpen = _transactionService.IsActive;
            StatusText = UiStrings.ScriptStatusCancelled;
        }
        catch (ScriptExecutionException ex)
        {
            HasError = true;
            TransactionOpen = _transactionService.IsActive;
            StatusText = ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private void Fail(string message)
    {
        HasError = true;
        StatusText = message;
        IsRunning = false;
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    // ─── Commit / Rollback (Manual mode, after review) ────────────────────────
    private bool CanFinalize() => TransactionOpen && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanFinalize))]
    private async Task CommitScriptAsync()
    {
        try
        {
            await _executor.CommitAsync().ConfigureAwait(true);
            TransactionOpen = false;
            HasError = false;
            StatusText = UiStrings.ScriptStatusCommitted;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusText = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFinalize))]
    private async Task RollbackScriptAsync()
    {
        try
        {
            await _executor.RollbackAsync().ConfigureAwait(true);
            TransactionOpen = false;
            HasError = false;
            StatusText = UiStrings.ScriptStatusRolledBack;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusText = ex.Message;
        }
    }

    // ─── Copy ─────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void CopyAll() => CopyToClipboardRequested?.Invoke(BuildClipboard(failedOnly: false));

    [RelayCommand]
    private void CopyFailed() => CopyToClipboardRequested?.Invoke(BuildClipboard(failedOnly: true));

    // ─── Open / Save .sql ─────────────────────────────────────────────────────
    // The view owns the file picker + IO (StorageProvider + SqlFileWriter, UTF-8 no-BOM);
    // the VM just triggers it and receives the result via LoadScript / ReportFileSaved /
    // ReportFileError. Keeps the VM free of Avalonia storage types.
    public event Func<Task>? OpenRequested;
    public event Func<Task>? SaveRequested;

    [RelayCommand]
    private void OpenScript() => _ = OpenRequested?.Invoke();

    [RelayCommand]
    private void SaveScript() => _ = SaveRequested?.Invoke();

    /// <summary>Loads opened file text into the editor (via ScriptText → the view pushes it).</summary>
    public void LoadScript(string text, string fileName)
    {
        ScriptText = text ?? string.Empty;
        HasError = false;
        StatusText = string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusOpenedFormat, fileName);
    }

    public void ReportFileSaved(string fileName)
    {
        HasError = false;
        StatusText = string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusSavedFormat, fileName);
    }

    public void ReportFileError(string message)
    {
        HasError = true;
        StatusText = string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusFileErrorFormat, message);
    }

    // ─── Statement navigation ─────────────────────────────────────────────────
    public void NavigateToStatement(ScriptResultRowViewModel row)
    {
        if (row.HasSourceRange) NavigateToStatementRequested?.Invoke(row.SourceOffset, row.SourceLength);
    }

    /// <summary>Unsubscribes from the transaction-state event so the transient tab can be
    /// collected after it's closed (the TransactionService outlives the tab).</summary>
    public void Detach() => _transactionService.TransactionStateChanged -= OnTransactionStateChanged;

    // ─── internals ────────────────────────────────────────────────────────────
    private void OnTransactionStateChanged(object? sender, EventArgs e)
    {
        // Fires off the UI thread (gotcha #11). Marshal the state read + property write.
        Dispatcher.UIThread.Post(() =>
        {
            if (TransactionOpen && !_transactionService.IsActive) TransactionOpen = false;
        });
    }

    private void AddResultRow(ScriptStatementResult result)
    {
        int offset = result.Index < _lastStatements.Count ? _lastStatements[result.Index].SourceOffset : -1;
        int length = result.Index < _lastStatements.Count ? _lastStatements[result.Index].SourceLength : 0;
        int step = result.Index < _segmentMap.Length ? _segmentMap[result.Index] : 0;
        var row = new ScriptResultRowViewModel(result, offset, length, step);
        _allRows.Add(row);
        if (result.Success) SuccessCount++; else FailedCount++;
        if (PassesFilter(row)) Rows.Add(row);
        OnPropertyChanged(nameof(HasResults));
    }

    private void ClearRows()
    {
        _allRows.Clear();
        Rows.Clear();
        SuccessCount = 0;
        FailedCount = 0;
        OnPropertyChanged(nameof(HasResults));
    }

    // Appends a synthesized "not run" row for every statement a Sequenced stop-on-error / cancellation
    // left unexecuted (from FindNotRunStatements — empty for single-transaction modes, so a no-op there).
    // Not-run statements are always a contiguous suffix (execution stops/cancels and never resumes), so
    // appending in index order keeps the grid in source order. They are neither success nor failure, so
    // SuccessCount/FailedCount are untouched.
    private void AppendNotRunRows(IReadOnlyList<ScriptStatementResult> results)
    {
        var notRun = FindNotRunStatements(_segmentMap, results);
        if (notRun.Count == 0) return;

        foreach (int index in notRun)
        {
            if (index >= _lastStatements.Count) continue;
            int step = index < _segmentMap.Length ? _segmentMap[index] : 0;
            var row = new ScriptResultRowViewModel(_lastStatements[index], index, step);
            _allRows.Add(row);
            if (PassesFilter(row)) Rows.Add(row);
        }
        OnPropertyChanged(nameof(HasResults));
    }

    private bool PassesFilter(ScriptResultRowViewModel row) => SelectedFilterIndex switch
    {
        // "Success" = statements that actually succeeded — a not-run row succeeded no more than it failed.
        1 => !row.IsFailed && !row.IsNotRun,
        2 => row.IsFailed,
        _ => true,
    };

    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var row in _allRows)
        {
            if (PassesFilter(row)) Rows.Add(row);
        }
    }

    // Internal + static so the summary wording per mode is unit-pinned without the VM's services.
    internal static string BuildOutcomeStatus(ScriptRunOutcome outcome, ScriptTransactionMode mode)
    {
        if (outcome.Cancelled)
        {
            // Sequenced never leaves a transaction open, so the generic "transaction still open"
            // cancelled line would mislead — it committed step-by-step.
            return mode == ScriptTransactionMode.Sequenced
                ? UiStrings.ScriptStatusSequencedCancelled
                : UiStrings.ScriptStatusCancelled;
        }

        var elapsed = TimeSpan.Zero;
        foreach (var r in outcome.Results) elapsed += r.Elapsed;
        var elapsedText = FormatDuration(elapsed);

        if (mode == ScriptTransactionMode.Sequenced)
        {
            // Committed step-by-step — state the non-atomic reality, not a single verdict.
            return string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusSequencedSummaryFormat,
                outcome.SuccessCount, outcome.FailedCount, elapsedText);
        }

        if (!outcome.TransactionLeftOpen)
        {
            // Auto-commit mode already finalized the transaction.
            var head = outcome.AnyFailed ? UiStrings.ScriptStatusRolledBack : UiStrings.ScriptStatusCommitted;
            return string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusAutoSummaryFormat,
                head, outcome.SuccessCount, outcome.FailedCount, elapsedText);
        }

        return string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusManualSummaryFormat,
            outcome.SuccessCount, outcome.FailedCount, elapsedText);
    }

    // Tab-separated (#, Statement, Type, Result, Rows, Duration, [Error]) — pastes into Excel.
    private string BuildClipboard(bool failedOnly)
    {
        var sb = new StringBuilder();
        foreach (var r in _allRows)
        {
            if (failedOnly && !r.IsFailed) continue;
            sb.Append(r.Line).Append('\t').Append(r.Statement).Append('\t').Append(r.TypeText)
              .Append('\t').Append(r.Result).Append('\t').Append(r.RowsText).Append('\t').Append(r.Duration);
            if (r.IsFailed && r.Error.Length > 0) sb.Append('\t').Append(r.Error);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    internal static string FormatDuration(TimeSpan elapsed)
        => elapsed.TotalMilliseconds < 1000
            ? string.Format(CultureInfo.CurrentCulture, "{0:0} ms", elapsed.TotalMilliseconds)
            : string.Format(CultureInfo.CurrentCulture, "{0:0.00} s", elapsed.TotalSeconds);

    // Pure Run gate: returns null when a script may run, else the message explaining which
    // open transaction must be settled first — distinguishing this tab's own leftover
    // (Commit/Rollback are right here) from an external one (e.g. an uncommitted SQL Editor
    // statement). A script must own its transaction end-to-end, so it never joins an existing one.
    internal static string? ResolveRunBlock(bool transactionActive, bool ownLeftover)
    {
        if (!transactionActive) return null;
        return ownLeftover ? UiStrings.ScriptBlockOwnTxOpen : UiStrings.ScriptBlockExternalTxOpen;
    }

    // Pure: statement index → 1-based committed-step (Sequenced segment) number, reconstructed from
    // the SAME planner the engine ran — so the displayed step boundaries match what actually committed.
    // Empty for a non-Sequenced run (the whole script is one transaction — there are no steps). App
    // presentation only; the planner is Core and unchanged.
    internal static int[] BuildSegmentMap(IReadOnlyList<ScriptStatement> statements, ScriptTransactionMode mode)
    {
        if (mode != ScriptTransactionMode.Sequenced || statements.Count == 0) return Array.Empty<int>();

        var map = new int[statements.Count];
        int index = 0, step = 0;
        foreach (var segment in ScriptSegmentPlanner.Plan(statements))
        {
            step++;
            for (int i = 0; i < segment.Statements.Count && index < map.Length; i++) map[index++] = step;
        }
        return map;
    }

    // Pure: reconstructs each Sequenced step's commit/rollback outcome from the segment map (the plan
    // the engine ran) + the per-statement results, mirroring FirebirdScriptExecutor.RunSequencedAsync
    // EXACTLY — a step commits only if every one of its planned statements ran and none failed;
    // otherwise it rolled back (any failure, OR a partial run with no failure = cancelled mid-step),
    // and a step with no results at all was never reached. This is why a statement can be Success yet
    // its step rolled back: a LATER statement in the same step (transaction) failed. Empty in →
    // empty out (non-Sequenced run). App reconstruction only; the engine is untouched.
    internal static IReadOnlyDictionary<int, ScriptStepStatus> BuildStepStatuses(
        int[] segmentMap, IReadOnlyList<ScriptStatementResult> results)
    {
        var statuses = new Dictionary<int, ScriptStepStatus>();
        if (segmentMap.Length == 0) return statuses;

        var planned = new Dictionary<int, int>();
        foreach (var step in segmentMap)
            planned[step] = planned.TryGetValue(step, out var c) ? c + 1 : 1;

        var executed = new Dictionary<int, int>();
        var failed = new Dictionary<int, int>();
        foreach (var r in results)
        {
            if (r.Index < 0 || r.Index >= segmentMap.Length) continue;
            int step = segmentMap[r.Index];
            executed[step] = executed.TryGetValue(step, out var e) ? e + 1 : 1;
            if (!r.Success) failed[step] = failed.TryGetValue(step, out var f) ? f + 1 : 1;
        }

        foreach (var (step, plannedCount) in planned)
        {
            int exec = executed.TryGetValue(step, out var e) ? e : 0;
            int fail = failed.TryGetValue(step, out var f) ? f : 0;
            statuses[step] =
                exec == 0 ? ScriptStepStatus.NotRun
                : fail > 0 ? ScriptStepStatus.RolledBack
                : exec == plannedCount ? ScriptStepStatus.Committed
                : ScriptStepStatus.RolledBack; // partial run, no failure ⇒ cancelled mid-step
        }
        return statuses;
    }

    // Presentation: stamps each row with its Sequenced step's outcome (from BuildStepStatuses), so the
    // grid can colour the Step cell. Static + pure over the rows/map/results it is given (no services)
    // so it is unit-pinned. A no-op for a non-Sequenced run (empty map). Does NOT change the
    // reconstruction — it only distributes the step outcome onto the rows of that step.
    internal static void ApplyStepStatuses(
        IReadOnlyList<ScriptResultRowViewModel> rows, int[] segmentMap, IReadOnlyList<ScriptStatementResult> results)
    {
        if (segmentMap.Length == 0) return;
        var statuses = BuildStepStatuses(segmentMap, results);
        foreach (var row in rows)
            row.StepStatus = row.Step > 0 && statuses.TryGetValue(row.Step, out var s) ? s : ScriptStepStatus.NotRun;
    }

    // Pure: the statement indices (in source order) a Sequenced run left UNEXECUTED — a stop-on-error
    // stopped the run, or a cancellation ended it, before them, so they produced NO result row (rows
    // arrive only via the progress callback). Reconstructed from the plan (segmentMap has one entry per
    // statement) minus the indices the results cover. Empty for a single-transaction run (empty map),
    // so nothing is synthesized there — this is Sequenced presentation only, like the rest of seam C.
    // App reconstruction only; the engine is untouched.
    internal static IReadOnlyList<int> FindNotRunStatements(
        int[] segmentMap, IReadOnlyList<ScriptStatementResult> results)
    {
        if (segmentMap.Length == 0) return Array.Empty<int>();

        var ran = new HashSet<int>();
        foreach (var r in results)
            if (r.Index >= 0 && r.Index < segmentMap.Length) ran.Add(r.Index);

        var notRun = new List<int>();
        for (int i = 0; i < segmentMap.Length; i++)
            if (!ran.Contains(i)) notRun.Add(i);
        return notRun;
    }

    // Pure pre-flight gate: returns the block message when a single-transaction mode (Manual /
    // Auto-commit) is asked to run a MIXED DDL+DML script, else null. Sequenced is built for mixed
    // migrations, so it is never blocked. The engine is untouched — this only stops the run earlier,
    // with a message that explains the single-transaction limitation and names Sequenced, instead of
    // letting a later statement fail on "Table unknown" (gotcha #213).
    internal static string? ResolveMixedScriptBlock(IReadOnlyList<ScriptStatement> statements, ScriptTransactionMode mode)
    {
        if (mode == ScriptTransactionMode.Sequenced) return null;
        return IsMixedMigration(statements) ? UiStrings.ScriptStatusMixedNeedsSequenced : null;
    }

    // Pure: true when the script contains BOTH a schema statement (DDL/DCL) and a non-schema one —
    // the #213 risk surface. Classification comes from the AST-based SqlStatementClassifier (the same
    // authority the Sequenced planner uses), so "mixed" here and "segmented there" can never disagree.
    // A non-schema (Data or Ambiguous) statement counts as the data side (the classifier's safe default).
    private static bool IsMixedMigration(IReadOnlyList<ScriptStatement> statements)
    {
        bool hasSchema = false, hasNonSchema = false;
        foreach (var statement in statements)
        {
            if (SqlStatementClassifier.Classify(statement.Text) == SqlStatementCategory.Schema) hasSchema = true;
            else hasNonSchema = true;
            if (hasSchema && hasNonSchema) return true;
        }
        return false;
    }

    // Pure: lists the offending transaction-control / session statements so the message is
    // actionable ("remove these: COMMIT; SET NAMES WIN1250") rather than generic.
    internal static string BuildDisallowedMessage(IReadOnlyList<ScriptStatement> disallowed)
    {
        var snippets = string.Join("; ", disallowed.Select(s => Snippet(s.Text)));
        return string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptStatusDisallowedFormat, snippets);
    }

    private static string Snippet(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (flat.Contains("  ", StringComparison.Ordinal)) flat = flat.Replace("  ", " ");
        return flat.Length > 40 ? flat.Substring(0, 40) + "…" : flat;
    }
}

/// <summary>
/// A Sequenced step's (committed transaction's) outcome, reconstructed by the App from the plan +
/// results. Distinct from a statement's own success: a <see cref="Committed"/> step's changes
/// persisted; a <see cref="RolledBack"/> step's changes vanished (a statement in it failed, or it was
/// cancelled mid-way) even if this particular statement reported success; <see cref="NotRun"/> = the
/// step was never reached.
/// </summary>
public enum ScriptStepStatus
{
    NotRun,
    Committed,
    RolledBack,
}

/// <summary>One row in the Script Executor results grid.</summary>
public sealed partial class ScriptResultRowViewModel : ObservableObject
{
    private const int PreviewMaxLength = 100;

    public ScriptResultRowViewModel(ScriptStatementResult result, int sourceOffset, int sourceLength, int step = 0)
    {
        Line = result.Index + 1;
        Step = step;
        // Sequenced: the 1-based committed step; blank in single-transaction modes (step == 0).
        StepText = step > 0 ? step.ToString(CultureInfo.CurrentCulture) : string.Empty;
        Statement = Elide(result.Text);
        TypeText = KindLabel(result.Kind);
        IsFailed = !result.Success;
        Result = result.Success ? UiStrings.BatchResultOk : UiStrings.BatchResultFailed;
        RowsText = FormatRows(result);
        Duration = ScriptExecutorTabViewModel.FormatDuration(result.Elapsed);
        Error = result.Error ?? string.Empty;
        SourceOffset = sourceOffset;
        SourceLength = sourceLength;
    }

    /// <summary>
    /// A synthesized "not run" row (Step 5 seam C2b-2): a statement a Sequenced stop-on-error /
    /// cancellation left unexecuted. It has no <see cref="ScriptStatementResult"/> — the fields come
    /// from the source statement, and it is neither a success nor a failure. Its would-be step number
    /// is shown so the user sees which step never ran.
    /// </summary>
    public ScriptResultRowViewModel(ScriptStatement statement, int index, int step)
    {
        Line = index + 1;
        Step = step;
        StepText = step > 0 ? step.ToString(CultureInfo.CurrentCulture) : string.Empty;
        Statement = Elide(statement.Text);
        TypeText = KindLabel(statement.Kind);
        IsNotRun = true;
        IsFailed = false;
        Result = UiStrings.ScriptResultNotRun;
        RowsText = string.Empty;
        Duration = string.Empty;
        Error = string.Empty;
        SourceOffset = statement.SourceOffset;
        SourceLength = statement.SourceLength;
        StepStatus = ScriptStepStatus.NotRun;
    }

    public int Line { get; }
    /// <summary>1-based Sequenced step (0 in single-transaction modes). See <see cref="StepText"/>.</summary>
    public int Step { get; }

    // The step's commit/rollback outcome — set once after the run (via ScriptExecutorTabViewModel
    // .ApplyStepStatuses). Observable so the grid recolours the Step cell when it lands. A step's
    // outcome is NOT the statement's own result: a Success statement can still be RolledBack.
    [NotifyPropertyChangedFor(nameof(IsStepCommitted))]
    [NotifyPropertyChangedFor(nameof(IsStepRolledBack))]
    [NotifyPropertyChangedFor(nameof(StepStatusTooltip))]
    [ObservableProperty] private ScriptStepStatus _stepStatus;

    public bool IsStepCommitted => StepStatus == ScriptStepStatus.Committed;
    public bool IsStepRolledBack => StepStatus == ScriptStepStatus.RolledBack;
    public string StepStatusTooltip => StepStatus switch
    {
        ScriptStepStatus.Committed => UiStrings.ScriptStepCommittedTooltip,
        ScriptStepStatus.RolledBack => UiStrings.ScriptStepRolledBackTooltip,
        _ => UiStrings.ScriptColumnStepTooltip,
    };

    public string StepText { get; }
    public string Statement { get; }
    public string TypeText { get; }
    public string Result { get; }
    public bool IsFailed { get; }
    /// <summary>A statement a stop-on-error / cancellation left unexecuted (Sequenced). Neither
    /// succeeded nor failed — shown muted. See <see cref="IsSucceeded"/>.</summary>
    public bool IsNotRun { get; }
    /// <summary>Actually succeeded — excludes the not-run rows, so the Result cell colours "OK"
    /// green only for a real success and shows "Not run" muted (never green) otherwise.</summary>
    public bool IsSucceeded => !IsFailed && !IsNotRun;
    /// <summary>Explains a not-run row on the Result cell; null for executed rows (no tooltip).</summary>
    public string? ResultTooltip => IsNotRun ? UiStrings.ScriptResultNotRunTooltip : null;
    public string RowsText { get; }
    public string Duration { get; }
    public string Error { get; }
    public int SourceOffset { get; }
    public int SourceLength { get; }
    public bool HasSourceRange => SourceOffset >= 0;

    private static string Elide(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (flat.Contains("  ", StringComparison.Ordinal)) flat = flat.Replace("  ", " ");
        return flat.Length > PreviewMaxLength ? flat.Substring(0, PreviewMaxLength) + "…" : flat;
    }

    // Technical SQL-category labels (not translatable prose).
    private static string KindLabel(ScriptStatementKind kind) => kind switch
    {
        ScriptStatementKind.Ddl => "DDL",
        ScriptStatementKind.Dml => "DML",
        ScriptStatementKind.Select => "SELECT",
        ScriptStatementKind.ExecuteProcedure => "EXECUTE PROCEDURE",
        ScriptStatementKind.ExecuteBlock => "EXECUTE BLOCK",
        ScriptStatementKind.TransactionControl => "TRANSACTION",
        ScriptStatementKind.SessionControl => "SESSION",
        _ => "—",
    };

    private static string FormatRows(ScriptStatementResult r)
    {
        var count = r.RowCount ?? r.RecordsAffected;
        return count is { } n ? n.ToString(CultureInfo.CurrentCulture) : string.Empty;
    }
}
