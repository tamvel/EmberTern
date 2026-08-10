using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Controls;
using EmberTern.App.Localization;
using EmberTern.App.Settings;
using EmberTern.Core.Localization;
using EmberTern.Core.Settings.Export;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The import dialog: pick a file, see what it is, supply the passphrase, choose what to take, write it.
///
/// <para><b>⭐ The order of those steps is the whole design, and it is enforced by Core's API rather than by this
/// class's discipline (§6.3.3 / §15.3b, ratified).</b> <see cref="PickFile"/> runs phase one —
/// <c>SettingsImportReader.Inspect</c>, which takes <i>no</i> passphrase — and <see cref="CanEnterPassphrase"/> is
/// false unless that phase said the file is one we could open given the right credential. So a PDF, a
/// <c>settings.dat</c>, or a file from a newer build is rejected with its own distinct message and <b>the
/// passphrase field never appears</b>. ⛔ Do not "simplify" this by asking for the passphrase up front: a
/// passphrase prompt is an implicit claim that the file is readable given the right one, and
/// <c>SettingsImportReader.Open</c> takes an inspection precisely so the claim cannot be made falsely.</para>
///
/// <para>⚠ Failure text comes from Core and is shown as-is. The <c>SettingsImportStatus</c> is the stable half
/// this class switches on; duplicating the words in <c>UiStrings</c> would be two answers to one question
/// (§15.8).</para>
///
/// <para>⭐ <b>Since C4b that text arrives as a <c>LocalizableMessage</c> and is resolved HERE, at the moment of
/// display</b> (D‑3) — never captured earlier. One helper, <see cref="Say"/>, does it for all three surfaces and
/// falls back to Core's English when a producer has none, so an unmigrated path degrades to exactly today's
/// behaviour rather than to a blank bar.</para>
/// </summary>
public sealed partial class SettingsImportDialogViewModel : ObservableObject
{
    private readonly SettingsPortability _portability;
    private SettingsImportInspection? _inspection;
    private SettingsExportContent? _content;

    public SettingsImportDialogViewModel(SettingsPortability portability)
    {
        _portability = portability;
    }

    // ---- Step 1: the file --------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private string _fileName = string.Empty;

    public bool HasFile => !string.IsNullOrEmpty(FileName);

    // ---- Step 2: the passphrase --------------------------------------------------

    /// <summary>
    /// ⭐ True only once phase one has said this file is ours, of a version we support, encrypted in a way we can
    /// handle. Everything about "never ask for a credential that cannot possibly work" reduces to this one
    /// binding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private bool _canEnterPassphrase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private string _passphrase = string.Empty;

    public bool CanOpen => CanEnterPassphrase && !string.IsNullOrEmpty(Passphrase);

    // ---- Step 3: what the file holds, and what to take ---------------------------

    /// <summary>True once the payload has been decrypted — the cue to show the section list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(Blocked))]
    private bool _isOpened;

    /// <summary>⚠ Each section is offered only when the file actually carries it. A checkbox for a section that
    /// is not in the file would be a control that cannot do anything, and it would misrepresent the file's
    /// contents — which is the one thing this step exists to show.</summary>
    [ObservableProperty] private bool _offersPreferences;
    [ObservableProperty] private bool _offersGridProfiles;
    [ObservableProperty] private bool _offersFolders;
    [ObservableProperty] private bool _offersConnections;
    [ObservableProperty] private bool _offersPasswords;
    [ObservableProperty] private bool _offersWorkspaces;
    [ObservableProperty] private bool _offersImportProfiles;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takePreferences;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takeGridProfiles;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takeFolders;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takeConnections;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takePasswords;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takeWorkspaces;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))][NotifyPropertyChangedFor(nameof(Blocked))] private bool _takeImportProfiles;

    /// <summary>Shown only when this file carries passwords — an import overwrites the stored one for a matching
    /// connection, which is worth saying before it happens.</summary>
    public bool ShowPasswordNote => OffersPasswords;

    /// <summary>The current choice as the Core type — one place, so no view can express a state Core would have
    /// to guess about.</summary>
    public SettingsImportSelection Selection => new()
    {
        Preferences = TakePreferences,
        GridProfiles = TakeGridProfiles,
        Folders = TakeFolders,
        Connections = TakeConnections,
        Passwords = TakePasswords,
        Workspaces = TakeWorkspaces,
        ImportProfiles = TakeImportProfiles,
    };

    public bool CanImport => IsOpened && !Selection.IsEmpty;

    /// <summary>
    /// Why <see cref="CanImport"/> is false — but <b>only for the one state a user can reach by mistake</b>: the
    /// file is open and every section has been unticked, so Import is dead with nothing on screen saying why.
    ///
    /// <para>⚠ Deliberately silent before the file is open. The steps ahead of that are visible in the dialog
    /// itself (choose a file → the passphrase group appears → the contents appear), and a line saying "choose a
    /// file" under a dialog whose first control is <i>Choose file…</i> is noise. Premature validation is its own
    /// UX defect; this exists for the state that genuinely looks broken. Same severity vocabulary as the export's
    /// — see <see cref="DialogGateHint"/>.</para>
    /// </summary>
    public DialogGateHint Blocked => IsOpened && Selection.IsEmpty
        ? DialogGateHint.Error(UiStrings.SettingsImportNothingSelected)
        : DialogGateHint.None;

    // ---- Outcome ----------------------------------------------------------------

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private MessageSeverity _messageSeverity = MessageSeverity.Info;
    [ObservableProperty] private bool _showMessage;

    /// <summary>True once settings were written — the view stops offering Import and the user closes.</summary>
    [ObservableProperty]
    private bool _completed;

    // ---- The three steps --------------------------------------------------------

    /// <summary>Phase one. Runs the moment a file is chosen, so its verdict is what decides whether a passphrase
    /// is ever asked for.</summary>
    public void PickFile(string path)
    {
        Reset();
        FileName = Path.GetFileName(path);

        var inspection = _portability.Inspect(path);
        _inspection = inspection;

        if (inspection.CanBeOpened)
        {
            CanEnterPassphrase = true;
            Message = string.Empty;
            ShowMessage = false;
            return;
        }

        // Core's own words for its own status — one of the six distinct outcomes of §6.3.3, and the reason the
        // ordered checks were worth building.
        CanEnterPassphrase = false;
        Message = Say(inspection.Localized, inspection.Message);
        MessageSeverity = MessageSeverity.Error;
        ShowMessage = true;
    }

    /// <summary>Phase two — decrypt, migrate, and show what the file holds.</summary>
    public void Open()
    {
        if (_inspection is not { } inspection || !CanOpen)
        {
            return;
        }

        var result = _portability.Open(inspection, Passphrase);
        if (!result.IsUsable || result.Content is null)
        {
            Message = Say(result.Localized, result.Message);
            MessageSeverity = MessageSeverity.Error;
            ShowMessage = true;
            return;
        }

        _content = result.Content;
        Offer(result.Content);
        IsOpened = true;
        Message = string.Empty;
        ShowMessage = false;
    }

    /// <summary>Phase three — merge the chosen sections into <c>settings.dat</c> and bring the app into step.</summary>
    public void ApplySelected()
    {
        if (_content is not { } content || !CanImport)
        {
            return;
        }

        var result = _portability.Apply(content, Selection);
        if (!result.Applied)
        {
            // ⚠ Includes the store's refusal (§2.5 / audit A-03), which an import must surface for exactly the
            // reason Settings Center must: a surface that accepts the instruction and writes nothing is the worst
            // possible place for that silence. ⭐ That refusal arrives as the STORE's own localizable message,
            // forwarded through the applier — the same sentence Settings Center shows, from the same key.
            Message = Say(result.Localized, result.Message);
            MessageSeverity = MessageSeverity.Warning;
            ShowMessage = true;
            return;
        }

        var sections = string.Join(", ", result.AppliedSections);
        Message = result.PreservedAt is { } preserved
            ? string.Format(
                CultureInfo.CurrentCulture, UiStrings.SettingsImportDoneFormat,
                sections, Path.GetFileName(preserved))
            : string.Format(CultureInfo.CurrentCulture, UiStrings.SettingsImportDoneNoBackupFormat, sections);
        MessageSeverity = MessageSeverity.Success;
        ShowMessage = true;
        Completed = true;
    }

    /// <summary>
    /// Core's verdict in the reader's language, resolved at the moment of display (D‑3).
    ///
    /// <para>⚠ The English half is the fallback, not the source: a producer that has no key yet still shows
    /// today's sentence instead of nothing. ⛔ Do not invert this — resolving the key first is what makes a new
    /// language reach this dialog with no change here.</para>
    ///
    /// <para>⭐ <b>Why the composed text may be stored in <c>Message</c> without freezing in one language, which
    /// is the #353 trap this would otherwise walk into — measured, not assumed:</b> the language preference has
    /// exactly one writer in the app (Settings Center's Language row), this dialog is opened with
    /// <c>ShowDialog</c> over that very window, so <b>the language cannot change while it is on screen</b>. And
    /// in the one case where an import itself changes it — a file whose Preferences carry another language —
    /// <c>SettingsPortability.Apply</c> reloads the preferences (which switches <c>Loc</c>) <i>before</i>
    /// returning, so composition already happens in the new language. Same "correct by ordering" as Settings
    /// Center's save-refusal banner. ⛔ If this dialog ever becomes non-modal, that reasoning lapses and the
    /// message needs recomposing from a language hook.</para>
    /// </summary>
    private static string Say(LocalizableMessage? localized, string english)
        => localized is { } message ? Loc.Format(message) : english;

    private void Offer(SettingsExportContent content)
    {
        var everything = SettingsImportSelection.EverythingIn(content);

        OffersPreferences = everything.Preferences;
        OffersGridProfiles = everything.GridProfiles;
        OffersFolders = everything.Folders;
        OffersConnections = everything.Connections;
        OffersPasswords = everything.Passwords;
        OffersWorkspaces = everything.Workspaces;
        OffersImportProfiles = everything.ImportProfiles;
        OnPropertyChanged(nameof(ShowPasswordNote));

        // Pre-selected to everything the file carries: the user picked this file in order to take what is in it,
        // so unchecking is the exception. Nothing is hidden by that — every box is visible and unticking one is
        // one click.
        TakePreferences = everything.Preferences;
        TakeGridProfiles = everything.GridProfiles;
        TakeFolders = everything.Folders;
        TakeConnections = everything.Connections;
        TakePasswords = everything.Passwords;
        TakeWorkspaces = everything.Workspaces;
        TakeImportProfiles = everything.ImportProfiles;
    }

    partial void OnTakeConnectionsChanged(bool value)
    {
        // Same rule as the export's: a password needs a connection to belong to.
        if (!value) TakePasswords = false;
    }

    private void Reset()
    {
        _inspection = null;
        _content = null;
        IsOpened = false;
        CanEnterPassphrase = false;
        Passphrase = string.Empty;
        Completed = false;

        OffersPreferences = OffersGridProfiles = OffersFolders = OffersConnections =
            OffersPasswords = OffersWorkspaces = OffersImportProfiles = false;
        TakePreferences = TakeGridProfiles = TakeFolders = TakeConnections =
            TakePasswords = TakeWorkspaces = TakeImportProfiles = false;

        OnPropertyChanged(nameof(ShowPasswordNote));
    }
}
