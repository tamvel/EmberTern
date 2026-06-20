using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row in the New Table tab's fields grid. Mirrors most of the AddField
/// dialog form state but lives inside an editable DataGrid — every property
/// is observable so the live DDL preview re-renders on each keystroke.
/// </summary>
public partial class NewTableFieldRowViewModel : ObservableObject
{
    public NewTableFieldRowViewModel(NewTableTabViewModel? owner = null)
    {
        _owner = owner;
    }

    private readonly NewTableTabViewModel? _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotNullEnabled))]
    private bool _primaryKey;

    partial void OnPrimaryKeyChanged(bool value)
    {
        // PK implies NOT NULL (#4) — force it on; IsNotNullEnabled disables the
        // cell so it can't be toggled back off while PK is set.
        if (value) NotNull = true;
    }

    [ObservableProperty] private string _name = string.Empty;

    // Identifier names live in catalog UPPERCASE. Auto-coerce so the live
    // DDL preview and the eventual CREATE TABLE statement both pick up the
    // user's intent regardless of how they typed it. Re-entrancy guard avoids
    // an OnNameChanged → Name = upper → OnNameChanged loop.
    private bool _settingNameUpper;
    partial void OnNameChanged(string value)
    {
        if (_settingNameUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, StringComparison.Ordinal))
        {
            _settingNameUpper = true;
            try { Name = upper; } finally { _settingNameUpper = false; }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPrecisionScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedDomainSpec))]
    [NotifyPropertyChangedFor(nameof(SelectedTypeItem))]
    [NotifyPropertyChangedFor(nameof(EffectiveTypeDisplay))]
    private string _type = "INTEGER";

    /// <summary>Null-safe Type wrapper for the filtering picker — a partial-typed
    /// filter (no exact match yet) writes null to SelectedItem; ignore it so the
    /// type isn't cleared mid-filter. <see cref="Type"/> is already a base-type name.</summary>
    public string? SelectedTypeItem
    {
        get => Type;
        set { if (!string.IsNullOrEmpty(value)) Type = value; }
    }

    // Bug fix: changing to a type without Size/Scale (e.g. VARCHAR → SMALLINT) clears
    // the now-irrelevant cells so a stale value can't linger in the grid.
    partial void OnTypeChanged(string value)
    {
        if (!FieldTypeRules.UsesSize(value) && Size is not null) Size = null;
        if (!FieldTypeRules.UsesScale(value) && Scale is not null) Scale = null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDomainSpec))]
    [NotifyPropertyChangedFor(nameof(HasDomain))]
    [NotifyPropertyChangedFor(nameof(DomainType))]
    [NotifyPropertyChangedFor(nameof(IsTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPrecisionScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCharsetEnabled))]
    [NotifyPropertyChangedFor(nameof(IsComputedEnabled))]
    [NotifyPropertyChangedFor(nameof(EffectiveTypeDisplay))]
    private string? _domainName;

    [ObservableProperty] private string _defaultValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComputed))]
    [NotifyPropertyChangedFor(nameof(IsTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDomainEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPrecisionScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsNotNullEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDefaultEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCheckEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCharsetEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPkEnabled))]
    [NotifyPropertyChangedFor(nameof(IsAiEnabled))]
    private string _computedExpression = string.Empty;

    partial void OnComputedExpressionChanged(string value)
    {
        // A computed column derives everything from its expression — Firebird
        // rejects Domain / Size / Scale / Default / NOT NULL / CHECK / PK /
        // Autoincrement on it. We BOTH disable those cells (via the Is*Enabled
        // flags below, bound to per-cell template editors) AND clear any values
        // already entered, so the row can't carry contradictory state (#1/#4).
        // Re-entrancy is safe — none of these write back to ComputedExpression.
        if (string.IsNullOrWhiteSpace(value)) return;
        DomainName = null;
        Size = null;
        Scale = null;
        DefaultValue = string.Empty;
        CheckExpression = string.Empty;
        Charset = null;
        NotNull = false;
        PrimaryKey = false;
        AutoIncrement = false;
    }

    [ObservableProperty] private string _checkExpression = string.Empty;
    [ObservableProperty] private int? _size;
    [ObservableProperty] private int? _scale;
    [ObservableProperty] private bool _notNull;
    [ObservableProperty] private string? _charset;
    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultEnabled))]
    private bool _autoIncrement;

    partial void OnAutoIncrementChanged(bool value)
    {
        // Autoincrement supplies the value — a manual DEFAULT is redundant /
        // invalid alongside it (#4). Clear it when AI is turned on.
        if (value) DefaultValue = string.Empty;
    }

    // ─── Per-cell enable gates (#1/#4 — full dependency model) ────────────
    // Every editable New-Table cell is a template column whose editor binds
    // IsEnabled to one of these. Computed By wins over everything; Domain
    // governs the type-related cells; PK forces Not Null; Autoincrement owns
    // the value (no Default).
    public bool IsSizeEnabled => !HasComputed && !HasDomain && Type is "CHAR" or "VARCHAR" or "NUMERIC" or "DECIMAL";
    public bool IsPrecisionScaleEnabled => !HasComputed && !HasDomain && Type is "NUMERIC" or "DECIMAL";
    public bool IsDefaultEnabled => !HasComputed && !AutoIncrement;
    public bool IsCheckEnabled => !HasComputed;
    public bool IsCharsetEnabled => !HasComputed && !HasDomain;
    public bool IsPkEnabled => !HasComputed;
    public bool IsAiEnabled => !HasComputed;

    /// <summary>True when a COMPUTED BY expression is set — the type/domain are
    /// then derived from the expression and ignored by Firebird, so the Type +
    /// Domain cell editors are disabled to avoid a contradictory definition (#4).</summary>
    public bool HasComputed => !string.IsNullOrWhiteSpace(ComputedExpression);

    /// <summary>True when a domain is selected — the domain governs the type, so
    /// the Type cell shows the domain's type (read-only) instead of the combo (#3).</summary>
    public bool HasDomain => !string.IsNullOrEmpty(DomainName);

    /// <summary>Type combo enabled only when neither computed nor domain-governed.</summary>
    public bool IsTypeEnabled => !HasComputed && !HasDomain;

    /// <summary>Domain combo enabled unless the field is computed (mutually exclusive).</summary>
    public bool IsDomainEnabled => !HasComputed;

    /// <summary>Computed cell enabled unless a domain is selected (mutually exclusive).</summary>
    public bool IsComputedEnabled => !HasDomain;

    /// <summary>Not Null cell enabled unless computed or PK forces it on.</summary>
    public bool IsNotNullEnabled => !HasComputed && !PrimaryKey;

    /// <summary>The selected domain's resolved SQL type (e.g. VARCHAR(80)), or
    /// empty when no domain is selected.</summary>
    public string? DomainType
    {
        get
        {
            if (string.IsNullOrEmpty(DomainName)) return null;
            foreach (var d in AvailableDomains)
            {
                if (string.Equals(d.Name, DomainName, StringComparison.Ordinal)) return d.Type;
            }
            return null;
        }
    }

    /// <summary>What the Type cell shows: the domain's type when a domain is
    /// picked (#3), otherwise the chosen basic type.</summary>
    public string EffectiveTypeDisplay => HasDomain ? (DomainType ?? string.Empty) : Type;

    public IReadOnlyList<string> BasicTypes => _owner?.BasicTypes ?? FallbackBasicTypes;
    public ObservableCollection<DomainSpec> AvailableDomains
        => _owner?.AvailableDomains ?? FallbackDomains;

    /// <summary>Wrapper so the Domain <c>SearchableComboBox</c> can bind SelectedItem to
    /// a DomainSpec while the underlying DomainName stays a plain string. Empty → null
    /// (empty field, no "(none)" sentinel); the picker commits only on explicit pick/clear,
    /// so null = the user cleared (✕).</summary>
    public DomainSpec? SelectedDomainSpec
    {
        get
        {
            if (string.IsNullOrEmpty(DomainName)) return null;
            foreach (var d in AvailableDomains)
            {
                if (string.Equals(d.Name, DomainName, StringComparison.Ordinal)) return d;
            }
            return null;
        }
        set => DomainName = value?.Name;
    }

    public FieldDefinition ToFieldDefinition()
    {
        return new FieldDefinition
        {
            Name = Name ?? string.Empty,
            NotNull = NotNull,
            PrimaryKey = PrimaryKey,
            Domain = string.IsNullOrWhiteSpace(DomainName) ? null : DomainName,
            BasicType = Type,
            Size = IsSizeEnabled && Type is "CHAR" or "VARCHAR" ? Size : null,
            Precision = IsPrecisionScaleEnabled ? Size : null,
            Scale = IsPrecisionScaleEnabled ? Scale : null,
            DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue,
            CheckExpression = string.IsNullOrWhiteSpace(CheckExpression) ? null : CheckExpression,
            ComputedExpression = string.IsNullOrWhiteSpace(ComputedExpression) ? null : ComputedExpression,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            AutoIncrement = AutoIncrement ? AutoIncrementMode.NewGenerator : AutoIncrementMode.None,
        };
    }

    private static readonly IReadOnlyList<string> FallbackBasicTypes = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB",
    };
    private static readonly ObservableCollection<DomainSpec> FallbackDomains = new();
}

/// <summary>
/// Workspace-tab variant of the CreateTableDialog. Lives next to SQL Editor /
/// DDL / TableDetail tabs in the main editor area; the user can build a new
/// table progressively, switch to other tabs, and come back. Compile fires
/// the DDL through <c>FirebirdDdlExecutor</c> via the owner.
/// </summary>
public partial class NewTableTabViewModel : ViewModelBase, IUnsavedWorkSource
{
    public NewTableTabViewModel() : this(null)
    {
    }

    public NewTableTabViewModel(MainWindowViewModel? owner)
    {
        _owner = owner;
        AvailableDomains = new ObservableCollection<DomainSpec>();
        Fields = new ObservableCollection<NewTableFieldRowViewModel>();
        Fields.CollectionChanged += OnFieldsCollectionChanged;
        // Persistent default — the most common case.
        SelectedKind = TableKinds[0];
        // Seed a default first row so the DDL preview reads sensibly from the start.
        Fields.Add(new NewTableFieldRowViewModel(this)
        {
            Name = "ID",
            Type = "INTEGER",
            NotNull = true,
            PrimaryKey = true,
        });
    }

    private readonly MainWindowViewModel? _owner;

    public ObservableCollection<NewTableFieldRowViewModel> Fields { get; }
    public ObservableCollection<DomainSpec> AvailableDomains { get; }

    public IReadOnlyList<string> BasicTypes { get; } = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB",
    };

    public IReadOnlyList<NamedTableKind> TableKinds { get; } = new[]
    {
        new NamedTableKind(TableKind.Persistent,         UiStrings.NewTableKindPersistent),
        new NamedTableKind(TableKind.TempDeleteRows,     UiStrings.NewTableKindTempDelete),
        new NamedTableKind(TableKind.TempPreserveRows,   UiStrings.NewTableKindTempPreserve),
    };

    public sealed record NamedTableKind(TableKind Kind, string Label);

    public string DisplayTitle => string.IsNullOrWhiteSpace(TableName)
        ? UiStrings.NewTableTabDefaultTitle
        : TableName.Trim();

    /// <summary>
    /// True when the user has done meaningful work on the form — used to gate
    /// the close-confirmation. A freshly-opened tab has an empty name and the
    /// single seeded ID field; anything beyond that counts as content worth
    /// confirming before discard.
    /// </summary>
    public bool HasContent
        => !string.IsNullOrWhiteSpace(TableName) || Fields.Count != 1;

    // Unsaved-work for the WorkGuard: a new table the user has started filling in
    // and not yet created in the database.
    public UnsavedWorkItem? GetUnsavedWork()
        => HasContent
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.UnsavedNewTableFormat, DisplayTitle))
            : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _tableName = string.Empty;

    // Table identifier always UPPERCASE — see NewTableFieldRowViewModel.OnNameChanged
    // for the same coercion shape on field names.
    private bool _settingTableNameUpper;
    partial void OnTableNameChanged(string value)
    {
        if (_settingTableNameUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, StringComparison.Ordinal))
        {
            _settingTableNameUpper = true;
            try { TableName = upper; } finally { _settingTableNameUpper = false; }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private NamedTableKind? _selectedKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteFieldCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFieldUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFieldDownCommand))]
    private NewTableFieldRowViewModel? _selectedField;

    // NotifyPropertyChangedFor(HasValidationMessage) is MANDATORY here: IsValid()
    // and the compile-error catch set ValidationMessage directly, so without this
    // the message text changes but HasValidationMessage (→ the row's IsVisible)
    // never re-evaluates → "click Compile, nothing happens" (#2).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>
    /// Owner injects the live domain list after the active-connection metadata
    /// load — called from the New Table command handler. The collection is
    /// shared with each row via FallbackDomains-vs-AvailableDomains lookup.
    /// </summary>
    public void SetAvailableDomains(IEnumerable<DomainSpec> domains)
    {
        AvailableDomains.Clear();
        // No "(none)" sentinel — the SearchableComboBox clears via its ✕ button.
        foreach (var d in domains) AvailableDomains.Add(d);
    }

    public string DdlPreview
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(TableName) ? "<table>" : TableName.Trim();
            return DdlGenerator.BuildCreateTable(name, BuildSpec());
        }
    }

    public TableSpec BuildSpec()
    {
        var spec = new TableSpec
        {
            Kind = SelectedKind?.Kind ?? TableKind.Persistent,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
        };
        foreach (var row in Fields) spec.Fields.Add(row.ToFieldDefinition());
        return spec;
    }

    [RelayCommand]
    private void AddField()
    {
        var row = new NewTableFieldRowViewModel(this);
        Fields.Add(row);
        SelectedField = row;
    }

    public bool CanDeleteField => SelectedField is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteField))]
    private void DeleteField()
    {
        if (SelectedField is null) return;
        var idx = Fields.IndexOf(SelectedField);
        if (idx < 0) return;
        Fields.RemoveAt(idx);
        SelectedField = Fields.Count > 0 ? Fields[Math.Min(idx, Fields.Count - 1)] : null;
    }

    public bool CanMoveFieldUp => SelectedField is not null && Fields.IndexOf(SelectedField) > 0;
    public bool CanMoveFieldDown => SelectedField is not null
        && Fields.IndexOf(SelectedField) >= 0
        && Fields.IndexOf(SelectedField) < Fields.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveFieldUp))]
    private void MoveFieldUp() => MoveBy(-1);

    [RelayCommand(CanExecute = nameof(CanMoveFieldDown))]
    private void MoveFieldDown() => MoveBy(+1);

    private void MoveBy(int delta)
    {
        if (SelectedField is not { } row) return;
        var idx = Fields.IndexOf(row);
        var t = idx + delta;
        if (idx < 0 || t < 0 || t >= Fields.Count) return;
        // RemoveAt + Insert instead of ObservableCollection.Move: Avalonia's
        // DataGrid doesn't reliably re-render a NotifyCollectionChangedAction.Move
        // (the row order in the VM changes but the grid keeps the old visual
        // order). Remove + Add are handled correctly, so the moved row shows in
        // its new position immediately.
        Fields.RemoveAt(idx);
        Fields.Insert(t, row);
        SelectedField = row;
    }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(TableName))
        {
            ValidationMessage = UiStrings.NewTableValidationNameRequired;
            return false;
        }
        var hasName = false;
        foreach (var f in Fields)
        {
            if (!string.IsNullOrWhiteSpace(f.Name)) { hasName = true; break; }
        }
        if (!hasName)
        {
            ValidationMessage = UiStrings.NewTableValidationAtLeastOneField;
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }

    /// <summary>Fires when the user presses ⚡ Compile in the toolbar. The owner
    /// (MainWindowViewModel) handles execution + tab close + metadata refresh.</summary>
    public event Func<NewTableTabViewModel, Task>? CompileRequested;

    [RelayCommand]
    private async Task CompileAsync()
    {
        if (!IsValid()) return;
        if (CompileRequested is null) return;
        await CompileRequested(this).ConfigureAwait(true);
    }

    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (NewTableFieldRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnFieldRowPropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (NewTableFieldRowViewModel row in e.NewItems)
                row.PropertyChanged += OnFieldRowPropertyChanged;
        }
        OnPropertyChanged(nameof(DdlPreview));
        MoveFieldUpCommand.NotifyCanExecuteChanged();
        MoveFieldDownCommand.NotifyCanExecuteChanged();
        DeleteFieldCommand.NotifyCanExecuteChanged();
    }

    private void OnFieldRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DdlPreview));
    }
}
