using System.Collections;
using Avalonia;
using Avalonia.Controls.Templates;

namespace EmberTern.App.Controls;

/// <summary>
/// One tab/source inside a <see cref="SearchableComboBox"/> (e.g. "Domain",
/// "Table column"). When a control has zero sections it renders a single list
/// from its own <see cref="SearchableComboBox.ItemsSource"/>; with one or more
/// sections it renders a tab per section. Each section carries its own item
/// list, row template, header, and the property path used for filter/display.
/// </summary>
public sealed class SearchableComboBoxSection : AvaloniaObject
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<SearchableComboBoxSection, string>(nameof(Header), string.Empty);

    /// <summary>Tab caption (e.g. "Domain", "Table column").</summary>
    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableComboBoxSection, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SearchableComboBoxSection, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>Rich multi-column row template for this section's dropdown list.</summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<SearchableComboBoxSection, IDataTemplate?>(nameof(HeaderTemplate));

    /// <summary>Optional column-header row shown above the list (aligns with the
    /// item rows via <c>Grid.SharedSizeGroup</c>; built per popup).</summary>
    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public static readonly StyledProperty<Avalonia.Controls.Control?> ContentProperty =
        AvaloniaProperty.Register<SearchableComboBoxSection, Avalonia.Controls.Control?>(nameof(Content));

    /// <summary>Custom tab content (e.g. a two-pane table→column picker). When set, the
    /// host renders this instead of the built-in filter+list, hides the shared filter
    /// box for this tab, and wires the content's commit (see
    /// <see cref="ISearchableComboBoxContent"/>). <see cref="ItemsSource"/> is ignored.</summary>
    public Avalonia.Controls.Control? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<SearchableComboBoxSection, string?>(nameof(DisplayMemberPath));

    /// <summary>Property name used for the case-insensitive Contains filter and the
    /// closed-box display (e.g. "Name"). Null → the item's <c>ToString()</c>.</summary>
    public string? DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }
}
