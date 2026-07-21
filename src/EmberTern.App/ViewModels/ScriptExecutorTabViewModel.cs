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

        _lastStatements = statements;
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
        var row = new ScriptResultRowViewModel(result, offset, length);
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

    private bool PassesFilter(ScriptResultRowViewModel row) => SelectedFilterIndex switch
    {
        1 => !row.IsFailed,
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

/// <summary>One row in the Script Executor results grid.</summary>
public sealed class ScriptResultRowViewModel
{
    private const int PreviewMaxLength = 100;

    public ScriptResultRowViewModel(ScriptStatementResult result, int sourceOffset, int sourceLength)
    {
        Line = result.Index + 1;
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

    public int Line { get; }
    public string Statement { get; }
    public string TypeText { get; }
    public string Result { get; }
    public bool IsFailed { get; }
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
