using System;
using System.Collections;
using System.Collections.Generic;

namespace EmberTern.Core.Query;

/// <summary>
/// Compares two <c>object?[]</c> rows by a fixed column index. Shared by every
/// dynamic result grid: the SQL editor's Results grid (client-side paging/sort
/// over the materialized result) and the Table Data View (Avalonia
/// <c>DataGridColumn.CustomSortComparer</c>, which takes a non-generic
/// <see cref="IComparer"/>). Nulls sort first; values of the same comparable
/// type use <see cref="IComparable"/>, otherwise a culture-aware string compare.
/// </summary>
public sealed class RowIndexComparer : IComparer, IComparer<object?[]>
{
    private readonly int _index;

    public RowIndexComparer(int index) => _index = index;

    public int Compare(object?[]? x, object?[]? y)
    {
        var xv = x is not null && _index < x.Length ? x[_index] : null;
        var yv = y is not null && _index < y.Length ? y[_index] : null;
        if (xv is null && yv is null) return 0;
        if (xv is null) return -1;
        if (yv is null) return 1;
        if (xv is IComparable xcmp && xv.GetType() == yv.GetType()) return xcmp.CompareTo(yv);
        return string.Compare(xv.ToString(), yv.ToString(), StringComparison.CurrentCulture);
    }

    int IComparer.Compare(object? x, object? y) => Compare(x as object?[], y as object?[]);
}
