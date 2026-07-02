using System;
using System.Collections.Generic;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Lightweight descriptor of one grid column, used by the shared filter panel +
/// aggregation bar to populate column pickers and derive type-aware operator /
/// aggregate menus. Category is computed from the CLR type.
/// </summary>
public sealed record GridColumnRef(int Index, string Name, Type ClrType)
{
    public GridColumnCategory Category => GridColumnClassifier.Classify(ClrType);

    // Shown in the column ComboBox.
    public override string ToString() => Name;

    /// <summary>Build column refs from a result's columns (index = position). Shared
    /// by every data-grid host so the filter/aggregation column set is derived one way.</summary>
    public static IReadOnlyList<GridColumnRef> From(IReadOnlyList<QueryColumn>? columns)
    {
        if (columns is null || columns.Count == 0) return System.Array.Empty<GridColumnRef>();
        var list = new List<GridColumnRef>(columns.Count);
        for (int i = 0; i < columns.Count; i++)
            list.Add(new GridColumnRef(i, columns[i].Name, columns[i].ClrType));
        return list;
    }
}

/// <summary>
/// How a filter condition edits its value operand. Operator-driven so the value
/// editor UI can adapt. Today: <see cref="None"/> (IS NULL / IS NOT NULL) and
/// <see cref="Single"/> (one value). Reserved for future operators WITHOUT a
/// panel rebuild: List (IN / NOT IN), Range (BETWEEN / NOT BETWEEN), DateRange.
/// </summary>
public enum ValueEditorKind
{
    None,
    Single,
    // Future (not implemented): List, Range, DateRange.
}
