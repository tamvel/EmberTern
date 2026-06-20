using System;
using Avalonia.Controls;

namespace EmberTern.App.Controls;

/// <summary>
/// One shared searchable lookup control for large object lists (tables, domains,
/// generators, …). A pre-configured <see cref="AutoCompleteBox"/>: type-to-filter
/// (case-insensitive <i>contains</i>), keyboard navigation, and show-the-whole-list on
/// focus — so it still "expands" like a combo while letting the user narrow a 2000-row
/// list by typing. Every picker uses this single control (no per-site reimplementation).
///
/// Usage: bind <see cref="AutoCompleteBox.ItemsSource"/> + <see cref="AutoCompleteBox.SelectedItem"/>.
/// For object items (e.g. a DomainSpec) also set
/// <see cref="AutoCompleteBox.ValueMemberBinding"/> (the text member used for
/// filtering / display) and an <see cref="ItemsControl.ItemTemplate"/>. String item
/// lists need neither.
/// </summary>
public class SearchablePicker : AutoCompleteBox
{
    public SearchablePicker()
    {
        FilterMode = AutoCompleteFilterMode.Contains; // case-insensitive substring
        MinimumPrefixLength = 0;                       // show/filter even with empty text
        MaxDropDownHeight = 320;

        // Show the full list when the picker gains focus so it behaves like an
        // expandable combo (click → see everything), while typing filters it down.
        GotFocus += (_, _) => { if (string.IsNullOrEmpty(Text)) IsDropDownOpen = true; };
    }

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);
}
