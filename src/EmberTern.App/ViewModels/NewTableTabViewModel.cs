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

    [ObservableProperty] private bool _primaryKey;
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsPrecisionScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedDomainSpec))]
    private string _type = "INTEGER";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDomainSpec))]
    private string? _domainName;

    [ObservableProperty] private string _defaultValue = string.Empty;
    [ObservableProperty] private string _computedExpression = string.Empty;
    [ObservableProperty] private string _checkExpression = string.Empty;
    [ObservableProperty] private int? _size;
    [ObservableProperty] private int? _scale;
    [ObservableProperty] private bool _notNull;
    [ObservableProperty] private string? _charset;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _autoIncrement;

    public bool IsSizeEnabled => Type is "CHAR" or "VARCHAR" or "NUMERIC" or "DECIMAL";
    public bool IsPrecisionScaleEnabled => Type is "NUMERIC" or "DECIMAL";

    public IReadOnlyList<string> BasicTypes => _owner?.BasicTypes ?? FallbackBasicTypes;
    public ObservableCollection<DomainSpec> AvailableDomains
        => _owner?.AvailableDomains ?? FallbackDomains;

    /// <summary>Wrapper so the Domain ComboBox can bind SelectedItem to a DomainSpec
    /// while the underlying DomainName stays a plain string (matches the inline-edit
    /// pattern in FieldRowViewModel).</summary>
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
public partial class NewTableTabViewModel : ViewModelBase
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _tableName = string.Empty;

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

    [ObservableProperty]
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
        Fields.Move(idx, t);
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
