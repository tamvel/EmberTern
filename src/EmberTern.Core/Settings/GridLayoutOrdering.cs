using System;
using System.Collections.Generic;

namespace EmberTern.Core.Settings;

// Pure column-ordering helper for grid-layout persistence. Given the columns a grid
// currently has (by display name) and the user's saved preferred order, produces the
// target left-to-right order: saved columns first (in saved order), then any columns
// the saved order doesn't know about (new since the profile was written) appended in
// their current order. Columns in the saved order that no longer exist are skipped.
//
// No Avalonia dependency — unit-tested directly. The behavior layer turns the returned
// order into DisplayIndex assignments on the live DataGrid.
public static class GridLayoutOrdering
{
    public static IReadOnlyList<string> OrderedNames(
        IReadOnlyList<string> current, IReadOnlyList<string>? savedOrder)
    {
        if (savedOrder is null || savedOrder.Count == 0)
        {
            return current;
        }

        var currentSet = new HashSet<string>(current, StringComparer.Ordinal);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(current.Count);

        // Saved columns first, in the user's order — but only those still present, and
        // never twice (a malformed profile with duplicates collapses to one).
        foreach (var name in savedOrder)
        {
            if (currentSet.Contains(name) && placed.Add(name))
            {
                result.Add(name);
            }
        }

        // New columns (absent from the saved order) keep their current relative order,
        // appended at the end.
        foreach (var name in current)
        {
            if (placed.Add(name))
            {
                result.Add(name);
            }
        }

        return result;
    }
}
