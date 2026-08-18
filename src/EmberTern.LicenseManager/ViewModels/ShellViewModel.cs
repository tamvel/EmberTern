using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;

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

    /// <summary>
    /// Asks the platform where to save. Takes a suggested file name, returns the chosen path or
    /// <see langword="null"/> when the operator cancelled. Assigned by the view.
    /// </summary>
    public Func<string, Task<string?>>? SaveFilePicker { get; set; }

    /// <summary>Creates the shell.</summary>
    public ShellViewModel(
        LicenseRegister register, SigningSession session, Func<DateTimeOffset>? clock = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _workflow = new IssuingWorkflow(register, _clock);
        Browser = new LicenseBrowserViewModel(register, _clock);
        History = new ArtifactHistoryViewModel(register, _workflow, session);
        BatchRenewal = new BatchRenewalViewModel(
            register, _workflow, session, Browser, message => Message = message);

        ReloadCustomers();
        Message = StatusMessage.Info($"Signing with key {session.KeyId}.");
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
            Message = StatusMessage.Warning("Select a licence in the list first.");
            return;
        }

        var customer = Customers.FirstOrDefaultById(summary.License.CustomerId);
        if (customer is null)
        {
            // Unreachable while the register is sound; CheckIntegrity reports exactly this shape.
            Message = StatusMessage.Error(
                $"Licence {summary.License.LicenseId} names customer {summary.License.CustomerId}, " +
                "which is not in the register.");
            return;
        }

        IsLicensesView = false;
        SelectedCustomer = customer;
        SelectedLicense = Licenses.FirstOrDefaultById(summary.License.LicenseId);
    }

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

        Message = StatusMessage.Info("New customer. The name is required — it is what gets signed.");
    }

    /// <summary>Creates or updates the customer.</summary>
    [RelayCommand]
    private void SaveCustomer()
    {
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            Message = StatusMessage.Warning(
                "A customer name is required. It is signed into every licence and shown in their EmberTern.");
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
        Message = StatusMessage.Success($"Saved {saved.Name}.");
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
            Message = StatusMessage.Warning("Select or save a customer first.");
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
        Message = StatusMessage.Info("New licence. Save the terms, then issue.");
    }

    /// <summary>Creates or updates the licence terms.</summary>
    [RelayCommand]
    private void SaveLicense()
    {
        if (SelectedCustomer is null)
        {
            Message = StatusMessage.Warning("Select a customer first.");
            return;
        }

        if (!TryReadTerms(out var notBefore, out var expiresAt, out var problem))
        {
            Message = StatusMessage.Warning(problem);
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
        Message = StatusMessage.Success($"Saved licence {saved.LicenseId[..8]}….");
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
            IssueReasonChoices.Add(new IssueReasonOption(
                reason, ReasonText.Describe(reason), ReasonText.Explain(reason)));
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
            Message = StatusMessage.Warning("Select a saved licence to issue.");
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
                "Choose why this licence is being issued again before signing a new artifact.");
            return;
        }

        if (IssueReasonPolicy.Refuse(reason, change) is { } refusal)
        {
            Message = StatusMessage.Warning(refusal);
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
            Message = StatusMessage.Error($"The licence could not be issued: {e.Message}");
            return;
        }
        catch (System.Security.Cryptography.CryptographicException e)
        {
            // The issuer refused to hand out an artifact it could not verify. This is a key or format
            // fault, and it is the one error here that is ours rather than the operator's.
            Message = StatusMessage.Error(e.Message);
            return;
        }

        OnSelectedLicenseChanged(SelectedLicense);

        var path = SaveFilePicker is null
            ? null
            : await SaveFilePicker(LicenseConstants.DeliveredFileName).ConfigureAwait(true);

        if (path is null)
        {
            Message = StatusMessage.Success(
                $"Issued and recorded for {SelectedCustomer.Name}. Not saved to disk — " +
                "it can be exported from the register at any time.");
            return;
        }

        try
        {
            _workflow.SaveArtifact(result.Artifact, path);
            Message = StatusMessage.Success($"Issued for {SelectedCustomer.Name} and saved to {path}.");
        }
        catch (System.IO.IOException e)
        {
            Message = StatusMessage.Warning(
                $"Issued and recorded, but the file could not be written: {e.Message}");
        }
    }

    /// <summary>Re-exports the most recent artifact, byte-for-byte, without signing anything new.</summary>
    [RelayCommand]
    private async Task ExportLatestAsync()
    {
        if (SelectedLicense is null)
        {
            Message = StatusMessage.Warning("Select a licence.");
            return;
        }

        var artifacts = _register.GetArtifacts(SelectedLicense.LicenseId);
        if (artifacts.Count == 0)
        {
            Message = StatusMessage.Warning("This licence has never been issued.");
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
        Message = StatusMessage.Success($"Exported the stored artifact to {path}.");
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
            Message = StatusMessage.Warning("Select a licence.");
            return;
        }

        var current = _register.GetCurrentArtifact(SelectedLicense.LicenseId);
        if (current is null)
        {
            Message = StatusMessage.Warning("This licence has never been issued.");
            return;
        }

        History.SelectCurrent();
        Message = VerdictText.Describe(_workflow.Inspect(_session, current));
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
            Message = StatusMessage.Warning("Select an issue from the history first.");
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
                $"Exported issue {selected.Ordinal} of {selected.IssuedAt} to {path}.");
        }
        catch (System.IO.IOException e)
        {
            Message = StatusMessage.Warning($"The file could not be written: {e.Message}");
        }
    }

    // ── Chrome ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Which key is signing, shown so the operator can see it at a glance.</summary>
    public string SigningKeyId => _session.KeyId;

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private bool TryReadTerms(
        out DateTimeOffset notBefore, out DateTimeOffset expiresAt, out string problem)
    {
        notBefore = default;
        expiresAt = default;

        if (LicenseSeats < 1)
        {
            problem = "A licence must carry at least one seat.";
            return false;
        }

        // ⚠ The picker can be EMPTY — cleared by hand, or never filled on a licence whose terms were
        //   only half entered. Empty is a different fault from malformed, and it is now the only one that
        //   can reach here: text that does not parse never becomes a SelectedDate at all.
        if (LicenseNotBefore is not { } startDate)
        {
            problem = "A start date is required. Pick one from the calendar, or type it into the field.";
            return false;
        }

        if (LicenseExpiresAt is not { } endDate)
        {
            problem = "An expiry date is required. Pick one from the calendar, or type it into the field.";
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
            problem = "The expiry must be after the start date.";
            return false;
        }

        problem = string.Empty;
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
