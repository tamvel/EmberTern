using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class TableDetailTabViewModel : ViewModelBase
{
    // Data preview is capped — we never want to pull a whole table into the
    // grid from a metadata-browsing tab. 200 is enough to spot-check shape.
    public const int DataPreviewRowLimit = 200;

    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;

    // Tracks the in-flight (or completed) load. EnsureLoadedAsync returns this
    // task — second-and-subsequent callers get the same Task back and join the
    // already-running load instead of kicking off a duplicate, which would
    // collide on the single-statement FbConnection. Reset implicitly on
    // disconnect/reconnect because LoadWorkspaceFor builds a fresh VM instance.
    private Task? _loadTask;

    public TableDetailTabViewModel(string tableName)
        : this(tableName, null, null)
    {
    }

    public TableDetailTabViewModel(string tableName, FirebirdTableDetailReader? reader, FirebirdDdlReader? ddlReader)
    {
        TableName = tableName;
        _reader = reader;
        _ddlReader = ddlReader;
        Fields = new ObservableCollection<FieldInfo>();
        Indexes = new ObservableCollection<IndexInfo>();
        Constraints = new ObservableCollection<ConstraintInfo>();
        DependsOn = new ObservableCollection<DependencyInfo>();
        DependedOnBy = new ObservableCollection<DependencyInfo>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
        Constraints.CollectionChanged += OnConstraintsCollectionChanged;
    }

    public string TableName { get; }

    public ObservableCollection<FieldInfo> Fields { get; }
    public ObservableCollection<IndexInfo> Indexes { get; }
    public ObservableCollection<ConstraintInfo> Constraints { get; }
    public ObservableCollection<DependencyInfo> DependsOn { get; }
    public ObservableCollection<DependencyInfo> DependedOnBy { get; }
    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

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
    private int _activeSubTabIndex;

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

    public string DataPreviewHint => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        UiStrings.TableDetailDataPreviewHintFormat,
        DataPreviewRowLimit);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataResult))]
    [NotifyPropertyChangedFor(nameof(ShowDataError))]
    private QueryResult? _dataResult;

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

            // Data preview gets its own visible error slot (DataError → shown
            // on the Dane tab); other tabs render normally even when this fails
            // (large tables, permission denied, dialect mismatch on quoted IDs).
            try
            {
                var preview = await _reader.GetDataPreviewAsync(TableName, DataPreviewRowLimit, cancellationToken).ConfigureAwait(true);
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
}
