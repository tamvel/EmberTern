namespace EmberTern.App.ViewModels;

/// <summary>
/// A "TYPE OF COLUMN" source picked from the Table-column tab of the merged
/// "Domena/Kolumna" picker. <see cref="TypeOfClause"/> is what a field's TypeOf
/// holds (the generator prepends "TYPE OF"); <see cref="Qualified"/> is the display.
/// </summary>
public sealed record ColumnRef(string Table, string Column, string? Type = null)
{
    /// <summary><c>TABLE.COLUMN</c> — closed-box display.</summary>
    public string Qualified => $"{Table}.{Column}";

    /// <summary><c>COLUMN TABLE.COLUMN</c> — stored in a field's TypeOf; ComposeType
    /// emits <c>TYPE OF COLUMN TABLE.COLUMN</c>.</summary>
    public string TypeOfClause => $"COLUMN {Table}.{Column}";
}
