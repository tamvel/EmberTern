using Avalonia.Controls;

namespace EmberTern.App.Localization;

/// <summary>
/// Gives a <b>code-built</b> <see cref="DataGridColumn"/> a header that follows the language, the same way
/// <c>{app:Loc}</c> does for XAML.
///
/// <para>⭐ <b>Why this exists at all.</b> A column created in code takes its header by ASSIGNMENT
/// (<c>Header = UiStrings.X</c>), which captures the text once — so it renders correctly and then stays in
/// whatever language was current when the grid was built. That is precisely the class of consumer a binding
/// cannot reach, and it is invisible: nothing fails, the header is simply stale. Binding
/// <see cref="DataGridColumn.HeaderProperty"/> instead removes the problem rather than scheduling a refresh
/// for it.</para>
///
/// <para>⚠ It takes a <b>key</b>, not a resolved string, and that is the whole point — a resolved string is
/// already the wrong language the moment the user switches. ⛔ Do not add an overload taking
/// <c>UiStrings.Something</c>; that is the shape this replaces.</para>
///
/// <para>⚠ Headers that come from DATA — a result grid's column names, an imported file's fields — are NOT
/// localizable and must keep their plain assignment. Those are the user's own identifiers, and translating
/// them would be a defect (rule #11).</para>
/// </summary>
internal static class LocalizedColumn
{
    /// <summary>Binds the column's header to <paramref name="key"/> and returns the column, so it composes
    /// inside an object initializer chain.</summary>
    public static TColumn Header<TColumn>(TColumn column, string key)
        where TColumn : DataGridColumn
    {
        column.Bind(DataGridColumn.HeaderProperty, new LocExtension(key).ProvideValue());
        return column;
    }
}
