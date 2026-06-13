using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class TableDetailTabViewModel : ViewModelBase
{
    // Data preview is capped — we never want to pull a whole table into the
    // grid from a metadata-browsing tab. 200 is the default page size for
    // pagination (used by both the initial load and the Refresh button).
    public const int DataPreviewRowLimit = 200;

    // Hard upper bound on PageSize. The user can bump PageSize up to this; the
    // grid stays usable but each fetch grows linearly. 1000 is the spec.
    public const int MaxPageSize = 1000;

    // Hard upper bound on how far GoToLastPageCommand will probe. The COUNT(*)
    // query is wrapped in SELECT FIRST {cap} so it never sequential-scans
    // beyond this many rows, even on tables with tens of millions of records.
    public const int RowCountCap = 50000;

    // Index of the "Dane" sub-tab inside the inner TabControl. Must match the
    // TabItem order in TableDetailTabView.axaml (Pola, Ograniczenia, Indeksy,
    // Zależności, Dane, Opis, DDL).
    public const int DataSubTabIndex = 4;

    // Index of the "Pola" sub-tab — leftmost, default-selected. The main
    // toolbar binds ⚡ ＋ − ↑ ↓ visibility to this so structural-edit affordances
    // live in a single chrome row alongside Execute/Commit/Pagination.
    public const int FieldsSubTabIndex = 0;

    // Index of the "Ograniczenia" sub-tab. Used by the FK wizard's
    // post-create UX which jumps the user to Constraints → Foreign Keys
    // after a successful CREATE.
    public const int ConstraintsSubTabIndex = 1;

    // Inner Constraints TabControl tab indices: Primary Key (0) /
    // Foreign Keys (1) / Check (2) / Unique (3). Must match the TabItem
    // order in TableDetailTabView.axaml's nested Constraints TabControl.
    public const int ConstraintsForeignKeysIndex = 1;

    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;
    private readonly FirebirdDataEditor? _dataEditor;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private readonly FirebirdMetadataReader? _metadataReader;

    // Original PK snapshots per row reference. We capture them on row load (when
    // we know the PK columns) so UPDATE/DELETE can identify the row even after
    // the user edits a PK cell.
    private readonly Dictionary<object?[], object?[]> _pkSnapshots = new(ReferenceEqualityComparer.Instance);

    // Rows that were added in-grid via AddRowCommand and haven't been INSERTed
    // yet. INSERT fires when the user "confirms" the row (RowEditEnding) — we
    // remove from this set on success so a subsequent cell edit becomes an UPDATE.
    private readonly HashSet<object?[]> _newRows = new(ReferenceEqualityComparer.Instance);


    // Tracks the in-flight (or completed) load. EnsureLoadedAsync returns this
    // task — second-and-subsequent callers get the same Task back and join the
    // already-running load instead of kicking off a duplicate, which would
    // collide on the single-statement FbConnection. Reset implicitly on
    // disconnect/reconnect because LoadWorkspaceFor builds a fresh VM instance.
    private Task? _loadTask;

    public TableDetailTabViewModel(string tableName)
        : this(tableName, null, null, null, null, null)
    {
    }

    public TableDetailTabViewModel(string tableName, FirebirdTableDetailReader? reader, FirebirdDdlReader? ddlReader)
        : this(tableName, reader, ddlReader, null, null, null)
    {
    }

    public TableDetailTabViewModel(string tableName, FirebirdTableDetailReader? reader, FirebirdDdlReader? ddlReader, FirebirdDataEditor? dataEditor)
        : this(tableName, reader, ddlReader, dataEditor, null, null)
    {
    }

    public TableDetailTabViewModel(string tableName, FirebirdTableDetailReader? reader, FirebirdDdlReader? ddlReader, FirebirdDataEditor? dataEditor, FirebirdDdlExecutor? ddlExecutor)
        : this(tableName, reader, ddlReader, dataEditor, ddlExecutor, null)
    {
    }

    public TableDetailTabViewModel(string tableName, FirebirdTableDetailReader? reader, FirebirdDdlReader? ddlReader, FirebirdDataEditor? dataEditor, FirebirdDdlExecutor? ddlExecutor, FirebirdMetadataReader? metadataReader)
    {
        TableName = tableName;
        _reader = reader;
        _ddlReader = ddlReader;
        _dataEditor = dataEditor;
        _ddlExecutor = ddlExecutor;
        _metadataReader = metadataReader;
        Fields = new ObservableCollection<FieldInfo>();
        EditableFields = new ObservableCollection<FieldRowViewModel>();
        AvailableDomains = new ObservableCollection<DomainSpec>();
        FieldDependencies = new ObservableCollection<FieldDependencyItem>();
        BasicTypes = new[]
        {
            "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
            "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
            "DATE", "TIME", "TIMESTAMP", "BLOB",
        };
        Indexes = new ObservableCollection<IndexInfo>();
        Constraints = new ObservableCollection<ConstraintInfo>();
        DependsOn = new ObservableCollection<DependencyInfo>();
        DependedOnBy = new ObservableCollection<DependencyInfo>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
        EditableRows = new ObservableCollection<object?[]>();
        PendingChanges = new ObservableCollection<PendingDdlChange>();
        Constraints.CollectionChanged += OnConstraintsCollectionChanged;
        PendingChanges.CollectionChanged += OnPendingChangesCollectionChanged;
        Fields.CollectionChanged += OnFieldsCollectionChanged;
        // Field-dependencies panel: rebuild whenever DependedOnBy mutates
        // (i.e. after every refresh — LoadAsync clears+repopulates the
        // collection so the panel auto-syncs without an explicit hook in
        // each callsite).
        DependedOnBy.CollectionChanged += OnDependedOnByCollectionChanged;
    }

    // Mirror Fields → EditableFields whenever Fields changes (load + post-Compile
    // re-load both clear-and-rebuild Fields). EditableFields is what the Pola
    // grid binds to; FieldRowViewModel forwards read-only props and surfaces
    // owner-side AvailableDomains / BasicTypes / CanEditStructure for the in-cell editors.
    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EditableFields.Clear();
        foreach (var f in Fields) EditableFields.Add(new FieldRowViewModel(f, this));
    }

    /// <summary>
    /// Fires when the VM wants the user to confirm a destructive operation
    /// (e.g. row delete). The owner translates this to a ConfirmDialog in the
    /// usual way. When unset, all confirmations are auto-accepted (test mode).
    /// </summary>
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;

    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    // Strips the size/precision suffix from a type string for base-type
    // comparison: "VARCHAR(50)" → "VARCHAR", "NUMERIC(15,2)" → "NUMERIC".
    private static string StripTypeSize(string? type)
    {
        if (string.IsNullOrEmpty(type)) return string.Empty;
        var paren = type.IndexOf('(');
        return paren < 0 ? type : type.Substring(0, paren).TrimEnd();
    }

    public string TableName { get; }

    public ObservableCollection<FieldInfo> Fields { get; }
    /// <summary>
    /// Editable wrappers around <see cref="Fields"/>, one per row. Rebuilt
    /// every time Fields changes. Bound to the Pola DataGrid for inline editing.
    /// </summary>
    public ObservableCollection<FieldRowViewModel> EditableFields { get; }

    /// <summary>
    /// Domain list for the inline Domain ComboBox. Populated by the load chain;
    /// the FieldRowViewModel surfaces a reference to this via its owner so the
    /// DataGrid's CellEditingTemplate can bind directly.
    /// </summary>
    public ObservableCollection<DomainSpec> AvailableDomains { get; }

    /// <summary>Basic SQL types — used by the Type ComboBox in inline edit.</summary>
    public IReadOnlyList<string> BasicTypes { get; }

    /// <summary>
    /// True when the named field has no incoming dependencies — i.e. nothing
    /// in <see cref="DependedOnBy"/> references it. Rename is only safe in
    /// that case (Firebird rejects ALTER COLUMN TO when triggers / views / etc.
    /// still reference the old name).
    /// </summary>
    public bool CanRenameField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;
        foreach (var dep in DependedOnBy)
        {
            if (string.Equals(dep.FieldName, fieldName, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Inspects the edited row vs. its original and queues
    /// <see cref="PendingDdlChange"/> entries via the shared
    /// <see cref="DdlGenerator.BuildAlterStatements"/>. Called by the
    /// view's RowEditEnding handler. Idempotent — re-editing a row simply
    /// queues another batch of ALTERs.
    /// </summary>
    public void EnqueueRowEdits(FieldRowViewModel row)
    {
        if (row is null) return;
        var original = row.Original;
        var canRename = CanRenameField(original.Name);

        // Type clause — set ONLY when the user genuinely changed the type or
        // domain. This pre-filter is necessary because the two representations
        // don't compare cleanly against original.Type:
        //   - Domain columns: original.Type is the RESOLVED type (e.g.
        //     "NUMERIC(15,2)") while DomainName is "T_KWOTA". Passing the
        //     domain name unconditionally would always read as a change.
        //   - Basic-type columns: the Type ComboBox offers base types, but
        //     original.Type is the full string. We compare base-to-base.
        // When nothing changed we pass null so BuildAlterStatements emits no
        // type ALTER (the spec's "no-op when no change" rule).
        string? typeClause = null;
        var domainChanged = !string.Equals(
            row.DomainName ?? string.Empty, original.Domain ?? string.Empty,
            System.StringComparison.OrdinalIgnoreCase);
        var baseTypeChanged = !string.Equals(
            StripTypeSize(row.TypeText), StripTypeSize(original.Type),
            System.StringComparison.OrdinalIgnoreCase);
        if (domainChanged && !string.IsNullOrWhiteSpace(row.DomainName))
        {
            typeClause = row.DomainName;
        }
        else if (baseTypeChanged && !string.IsNullOrWhiteSpace(row.TypeText))
        {
            typeClause = row.TypeText;
        }

        var target = new AlterFieldTarget
        {
            Name = row.Name,
            TypeClause = typeClause,
            NotNull = row.NotNull,
            DefaultValue = row.DefaultValue,
            Description = row.Description,
        };

        var statements = DdlGenerator.BuildAlterStatements(TableName, original, target, canRename);
        foreach (var s in statements) PendingChanges.Add(s);

        // UX: when the user attempted a rename / type-change but the field has
        // incoming dependencies, BuildAlterStatements silently skipped them.
        // Revert the displayed values + surface the standard "rename blocked"
        // hint so the user knows why their edit didn't take.
        if (!canRename)
        {
            bool attemptedRename = !string.Equals(row.Name, original.Name, System.StringComparison.Ordinal);
            // typeClause is non-null only on a genuine type/domain change (see
            // the pre-filter above), so its presence IS the "attempted" signal.
            bool attemptedTypeChange = typeClause is not null;
            if (attemptedRename) row.Name = original.Name;
            if (attemptedTypeChange)
            {
                row.TypeText = original.Type;
                row.DomainName = original.Domain;
            }
            if (attemptedRename || attemptedTypeChange)
            {
                ErrorMessage = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    UiStrings.FieldEditRenameBlockedFormat, original.Name);
            }
        }
    }
    public ObservableCollection<IndexInfo> Indexes { get; }
    public ObservableCollection<ConstraintInfo> Constraints { get; }
    public ObservableCollection<DependencyInfo> DependsOn { get; }
    public ObservableCollection<DependencyInfo> DependedOnBy { get; }
    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

    /// <summary>
    /// Per-field dependencies for the Pola sub-tab's bottom panel. Filtered
    /// view over <see cref="DependedOnBy"/> matching the currently
    /// <see cref="SelectedField"/>. Rebuilt automatically whenever the
    /// selection changes or <see cref="DependedOnBy"/> repopulates (e.g.
    /// after <see cref="RefreshStructureAsync"/> runs).
    /// </summary>
    public ObservableCollection<FieldDependencyItem> FieldDependencies { get; }

    /// <summary>Selected row in the dependency panel — bound TwoWay so the
    /// Enter keybinding can fire the right item's NavigateCommand.</summary>
    [ObservableProperty]
    private FieldDependencyItem? _selectedFieldDependency;

    /// <summary>Fires the selected dependency's NavigateCommand. Bound to the
    /// dependency grid's Enter keybinding; the double-click path goes through
    /// the row's own item in code-behind. No-op when nothing is selected or
    /// the selection isn't navigable.</summary>
    [RelayCommand]
    private void NavigateSelectedDependency()
    {
        if (SelectedFieldDependency is { CanNavigate: true } item)
        {
            item.NavigateCommand.Execute(null);
        }
    }

    public bool HasFieldDependencies => FieldDependencies.Count > 0;
    public bool HasFieldSelectionForDependencies => SelectedField is not null;
    public bool ShowFieldDependenciesEmpty
        => SelectedField is not null && FieldDependencies.Count == 0;
    public bool ShowFieldDependenciesNoSelection => SelectedField is null;

    /// <summary>
    /// Fired when the user double-clicks a dependency leaf in the tree. The
    /// owner (MainWindowViewModel) reuses its existing OnOpenDdlRequested path
    /// to open a TableDetail tab (for tables) or a DDL tab (other kinds).
    /// </summary>
    public event Action<MetadataObject>? OpenObjectRequested;

    /// <summary>
    /// Resolves the dependency leaf to a (Name, Kind) MetadataObject and raises
    /// <see cref="OpenObjectRequested"/>. Silently no-ops for kinds that aren't
    /// independently openable (e.g. Field, "Object (N)" fallbacks).
    /// </summary>
    public void RequestOpen(DependencyInfo dependency)
    {
        if (dependency is null || string.IsNullOrEmpty(dependency.ObjectName)) return;
        var kind = MapObjectTypeToKind(dependency.ObjectType);
        if (kind is null) return;
        OpenObjectRequested?.Invoke(new MetadataObject(dependency.ObjectName, kind.Value));
    }

    public void RequestOpen(DependencyLeafNode leaf)
    {
        if (leaf is null) return;
        RequestOpen(leaf.Dependency);
    }

    // Inverse of FirebirdTableDetailReader.MapObjectType. Kinds without an
    // independent open-tab affordance (Field, unknown "Object (N)") return null;
    // RequestOpen treats null as a silent no-op.
    internal static MetadataObjectKind? MapObjectTypeToKind(string? objectType) => objectType switch
    {
        "Table" => MetadataObjectKind.Table,
        "View" => MetadataObjectKind.View,
        "Trigger" => MetadataObjectKind.Trigger,
        "Procedure" => MetadataObjectKind.Procedure,
        "Exception" => MetadataObjectKind.Exception,
        "Generator" => MetadataObjectKind.Generator,
        "Function" => MetadataObjectKind.Function,
        "Package" => MetadataObjectKind.Package,
        "Index" => MetadataObjectKind.Index,
        "User" => MetadataObjectKind.User,
        "Domain" => MetadataObjectKind.Domain,
        _ => null,
    };

    // IBExpert-style fixed category order for the dependency tree. Every entry
    // appears as a root node even when empty. ObjectTypeKey matches the singular
    // value MapObjectType returns; DisplayLabel is the plural shown in headers.
    // "UDF" has no matching dependency type code today — it stays as a fixed
    // empty placeholder so the category list mirrors IBExpert exactly.
    internal static readonly IReadOnlyList<DependencyCategory> CategoryOrder = new[]
    {
        new DependencyCategory("Domain",    MetadataObjectKind.Domain,    UiStrings.MetadataGroupDomains),
        new DependencyCategory("Table",     MetadataObjectKind.Table,     UiStrings.MetadataGroupTables),
        new DependencyCategory("View",      MetadataObjectKind.View,      UiStrings.MetadataGroupViews),
        new DependencyCategory("Procedure", MetadataObjectKind.Procedure, UiStrings.MetadataGroupProcedures),
        new DependencyCategory("Function",  MetadataObjectKind.Function,  UiStrings.MetadataGroupFunctions),
        new DependencyCategory("Package",   MetadataObjectKind.Package,   UiStrings.MetadataGroupPackages),
        new DependencyCategory("Trigger",   MetadataObjectKind.Trigger,   UiStrings.MetadataGroupTriggers),
        new DependencyCategory("Exception", MetadataObjectKind.Exception, UiStrings.MetadataGroupExceptions),
        new DependencyCategory("UDF",       null,                         UiStrings.DependencyCategoryUdfs),
        new DependencyCategory("Generator", MetadataObjectKind.Generator, UiStrings.MetadataGroupGenerators),
        new DependencyCategory("Index",     MetadataObjectKind.Index,     UiStrings.MetadataGroupIndexes),
    };

    internal sealed record DependencyCategory(string ObjectTypeKey, MetadataObjectKind? Kind, string DisplayLabel);

    internal static IReadOnlyList<DependencyGroupNode> BuildDependencyTree(IEnumerable<DependencyInfo> dependencies)
    {
        // Dedup by ObjectName within each category — the same object can show up
        // multiple times when several fields reference it (e.g. one trigger that
        // touches three columns); the tree should surface it as one leaf.
        var byType = dependencies
            .GroupBy(d => d.ObjectType, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DependencyInfo>)g
                    .DistinctBy(d => d.ObjectName, StringComparer.Ordinal)
                    .OrderBy(d => d.ObjectName, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        var groups = new List<DependencyGroupNode>(CategoryOrder.Count);
        foreach (var category in CategoryOrder)
        {
            var icon = category.Kind is { } k ? MetadataNodeViewModel.IconFor(k) : string.Empty;
            var iconKey = category.Kind is { } k2 ? MetadataNodeViewModel.ResourceKeyFor(k2) : string.Empty;

            IReadOnlyList<DependencyLeafNode> leaves = Array.Empty<DependencyLeafNode>();
            if (byType.TryGetValue(category.ObjectTypeKey, out var matched))
            {
                leaves = matched
                    .Select(d => new DependencyLeafNode
                    {
                        Dependency = d,
                        Icon = icon,
                        IconResourceKey = iconKey,
                    })
                    .ToList();
            }

            groups.Add(new DependencyGroupNode
            {
                ObjectType = category.DisplayLabel,
                Children = leaves,
                Icon = icon,
                IconResourceKey = iconKey,
            });
        }
        return groups;
    }

    // Filtered views over Constraints, one per constraint kind. Plain get-only
    // properties (not [ObservableProperty]) per spec; refresh is driven by
    // OnConstraintsCollectionChanged raising PropertyChanged on each filter +
    // its count + its HasX flag whenever the underlying collection mutates.
    public IReadOnlyList<ConstraintInfo> PrimaryKeyConstraints
        => Filter("PRIMARY KEY");

    public IReadOnlyList<ConstraintInfo> ForeignKeyConstraints
        => Filter("FOREIGN KEY");

    public IReadOnlyList<ConstraintInfo> CheckConstraints
        => Filter("CHECK");

    public IReadOnlyList<ConstraintInfo> UniqueConstraints
        => Filter("UNIQUE");

    public int PrimaryKeyConstraintCount => PrimaryKeyConstraints.Count;
    public int ForeignKeyConstraintCount => ForeignKeyConstraints.Count;
    public int CheckConstraintCount => CheckConstraints.Count;
    public int UniqueConstraintCount => UniqueConstraints.Count;

    public bool HasPrimaryKeyConstraints => PrimaryKeyConstraintCount > 0;
    public bool HasForeignKeyConstraints => ForeignKeyConstraintCount > 0;
    public bool HasCheckConstraints => CheckConstraintCount > 0;
    public bool HasUniqueConstraints => UniqueConstraintCount > 0;

    public string PrimaryKeyTabHeader => FormatHeader(UiStrings.TableDetailConstraintSubTabPrimaryKey, PrimaryKeyConstraintCount);
    public string ForeignKeyTabHeader => FormatHeader(UiStrings.TableDetailConstraintSubTabForeignKey, ForeignKeyConstraintCount);
    public string CheckTabHeader => FormatHeader(UiStrings.TableDetailConstraintSubTabCheck, CheckConstraintCount);
    public string UniqueTabHeader => FormatHeader(UiStrings.TableDetailConstraintSubTabUnique, UniqueConstraintCount);

    private static string FormatHeader(string label, int count) => $"{label} ({count})";

    private IReadOnlyList<ConstraintInfo> Filter(string constraintType)
        => Constraints
            .Where(c => string.Equals(c.ConstraintType, constraintType, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private void OnConstraintsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PrimaryKeyConstraints));
        OnPropertyChanged(nameof(ForeignKeyConstraints));
        OnPropertyChanged(nameof(CheckConstraints));
        OnPropertyChanged(nameof(UniqueConstraints));
        OnPropertyChanged(nameof(PrimaryKeyConstraintCount));
        OnPropertyChanged(nameof(ForeignKeyConstraintCount));
        OnPropertyChanged(nameof(CheckConstraintCount));
        OnPropertyChanged(nameof(UniqueConstraintCount));
        OnPropertyChanged(nameof(HasPrimaryKeyConstraints));
        OnPropertyChanged(nameof(HasForeignKeyConstraints));
        OnPropertyChanged(nameof(HasCheckConstraints));
        OnPropertyChanged(nameof(HasUniqueConstraints));
        OnPropertyChanged(nameof(PrimaryKeyTabHeader));
        OnPropertyChanged(nameof(ForeignKeyTabHeader));
        OnPropertyChanged(nameof(CheckTabHeader));
        OnPropertyChanged(nameof(UniqueTabHeader));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDataSubTabActive))]
    [NotifyPropertyChangedFor(nameof(IsFieldsSubTabActive))]
    private int _activeSubTabIndex;

    public bool IsDataSubTabActive => ActiveSubTabIndex == DataSubTabIndex;
    public bool IsFieldsSubTabActive => ActiveSubTabIndex == FieldsSubTabIndex;

    /// <summary>
    /// Two-way bound to the nested Constraints TabControl's SelectedIndex.
    /// The FK wizard's post-create flow sets this to
    /// <see cref="ConstraintsForeignKeysIndex"/> so the user lands on the
    /// "Foreign Keys" sub-tab and sees the new constraint in the list.
    /// </summary>
    [ObservableProperty]
    private int _constraintsActiveSubTabIndex;

    [ObservableProperty]
    private string _ddlText = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(ShowDescriptionEmpty))]
    private bool _descriptionLoaded;

    public bool HasDescription => DescriptionLoaded && !string.IsNullOrEmpty(Description);
    public bool ShowDescriptionEmpty => DescriptionLoaded && string.IsNullOrEmpty(Description);

    public string DataPreviewHint
    {
        get
        {
            // No data yet — show the page-only placeholder. With pagination the
            // "showing N rows" line is meaningless before the first fetch.
            if (DataResult is null || !DataResult.HasResultSet)
            {
                return string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    UiStrings.TableDetailDataPagedHintFormat,
                    CurrentPage,
                    0);
            }

            var count = DataResult.Rows.Count;
            var baseHint = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.TableDetailDataPagedHintFormat,
                CurrentPage,
                count);

            if (!string.IsNullOrEmpty(SortColumn))
            {
                var arrow = SortDescending ? "↓" : "↑";
                baseHint += string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    UiStrings.TableDetailDataPreviewSortedByFormat,
                    SortColumn,
                    arrow);
            }
            return baseHint;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataResult))]
    [NotifyPropertyChangedFor(nameof(ShowDataError))]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    private QueryResult? _dataResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    private string? _sortColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    private bool _sortDescending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataResult))]
    [NotifyPropertyChangedFor(nameof(ShowDataError))]
    private string _dataError = string.Empty;

    // Bump on each successful data fetch so the view can rebuild grid columns
    // (DataGrid columns are imperative — we can't bind them; same pattern as
    // CurrentResultVersionTag in MainWindowViewModel).
    [ObservableProperty]
    private string _dataResultVersionTag = string.Empty;

    public bool HasDataResult => DataResult is { HasResultSet: true } && string.IsNullOrEmpty(DataError);
    public bool ShowDataError => !string.IsNullOrEmpty(DataError);

    // ─── Pagination state ─────────────────────────────────────────────────
    //
    // CurrentPage is 1-based. PageSize defaults to DataPreviewRowLimit (200);
    // the user can bump it up to MaxPageSize (1000). LastKnownRowCount is set
    // only after GoToLastPageCommand probes COUNT(*) (capped at RowCountCap);
    // it stays null otherwise and HasNextPage falls back to "current page is
    // full" heuristic.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyCanExecuteChangedFor(nameof(GoToFirstPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToPreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToNextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToLastPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    private int _pageSize = DataPreviewRowLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyCanExecuteChangedFor(nameof(GoToNextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToLastPageCommand))]
    private int? _lastKnownRowCount;

    public bool HasPreviousPage => CurrentPage > 1;

    /// <summary>
    /// True when there might be more rows after the current page. Two signals:
    /// (1) the COUNT(*) probe (LastKnownRowCount) authoritatively says there
    /// are more pages; (2) before the probe runs, "current page is full" is a
    /// fallback heuristic (likely-but-not-guaranteed more rows). False when
    /// the current page came back partial.
    /// </summary>
    public bool HasNextPage
    {
        get
        {
            if (LastKnownRowCount is { } known)
            {
                return CurrentPage * PageSize < known;
            }
            return DataResult is { HasResultSet: true } r && r.Rows.Count >= PageSize;
        }
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value < 1) PageSize = 1;
        else if (value > MaxPageSize) PageSize = MaxPageSize;
    }

    /// <summary>
    /// Writable mirror of <see cref="DataResult"/>.Rows. The DataGrid binds to
    /// this so AddRow / DeleteRow can mutate the visible row list without
    /// allocating a fresh QueryResult. Re-populated from DataResult.Rows in
    /// <see cref="RebuildEditableRows"/> after every successful preview fetch.
    /// </summary>
    public ObservableCollection<object?[]> EditableRows { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRowCommand))]
    private object?[]? _selectedRow;

    [ObservableProperty]
    private string _editStatusMessage = string.Empty;

    public bool HasEditStatusMessage => !string.IsNullOrEmpty(EditStatusMessage);
    public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;

    /// <summary>
    /// Primary-key columns, in declaration order. Built from <see cref="Fields"/>
    /// where IsPrimaryKey=true after a load completes. Empty when the table has
    /// no PK (UPDATE / DELETE remain unavailable in that case — INSERT still works).
    /// </summary>
    public IReadOnlyList<string> PrimaryKeyColumns { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Column-name → column-index map for the current data preview. Maintained
    /// alongside DataResult so cell-edit handlers can resolve PK column indices
    /// without re-scanning columns on every keystroke.
    /// </summary>
    public IReadOnlyDictionary<string, int> ColumnIndex { get; private set; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when this VM has a data editor wired — DataGrid editing in the Dane
    /// sub-tab is gated on this. The grid is read-only when there's no editor
    /// (test / restored-from-cache scenarios where the editor wasn't supplied).
    /// </summary>
    public bool CanEditData => _dataEditor is not null;

    /// <summary>
    /// Inverse of <see cref="CanEditData"/> — bound to DataGrid.IsReadOnly so the
    /// grid becomes editable as soon as an editor is wired.
    /// </summary>
    public bool IsDataReadOnly => !CanEditData;

    public string EditModeHint => HasPrimaryKey
        ? string.Empty
        : UiStrings.DataEditNoPrimaryKeyHint;

    // Re-populate EditableRows and the per-row PK snapshot whenever a fresh
    // preview lands. Existing tests set DataResult directly — this keeps the
    // mirror collection in sync without those tests caring about it.
    partial void OnDataResultChanged(QueryResult? value)
    {
        RebuildEditableRows();
        // HasNextPage reads DataResult.Rows.Count when LastKnownRowCount is null —
        // so the property re-fires whenever the result lands.
        OnPropertyChanged(nameof(HasNextPage));
        GoToNextPageCommand.NotifyCanExecuteChanged();
    }

    private void RebuildEditableRows()
    {
        // Defensive: re-derive PK from Fields before we read PrimaryKeyColumns.
        // Without this, a data refresh that lands before LoadAsync's Fields step
        // (or any path that bypasses LoadAsync) would leave PrimaryKeyColumns
        // stale and the edit hint would falsely say "no PK". See gotcha note.
        if (Fields.Count > 0) RefreshPrimaryKeyColumns();

        _pkSnapshots.Clear();
        _newRows.Clear();
        EditableRows.Clear();

        if (DataResult is not { HasResultSet: true } r) return;

        // Rebuild the column-index lookup. Case-insensitive — Firebird stores
        // column names uppercase by default, but the user may have entered
        // them mixed-case.
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.Columns.Count; i++)
        {
            index[r.Columns[i].Name] = i;
        }
        ColumnIndex = index;

        // Capture PK column indices; build per-row PK snapshots so an UPDATE
        // can identify the row even after the user edits a PK cell.
        var pkIndices = ResolvePkIndices(index);
        foreach (var row in r.Rows)
        {
            EditableRows.Add(row);
            if (pkIndices.Count > 0)
            {
                var snapshot = new object?[pkIndices.Count];
                for (int i = 0; i < pkIndices.Count; i++)
                {
                    snapshot[i] = row[pkIndices[i]];
                }
                _pkSnapshots[row] = snapshot;
            }
        }
    }

    private List<int> ResolvePkIndices(IReadOnlyDictionary<string, int> columnIndex)
    {
        var result = new List<int>(PrimaryKeyColumns.Count);
        foreach (var pk in PrimaryKeyColumns)
        {
            if (columnIndex.TryGetValue(pk, out var idx)) result.Add(idx);
        }
        return result;
    }

    /// <summary>
    /// Recomputes <see cref="PrimaryKeyColumns"/> from the current <see cref="Fields"/>.
    /// Called after each Fields load and whenever fields change. Public so the
    /// LoadAsync path can drive the refresh deterministically; also called from
    /// <see cref="RebuildEditableRows"/> indirectly via the load chain.
    /// </summary>
    public void RefreshPrimaryKeyColumns()
    {
        PrimaryKeyColumns = Fields.Where(f => f.IsPrimaryKey).Select(f => f.Name).ToList();
        OnPropertyChanged(nameof(PrimaryKeyColumns));
        OnPropertyChanged(nameof(HasPrimaryKey));
        OnPropertyChanged(nameof(EditModeHint));
        DeleteRowCommand.NotifyCanExecuteChanged();
    }

    public bool CanAddRow => _dataEditor is not null;

    // Auto-begin the working transaction here so the toolbar Commit/Rollback
    // buttons reflect active-tx state immediately, not only after the user fills
    // a cell and the row commit fires INSERT. TransactionService raises
    // TransactionStateChanged on Begin → MainWindowViewModel re-notifies its
    // IsTransactionActive / HasExecutedInTransaction / TransactionBarText.
    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private async Task AddRowAsync()
    {
        if (_dataEditor is null) return;
        var columnCount = DataResult?.Columns.Count ?? 0;
        if (columnCount == 0) return;

        try
        {
            await _dataEditor.EnsureTransactionAsync().ConfigureAwait(true);
        }
        catch (TransactionFailedException ex)
        {
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }
        catch (InvalidOperationException ex)
        {
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }

        var row = new object?[columnCount];
        EditableRows.Add(row);
        _newRows.Add(row);
        SelectedRow = row;
        EditStatusMessage = string.Empty;
        OnPropertyChanged(nameof(HasEditStatusMessage));
    }

    public bool CanDeleteRow
        => _dataEditor is not null
           && SelectedRow is not null
           && HasPrimaryKey
           && _pkSnapshots.ContainsKey(SelectedRow);

    [RelayCommand(CanExecute = nameof(CanDeleteRow))]
    private async Task DeleteRowAsync()
    {
        if (SelectedRow is not { } row) return;
        if (_dataEditor is null) return;
        if (!_pkSnapshots.TryGetValue(row, out var pkValues)) return;

        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.DataEditDeleteConfirmTitle,
            Message = UiStrings.DataEditDeleteConfirmMessage,
            ConfirmLabel = UiStrings.DataEditDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        var pk = BuildKeyValuePairs(PrimaryKeyColumns, pkValues);
        try
        {
            await _dataEditor.DeleteRowAsync(TableName, pk).ConfigureAwait(true);
        }
        catch (DataEditException ex)
        {
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }
        catch (InvalidOperationException ex)
        {
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }

        EditableRows.Remove(row);
        _pkSnapshots.Remove(row);
        SelectedRow = null;
        EditStatusMessage = string.Empty;
        OnPropertyChanged(nameof(HasEditStatusMessage));
    }

    /// <summary>
    /// Commits a single-cell change. The view calls this from its CellEditEnding
    /// handler. For a newly-added (not-yet-INSERTed) row the cell is only updated
    /// in memory — the actual INSERT happens later via <see cref="CommitNewRowAsync"/>
    /// once all values are set.
    /// </summary>
    public async Task UpdateCellAsync(object?[] row, int columnIndex, object? newValue)
    {
        if (row is null) return;
        if (columnIndex < 0 || columnIndex >= row.Length) return;
        if (_dataEditor is null) return;

        var oldValue = row[columnIndex];

        // OPTIMISTIC local write — Avalonia rebuilds the DataGridCell's display
        // visual via CellTemplate immediately AFTER CellEditEnding returns. The
        // FuncDataTemplate lambda reads row[columnIndex], so the new value must
        // already be in place by the time the rebuild fires (i.e. before the
        // first await below). On DB failure we revert + force a refresh via
        // ReplaceRowInGrid so the cell snaps back to its original value.
        row[columnIndex] = newValue;

        if (_newRows.Contains(row))
        {
            // Defer the database round-trip until the row is committed as a whole.
            return;
        }

        if (!_pkSnapshots.TryGetValue(row, out var pkValues))
        {
            // No PK snapshot → can't identify the row for UPDATE. Revert.
            row[columnIndex] = oldValue;
            ReplaceRowInGrid(row);
            EditStatusMessage = UiStrings.DataEditNoPrimaryKeyHint;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }

        var columnName = ResolveColumnName(columnIndex);
        if (columnName is null)
        {
            row[columnIndex] = oldValue;
            ReplaceRowInGrid(row);
            return;
        }

        var pk = BuildKeyValuePairs(PrimaryKeyColumns, pkValues);
        try
        {
            await _dataEditor.UpdateCellAsync(TableName, columnName, newValue, pk).ConfigureAwait(true);
        }
        catch (DataEditException ex)
        {
            row[columnIndex] = oldValue;
            ReplaceRowInGrid(row);
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }
        catch (InvalidOperationException ex)
        {
            row[columnIndex] = oldValue;
            ReplaceRowInGrid(row);
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }

        // If the PK column itself was edited, refresh the snapshot — subsequent
        // UPDATEs on the same row need the new key value.
        for (int i = 0; i < PrimaryKeyColumns.Count; i++)
        {
            if (string.Equals(PrimaryKeyColumns[i], columnName, StringComparison.OrdinalIgnoreCase))
            {
                pkValues[i] = newValue;
                break;
            }
        }

        EditStatusMessage = string.Empty;
        OnPropertyChanged(nameof(HasEditStatusMessage));
    }

    // Force the DataGrid to rebuild this row's cells by swapping the reference
    // with a fresh array. ObservableCollection's indexer-set raises a Replace
    // event regardless of reference equality, but DataGrid checks reference
    // identity when deciding whether to refresh the row container — using a
    // CLONE guarantees the rebuild fires. Mappings (PK snapshot + new-row flag)
    // and SelectedRow are migrated to the clone so subsequent edits continue
    // to work.
    private void ReplaceRowInGrid(object?[] row)
    {
        var idx = EditableRows.IndexOf(row);
        if (idx < 0) return;
        var clone = (object?[])row.Clone();

        if (_pkSnapshots.Remove(row, out var snapshot))
        {
            _pkSnapshots[clone] = snapshot;
        }
        if (_newRows.Remove(row))
        {
            _newRows.Add(clone);
        }
        EditableRows[idx] = clone;
        if (ReferenceEquals(SelectedRow, row))
        {
            SelectedRow = clone;
        }
    }

    /// <summary>
    /// Commits a row that was added in-grid via <see cref="AddRowCommand"/>. Called
    /// from the view's RowEditEnding handler. Sends an INSERT including every
    /// column that has a non-null value (NULL columns omitted from the column list).
    /// On success the row is rolled into the normal "edited via UPDATE" path —
    /// its PK snapshot is captured so subsequent cell edits go through UPDATE.
    /// </summary>
    public async Task CommitNewRowAsync(object?[] row)
    {
        if (row is null) return;
        if (_dataEditor is null) return;
        if (!_newRows.Contains(row)) return;
        if (DataResult is not { HasResultSet: true } r) return;

        var values = new List<KeyValuePair<string, object?>>(r.Columns.Count);
        for (int i = 0; i < r.Columns.Count && i < row.Length; i++)
        {
            if (row[i] is not null)
            {
                values.Add(new KeyValuePair<string, object?>(r.Columns[i].Name, row[i]));
            }
        }
        if (values.Count == 0)
        {
            // Empty row — silently drop it from the grid so an Enter on a blank
            // new-row doesn't try to INSERT a useless tuple.
            EditableRows.Remove(row);
            _newRows.Remove(row);
            return;
        }

        try
        {
            await _dataEditor.InsertRowAsync(TableName, values).ConfigureAwait(true);
        }
        catch (DataEditException ex)
        {
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }
        catch (InvalidOperationException ex)
        {
            EditStatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return;
        }

        _newRows.Remove(row);
        if (PrimaryKeyColumns.Count > 0)
        {
            var pkIndices = ResolvePkIndices(ColumnIndex);
            var snapshot = new object?[pkIndices.Count];
            for (int i = 0; i < pkIndices.Count; i++)
            {
                snapshot[i] = row[pkIndices[i]];
            }
            _pkSnapshots[row] = snapshot;
        }
        EditStatusMessage = string.Empty;
        OnPropertyChanged(nameof(HasEditStatusMessage));
    }

    /// <summary>
    /// True when the given row is a not-yet-INSERTed row added via
    /// <see cref="AddRowCommand"/>. Used by the view to decide whether a
    /// RowEditEnding event should fire INSERT (new row) or be ignored
    /// (existing row — UPDATE already fired in CellEditEnding).
    /// </summary>
    public bool IsNewRow(object?[] row) => row is not null && _newRows.Contains(row);

    private string? ResolveColumnName(int index)
    {
        if (DataResult is not { } r) return null;
        if (index < 0 || index >= r.Columns.Count) return null;
        return r.Columns[index].Name;
    }

    internal static IReadOnlyList<KeyValuePair<string, object?>> BuildKeyValuePairs(IReadOnlyList<string> columns, IReadOnlyList<object?> values)
    {
        var list = new List<KeyValuePair<string, object?>>(columns.Count);
        for (int i = 0; i < columns.Count && i < values.Count; i++)
        {
            list.Add(new KeyValuePair<string, object?>(columns[i], values[i]));
        }
        return list;
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Lazy-load entry point. First caller starts the load; subsequent callers
    // (e.g. SelectTab kicking off fire-and-forget AND OnOpenDdlRequested
    // awaiting the result) get back the SAME task and join the running load.
    // This prevents "Connection in use" races when several restored TableDetail
    // tabs would otherwise all kick off LoadAsync concurrently against the
    // single FbConnection.
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        => _loadTask ??= LoadAsync(cancellationToken);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || _ddlReader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        DataError = string.Empty;
        try
        {
            // Sequential by design — FbConnection services one command at a time, so
            // Task.WhenAll across these calls would throw "Connection in use". Each
            // call uses its own short-lived ReadCommitted tx (see reader code).
            //
            // Each step is independently try/caught. A failure in (say) the
            // Constraints query on a particular FB version must not strand the
            // Fields / Indexes / DDL tabs empty too. The first error message
            // wins for the tab-level ErrorMessage (used to surface in Messages
            // and inside the view); the per-step DataError is separate so the
            // Dane tab can show "this query failed" while the rest renders.

            await SafeLoadAsync(
                async () =>
                {
                    var fields = await _reader.GetFieldsAsync(TableName, cancellationToken).ConfigureAwait(true);
                    Fields.Clear();
                    foreach (var f in fields) Fields.Add(f);
                    RefreshPrimaryKeyColumns();
                });

            await SafeLoadAsync(
                async () =>
                {
                    var constraints = await _reader.GetConstraintsAsync(TableName, cancellationToken).ConfigureAwait(true);
                    Constraints.Clear();
                    foreach (var c in constraints) Constraints.Add(c);
                });

            await SafeLoadAsync(
                async () =>
                {
                    var indexes = await _reader.GetIndexesAsync(TableName, cancellationToken).ConfigureAwait(true);
                    Indexes.Clear();
                    foreach (var i in indexes) Indexes.Add(i);
                });

            await SafeLoadAsync(
                async () =>
                {
                    var (dependsOn, dependedOnBy) = await _reader.GetDependenciesAsync(TableName, cancellationToken).ConfigureAwait(true);
                    DependsOn.Clear();
                    foreach (var d in dependsOn) DependsOn.Add(d);
                    DependedOnBy.Clear();
                    foreach (var d in dependedOnBy) DependedOnBy.Add(d);

                    DependsOnTree.Clear();
                    foreach (var g in BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                    DependedOnByTree.Clear();
                    foreach (var g in BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
                });

            await SafeLoadAsync(
                async () =>
                {
                    var ddl = await _ddlReader.FetchDdlAsync(
                        new MetadataObject(TableName, MetadataObjectKind.Table),
                        cancellationToken).ConfigureAwait(true);
                    DdlText = ddl;
                });

            await SafeLoadAsync(
                async () =>
                {
                    var description = await _reader.GetDescriptionAsync(TableName, cancellationToken).ConfigureAwait(true);
                    Description = description;
                    DescriptionLoaded = true;
                });

            // Domain list for the inline Domain ComboBox in the Pola grid. Wraps
            // a separate reader call — if it throws (FB2.5 etc.) the inline
            // Domain combo stays empty but the rest of the tab still renders.
            // Fetched via _metadataReader because that's where ListDomainsAsync
            // lives; injected from the owner.
            if (_metadataReader is not null)
            {
                await SafeLoadAsync(
                    async () =>
                    {
                        var domains = await _metadataReader.ListDomainsAsync(cancellationToken).ConfigureAwait(true);
                        AvailableDomains.Clear();
                        foreach (var d in domains) AvailableDomains.Add(d);
                    });
            }

            // Data preview gets its own visible error slot (DataError → shown
            // on the Dane tab); other tabs render normally even when this fails
            // (large tables, permission denied, dialect mismatch on quoted IDs).
            try
            {
                var preview = await _reader.GetDataPreviewAsync(TableName, CurrentPage, PageSize, cancellationToken).ConfigureAwait(true);
                DataResult = preview;
                DataResultVersionTag = System.Guid.NewGuid().ToString("N");
            }
            catch (MetadataReadException ex)
            {
                DataResult = null;
                DataError = ex.Message;
                DataResultVersionTag = System.Guid.NewGuid().ToString("N");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Header-click sort cycle: unsorted → asc on X → desc on X → unsorted.
    // Clicking a different column resets to asc on the new column. Each call
    // re-fetches the data preview with the appropriate ORDER BY, so the rows
    // come back already sorted by the server (no in-memory sort).
    public async Task ApplyColumnSortAsync(string columnName, CancellationToken cancellationToken = default)
    {
        // State machine applies regardless of reader presence — keeps the VM
        // testable without a live FB. ReloadDataPreviewAsync silently no-ops
        // when there's no reader, so production calls re-fetch and test calls
        // simply pin the state cycle.
        if (string.IsNullOrEmpty(columnName)) return;

        // Sort changes row positions — invalidate row-count knowledge and
        // jump back to page 1 so the user lands at the new ordering's start.
        LastKnownRowCount = null;
        CurrentPage = 1;

        if (string.Equals(SortColumn, columnName, StringComparison.Ordinal))
        {
            if (!SortDescending)
            {
                SortDescending = true;
            }
            else
            {
                SortColumn = null;
                SortDescending = false;
            }
        }
        else
        {
            SortColumn = columnName;
            SortDescending = false;
        }

        await ReloadDataPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    internal async Task ReloadDataPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null) return;

        // Synchronize with the lazy initial load — refreshing data before Fields
        // has finished loading leaves PrimaryKeyColumns empty and the edit hint
        // would falsely say "Table has no primary key". EnsureLoadedAsync is
        // idempotent: returns instantly when LoadAsync has already completed.
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);

        string? orderBy = null;
        if (!string.IsNullOrEmpty(SortColumn))
        {
            var escaped = SortColumn.Replace("\"", "\"\"");
            orderBy = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "\"{0}\" {1}",
                escaped,
                SortDescending ? "DESC" : "ASC");
        }

        try
        {
            var preview = await _reader.GetDataPreviewAsync(TableName, CurrentPage, PageSize, orderBy, cancellationToken).ConfigureAwait(true);
            DataResult = preview;
            DataError = string.Empty;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
        }
        catch (MetadataReadException ex)
        {
            DataResult = null;
            DataError = ex.Message;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
        }
    }

    // ─── Pagination commands ──────────────────────────────────────────────
    //
    // CanExecute for these is computed against the current HasPrev/Next state.
    // GoToLastPage probes COUNT(*) via the reader's bounded row-count query
    // before navigating; the others are pure CurrentPage assignment + reload.

    public bool CanGoToFirstPage => HasPreviousPage;
    public bool CanGoToPreviousPage => HasPreviousPage;
    public bool CanGoToNextPage => HasNextPage;
    public bool CanGoToLastPage => _reader is not null && (HasNextPage || LastKnownRowCount is null);

    [RelayCommand(CanExecute = nameof(CanGoToFirstPage))]
    private async Task GoToFirstPageAsync()
    {
        if (CurrentPage == 1) return;
        CurrentPage = 1;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task GoToPreviousPageAsync()
    {
        if (CurrentPage <= 1) return;
        CurrentPage--;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task GoToNextPageAsync()
    {
        if (!HasNextPage) return;
        CurrentPage++;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    // GoToLast probes the row count (bounded by RowCountCap) and jumps to the
    // last page. When the probe hits the cap we honestly don't know the true
    // last page — we navigate to the last *known* page (ceil(cap / pageSize))
    // and leave HasNextPage governed by the partial-page heuristic, so the
    // user can still page forward beyond the cap one page at a time.
    [RelayCommand(CanExecute = nameof(CanGoToLastPage))]
    private async Task GoToLastPageAsync()
    {
        if (_reader is null) return;
        try
        {
            var count = await _reader.GetRowCountAsync(TableName, RowCountCap).ConfigureAwait(true);
            LastKnownRowCount = count;
            if (count <= 0)
            {
                CurrentPage = 1;
            }
            else
            {
                var lastPage = (count + PageSize - 1) / PageSize;
                if (lastPage < 1) lastPage = 1;
                CurrentPage = lastPage;
            }
        }
        catch (MetadataReadException ex)
        {
            DataError = ex.Message;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
            return;
        }
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    // Runs one load step and traps MetadataReadException so it doesn't poison
    // the rest of the chain. The first error wins for the tab-level ErrorMessage.
    private async Task SafeLoadAsync(System.Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(true);
        }
        catch (MetadataReadException ex)
        {
            if (string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    public void Populate(IReadOnlyList<FieldInfo> fields,
                        IReadOnlyList<IndexInfo> indexes,
                        string ddl)
    {
        Fields.Clear();
        foreach (var f in fields) Fields.Add(f);
        Indexes.Clear();
        foreach (var i in indexes) Indexes.Add(i);
        DdlText = ddl;
    }

    // ─── Structural editing (Add / Drop / Move fields + Compile) ───────────
    //
    // PendingChanges is the in-memory queue of DDL statements collected from
    // the Pola toolbar. Nothing leaves the VM until the user presses ⚡ Compile,
    // which feeds the statements one-by-one through FirebirdDdlExecutor. On
    // success the list is cleared and the tab fully refreshed; on failure the
    // first server error is surfaced as ErrorMessage and the remaining
    // statements stay queued so the user can fix and retry.

    public ObservableCollection<PendingDdlChange> PendingChanges { get; }

    public bool HasPendingChanges => PendingChanges.Count > 0;
    public bool CanCompile => _ddlExecutor is not null && HasPendingChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropFieldCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditFieldCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFieldUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFieldDownCommand))]
    private FieldInfo? _selectedField;

    /// <summary>
    /// Grid-side selection — the wrapper. Drives the same Drop / Move commands
    /// (those still take a <see cref="FieldInfo"/>) by mirroring into
    /// <see cref="SelectedField"/> on every change.
    /// </summary>
    [ObservableProperty]
    private FieldRowViewModel? _selectedFieldRow;

    partial void OnSelectedFieldRowChanged(FieldRowViewModel? value)
    {
        SelectedField = value?.Original;
    }

    /// <summary>
    /// When true the Pola DataGrid accepts inline edits (Name, Type, Domain,
    /// NotNull, Default, Description); when false the grid is read-only.
    /// Default off for existing tables — the user explicitly toggles it via
    /// the main-toolbar grid-pencil button. The CreateTable workspace tab
    /// runs its own grid always-on (no shared state).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFieldsReadOnly))]
    private bool _isFieldEditMode;

    public bool IsFieldsReadOnly => !IsFieldEditMode;

    [RelayCommand]
    private void ToggleFieldEditMode() => IsFieldEditMode = !IsFieldEditMode;

    public bool CanAddField => _ddlExecutor is not null;
    public bool CanEditField => _ddlExecutor is not null && SelectedField is not null;
    public bool CanCreateForeignKey => _ddlExecutor is not null;

    /// <summary>
    /// View-side handler returns the populated <see cref="FieldDefinition"/> from
    /// the modal AddFieldDialog, or null on Cancel. Async because the dialog
    /// fetches the available domains + generators before opening.
    /// </summary>
    public event System.Func<Task<FieldDefinition?>>? AddFieldRequested;

    /// <summary>
    /// Edit-mode counterpart of <see cref="AddFieldRequested"/>. View opens
    /// the same AddFieldDialog seeded from the selected <see cref="FieldInfo"/>
    /// + canRename flag (so the dialog can disable the name TextBox + show a
    /// "rename blocked — has dependencies" hint when needed). Returns the
    /// dialog's target <see cref="FieldDefinition"/> on OK, or null on Cancel
    /// / no-change.
    /// </summary>
    public event System.Func<FieldInfo, bool, Task<FieldDefinition?>>? EditFieldRequested;

    /// <summary>
    /// Opens the Foreign Key wizard (Session 3). View handler resolves the
    /// current source-table state (Fields, available tables, on-demand
    /// referenced-table column lookup, on-demand referenced-table PK lookup)
    /// and shows the dialog. Returns the populated
    /// <see cref="ForeignKeySpec"/> on OK, or null on Cancel — symmetric to
    /// <see cref="AddFieldRequested"/> / <see cref="EditFieldRequested"/>.
    /// </summary>
    public event System.Func<Task<ForeignKeySpec?>>? CreateForeignKeyRequested;

    [RelayCommand(CanExecute = nameof(CanAddField))]
    private async Task AddFieldAsync()
    {
        if (AddFieldRequested is null) return;
        var def = await AddFieldRequested().ConfigureAwait(true);
        if (def is null) return;
        await ExecuteAddFieldAsync(def).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the AddFieldDialog in edit mode seeded from the current
    /// <see cref="SelectedField"/>. On OK, executes the diff via
    /// <see cref="ExecuteEditFieldAsync"/>. On Cancel / no-change → no-op
    /// (no DDL emitted, table NOT marked modified — per session spec).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditField))]
    private async Task EditFieldAsync()
    {
        if (SelectedField is not { } field) return;
        if (EditFieldRequested is null) return;
        var canRename = CanRenameField(field.Name);
        var target = await EditFieldRequested(field, canRename).ConfigureAwait(true);
        if (target is null) return;
        await ExecuteEditFieldAsync(field, target).ConfigureAwait(true);
    }

    /// <summary>
    /// Diffs <paramref name="original"/> vs <paramref name="target"/> via
    /// <see cref="DdlGenerator.BuildAlterStatements"/> and executes the
    /// resulting ALTERs sequentially in the user's working transaction.
    /// Empty diff = no-op. First failure halts and leaves
    /// <see cref="ErrorMessage"/> set; the user can Rollback to undo any
    /// partially-applied changes. Symmetric to
    /// <see cref="ExecuteAddFieldAsync"/> / <see cref="ExecuteDropFieldAsync"/>.
    /// </summary>
    public async Task ExecuteEditFieldAsync(FieldInfo original, FieldDefinition target)
    {
        if (original is null) return;
        if (target is null) return;
        if (_ddlExecutor is null) return;

        var canRename = CanRenameField(original.Name);
        var alterTarget = new AlterFieldTarget
        {
            Name = target.Name,
            // FormatTypeOrDomain handles Domain-vs-BasicType + Size + Precision/Scale
            // + BlobSubType. Same string the AddField flow would emit for an ADD —
            // so a Type/Domain ALTER is a one-string substitution.
            TypeClause = DdlGenerator.FormatTypeOrDomain(target),
            NotNull = target.NotNull,
            DefaultValue = target.DefaultValue,
            Description = target.Description,
        };

        var statements = DdlGenerator.BuildAlterStatements(TableName, original, alterTarget, canRename);
        if (statements.Count == 0)
        {
            // No-op: user clicked OK without changing anything (or only changed
            // properties we don't ALTER inline — Computed / Check / AutoIncrement
            // / PrimaryKey). Don't refresh; don't touch ErrorMessage.
            return;
        }

        ErrorMessage = null;
        foreach (var change in statements)
        {
            try
            {
                await _ddlExecutor.ExecuteAsync(change.Sql).ConfigureAwait(true);
            }
            catch (DdlExecutionException ex)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
                return;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
                return;
            }
        }
        await RefreshStructureAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the Foreign Key wizard. On OK, executes the resulting spec via
    /// <see cref="ExecuteCreateForeignKeyAsync"/>. Cancel = no-op.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateForeignKey))]
    private async Task CreateForeignKeyAsync()
    {
        if (CreateForeignKeyRequested is null) return;
        var spec = await CreateForeignKeyRequested().ConfigureAwait(true);
        if (spec is null) return;
        await ExecuteCreateForeignKeyAsync(spec).ConfigureAwait(true);
    }

    /// <summary>
    /// Executes the ADD CONSTRAINT … FOREIGN KEY DDL emitted by
    /// <see cref="DdlGenerator.BuildAddForeignKey"/>. Runs in the user's
    /// working transaction (auto-begin via DdlExecutor — Rollback undoes).
    /// On success: RefreshStructureAsync re-fetches constraints + DDL, then
    /// jumps the inner UI to Constraints → Foreign Keys so the user sees
    /// the new entry. Errors land in <see cref="ErrorMessage"/>.
    /// </summary>
    public async Task ExecuteCreateForeignKeyAsync(ForeignKeySpec spec)
    {
        if (spec is null) return;
        if (_ddlExecutor is null) return;

        ErrorMessage = null;
        string sql;
        try
        {
            sql = DdlGenerator.BuildAddForeignKey(TableName, spec);
        }
        catch (System.ArgumentException ex)
        {
            // Validation gap that survived the dialog (defensive — dialog's
            // IsValid + BuildAddForeignKey's own throw catch this earlier).
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.ForeignKeyExecuteFailedFormat, ex.Message);
            return;
        }

        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.ForeignKeyExecuteFailedFormat, ex.Message);
            return;
        }
        catch (System.InvalidOperationException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.ForeignKeyExecuteFailedFormat, ex.Message);
            return;
        }

        // Refresh: re-fetch fields/constraints/indexes/ddl from the live
        // catalog. The new FK shows up in Constraints; the snapshot
        // mechanism preserves user's selection / sort / page.
        await RefreshStructureAsync().ConfigureAwait(true);

        // Override the snapshot-restored sub-tab — spec says "switch to
        // Ograniczenia → Foreign Keys after a successful FK". Set both the
        // outer (Ograniczenia) and inner (FK) indices.
        ActiveSubTabIndex = ConstraintsSubTabIndex;
        ConstraintsActiveSubTabIndex = ConstraintsForeignKeysIndex;
    }

    /// <summary>
    /// Executes the ALTER TABLE … ADD statement immediately (in the user's
    /// working transaction — DDL Executor auto-begins one if needed) and reloads
    /// the table detail so the new column appears in the Pola grid. Errors
    /// surface as <see cref="ErrorMessage"/>; the Commit / Rollback toolbar
    /// buttons remain the user's escape hatch.
    /// </summary>
    public async Task ExecuteAddFieldAsync(FieldDefinition definition)
    {
        if (definition is null) return;
        if (_ddlExecutor is null) return;

        ErrorMessage = null;
        var sql = DdlGenerator.BuildAddField(TableName, definition);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
            return;
        }
        await RefreshStructureAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Executes the ALTER TABLE … DROP statement immediately (in tx) and reloads
    /// the table detail. Symmetric to <see cref="ExecuteAddFieldAsync"/>.
    /// </summary>
    public async Task ExecuteDropFieldAsync(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return;
        if (_ddlExecutor is null) return;

        ErrorMessage = null;
        var sql = DdlGenerator.BuildDropField(TableName, fieldName);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
            return;
        }
        await RefreshStructureAsync().ConfigureAwait(true);
    }

    // Force the next EnsureLoadedAsync to re-fetch fields/constraints/indexes/DDL
    // from the live catalog. Called after Add / Drop / Compile so the Pola grid
    // reflects the new structure immediately. Public surface for the view to
    // call after an external structural change (rollback, manual refresh).
    // Snapshots active sub-tab, selected-field name, sort column/direction,
    // current page, and selected-row PK values before discarding _loadTask;
    // restores them after the re-fetch completes so the user lands on the
    // same row they left.
    public async Task RefreshStructureAsync(System.Threading.CancellationToken ct = default)
    {
        // Snapshot — capture by VALUE before we drop _loadTask, because the
        // collections will be cleared during LoadAsync's Fields/Constraints/Indexes
        // re-population steps.
        var snap = new StructureSnapshot
        {
            ActiveSubTabIndex = ActiveSubTabIndex,
            SelectedFieldName = SelectedField?.Name,
            SortColumn = SortColumn,
            SortDescending = SortDescending,
            CurrentPage = CurrentPage,
            PageSize = PageSize,
            SelectedRowPk = _pkSnapshots.TryGetValue(SelectedRow ?? System.Array.Empty<object?>(), out var pk)
                ? pk
                : null,
        };

        _loadTask = null;
        await EnsureLoadedAsync(ct).ConfigureAwait(true);
        // Clear the pending DDL queue — any user-pending edits no longer
        // describe the current schema (rolled back, compiled, or refreshed
        // out from under them).
        PendingChanges.Clear();
        RestoreStructureSnapshot(snap);

        // Notify the view (column-width preservation lives there).
        StructureRefreshed?.Invoke(this, System.EventArgs.Empty);
    }

    /// <summary>
    /// Fired immediately after a structural refresh completes — the view uses
    /// this hook to restore column widths it snapshotted before the reload.
    /// </summary>
    public event System.EventHandler? StructureRefreshed;

    private void RestoreStructureSnapshot(StructureSnapshot snap)
    {
        ActiveSubTabIndex = snap.ActiveSubTabIndex;

        if (!string.IsNullOrEmpty(snap.SelectedFieldName))
        {
            foreach (var f in Fields)
            {
                if (string.Equals(f.Name, snap.SelectedFieldName, System.StringComparison.OrdinalIgnoreCase))
                {
                    SelectedField = f;
                    // Mirror to the row VM as well — the grid's SelectedItem
                    // binds to SelectedFieldRow, not SelectedField directly.
                    foreach (var row in EditableFields)
                    {
                        if (ReferenceEquals(row.Original, f))
                        {
                            SelectedFieldRow = row;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        // Page + sort are restored "best effort" — if the data preview
        // re-fetch is not yet triggered, ReloadDataPreviewAsync sees them
        // already set and respects them. (LoadAsync's data-preview branch
        // uses CurrentPage/PageSize as-is.)
        if (snap.SortColumn is not null) SortColumn = snap.SortColumn;
        SortDescending = snap.SortDescending;
        // CurrentPage/PageSize already had the live values; if the user
        // dropped a column the row count may shrink, but we don't auto-clamp
        // here — ReloadDataPreviewAsync would land an empty page and the
        // pagination buttons remain usable.
    }

    private sealed class StructureSnapshot
    {
        public int ActiveSubTabIndex;
        public string? SelectedFieldName;
        public string? SortColumn;
        public bool SortDescending;
        public int CurrentPage;
        public int PageSize;
        public object?[]? SelectedRowPk;
    }

    /// <summary>
    /// Called by <see cref="MainWindowViewModel"/> after the user fires
    /// Rollback (or any other event that may have changed the underlying
    /// schema without our knowledge — Commit too, for symmetry). Discards
    /// any pending DDL edits the user had queued and re-fetches the table
    /// detail from the live catalog. Fire-and-forget — errors surface as
    /// <see cref="ErrorMessage"/> through the standard LoadAsync path.
    /// </summary>
    public Task RefreshAfterTransactionAsync(System.Threading.CancellationToken ct = default)
    {
        // Identical mechanics to RefreshStructureAsync — kept separate so the
        // call site reads clearly at the owner level (search for
        // RefreshAfterTransactionAsync to find every transaction-driven
        // refresh).
        return RefreshStructureAsync(ct);
    }
    public bool CanDropField => _ddlExecutor is not null && SelectedField is not null;
    public bool CanMoveFieldUp => _ddlExecutor is not null && SelectedField is not null && Fields.IndexOf(SelectedField) > 0;
    public bool CanMoveFieldDown => _ddlExecutor is not null && SelectedField is not null && Fields.IndexOf(SelectedField) >= 0 && Fields.IndexOf(SelectedField) < Fields.Count - 1;

    /// <summary>
    /// Queues an ADD-FIELD change. Called by the view after the AddFieldDialog
    /// closes with a valid <see cref="FieldDefinition"/>. Pure VM call — no
    /// I/O — so unit tests drive it directly.
    /// </summary>
    public void AddPendingAddField(FieldDefinition definition)
    {
        if (definition is null) return;
        var sql = DdlGenerator.BuildAddField(TableName, definition);
        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.AddField,
            Description = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditDescriptionAddFormat, definition.Name),
            Sql = sql,
        });
    }

    [RelayCommand(CanExecute = nameof(CanDropField))]
    private async Task DropFieldAsync()
    {
        if (SelectedField is not { } field) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.FieldEditDropConfirmTitle,
            Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditDropConfirmFormat, field.Name),
            ConfirmLabel = UiStrings.FieldEditDropConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        // Immediate execute in the user's working transaction — symmetric to
        // Add Field. Rollback from the main toolbar undoes the drop.
        await ExecuteDropFieldAsync(field.Name).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanMoveFieldUp))]
    private async Task MoveFieldUpAsync()
    {
        if (SelectedField is not { } field) return;
        var index = Fields.IndexOf(field);
        if (index <= 0) return;
        // Firebird positions are 1-based — moving up means newPosition = currentIndex
        // (which equals (index+1) - 1).
        var newPos = index; // current 1-based pos is index+1; we want index → 1-based pos = index
        await ExecuteMoveAsync(field.Name, newPos).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanMoveFieldDown))]
    private async Task MoveFieldDownAsync()
    {
        if (SelectedField is not { } field) return;
        var index = Fields.IndexOf(field);
        if (index < 0 || index >= Fields.Count - 1) return;
        // Index+1 in 0-based → 1-based pos = index+2.
        var newPos = index + 2;
        await ExecuteMoveAsync(field.Name, newPos).ConfigureAwait(true);
    }

    /// <summary>
    /// Executes an ALTER TABLE … POSITION immediately in the user's working
    /// transaction (auto-begin) and reloads — symmetric to Add/Drop. Selected
    /// field is preserved by name through <see cref="RefreshStructureAsync"/>.
    /// </summary>
    public async Task ExecuteMoveAsync(string fieldName, int oneBasedPosition)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return;
        if (_ddlExecutor is null) return;
        ErrorMessage = null;
        var sql = DdlGenerator.BuildMoveField(TableName, fieldName, oneBasedPosition);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
            return;
        }
        await RefreshStructureAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Test-only helper retained from the previous queue-and-compile model.
    /// Production code paths use <see cref="ExecuteMoveAsync"/> directly.
    /// </summary>
    public void AddMovePending(string fieldName, int oneBasedPosition)
    {
        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.MoveField,
            Description = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditDescriptionMoveFormat, fieldName, oneBasedPosition),
            Sql = DdlGenerator.BuildMoveField(TableName, fieldName, oneBasedPosition),
        });
    }

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private async Task CompileAsync()
    {
        if (_ddlExecutor is null) return;
        if (PendingChanges.Count == 0) return;

        ErrorMessage = null;
        // Drain the pending list as we go — partial success leaves the still-pending
        // statements in place so the user can fix and retry. We snapshot the list
        // first so removing-from-front doesn't shift indices under the loop.
        var snapshot = PendingChanges.ToList();
        foreach (var change in snapshot)
        {
            try
            {
                await _ddlExecutor.ExecuteAsync(change.Sql).ConfigureAwait(true);
            }
            catch (DdlExecutionException ex)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
                return;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditCompileFailedFormat, ex.Message);
                return;
            }
            PendingChanges.Remove(change);
        }

        // Full success — refresh the live structure so Fields / Constraints /
        // Indexes / DDL all reflect the new shape. Goes through the central
        // RefreshStructureAsync helper so the user's sub-tab, selected field,
        // sort state, page, and column widths all survive the rebuild.
        await RefreshStructureAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// True while the user is editing structure (any pending changes queued).
    /// Drives the DDL sub-tab's "current + pending" rendering.
    /// </summary>
    public string DdlWithPendingPreview
    {
        get
        {
            if (PendingChanges.Count == 0) return DdlText;
            var sb = new System.Text.StringBuilder();
            sb.Append(DdlText);
            if (!DdlText.EndsWith('\n')) sb.Append('\n');
            sb.Append('\n').Append(UiStrings.FieldEditPendingHeader).Append('\n');
            foreach (var change in PendingChanges)
            {
                sb.Append(change.Sql);
                if (!change.Sql.EndsWith(';')) sb.Append(';');
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    private void OnPendingChangesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanCompile));
        OnPropertyChanged(nameof(DdlWithPendingPreview));
        CompileCommand.NotifyCanExecuteChanged();
    }

    partial void OnDdlTextChanged(string value)
    {
        // Live DDL preview tab reads DdlWithPendingPreview; keep it in sync when
        // the underlying DDL refreshes.
        OnPropertyChanged(nameof(DdlWithPendingPreview));
    }

    partial void OnSelectedFieldChanged(FieldInfo? value)
    {
        // Move enablement depends on the selection's *index* — re-evaluate Up / Down
        // explicitly on each selection change.
        OnPropertyChanged(nameof(CanMoveFieldUp));
        OnPropertyChanged(nameof(CanMoveFieldDown));
        OnPropertyChanged(nameof(CanDropField));
        MoveFieldUpCommand.NotifyCanExecuteChanged();
        MoveFieldDownCommand.NotifyCanExecuteChanged();
        DropFieldCommand.NotifyCanExecuteChanged();
        // Per-field dependency panel reacts to selection changes.
        RebuildFieldDependencies();
    }

    private void OnDependedOnByCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // DependedOnBy gets cleared then re-populated during every
        // LoadAsync / RefreshStructureAsync run — each Add fires once. We
        // rebuild on every notification; for typical schemas (≤20 deps per
        // table) this is cheaper than introducing a debounce flag.
        RebuildFieldDependencies();
    }

    /// <summary>
    /// Recomputes the per-field dependencies panel content. Filter:
    /// <see cref="DependedOnBy"/> rows whose <see cref="DependencyInfo.FieldName"/>
    /// matches the currently selected field (case-insensitive — Firebird
    /// stores names uppercase but user input may not be). Dedup by
    /// (ObjectType, ObjectName) — the same trigger may touch several fields
    /// and would otherwise show up multiple times for the same selection.
    /// </summary>
    private void RebuildFieldDependencies()
    {
        FieldDependencies.Clear();
        var fieldName = SelectedField?.Name;
        if (!string.IsNullOrEmpty(fieldName))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in DependedOnBy)
            {
                if (!string.Equals(dep.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = $"{dep.ObjectType}|{dep.ObjectName}";
                if (!seen.Add(key)) continue;
                FieldDependencies.Add(new FieldDependencyItem(dep, this));
            }
        }
        OnPropertyChanged(nameof(HasFieldDependencies));
        OnPropertyChanged(nameof(HasFieldSelectionForDependencies));
        OnPropertyChanged(nameof(ShowFieldDependenciesEmpty));
        OnPropertyChanged(nameof(ShowFieldDependenciesNoSelection));
    }
}
