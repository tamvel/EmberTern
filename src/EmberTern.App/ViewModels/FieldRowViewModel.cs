using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        }
    }

    private void OnOwnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDetailTabViewModel.IsFieldEditMode))
        {
            OnPropertyChanged(nameof(IsCellEditable));
        }
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
    private string _typeText;

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
            DomainName = value?.Name;
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
