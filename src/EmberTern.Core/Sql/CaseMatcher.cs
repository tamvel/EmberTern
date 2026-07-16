namespace EmberTern.Core.Sql;

/// <summary>
/// Case-preserving completion: when the user picks a completion entry, the
/// inserted text should match the case of what they already typed. IBExpert-
/// style — typing "nagl" then picking NAGL_TABLE inserts "nagl_table"; typing
/// "NAGL" inserts "NAGL_TABLE"; anything mixed inserts the candidate verbatim.
/// </summary>
public static class CaseMatcher
{
    /// <summary>
    /// Matches <paramref name="candidate"/>'s case to <paramref name="typedPrefix"/>:
    /// all-lowercase prefix → lowercase candidate; all-uppercase → uppercase;
    /// mixed → candidate verbatim (the original catalog casing wins).
    ///
    /// <para>When the prefix carries NO letters — most importantly right after a qualifier dot
    /// (<c>k.</c>), where the prefix is empty — we fall back to <paramref name="documentStyle"/>:
    /// how the user actually writes in this document (see <see cref="SqlCaseStyleDetector"/>).
    /// Without that fallback the dot behaved like the start of a fresh word and an all-lowercase
    /// query got <c>ID_KONTRAHENT</c> inserted into it. <see cref="SqlCaseStyle.Unknown"/> keeps
    /// the catalog casing, i.e. the previous behaviour.</para>
    /// </summary>
    public static string Match(
        string? typedPrefix, string candidate, SqlCaseStyle documentStyle = SqlCaseStyle.Unknown)
    {
        if (string.IsNullOrEmpty(candidate)) return candidate;

        bool sawLetter = false;
        bool allLower = true;
        bool allUpper = true;
        foreach (var c in typedPrefix ?? string.Empty)
        {
            if (!char.IsLetter(c)) continue;
            sawLetter = true;
            if (!char.IsLower(c)) allLower = false;
            if (!char.IsUpper(c)) allUpper = false;
            if (!allLower && !allUpper) break;
        }

        // The typed prefix is the strongest signal — it is what the user is typing RIGHT NOW.
        if (sawLetter)
        {
            if (allLower) return candidate.ToLowerInvariant();
            if (allUpper) return candidate.ToUpperInvariant();
            return candidate;   // mixed → verbatim
        }

        // No letters typed (empty prefix after a dot, or digits/underscores only) → the user's
        // established style in this document decides, not the preceding character.
        return documentStyle switch
        {
            SqlCaseStyle.Lower => candidate.ToLowerInvariant(),
            SqlCaseStyle.Upper => candidate.ToUpperInvariant(),
            _ => candidate,
        };
    }
}
