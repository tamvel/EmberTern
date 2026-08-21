using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The Storage surface: where the register lives, how to back it up, and how to restore one somewhere
/// else.
///
/// <para>⭐⭐ <b>Its own window, reached deliberately</b> (D‑4). It is not a third view tab — the two tabs
/// answer two questions about LICENCES, and file operations are not a third one of those — and it is not
/// a card on the customers view, which already carries four sections about a customer. ⚠ The separation
/// is also a safety property: restore is the most consequential action in this application, and it should
/// take a decision to reach rather than sit one click from daily work.</para>
///
/// <para>⛔ <b>Restore never writes over the active register, and this view model cannot make it.</b> It
/// hands <see cref="RestoreWorkflow"/> a folder the operator chose, and that class refuses the active
/// register's own folder and any folder that is not empty. The warning text below explains the rule; it
/// is not what enforces it.</para>
///
/// <para>⚠ <b>No Avalonia types (Architecture rule 1).</b> The four things this needs from the platform —
/// a save dialog, an open dialog, a folder dialog and "show this folder" — arrive as delegates the view
/// assigns, exactly as <see cref="ShellViewModel.SaveFilePicker"/> does.</para>
/// </summary>
public sealed partial class StorageViewModel : MessageHostViewModel
{
    private readonly BackupWorkflow _backups;
    private readonly RestoreWorkflow _restores;
    private readonly ManagerPaths _paths;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates the view model.</summary>
    public StorageViewModel(
        LicenseRegister register, ManagerPaths paths, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _backups = new BackupWorkflow(register, _clock);

        // ⭐ The restorer is given the active folder so it can REFUSE it, and is given no register at
        //    all. That is D‑5 made structural rather than remembered — see RestoreWorkflow.
        _restores = new RestoreWorkflow(_paths.Root, _clock);

        // ⭐ The safe mode, taken FROM the offered list rather than built again.
        // ⚠ Safe by language rule, not by luck: every instance initializer — RestoreModes included — runs
        //   before a constructor BODY, whatever order the members appear in the file.
        _selectedRestoreMode = RestoreModes[0];

        Counts = SnapshotCounts.Read(register);
    }

    // ── The two tasks ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which of the two tasks is showing. ⭐ Backup first — it is what an operator opens this window to
    /// do, and it is the only one of the two that is routine.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestoreTab))]
    private bool _isBackupTab = true;

    /// <summary>The restore task.</summary>
    public bool IsRestoreTab => !IsBackupTab;

    /// <summary>Shows the backup task.</summary>
    [RelayCommand]
    private void ShowBackup() => IsBackupTab = true;

    /// <summary>Shows the restore task.</summary>
    [RelayCommand]
    private void ShowRestore() => IsBackupTab = false;

    /// <summary>Asks where to save. Takes a suggested file name; returns the path or <see langword="null"/>.</summary>
    public Func<string, Task<string?>>? SaveFilePicker { get; set; }

    /// <summary>Asks which backup to open. Returns the path or <see langword="null"/>.</summary>
    public Func<Task<string?>>? OpenBackupPicker { get; set; }

    /// <summary>Asks which folder to restore INTO. Returns the path or <see langword="null"/>.</summary>
    public Func<Task<string?>>? RestoreFolderPicker { get; set; }

    /// <summary>Shows a folder to the operator in their file manager.</summary>
    public Action<string>? FolderOpener { get; set; }

    // ── What is here ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The folder holding both files.</summary>
    public string DataFolder => _paths.Root;

    /// <summary>The register of record.</summary>
    public string RegisterPath => _paths.Register;

    /// <summary>
    /// ⛔ The keystore's path — shown so the operator knows it exists and is a SEPARATE thing to look
    /// after. It is deliberately not part of any backup this window takes (§12.3, §24.2).
    /// </summary>
    public string KeyStorePath => _paths.KeyStore;

    /// <summary>What the register currently holds.</summary>
    public SnapshotCounts Counts { get; }

    /// <summary>The register's schema version.</summary>
    public int SchemaVersion { get; } = LicenseRegister.CurrentSchemaVersion;

    /// <summary>One sentence naming everything a backup will carry.</summary>
    public string BackupContents =>
        $"{Counts.Customers} customer(s) · {Counts.Licenses} licence(s) · " +
        $"{Counts.Artifacts} issued artifact(s), the whole history · " +
        $"{Counts.CurrentPointers} current-artifact pointer(s) · {Counts.AuditEntries} audit entries.";

    // ── Backup ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The backup's own passphrase. ⛔ Never the keystore's (D‑1).</summary>
    [ObservableProperty]
    private string _backupPassphrase = string.Empty;

    /// <summary>Typed again, because a mistyped backup passphrase is discovered years later.</summary>
    [ObservableProperty]
    private string _backupPassphraseConfirmation = string.Empty;

    /// <summary>Takes a verified encrypted backup of the whole register.</summary>
    [RelayCommand]
    private async Task BackupAsync()
    {
        if (BackupPassphrase.Length < 12)
        {
            // ⚠ A length floor, not a complexity ruleset — the same standard the keystore ceremony
            //    applies, for the same reason: this passphrase is the only thing between anyone holding
            //    the file and every customer's details.
            Message = StatusMessage.Warning(StatusCatalog.BackupPassphraseHint);
            return;
        }

        if (!string.Equals(BackupPassphrase, BackupPassphraseConfirmation, StringComparison.Ordinal))
        {
            Message = StatusMessage.Warning(StatusCatalog.PassphrasesDoNotMatch);
            return;
        }

        if (SaveFilePicker is null)
        {
            return;
        }

        var suggested =
            $"EmberTern-register-{_clock():yyyy-MM-dd}{RegisterBackup.FileExtension}";
        var path = await SaveFilePicker(suggested).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var report = _backups.CreateBackup(path, BackupPassphrase);

            BackupPassphrase = string.Empty;
            BackupPassphraseConfirmation = string.Empty;

            Message = StatusMessage.Success(
                StatusCatalog.BackupWritten,
                report.Path, report.Customers, report.Licenses, report.Artifacts, report.AuditEntries);
        }
        catch (RegisterIntegrityException e)
        {
            Message = StatusMessage.FromError(e, MessageSeverity.Error);
        }
        catch (IOException e)
        {
            Message = StatusMessage.Error(StatusCatalog.BackupNotWritten, e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            Message = StatusMessage.Error(StatusCatalog.BackupNotWritten, e.Message);
        }
    }

    // ── JSONL ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Writes the whole register as plain, readable JSON Lines.</summary>
    [RelayCommand]
    private async Task ExportJsonlAsync()
    {
        if (SaveFilePicker is null)
        {
            return;
        }

        var suggested = $"EmberTern-register-{_clock():yyyy-MM-dd}{RegisterJsonl.FileExtension}";
        var path = await SaveFilePicker(suggested).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var report = _backups.ExportJsonl(path);

            // ⚠ The warning is part of the success message, not a separate dialog nobody reads. The file
            //    holds every issued token in the clear, and the operator has just chosen where to put it.
            Message = StatusMessage.Warning(StatusCatalog.JsonlExportWritten, report.Path, report.Lines);
        }
        catch (IOException e)
        {
            Message = StatusMessage.Error(StatusCatalog.ExportNotWritten, e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            Message = StatusMessage.Error(StatusCatalog.ExportNotWritten, e.Message);
        }
    }

    // ── Restore ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The passphrase of the backup being restored.</summary>
    [ObservableProperty]
    private string _restorePassphrase = string.Empty;

    /// <summary>
    /// What replacing the active register does. ⚠ It describes the rule; it is not what enforces it —
    /// <see cref="RestoreWorkflow"/> is, by preserving and by verifying twice.
    /// </summary>
    public string ReplaceRule =>
        "The current register will be preserved before restore. " +
        $"It is moved to {ManagerPaths.RegisterFileName}.replaced-<date-time> in the same folder and is " +
        "never deleted, so a failed restore always leaves you the register you started with. " +
        "⚠ The License Manager closes when this succeeds — start it again to work on the restored register.";

    /// <summary>
    /// What restoring elsewhere does. ⛔ The active register is not touched, not even a history entry.
    /// </summary>
    public string RestoreElsewhereRule =>
        "The active register will not be changed. " +
        $"The backup is restored into a NEW, empty folder of your choosing; nothing is written into " +
        $"{DataFolder}, and no history entry is added. For recovering or inspecting a backup while you " +
        "carry on working.";

    /// <summary>
    /// ⭐ Closes the running application's register and reports whether it let go. Assigned by the
    /// composition root, because only it owns that register — ⛔ this view model must not.
    /// </summary>
    public Func<bool>? ActiveRegisterCloser { get; set; }

    /// <summary>⭐ Shuts the application down after a successful replace (D‑6).</summary>
    public Action? ShutdownRequested { get; set; }

    /// <summary>
    /// The two restore modes, as the picker offers them.
    ///
    /// <para>⭐ ONE list, so the label an operator picks and the branch that runs cannot disagree. ⛔ The
    /// modes are not two buttons: with two buttons the consequence text would have to describe both at
    /// once, which is how the previous layout made the dangerous one easy to miss.</para>
    /// </summary>
    public IReadOnlyList<RestoreModeOption> RestoreModes { get; } =
    [
        new(false),
        new(true),
    ];

    /// <summary>
    /// Which mode the single Restore action will run. ⭐ Defaults to the SAFE one — the mode that cannot
    /// touch the working register is the one an operator should have to choose to leave.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ Assigned in the constructor FROM <see cref="RestoreModes"/>, never constructed a second time.
    /// It used to be its own <c>new(false, "Restore to another location")</c> — a duplicate of the list's
    /// first entry, which meant the default selection and the offered option were two objects whose
    /// equality depended on two literals staying identical.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreConsequence))]
    [NotifyPropertyChangedFor(nameof(IsReplacingActiveRegister))]
    private RestoreModeOption _selectedRestoreMode = null!;

    /// <summary>True when the picked mode replaces the active register.</summary>
    public bool IsReplacingActiveRegister => SelectedRestoreMode?.ReplacesActiveRegister ?? false;

    /// <summary>
    /// What the CURRENTLY PICKED mode will do, in full.
    ///
    /// <para>⭐ One paragraph that changes with the choice, rather than two paragraphs shown at once. The
    /// operator reads the consequence of the thing they are about to do, not a comparison they have to
    /// resolve themselves.</para>
    /// </summary>
    public string RestoreConsequence => IsReplacingActiveRegister ? ReplaceRule : RestoreElsewhereRule;

    /// <summary>
    /// Runs the picked restore mode.
    ///
    /// <para>⭐⭐ <b>Routing only.</b> It calls one of the two existing methods unchanged — there is no
    /// third path, no new workflow, and no second way into a register. ⛔ Do not fold the two into one
    /// method here: they refuse on different grounds and end differently, and the difference is the
    /// whole point of the choice above.</para>
    /// </summary>
    [RelayCommand]
    private Task RunRestoreAsync() =>
        IsReplacingActiveRegister ? ReplaceActiveRegisterAsync() : RestoreAsync();

    /// <summary>
    /// Replaces the ACTIVE register with a backup, keeping the current one.
    ///
    /// <para>⛔ The register is closed before the workflow runs, and the application is shut down after
    /// it succeeds. There is no hot swap: re-pointing the running view models at a different register is
    /// a separate stage (D‑6).</para>
    /// </summary>
    [RelayCommand]
    private async Task ReplaceActiveRegisterAsync()
    {
        var prepared = await PrepareRestoreAsync().ConfigureAwait(true);
        if (prepared is null)
        {
            return;
        }

        // ⚠ The register is closed HERE, before anything is moved — and the workflow still proves the
        //    file is free rather than believing this call worked.
        if (ActiveRegisterCloser is { } close && !close())
        {
            Message = StatusMessage.Error(StatusCatalog.RegisterNotClosed);
            return;
        }

        try
        {
            var report = _restores.RestoreOverActiveRegister(prepared, RestorePassphrase);

            RestorePassphrase = string.Empty;

            Message = StatusMessage.Success(
                StatusCatalog.RegisterReplaced,
                report.Counts.Customers, report.Counts.Licenses, report.Counts.Artifacts,
                report.Counts.AuditEntries, report.PreservedRegisterPath);

            ShutdownRequested?.Invoke();
        }
        catch (Exception e) when (Explain(e) is { } text)
        {
            // ⚠⚠ The register was already CLOSED before this ran, so whatever went wrong, this
            //    application no longer has one — every other window is now looking at nothing. The
            //    operator has to be told that, and told it is safe: the workflow's contract is that the
            //    register ON DISK is either the restored one or the one they started with, never a half
            //    state. ⛔ Deliberately NOT shutting down here — the message is the only place they can
            //    learn what happened, and a shutdown would take it off the screen before it was read.
            Message = StatusMessage.Error(StatusCatalog.RegisterClosedAndMustRestart, text);
        }
    }

    /// <summary>Restores a backup into a new folder, leaving the active register alone.</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (RestoreFolderPicker is null)
        {
            return;
        }

        var prepared = await PrepareRestoreAsync().ConfigureAwait(true);
        if (prepared is null)
        {
            return;
        }

        var target = await RestoreFolderPicker().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            var report = _restores.Restore(prepared, RestorePassphrase, target);

            RestorePassphrase = string.Empty;

            Message = StatusMessage.Success(
                StatusCatalog.RestoredElsewhere,
                report.Directory, report.Counts.Customers, report.Counts.Licenses,
                report.Counts.Artifacts, report.Counts.AuditEntries, RegisterPath);
        }
        catch (Exception e) when (Explain(e) is { } text)
        {
            Message = StatusMessage.Error(text.Key, [.. text.Arguments]);
        }
    }

    /// <summary>
    /// Everything both restore modes ask for before they diverge: which file, is it a backup at all, and
    /// is there a passphrase to try.
    ///
    /// <para>⭐ Shared so the two modes cannot start disagreeing about what a readable backup is, or about
    /// when the operator is told that they picked the wrong file. Returns <see langword="null"/> when the
    /// operator cancelled or has already been told what is wrong.</para>
    /// </summary>
    private async Task<byte[]?> PrepareRestoreAsync()
    {
        if (OpenBackupPicker is null)
        {
            return null;
        }

        var backupPath = await OpenBackupPicker().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return null;
        }

        byte[] backup;
        try
        {
            backup = File.ReadAllBytes(backupPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = StatusMessage.Error(StatusCatalog.BackupNotRead, e.Message);
            return null;
        }

        // ⭐ Read what the file claims BEFORE asking for anything else, so an operator who picked the
        //    wrong file is told immediately rather than after typing a passphrase.
        BackupHeader header;
        try
        {
            header = RestoreWorkflow.Inspect(backup);
        }
        catch (BackupException e)
        {
            Message = StatusMessage.Error(Describe(e).Key);
            return null;
        }

        if (RestorePassphrase.Length == 0)
        {
            Message = StatusMessage.Warning(
                StatusCatalog.BackupInspected,
                header.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                header.SchemaVersion);
            return null;
        }

        return backup;
    }

    // ⭐ ONE place turns a restore failure into words, so the two modes cannot describe the same
    //   condition differently. ⚠ Returns null for anything it does not claim to explain, which lets the
    //   `when` filter leave a genuinely unexpected exception unhandled rather than swallowing it.
    private static LocalizedText? Explain(Exception error) => error switch
    {
        BackupException backup => Describe(backup),

        // ⭐ The problems are listed, never summarised away: an operator deciding whether a backup is
        //    salvageable needs to know WHAT disagreed.
        // ⚠ Both halves are OURS and both resolve at read time: the refusal carries its own key (the
        //   refusal ENUM could not serve — see RestoreRefusedException's remarks), and each problem is a
        //   whole sentence of its own. `LocalizedSentences` joins them at format time, so the count may
        //   vary without any of them being frozen.
        RestoreRefusedException refused => refused.Problems.Count == 0
            ? new LocalizedText(refused.Key)
            : new LocalizedText(
                StatusCatalog.RestoreRefusedWithProblems,
                new LocalizedText(refused.Key),
                new LocalizedSentences(refused.Problems)),

        IOException io => new LocalizedText(StatusCatalog.RestoreNotCompleted, io.Message),
        UnauthorizedAccessException access =>
            new LocalizedText(StatusCatalog.RestoreNotCompleted, access.Message),
        _ => null,
    };

    // ── The folder ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the data folder in the operator's file manager.
    ///
    /// <para>⭐ The answer to a question first run deliberately refused to answer (§36.5): where these
    /// files are is administrative, and this is the administrative surface.</para>
    /// </summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            _paths.EnsureFolder();
            FolderOpener?.Invoke(_paths.Root);
        }
        catch (IOException e)
        {
            Message = StatusMessage.Error(StatusCatalog.DataFolderNotOpened, e.Message);
        }
    }

    // ⭐ One place turns a backup failure into words, so the restore path cannot describe the same
    //   condition differently from the inspect path.
    // ⚠ It maps the CLASSIFIED failure to a key (L8.2). The classification is what makes that possible —
    //   the same reason UnlockViewModel can key its keystore failures instead of printing a message.
    internal static LocalizedText Describe(BackupException error) => error.Failure switch
    {
        BackupFailure.WrongPassphrase => new LocalizedText(StatusCatalog.BackupWrongPassphrase),
        BackupFailure.NotABackup => new LocalizedText(StatusCatalog.BackupNotABackup),
        BackupFailure.UnsupportedVersion => new LocalizedText(StatusCatalog.BackupFromNewerBuild),
        BackupFailure.UnsupportedScheme => new LocalizedText(StatusCatalog.BackupUnsupportedScheme),
        BackupFailure.Corrupt => new LocalizedText(StatusCatalog.BackupCorrupt),
        _ => new LocalizedText(StatusCatalog.BackupNotOpened, error.Failure),
    };
}

/// <summary>
/// One of the two restore modes, as the picker offers it.
///
/// <para>⭐ The branch that runs is decided by the object the operator picked — never by comparing the
/// label's text.</para>
///
/// <para>⭐⭐ <b>And the label is therefore NOT a member.</b> A <c>record</c> compares by every positional
/// member and <c>ComboBox.SelectedItem</c> matches by equality, so a label inside the identity would tie
/// the selection to the language the option was built in. ⚠ This one carried the worst version of that
/// problem: the default selection was a SECOND, independently constructed
/// <c>new(false, "Restore to another location")</c>, so the same mode existed twice as two unequal values
/// and the safe default depended on two literals agreeing. ⛔ Do not put a word back in here.</para>
/// </summary>
/// <param name="ReplacesActiveRegister">
/// <see langword="true"/> for the mode that replaces the working register (preserving it first).
/// ⭐ This flag IS the option's identity.
/// </param>
public sealed record RestoreModeOption(bool ReplacesActiveRegister)
{
    /// <summary>What the picker shows. ⭐ Resolved at read time, from one place.</summary>
    public string Label => LabelFor(ReplacesActiveRegister);

    /// <summary>
    /// The one place either mode is named.
    /// </summary>
    /// <remarks>⚠ A property-shaped body — in L8 it becomes a lookup, and nothing else changes.</remarks>
    internal static string LabelFor(bool replacesActiveRegister) =>
        replacesActiveRegister ? "Replace active register" : "Restore to another location";
}
