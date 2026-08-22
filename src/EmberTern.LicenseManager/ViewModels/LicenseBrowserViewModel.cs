using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Data;

using EmberTern.LicenseManager.Localization;
namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One option in a filter dropdown: the narrowing it stands for, and the label the operator reads.
///
/// <para>⭐ The narrowing is DATA, not a delegate, so a test can assert that "Expiring within 30 days"
/// actually produces the query it claims to. A list of lambdas would be equally short and would move
/// the interesting half of the decision somewhere no test can look at it.</para>
///
/// <para>⭐⭐ <b>The label is an abstract PROJECTION, never a member of the identity.</b> A
/// <c>record</c> compares by every positional member, and <c>ComboBox.SelectedItem</c> matches by
/// equality — so a label carried in the primary constructor would put the current language inside the
/// option's identity. Rebuild these three lists in another language and each dropdown's selection equals
/// nothing in its own list, so all three silently blank and the licences view reverts to unfiltered
/// without saying so. ⛔ Do not move a word back into a positional parameter here.</para>
/// </summary>
public abstract record FilterOption
{
    /// <summary>What the dropdown shows. ⭐ Derived from the narrowing, resolved at read time.</summary>
    public abstract string Label { get; }

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

/// <summary>Narrows by the licence row's own status.</summary>
/// <param name="Status">
/// One of <see cref="LicenseStatuses"/>, or <see langword="null"/> for any. ⭐ This IS the option.
/// </param>
public sealed record StatusFilter(string? Status) : FilterOption
{
    /// <inheritdoc />
    /// <remarks>
    /// ⭐ Each arm is a lookup, exactly as this comment predicted before L8.4 — and the two status words
    /// come from <see cref="LicenceStatusText"/> rather than from here, because the licences list's Status
    /// column reads the same dictionary, and two owners is how a column and a filter start disagreeing.
    /// </remarks>
    public override string Label => Status switch
    {
        LicenseStatuses.Active => LicenceStatusText.Active,
        LicenseStatuses.Blocked => LicenceStatusText.Blocked,
        _ => FilterCatalog.AnyStatus,
    };
}

/// <summary>
/// Narrows by when the licence runs out.
///
/// <para>⚠ <see cref="Expired"/> and <see cref="WithinDays"/> are not two spellings of one idea:
/// "already lapsed" is an open-ended window ending now, "expiring within 30 days" is a window that
/// deliberately EXCLUDES the ones that already lapsed — otherwise the renewal list an operator works
/// from silently mixes in customers who are already locked out.</para>
/// </summary>
/// <param name="WithinDays">Length of the forward window, or <see langword="null"/>.</param>
/// <param name="Expired">Only licences already past their expiry.</param>
public sealed record ExpiryFilter(int? WithinDays = null, bool Expired = false) : FilterOption
{
    /// <inheritdoc />
    /// <remarks>
    /// ⭐ Each offered option is a WHOLE sentence rather than "Expiring within " + a number: 365 is
    /// already worded as "a year" rather than as its own digit count, so the set was never composable —
    /// and a whole string is what a translator needs anyway, because word order is their decision.
    /// ⚠ The composing default covers a window this list does not currently offer; it is a fallback, not
    /// the pattern.
    /// </remarks>
    public override string Label => (WithinDays, Expired) switch
    {
        (null, true) => FilterCatalog.AlreadyExpired,
        (null, false) => FilterCatalog.AnyExpiry,
        (30, _) => FilterCatalog.ExpiringWithin30Days,
        (90, _) => FilterCatalog.ExpiringWithin90Days,
        (365, _) => FilterCatalog.ExpiringWithinAYear,
        var (days, _) => FilterCatalog.ExpiringWithinDays(days!.Value),
    };
}

/// <summary>Narrows by whether an artifact was ever produced.</summary>
/// <param name="NeverIssued">⭐ <see langword="true"/> finds the licence somebody saved and forgot to send.</param>
public sealed record IssuingFilter(bool? NeverIssued) : FilterOption
{
    /// <inheritdoc />
    public override string Label => NeverIssued switch
    {
        true => FilterCatalog.NeverIssued,
        false => FilterCatalog.IssuedAtLeastOnce,
        _ => FilterCatalog.IssuedOrNot,
    };
}

/// <summary>
/// One row of the licences list, already in the words the operator reads.
///
/// <para>⭐ The formatting lives here rather than in the view because "expires in 12 days" is a
/// judgement about a date, not a layout decision — and a judgement is worth a test. ⚠ No Avalonia types
/// (Architecture rule 1); everything below is a string or a bool.</para>
///
/// <para>⚠⚠ <b>An observable CLASS rather than a record, since L5.4, and only because of
/// <see cref="IsChecked"/>.</b> A batch tick is state the ROW carries and the operator toggles, so it has
/// to notify; every other member here is still an immutable projection of what the register said.</para>
/// </summary>
public sealed partial class LicenseListItem : ObservableObject
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>What the register answered.</summary>
    public required LicenseSummary Summary { get; init; }

    /// <summary>
    /// ⭐⭐ Whether this licence is ticked for a batch operation.
    ///
    /// <para><b>Deliberately independent of the list's own selection.</b> Row selection answers "which
    /// licence am I looking at?" and changes on every click; a batch tick answers "which licences am I
    /// about to change?" and must survive one. Tying the two together would mean a stray click on row
    /// four throws away nineteen decisions — which is the one failure a bulk operation cannot have.</para>
    ///
    /// <para>⚠ The row is rebuilt on every <see cref="LicenseBrowserViewModel.Refresh"/>, so this value
    /// is RESTORED from the browser's id set rather than being the authority for it.</para>
    /// </summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>⭐ The name signed into every artifact for this licence.</summary>
    public required string CustomerName { get; init; }

    /// <summary>The licence id, shortened for a list. The full one is on the detail strip.</summary>
    public required string ShortId { get; init; }

    /// <summary>
    /// ⭐ The FULL licence id, for the grid to sort on.
    ///
    /// <para>⚠ Not the same value the column shows. <see cref="ShortId"/> is truncated for width, and
    /// sorting a list on a truncated key is a list that orders two licences by where the ellipsis fell.
    /// The identity is what sorts; the abbreviation is what is read.</para>
    /// </summary>
    public string LicenseId => Summary.License.LicenseId;

    /// <summary>Contractual seats (D2) — displayed, never enforced.</summary>
    public required string Seats { get; init; }

    /// <summary>
    /// ⭐⭐ The seat count as a NUMBER, for the grid to sort on.
    ///
    /// <para>⚠ <see cref="Seats"/> is a sentence ("5 seats"), and text order is not seat order: sorted as
    /// text, "10 seats" lands before "3 seats". A column that shows a count and sorts it alphabetically
    /// is worse than a column that does not sort at all — it answers the operator's question with a
    /// confident wrong answer.</para>
    /// </summary>
    public int SeatsValue => Summary.License.Seats;

    /// <summary>
    /// The expiry as a DATE, for the grid to sort on.
    ///
    /// <para>⚠ <see cref="Expiry"/> happens to sort correctly as text because it is ISO — which is luck
    /// rather than design, and luck that ends the day the format is localised.</para>
    /// </summary>
    public DateTimeOffset ExpiresAtValue => Summary.License.ExpiresAt;

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
            // ⭐ Through the one owner of that rule since L10.2 — see `LicenceIdText`.
            ShortId = Services.LicenceIdText.Short(id),
            Seats = RowCatalog.Seats(summary.License.Seats),
            Contact = DescribeContact(summary),
            // ⭐⭐ §53.6 obligation 2, discharged. This used to be `Capitalise(summary.License.Status)` — a
            //    presentation built by upper-casing the PERSISTED value, which no other language can
            //    reach. The stored value is unchanged; only the reading of it moved to its one owner, and
            //    a value this build has never heard of is still echoed capitalised exactly as before.
            Status = LicenceStatusText.Describe(summary.License.Status),
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

    private static string DescribeStanding(LicenseSummary summary, DateTimeOffset now)
    {
        if (summary.NeverIssued)
        {
            return RowCatalog.StandingNeverIssued;
        }

        // ⚠ Whole days between the two DATES, not a rounded span. "Expires in 0 days" for something that
        //    lapses tonight reads as a bug; "Expires today" is what the operator means.
        var days = (int)(summary.License.ExpiresAt.UtcDateTime.Date - now.UtcDateTime.Date).TotalDays;

        return days switch
        {
            0 => RowCatalog.StandingExpiresToday,
            1 => RowCatalog.StandingExpiresTomorrow,
            > 1 => RowCatalog.StandingExpiresInDays(days),
            -1 => RowCatalog.StandingExpiredYesterday,
            _ => RowCatalog.StandingExpiredDaysAgo(-days),
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

        // ⭐⭐ Every row is BUILT, not bound: `LicenseListItem` carries `required … { get; init; }`
        //    strings, and the grid's columns bind straight to them. So the only way a language change
        //    reaches this list is to build it again — and `Refresh` already restores the selection and
        //    the batch ticks, which is exactly what makes rebuilding safe here.
        // ⚠ Weak, with a static handler: `Loc.LanguageChanged` is a static event, i.e. a GC root.
        //    See LanguageChange.SubscribeWeak.
        LanguageChange.SubscribeWeak(this, static browser => browser.Refresh());
    }

    /// <summary>What the list currently shows.</summary>
    public ObservableCollection<LicenseListItem> Results { get; } = [];

    // ── Batch ticks (L5.4) ──────────────────────────────────────────────────────────────────────────

    /// <summary>Raised whenever the ticked set changes — what the batch preview rebuilds on.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>
    /// ⭐⭐ <b>The ticked set survives a filter change, and that is deliberate.</b>
    ///
    /// <para><see cref="Refresh"/> runs on EVERY keystroke in the search box, and it REBUILDS the rows.
    /// If the tick lived only on the row, one more character typed after ticking twenty licences would
    /// silently throw all twenty away. So the authority is a set of licence IDS held here, and every
    /// rebuilt row has its checkbox restored from it.</para>
    ///
    /// <para>⚠ The consequence is stated rather than hidden: a licence can be ticked and not currently
    /// listed. <see cref="CheckedNotShown"/> puts that on screen in words, and the batch preview lists
    /// every ticked licence by name — so "what will run" is never narrower or wider than what the operator
    /// can read.</para>
    /// </summary>
    private readonly Dictionary<string, LicenseSummary> _checked = new(StringComparer.Ordinal);

    // ⚠ Set while this view model is the one writing IsChecked onto rows, so a restore is not read back
    //   as an operator's decision — and so one command raises ONE change notification, not twenty.
    private bool _syncingChecks;

    /// <summary>
    /// The IDS of every ticked licence.
    ///
    /// <para>⭐⭐ <b>Ids, not the rows.</b> The rows here are a SNAPSHOT taken when the licence was ticked,
    /// and a consumer that planned an operation from them would be planning against whatever the register
    /// held at that moment. Handing out identities instead forces the consumer to read the register when
    /// it acts — which is also what makes <c>BatchRenewalPlan.Matches</c> able to notice that something
    /// changed, rather than comparing one cached reading against itself.</para>
    /// </summary>
    public IReadOnlyCollection<string> CheckedIds => _checked.Keys;

    /// <summary>How many licences are ticked.</summary>
    public int CheckedCount => _checked.Count;

    /// <summary>⚠ How many ticked licences the current filters are hiding. Shown, never silently dropped.</summary>
    public int CheckedNotShown
    {
        get
        {
            var shown = 0;
            foreach (var item in Results)
            {
                if (_checked.ContainsKey(item.Summary.License.LicenseId))
                {
                    shown++;
                }
            }

            return _checked.Count - shown;
        }
    }

    /// <summary>Whether anything is ticked.</summary>
    public bool HasChecked => _checked.Count > 0;

    /// <summary>The ticked set in one line, including what the filters are hiding.</summary>
    public string CheckSummary
    {
        get
        {
            if (_checked.Count == 0)
            {
                return LicencesCatalog.NoneSelected;
            }

            // ⭐ Two WHOLE sentences rather than a fragment plus a tail. The old shape appended "." or
            //    spliced the fragment into a longer clause, which left word order to English.
            var hidden = CheckedNotShown;

            return hidden == 0
                ? LicencesCatalog.Checked(_checked.Count)
                : LicencesCatalog.CheckedWithHidden(_checked.Count, hidden);
        }
    }

    /// <summary>Ticks every licence the filters currently show.</summary>
    [RelayCommand]
    private void CheckAllShown()
    {
        foreach (var item in Results)
        {
            _checked[item.Summary.License.LicenseId] = item.Summary;
        }

        RestoreChecks();
        RaiseCheckedChanged();
    }

    /// <summary>Unticks everything, including licences the filters are hiding.</summary>
    [RelayCommand]
    private void ClearChecks()
    {
        _checked.Clear();
        RestoreChecks();
        RaiseCheckedChanged();
    }

    // ⭐ One row's checkbox, toggled by the operator. A row the filters hide has no checkbox to click, so
    //    its tick is left exactly as it was — which is what makes the set survive a keystroke.
    private void OnRowCheckedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingChecks ||
            !string.Equals(e.PropertyName, nameof(LicenseListItem.IsChecked), StringComparison.Ordinal) ||
            sender is not LicenseListItem item)
        {
            return;
        }

        var id = item.Summary.License.LicenseId;

        if (item.IsChecked)
        {
            _checked[id] = item.Summary;
        }
        else
        {
            _checked.Remove(id);
        }

        RaiseCheckedChanged();
    }

    // ⚠ Rows are rebuilt on every Refresh, so their checkboxes start false and have to be told again.
    private void RestoreChecks()
    {
        _syncingChecks = true;
        try
        {
            foreach (var item in Results)
            {
                item.IsChecked = _checked.ContainsKey(item.Summary.License.LicenseId);
            }
        }
        finally
        {
            _syncingChecks = false;
        }
    }

    private void RaiseCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CheckedNotShown));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckSummary));
        OnPropertyChanged(nameof(CheckedIds));
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>⛔ <c>superseded</c> is not here — it is a fact about an artifact, not about a licence row.</summary>
    public IReadOnlyList<StatusFilter> StatusFilters { get; } =
    [
        new(null),
        new(LicenseStatuses.Active),
        new(LicenseStatuses.Blocked),
    ];

    /// <summary>The renewal windows an administrator actually works from.</summary>
    public IReadOnlyList<ExpiryFilter> ExpiryFilters { get; } =
    [
        new(),
        new(Expired: true),
        new(WithinDays: 30),
        new(WithinDays: 90),
        new(WithinDays: 365),
    ];

    /// <summary>Whether an artifact was ever produced.</summary>
    public IReadOnlyList<IssuingFilter> IssuingFilters { get; } =
    [
        new(null),
        new(false),
        new(true),
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

            // ⭐ Three WHOLE sentences. The clause used to be composed here and spliced into a shared
            //    tail — legible in English, and unassignable to a translator.
            return summary.NeverIssued
                ? LicencesCatalog.DetailNeverIssued(summary.CustomerName)
                : summary.ArtifactCount == 1
                    ? LicencesCatalog.DetailIssuedOnce(
                        summary.CustomerName, Stamp(summary.LastIssuedAt))
                    : LicencesCatalog.DetailIssuedTimes(
                        summary.CustomerName, summary.ArtifactCount, Stamp(summary.LastIssuedAt));
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

        foreach (var stale in Results)
        {
            stale.PropertyChanged -= OnRowCheckedChanged;
        }

        Results.Clear();
        foreach (var row in rows)
        {
            var item = LicenseListItem.From(row, now);
            item.PropertyChanged += OnRowCheckedChanged;
            Results.Add(item);
        }

        // ⭐ Selection survives a keystroke. Without this, typing one more character into the search box
        //    clears the detail strip the operator is reading.
        SelectedLicense = keep is null ? null : FindById(keep);

        // ⭐ And so do the TICKS — restored from the id set onto the freshly built rows, so a narrowed
        //    filter hides rows without unticking them (see the `_checked` remarks).
        RestoreChecks();

        HasResults = Results.Count > 0;
        ResultSummary = Describe(Results.Count);
        OnPropertyChanged(nameof(IsFiltered));
        RaiseCheckedChanged();
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
        0 when IsFiltered => LicencesCatalog.NoneMatchesTheseFilters,
        0 => LicencesCatalog.RegisterHoldsNoneYet,

        // ⭐ One counted key rather than two arms: English already had both forms, so `one` / `other`
        //   reproduce exactly what was rendered — and Polish gets its third form without a code change.
        _ => LicencesCatalog.Count(count),
    };

    private static string Stamp(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
}
