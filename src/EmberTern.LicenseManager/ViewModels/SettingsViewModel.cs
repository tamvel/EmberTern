using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One transport choice, as the picker shows it.
///
/// <para>⭐⭐ <b>Its identity is the VALUE alone; the label is a projection.</b> A <c>record</c> compares
/// by every positional member, so a label inside it would put the CURRENT LANGUAGE into the option's
/// identity — and <c>ComboBox.SelectedItem</c> matches by equality. Rebuild the list in another language
/// and the selected option equals nothing in it, so the picker silently blanks. ⛔ Do not add a label,
/// a description or any other word as a member here.</para>
/// </summary>
/// <param name="Value">What gets stored, and what this option IS.</param>
public sealed record SmtpSecurityOption(SmtpSecurity Value)
{
    /// <summary>What the operator reads. ⭐ Resolved at read time, from the one catalog that owns it.</summary>
    public string Label => ManagerSettingsCatalog.SecurityLabel(Value);

    /// <summary>
    /// The caption a picker binds to. ⭐ Notifying, so the label follows a language change.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ A picker binds <b>this</b>, never <see cref="Label"/> directly — measured: an option record
    /// raises no <c>PropertyChanged</c>, so a <c>ComboBox</c> bound straight to a label renders correctly
    /// on load and then freezes in that language. See <see cref="LocalizedCaption"/>.
    /// </remarks>
    public LocalizedCaption Caption => new(() => Label);
}

/// <summary>
/// Where the License Manager sends from — the whole of L6.1's surface.
///
/// <para>⭐⭐ <b>This window configures delivery and nothing else.</b> Decision D‑5: it is deliberately
/// NOT a third tab in Storage. Backup, restore and the data folder are about the register of record;
/// this is about a credential for an outside service. Putting them together would have meant editing a
/// closed stage's window and rewriting a title that was chosen on purpose.</para>
///
/// <para>⭐ <b>It saves nothing until the operator says so.</b> Unlike EmberTern's Settings Center, which
/// applies on change, these fields describe a single coherent configuration: a half-typed host with a
/// finished password is not a state worth persisting, and a live-applying password box would write a
/// DPAPI blob on every keystroke.</para>
///
/// <para>⭐⭐ <b>Since L6.3 it DOES test the connection — by sending a real message.</b> L6.1 recorded
/// that a "Test" button reporting success without ever sending would be worse than none; this one reaches
/// the server, authenticates and delivers, down the same code path a licence takes. ⛔ There is no
/// cheaper check and there must not be one: a handshake that stops before <c>DATA</c> proves the
/// credentials and nothing about whether mail actually arrives.</para>
/// </summary>
public sealed partial class SettingsViewModel : MessageHostViewModel
{
    private readonly SmtpSettingsStore _store;

    /// <summary>Creates the view model over a store.</summary>
    /// <remarks>⚠ Reads immediately, so the window opens on the truth rather than on blanks.</remarks>
    public SettingsViewModel(SmtpSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        SecurityOptions = new ReadOnlyCollection<SmtpSecurityOption>(
        [
            new SmtpSecurityOption(SmtpSecurity.StartTls),
            new SmtpSecurityOption(SmtpSecurity.None),
        ]);

        _selectedSecurity = SecurityOptions[0];
        SettingsPath = _store.FilePath;

        Pages = ManagerSettingsCatalog.Categories
            .Select(c => new SettingsPageViewModel(c))
            .ToList()
            .AsReadOnly();
        _selectedPage = Pages[0];

        // ⭐⭐ TWO CATALOGS, TWO CALLS. They are not the same list and must never become one: the message
        //    language is a fact about the CUSTOMER who reads the e-mail, the application language a fact
        //    about the OPERATOR. Until this line was split, both pickers were built from
        //    MessageLanguages.All, so adding a message language would have silently added an interface
        //    language with no translation behind it.
        MessageLanguageOptions = LanguageOption.ForMessages();
        ApplicationLanguageOptions = LanguageOption.ForApplication();
        _messageLanguage = MessageLanguageOptions[0];

        Reload();
    }

    /// <summary>Where the settings are kept, shown so the operator can find or delete the file.</summary>
    public string SettingsPath { get; }

    /// <summary>The two transport choices.</summary>
    public IReadOnlyList<SmtpSecurityOption> SecurityOptions { get; }

    // ── Navigation ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pages, in the order the navigation offers them.
    ///
    /// <para>⭐ Projected from <see cref="ManagerSettingsCatalog"/>, never listed a second time here — the
    /// rule EmberTern's Settings Center states about every option list it owns: a second list drifts
    /// silently, and the drift is invisible until someone opens both.</para>
    /// </summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>Which page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralPage))]
    [NotifyPropertyChangedFor(nameof(IsEmailPage))]
    private SettingsPageViewModel _selectedPage = null!;

    /// <summary>The General page is showing.</summary>
    public bool IsGeneralPage => SelectedPage?.Id == ManagerSettingsCatalog.CategoryGeneral;

    /// <summary>The E-mail page is showing.</summary>
    public bool IsEmailPage => SelectedPage?.Id == ManagerSettingsCatalog.CategoryEmail;

    // ── Languages ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The languages a message can be written in.
    ///
    /// <para>⭐ From <see cref="MessageLanguages.All"/>, so adding one is a row there rather than an edit
    /// in the view. ⛔ Never a second list in XAML.</para>
    /// </summary>
    public IReadOnlyList<LanguageOption> MessageLanguageOptions { get; }

    /// <summary>
    /// The interface languages, shown so the structure is real — ⛔ but the control is DISABLED and
    /// nothing here is stored (decision D‑8).
    /// </summary>
    /// <remarks>
    /// ⭐ From <see cref="ApplicationLanguages.All"/> — its OWN catalog, not the message one. The two
    /// happen to hold the same two codes today and they answer different questions; sharing the list
    /// would mean a message language added tomorrow arrives here as an interface language with no
    /// translation behind it.
    /// </remarks>
    public IReadOnlyList<LanguageOption> ApplicationLanguageOptions { get; }

    /// <summary>
    /// What the interface-language picker shows.
    ///
    /// <para>⛔ Read-only on purpose. The setter a picker would need does not exist, so there is no path —
    /// not even an accidental one — by which a choice made here could be persisted and then do nothing.
    /// ⚠ That is the whole of D‑8: the row is honest about being unavailable rather than pretending.</para>
    ///
    /// <para>⭐ Resolved by CODE from the catalog's default, never by an index into the list. An index is a
    /// fact about the list's order, which is a presentation decision — and this one used to read
    /// <c>[1]</c>, which meant English only because the MESSAGE catalog happens to list Polish first.</para>
    /// </summary>
    public LanguageOption ApplicationLanguage =>
        ApplicationLanguageOptions.First(o => o.Code == ApplicationLanguages.Default);

    /// <summary>Why the interface-language picker is disabled, in words the operator can act on.</summary>
    public string ApplicationLanguageUnavailable =>
        ManagerSettingsCatalog.ApplicationLanguageUnavailable;

    /// <summary>
    /// The language a licence e-mail is written in.
    ///
    /// <para>⚠⚠ <b>It commits with the page's Save, NOT on selection — and that is a deliberate narrowing
    /// of the plan, taken because implementing the alternative would have introduced a known hazard.</b>
    /// The language lives in the same file as the SMTP settings, so "apply on selection" would mean a
    /// read-modify-write of <c>smtp.dat</c> on every pick: it would either persist half-typed SMTP edits
    /// the operator had not committed, or re-read the file underneath them and lose those edits. The
    /// audit follow-up's item E is this project's own scar from exactly that shape.</para>
    ///
    /// <para>⭐ So the E-mail page has ONE Save covering everything on it. EmberTern's apply-on-change
    /// rule is not broken here — it governs preferences that are independent of one another, and these
    /// are one coherent configuration written as one file.</para>
    /// </summary>
    [ObservableProperty]
    private LanguageOption _messageLanguage = null!;

    // ── The fields ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The submission server. ⭐ Empty is legal — an operator may deliver by file only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeliverySummary))]
    private string _host = string.Empty;

    /// <summary>The submission port, as typed.</summary>
    [ObservableProperty]
    private int _port = SmtpSettings.DefaultPort;

    /// <summary>How the connection is secured.</summary>
    [ObservableProperty]
    private SmtpSecurityOption _selectedSecurity;

    /// <summary>The address the customer sees.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeliverySummary))]
    private string _fromAddress = string.Empty;

    /// <summary>The display name beside it.</summary>
    [ObservableProperty]
    private string _fromName = string.Empty;

    /// <summary>The account that signs in.</summary>
    [ObservableProperty]
    private string _username = string.Empty;

    /// <summary>
    /// The password, in memory only.
    ///
    /// <para>⚠ For Gmail this is an <b>app password</b>, not the account password — measured, and said in
    /// the window rather than left for the operator to find out from a <c>535</c>.</para>
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    // ── What the window says about itself ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ What delivery is possible with what is currently typed — the one sentence that tells an
    /// operator whether they are done. It reads the FIELDS, not the saved file, so it answers about what
    /// they are looking at.
    /// </summary>
    /// <inheritdoc />
    /// <remarks>
    /// ⚠⚠ Every property listed here composes its words in C#, so it follows the language perfectly on
    /// READ and is never re-read unless something says so. ⛔ Without this the window renders two
    /// languages at once, with no binding error and no exception.
    /// </remarks>
    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(DeliverySummary));
    }

    public string DeliverySummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FromAddress))
            {
                return ManagerSettingsCatalog.DeliveryNotConfigured;
            }

            return string.IsNullOrWhiteSpace(Host)
                ? ManagerSettingsCatalog.DeliveryFileOnly
                : ManagerSettingsCatalog.DeliveryBoth;
        }
    }

    /// <summary>
    /// ⚠ Said in the window because DPAPI's non-portability is invisible until it bites — the same
    /// warning EmberTern gives for connection passwords.
    /// </summary>
    /// <remarks>
    /// ⚠ A property reading the catalog, never a captured string — the <c>static readonly</c> lesson
    /// (L8.2). It is bound directly by the Settings window as well as used as a fallback message below.
    /// </remarks>
    public string ProtectionNote => Loc.Text(StatusCatalog.DpapiProtectionNote.Value);

    // ── Actions ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Writes the settings, refusing anything <see cref="SmtpSettings.Validate"/> can disprove.</summary>
    [RelayCommand]
    private void Save()
    {
        var settings = Current();
        var problems = settings.Validate();

        if (problems.Count > 0)
        {
            // ⭐ Every problem at once, not the first one — an operator fixing four fields one
            //    error-message at a time is four round trips through a window they cannot see past.
            Message = StatusMessage.Warning(StatusCatalog.Verbatim, new LocalizedSentences(problems));
            return;
        }

        try
        {
            _store.Save(settings);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = StatusMessage.Error(StatusCatalog.EmailSettingsNotSaved, e.Message);
            return;
        }

        Message = string.IsNullOrWhiteSpace(settings.Host)
            ? StatusMessage.Success(StatusCatalog.SavedFileDelivery)
            : StatusMessage.Success(StatusCatalog.SavedThroughHost, settings.Host);
    }

    /// <summary>Throws away the edits and re-reads what is on disk.</summary>
    [RelayCommand]
    private void Revert()
    {
        Reload();

        if (Message is null)
        {
            Message = StatusMessage.Info(StatusCatalog.SettingsReloaded);
        }
    }

    /// <summary>
    /// Asks the operator to confirm a destructive action. Assigned by the view.
    ///
    /// <para>⭐ The same seam shape <c>ShellViewModel.SaveFilePicker</c> uses, and for the same reason: a
    /// dialog is pure platform, so it arrives as a delegate and this view model keeps no Avalonia types
    /// (Architecture rule 1). ⚠ It returns the ANSWER, not a dialog.</para>
    /// </summary>
    public Func<ConfirmRequest, Task<bool>>? Confirm { get; set; }

    /// <summary>
    /// Forgets the configuration entirely — ⭐ <b>after an explicit confirmation, always.</b>
    ///
    /// <para>⚠⚠ <b>This shipped without one and the user refused it at QA, correctly.</b> The earlier
    /// reasoning — "there is nothing here the operator cannot retype" — weighed the wrong thing: the cost
    /// of retyping is not the point, an irreversible act performed on a single click is. The stored
    /// password in particular is gone for good, since DPAPI ciphertext cannot be recovered once the file
    /// is deleted.</para>
    ///
    /// <para>⛔ <b>With no confirmer wired, it REFUSES rather than proceeding.</b> That is the important
    /// half: proceeding would mean a destructive action silently losing its guard the moment a view
    /// forgot to attach one, and every test would still pass. Uncertainty ⇒ do nothing.</para>
    /// </summary>
    [RelayCommand]
    private async Task ForgetAsync()
    {
        if (Confirm is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.ConfirmationUnavailableNothingChanged);
            return;
        }

        var confirmed = await Confirm(new ConfirmRequest(
            ConfirmCatalog.ForgetSmtpTitle,
            ConfirmCatalog.ForgetSmtpMessage,
            ConfirmCatalog.ForgetSmtpAction)).ConfigureAwait(true);

        if (!confirmed)
        {
            // ⭐ Cancel changes NOTHING — not the file, not the form, and not the message strip. A
            //   "cancelled" notice would be reporting the absence of an event.
            return;
        }

        try
        {
            _store.Delete();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = StatusMessage.Error(StatusCatalog.EmailSettingsNotDeleted, e.Message);
            return;
        }

        Apply(SmtpSettings.Empty);
        Message = StatusMessage.Info(StatusCatalog.EmailNoLongerConfigured);
    }

    // ── The configuration test ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the test message goes.
    ///
    /// <para>⛔⛔ <b>It is NEVER pre-filled with a customer's address, and there is no picker that could
    /// offer one.</b> The operator types their own mailbox — a private Gmail, the company address — because
    /// a diagnostic that can be aimed at a customer by a mis-click eventually will be, and the customer
    /// would receive an unexplained message from a licensing system.</para>
    ///
    /// <para>⚠ Not persisted: it is a scratch value for one attempt, not a setting. Storing it would make
    /// it one more field in <c>smtp.dat</c> that means nothing to the next run.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTestEmailCommand))]
    private string _testRecipient = string.Empty;

    /// <summary>True while the test message is in flight.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTestEmailCommand))]
    private bool _isTesting;

    /// <summary>
    /// Builds the sender for the test. ⭐ A seam so a test can prove the refusal and the audit-free
    /// behaviour without a server; production leaves it alone and gets the real SMTP sender.
    /// </summary>
    public Func<SmtpSettings, ILicenseEmailSender> TestSenderFactory { get; set; } =
        settings => new SmtpLicenseEmailSender(settings);

    /// <summary>Whether a test can be attempted at all.</summary>
    public bool CanSendTestEmail => !IsTesting && !string.IsNullOrWhiteSpace(TestRecipient);

    /// <summary>
    /// Sends a real message to prove the configuration works.
    ///
    /// <para>⭐⭐ <b>It tests the form, not the file.</b> An operator who has just typed a host expects the
    /// test to try THAT host; requiring a Save first would mean persisting a configuration in order to
    /// discover it is wrong. ⚠ Said in the window, because the two could otherwise differ silently.</para>
    ///
    /// <para>⭐ It goes through the SAME sender a licence uses, so a success is evidence about real
    /// deliveries rather than about a simpler path.</para>
    ///
    /// <para>⛔⛔ <b>Nothing is written to the audit log — not on success, not on failure.</b>
    /// <c>audit_log</c> answers questions about licences and customers; this message concerns neither, and
    /// a <c>licence.sent</c> line with no licence would be a false entry in an append-only history. ⭐ This
    /// view model holds no register at all, so that is structural rather than a rule to remember.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendTestEmail))]
    private async Task SendTestEmailAsync()
    {
        var recipient = TestRecipient.Trim();

        if (!SmtpSettings.LooksLikeAddress(recipient))
        {
            Message = StatusMessage.Warning(StatusCatalog.NotAnEmailAddress, recipient);
            return;
        }

        var settings = Current();
        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            Message = StatusMessage.Warning(StatusCatalog.Verbatim, new LocalizedSentences(problems));
            return;
        }

        if (!settings.CanSendDirectly)
        {
            Message = StatusMessage.Warning(StatusCatalog.NoSmtpHostToTest);
            return;
        }

        if (Confirm is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.ConfirmationUnavailableNothingSent);
            return;
        }

        var confirmed = await Confirm(new ConfirmRequest(
            ConfirmCatalog.TestMessageTitle,
            ConfirmCatalog.TestMessageMessage,
            ConfirmCatalog.TestMessageAction,
            recipient,
            settings.Host)).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        IsTesting = true;
        Message = StatusMessage.Info(StatusCatalog.SendingTestMessage, recipient);

        SendOutcome outcome;
        try
        {
            outcome = await TestSenderFactory(settings)
                .SendAsync(TestEmail.Compose(settings, recipient))
                .ConfigureAwait(true);
        }
        catch (ArgumentException e)
        {
            Message = StatusMessage.FromError(e, MessageSeverity.Warning);
            return;
        }
        finally
        {
            IsTesting = false;
        }

        Message = outcome.Sent
            ? StatusMessage.Success(StatusCatalog.TestEmailSent, recipient, outcome.Delivered)

            // ⚠ The server's own words, and what they mean for the operator's next step. ⛔ Never
            //   interpreted — a wrong password and a blocked app password differ only in that text.
            : StatusMessage.Error(
                StatusCatalog.TestMessageNotSent, outcome.Error);
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The settings as the form currently reads. ⭐ Public so a test can assert on the shape.</summary>
    public SmtpSettings Current() => new()
    {
        Host = Host.Trim(),
        Port = Port,
        Security = SelectedSecurity.Value,
        FromAddress = FromAddress.Trim(),
        FromName = FromName.Trim(),
        Username = Username.Trim(),
        Password = Password,
        MessageLanguage = MessageLanguage.Code,
    };

    private void Reload()
    {
        SmtpSettingsLoad load;
        try
        {
            load = _store.Load();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = StatusMessage.Error(StatusCatalog.EmailSettingsNotRead, e.Message);
            return;
        }

        // ⭐⭐ The four states are answered SEPARATELY, and that is the whole reason the store reports
        //    them. "There are no settings yet" is a first run and says nothing alarming; "they could not
        //    be read" is a fault the operator must see BEFORE they save over it.
        switch (load.State)
        {
            case SmtpSettingsState.NotConfigured:
                Apply(SmtpSettings.Empty);
                Message = StatusMessage.None;
                break;

            case SmtpSettingsState.Loaded:
                Apply(load.Settings);
                Message = StatusMessage.None;
                break;

            case SmtpSettingsState.PasswordUnavailable:
                Apply(load.Settings);
                Message = load.Problem is { } problem
                    ? StatusMessage.Warning(problem.Key, [.. problem.Arguments])
                    : StatusMessage.Warning(StatusCatalog.DpapiProtectionNote);
                break;

            default:
                // ⛔ The form is NOT filled from a file that could not be understood. Showing recovered
                //    fragments beside an error invites a save that overwrites whatever is really there.
                Apply(SmtpSettings.Empty);
                Message = load.Problem is { } unreadable
                    ? StatusMessage.Error(unreadable.Key, [.. unreadable.Arguments])
                    : StatusMessage.Error(StatusCatalog.EmailSettingsNotReadShort);
                break;
        }
    }

    private void Apply(SmtpSettings settings)
    {
        Host = settings.Host;
        Port = settings.Port;
        FromAddress = settings.FromAddress;
        FromName = settings.FromName;
        Username = settings.Username;
        Password = settings.Password;

        SelectedSecurity = SecurityOptions.FirstOrDefault(o => o.Value == settings.Security)
            ?? SecurityOptions[0];

        // ⚠ Through `Resolve`, so a settings file naming a language this build does not know lands on the
        //    default rather than on nothing at all — a picker with no selection is a form that cannot be
        //    saved and does not say why.
        var language = MessageLanguages.Resolve(settings.MessageLanguage);
        MessageLanguage = MessageLanguageOptions.FirstOrDefault(o => o.Code == language)
            ?? MessageLanguageOptions[0];
    }

    /// <summary>The port as text, for the one field that is not a string. ⚠ Commits on blur or Enter.</summary>
    public string PortText
    {
        get => Port.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                Port = parsed;
            }

            OnPropertyChanged();
        }
    }
}

/// <summary>
/// One page in the Settings window's navigation.
///
/// <para>⭐ A thin projection of <see cref="ManagerSettingsCatalog"/>: it carries the id and the icon KEY
/// (a string, never a <c>Geometry</c> — Architecture rule 1) and reads its title back from the catalog
/// rather than capturing it.</para>
///
/// <para>⚠⚠ <b>The title is a PROPERTY, not a captured field, and that is the whole point.</b> EmberTern's
/// own settings catalog captured its titles into a table built in a static constructor, which froze the
/// entire vocabulary to the language in force when the type was first touched — the Polish QA round found
/// the page heading and the list beside it rendering the same word in two languages, and only a restart
/// cleared it. ⛔ Do not "optimise" this into a field.</para>
/// </summary>
public sealed class SettingsPageViewModel
{
    internal SettingsPageViewModel(SettingsCategory category)
    {
        Id = category.Id;
        IconKey = category.IconKey;
    }

    /// <summary>The stable identifier. ⛔ Never shown.</summary>
    public string Id { get; }

    /// <summary>The icon, as a geometry key resolved in the view.</summary>
    public string IconKey { get; }

    /// <summary>What the navigation row and the page heading both read.</summary>
    public string Title => ManagerSettingsCatalog.TitleOf(Id);
}

/// <summary>
/// One language, as a picker offers it.
///
/// <para>⭐⭐ <b>Its identity is the CODE alone.</b> A <c>record</c> compares by every positional member,
/// and <c>ComboBox.SelectedItem</c> matches by equality — so a label held as a member would make the
/// option's identity depend on the language it was built in, and rebuilding the list in another language
/// would blank the picker. ⛔ Do not add the label back as a member.</para>
/// </summary>
/// <param name="Code">What gets stored, and what this option IS. ⭐ A culture name.</param>
public sealed record LanguageOption(string Code)
{
    /// <summary>
    /// The language named IN ITSELF ("Polski", not "Polish").
    ///
    /// <para>⭐ A language picker owes its reader that: the one person who cannot read the current
    /// interface language is exactly the person reaching for it. ⛔ So this one stays a literal map in
    /// <see cref="ManagerSettingsCatalog.LanguageLabel"/> even after L8 — it is not a translation.</para>
    /// </summary>
    public string Label => ManagerSettingsCatalog.LanguageLabel(Code);

    /// <summary>
    /// The caption a picker binds to. ⭐ Notifying, like every other option in this application.
    /// </summary>
    /// <remarks>
    /// ⭐ A language names itself, so this one could not go stale — and it binds through the caption
    /// anyway, because the rule is stated POSITIVELY (every picker binds a caption) rather than as
    /// "every picker except the two that happen not to need it". See <see cref="LocalizedCaption"/>.
    /// </remarks>
    public LocalizedCaption Caption => new(() => Label);

    /// <summary>
    /// The languages a licence e-mail can be written in — a fact about the CUSTOMER who reads it.
    /// </summary>
    /// <remarks>
    /// ⛔ Built from <see cref="MessageLanguages.All"/> and never listed again. A second list is how a
    /// third language ships unreachable with a green build.
    /// </remarks>
    public static IReadOnlyList<LanguageOption> ForMessages() => Over(MessageLanguages.All);

    /// <summary>
    /// The languages the INTERFACE can be shown in — a fact about the OPERATOR.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ A different catalog from <see cref="ForMessages"/>, deliberately. Both hold the same two codes
    /// today, and they are still two questions: a message language added for a customer must not become
    /// an interface language nobody has translated. ⛔ Do not collapse these two methods into one.
    /// </remarks>
    public static IReadOnlyList<LanguageOption> ForApplication() => Over(ApplicationLanguages.All);

    private static IReadOnlyList<LanguageOption> Over(IReadOnlyList<string> codes) =>
        codes.Select(code => new LanguageOption(code)).ToList().AsReadOnly();
}
