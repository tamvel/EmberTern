using System;
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
