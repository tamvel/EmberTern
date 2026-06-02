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
    /// empty / mixed / non-letter-only → candidate verbatim (the original
    /// catalog casing wins).
    /// </summary>
    public static string Match(string? typedPrefix, string candidate)
    {
        if (string.IsNullOrEmpty(typedPrefix) || string.IsNullOrEmpty(candidate))
        {
            return candidate;
        }

        bool sawLetter = false;
        bool allLower = true;
        bool allUpper = true;
        foreach (var c in typedPrefix)
        {
            if (!char.IsLetter(c)) continue;
            sawLetter = true;
            if (!char.IsLower(c)) allLower = false;
            if (!char.IsUpper(c)) allUpper = false;
            if (!allLower && !allUpper) break;
        }

        // No letters in the prefix (digits / underscores only) → no signal,
        // keep the catalog casing.
        if (!sawLetter) return candidate;
        if (allLower) return candidate.ToLowerInvariant();
        if (allUpper) return candidate.ToUpperInvariant();
        return candidate;
    }
}
