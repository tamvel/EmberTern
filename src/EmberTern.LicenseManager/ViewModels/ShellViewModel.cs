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
/// <para>⭐ <b>Dates are typed as ISO text rather than picked from a calendar, and that is a deliberate
/// L3 decision.</b> A picker is a templated control with a flyout, i.e. the largest theming surface in
/// the application, introduced in the first stage that has any UI at all. <c>2027-08-15</c> is
/// unambiguous, verifiable, and what an administrator reads off a purchase order anyway. A picker is an
/// L5 refinement, not a correctness gap.</para>
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

        ReloadCustomers();
        Message = StatusMessage.Info($"Signing with key {session.KeyId}.");
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

    /// <summary>Start of validity, ISO.</summary>
    [ObservableProperty]
    private string _licenseNotBefore = string.Empty;

    /// <summary>End of validity, ISO.</summary>
    [ObservableProperty]
    private string _licenseExpiresAt = string.Empty;

    /// <summary>⛔ Administrative only.</summary>
    [ObservableProperty]
    private string _licenseNotes = string.Empty;

    /// <summary>How many artifacts have been signed for this licence.</summary>
    [ObservableProperty]
    private string _licenseHistory = string.Empty;

    partial void OnSelectedLicenseChanged(LicenseRecord? value)
    {
        if (value is null)
        {
            LicenseHistory = string.Empty;
            return;
        }

        LicenseId = value.LicenseId;
        LicenseSeats = value.Seats;
        LicenseNotBefore = value.NotBefore.ToString(DateFormat, CultureInfo.InvariantCulture);
        LicenseExpiresAt = value.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture);
        LicenseNotes = value.Notes ?? string.Empty;

        var artifacts = _register.GetArtifacts(value.LicenseId);
        LicenseHistory = artifacts.Count == 0
            ? "Never issued."
            : $"{artifacts.Count} issued; last on " +
              $"{artifacts[0].IssuedAt.ToString(DateFormat, CultureInfo.InvariantCulture)} " +
              $"with key {artifacts[0].KeyId}.";
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
        LicenseNotBefore = today.ToString(DateFormat, CultureInfo.InvariantCulture);
        LicenseExpiresAt = today.AddYears(1).ToString(DateFormat, CultureInfo.InvariantCulture);
        LicenseNotes = string.Empty;
        LicenseHistory = "Never issued.";
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

        IssueResult result;
        try
        {
            var reason = _register.GetArtifacts(SelectedLicense.LicenseId).Count == 0
                ? IssueReasons.Initial
                : IssueReasons.Renewal;

            result = _workflow.Issue(_session, SelectedLicense, SelectedCustomer, reason);
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

    /// <summary>Shows what EmberTern would say about the newest artifact today.</summary>
    [RelayCommand]
    private void InspectLatest()
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

        var verdict = _workflow.Inspect(_session, artifacts[0]);
        Message = verdict.Status switch
        {
            LicenseStatus.Valid => StatusMessage.Success(
                $"EmberTern would accept it: valid until " +
                $"{verdict.Payload!.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture)}, " +
                $"licensed to {verdict.Payload.Licensee}."),
            LicenseStatus.Grace => StatusMessage.Warning(
                "EmberTern would accept it, but it is past its expiry and inside the grace period."),
            LicenseStatus.Expired => StatusMessage.Warning("EmberTern would report it as expired."),
            LicenseStatus.NotYetValid => StatusMessage.Info("EmberTern would report it as not yet valid."),
            _ => StatusMessage.Error($"EmberTern would refuse it ({verdict.Failure})."),
        };
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

        if (!TryReadDate(LicenseNotBefore, out notBefore))
        {
            problem = $"The start date must be written as {DateFormat}, for example 2026-08-15.";
            return false;
        }

        if (!TryReadDate(LicenseExpiresAt, out expiresAt))
        {
            problem = $"The expiry date must be written as {DateFormat}, for example 2027-08-15.";
            return false;
        }

        // ⭐ The expiry runs to the END of the day the operator typed. Storing midnight would expire a
        //    licence at the start of the day it says it is valid until, which is an off-by-one nobody
        //    reads as a bug until a customer is locked out on a date their invoice says they own.
        expiresAt = expiresAt.AddDays(1).AddSeconds(-1);

        if (expiresAt <= notBefore)
        {
            problem = "The expiry must be after the start date.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool TryReadDate(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(
            text?.Trim(), DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

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
