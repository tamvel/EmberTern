using System.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// A view model that offers a LIVE DDL preview — the statement its current form state would generate,
/// recomputed as the user edits.
///
/// <para>⭐ It exists so the five preview surfaces (New Table + the Check-constraint, PK/Unique, Foreign-key
/// and Index dialogs) can share ONE wiring call instead of five copies of "find the editor, subscribe to
/// PropertyChanged, push the text". The property was already on all five view models under the same name;
/// the interface only writes down the shared concept that was already there.</para>
///
/// <para>⚠ Deliberately NOT retrofitted onto the twelve object-editor / Data-Import previews. They expose the
/// same idea under different names (<c>DdlText</c>, <c>CreateTableSql</c>) and each already carries its own
/// hand-written push; unifying them is a worthwhile follow-up, not a change to smuggle into this one.</para>
/// </summary>
public interface IDdlPreviewSource : INotifyPropertyChanged
{
    /// <summary>The generated DDL for the current form state, or empty when there is nothing to show.</summary>
    string DdlPreview { get; }
}
