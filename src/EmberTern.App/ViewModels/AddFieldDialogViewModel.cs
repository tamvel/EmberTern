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
    {
        TableName = tableName;
        Domains = new ObservableCollection<DomainSpec>(domains);
        Generators = new ObservableCollection<string>(generators);
    }

    public string TableName { get; }
    public ObservableCollection<DomainSpec> Domains { get; }
    public ObservableCollection<string> Generators { get; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private bool _notNull;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private bool _primaryKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(IsDomainEmpty))]
    private DomainSpec? _selectedDomain;

    public bool IsDomainEmpty => SelectedDomain is null;

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
    private string _computedExpression = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
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
