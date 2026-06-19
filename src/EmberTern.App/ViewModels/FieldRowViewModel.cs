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
        // Size/Scale are parsed from the USER-FACING type string ("VARCHAR(80)",
        // "NUMERIC(15,2)") — NOT from FieldInfo.Size, which is the raw byte length
        // (RDB$FIELD_LENGTH = 8 for a NUMERIC(15,2)) and would generate wrong DDL.
        var (arg1, arg2) = ParseTypeArgs(original.Type);
        _size = arg1;
        _scale = arg2;

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

    // Unhooks the owner-event subscriptions. MUST be called before a row VM is
    // discarded (TableDetailTabViewModel rebuilds EditableFields on every Fields
    // mutation) — otherwise each refresh leaves a dead row VM still wired to the
    // owner's PropertyChanged + AvailableDomains.CollectionChanged, accumulating
    // across reloads (the event-subscription leak behind the refresh storm).
    private bool _detached;
    public void Detach()
    {
        if (_detached || _owner is null) return;
        _detached = true;
        _owner.PropertyChanged -= OnOwnerPropertyChanged;
        _owner.AvailableDomains.CollectionChanged -= OnAvailableDomainsChanged;
    }

    private void OnOwnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDetailTabViewModel.IsFieldEditMode))
        {
            OnPropertyChanged(nameof(IsCellEditable));
            OnPropertyChanged(nameof(IsTypeCellEditable));
        }
    }

    private void OnAvailableDomainsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(SelectedDomainSpec));

    // Editable cell properties — any change re-queues this row's inline edit on the
    // owner. Crucially this covers the Type / Domain ComboBoxes (always-visible cells
    // in IsReadOnly template columns), which never fire the DataGrid's RowEditEnding,
    // so without this hook a type/domain change wouldn't enqueue a pending change and
    // Compile would stay disabled.
    private static readonly HashSet<string> InlineEditableProps = new(StringComparer.Ordinal)
    {
        nameof(Name), nameof(NotNull), nameof(DefaultValue), nameof(TypeText),
        nameof(DomainName), nameof(Size), nameof(Scale), nameof(Description),
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_owner is not null && e.PropertyName is { } p && InlineEditableProps.Contains(p))
            _owner.OnInlineFieldEdited(this);
    }

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
    [NotifyPropertyChangedFor(nameof(EffectiveTypeText))]
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
    [NotifyPropertyChangedFor(nameof(HasDomain))]
    [NotifyPropertyChangedFor(nameof(IsTypeCellEditable))]
    private string? _domainName;

    /// <summary>True when this column is domain-governed — the Type combo is then
    /// disabled (the domain governs the type, #3/#4).</summary>
    public bool HasDomain => !string.IsNullOrEmpty(DomainName);

    /// <summary>Type combo enabled only in edit mode AND when not domain-governed.</summary>
    public bool IsTypeCellEditable => IsCellEditable && !HasDomain;

    /// <summary>
    /// DomainSpec wrapper for the Domain ComboBox's SelectedItem binding.
    /// Avalonia 12 ComboBox has no SelectedValueBinding (WPF-only), so we expose
    /// a get/set property that maps the DomainSpec round-trip onto DomainName.
    /// </summary>
    public DomainSpec? SelectedDomainSpec
    {
        get
        {
            if (string.IsNullOrEmpty(DomainName))
            {
                // Show the "(none)" sentinel as selected when the column has no
                // domain, so the combo isn't blank and the user can see/keep the
                // "no domain" state.
                foreach (var d in AvailableDomains)
                {
                    if (string.Equals(d.Name, UiStrings.DomainNoneOption, System.StringComparison.Ordinal))
                        return d;
                }
                return null;
            }
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
            // modified.
            if (value is null) return;
            // The "(none)" sentinel is the explicit "clear domain" choice (#5):
            // map it to a null DomainName so the column falls back to a basic type.
            DomainName = string.Equals(value.Name, UiStrings.DomainNoneOption, System.StringComparison.Ordinal)
                ? null
                : value.Name;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private string _description;

    /// <summary>
    /// Working-model state of this row in the buffered Table Designer:
    /// <see cref="PendingChangeKind.Added"/> for a not-yet-compiled new column,
    /// <see cref="PendingChangeKind.Dropped"/> for a column marked for deletion
    /// (kept visible, struck through), <see cref="PendingChangeKind.Modified"/>
    /// once an inline/dialog edit has been queued, <see cref="PendingChangeKind.None"/>
    /// for a clean live-catalog row. Drives the row tint + the Move/Drop gates
    /// (a dropped row can't be re-dropped or moved).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingAdded))]
    [NotifyPropertyChangedFor(nameof(IsPendingDropped))]
    [NotifyPropertyChangedFor(nameof(HasPendingChange))]
    private PendingChangeKind _pendingKind;

    public bool IsPendingAdded => PendingKind == PendingChangeKind.Added;
    public bool IsPendingDropped => PendingKind == PendingChangeKind.Dropped;

    /// <summary>True when the row carries any working-model change (Added /
    /// Dropped, or Modified / inline-edited). Drives the Pola row tint.</summary>
    public bool HasPendingChange => PendingKind != PendingChangeKind.None || IsModified;

    /// <summary>
    /// True when at least one editable property differs from its original.
    /// The Pola DataGrid binds a row-level class to this so modified rows get
    /// a subtle WarningBrush tint until Compile drains the pending queue.
    /// </summary>
    public bool IsModified =>
        !string.Equals(Name, Original.Name, System.StringComparison.Ordinal)
        || NotNull != Original.NotNull
        || !string.Equals(DefaultValue ?? string.Empty, Original.DefaultValue ?? string.Empty, System.StringComparison.Ordinal)
        || !string.Equals(EffectiveTypeText, Original.Type, System.StringComparison.OrdinalIgnoreCase)
        || !string.Equals(DomainName ?? string.Empty, Original.Domain ?? string.Empty, System.StringComparison.Ordinal)
        || !string.Equals(Description ?? string.Empty, Original.Description ?? string.Empty, System.StringComparison.Ordinal);

    // ─── Editable Size / Scale (length / precision / scale) ───────────────
    //
    // Surfaces the user-facing arguments of the column's type so the Pola grid
    // can edit them inline (parity with the Edit-Field dialog, which the user
    // wants matched). For CHAR/VARCHAR/CSTRING, Size is the length; for
    // NUMERIC/DECIMAL, Size is the precision and Scale the scale. Other types
    // ignore both. EffectiveTypeText reassembles the full Firebird type through
    // the SAME DdlGenerator.FormatTypeOrDomain pipeline the dialog uses — no
    // duplicated formatting logic.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(EffectiveTypeText))]
    private int? _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(EffectiveTypeText))]
    private int? _scale;

    private string BaseType => StripSize(TypeText);
    private bool IsCharType
    {
        get
        {
            var b = BaseType.ToUpperInvariant();
            return b is "CHAR" or "VARCHAR" or "CSTRING";
        }
    }
    private bool IsNumericType
    {
        get
        {
            var b = BaseType.ToUpperInvariant();
            return b is "NUMERIC" or "DECIMAL";
        }
    }

    /// <summary>
    /// The full Firebird type string assembled from the current base type +
    /// Size/Scale, via the shared <see cref="DdlGenerator.FormatTypeOrDomain"/>.
    /// Used by <see cref="IsModified"/> and by the inline-edit pipeline to build
    /// the ALTER COLUMN TYPE clause — identical formatting to the dialog.
    /// </summary>
    public string EffectiveTypeText => DdlGenerator.FormatTypeOrDomain(BuildTypeDefinition());

    /// <summary>
    /// Builds a type-only <see cref="FieldDefinition"/> (no domain) from the
    /// row's current base type + Size/Scale, mapping Size→length for character
    /// types and Size→precision for numeric types. Domain handling stays in the
    /// inline-edit handler.
    /// </summary>
    public FieldDefinition BuildTypeDefinition() => new()
    {
        BasicType = BaseType,
        Size = IsCharType ? Size : null,
        Precision = IsNumericType ? Size : null,
        Scale = IsNumericType ? Scale : null,
    };

    /// <summary>
    /// Restores TypeText / Domain / Size / Scale to the original column shape.
    /// Used by the inline-edit handler when a type/size change is rejected
    /// because the field has dependencies (rename/type-change blocked).
    /// </summary>
    public void RevertTypeToOriginal()
    {
        TypeText = Original.Type;
        DomainName = Original.Domain;
        var (a, b) = ParseTypeArgs(Original.Type);
        Size = a;
        Scale = b;
    }

    // "VARCHAR(80)" → (80, null); "NUMERIC(15,2)" → (15, 2);
    // "INTEGER" / "DOUBLE PRECISION" → (null, null). Tolerant of spaces.
    private static (int?, int?) ParseTypeArgs(string? type)
    {
        if (string.IsNullOrEmpty(type)) return (null, null);
        var open = type.IndexOf('(');
        if (open < 0) return (null, null);
        var close = type.IndexOf(')', open + 1);
        if (close < 0) return (null, null);
        var inner = type.Substring(open + 1, close - open - 1);
        var parts = inner.Split(',');
        int? a = null, b = null;
        if (parts.Length > 0 && int.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pa)) a = pa;
        if (parts.Length > 1 && int.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pb)) b = pb;
        return (a, b);
    }

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
