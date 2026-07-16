using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmberTern.Core.Metadata;

/// <summary>
/// One shared auto-namer for a table's constraints and indexes. Given a base name
/// (e.g. <c>IDX_ORDERS</c>, <c>UNQ_ORDERS</c>, <c>CHK_ORDERS</c>, <c>FK_ORDERS_CUSTOMER</c>)
/// and the names already in use, it returns the base name when it is free, otherwise the
/// base name with an incrementing integer inserted right after its leading letter run —
/// IBExpert's convention:
/// <code>IDX_ORDERS → IDX1_ORDERS → IDX2_ORDERS → …</code>
/// This is deliberately ONE mechanism for Index / Unique / Check / Foreign Key: the only
/// per-kind difference is the prefix, which lives in the caller-supplied base name, not here.
/// Comparison is case-insensitive because Firebird folds unquoted identifiers to upper case.
/// </summary>
public static class ConstraintNaming
{
    /// <summary>Returns <paramref name="baseName"/> if it is not already in
    /// <paramref name="existingNames"/>; otherwise inserts the lowest free integer after the
    /// leading letter run (<c>IDX_T → IDX1_T</c>). A name that does not start with letters gets
    /// the number appended at the end. Empty base → returned unchanged.</summary>
    public static string MakeUnique(string? baseName, IEnumerable<string>? existingNames)
    {
        var name = (baseName ?? string.Empty).Trim();
        if (name.Length == 0) return name;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingNames is not null)
        {
            foreach (var n in existingNames)
                if (!string.IsNullOrWhiteSpace(n)) used.Add(n.Trim());
        }

        if (!used.Contains(name)) return name;

        // Split "IDX_ORDERS" into head "IDX" + tail "_ORDERS"; the counter goes between them.
        int split = 0;
        while (split < name.Length && char.IsLetter(name[split])) split++;
        string head = split > 0 ? name.Substring(0, split) : name;
        string tail = split > 0 ? name.Substring(split) : string.Empty;

        for (int n = 1; ; n++)
        {
            var candidate = head + n.ToString(CultureInfo.InvariantCulture) + tail;
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
