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

    /// <summary>Drops a leading <c>COLUMN </c> from a TypeOf string
    /// (<c>COLUMN T.C</c> → <c>T.C</c>).</summary>
    public static string StripColumnPrefix(string typeOf)
    {
        var t = (typeOf ?? string.Empty).Trim();
        return t.StartsWith("COLUMN ", System.StringComparison.OrdinalIgnoreCase) ? t[7..].Trim() : t;
    }

    /// <summary><c>COLUMN TABLE.COLUMN</c> (or a bare <c>TABLE.COLUMN</c>) → a
    /// <see cref="ColumnRef"/>; null for a TYPE OF &lt;domain&gt; form (no dot).</summary>
    public static ColumnRef? Parse(string? typeOf)
    {
        if (string.IsNullOrWhiteSpace(typeOf)) return null;
        var t = StripColumnPrefix(typeOf);
        var dot = t.IndexOf('.');
        if (dot <= 0 || dot >= t.Length - 1) return null;
        return new ColumnRef(t[..dot].Trim(), t[(dot + 1)..].Trim());
    }
}
