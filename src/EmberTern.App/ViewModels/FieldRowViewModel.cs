using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Wraps a <see cref="FieldInfo"/> for the Pola sub-tab's inline edit grid.
/// Holds editable copies of <see cref="Name"/> / <see cref="NotNull"/> /
/// <see cref="DefaultValue"/> / <see cref="TypeText"/> / <see cref="DomainName"/> /
/// <see cref="Description"/>; forwards every other (read-only) field as a
/// computed property.
///
/// Edits themselves are queued into <see cref="TableDetailTabViewModel.PendingChanges"/>
/// by the view's CellEditEnding handler — this VM stores no PendingDdlChange
/// itself. <see cref="IsModified"/> compares current vs. original so the row
/// can be subtly tinted while changes are queued.
/// </summary>
public partial class FieldRowViewModel : ObservableObject
{
    private readonly TableDetailTabViewModel? _owner;

    public FieldRowViewModel(FieldInfo original) : this(original, null)
    {
    }

    public FieldRowViewModel(FieldInfo original, TableDetailTabViewModel? owner)
    {
        Original = original;
        _owner = owner;
        _name = original.Name;
        _notNull = original.NotNull;
        _defaultValue = original.DefaultValue ?? string.Empty;
        _typeText = original.Type;
        _domainName = original.Domain;
        _description = original.Description ?? string.Empty;

        // Mirror the owner's IsFieldEditMode flag into our own IsCellEditable
        // so always-visible cell ComboBoxes (Type, Domain) gray out when edit
        // mode is off.
        if (_owner is not null)
        {
            _owner.PropertyChanged += OnOwnerPropertyChanged;
            // Domains load asynchronously AFTER the field rows are built, so the
            // Domain ComboBox's SelectedItem can't resolve at construction time.
            // Re-raise SelectedDomainSpec when the list arrives so the combo
            // visually selects the right domain once it's available.
            _owner.AvailableDomains.CollectionChanged += OnAvailableDomainsChanged;
        }
    }

    private void OnOwnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDetailTabViewModel.IsFieldEditMode))
        {
            OnPropertyChanged(nameof(IsCellEditable));
        }
    }

    private void OnAvailableDomainsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(SelectedDomainSpec));

    // Strips the size/precision suffix from a type string: "VARCHAR(50)" →
    // "VARCHAR", "NUMERIC(15,2)" → "NUMERIC", "DOUBLE PRECISION" → unchanged.
    private static string StripSize(string? type)
    {
        if (string.IsNullOrEmpty(type)) return string.Empty;
        var paren = type.IndexOf('(');
        return paren < 0 ? type : type.Substring(0, paren).TrimEnd();
    }

    /// <summary>
    /// True when this row's always-visible cell editors (Type / Domain
    /// ComboBoxes) should accept user interaction. Mirrors the owner's
    /// IsFieldEditMode toggle; defaults to true when no owner is wired
    /// (tests / construction without an owner).
    /// </summary>
    public bool IsCellEditable => _owner is null || _owner.IsFieldEditMode;

    public FieldInfo Original { get; }

    // ─── Editable properties (drive the grid's editing templates) ─────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private string _name;

    // Field identifier always UPPERCASE — Firebird stores all unquoted identifiers
    // in upper case, and the rest of the workbench (autocomplete, DDL) assumes
    // that form. Coerce in the setter so the live grid display reflects what
    // the eventual ALTER COLUMN TO will emit.
    private bool _settingNameUpper;
    partial void OnNameChanged(string value)
    {
        if (_settingNameUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, System.StringComparison.Ordinal))
        {
            _settingNameUpper = true;
            try { Name = upper; } finally { _settingNameUpper = false; }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private bool _notNull;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private string _defaultValue;

    /// <summary>
    /// The full formatted type string (e.g. <c>VARCHAR(80)</c>). Edited via
    /// the Type ComboBox + Size/Scale support cells; the inline-edit handler
    /// regenerates the ALTER TYPE statement from whatever the user picked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(SelectedTypeItem))]
    private string _typeText;

    /// <summary>
    /// Base-type wrapper for the Type ComboBox's SelectedItem binding. The
    /// ComboBox's ItemsSource (<see cref="BasicTypes"/>) only carries base
    /// type names, but <see cref="TypeText"/> holds the FULL type
    /// (<c>VARCHAR(50)</c>). Without this wrapper the ComboBox can't match the
    /// full string in its items, resets SelectedItem to null, and the TwoWay
    /// binding writes that null straight back into TypeText — which then reads
    /// as a change vs. the original and falsely tints the row modified.
    ///
    /// Getter: returns the base type ONLY when it's a known basic type;
    /// otherwise null (combo shows blank, TypeText preserved).
    /// Setter: ignores null/empty (the load-time and not-found writeback) and
    /// no-ops when the base type is unchanged, so TypeText keeps its full
    /// form unless the user genuinely picks a different base type.
    /// </summary>
    public string? SelectedTypeItem
    {
        get
        {
            var baseType = StripSize(TypeText);
            foreach (var t in BasicTypes)
            {
                if (string.Equals(t, baseType, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            if (string.Equals(value, StripSize(TypeText), StringComparison.OrdinalIgnoreCase)) return;
            TypeText = value;
        }
    }

    /// <summary>Domain name or null. Editable via Domain ComboBox.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(SelectedDomainSpec))]
    private string? _domainName;

    /// <summary>
    /// DomainSpec wrapper for the Domain ComboBox's SelectedItem binding.
    /// Avalonia 12 ComboBox has no SelectedValueBinding (WPF-only), so we expose
    /// a get/set property that maps the DomainSpec round-trip onto DomainName.
    /// </summary>
    public DomainSpec? SelectedDomainSpec
    {
        get
        {
            if (string.IsNullOrEmpty(DomainName)) return null;
            foreach (var d in AvailableDomains)
            {
                if (string.Equals(d.Name, DomainName, System.StringComparison.Ordinal))
                    return d;
            }
            return null;
        }
        set
        {
            // Ignore null writeback. The ComboBox sets SelectedItem to null
            // whenever the getter can't resolve DomainName against
            // AvailableDomains — which happens on load (domains arrive
            // asynchronously after the rows are built) and for anonymous
            // RDB$ backing-domains that never appear in the list. Honoring
            // that null would clear DomainName and falsely mark the row
            // modified. There is no "clear domain" entry in the list, so the
            // user never legitimately picks null here.
            if (value is null) return;
            DomainName = value.Name;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private string _description;

    /// <summary>
    /// True when at least one editable property differs from its original.
    /// The Pola DataGrid binds a row-level class to this so modified rows get
    /// a subtle WarningBrush tint until Compile drains the pending queue.
    /// </summary>
    public bool IsModified =>
        !string.Equals(Name, Original.Name, System.StringComparison.Ordinal)
        || NotNull != Original.NotNull
        || !string.Equals(DefaultValue ?? string.Empty, Original.DefaultValue ?? string.Empty, System.StringComparison.Ordinal)
        || !string.Equals(TypeText, Original.Type, System.StringComparison.Ordinal)
        || !string.Equals(DomainName ?? string.Empty, Original.Domain ?? string.Empty, System.StringComparison.Ordinal)
        || !string.Equals(Description ?? string.Empty, Original.Description ?? string.Empty, System.StringComparison.Ordinal);

    // ─── Read-only forwards (kept so XAML can keep its old Binding paths) ──

    public int DisplayPosition => Original.DisplayPosition;
    public int Position => Original.Position;
    public string? ComputedSource => Original.ComputedSource;
    public bool IsPrimaryKey => Original.IsPrimaryKey;
    public bool IsForeignKey => Original.IsForeignKey;
    public bool IsUnique => Original.IsUnique;
    public string? Domain => Original.Domain;
    public string? Charset => Original.Charset;
    public string? ForeignKeyTable => Original.ForeignKeyTable;
    public bool IsAutoIncrement => Original.IsAutoIncrement;
    public int? Size => Original.Size;
    public int? Scale => Original.Scale;

    // ─── Owner-surfaced collections for the in-cell editors ────────────────

    /// <summary>Basic SQL type list — null-safe; defaults to a static fallback when
    /// no owner is wired (tests / standalone construction).</summary>
    public IReadOnlyList<string> BasicTypes => _owner?.BasicTypes ?? FallbackBasicTypes;

    /// <summary>Domains available on the active connection — populated by the
    /// owner during LoadAsync.</summary>
    public ObservableCollection<DomainSpec> AvailableDomains
        => _owner?.AvailableDomains ?? FallbackDomains;

    /// <summary>True when this row's name and type can be edited — false when
    /// other DB objects depend on this column (rename/type-change would break
    /// triggers / views / check constraints).</summary>
    public bool CanEditStructure
        => _owner is null || _owner.CanRenameField(Original.Name);

    private static readonly IReadOnlyList<string> FallbackBasicTypes = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB",
    };
    private static readonly ObservableCollection<DomainSpec> FallbackDomains = new();
}
