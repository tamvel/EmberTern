using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Controls;
using EmberTern.App.Settings;
using EmberTern.Core.Settings.Export;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The export dialog: which sections to include, the passphrase, and the write.
///
/// <para><b>⚠ This is a command, not a preference, so it is NOT apply-on-change</b> — and that does not
/// contradict ratified Q8, which is about the preference pages. An export happens once, when the user says so,
/// and it has an outcome to report; there is nothing to apply continuously.</para>
///
/// <para>⭐ <b>The section checkboxes start from <see cref="SettingsExportOptions"/>' own defaults, which ARE the
/// ratified content classification (§6.3.4).</b> Not re-listed here: a second copy of "what should normally
/// travel" is a second answer to a rule #11 question, and the one that drifts is the one nobody is testing.</para>
/// </summary>
public sealed partial class SettingsExportDialogViewModel : ObservableObject
{
    private readonly SettingsPortability _portability;

    public SettingsExportDialogViewModel(SettingsPortability portability)
    {
        _portability = portability;

        var defaults = new SettingsExportOptions();
        _preferences = defaults.Preferences;
        _gridProfiles = defaults.GridProfiles;
        _folders = defaults.Folders;
        _connections = defaults.Connections;
        _passwords = defaults.Passwords;
        _workspaces = defaults.Workspaces;
        _importProfiles = defaults.ImportProfiles;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _preferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _gridProfiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _folders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(CanIncludePasswords))]
    private bool _connections;

    [ObservableProperty]
    private bool _passwords;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _workspaces;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _importProfiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private string _passphrase = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private string _passphraseConfirmation = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private MessageSeverity _messageSeverity = MessageSeverity.Info;

    [ObservableProperty]
    private bool _showMessage;

    /// <summary>True when the export finished — the view closes on it.</summary>
    [ObservableProperty]
    private bool _completed;

    /// <summary>A password on a connection needs a connection to be attached to (§6.3.4).</summary>
    public bool CanIncludePasswords => Connections;

    /// <summary>The current selection as the Core type. One place, so nothing can select a state Core refuses.</summary>
    public SettingsExportOptions Options => new()
    {
        Preferences = Preferences,
        GridProfiles = GridProfiles,
        Folders = Folders,
        Connections = Connections,
        Passwords = Passwords,
        Workspaces = Workspaces,
        ImportProfiles = ImportProfiles,
    };

    /// <summary>
    /// Whether the Export button may run. ⚠ The passphrase confirmation is part of the gate rather than a
    /// warning afterwards: a mistyped passphrase produces a file that is <b>permanently</b> unreadable, and the
    /// mistake cannot be detected later by anyone — so the only place to catch it is before the file exists.
    /// </summary>
    public bool CanExport => !Options.IsEmpty
                             && !string.IsNullOrEmpty(Passphrase)
                             && string.Equals(Passphrase, PassphraseConfirmation, StringComparison.Ordinal);

    /// <summary>Why <see cref="CanExport"/> is false, for the hint under the buttons — or empty when it is
    /// true.</summary>
    public string BlockedReason
    {
        get
        {
            if (Options.IsEmpty) return UiStrings.SettingsExportNothingSelected;
            if (string.IsNullOrEmpty(Passphrase)) return UiStrings.SettingsExportPassphraseMissing;
            if (!string.Equals(Passphrase, PassphraseConfirmation, StringComparison.Ordinal))
            {
                return UiStrings.SettingsExportPassphraseMismatch;
            }
            return string.Empty;
        }
    }

    partial void OnPassphraseChanged(string value) => OnPropertyChanged(nameof(BlockedReason));

    partial void OnPassphraseConfirmationChanged(string value) => OnPropertyChanged(nameof(BlockedReason));

    partial void OnConnectionsChanged(bool value)
    {
        // Unchecking the connections leaves nothing for a password to belong to, so the opt-in follows it down
        // rather than staying checked over an empty section.
        if (!value) Passwords = false;
        OnPropertyChanged(nameof(BlockedReason));
    }

    /// <summary>
    /// Writes the export to <paramref name="path"/>. The view supplies the path from a save picker; this method
    /// owns the outcome message.
    /// </summary>
    public void ExportTo(string path)
    {
        if (!CanExport)
        {
            return;
        }

        try
        {
            _portability.ExportTo(path, Options, Passphrase);
            Message = string.Format(
                CultureInfo.CurrentCulture, UiStrings.SettingsExportDoneFormat, Path.GetFileName(path));
            MessageSeverity = MessageSeverity.Success;
            ShowMessage = true;
            Completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            Message = string.Format(
                CultureInfo.CurrentCulture, UiStrings.SettingsExportFailedFormat, ex.Message);
            MessageSeverity = MessageSeverity.Error;
            ShowMessage = true;
        }
    }
}
