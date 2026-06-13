using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// View-side wrapper around a single <see cref="DependencyInfo"/> for the
/// Pola sub-tab's field-dependency panel. Carries <see cref="ObjectType"/>
/// and <see cref="ObjectName"/> verbatim plus a computed
/// <see cref="CanNavigate"/> flag that drives the future double-click /
/// Open-object affordance.
///
/// V1 surface: data + CanNavigate. <see cref="NavigateCommand"/> is wired
/// (fires the owner's existing <c>RequestOpen(DependencyInfo)</c> chain
/// used by the table-level Zależności tree) but the view does NOT bind any
/// gesture to it yet — Session 5 wires double-click + key handler.
/// </summary>
public partial class FieldDependencyItem : ObservableObject
{
    private readonly TableDetailTabViewModel? _owner;

    public FieldDependencyItem(DependencyInfo info, TableDetailTabViewModel? owner = null)
    {
        Info = info;
        _owner = owner;
    }

    public DependencyInfo Info { get; }

    /// <summary>The dependency's object name (e.g. trigger name, view name).</summary>
    public string ObjectName => Info.ObjectName;

    /// <summary>Object kind label (Table, View, Trigger, …). Comes from the
    /// reader's <c>MapObjectType</c> mapping of RDB$ catalog ints to
    /// human-readable kind strings.</summary>
    public string ObjectType => Info.ObjectType;

    /// <summary>True when the object kind is one we know how to open
    /// independently — same set as the table-level Zależności tree uses.
    /// Field-only or unknown kinds ("Field", "Object (N)") return false;
    /// the UI keeps them visible but ungesture-able.</summary>
    public bool CanNavigate
        => TableDetailTabViewModel.MapObjectTypeToKind(Info.ObjectType) is not null;

    // Icon glyph + theme-resource key, resolved through the SAME mapping the
    // metadata tree uses — no second icon set. Unknown kinds (Field,
    // "Object (N)") fall back to empty glyph + empty key (the IconBrushConverter
    // returns a transparent brush for empty keys, so the cell just shows blank).
    private MetadataObjectKind? Kind
        => TableDetailTabViewModel.MapObjectTypeToKind(Info.ObjectType);

    /// <summary>Unicode glyph for the object kind (▦ ◫ ⚙ ⚡ ƒ …) — same set
    /// as the sidebar metadata tree. Empty for kinds without a mapping.</summary>
    public string Icon => Kind is { } k ? MetadataNodeViewModel.IconFor(k) : string.Empty;

    /// <summary>Theme-aware brush resource key (e.g. "IconColor_Trigger").
    /// Bound through IconBrushConverter + ActualThemeVariant so the glyph
    /// recolors live on theme toggle, exactly like the tree.</summary>
    public string IconResourceKey => Kind is { } k ? MetadataNodeViewModel.ResourceKeyFor(k) : string.Empty;

    /// <summary>✓ when this trigger fires on INSERT for the field; blank for
    /// non-triggers (where the catalog carries no operation semantics) or when
    /// the trigger doesn't fire on INSERT.</summary>
    public string InsertMark => Info.FiresOnInsert == true ? "✓" : string.Empty;

    /// <summary>✓ when this trigger fires on UPDATE for the field; blank otherwise.</summary>
    public string UpdateMark => Info.FiresOnUpdate == true ? "✓" : string.Empty;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Navigate()
    {
        // Routes through the owner's existing dependency-open path —
        // same plumbing as the table-level Zależności tree uses. View
        // does not currently bind this command to any gesture in
        // Session 4; Session 5 will wire DoubleTapped / Enter.
        _owner?.RequestOpen(Info);
    }
}
