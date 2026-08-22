namespace EmberTern.LicenseManager.Services;

/// <summary>
/// How a licence id is shortened for a list.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>One owner for one rule, and it exists because L10.2 would otherwise have been its THIRD
/// copy.</b> The expression <c>id.Length &gt; 12 ? id[..12] + "…" : id</c> was written out in
/// <c>LicenseListItem</c> (L5.1) and again in <c>BatchRenewalCandidate</c> (L5.4), identically both times.
/// Two copies of an abbreviation are survivable; a third is the point at which "identical" stops being a
/// fact and becomes a hope. Both existing call sites were migrated here in the same step — a purely
/// mechanical change with no behavioural difference, which their own tests already pin.</para>
///
/// <para>⚠ It is <b>presentation, not identity</b>. ⛔ Nothing may sort, compare, look up or persist a
/// shortened id: <c>LicenseListItem</c> carries a separate full <c>LicenseId</c> for the grid to sort on
/// precisely because sorting on a truncated key orders two licences by where the ellipsis fell.</para>
///
/// <para>⚠ It lives in <c>Services</c> rather than in <c>ViewModels</c> because a <c>Services</c> type
/// (<c>BatchRenewalCandidate</c>) uses it, and a view-model home would invert that. ⛔ And not in
/// <c>Data</c>: <c>RegisterRecords.cs</c> holds what the register PERSISTS, and how many characters a list
/// shows is not that.</para>
/// </remarks>
public static class LicenceIdText
{
    /// <summary>How many characters of an id a list shows before the ellipsis.</summary>
    /// <remarks>
    /// ⭐ 12 is the figure L5.1 chose and L5.4 matched. It is wide enough that two ids in one register are
    /// distinguishable by eye and narrow enough for a grid column; ⚠ it is not a uniqueness guarantee, and
    /// nothing here pretends otherwise — see the type remarks.
    /// </remarks>
    public const int ShortLength = 12;

    /// <summary>The id as a list shows it: the first <see cref="ShortLength"/> characters, then an ellipsis.</summary>
    /// <remarks>⚠ An id short enough already is returned unchanged, with no ellipsis to suggest hidden text.</remarks>
    public static string Short(string licenseId) =>
        licenseId is { Length: > ShortLength }
            ? licenseId[..ShortLength] + "…"
            : licenseId;
}
