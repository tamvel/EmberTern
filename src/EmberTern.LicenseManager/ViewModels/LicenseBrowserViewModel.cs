using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One option in a filter dropdown: a label the operator reads, and the narrowing it stands for.
///
/// <para>⭐ The narrowing is DATA, not a delegate, so a test can assert that "Expiring within 30 days"
/// actually produces the query it claims to. A list of lambdas would be equally short and would move
/// the interesting half of the decision somewhere no test can look at it.</para>
/// </summary>
/// <param name="Label">What the dropdown shows.</param>
public abstract record FilterOption(string Label);

/// <summary>Narrows by the licence row's own status.</summary>
/// <param name="Label">What the dropdown shows.</param>
/// <param name="Status">One of <see cref="LicenseStatuses"/>, or <see langword="null"/> for any.</param>
public sealed record StatusFilter(string Label, string? Status) : FilterOption(Label);

/// <summary>
/// Narrows by when the licence runs out.
///
/// <para>⚠ <see cref="Expired"/> and <see cref="WithinDays"/> are not two spellings of one idea:
/// "already lapsed" is an open-ended window ending now, "expiring within 30 days" is a window that
/// deliberately EXCLUDES the ones that already lapsed — otherwise the renewal list an operator works
/// from silently mixes in customers who are already locked out.</para>
/// </summary>
/// <param name="Label">What the dropdown shows.</param>
/// <param name="WithinDays">Length of the forward window, or <see langword="null"/>.</param>
/// <param name="Expired">Only licences already past their expiry.</param>
public sealed record ExpiryFilter(string Label, int? WithinDays = null, bool Expired = false)
    : FilterOption(Label);

/// <summary>Narrows by whether an artifact was ever produced.</summary>
/// <param name="Label">What the dropdown shows.</param>
/// <param name="NeverIssued">⭐ <see langword="true"/> finds the licence somebody saved and forgot to send.</param>
public sealed record IssuingFilter(string Label, bool? NeverIssued) : FilterOption(Label);

/// <summary>
/// One row of the licences list, already in the words the operator reads.
///
/// <para>⭐ The formatting lives here rather than in the view because "expires in 12 days" is a
/// judgement about a date, not a layout decision — and a judgement is worth a test. ⚠ No Avalonia types
/// (Architecture rule 1); everything below is a string.</para>
/// </summary>
public sealed record LicenseListItem
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>What the register answered.</summary>
    public required LicenseSummary Summary { get; init; }

    /// <summary>⭐ The name signed into every artifact for this licence.</summary>
    public required string CustomerName { get; init; }

    /// <summary>The licence id, shortened for a list. The full one is on the detail strip.</summary>
    public required string ShortId { get; init; }

    /// <summary>Contractual seats (D2) — displayed, never enforced.</summary>
    public required string Seats { get; init; }

    /// <summary>The contact person, or an em dash when the customer has none recorded.</summary>
    public required string Contact { get; init; }

    /// <summary>
    /// The licence row's own status, as the REGISTER holds it.
    ///
    /// <para>⛔⛔ <b>This is the register's value, presented — never a status the UI computes.</b> There
    /// are two status vocabularies in this system and mixing them would be the one lie an administrative
    /// tool must not tell: <c>LicenseStatus</c> in <c>EmberTern.Licensing</c> is the CLIENT'S VERDICT
    /// about an artifact (Valid · Grace · Expired · NotYetValid · Invalid · VersionNotCovered), produced
    /// by <c>LicenseVerifier</c> and by nothing else; <c>LicenseStatuses</c> here is administrative
    /// bookkeeping about a licence ROW (active · blocked).</para>
    ///
    /// <para>⚠ The client verdict is deliberately NOT in this list, and the reason is cost as well as
    /// principle: it is an ECDSA verification per row, i.e. hundreds of signature checks on every
    /// keystroke. It belongs on the SELECTED licence, where "Inspect latest" already runs the real
    /// verifier. What this column shows instead is stored data; what <see cref="Standing"/> shows beside
    /// it is arithmetic on a date. Neither invents a licensing state.</para>
    /// </summary>
    public required string Status { get; init; }

    /// <summary>The expiry date, ISO.</summary>
    public required string Expiry { get; init; }

    /// <summary>
    /// ⭐ The one thing an administrator actually scans for: how long is left, or that nothing was ever
    /// sent. "Never issued" outranks the date on purpose — a licence nobody received has no meaningful
    /// remaining time.
    /// </summary>
    public required string Standing { get; init; }

    /// <summary>Builds a row as of <paramref name="now"/>.</summary>
    public static LicenseListItem From(LicenseSummary summary, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var id = summary.License.LicenseId;

        return new LicenseListItem
        {
            Summary = summary,
            CustomerName = summary.CustomerName,
            ShortId = id.Length > 12 ? id[..12] + "…" : id,
            Seats = summary.License.Seats == 1 ? "1 seat" : $"{summary.License.Seats} seats",
            Contact = DescribeContact(summary),
            // ⚠ Capitalised for display and NOT translated into another vocabulary: whatever the register
            //   stores is what the operator reads, so a value this build has never heard of still shows up
            //   rather than silently becoming "Unknown".
            Status = Capitalise(summary.License.Status),
            Expiry = summary.License.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture),
            Standing = DescribeStanding(summary, now),
        };
    }

    private static string DescribeContact(LicenseSummary summary)
    {
        var name = string.Join(
            ' ',
            new[] { summary.CustomerFirstName, summary.CustomerLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(name) ? "—" : name;
    }

    private static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string DescribeStanding(LicenseSummary summary, DateTimeOffset now)
    {
        if (summary.NeverIssued)
        {
            return "Never issued";
        }

        // ⚠ Whole days between the two DATES, not a rounded span. "Expires in 0 days" for something that
        //    lapses tonight reads as a bug; "Expires today" is what the operator means.
        var days = (int)(summary.License.ExpiresAt.UtcDateTime.Date - now.UtcDateTime.Date).TotalDays;

        return days switch
        {
            0 => "Expires today",
            1 => "Expires tomorrow",
            > 1 => $"Expires in {days} days",
            -1 => "Expired yesterday",
            _ => $"Expired {-days} days ago",
        };
    }
}

/// <summary>
/// The licences view: every licence in the register, across every customer, narrowed by free text and
/// three filters.
///
/// <para>⭐⭐ <b>It is a SEPARATE view model rather than more properties on <see cref="ShellViewModel"/>,
/// and the reason is the same one that made it a separate view.</b> The shell is organised around one
/// customer at a time — pick a customer, see their licences, edit, issue. This is organised around the
/// whole register: it crosses customers, and the operator arrives at it with a question ("who lapses
/// next month?") rather than with a name. Two organising principles in one class is how a view model
/// becomes the place every future feature is added.</para>
///
/// <para>⚠ No Avalonia types, and no message surface of its own — the shell owns the one message strip
/// this application has.</para>
/// </summary>
public sealed partial class LicenseBrowserViewModel : ObservableObject
{
    private readonly LicenseRegister _register;
    private readonly Func<DateTimeOffset> _clock;

    // ⚠ Every filter property re-queries on change, and the defaults are assigned in the constructor —
    //    which would fire four queries before the first one is wanted. The flag makes construction one
    //    query instead of five, and it is the only thing it does.
    private bool _loading = true;

    /// <summary>Creates the browser over the register.</summary>
    public LicenseBrowserViewModel(LicenseRegister register, Func<DateTimeOffset>? clock = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        SelectedStatus = StatusFilters[0];
        SelectedExpiry = ExpiryFilters[0];
        SelectedIssuing = IssuingFilters[0];

        _loading = false;
        Refresh();
    }

    /// <summary>What the list currently shows.</summary>
    public ObservableCollection<LicenseListItem> Results { get; } = [];

    /// <summary>⛔ <c>superseded</c> is not here — it is a fact about an artifact, not about a licence row.</summary>
    public IReadOnlyList<StatusFilter> StatusFilters { get; } =
    [
        new("Any status", null),
        new("Active", LicenseStatuses.Active),
        new("Blocked", LicenseStatuses.Blocked),
    ];

    /// <summary>The renewal windows an administrator actually works from.</summary>
    public IReadOnlyList<ExpiryFilter> ExpiryFilters { get; } =
    [
        new("Any expiry"),
        new("Already expired", Expired: true),
        new("Expiring within 30 days", WithinDays: 30),
        new("Expiring within 90 days", WithinDays: 90),
        new("Expiring within a year", WithinDays: 365),
    ];

    /// <summary>Whether an artifact was ever produced.</summary>
    public IReadOnlyList<IssuingFilter> IssuingFilters { get; } =
    [
        new("Issued or not", null),
        new("Issued at least once", false),
        new("Never issued", true),
    ];

    /// <summary>Free text over customer name, e-mail, identifier, licence id and licence notes.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Which status narrowing is active.</summary>
    [ObservableProperty]
    private StatusFilter _selectedStatus = null!;

    /// <summary>Which expiry window is active.</summary>
    [ObservableProperty]
    private ExpiryFilter _selectedExpiry = null!;

    /// <summary>Which issuing narrowing is active.</summary>
    [ObservableProperty]
    private IssuingFilter _selectedIssuing = null!;

    /// <summary>The row the operator is looking at, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedLicenseId))]
    [NotifyPropertyChangedFor(nameof(SelectedDetail))]
    private LicenseListItem? _selectedLicense;

    /// <summary>What the list is showing, in one line.</summary>
    [ObservableProperty]
    private string _resultSummary = string.Empty;

    /// <summary>Whether anything matched. ⭐ Drives the empty state rather than an empty box.</summary>
    [ObservableProperty]
    private bool _hasResults;

    /// <summary>Whether any narrowing is in force — what makes an empty result explainable.</summary>
    public bool IsFiltered =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        SelectedStatus?.Status is not null ||
        SelectedExpiry?.WithinDays is not null ||
        SelectedExpiry?.Expired == true ||
        SelectedIssuing?.NeverIssued is not null;

    /// <summary>Whether a row is selected.</summary>
    public bool HasSelection => SelectedLicense is not null;

    /// <summary>The full licence id of the selection — the list only has room for a short one.</summary>
    public string SelectedLicenseId => SelectedLicense?.Summary.License.LicenseId ?? string.Empty;

    /// <summary>The selection's issuing history in one line.</summary>
    public string SelectedDetail
    {
        get
        {
            if (SelectedLicense?.Summary is not { } summary)
            {
                return string.Empty;
            }

            var issuing = summary.NeverIssued
                ? "never issued"
                : summary.ArtifactCount == 1
                    ? "issued once, on " + Stamp(summary.LastIssuedAt)
                    : $"issued {summary.ArtifactCount} times, last on {Stamp(summary.LastIssuedAt)}";

            return $"{summary.CustomerName} — {issuing}.";
        }
    }

    /// <summary>Re-runs the query. ⭐ The one path that fills the list; every filter change lands here.</summary>
    public void Refresh()
    {
        if (_loading)
        {
            return;
        }

        var now = _clock();
        var keep = SelectedLicense?.Summary.License.LicenseId;

        var rows = _register.QueryLicenses(BuildQuery(now));

        Results.Clear();
        foreach (var row in rows)
        {
            Results.Add(LicenseListItem.From(row, now));
        }

        // ⭐ Selection survives a keystroke. Without this, typing one more character into the search box
        //    clears the detail strip the operator is reading.
        SelectedLicense = keep is null ? null : FindById(keep);

        HasResults = Results.Count > 0;
        ResultSummary = Describe(Results.Count);
        OnPropertyChanged(nameof(IsFiltered));
    }

    /// <summary>Builds the query the current filters stand for. ⭐ Public so a test can read it.</summary>
    public LicenseQuery BuildQuery(DateTimeOffset now)
    {
        var query = new LicenseQuery
        {
            Text = SearchText,
            Status = SelectedStatus?.Status,
            NeverIssued = SelectedIssuing?.NeverIssued,
        };

        if (SelectedExpiry is { Expired: true })
        {
            return query with { ExpiresBefore = now };
        }

        // ⭐ ExpiresFrom is what keeps "expiring within 30 days" from also listing everyone who lapsed
        //    last year — two different jobs, and mixing them makes the renewal list untrustworthy.
        return SelectedExpiry?.WithinDays is { } days
            ? query with { ExpiresFrom = now, ExpiresBefore = now.AddDays(days) }
            : query;
    }

    /// <summary>Drops every narrowing and shows everything again.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        _loading = true;

        SearchText = string.Empty;
        SelectedStatus = StatusFilters[0];
        SelectedExpiry = ExpiryFilters[0];
        SelectedIssuing = IssuingFilters[0];

        _loading = false;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedStatusChanged(StatusFilter value) => Refresh();

    partial void OnSelectedExpiryChanged(ExpiryFilter value) => Refresh();

    partial void OnSelectedIssuingChanged(IssuingFilter value) => Refresh();

    private LicenseListItem? FindById(string licenseId)
    {
        foreach (var item in Results)
        {
            if (string.Equals(item.Summary.License.LicenseId, licenseId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private string Describe(int count) => count switch
    {
        0 when IsFiltered => "No licence matches these filters.",
        0 => "The register holds no licences yet.",
        1 => "1 licence.",
        _ => $"{count} licences.",
    };

    private static string Stamp(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
}
