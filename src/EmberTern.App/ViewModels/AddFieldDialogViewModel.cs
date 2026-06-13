using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Drives the AddFieldDialog. Exposes the form state via observable properties
/// and re-evaluates <see cref="DdlPreview"/> every time anything changes so the
/// "DDL" sub-tab paints a live preview. <see cref="BuildDefinition"/> is the
/// pure conversion step that <see cref="MainWindowViewModel"/> -> TableDetail VM
/// uses to drop a <see cref="PendingDdlChange"/> onto the queue.
/// </summary>
public partial class AddFieldDialogViewModel : ViewModelBase
{
    public AddFieldDialogViewModel(string tableName,
                                   IReadOnlyList<DomainSpec> domains,
                                   IReadOnlyList<string> generators)
        : this(tableName, domains, generators, originalField: null, canRename: true)
    {
    }

    /// <summary>
    /// Edit-mode ctor — seeds the form from <paramref name="originalField"/>'s
    /// current state, sets <see cref="IsEditMode"/>, swaps the dialog title,
    /// and gates the FieldName input on <paramref name="canRename"/> (false
    /// when the field has incoming dependencies — Firebird rejects ALTER … TO
    /// in that case). Tabs that emit DDL outside the safe ALTER set (Check,
    /// Computed, Autoincrement) are disabled in edit mode to keep this dialog
    /// reusable for both Add and Edit; non-ALTERable changes simply produce no
    /// statements in <see cref="DdlGenerator.BuildAlterStatements"/>.
    /// </summary>
    public AddFieldDialogViewModel(string tableName,
                                   IReadOnlyList<DomainSpec> domains,
                                   IReadOnlyList<string> generators,
                                   FieldInfo? originalField,
                                   bool canRename)
    {
        TableName = tableName;
        Domains = new ObservableCollection<DomainSpec>(domains);
        Generators = new ObservableCollection<string>(generators);
        OriginalField = originalField;
        IsEditMode = originalField is not null;
        CanRename = !IsEditMode || canRename;
        if (originalField is not null) SeedFromField(originalField);
    }

    public string TableName { get; }
    public ObservableCollection<DomainSpec> Domains { get; }
    public ObservableCollection<string> Generators { get; }

    /// <summary>Existing field when this dialog opens in edit mode; null
    /// otherwise. The caller passes this through to
    /// <see cref="TableDetailTabViewModel.ExecuteEditFieldAsync"/> alongside
    /// the dialog's <see cref="BuildDefinition"/> result so the diff has
    /// both sides.</summary>
    public FieldInfo? OriginalField { get; }
    public bool IsEditMode { get; }
    public bool IsAddMode => !IsEditMode;

    /// <summary>True when the FieldName TextBox is editable. False in edit
    /// mode when the original field has incoming dependencies (rename would
    /// be rejected by Firebird). Bound to <c>IsEnabled</c> on the input.</summary>
    public bool CanRename { get; }

    /// <summary>Inverse of <see cref="CanRename"/> in edit mode — drives the
    /// "Cannot rename — field has dependencies" hint visibility.</summary>
    public bool ShowRenameBlockedHint => IsEditMode && !CanRename;

    /// <summary>True for tabs that emit non-ALTERable DDL (Check, Computed,
    /// Autoincrement). Bound to TabItem.IsEnabled in XAML so the user can
    /// still see them but not touch them in edit mode.</summary>
    public bool IsAddOnlyTabEnabled => IsAddMode;

    /// <summary>Dialog title — "Add field" or "Edit field <name>".</summary>
    public string DialogTitle => IsEditMode
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                        UiStrings.AddFieldDialogEditTitleFormat,
                        OriginalField?.Name ?? string.Empty)
        : UiStrings.AddFieldDialogTitle;

    private void SeedFromField(FieldInfo f)
    {
        // Use the generated public setters (not the backing fields) — the
        // CommunityToolkit analyzer (MVVMTK0034) requires it. PropertyChanged
        // fires for each, but that's fine: DdlPreview re-evaluates to reflect
        // the seeded state, which is exactly what we want for the live preview.
        FieldName = f.Name;
        NotNull = f.NotNull;
        PrimaryKey = f.IsPrimaryKey;
        DefaultValue = f.DefaultValue ?? string.Empty;
        Description = f.Description ?? string.Empty;
        ComputedExpression = f.ComputedSource ?? string.Empty;

        // Domain match — straight name lookup against the loaded Domains list.
        // When the field carries no Domain or the name doesn't resolve, we leave
        // SelectedDomain null and the dialog falls through to BasicType.
        if (!string.IsNullOrEmpty(f.Domain))
        {
            foreach (var d in Domains)
            {
                if (string.Equals(d.Name, f.Domain, System.StringComparison.Ordinal))
                {
                    SelectedDomain = d;
                    break;
                }
            }
        }

        // BasicType: prefer FieldInfo's parsed BaseTypeName (already stripped
        // of parens). Falls back to INTEGER for safety.
        var baseType = f.BaseTypeName?.ToUpperInvariant();
        if (string.IsNullOrEmpty(baseType)) baseType = "INTEGER";
        SelectedBasicType = baseType;

        // Size/Precision/Scale: FieldInfo.Size carries the parens value (length
        // for CHAR/VARCHAR, precision for NUMERIC/DECIMAL). Scale is separate.
        if (baseType is "CHAR" or "VARCHAR" or "CSTRING")
        {
            Size = f.Size;
        }
        else if (baseType is "NUMERIC" or "DECIMAL")
        {
            Precision = f.Size;
            Scale = f.Scale;
        }
        // BLOB sub-type isn't carried on FieldInfo today — leave the default
        // (TEXT). Editing the sub-type via ALTER is unsupported anyway.
    }

    public static IReadOnlyList<string> BasicTypes { get; } = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR", "DATE", "TIME", "TIMESTAMP", "BLOB",
    };

    public static IReadOnlyList<string> BlobSubTypes { get; } = new[]
    {
        "TEXT (1)", "BINARY (0)",
    };

    public string FieldNameLabel => UiStrings.AddFieldFieldName;
    public string NotNullLabel => UiStrings.AddFieldNotNull;
    public string PrimaryKeyLabel => UiStrings.AddFieldPrimaryKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _fieldName = string.Empty;

    // Identifiers (field, generator, trigger) always UPPERCASE — same coercion
    // pattern as NewTableTabViewModel + FieldRowViewModel.
    private bool _coercingFieldName;
    private bool _coercingNewGeneratorName;
    private bool _coercingTriggerName;
    partial void OnFieldNameChanged(string value)
    {
        if (_coercingFieldName) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, System.StringComparison.Ordinal))
        {
            _coercingFieldName = true;
            try { FieldName = upper; } finally { _coercingFieldName = false; }
        }
    }
    partial void OnNewGeneratorNameChanged(string value)
    {
        if (_coercingNewGeneratorName) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, System.StringComparison.Ordinal))
        {
            _coercingNewGeneratorName = true;
            try { NewGeneratorName = upper; } finally { _coercingNewGeneratorName = false; }
        }
    }
    partial void OnTriggerNameChanged(string value)
    {
        if (_coercingTriggerName) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, System.StringComparison.Ordinal))
        {
            _coercingTriggerName = true;
            try { TriggerName = upper; } finally { _coercingTriggerName = false; }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private bool _notNull;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(IsNotNullEnabled))]
    private bool _primaryKey;

    partial void OnPrimaryKeyChanged(bool value)
    {
        // PK implies NOT NULL — force it on and let IsNotNullEnabled disable the
        // checkbox so the user can't make a PK column nullable.
        if (value) NotNull = true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(IsDomainEmpty))]
    [NotifyPropertyChangedFor(nameof(HasDomain))]
    [NotifyPropertyChangedFor(nameof(SelectedDomainType))]
    [NotifyPropertyChangedFor(nameof(HasDomainType))]
    [NotifyPropertyChangedFor(nameof(IsBasicTypeTabEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ClearDomainCommand))]
    private DomainSpec? _selectedDomain;

    public bool IsDomainEmpty => SelectedDomain is null;

    /// <summary>True when a domain is selected — the column's type is then
    /// governed by the domain, so the Basic type tab is disabled (#3/#4).</summary>
    public bool HasDomain => SelectedDomain is not null;

    /// <summary>The resolved SQL type of the selected domain (e.g.
    /// <c>VARCHAR(80)</c>), surfaced so the user sees what the domain actually
    /// represents (#3). Empty when no domain is selected.</summary>
    public string SelectedDomainType => SelectedDomain?.Type ?? string.Empty;

    public bool HasDomainType => !string.IsNullOrEmpty(SelectedDomainType);

    /// <summary>Clears the domain selection so the field falls back to a basic
    /// type. There is no "(none)" entry in the list, so this is the way back to
    /// an unset domain (#5).</summary>
    [RelayCommand(CanExecute = nameof(IsDomainSelected))]
    private void ClearDomain() => SelectedDomain = null;

    public bool IsDomainSelected => SelectedDomain is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(ShowSize))]
    [NotifyPropertyChangedFor(nameof(ShowPrecisionScale))]
    [NotifyPropertyChangedFor(nameof(ShowBlobSubType))]
    private string? _selectedBasicType = "INTEGER";

    public bool ShowSize => SelectedBasicType is "CHAR" or "VARCHAR" or "CSTRING";
    public bool ShowPrecisionScale => SelectedBasicType is "NUMERIC" or "DECIMAL";
    public bool ShowBlobSubType => SelectedBasicType == "BLOB";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private int? _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private int? _precision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private int? _scale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string? _selectedBlobSubType = "TEXT (1)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _defaultValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _checkExpression = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(HasComputed))]
    [NotifyPropertyChangedFor(nameof(IsRegularTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDomainTabEnabled))]
    [NotifyPropertyChangedFor(nameof(IsBasicTypeTabEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDefaultTabEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCheckTabEnabled))]
    [NotifyPropertyChangedFor(nameof(IsAutoincTabEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryKeyEnabled))]
    [NotifyPropertyChangedFor(nameof(IsNotNullEnabled))]
    private string _computedExpression = string.Empty;

    // ─── Field-option dependency model (#4) ──────────────────────────────
    //
    // Firebird semantics, encoded as enable/disable gates + value coordination:
    //   Computed BY  →  mutually exclusive with Type / Domain / Default /
    //                   Not Null / Autoincrement / Primary Key / Check
    //                   (type is derived from the expression). Description stays.
    //   Domain       →  governs the type, so Basic type is disabled while a
    //                   domain is picked. Default / Not Null / Autoinc still OK.
    //   Autoincrement→  the generator/identity supplies the value, so Default
    //                   is disabled (and cleared) while engaged.
    //   Primary Key  →  implies NOT NULL: NotNull is forced true + disabled.

    /// <summary>True when the user has typed a COMPUTED BY expression.</summary>
    public bool HasComputed => !string.IsNullOrWhiteSpace(ComputedExpression);
    public bool HasAutoincrement => AutoIncrementMode != AutoIncrementMode.None;

    /// <summary>Back-compat alias retained for existing tests — equals
    /// <see cref="IsDomainTabEnabled"/>.</summary>
    public bool IsRegularTypeEnabled => !HasComputed;

    public bool IsDomainTabEnabled => !HasComputed;
    public bool IsBasicTypeTabEnabled => !HasComputed && !HasDomain;
    public bool IsDefaultTabEnabled => !HasComputed && !HasAutoincrement;
    public bool IsCheckTabEnabled => IsAddMode && !HasComputed;
    public bool IsAutoincTabEnabled => IsAddMode && !HasComputed;

    /// <summary>Primary Key checkbox: add-mode AND not computed.</summary>
    public bool IsPrimaryKeyEnabled => IsAddMode && !HasComputed;

    /// <summary>Not Null checkbox: disabled when computed (computed can't be
    /// NOT NULL in the column def) or when Primary Key forces it true.</summary>
    public bool IsNotNullEnabled => !HasComputed && !PrimaryKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(HasAutoincrement))]
    [NotifyPropertyChangedFor(nameof(IsDefaultTabEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowExistingGeneratorPicker))]
    [NotifyPropertyChangedFor(nameof(ShowNewGeneratorFields))]
    private AutoIncrementMode _autoIncrementMode = AutoIncrementMode.None;

    public bool ShowExistingGeneratorPicker => AutoIncrementMode == AutoIncrementMode.ExistingGenerator;
    public bool ShowNewGeneratorFields => AutoIncrementMode == AutoIncrementMode.NewGenerator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string? _selectedGenerator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _newGeneratorName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _triggerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>
    /// Live-preview DDL. Pure formula over the form state — read by the DDL tab
    /// every time any source property changes (every [ObservableProperty] above
    /// notifies <c>DdlPreview</c>).
    /// </summary>
    public string DdlPreview
    {
        get
        {
            // Field name might still be empty while the user fills the form —
            // BuildDefinition still emits a meaningful preview using a placeholder
            // name. We swap "" for "<field>" in the displayed DDL.
            var name = string.IsNullOrWhiteSpace(FieldName) ? "<field>" : FieldName.Trim();
            var def = BuildDefinitionCore(name);
            return DdlGenerator.BuildAddField(TableName, def);
        }
    }

    /// <summary>
    /// True when the form is valid enough to convert into a definition. The
    /// dialog gates OK on this.
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(FieldName))
        {
            ValidationMessage = UiStrings.AddFieldValidationNameRequired;
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Returns the dialog's final <see cref="FieldDefinition"/> with the trimmed
    /// field name (assumes <see cref="IsValid"/> already passed).
    /// </summary>
    public FieldDefinition BuildDefinition()
        => BuildDefinitionCore(FieldName.Trim());

    private FieldDefinition BuildDefinitionCore(string name)
    {
        BlobSubType? blob = null;
        if (ShowBlobSubType)
        {
            blob = SelectedBlobSubType?.StartsWith("BINARY", System.StringComparison.OrdinalIgnoreCase) == true
                ? Core.Metadata.BlobSubType.Binary
                : Core.Metadata.BlobSubType.Text;
        }

        var generatorName = AutoIncrementMode switch
        {
            AutoIncrementMode.ExistingGenerator => SelectedGenerator,
            AutoIncrementMode.NewGenerator => NewGeneratorName,
            _ => null,
        };

        return new FieldDefinition
        {
            Name = name,
            NotNull = NotNull,
            PrimaryKey = PrimaryKey,
            Domain = SelectedDomain?.Name,
            BasicType = SelectedBasicType,
            Size = ShowSize ? Size : null,
            Precision = ShowPrecisionScale ? Precision : null,
            Scale = ShowPrecisionScale ? Scale : null,
            BlobSubType = blob,
            DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue,
            CheckExpression = string.IsNullOrWhiteSpace(CheckExpression) ? null : CheckExpression,
            ComputedExpression = string.IsNullOrWhiteSpace(ComputedExpression) ? null : ComputedExpression,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            AutoIncrement = AutoIncrementMode,
            GeneratorName = generatorName,
            TriggerName = string.IsNullOrWhiteSpace(TriggerName) ? null : TriggerName,
        };
    }

    // Radio-button helpers — Avalonia RadioButton doesn't easily bind to an enum,
    // so we expose four separate IsAutoincX properties with TwoWay binding. Setting
    // any of them to true updates AutoIncrementMode; setters are tolerant of the
    // "set to false" half of a radio toggle (no-op when the bit isn't ours).

    public bool IsAutoincNone
    {
        get => AutoIncrementMode == AutoIncrementMode.None;
        set { if (value) AutoIncrementMode = AutoIncrementMode.None; OnPropertyChanged(nameof(IsAutoincNone)); }
    }

    public bool IsAutoincIdentity
    {
        get => AutoIncrementMode == AutoIncrementMode.Identity;
        set { if (value) AutoIncrementMode = AutoIncrementMode.Identity; OnPropertyChanged(nameof(IsAutoincIdentity)); }
    }

    public bool IsAutoincExisting
    {
        get => AutoIncrementMode == AutoIncrementMode.ExistingGenerator;
        set { if (value) AutoIncrementMode = AutoIncrementMode.ExistingGenerator; OnPropertyChanged(nameof(IsAutoincExisting)); }
    }

    public bool IsAutoincNew
    {
        get => AutoIncrementMode == AutoIncrementMode.NewGenerator;
        set { if (value) AutoIncrementMode = AutoIncrementMode.NewGenerator; OnPropertyChanged(nameof(IsAutoincNew)); }
    }

    partial void OnAutoIncrementModeChanged(AutoIncrementMode value)
    {
        // An autoincremented column's value comes from the identity/generator —
        // a manual DEFAULT would be redundant (and invalid alongside IDENTITY).
        // Clear it when autoinc engages (#4). Empty default is harmless.
        if (value != AutoIncrementMode.None) DefaultValue = string.Empty;

        // Sync sibling radio properties so the dialog's RadioButton group repaints
        // when AutoIncrementMode flips via any of them.
        OnPropertyChanged(nameof(IsAutoincNone));
        OnPropertyChanged(nameof(IsAutoincIdentity));
        OnPropertyChanged(nameof(IsAutoincExisting));
        OnPropertyChanged(nameof(IsAutoincNew));
    }

    public event System.Action? RequestClose;
    public FieldDefinition? Result { get; private set; }

    [RelayCommand]
    private void Accept()
    {
        if (!IsValid()) return;
        Result = BuildDefinition();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}
