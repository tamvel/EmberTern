using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.Localization;

using EmberTern.LicenseManager.Settings;
namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The main window's view model: customers, their licences, and issuing.
///
/// <para>⚠ <b>No Avalonia types (Architecture rule 1).</b> The one thing this needs from the platform — a
/// Save-As dialog — arrives as <see cref="SaveFilePicker"/>, a delegate the view assigns. That keeps the
/// whole issuing path testable without a window.</para>
///
/// <para>⭐ <b>Dates are picked from a calendar OR typed, since the L5.1 QA pass.</b> L3 held them as ISO
/// text and taught the operator the format in the field's own caption — a deliberate deferral, because a
/// picker is a templated control with a flyout, i.e. the largest theming surface in the application, and
/// L3 was the first stage with any UI at all. ⚠ The DOMAIN did not move with the control: a chosen day is
/// still read as a UTC calendar day, and the expiry still runs to the END of it.</para>
/// </summary>
public sealed partial class ShellViewModel : MessageHostViewModel
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly LicenseRegister _register;
    private readonly IssuingWorkflow _workflow;
    private readonly SigningSession _session;
    private readonly Func<DateTimeOffset> _clock;

    // ⚠ Null off Windows, where DPAPI does not exist — the same platform fact that leaves `Settings` null.
    //   ⛔ Not a disabled button: the PLATFORM decides whether e-mail exists here at all.
    private readonly SmtpSettingsStore? _smtpStore;

    /// <summary>
    /// Asks the platform where to save. Takes a suggested file name, returns the chosen path or
    /// <see langword="null"/> when the operator cancelled. Assigned by the view.
    /// </summary>
    public Func<string, Task<string?>>? SaveFilePicker { get; set; }

    /// <summary>
    /// Asks the operator to confirm a destructive act. Assigned by the view.
    /// </summary>
    /// <remarks>
    /// ⭐ The same arrangement <c>SettingsViewModel</c> and <c>SendLicenceViewModel</c> use: the dialog is
    /// pure platform, so it arrives as a delegate and this view model keeps no Avalonia types. ⛔ A command
    /// that needs one and finds it missing REFUSES rather than proceeding unguarded.
    /// </remarks>
    public Func<ConfirmRequest, Task<bool>>? Confirm { get; set; }

    /// <summary>Creates the shell.</summary>
    /// <param name="register">The register of record.</param>
    /// <param name="session">The unlocked signing key.</param>
    /// <param name="paths">
    /// ⭐ Where the two files live. Required rather than optional, and defaulted nowhere: a default of
    /// <see cref="ManagerPaths.Default"/> would point a test at the operator's real
    /// <c>%APPDATA%</c> folder, which is the one place a test must never reach.
    /// </param>
    /// <param name="clock">The clock.</param>
    /// <param name="bulkSenderFactory">
    /// ⭐ How the bulk send reaches a server. ⚠ A seam for the same reason <c>clock</c> is one: the CARD is
    /// what a view test drives, and a card wired to the real SMTP sender would try to reach a host. ⛔ It
    /// defaults to the production sender, so nothing has to be remembered at the composition root.
    /// </param>
    /// <param name="bulkDelay">
    /// ⭐ How the bulk send paces itself. ⚠ Same seam, second reason: a test that actually waited fifteen
    /// seconds a message would not be run.
    /// </param>
    public ShellViewModel(
        LicenseRegister register,
        SigningSession session,
        ManagerPaths paths,
        Func<DateTimeOffset>? clock = null,
        Func<SmtpSettings, ILicenseEmailSender>? bulkSenderFactory = null,
        Func<TimeSpan, System.Threading.CancellationToken, Task>? bulkDelay = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(paths);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _workflow = new IssuingWorkflow(register, _clock);
        Browser = new LicenseBrowserViewModel(register, _clock);
        History = new ArtifactHistoryViewModel(register, _workflow, session);
        BatchRenewal = new BatchRenewalViewModel(
            register, _workflow, session, Browser, message => Message = message);
        // ⭐ The PUBLIC half only. `SigningKeyFacts.Of` reads the three ceremony values off the session and
        //   keeps nothing of it — no issuer, no key, no passphrase — so the Storage surface can show and
        //   verify a key without being able to sign with one (L7.1).
        Storage = new StorageViewModel(register, paths, SigningKeyFacts.Of(session), _clock);

        // ⭐ Built here rather than when the window opens, for the same reason Storage is: the settings
        //    are read ONCE, so re-opening the window shows what the operator last typed rather than
        //    silently re-reading the file underneath them.
        // ⚠ The OS check is the composition root's job, not the view model's: the whole application is
        //    DPAPI-bound on Windows (LocalDpapiProtector), and this is where that is known.
        _smtpStore = OperatingSystem.IsWindows() ? SmtpSettingsStore.At(paths) : null;
        Settings = _smtpStore is null
            ? null
            : new SettingsViewModel(_smtpStore, ApplicationLanguageService.At(paths));

        // ⚠ Built after the store, because it reads the settings through PrepareBulkSend. ⭐ The method
        //   group is what keeps "read fresh from the file" true at every rebuild rather than at construction.
        BulkSend = new BulkSendViewModel(
            register, Browser, PrepareBulkSend, message => Message = message,
            senderFactory: bulkSenderFactory, delay: bulkDelay, clock: _clock);

        // ⭐ 🔒 Decision M — the two bulk operations on this view are mutually exclusive while one runs.
        //    Wired HERE rather than by either view model knowing about the other: neither owns the rule,
        //    the composition root does.
        BulkSend.SendingChanged += (_, _) => BatchRenewal.IsBlockedByBulkSend = BulkSend.IsSending;

        ReloadCustomers();
        Message = StatusMessage.Info(StatusCatalog.SigningWithKey, session.KeyId);
    }

    // ── Views ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The licences view: every licence in the register, across every customer.
    ///
    /// <para>⭐ A second VIEW rather than more filters on the customer panel, ratified by the user. The
    /// customer panel answers <i>"what does THIS customer have?"</i>; the operator arriving with
    /// <i>"who lapses next month?"</i> has no customer to start from, and folding that question into a
    /// panel organised around one name is what makes an administrative tool unusable at fifty
    /// customers.</para>
    /// </summary>
    public LicenseBrowserViewModel Browser { get; }

    /// <summary>
    /// Every artifact ever issued for the selected licence, and the detail of the one being looked at.
    ///
    /// <para>⭐ A third organising principle, split out for the reason §40.1 gave for the browser: the
    /// licence FORM is about terms — singular, editable, the state of an agreement — while this is about
    /// artifacts, which are plural, ordered and immutable. ⛔ Read-only over the history by design, not by
    /// stage boundary: <c>issued_artifacts</c> refuses UPDATE and DELETE at the database.</para>
    /// </summary>
    public ArtifactHistoryViewModel History { get; }

    /// <summary>
    /// Extending many licences to one date, as one act — the licences view's bulk operation.
    ///
    /// <para>⭐ It reads the browser's ticked set rather than owning a selection of its own, so there is
    /// exactly one answer to <i>"which licences is this about?"</i> ⛔ It writes no file (D‑4): a batch
    /// ends at a committed register, and delivery stays the separate export action.</para>
    /// </summary>
    public BatchRenewalViewModel BatchRenewal { get; }

    /// <summary>
    /// Sending many licences by e-mail, one message at a time — the licences view's other bulk operation.
    ///
    /// <para>⭐ It reads the SAME ticked set <see cref="BatchRenewal"/> reads, so there is exactly one
    /// answer to <i>"which licences is this about?"</i> ⛔ And the two never run together (🔒 decision M):
    /// a send composes every message before the first one leaves, so a renewal underneath it would
    /// supersede an artifact that is still queued to go out.</para>
    /// </summary>
    public BulkSendViewModel BulkSend { get; }

    /// <summary>
    /// Backup, restore, the JSONL escape hatch and the data folder.
    ///
    /// <para>⭐ It is opened as its OWN WINDOW (D‑4), not shown as a third view. The two view tabs answer
    /// two questions about licences; file operations are not a third one of those. ⚠ The separation is
    /// also a safety property — restore is the most consequential action in this application, and it
    /// should take a deliberate step to reach.</para>
    /// </summary>
    public StorageViewModel Storage { get; }

    /// <summary>
    /// The application's preferences: General, and E-mail.
    ///
    /// <para>⭐ Its OWN window (D‑5), reached from the hamburger menu exactly as EmberTern's is, and
    /// deliberately not a tab in <see cref="Storage"/>: that window is about looking after the register of
    /// record, and a preference — least of all a credential for an outside service — is not one of its
    /// questions.</para>
    ///
    /// <para>⚠ <see langword="null"/> when not running on Windows, because the e-mail credential is
    /// protected with DPAPI and there is nothing honest to show without it. ⛔ The window is simply not
    /// opened — no disabled form promising a configuration this platform cannot keep.</para>
    /// </summary>
    public SettingsViewModel? Settings { get; }

    /// <summary>Which of the two views is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomersView))]
    private bool _isLicensesView;

    /// <summary>The customer-centric view — the one L3 built, unchanged.</summary>
    public bool IsCustomersView => !IsLicensesView;

    /// <summary>Shows customers.</summary>
    [RelayCommand]
    private void ShowCustomers() => IsLicensesView = false;

    /// <summary>
    /// Shows the licences view, re-reading the register first.
    ///
    /// <para>⚠ The refresh is here rather than on every mutation because in L5.1 this view is read-only
    /// and unreachable while the operator edits — so "fresh whenever it is looked at" is both sufficient
    /// and the only rule that cannot fall out of step with a mutation somebody adds later.</para>
    /// </summary>
    [RelayCommand]
    private void ShowLicenses()
    {
        Browser.Refresh();
        IsLicensesView = true;
    }

    /// <summary>
    /// Takes the licence selected in the browser back to the customer view, with its customer and the
    /// licence itself selected.
    ///
    /// <para>⭐ Without it, search is a dead end: the operator finds the licence that lapses on Friday
    /// and has no way to act on it. ⚠ Navigation only — L5.1 changes nothing.</para>
    /// </summary>
    [RelayCommand]
    private void OpenSelectedLicense()
    {
        if (Browser.SelectedLicense?.Summary is not { } summary)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectLicenceInListFirst);
            return;
        }

        var customer = Customers.FirstOrDefaultById(summary.License.CustomerId);
        if (customer is null)
        {
            // Unreachable while the register is sound; CheckIntegrity reports exactly this shape.
            Message = StatusMessage.Error(
                StatusCatalog.LicenceNamesUnknownCustomer,
                summary.License.LicenseId,
                summary.License.CustomerId);
            return;
        }

        IsLicensesView = false;

        // ⭐ …and onto the LICENCES tab of that customer, not their contact details. The operator asked to
        //    open a LICENCE; landing on the Customer page would answer a question they did not ask and
        //    would hide the very row they double-clicked. ⚠ A consequence of the Customer/Licences split,
        //    and the one place where it is not enough to leave the tab where it was.
        IsCustomerTab = false;

        SelectedCustomer = customer;
        SelectedLicense = Licenses.FirstOrDefaultById(summary.License.LicenseId);
    }

    // ── The customer's two pages ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which page of the selected customer is showing: their DETAILS, or their LICENCES.
    ///
    /// <para>⭐⭐ <b>Two pages because there are two questions</b> — <i>"who is this customer?"</i> and
    /// <i>"what licences do they have and what happened to them?"</i> They used to share one scrolling
    /// column, so contact details ran into licence terms and then into the issuing history with no
    /// boundary the eye could use. ⛔ Not a spacing problem: no amount of gutter makes "which of these
    /// belongs to what I am looking at" answerable.</para>
    ///
    /// <para>⭐ The shape is <see cref="StorageViewModel"/>'s Backup/Restore switch, and the markup is the
    /// same <c>Border.view-switch</c> + <c>Button.view-tab</c> the main view switch uses. ⛔ Not a
    /// <c>TabControl</c>, for the reason §40.2 recorded: <c>ControlStyles.axaml</c> is not linkable here,
    /// so a <c>TabItem</c> would fall back to Fluent's own chrome.</para>
    ///
    /// <para>⚠ <b>Selecting a different customer KEEPS the current page</b> (user decision). An operator
    /// comparing licences across customers stays in licences; being thrown back to contact details on
    /// every selection would make the switch feel like it undoes itself.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLicencesTab))]
    private bool _isCustomerTab = true;

    /// <summary>The customer's licences, their terms, and the history of what was issued.</summary>
    public bool IsLicencesTab => !IsCustomerTab;

    /// <summary>Shows the customer's own details.</summary>
    [RelayCommand]
    private void ShowCustomerTab() => IsCustomerTab = true;

    /// <summary>Shows the customer's licences.</summary>
    [RelayCommand]
    private void ShowLicencesTab() => IsCustomerTab = false;

    // ── Customers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every customer, by name.</summary>
    public ObservableCollection<CustomerRecord> Customers { get; } = [];

    /// <summary>Which one is being looked at.</summary>
    [ObservableProperty]
    private CustomerRecord? _selectedCustomer;

    /// <summary>⭐ Required.</summary>
    [ObservableProperty]
    private string _customerId = string.Empty;

    /// <summary>⭐ Required — this is the name that gets signed and displayed in EmberTern.</summary>
    [ObservableProperty]
    private string _customerName = string.Empty;

    /// <summary>Postal address.</summary>
    [ObservableProperty]
    private string _customerAddress = string.Empty;

    /// <summary>Contact first name.</summary>
    [ObservableProperty]
    private string _customerFirstName = string.Empty;

    /// <summary>Contact last name.</summary>
    [ObservableProperty]
    private string _customerLastName = string.Empty;

    /// <summary>Where the licence will be sent (L6).</summary>
    [ObservableProperty]
    private string _customerEmail = string.Empty;

    /// <summary>⛔ Administrative only — never travels in a licence.</summary>
    [ObservableProperty]
    private string _customerNotes = string.Empty;

    partial void OnSelectedCustomerChanged(CustomerRecord? value)
    {
        if (value is null)
        {
            return;
        }

        CustomerId = value.CustomerId;
        CustomerName = value.Name;
        CustomerAddress = value.Address ?? string.Empty;
        CustomerFirstName = value.FirstName ?? string.Empty;
        CustomerLastName = value.LastName ?? string.Empty;
        CustomerEmail = value.Email ?? string.Empty;
        CustomerNotes = value.Notes ?? string.Empty;

        ReloadLicenses();
    }

    /// <summary>Starts a blank customer with the next free identifier.</summary>
    [RelayCommand]
    private void NewCustomer()
    {
        SelectedCustomer = null;
        CustomerId = _register.NextCustomerId();
        CustomerName = string.Empty;
        CustomerAddress = string.Empty;
        CustomerFirstName = string.Empty;
        CustomerLastName = string.Empty;
        CustomerEmail = string.Empty;
        CustomerNotes = string.Empty;
        Licenses.Clear();
        SelectedLicense = null;

        // ⭐⭐ THE LICENCE FORM BELONGS TO A CUSTOMER, so starting a new customer must empty it. Clearing
        //    the LIST and the SELECTION was not enough: the licence ID stayed behind, and the next "Save
        //    terms" addressed the previous customer's row. That is the reported defect — see
        //    SecondCustomerRegressionTests.
        ClearLicenseForm();

        Message = StatusMessage.Info(StatusCatalog.NewCustomerHint);
    }

    /// <summary>
    /// Removes the selected licence from the active register — after saying exactly which of the two
    /// things that means.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>TWO OUTCOMES, AND THE SCHEMA CHOOSES BETWEEN THEM</b> (see
    /// <c>LicenseRegister.RemoveLicense</c>): a licence that was never issued is DELETED, one that was is
    /// RETIRED. ⚠ The operator is told WHICH before they confirm, in two different sentences — they are
    /// confirming two different acts, and one sentence covering both would have to be vague about exactly
    /// the part that matters.</para>
    ///
    /// <para>⭐ The artifact count is read from the REGISTER rather than from the history panel, which is a
    /// snapshot taken when the licence was selected. The register counts again inside its own transaction;
    /// this one is for the WORDS, that one is the guarantee.</para>
    ///
    /// <para>⛔ <b>With no confirmer wired it REFUSES rather than proceeding</b> — the rule L6.1a's
    /// <c>Forget settings</c> established.</para>
    /// </remarks>
    [RelayCommand]
    private async Task RemoveLicenceAsync()
    {
        if (SelectedLicense is not { } licence)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectLicenceToRemove);
            return;
        }

        if (Confirm is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.ConfirmationUnavailableNothingSent);
            return;
        }

        var artifacts = _register.CountArtifacts(licence.LicenseId);
        var shortId = LicenceIdText.Short(licence.LicenseId);

        var confirmed = await Confirm(new ConfirmRequest(
            ConfirmCatalog.RemoveLicenceTitle,
            artifacts == 0
                ? ConfirmCatalog.RemoveLicenceNeverIssuedMessage
                : ConfirmCatalog.RemoveLicenceIssuedMessage,
            ConfirmCatalog.RemoveLicenceAction,
            shortId,
            artifacts.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(true);

        if (!confirmed)
        {
            // ⭐ Cancel changes nothing and says nothing — a "cancelled" notice reports the absence of an event.
            return;
        }

        LicenceRemoval outcome;
        try
        {
            outcome = _register.RemoveLicense(licence.LicenseId);
        }
        catch (RegisterIntegrityException e)
        {
            // ⭐ The register's own sentence, resolved from its key. ⛔ Not `e.Message`.
            Message = StatusMessage.Error(e.Key, [.. e.Arguments]);
            return;
        }

        // ⚠ The FORM is cleared as well as the list: the removed licence's fields left on screen would
        //   invite a "Save terms" that recreates it under the same identifier.
        ClearLicenseForm();
        SelectedLicense = null;
        ReloadLicenses();

        // ⚠ And the history panel, which was showing that licence's artifacts.
        History.Load(null);

        Message = outcome == LicenceRemoval.Deleted
            ? StatusMessage.Success(StatusCatalog.LicenceDeleted, shortId)
            : StatusMessage.Success(
                StatusCatalog.LicenceRetired,
                shortId,
                artifacts.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Removes the selected customer — after saying exactly what that does, and only when it is safe.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>THREE ANSWERS, and only one of them is a question.</b> Nothing selected is a nudge;
    /// a customer who still has licences is a REFUSAL that names the obstacle; and a customer who has none
    /// is the only case that reaches a confirmation. ⛔ A confirmation offered for something that cannot
    /// happen teaches the operator that confirmations are noise.</para>
    ///
    /// <para>⭐ The count is read from the REGISTER rather than from <c>Licenses</c>, which is a snapshot
    /// taken when the customer was selected. A licence added since would be invisible to that list, and
    /// the guard has to hold against what the database contains, not against what a panel remembers.
    /// ⚠ The register checks again inside its own transaction — this one is for the WORDS, that one is the
    /// guarantee.</para>
    ///
    /// <para>⛔ <b>With no confirmer wired it REFUSES rather than proceeding</b> — the rule L6.1a's
    /// <c>Forget settings</c> established, and the stakes here are the same class: an operator's data.</para>
    /// </remarks>
    [RelayCommand]
    private async Task RemoveCustomerAsync()
    {
        if (SelectedCustomer is not { } customer)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectCustomerToRemove);
            return;
        }

        var licences = _register.CountLicenses(customer.CustomerId);
        if (licences > 0)
        {
            Message = StatusMessage.Warning(
                StatusCatalog.CustomerStillHasLicences,
                customer.Name,
                licences.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (Confirm is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.ConfirmationUnavailableNothingSent);
            return;
        }

        var confirmed = await Confirm(new ConfirmRequest(
            ConfirmCatalog.RemoveCustomerTitle,
            ConfirmCatalog.RemoveCustomerMessage,
            ConfirmCatalog.RemoveCustomerAction,
            customer.Name,
            customer.CustomerId)).ConfigureAwait(true);

        if (!confirmed)
        {
            // ⭐ Cancel changes nothing and says nothing — a "cancelled" notice reports the absence of an event.
            return;
        }

        try
        {
            _register.DeleteCustomer(customer.CustomerId);
        }
        catch (RegisterIntegrityException e)
        {
            // ⭐ The register's own sentence, resolved from its key. ⛔ Not `e.Message`, which is the
            //   English diagnostic half.
            Message = StatusMessage.Error(e.Key, [.. e.Arguments]);
            return;
        }

        // ⚠ The form is cleared as well as the list: leaving the removed customer's fields on screen would
        //   invite a Save that recreates them with the same identifier.
        NewCustomerCommand.Execute(null);
        ReloadCustomers();

        Message = StatusMessage.Success(StatusCatalog.CustomerRemoved, customer.Name);
    }

    /// <summary>Creates or updates the customer.</summary>
    [RelayCommand]
    private void SaveCustomer()
    {
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            Message = StatusMessage.Warning(StatusCatalog.CustomerNameRequired);
            return;
        }

        if (string.IsNullOrWhiteSpace(CustomerId))
        {
            CustomerId = _register.NextCustomerId();
        }

        var saved = _register.SaveCustomer(new CustomerRecord
        {
            CustomerId = CustomerId.Trim(),
            Name = CustomerName,
            Address = Blank(CustomerAddress),
            FirstName = Blank(CustomerFirstName),
            LastName = Blank(CustomerLastName),
            Email = Blank(CustomerEmail),
            Notes = Blank(CustomerNotes),
        });

        ReloadCustomers(saved.CustomerId);
        Message = StatusMessage.Success(StatusCatalog.CustomerSaved, saved.Name);
    }

    private void ReloadCustomers(string? selectId = null)
    {
        var target = selectId ?? SelectedCustomer?.CustomerId;

        Customers.Clear();
        foreach (var customer in _register.GetCustomers())
        {
            Customers.Add(customer);
        }

        SelectedCustomer = target is null
            ? null
            : Customers.FirstOrDefaultById(target);
    }

    // ── Licences ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The selected customer's licences.</summary>
    public ObservableCollection<LicenseRecord> Licenses { get; } = [];

    /// <summary>Which licence is being looked at.</summary>
    [ObservableProperty]
    private LicenseRecord? _selectedLicense;

    /// <summary>The <c>lid</c>. Read-only in the UI — it is generated, not chosen.</summary>
    [ObservableProperty]
    private string _licenseId = string.Empty;

    /// <summary>Contractual seats (D2). ⚠ Displayed by EmberTern, never enforced by it.</summary>
    [ObservableProperty]
    private int _licenseSeats = 1;

    /// <summary>
    /// Start of validity — a DATE, not text.
    ///
    /// <para>⭐ L3 held these as ISO strings and told the operator the format in the field's own caption,
    /// because a date picker was judged too large a theming surface for the first stage that had any UI.
    /// The picker is here now: the operator picks from a calendar or types, and nobody has to be taught
    /// <c>yyyy-MM-dd</c>. ⚠ The DOMAIN is unchanged — the date is read as a UTC calendar day, and the
    /// expiry still runs to the END of the day the operator chose (see <see cref="TryReadTerms"/>).</para>
    /// </summary>
    [ObservableProperty]
    private DateTime? _licenseNotBefore;

    /// <summary>End of validity, inclusive. ⭐ Stored as running to 23:59:59 of this day.</summary>
    [ObservableProperty]
    private DateTime? _licenseExpiresAt;

    /// <summary>⛔ Administrative only.</summary>
    [ObservableProperty]
    private string _licenseNotes = string.Empty;

    partial void OnSelectedLicenseChanged(LicenseRecord? value)
    {
        if (value is null)
        {
            History.Load(null);
            RefreshIssueReasons();
            return;
        }

        LicenseId = value.LicenseId;
        LicenseSeats = value.Seats;
        LicenseNotBefore = value.NotBefore.UtcDateTime.Date;
        LicenseExpiresAt = value.ExpiresAt.UtcDateTime.Date;
        LicenseNotes = value.Notes ?? string.Empty;

        // ⭐ ONE read of the history, handed to the panel that owns it. The one-line summary the form
        //    used to compute for itself now comes from the same load, so the line and the list can never
        //    report different counts.
        History.Load(value.LicenseId);

        // ⭐ The reason choices follow the SAME path as the history, so "this licence has been issued" can
        //    never be true for one of them and false for the other. Issuing re-enters here (the command
        //    calls OnSelectedLicenseChanged), which is what turns `initial` into a real choice afterwards.
        RefreshIssueReasons();
    }

    /// <summary>Starts a blank licence for the selected customer.</summary>
    [RelayCommand]
    private void NewLicense()
    {
        if (SelectedCustomer is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectOrSaveCustomerFirst);
            return;
        }

        var today = _clock().UtcDateTime.Date;

        SelectedLicense = null;
        LicenseId = LicenseIssuer.NewLicenseId();
        LicenseSeats = 1;                                   // decision O4 — explicit, never blank
        LicenseNotBefore = today;
        LicenseExpiresAt = today.AddYears(1);
        LicenseNotes = string.Empty;
        History.Load(null);
        RefreshIssueReasons();
        Message = StatusMessage.Info(StatusCatalog.NewLicenceHint);
    }

    /// <summary>Creates or updates the licence terms.</summary>
    [RelayCommand]
    private void SaveLicense()
    {
        if (SelectedCustomer is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectCustomerFirst);
            return;
        }

        if (!TryReadTerms(out var notBefore, out var expiresAt, out var problem))
        {
            Message = StatusMessage.Warning(problem!.Key, [.. problem.Arguments]);
            return;
        }

        if (string.IsNullOrWhiteSpace(LicenseId))
        {
            LicenseId = LicenseIssuer.NewLicenseId();
        }

        var saved = _register.SaveLicense(new LicenseRecord
        {
            LicenseId = LicenseId,
            CustomerId = SelectedCustomer.CustomerId,
            Product = LicenseConstants.ProductId,
            Seats = LicenseSeats,
            NotBefore = notBefore,
            ExpiresAt = expiresAt,
            Status = LicenseStatuses.Active,
            Notes = Blank(LicenseNotes),
        });

        ReloadLicenses(saved.LicenseId);
        Message = StatusMessage.Success(StatusCatalog.LicenceSavedShort, saved.LicenseId[..8]);
    }

    /// <summary>
    /// Empties the licence form. ⭐ ONE place, so "what a blank licence form looks like" cannot drift
    /// between the two callers that need it.
    /// </summary>
    private void ClearLicenseForm()
    {
        LicenseId = string.Empty;
        LicenseSeats = 1;
        LicenseNotBefore = null;
        LicenseExpiresAt = null;
        LicenseNotes = string.Empty;
        History.Load(null);
        RefreshIssueReasons();
    }

    private void ReloadLicenses(string? selectId = null)
    {
        var target = selectId ?? SelectedLicense?.LicenseId;

        Licenses.Clear();
        if (SelectedCustomer is null)
        {
            SelectedLicense = null;
            return;
        }

        foreach (var license in _register.GetLicenses(SelectedCustomer.CustomerId))
        {
            Licenses.Add(license);
        }

        SelectedLicense = target is null ? null : Licenses.FirstOrDefaultById(target);
    }

    // ── Issuing ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why the next artifact would be issued — the choices the operator may truthfully pick from.
    ///
    /// <para>⭐⭐ <b>L5.3 replaced an INFERENCE with a choice.</b> Until this stage the reason was computed
    /// as <c>artifacts.Count == 0 ? initial : renewal</c>, which contradicted the contract stated on
    /// <see cref="IssueRequest.Reason"/> — <i>chosen by the operator, never inferred from a diff</i> — and
    /// left two of the four vocabulary values (<c>terms-change</c>, <c>reissue-lost</c>) unreachable by any
    /// code path. Every re-issue was recorded as a renewal whether or not an expiry had ever moved, in a
    /// column that cannot be corrected.</para>
    /// </summary>
    public ObservableCollection<IssueReasonOption> IssueReasonChoices { get; } = [];

    /// <summary>The reason the operator picked for the next issue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueReasonExplanation))]
    [NotifyPropertyChangedFor(nameof(IsReissueLostSelected))]
    private IssueReasonOption? _selectedIssueReason;

    /// <summary>What the selected reason means, so the choice is made on meaning rather than on a label.</summary>
    /// <inheritdoc />
    /// <remarks>
    /// ⚠⚠ Every property listed here composes its words in C#, so it follows the language perfectly on
    /// READ and is never re-read unless something says so. ⛔ Without this the window renders two
    /// languages at once, with no binding error and no exception.
    /// </remarks>
    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(IssueReasonExplanation));
    }

    public string IssueReasonExplanation => SelectedIssueReason?.Explanation ?? string.Empty;

    /// <summary>
    /// ⭐ <b>D‑6.</b> Whether the operator is about to sign a NEW artifact because a customer lost a file
    /// they were already sent — the one reason whose correct answer is usually not to issue at all.
    ///
    /// <para>⚠ It gates advice, never the action. A new <c>iat</c> makes EmberTern treat the file as a
    /// replacement (§16.4), so re-exporting the stored artifact is both cheaper and more faithful — but an
    /// operator who has read that and still chooses this is making a decision, and the application does
    /// not overrule it.</para>
    /// </summary>
    public bool IsReissueLostSelected =>
        SelectedIssueReason is { } option &&
        string.Equals(option.Value, IssueReasons.ReissueLost, StringComparison.Ordinal);

    /// <summary>
    /// ⭐ <b>D‑4.</b> An optional remark recorded with the issue — a ticket number, who asked, why now.
    ///
    /// <para>It travels on the existing <c>audit_log.note</c> and adds no column and no model: the audit
    /// line is written for every issue anyway, and is already append-only. ⚠ Optional means optional —
    /// nothing here refuses an issue for want of a remark.</para>
    /// </summary>
    [ObservableProperty]
    private string _issueNote = string.Empty;

    /// <summary>
    /// Whether the operator is choosing between reasons, or the answer is already fixed.
    ///
    /// <para>⭐ <b>D‑2.</b> Before the first issue there is exactly one truthful value, so the picker is a
    /// statement rather than a question. Offering a list there would invite the operator to record an
    /// untruth about an artifact that does not exist yet.</para>
    /// </summary>
    [ObservableProperty]
    private bool _canChooseIssueReason;

    /// <summary>
    /// Rebuilds the reason choices for whichever licence is selected.
    ///
    /// <para>⚠ Called on every selection change and after every issue: an issue turns a never-issued
    /// licence into an issued one, which changes what may truthfully be recorded next.</para>
    /// </summary>
    private void RefreshIssueReasons()
    {
        var change = MeasureChange();

        IssueReasonChoices.Clear();
        foreach (var reason in IssueReasonPolicy.Offer(change))
        {
            // ⭐ The stored value only. The option resolves its own words through ReasonText, so the
            //   picker and the history list read one mapping rather than two copies of it.
            IssueReasonChoices.Add(new IssueReasonOption(reason));
        }

        CanChooseIssueReason = change.HasPrevious;

        // The single offered value is selected for the operator; a real choice starts unmade, so that
        // issuing without deciding is not something a default can do on their behalf.
        SelectedIssueReason = IssueReasonChoices.Count == 1 ? IssueReasonChoices[0] : null;
        IssueNote = string.Empty;
    }

    /// <summary>
    /// Measures the selected licence's saved terms against the artifact the customer is currently holding.
    ///
    /// <para>⚠⚠ <b>Measured afresh at every use, never cached.</b> The operator can press <b>Save terms</b>
    /// between choosing a reason and issuing — which is exactly the sequence a renewal requires — so a
    /// stored answer would judge the reason against terms that are no longer the ones being signed.</para>
    ///
    /// <para>⭐ Compared against <see cref="LicenseRegister.GetCurrentArtifact"/>, the register's POINTER,
    /// not the newest row. The pointer is the authority on which release the customer holds (§39.2).</para>
    /// </summary>
    private IssueChange MeasureChange()
    {
        if (SelectedLicense is not { } licence || SelectedCustomer is not { } customer)
        {
            return IssueChange.NeverIssued;
        }

        return IssueChange.Between(
            _register.GetCurrentArtifact(licence.LicenseId), licence, customer.Name);
    }

    /// <summary>
    /// Signs the selected licence, records the artifact, and offers to save
    /// <c>EmberTern.etlic</c>.
    ///
    /// <para>⭐ Recording happens BEFORE saving, and a cancelled Save-As leaves the artifact recorded. A
    /// signed licence that the register does not know about is the one state from which it can no longer
    /// answer "what did we send this customer?" — and the file can always be exported again from the
    /// stored token (§12.5).</para>
    /// </summary>
    [RelayCommand]
    private async Task IssueAndSaveAsync()
    {
        if (SelectedCustomer is null || SelectedLicense is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectSavedLicenceToIssue);
            return;
        }

        // ⭐⭐ THE REASON IS THE OPERATOR'S, AND IT IS CHECKED AGAINST WHAT ACTUALLY CHANGED.
        //    `issued_artifacts.reason` is append-only: a value written here is in the register forever and
        //    no later screen can correct it. So a claim the register can DISPROVE — "renewal" against an
        //    expiry that never moved — is refused before it becomes a row, while a claim it cannot judge
        //    ("the customer lost their file") is the operator's to make. See IssueReasonPolicy.
        // ⚠ Measured again HERE rather than reused from RefreshIssueReasons: Save terms may have run since.
        var change = MeasureChange();
        var reason = change.HasPrevious ? SelectedIssueReason?.Value : IssueReasons.Initial;

        if (reason is null)
        {
            Message = StatusMessage.Warning(
                StatusCatalog.ChooseIssueReasonFirst);
            return;
        }

        if (IssueReasonPolicy.Refuse(reason, change) is { } refusal)
        {
            Message = StatusMessage.Warning(refusal.Key, [.. refusal.Arguments]);
            return;
        }

        IssueResult result;
        try
        {
            result = _workflow.Issue(
                _session, SelectedLicense, SelectedCustomer, reason, Blank(IssueNote));
        }
        catch (ArgumentException e)
        {
            Message = StatusMessage.Error(StatusCatalog.LicenceNotIssued, e.Message);
            return;
        }
        catch (System.Security.Cryptography.CryptographicException e)
        {
            // The issuer refused to hand out an artifact it could not verify. This is a key or format
            // fault, and it is the one error here that is ours rather than the operator's.
            Message = StatusMessage.FromError(e, MessageSeverity.Error);
            return;
        }

        OnSelectedLicenseChanged(SelectedLicense);

        var path = SaveFilePicker is null
            ? null
            : await SaveFilePicker(LicenseConstants.DeliveredFileName).ConfigureAwait(true);

        if (path is null)
        {
            Message = StatusMessage.Success(StatusCatalog.IssuedAndRecorded, SelectedCustomer.Name);
            return;
        }

        try
        {
            _workflow.SaveArtifact(result.Artifact, path);
            Message = StatusMessage.Success(StatusCatalog.IssuedAndSaved, SelectedCustomer.Name, path);
        }
        catch (System.IO.IOException e)
        {
            Message = StatusMessage.Warning(StatusCatalog.IssuedButFileNotWritten, e.Message);
        }
    }

    /// <summary>Re-exports the most recent artifact, byte-for-byte, without signing anything new.</summary>
    [RelayCommand]
    private async Task ExportLatestAsync()
    {
        if (SelectedLicense is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectLicence);
            return;
        }

        var artifacts = _register.GetArtifacts(SelectedLicense.LicenseId);
        if (artifacts.Count == 0)
        {
            Message = StatusMessage.Warning(StatusCatalog.LicenceNeverIssued);
            return;
        }

        var path = SaveFilePicker is null
            ? null
            : await SaveFilePicker(LicenseConstants.DeliveredFileName).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        _workflow.SaveArtifact(artifacts[0], path);
        Message = StatusMessage.Success(StatusCatalog.ArtifactExported, path);
    }

    /// <summary>
    /// Shows what EmberTern would say about the CURRENT artifact today, and opens it in the history.
    ///
    /// <para>⭐ L5.2 gave this command a second half rather than a second command: it now selects the
    /// artifact it is describing, so the message strip and the detail panel are always talking about the
    /// same release. ⛔ Still the only Inspect — the double-click gesture (P1-c) runs this, and there is
    /// no parallel path that would have to re-answer "never issued" on its own.</para>
    ///
    /// <para>⚠ It selects the artifact the REGISTER marks current, not <c>Artifacts[0]</c>. Those are the
    /// same row today and the pointer is the authority (§39.2).</para>
    /// </summary>
    [RelayCommand]
    private void InspectLatest()
    {
        if (SelectedLicense is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectLicence);
            return;
        }

        var current = _register.GetCurrentArtifact(SelectedLicense.LicenseId);
        if (current is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.LicenceNeverIssued);
            return;
        }

        History.SelectCurrent();
        Message = VerdictText.Describe(_workflow.Inspect(_session, current));
    }

    /// <summary>
    /// Prepares the <b>Send licence</b> window for the selected licence, or explains why it cannot open.
    ///
    /// <para>⭐⭐ <b>Everything that can refuse happens HERE, in the view model</b> — no licence selected,
    /// e-mail not configured, settings unreadable, the customer has no address, the licence was never
    /// issued. The window therefore only ever opens on a message that CAN be sent, and every refusal is
    /// testable without a window. ⛔ A window that opens and then says "actually, no" is a window the
    /// operator has to close to learn nothing.</para>
    ///
    /// <para>⭐⭐ <b>The attachment comes from <c>license_current_artifact</c></b> — the register's POINTER,
    /// the same authority <see cref="InspectLatest"/> reads. ⛔ Never <c>Artifacts[0]</c>, and ⛔ nothing is
    /// signed here: sending an e-mail must never mint a new <c>iat</c>, which the client would install as
    /// a replacement for the licence the customer already holds (§16.4).</para>
    ///
    /// <para>⚠ The settings are read FRESH, not from the Settings window's in-memory copy: the operator
    /// may have opened that window, typed, and not saved — and what gets sent must be what is saved.</para>
    /// </summary>
    public SendLicenceViewModel? PrepareSendLicence()
    {
        if (SelectedCustomer is not { } customer || SelectedLicense is not { } licence)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectLicenceToSend);
            return null;
        }

        if (_smtpStore is null)
        {
            Message = StatusMessage.Warning(
                StatusCatalog.EmailIsWindowsOnly);
            return null;
        }

        var current = _register.GetCurrentArtifact(licence.LicenseId);
        if (current is null)
        {
            Message = StatusMessage.Warning(StatusCatalog.LicenceNeverIssuedNothingToSend);
            return null;
        }

        SmtpSettingsLoad load;
        try
        {
            load = _smtpStore.Load();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = StatusMessage.Error(StatusCatalog.EmailSettingsNotRead, e.Message);
            return null;
        }

        // ⭐ The store's four states, answered separately — the reason it reports them (§48.3).
        switch (load.State)
        {
            case SmtpSettingsState.NotConfigured:
                Message = StatusMessage.Warning(
                    StatusCatalog.EmailNotConfigured);
                return null;

            case SmtpSettingsState.Unreadable:
                Message = StatusMessage.Error(
                    load.Problem is { } unreadable
                        ? unreadable.Key
                        : StatusCatalog.EmailSettingsNotReadNothingSent);
                return null;

            default:
                break;
        }

        var problems = LicenseMessageComposer.Problems(current, customer, load.Settings);
        if (problems.Count > 0)
        {
            Message = StatusMessage.Warning(StatusCatalog.Verbatim, new LocalizedSentences(problems));
            return null;
        }

        var model = new SendLicenceViewModel(
            LicenseMessageComposer.Compose(current, customer, load.Settings),
            load.Settings,
            new LicenceDelivery(_register));

        if (load.State == SmtpSettingsState.PasswordUnavailable)
        {
            // ⚠ Not a refusal: the message can be composed and saved as a file, and an attempt to send
            //   will fail with the SERVER's own words rather than with our guess about them.
            model.Message = load.Problem is { } unreadablePassword
                ? StatusMessage.Warning(unreadablePassword.Key, [.. unreadablePassword.Arguments])
                : StatusMessage.Warning(StatusCatalog.SmtpPasswordUnreadableSendWillFail);
        }

        Message = StatusMessage.None;
        return model;
    }

    /// <summary>
    /// Reads the e-mail settings a bulk send would use, or says why there are none.
    ///
    /// <para>⭐⭐ <b>The SAME refusal path as <see cref="PrepareSendLicence"/></b>, minus the three refusals
    /// that are about ONE licence (nothing selected, never issued, this customer's address). Those are
    /// per-licence facts, and in a bulk send they are the planner's business: a licence that cannot be sent
    /// is HELD and named, and it must not stop the other forty (§60.2).</para>
    ///
    /// <para>⚠ The settings are read FRESH from the file, not from the Settings window's in-memory copy:
    /// the operator may have opened that window, typed, and not saved — and what gets sent must be what is
    /// saved.</para>
    ///
    /// <para>⭐⭐ <b>It RETURNS the refusal instead of announcing it</b>, which is the one difference from
    /// its sibling and the reason it is a separate method. The bulk preview is rebuilt on every keystroke
    /// in the search box, so a producer that put its own warning on the strip would re-raise it dozens of
    /// times while the operator typed. The caller decides when it is worth saying.</para>
    /// </summary>
    public BulkSendSettings PrepareBulkSend()
    {
        if (_smtpStore is null)
        {
            return BulkSendSettings.Refused(StatusMessage.Warning(StatusCatalog.EmailIsWindowsOnly));
        }

        SmtpSettingsLoad load;
        try
        {
            load = _smtpStore.Load();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return BulkSendSettings.Refused(
                StatusMessage.Error(StatusCatalog.EmailSettingsNotRead, e.Message));
        }

        // ⭐ The store's four states, answered separately — the reason it reports them (§48.3).
        // ⚠ PasswordUnavailable is NOT among the refusals, exactly as it is not one on the single send
        //   path: the messages can still be composed, and an attempt will fail with the SERVER's own words
        //   rather than with our guess about them.
        return load.State switch
        {
            SmtpSettingsState.NotConfigured =>
                BulkSendSettings.Refused(StatusMessage.Warning(StatusCatalog.EmailNotConfigured)),

            SmtpSettingsState.Unreadable =>
                BulkSendSettings.Refused(StatusMessage.Error(
                    load.Problem is { } unreadable
                        ? unreadable.Key
                        : StatusCatalog.EmailSettingsNotReadNothingSent)),

            _ => BulkSendSettings.Ready(load.Settings),
        };
    }

    /// <summary>
    /// Re-exports the artifact the operator selected in the history, byte-for-byte.
    ///
    /// <para>⭐ The natural extension of "Export latest…", and deliberately the SAME writer:
    /// <c>IssuingWorkflow.SaveArtifact</c> from the STORED token, which is what keeps a re-export from
    /// becoming a re-issue with a new <c>iat</c> the client would treat as a replacement (§16.4). ⛔ No
    /// second copy of the save logic, no second audit action — the export is recorded by the workflow
    /// exactly as the other one is.</para>
    ///
    /// <para>⚠ "Export latest…" keeps its own meaning untouched. The two answer different questions —
    /// <i>"send them their file"</i> versus <i>"send them THIS one"</i> — and collapsing them would make
    /// the common case depend on a selection the operator did not make.</para>
    /// </summary>
    [RelayCommand]
    private async Task ExportSelectedArtifactAsync()
    {
        if (History.SelectedArtifact is not { } selected)
        {
            Message = StatusMessage.Warning(StatusCatalog.SelectIssueFromHistoryFirst);
            return;
        }

        var path = SaveFilePicker is null
            ? null
            : await SaveFilePicker(LicenseConstants.DeliveredFileName).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        try
        {
            _workflow.SaveArtifact(selected.Artifact, path);
            Message = StatusMessage.Success(
                StatusCatalog.IssueExported, selected.Ordinal, selected.IssuedAt, path);
        }
        catch (System.IO.IOException e)
        {
            Message = StatusMessage.Warning(StatusCatalog.FileNotWritten, e.Message);
        }
    }

    // ── Chrome ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Which key is signing, shown so the operator can see it at a glance.</summary>
    public string SigningKeyId => _session.KeyId;

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private bool TryReadTerms(
        out DateTimeOffset notBefore, out DateTimeOffset expiresAt, out LocalizedText? problem)
    {
        notBefore = default;
        expiresAt = default;

        if (LicenseSeats < 1)
        {
            problem = new LocalizedText(StatusCatalog.TermsSeatsRequired);
            return false;
        }

        // ⚠ The picker can be EMPTY — cleared by hand, or never filled on a licence whose terms were
        //   only half entered. Empty is a different fault from malformed, and it is now the only one that
        //   can reach here: text that does not parse never becomes a SelectedDate at all.
        if (LicenseNotBefore is not { } startDate)
        {
            problem = new LocalizedText(StatusCatalog.TermsStartDateRequired);
            return false;
        }

        if (LicenseExpiresAt is not { } endDate)
        {
            problem = new LocalizedText(StatusCatalog.TermsExpiryDateRequired);
            return false;
        }

        // ⭐ Both conversions go through LicenseDay, which is the ONE owner of "a chosen day as a UTC
        //   day" and of "the expiry runs to the END of it". ⚠ The batch renewal picks a date too, and a
        //   second copy of that arithmetic here is exactly how the two surfaces would come to differ by
        //   a day for licences that happened to go through the other one.
        notBefore = LicenseDay.StartOf(startDate);
        expiresAt = LicenseDay.EndOf(endDate);

        if (expiresAt <= notBefore)
        {
            problem = new LocalizedText(StatusCatalog.TermsExpiryMustFollowStart);
            return false;
        }

        problem = null;
        return true;
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class RecordCollectionExtensions
{
    internal static CustomerRecord? FirstOrDefaultById(
        this ObservableCollection<CustomerRecord> source, string customerId)
    {
        foreach (var item in source)
        {
            if (string.Equals(item.CustomerId, customerId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    internal static LicenseRecord? FirstOrDefaultById(
        this ObservableCollection<LicenseRecord> source, string licenseId)
    {
        foreach (var item in source)
        {
            if (string.Equals(item.LicenseId, licenseId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }
}
