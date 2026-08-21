using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The narrowing options the licences view offers, as words.
///
/// <para>⭐ A catalog of its own rather than more members on <see cref="LicencesCatalog"/>: a filter names
/// a QUESTION the operator is asking ("who lapses next month?"), while the sentences next door describe
/// what the list came back with. The two are read at different moments and translated differently — a
/// filter is a noun phrase, a result is a sentence.</para>
///
/// <para>⛔ Every member here is a PROPERTY or a method whose NAME is its key. A <c>const</c> is inlined by
/// the compiler and a <c>static readonly</c> freezes in the first language — see <see cref="Loc"/>.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class FilterCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Filter.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>No narrowing by the licence row's status.</summary>
    public static string AnyStatus => Word(nameof(AnyStatus));

    /// <summary>Only licences already past their expiry.</summary>
    public static string AlreadyExpired => Word(nameof(AlreadyExpired));

    /// <summary>No narrowing by expiry.</summary>
    public static string AnyExpiry => Word(nameof(AnyExpiry));

    /// <summary>The one-month renewal window.</summary>
    public static string ExpiringWithin30Days => Word(nameof(ExpiringWithin30Days));

    /// <summary>The three-month renewal window.</summary>
    public static string ExpiringWithin90Days => Word(nameof(ExpiringWithin90Days));

    /// <summary>The one-year renewal window.</summary>
    /// <remarks>
    /// ⚠ Worded as "a year" rather than as its own digit count, which is why the offered set was never
    /// composable from a number — and why each option is a whole phrase (see <see cref="ExpiringWithinDays"/>).
    /// </remarks>
    public static string ExpiringWithinAYear => Word(nameof(ExpiringWithinAYear));

    /// <summary>A window the offered list does not currently hold. ⚠ A fallback, not the pattern.</summary>
    public static string ExpiringWithinDays(int days) =>
        Loc.Format(KeyPrefix + nameof(ExpiringWithinDays), days);

    /// <summary>Only licences no artifact was ever produced for.</summary>
    public static string NeverIssued => Word(nameof(NeverIssued));

    /// <summary>Only licences that have been issued at least once.</summary>
    public static string IssuedAtLeastOnce => Word(nameof(IssuedAtLeastOnce));

    /// <summary>No narrowing by whether an artifact exists.</summary>
    public static string IssuedOrNot => Word(nameof(IssuedOrNot));
}

/// <summary>
/// ⭐⭐ The ONE owner of the persisted <c>active</c> / <c>blocked</c> vocabulary, as words.
///
/// <para><b>It exists because the same dictionary is read by two surfaces</b> — the licences list's Status
/// column and the status filter's labels — and until L8.4 each answered for itself: the column
/// UPPER-CASED the stored value and the filter carried its own literals. Capitalising a persisted value is
/// a presentation that no other language can reach, and it fails in the silent direction: a Polish
/// interface would have rendered <c>Active</c> / <c>Blocked</c> with a green build (§53.6 obligation 2).</para>
///
/// <para>⛔⛔ <b>The persisted value is untouched.</b> <see cref="LicenseStatuses"/> is what the register
/// stores and what every query compares; this class only decides how it READS. ⛔ Never add a comparison
/// here that could let a display disagree with what was recorded.</para>
///
/// <para>⚠ An unrecognised value is still echoed CAPITALISED rather than mapped to a word for "unknown".
/// The column's vocabulary can only grow, so a register written by a later build must stay readable in an
/// older one — and that echo is exactly what the pre-L8.4 code did for every value, so nothing an operator
/// can see has changed.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class LicenceStatusText
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "LicenceStatus.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>The licence is in force as far as the register is concerned.</summary>
    public static string Active => Word(nameof(Active));

    /// <summary>The licence is administratively blocked.</summary>
    public static string Blocked => Word(nameof(Blocked));

    /// <summary>How a stored status reads. ⭐ The one place either value becomes a word.</summary>
    internal static string Describe(string status) => status switch
    {
        LicenseStatuses.Active => Active,
        LicenseStatuses.Blocked => Blocked,
        _ => Capitalise(status),
    };

    // ⚠ Kept for the UNKNOWN arm only, and deliberately: it reproduces exactly what an unrecognised value
    //    rendered as before L8.4. ⛔ It is no longer how a KNOWN status is presented — that was the defect.
    private static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>
/// What the licences view says about the list itself — how many, what is ticked, what the selection is.
///
/// <para>⭐⭐ <b>Every entry is a WHOLE sentence.</b> Before L8.4 three of these were assembled from a
/// fragment plus a shared tail ("1 licence selected" + "." or spliced into a longer clause), which is the
/// shape §55.6 forbids: word order is the translator's decision, and Polish does not put the clause where
/// English does. The combinations are enumerated instead — the count of keys went up and the number of
/// decisions a translator has to make went down.</para>
///
/// <para>⭐ The counted entries are plural FAMILIES, and only because English already had two arms. ⛔ A
/// family is never introduced where English had one form: that would invent an English variant, which
/// L8.4 may not do (⏭ L8.5).</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class LicencesCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Licences.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>Nothing matched, and a narrowing is in force — so the emptiness is explainable.</summary>
    public static string NoneMatchesTheseFilters => Word(nameof(NoneMatchesTheseFilters));

    /// <summary>Nothing matched because there is nothing to match.</summary>
    public static string RegisterHoldsNoneYet => Word(nameof(RegisterHoldsNoneYet));

    /// <summary>How many licences the list is showing.</summary>
    public static string Count(int count) => Loc.FormatCount(KeyPrefix + nameof(Count), count);

    /// <summary>Nothing is ticked for a batch operation.</summary>
    public static string NoneSelected => Word(nameof(NoneSelected));

    /// <summary>How many licences are ticked, when the filters are hiding none of them.</summary>
    public static string Checked(int count) => Loc.FormatCount(KeyPrefix + nameof(Checked), count);

    /// <summary>How many are ticked, and how many of those the filters are hiding.</summary>
    /// <remarks>⚠ Stated rather than silently dropped — a batch must never be wider than what can be read.</remarks>
    public static string CheckedWithHidden(int count, int hidden) =>
        Loc.FormatCount(KeyPrefix + nameof(CheckedWithHidden), count, hidden);

    /// <summary>The selection's issuing history in one line, for a licence nothing was ever sent for.</summary>
    public static string DetailNeverIssued(string customer) =>
        Loc.Format(KeyPrefix + nameof(DetailNeverIssued), customer);

    /// <summary>The selection's issuing history in one line, for a single issue.</summary>
    public static string DetailIssuedOnce(string customer, string stamp) =>
        Loc.Format(KeyPrefix + nameof(DetailIssuedOnce), customer, stamp);

    /// <summary>The selection's issuing history in one line, for several issues.</summary>
    public static string DetailIssuedTimes(string customer, int count, string stamp) =>
        Loc.Format(KeyPrefix + nameof(DetailIssuedTimes), customer, count, stamp);
}

/// <summary>
/// The words one licence ROW shows about its own terms and standing.
///
/// <para>⭐ Separate from <see cref="LicencesCatalog"/> because these describe a LICENCE, not the list:
/// <see cref="Seats"/> is read by the issuing history's artifact detail too, where there is no list at
/// all. ⛔ Do not fold the two together — a row's vocabulary outlives the view it first appeared in.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class RowCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Row.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>Contractual seats (D2) — displayed, never enforced.</summary>
    /// <remarks>
    /// ⭐ A plural FAMILY: English already spelled both arms ("1 seat" / "5 seats"), so <c>one</c> and
    /// <c>other</c> reproduce exactly what was rendered before. ⚠ The count is always <c>{0}</c>.
    /// </remarks>
    public static string Seats(int seats) => Loc.FormatCount(KeyPrefix + nameof(Seats), seats);

    /// <summary>⭐ Outranks the date: a licence nobody received has no meaningful remaining time.</summary>
    public static string StandingNeverIssued => Word(nameof(StandingNeverIssued));

    /// <summary>It lapses tonight.</summary>
    public static string StandingExpiresToday => Word(nameof(StandingExpiresToday));

    /// <summary>It lapses tomorrow.</summary>
    public static string StandingExpiresTomorrow => Word(nameof(StandingExpiresTomorrow));

    /// <summary>Whole days left. ⚠ Never reached for 0 or 1 — those have their own sentences.</summary>
    public static string StandingExpiresInDays(int days) =>
        Loc.Format(KeyPrefix + nameof(StandingExpiresInDays), days);

    /// <summary>It lapsed yesterday.</summary>
    public static string StandingExpiredYesterday => Word(nameof(StandingExpiredYesterday));

    /// <summary>Whole days since it lapsed.</summary>
    public static string StandingExpiredDaysAgo(int days) =>
        Loc.Format(KeyPrefix + nameof(StandingExpiredDaysAgo), days);
}
