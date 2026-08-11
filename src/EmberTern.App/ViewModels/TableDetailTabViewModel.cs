using System.Globalization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Export;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class TableDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, ISavableObjectEditor, IDependencyNavigator
{
    // Data preview is capped — we never want to pull a whole table into the
    // grid from a metadata-browsing tab.
    //
    // ⚠ The number itself lives in Core now (Settings Center etap 6 / §7.7): it is a user setting, and it was
    // hard-coded HERE and again in ViewDetailTabViewModel, which is exactly the pair that drifts. Both read
    // PreferenceOptions.DataPageSize so the shipped default and its ceiling exist once — and a tab built with a
    // page size supplied by MainWindowViewModel gets the user's value instead.
    public static readonly int DataPreviewRowLimit = PreferenceOptions.DataPageSize.Default;

    // Hard upper bound on PageSize. The user can bump PageSize up to this; the
    // grid stays usable but each fetch grows linearly.
    public static readonly int MaxPageSize = PreferenceOptions.DataPageSize.Maximum;

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

    // Index of the "Indeksy" sub-tab. Index Management's post-add UX jumps here
    // and selects the new index.
    public const int IndexesSubTabIndex = 2;

    // Inner Constraints TabControl tab indices: Primary Key (0) /
    // Foreign Keys (1) / Check (2) / Unique (3). Must match the TabItem
    // order in TableDetailTabView.axaml's nested Constraints TabControl.
    public const int ConstraintsPrimaryKeyIndex = 0;
    public const int ConstraintsForeignKeysIndex = 1;
    public const int ConstraintsCheckIndex = 2;
    public const int ConstraintsUniqueIndex = 3;

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
        // "Recompute all statistics" is enabled only when the table has indexes —
        // re-evaluate its CanExecute when the loaded index list changes.
        Indexes.CollectionChanged += OnIndexesCollectionChanged;

        // Shared filter panel + aggregation bar for the Dane grid. Server-paged →
        // filter + aggregates are pushed to SQL (WHERE / SELECT agg over the FULL set),
        // never over the current page. Identical UX to the materialized grids.
        DataFilterPanel = new FilterPanelViewModel { ApplyRequested = ApplyDataFilterAsync };
        DataAggregationBar = new AggregationBarViewModel(ComputeDataAggregateAsync);
    }

    private void OnIndexesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RecomputeAllIndexStatisticsCommand.NotifyCanExecuteChanged();

    // Mirror Fields → EditableFields whenever Fields changes (load + post-Compile
    // re-load both clear-and-rebuild Fields). EditableFields is what the Pola
    // grid binds to; FieldRowViewModel forwards read-only props and surfaces
    // owner-side AvailableDomains / BasicTypes / CanEditStructure for the in-cell editors.
    // True while LoadStructureAsync bulk-replaces Fields (Clear + N×Add). Without
    // this guard, OnFieldsCollectionChanged fired on EACH of those N+1 mutations
    // and rebuilt the ENTIRE EditableFields collection every time — O(N²) row-VM
    // allocations per load, every one leaking an owner-event subscription. With
    // the guard the rebuild happens exactly once, at the end of the bulk update.
    private bool _bulkFieldsLoading;

    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_bulkFieldsLoading) return;
        RebuildEditableFields();
    }

    // Detaches the outgoing row VMs (unhooks their owner-event subscriptions —
    // the leak fix) before clearing, then rebuilds one wrapper per field.
    private void RebuildEditableFields()
    {
        foreach (var row in EditableFields) row.Detach();
        EditableFields.Clear();
        // Row VMs are discarded → their per-row inline-edit tracking is stale.
        _inlineRowEdits.Clear();
        foreach (var f in Fields) EditableFields.Add(new FieldRowViewModel(f, this));
    }

    // Bulk-replaces Fields and rebuilds EditableFields exactly once. Used by the
    // load chain instead of an open-coded Clear + foreach Add (which would fire
    // OnFieldsCollectionChanged N+1 times).
    private void ReplaceFields(IReadOnlyList<FieldInfo> fields)
    {
        _bulkFieldsLoading = true;
        try
        {
            Fields.Clear();
            foreach (var f in fields) Fields.Add(f);
        }
        finally
        {
            _bulkFieldsLoading = false;
        }
        RebuildEditableFields();
    }

    /// <summary>
    /// Fires when the VM wants the user to confirm a destructive operation
    /// (e.g. row delete). The owner translates this to a ConfirmDialog in the
    /// usual way. When unset, all confirmations are auto-accepted (test mode).
    /// </summary>
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;

    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

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

    /// <summary>Live table list for the merged Domena/Kolumna picker's Table-column tab
    /// (TYPE OF COLUMN). Populated best-effort by the owner after the schema loads.</summary>
    public ObservableCollection<string> AvailableTables { get; } = new();

    /// <summary>Lazy column loader for the Table-column tab (set by the owner).</summary>
    public IColumnsLoader? ColumnsLoader { get; set; }

    /// <summary>Owner injects the live table list for the Table-column picker tab.</summary>
    public void SetAvailableTables(IEnumerable<string> tables)
    {
        AvailableTables.Clear();
        foreach (var t in tables) AvailableTables.Add(t);
    }

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
    // Inline edits tracked per row so a repeated EnqueueRowEdits call for the SAME row
    // REPLACES its prior statements instead of duplicating them. Reference-keyed.
    private readonly Dictionary<FieldRowViewModel, List<PendingDdlChange>> _inlineRowEdits = new();
    // Re-entrancy guard: the dependency-blocked revert below writes editable
    // properties, which would re-trigger OnInlineFieldEdited → EnqueueRowEdits.
    private bool _enqueuingRowEdits;

    /// <summary>
    /// Called by <see cref="FieldRowViewModel"/> whenever an editable cell changes.
    /// This is the path that catches the Type and Domain ComboBoxes: they live in
    /// always-visible (IsReadOnly) template columns — the gotcha #56 focus-race
    /// workaround — so the DataGrid's RowEditEnding never fires for a type/domain
    /// change, and without this hook EnqueueRowEdits would be missed and Compile
    /// would stay greyed after a primary design action.
    /// </summary>
    internal void OnInlineFieldEdited(FieldRowViewModel row) => EnqueueRowEdits(row);

    public void EnqueueRowEdits(FieldRowViewModel row)
    {
        if (row is null || _enqueuingRowEdits) return;
        // Added/Dropped rows carry their own ADD/DROP statement; inline cell edits on
        // them don't generate ALTERs here.
        if (row.PendingKind is PendingChangeKind.Added or PendingChangeKind.Dropped) return;

        _enqueuingRowEdits = true;
        try
        {
            // Idempotent per row: drop edits previously queued for THIS row before
            // recomputing, so repeated calls (the per-property auto-enqueue +
            // RowEditEnding, successive edits, or an edit-then-revert) reflect the
            // row's CURRENT total diff instead of accumulating duplicates.
            if (_inlineRowEdits.TryGetValue(row, out var prior))
            {
                foreach (var p in prior) PendingChanges.Remove(p);
                _inlineRowEdits.Remove(row);
            }

            var original = row.Original;
            var canRename = CanRenameField(original.Name);

            // Type clause — set ONLY when the user genuinely changed the type or
            // domain. Domain columns: original.Type is the RESOLVED type while
            // DomainName is the domain. Basic-type columns: compare full type via
            // EffectiveTypeText (catches Size/Scale edits). null ⇒ no type ALTER.
            string? typeClause = null;
            var domainChanged = !string.Equals(
                row.DomainName ?? string.Empty, original.Domain ?? string.Empty,
                System.StringComparison.OrdinalIgnoreCase);
            var typeChanged = !string.Equals(
                row.EffectiveTypeText, original.Type,
                System.StringComparison.OrdinalIgnoreCase);
            if (domainChanged && !string.IsNullOrWhiteSpace(row.DomainName))
            {
                // Generated-DDL identifier style: present the domain UPPERCASE (bare) — §0-safe.
                typeClause = EmberTern.Core.Metadata.DdlGenerator.PresentIdentifier(row.DomainName);
            }
            else if (typeChanged
                     && string.IsNullOrWhiteSpace(row.DomainName)
                     && !string.IsNullOrWhiteSpace(row.EffectiveTypeText))
            {
                typeClause = row.EffectiveTypeText;
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
            if (statements.Count > 0)
            {
                foreach (var s in statements) PendingChanges.Add(s);
                _inlineRowEdits[row] = new List<PendingDdlChange>(statements);
                row.PendingKind = PendingChangeKind.Modified;
            }
            else
            {
                // No diff (clean, or the user reverted to the original) — clear the tint.
                row.PendingKind = PendingChangeKind.None;
            }

            // UX: when the user attempted a rename / type-change but the field has
            // incoming dependencies, BuildAlterStatements silently skipped them.
            // Revert the displayed values + surface the standard "rename blocked" hint.
            if (!canRename)
            {
                bool attemptedRename = !string.Equals(row.Name, original.Name, System.StringComparison.Ordinal);
                bool attemptedTypeChange = typeClause is not null;
                if (attemptedRename) row.Name = original.Name;
                if (attemptedTypeChange) row.RevertTypeToOriginal();
                if (attemptedRename || attemptedTypeChange)
                {
                    ErrorMessage = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        UiStrings.FieldEditRenameBlockedFormat, original.Name);
                }
            }
        }
        finally
        {
            _enqueuingRowEdits = false;
        }
    }
    public ObservableCollection<IndexInfo> Indexes { get; }
    public ObservableCollection<ConstraintInfo> Constraints { get; }

    /// <summary>Every index + constraint name currently on the table — the collision set the
    /// Add-Index / Add-PK / Add-Unique / Add-Check / Add-FK dialogs feed to
    /// <see cref="EmberTern.Core.Metadata.ConstraintNaming.MakeUnique"/> so their default name is
    /// auto-numbered past anything already used (IDX_T → IDX1_T …), instead of colliding.</summary>
    public IReadOnlyList<string> ExistingIndexAndConstraintNames
        => Indexes.Select(i => i.Name)
            .Concat(Constraints.Select(c => c.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

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

    // ⭐⭐ M4.2b: kolejność NIE jest już własną tablicą — jest KANONICZNĄ kolejnością
    // (`MetadataCategoryOrder.All`) zawężoną do kategorii, które mogą być zależnością. Powód jest
    // odbiorczy, nie porządkowy: użytkownik zobaczył, że Trigger, Function, Generator, Domain i Package
    // stoją w innym miejscu niż w drzewie połączenia — wyłącznie dlatego, że każdy mechanizm miał
    // własną listę. ⛔ Nie wracać do literalnej tablicy: dwie listy, które dziś się zgadzają, jutro
    // się rozjadą, i to jest ten sam defekt jeszcze raz.
    //
    // ⚠ Każda kategoria pojawia się jako korzeń również wtedy, gdy jest pusta. `ObjectTypeKey` odpowiada
    // liczbie pojedynczej zwracanej przez `MapObjectType`; `DisplayLabel` to liczba mnoga w nagłówku.
    //
    // ⛔ „UDF" USUNIĘTE (decyzja użytkownika, 2026-08-08): to kategoria HISTORYCZNA, a produkt wspiera
    // Firebird 5, więc nie ma jej w nowym UI. Była pozycją-parytetem z IBExpertem i ZAWSZE PUSTĄ — żaden
    // kod typu zależności jej nie zwracał — czyli wierszem, który nigdy niczego nie pokazał.
    // ⛔ Nie zastępować jej inną kategorią i nie przywracać „dla kompletności listy".
    internal static readonly IReadOnlyList<DependencyCategory> CategoryOrder = BuildCategoryOrder();

    private static IReadOnlyList<DependencyCategory> BuildCategoryOrder()
    {
        var labels = new Dictionary<MetadataObjectKind, (string Key, string Label)>
        {
            [MetadataObjectKind.Table]     = ("Table",     UiStrings.MetadataGroupTables),
            [MetadataObjectKind.View]      = ("View",      UiStrings.MetadataGroupViews),
            [MetadataObjectKind.Procedure] = ("Procedure", UiStrings.MetadataGroupProcedures),
            [MetadataObjectKind.Trigger]   = ("Trigger",   UiStrings.MetadataGroupTriggers),
            [MetadataObjectKind.Function]  = ("Function",  UiStrings.MetadataGroupFunctions),
            [MetadataObjectKind.Generator] = ("Generator", UiStrings.MetadataGroupGenerators),
            [MetadataObjectKind.Domain]    = ("Domain",    UiStrings.MetadataGroupDomains),
            [MetadataObjectKind.Package]   = ("Package",   UiStrings.MetadataGroupPackages),
            [MetadataObjectKind.Exception] = ("Exception", UiStrings.MetadataGroupExceptions),
            [MetadataObjectKind.Index]     = ("Index",     UiStrings.MetadataGroupIndexes),
        };

        // ⭐ Po usunięciu „UDF" KAŻDA kategoria drzewa zależności ma swój `MetadataObjectKind`, więc lista
        // jest już wyłącznie zawężeniem kolejności kanonicznej — bez ani jednej pozycji wstawianej lokalnie.
        return MetadataCategoryOrder
            .Only(labels.Keys.ToArray())
            .Select(kind => new DependencyCategory(labels[kind].Key, kind, labels[kind].Label))
            .ToList();
    }

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
            var iconGeometry = category.Kind is { } k3 ? MetadataNodeViewModel.GeometryKeyFor(k3) : string.Empty;

            IReadOnlyList<DependencyLeafNode> leaves = Array.Empty<DependencyLeafNode>();
            if (byType.TryGetValue(category.ObjectTypeKey, out var matched))
            {
                leaves = matched
                    .Select(d => new DependencyLeafNode
                    {
                        Dependency = d,
                        Icon = icon,
                        IconResourceKey = iconKey,
                        IconGeometryKey = iconGeometry,
                    })
                    .ToList();
            }

            groups.Add(new DependencyGroupNode
            {
                ObjectType = category.DisplayLabel,
                Children = leaves,
                Icon = icon,
                IconResourceKey = iconKey,
                IconGeometryKey = iconGeometry,
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
    [NotifyCanExecuteChangedFor(nameof(DropConstraintCommand))]
    private int _constraintsActiveSubTabIndex;

    // One selected-constraint property per sub-grid. A single shared property
    // bound to all four DataGrids would self-clobber: an item selected in the
    // PK grid isn't present in the FK grid's ItemsSource, so the FK grid's
    // TwoWay SelectedItem binding would push null back. Four independent
    // properties + ActiveConstraint (keyed off the inner sub-tab) sidestep that.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropConstraintCommand))]
    private ConstraintInfo? _selectedPrimaryKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropConstraintCommand))]
    private ConstraintInfo? _selectedForeignKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropConstraintCommand))]
    private ConstraintInfo? _selectedCheck;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropConstraintCommand))]
    private ConstraintInfo? _selectedUnique;

    /// <summary>The constraint the Drop command acts on — the selection of
    /// whichever inner sub-tab is currently active.</summary>
    public ConstraintInfo? ActiveConstraint => ConstraintsActiveSubTabIndex switch
    {
        ConstraintsPrimaryKeyIndex => SelectedPrimaryKey,
        ConstraintsForeignKeysIndex => SelectedForeignKey,
        ConstraintsCheckIndex => SelectedCheck,
        ConstraintsUniqueIndex => SelectedUnique,
        _ => null,
    };

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
    [NotifyPropertyChangedFor(nameof(CanExportData))]
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

    // ── Dane grid: shared filter panel + aggregation bar (SQL push-down) ──────
    public FilterPanelViewModel DataFilterPanel { get; }
    public AggregationBarViewModel DataAggregationBar { get; }
    private GridFilter _dataFilter = GridFilter.Empty;
    private FirebirdGridSqlBuilder.GridSqlFilter? _dataSqlFilter;
    private int _selectedDataRowInPage = -1; // selection within the current page; -1 = none

    // IBExpert-style "Record N of M" for the server-paged Dane grid. M = the bounded
    // COUNT probe (LastKnownRowCount, refreshed on load / sort / filter). A "+" suffix
    // marks a count that hit RowCountCap (i.e. "≥ cap").
    public string DataRecordInfo
    {
        get
        {
            if (DataResult is not { HasResultSet: true }) return string.Empty;
            if (LastKnownRowCount is not { } total) return string.Empty;
            string totalText = total >= RowCountCap
                ? total.ToString(System.Globalization.CultureInfo.CurrentCulture) + "+"
                : total.ToString(System.Globalization.CultureInfo.CurrentCulture);
            if (_selectedDataRowInPage >= 0)
            {
                int global = (CurrentPage - 1) * PageSize + _selectedDataRowInPage + 1;
                return string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.RecordPositionFormat, global, totalText);
            }
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.RecordCountFormat, totalText);
        }
    }

    // Called by the view when the Dane grid selection changes.
    public void SetDataSelectedRow(int indexInPage)
    {
        if (_selectedDataRowInPage == indexInPage) return;
        _selectedDataRowInPage = indexInPage;
        OnPropertyChanged(nameof(DataRecordInfo));
    }

    // Re-point the filter/aggregation panels only when the column STRUCTURE changes
    // (first load / table change) — NOT on every page or filter reload (that would
    // wipe the just-applied conditions, since SetColumns clears them).
    private void SyncDataFilterColumns(QueryResult? value)
    {
        var newNames = value is { HasResultSet: true }
            ? value.Columns.Select(c => c.Name).ToList()
            : new List<string>();
        var curNames = DataFilterPanel.Columns.Select(c => c.Name).ToList();
        if (newNames.SequenceEqual(curNames, StringComparer.Ordinal)) return;
        var cols = GridColumnRef.From(value is { HasResultSet: true } ? value.Columns : null);
        DataFilterPanel.SetColumns(cols);
        DataAggregationBar.SetColumns(cols);
        _dataFilter = GridFilter.Empty;
        _dataSqlFilter = null;
    }

    private FirebirdGridSqlBuilder.GridSqlFilter? BuildDataSqlFilter(GridFilter filter)
    {
        if (filter.IsEmpty) return null;
        var cols = DataFilterPanel.Columns.Select(c => new QueryColumn(c.Name, c.ClrType)).ToList();
        return FirebirdGridSqlBuilder.BuildWhere(filter, cols);
    }

    // Host callback for the filter panel: push the filter to SQL, re-fetch page 1,
    // re-probe the row count (for Record N of M), and recompute the aggregates.
    private async Task ApplyDataFilterAsync(GridFilter filter)
    {
        _dataFilter = filter;
        _dataSqlFilter = BuildDataSqlFilter(filter);
        LastKnownRowCount = null; // the filter changes the row count
        CurrentPage = 1;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
        await RefreshDataRowCountAsync().ConfigureAwait(true);
        await DataAggregationBar.RecomputeAllAsync().ConfigureAwait(true);
    }

    // Host callback for the aggregation bar: a server-side SELECT agg over the WHOLE
    // (filtered) table — never over the current page.
    private Task<object?> ComputeDataAggregateAsync(GridColumnRef col, GridAggregate agg)
        => _reader is null
            ? Task.FromResult<object?>(null)
            : _reader.GetAggregateAsync(TableName, col.Name, agg, _dataSqlFilter);

    // Bounded COUNT(*) (capped at RowCountCap) so Record N of M has an M. Refreshed
    // on initial load / sort / filter — NOT on plain page navigation (the count is
    // stable between pages of the same filter+sort).
    private async Task RefreshDataRowCountAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null) return;
        try
        {
            LastKnownRowCount = await _reader.GetRowCountAsync(TableName, RowCountCap, _dataSqlFilter, cancellationToken).ConfigureAwait(true);
            OnPropertyChanged(nameof(DataRecordInfo));
        }
        catch (MetadataReadException) { /* keep the prior count */ }
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
        // Re-point the filter/aggregation panels when the columns change (first
        // load / table change); a same-column filter reload keeps the conditions.
        SyncDataFilterColumns(value);
        RebuildEditableRows();
        // Re-slicing a page drops grid selection → reset the record pointer.
        _selectedDataRowInPage = -1;
        // HasNextPage reads DataResult.Rows.Count when LastKnownRowCount is null —
        // so the property re-fires whenever the result lands.
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(DataRecordInfo));
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
    /// Recomputes <see cref="PrimaryKeyColumns"/>. The authoritative source is the
    /// PRIMARY KEY entry in <see cref="Constraints"/> — loaded straight from
    /// RDB$RELATION_CONSTRAINTS → RDB$INDEX_SEGMENTS (the same reliable path that fills
    /// the Ograniczenia tab). We deliberately do NOT trust the per-field
    /// <see cref="FieldInfo.IsPrimaryKey"/> flag as the primary source: it comes from a
    /// correlated subquery in FieldsSql whose <c>s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME</c>
    /// CHAR comparison can return 0 for a table that genuinely HAS a primary key, which
    /// left <see cref="HasPrimaryKey"/> false and surfaced "Table has no primary key —
    /// only INSERT is available" on a table IBExpert happily UPDATEs. The flag is used
    /// only as a fallback (e.g. before the constraints have loaded). Called after the
    /// Fields AND the Constraints load steps, and from <see cref="RebuildEditableRows"/>.
    /// </summary>
    public void RefreshPrimaryKeyColumns()
    {
        var fromConstraint = PrimaryKeyColumnsFromConstraints(Constraints);
        PrimaryKeyColumns = fromConstraint.Count > 0
            ? fromConstraint
            : Fields.Where(f => f.IsPrimaryKey).Select(f => f.Name).ToList();
        OnPropertyChanged(nameof(PrimaryKeyColumns));
        OnPropertyChanged(nameof(HasPrimaryKey));
        OnPropertyChanged(nameof(EditModeHint));
        DeleteRowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Extracts the primary-key column names from a loaded constraint set: the fields of
    /// the PRIMARY KEY constraint (the reader stores them comma-separated via LIST()).
    /// Empty when there is no PK constraint. Pure + internal so it's unit-testable.
    /// </summary>
    internal static IReadOnlyList<string> PrimaryKeyColumnsFromConstraints(IEnumerable<ConstraintInfo> constraints)
    {
        foreach (var c in constraints)
        {
            if (string.Equals(c.ConstraintType, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            {
                return (c.Fields ?? string.Empty)
                    .Split(',')
                    .Select(f => f.Trim())
                    .Where(f => f.Length > 0)
                    .ToList();
            }
        }
        return Array.Empty<string>();
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

        HasPendingDataEdits = true;
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
    // True once a data cell/row has been edited in the current working transaction.
    // The owner uses it to scope the post-rollback data-preview reload to ONLY the
    // tabs that were actually edited — never a blanket refresh of every open tab (the
    // refresh-storm fix, gotcha #119). Cleared after a data-preview reload / commit.
    public bool HasPendingDataEdits { get; set; }

    public async Task UpdateCellAsync(object?[] row, int columnIndex, object? newValue)
    {
        if (row is null) return;
        if (columnIndex < 0 || columnIndex >= row.Length) return;
        if (_dataEditor is null) return;
        HasPendingDataEdits = true;

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

    /// <summary>
    /// True when the data-preview column at <paramref name="columnIndex"/> accepts
    /// NULL — gates the cell context-menu's "Set NULL". Nullable means: a matching
    /// <see cref="FieldInfo"/> exists, it is NOT declared NOT NULL, it is not a
    /// primary-key column, and it is not a computed (read-only) column.
    /// </summary>
    public bool IsColumnNullable(int columnIndex)
    {
        var name = ResolveColumnName(columnIndex);
        if (name is null) return false;
        var field = Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (field is null) return false;
        if (field.NotNull || field.IsPrimaryKey) return false;
        if (!string.IsNullOrWhiteSpace(field.ComputedSource)) return false;
        return true;
    }

    /// <summary>
    /// Sets the right-clicked cell to NULL through the EXACT SAME
    /// <see cref="UpdateCellAsync"/> path a manual edit uses — same change-tracking,
    /// same UPDATE statement, same optimistic-write/revert handling. No separate save
    /// path. Only acts on nullable columns. Because there is no in-grid CellEditEnding
    /// to trigger Avalonia's cell-template rebuild, the row is repainted afterwards.
    /// </summary>
    public async Task SetCellNullAsync(object?[] row, int columnIndex)
    {
        if (row is null) return;
        if (columnIndex < 0 || columnIndex >= row.Length) return;
        if (_dataEditor is null) return;
        if (!IsColumnNullable(columnIndex)) return;
        if (row[columnIndex] is null) return; // already NULL — nothing to do

        await UpdateCellAsync(row, columnIndex, null).ConfigureAwait(true);

        // UpdateCellAsync repaints (ReplaceRowInGrid) only on its revert paths; on the
        // success path the row reference is unchanged and nothing told the grid to
        // rebuild the cell (no CellEditEnding fired for a context-menu action). Force
        // the repaint. If the update reverted (cloned the row away), this no-ops because
        // the original 'row' is no longer in EditableRows.
        ReplaceRowInGrid(row);
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
        HasPendingDataEdits = true;

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
            await LoadStructureCoreAsync(cancellationToken).ConfigureAwait(true);
            await LoadDataPreviewCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Loads everything EXCEPT the data preview: fields, constraints, indexes,
    // dependencies, DDL, description, domains. Separated from the data preview so
    // a metadata-only structure refresh (e.g. a column type/length edit that
    // doesn't change the column SET) can skip the expensive `SELECT *` reload —
    // the single biggest contributor to the post-commit refresh storm.
    //
    // Sequential by design — FbConnection services one command at a time, so
    // Task.WhenAll across these calls would throw "Connection in use". Each step
    // is independently try/caught (SafeLoadAsync): a failure in one query must not
    // strand the other sub-tabs empty.
    private async Task LoadStructureCoreAsync(CancellationToken cancellationToken)
    {
        if (_reader is null || _ddlReader is null) return;
        Diagnostics.RefreshTrace.Log("LoadStructure", $"begin table={TableName}");

        await SafeLoadAsync(
            async () =>
            {
                var fields = await _reader.GetFieldsAsync(TableName, cancellationToken).ConfigureAwait(true);
                ReplaceFields(fields);
                RefreshPrimaryKeyColumns();
            });

        await SafeLoadAsync(
            async () =>
            {
                var constraints = await _reader.GetConstraintsAsync(TableName, cancellationToken).ConfigureAwait(true);
                Constraints.Clear();
                foreach (var c in constraints) Constraints.Add(c);
                // PK now comes from the PRIMARY KEY constraint (authoritative) — the
                // Fields step may have derived it from the unreliable per-field flag,
                // so recompute it here now that Constraints is populated.
                RefreshPrimaryKeyColumns();
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
                // DDL tab == Export: the full portable script (structure + table/column
                // COMMENT ON) via the same MetadataExportService the Export button uses.
                // DdlWithPendingPreview appends any queued designer changes on top of this.
                DdlText = await new MetadataExportService(_ddlReader, _reader).BuildObjectScriptAsync(
                    new MetadataObject(TableName, MetadataObjectKind.Table),
                    cancellationToken).ConfigureAwait(true);
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
                    // No "(none)" sentinel — the SearchableComboBox clears via its ✕ button.
                    foreach (var d in domains) AvailableDomains.Add(d);
                });
        }
        Diagnostics.RefreshTrace.Log("LoadStructure", $"end table={TableName}");
    }

    // The data preview step (`SELECT * … ROWS m TO n`). Own visible error slot
    // (DataError → shown on the Dane tab); other tabs render normally even when
    // this fails (large tables, permission denied, dialect mismatch on quoted IDs).
    private async Task LoadDataPreviewCoreAsync(CancellationToken cancellationToken)
    {
        if (_reader is null) return;
        Diagnostics.RefreshTrace.Log("LoadDataPreview", $"SELECT * table={TableName} page={CurrentPage} size={PageSize}");
        try
        {
            var preview = await _reader.GetDataPreviewAsync(TableName, CurrentPage, PageSize, null, _dataSqlFilter, cancellationToken).ConfigureAwait(true);
            DataResult = preview;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
            // Probe the (bounded) row count so Record N of M has an M on first load.
            await RefreshDataRowCountAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (MetadataReadException ex)
        {
            DataResult = null;
            DataError = ex.Message;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
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
        // Sort reset LastKnownRowCount → re-probe so Record N of M keeps its M.
        await RefreshDataRowCountAsync(cancellationToken).ConfigureAwait(true);
    }

    internal async Task ReloadDataPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null) return;

        // Synchronize with the lazy initial load — refreshing data before Fields
        // has finished loading leaves PrimaryKeyColumns empty and the edit hint
        // would falsely say "Table has no primary key". EnsureLoadedAsync is
        // idempotent: returns instantly when LoadAsync has already completed.
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);

        var orderBy = BuildDataOrderBy();

        try
        {
            var preview = await _reader.GetDataPreviewAsync(TableName, CurrentPage, PageSize, orderBy, _dataSqlFilter, cancellationToken).ConfigureAwait(true);
            DataResult = preview;
            DataError = string.Empty;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
            HasPendingDataEdits = false; // grid now matches the DB

        }
        catch (MetadataReadException ex)
        {
            DataResult = null;
            DataError = ex.Message;
            DataResultVersionTag = System.Guid.NewGuid().ToString("N");
        }
    }

    // The current data-grid ORDER BY (a quoted column + ASC/DESC), or null when unsorted. Shared by
    // the page reload and the export re-fetch so an export matches exactly what the grid shows.
    private string? BuildDataOrderBy()
    {
        if (string.IsNullOrEmpty(SortColumn)) return null;
        var escaped = SortColumn.Replace("\"", "\"\"");
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "\"{0}\" {1}",
            escaped,
            SortDescending ? "DESC" : "ASC");
    }

    public bool CanExportData => _reader is not null && DataResult is { HasResultSet: true };

    /// <summary>Builds the shared-framework export source for the Dane grid (server-paged):
    /// <see cref="ExportScope.CurrentView"/> = the current page; <see cref="ExportScope.AllRows"/>
    /// re-fetches the whole table page-by-page with the current filter + order. The AllRows estimate
    /// is the bounded COUNT probe (approximate when it hit the cap).</summary>
    public IExportDataSource? BuildDataExportSource()
    {
        if (_reader is not { } reader || DataResult is not { HasResultSet: true } result) return null;

        var columns = result.Columns.Select(c => new ExportColumn(c.Name, c.ClrType)).ToList();
        var allEstimate = LastKnownRowCount is { } count
            ? (count >= RowCountCap ? RowEstimate.Approximate(count) : RowEstimate.Exact(count))
            : RowEstimate.Unknown;
        var orderBy = BuildDataOrderBy();
        var filter = _dataSqlFilter;

        return new ServerPagedExportSource(
            columns,
            result.Rows,
            allEstimate,
            async (page, size, ct) => (await reader.GetDataPreviewAsync(TableName, page, size, orderBy, filter, ct).ConfigureAwait(false)).Rows,
            ServerPagedExportSource.DefaultFetchPageSize,
            TableName);
    }

    // ── Copy as INSERT / UPDATE (E6) ──────────────────────────────────────────
    // The SAME shared controller + coordinator the SQL Editor grid uses — one mechanism, no re-derived
    // copy. Table Data is the safe case: the grid IS a table, so it declares OriginShape.DirectTable and
    // nothing is inferred from a statement. Provenance (columns + declared types) is captured lazily via
    // the reader's Data-lane schema seam and cached for the tab's life (it never changes with page /
    // filter / sort), so the ~7 ms cost lands on the first Copy, never on data load.

    /// <summary>The copy controller for the Dane grid, bound by its context menu
    /// (<c>SqlCopy.CanCopyAsInsert</c> / <c>SqlCopy.CopyAsInsertTooltip</c>). Null until
    /// <see cref="EnableSqlCopy"/> supplies the catalog — a system/read-only table or a reader-less test
    /// VM leaves it null and the menu items stay disabled.</summary>
    public SqlCopyController? SqlCopy { get; private set; }

    /// <summary>Turns on Copy-as-INSERT/UPDATE for this table by supplying signal C (the catalog snapshot)
    /// and the column warmer — the same two the SQL Editor coordinator uses. Called once at construction
    /// for a writable table; a no-op without a reader.</summary>
    internal void EnableSqlCopy(Func<ISqlMetadataProvider> catalog, Func<string, Task> warmColumns)
    {
        if (_reader is not { } reader || SqlCopy is not null) return;

        SqlCopy = new SqlCopyController(new SqlCopyCoordinator(
            ct => CaptureDirectOriginAsync(reader, ct),
            catalog)
        {
            WarmColumns = warmColumns,
        });
        OnPropertyChanged(nameof(SqlCopy));
    }

    private async Task<ResultOrigin> CaptureDirectOriginAsync(FirebirdTableDetailReader reader, CancellationToken cancellationToken)
    {
        var schema = await reader.CaptureDataSchemaTableAsync(TableName, cancellationToken).ConfigureAwait(true);
        if (schema is null)
        {
            // The table no longer prepares (dropped, tx rolled back) — honest "no provenance", the menu
            // item disables with a reason rather than erroring.
            return ResultOrigin.None(ExportUnavailableReason.Of(ExportUnavailableCode.StatementNotUnderstood));
        }

        return new ResultOrigin(
            FirebirdResultOriginReader.ReadColumnOrigins(schema),
            new OriginShape.DirectTable(TableName));
    }

    /// <summary>Re-evaluates whether the copy actions are available — called when the Dane grid's context
    /// menu opens (the gesture that makes the lazy schema capture "on demand").</summary>
    public Task RefreshSqlCopyAvailabilityAsync(CancellationToken cancellationToken = default)
        => SqlCopy?.RefreshAvailabilityAsync(HasDataResult, cancellationToken) ?? Task.CompletedTask;

    /// <summary>Builds the right-clicked row as INSERT/UPDATE. On refusal, surfaces the reason in the edit
    /// status (the same red status the row editors use) and returns null; on success returns the formatted
    /// SQL for the view to place on the clipboard.</summary>
    public async Task<string?> CopyRowAsSqlAsync(ExportFormat format, object?[] row, CancellationToken cancellationToken = default)
    {
        if (SqlCopy is not { } copy) return null;

        var built = await copy.BuildFormattedAsync(format, row, cancellationToken).ConfigureAwait(true);
        if (!built.IsBuilt)
        {
            EditStatusMessage = built.Text;
            OnPropertyChanged(nameof(HasEditStatusMessage));
            return null;
        }

        EditStatusMessage = string.Empty;
        OnPropertyChanged(nameof(HasEditStatusMessage));
        return built.Text;
    }

    /// <summary>
    /// Clipboard text for the grid's Copy cell / row / row with headers / all with headers actions, through
    /// the one shared <see cref="GridCopyText"/> builder every data grid uses. Returns null when there is
    /// nothing to copy; the view writes the clipboard.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ "All" reads <see cref="EditableRows"/>, NOT <c>DataResult.Rows</c>, and that is the one place this
    /// grid genuinely differs from the read-only ones: rows added or deleted in this session live only in the
    /// writable mirror. Copying the result would emit rows the user has deleted and omit ones they added —
    /// silently, since the text would look perfectly well-formed.
    /// </remarks>
    public string? BuildCopyText(CopyGridMode mode, object?[]? row, int columnIndex)
        => GridCopyText.Build(
            mode,
            DataResult?.Columns ?? Array.Empty<QueryColumn>(),
            EditableRows,
            row,
            columnIndex);

    // ─── Pagination commands ──────────────────────────────────────────────
    //
    // CanExecute for these is computed against the current HasPrev/Next state.
    // GoToLastPage probes COUNT(*) via the reader's bounded row-count query
    // before navigating; the others are pure CurrentPage assignment + reload.

    // Re-fetch the current data page. Lives on the tab VM (not the window) so the
    // Refresh button sits in the Dane sub-tab's own grid toolbar, alongside the
    // pagination + filter/aggregation controls (unified layout across all grids).
    [RelayCommand]
    private Task RefreshDataPreview() => ReloadDataPreviewAsync();

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
            var count = await _reader.GetRowCountAsync(TableName, RowCountCap, _dataSqlFilter).ConfigureAwait(true);
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

    // Unsaved-work for the WorkGuard: queued-but-not-compiled structural changes.
    // Pending DATA edits are transaction work (data lane) — surfaced by the
    // transaction guard at disconnect/exit, not here, so closing a single table
    // tab doesn't read as "lose your data edits" (the tx keeps them).
    public UnsavedWorkItem? GetUnsavedWork()
        => HasPendingChanges
            ? new UnsavedWorkItem(UnsavedWorkKind.PendingStructure,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.UnsavedPendingStructureFormat, TableName))
            : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropFieldCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditFieldCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFieldUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFieldDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropFieldForeignKeyCommand))]
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
    public Task ExecuteEditFieldAsync(FieldInfo original, FieldDefinition target)
    {
        if (original is null) return Task.CompletedTask;
        if (target is null) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

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
            // / PrimaryKey). Don't queue; don't touch ErrorMessage.
            return Task.CompletedTask;
        }

        // BUFFERED: queue the ALTERs + reflect the new values on the matching
        // Pola row (marked Modified). NO DDL runs here — Compile applies the batch.
        ErrorMessage = null;
        foreach (var change in statements) PendingChanges.Add(change);

        var row = EditableFields.FirstOrDefault(r =>
            string.Equals(r.Original.Name, original.Name, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            row.Name = target.Name;
            row.TypeText = alterTarget.TypeClause;
            row.DomainName = string.IsNullOrWhiteSpace(target.Domain) ? null : target.Domain;
            row.NotNull = target.NotNull;
            row.DefaultValue = target.DefaultValue ?? string.Empty;
            row.Description = target.Description ?? string.Empty;
            if (row.PendingKind != PendingChangeKind.Added) row.PendingKind = PendingChangeKind.Modified;
        }
        return Task.CompletedTask;
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
    public Task ExecuteCreateForeignKeyAsync(ForeignKeySpec spec)
    {
        if (spec is null) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

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
            return Task.CompletedTask;
        }

        // BUFFERED: queue the FK DDL + show it as a pending-Added row in the
        // Foreign Keys sub-grid. NO DDL runs here — Compile applies the batch.
        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.Other,
            Description = UiStrings.TableDetailAddForeignKeyPrefix + spec.ConstraintName,
            Sql = sql,
        });
        Constraints.Add(new ConstraintInfo
        {
            Name = spec.ConstraintName,
            ConstraintType = "FOREIGN KEY",
            Fields = string.Join(", ", spec.LocalFields),
            RefTable = spec.ReferencedTable,
            RefFields = string.Join(", ", spec.ReferencedFields),
            UpdateRule = RuleLabel(spec.OnUpdate),
            DeleteRule = RuleLabel(spec.OnDelete),
            PendingState = PendingChangeKind.Added,
        });
        SelectNewConstraint(ConstraintsForeignKeysIndex, ForeignKeyConstraints, spec.ConstraintName);
        return Task.CompletedTask;
    }

    private static string RuleLabel(ForeignKeyAction action) => action switch
    {
        ForeignKeyAction.Cascade => "CASCADE",
        ForeignKeyAction.SetNull => "SET NULL",
        _ => string.Empty,
    };

    // ─── Constraint management (Constraint Management Sprint V1) ──────────
    //
    // Add (PK / Check / Unique — FK reuses the wizard above) + Drop. All Add
    // dialogs follow the FK pattern: the VM raises a *Requested event, the view
    // opens the dialog and returns a spec (or null on Cancel). Execution goes
    // through FirebirdDdlExecutor in the user's working transaction (Rollback
    // undoes), then RefreshStructureAsync re-reads the catalog and we jump to
    // the matching inner sub-tab + select the new row. Drop is type-agnostic
    // (one command for all four sub-tabs) and confirms via ConfirmDialog.

    public bool CanManageConstraints => _ddlExecutor is not null;
    public bool CanDropConstraint => _ddlExecutor is not null && ActiveConstraint is not null;

    /// <summary>View returns the picked <see cref="ConstraintFieldSpec"/> from
    /// the field-picker dialog (Primary Key), or null on Cancel.</summary>
    public event System.Func<Task<ConstraintFieldSpec?>>? AddPrimaryKeyRequested;

    /// <summary>View returns the picked <see cref="ConstraintFieldSpec"/> from
    /// the field-picker dialog (Unique), or null on Cancel.</summary>
    public event System.Func<Task<ConstraintFieldSpec?>>? AddUniqueRequested;

    /// <summary>View returns the <see cref="CheckConstraintSpec"/> from the
    /// check dialog, or null on Cancel.</summary>
    public event System.Func<Task<CheckConstraintSpec?>>? AddCheckRequested;

    [RelayCommand(CanExecute = nameof(CanManageConstraints))]
    private async Task AddPrimaryKey()
    {
        if (AddPrimaryKeyRequested is null) return;
        var spec = await AddPrimaryKeyRequested().ConfigureAwait(true);
        if (spec is null) return;
        await ExecuteAddPrimaryKeyAsync(spec).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanManageConstraints))]
    private async Task AddUnique()
    {
        if (AddUniqueRequested is null) return;
        var spec = await AddUniqueRequested().ConfigureAwait(true);
        if (spec is null) return;
        await ExecuteAddUniqueAsync(spec).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanManageConstraints))]
    private async Task AddCheck()
    {
        if (AddCheckRequested is null) return;
        var spec = await AddCheckRequested().ConfigureAwait(true);
        if (spec is null) return;
        await ExecuteAddCheckAsync(spec).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanDropConstraint))]
    private async Task DropConstraint()
    {
        if (ActiveConstraint is not { } constraint) return;
        await ConfirmAndDropConstraintAsync(constraint.Name).ConfigureAwait(true);
    }

    // Shared confirm-then-drop for any constraint by name. Used by the
    // Ograniczenia sub-tab Drop command AND the Pola tab's Drop-Foreign-Key
    // entry (#1) so there's a single Drop Constraint code path.
    private async Task ConfirmAndDropConstraintAsync(string constraintName)
    {
        if (string.IsNullOrWhiteSpace(constraintName)) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.ConstraintDropConfirmTitle,
            Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.ConstraintDropConfirmFormat, constraintName),
            ConfirmLabel = UiStrings.ConstraintDropConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await ExecuteDropConstraintAsync(constraintName).ConfigureAwait(true);
    }

    // ─── Drop Foreign Key from the Pola sub-tab (#1) ──────────────────────
    //
    // Reuses the Drop Constraint path above. The FK constraint name is resolved
    // from the selected field by matching it against the FK constraints' local
    // field lists. No new FK-drop implementation — just a resolver + the shared
    // confirm/drop.

    public bool CanDropFieldForeignKey
        => _ddlExecutor is not null && SelectedField is { IsForeignKey: true };

    [RelayCommand(CanExecute = nameof(CanDropFieldForeignKey))]
    private async Task DropFieldForeignKey()
    {
        var name = ResolveForeignKeyConstraintForField(SelectedField);
        if (name is null)
        {
            // The field is flagged FK but we couldn't match a constraint (e.g.
            // constraints not loaded). Surface a message rather than silently no-op.
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.ConstraintExecuteFailedFormat, SelectedField?.Name ?? string.Empty);
            return;
        }
        await ConfirmAndDropConstraintAsync(name).ConfigureAwait(true);
    }

    // Finds the FOREIGN KEY constraint whose local field list contains the given
    // field. ConstraintInfo.Fields is a comma-separated list of local columns.
    internal string? ResolveForeignKeyConstraintForField(FieldInfo? field)
    {
        if (field is null || string.IsNullOrEmpty(field.Name)) return null;
        foreach (var c in ForeignKeyConstraints)
        {
            if (string.IsNullOrEmpty(c.Fields)) continue;
            foreach (var part in c.Fields.Split(','))
            {
                if (string.Equals(part.Trim(), field.Name, StringComparison.OrdinalIgnoreCase))
                    return c.Name;
            }
        }
        return null;
    }

    /// <summary>
    /// Executes ADD CONSTRAINT … PRIMARY KEY in the user's working transaction,
    /// then refreshes + jumps to Ograniczenia → Primary Key with the new
    /// constraint selected. Public for tests (no dialog, no confirm).
    /// </summary>
    public Task ExecuteAddPrimaryKeyAsync(ConstraintFieldSpec spec)
        => StageConstraintAddAsync(
            spec is null ? null : SafeBuild(() => DdlGenerator.BuildAddPrimaryKey(TableName, spec.Name, spec.Fields, spec.IndexName, spec.Descending)),
            spec is null ? null : new ConstraintInfo
            {
                Name = spec.Name,
                ConstraintType = "PRIMARY KEY",
                Fields = string.Join(", ", spec.Fields),
                IndexName = spec.IndexName ?? string.Empty,
                IsDescending = spec.Descending,
                PendingState = PendingChangeKind.Added,
            },
            ConstraintsPrimaryKeyIndex);

    /// <summary>
    /// BUFFERED ADD CONSTRAINT … UNIQUE. Symmetric to
    /// <see cref="ExecuteAddPrimaryKeyAsync"/>.
    /// </summary>
    public Task ExecuteAddUniqueAsync(ConstraintFieldSpec spec)
        => StageConstraintAddAsync(
            spec is null ? null : SafeBuild(() => DdlGenerator.BuildAddUnique(TableName, spec.Name, spec.Fields, spec.IndexName, spec.Descending)),
            spec is null ? null : new ConstraintInfo
            {
                Name = spec.Name,
                ConstraintType = "UNIQUE",
                Fields = string.Join(", ", spec.Fields),
                IndexName = spec.IndexName ?? string.Empty,
                IsDescending = spec.Descending,
                PendingState = PendingChangeKind.Added,
            },
            ConstraintsUniqueIndex);

    /// <summary>
    /// BUFFERED ADD CONSTRAINT … CHECK. Symmetric to
    /// <see cref="ExecuteAddPrimaryKeyAsync"/>.
    /// </summary>
    public Task ExecuteAddCheckAsync(CheckConstraintSpec spec)
        => StageConstraintAddAsync(
            spec is null ? null : SafeBuild(() => DdlGenerator.BuildAddCheck(TableName, spec.Name, spec.Expression)),
            spec is null ? null : new ConstraintInfo
            {
                Name = spec.Name,
                ConstraintType = "CHECK",
                CheckClause = spec.Expression,
                PendingState = PendingChangeKind.Added,
            },
            ConstraintsCheckIndex);

    /// <summary>
    /// Executes ALTER TABLE … DROP CONSTRAINT in the user's working transaction
    /// and refreshes. Public for tests (the confirm lives in
    /// <see cref="DropConstraintCommand"/>). After the drop the user stays on
    /// the current inner sub-tab; the gone row clears its selection.
    /// </summary>
    public Task ExecuteDropConstraintAsync(string constraintName)
    {
        if (string.IsNullOrWhiteSpace(constraintName)) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

        ErrorMessage = null;
        string sql;
        try
        {
            sql = DdlGenerator.BuildDropConstraint(TableName, constraintName);
        }
        catch (System.ArgumentException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.ConstraintExecuteFailedFormat, ex.Message);
            return Task.CompletedTask;
        }

        var existing = Constraints.FirstOrDefault(c => string.Equals(c.Name, constraintName, StringComparison.OrdinalIgnoreCase));
        if (existing is { PendingState: PendingChangeKind.Added })
        {
            // Un-add a not-yet-compiled constraint: drop the queued ADD + the row.
            RemovePendingByMarker(PendingDdlChangeKind.Other, $"ADD CONSTRAINT \"{constraintName.Replace("\"", "\"\"")}\"");
            Constraints.Remove(existing);
            return Task.CompletedTask;
        }

        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.Other,
            Description = "Drop constraint " + constraintName,
            Sql = sql,
        });
        if (existing is not null) MarkConstraintDropped(existing);
        return Task.CompletedTask;
    }

    // BUFFERED shared ADD path: queue the (already-built) DDL, insert the
    // pending-Added row into Constraints so the working model shows it, then
    // jump to the matching inner sub-tab and select it. A null sql/row means a
    // null spec or a builder validation gap (SafeBuild set ErrorMessage) → bail.
    private Task StageConstraintAddAsync(string? sql, ConstraintInfo? pendingRow, int innerIndex)
    {
        if (_ddlExecutor is null) return Task.CompletedTask;
        if (sql is null || pendingRow is null) return Task.CompletedTask;

        ErrorMessage = null;
        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.Other,
            Description = string.Format(CultureInfo.CurrentCulture, UiStrings.TableDetailAddConstraintFormat, pendingRow.ConstraintType, pendingRow.Name),
            Sql = sql,
        });
        Constraints.Add(pendingRow);
        SelectNewConstraint(innerIndex, ConstraintListFor(innerIndex), pendingRow.Name);
        return Task.CompletedTask;
    }

    private IReadOnlyList<ConstraintInfo> ConstraintListFor(int innerIndex) => innerIndex switch
    {
        ConstraintsForeignKeysIndex => ForeignKeyConstraints,
        ConstraintsCheckIndex => CheckConstraints,
        ConstraintsUniqueIndex => UniqueConstraints,
        _ => PrimaryKeyConstraints,
    };

    // Marks a live constraint row pending-Dropped (kept visible, tinted) and
    // forces the filtered-view-bound grid to re-render it via a Replace.
    private void MarkConstraintDropped(ConstraintInfo c)
    {
        c.PendingState = PendingChangeKind.Dropped;
        var idx = Constraints.IndexOf(c);
        if (idx >= 0) Constraints[idx] = c;
    }

    // Removes the first queued PendingDdlChange of the given kind whose SQL
    // contains the marker (used to un-queue an ADD when its pending row is
    // dropped before Compile).
    private void RemovePendingByMarker(PendingDdlChangeKind kind, string marker)
    {
        for (int i = PendingChanges.Count - 1; i >= 0; i--)
        {
            if (PendingChanges[i].Kind == kind
                && PendingChanges[i].Sql.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                PendingChanges.RemoveAt(i);
                break;
            }
        }
    }

    // Build SQL, trapping the builder's validation ArgumentException into
    // ErrorMessage and returning null so the caller bails before executing.
    private string? SafeBuild(System.Func<string> build)
    {
        try
        {
            return build();
        }
        catch (System.ArgumentException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.ConstraintExecuteFailedFormat, ex.Message);
            return null;
        }
    }

    // Jump to Ograniczenia → <innerIndex> and select the named constraint in
    // the matching filtered list (reference-equal to the grid's bound list, so
    // SelectedItem highlights the row).
    private void SelectNewConstraint(int innerIndex, IReadOnlyList<ConstraintInfo> list, string name)
    {
        ActiveSubTabIndex = ConstraintsSubTabIndex;
        ConstraintsActiveSubTabIndex = innerIndex;
        var match = list.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        switch (innerIndex)
        {
            case ConstraintsPrimaryKeyIndex: SelectedPrimaryKey = match; break;
            case ConstraintsForeignKeysIndex: SelectedForeignKey = match; break;
            case ConstraintsCheckIndex: SelectedCheck = match; break;
            case ConstraintsUniqueIndex: SelectedUnique = match; break;
        }
    }

    // ─── Index management (Index Management V1) ───────────────────────────
    //
    // Add + Drop, modeled on Constraint Management V1. Add runs CREATE INDEX in
    // the user's working transaction; Drop runs DROP INDEX after a confirm.
    // Indexes backing a PK / FK / UNIQUE constraint are blocked from dropping
    // here (managed via the Ograniczenia tab) — a friendly message is surfaced
    // instead of letting Firebird reject the DROP with a cryptic error.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecomputeIndexStatisticsCommand))]
    private IndexInfo? _selectedIndex;

    public bool CanManageIndexes => _ddlExecutor is not null;
    public bool CanDropIndex => _ddlExecutor is not null && SelectedIndex is not null;

    /// <summary>View returns the picked <see cref="IndexSpec"/> from the Add-Index
    /// dialog, or null on Cancel.</summary>
    public event System.Func<Task<IndexSpec?>>? AddIndexRequested;

    [RelayCommand(CanExecute = nameof(CanManageIndexes))]
    private async Task AddIndex()
    {
        if (AddIndexRequested is null) return;
        var spec = await AddIndexRequested().ConfigureAwait(true);
        if (spec is null) return;
        await ExecuteAddIndexAsync(spec).ConfigureAwait(true);
    }

    /// <summary>
    /// True when the index backs a PK / FK / UNIQUE constraint. Such indexes
    /// can't be dropped directly (Firebird rejects it) — they go through the
    /// constraint. PK/FK are detected via <see cref="IndexInfo.IndexType"/>;
    /// the unique-constraint backing index is matched against the constraints'
    /// <see cref="ConstraintInfo.IndexName"/>.
    /// </summary>
    internal bool IsConstraintBackedIndex(IndexInfo? index)
    {
        if (index is null) return false;
        if (index.IsPrimary || index.IsForeignKeyIndex) return true;
        foreach (var c in Constraints)
        {
            if (!string.IsNullOrEmpty(c.IndexName)
                && string.Equals(c.IndexName, index.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanDropIndex))]
    private async Task DropIndex()
    {
        if (SelectedIndex is not { } index) return;
        if (string.IsNullOrWhiteSpace(index.Name)) return;
        if (IsConstraintBackedIndex(index))
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.IndexConstraintBackedFormat, index.Name);
            return;
        }
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.IndexDropConfirmTitle,
            Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.IndexDropConfirmFormat, index.Name),
            ConfirmLabel = UiStrings.IndexDropConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await ExecuteDropIndexAsync(index.Name).ConfigureAwait(true);
    }

    /// <summary>
    /// Executes CREATE INDEX in the user's working transaction, refreshes, then
    /// jumps to the Indeksy sub-tab with the new index selected. Public for tests.
    /// </summary>
    public Task ExecuteAddIndexAsync(IndexSpec spec)
    {
        if (spec is null) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

        var sql = SafeBuildIndex(() => DdlGenerator.BuildCreateIndex(
            TableName, spec.Name, spec.Fields, spec.Unique, spec.Descending, spec.ComputedExpression));
        if (sql is null) return Task.CompletedTask;

        // BUFFERED: queue CREATE INDEX + show a pending-Added row. NO DDL here.
        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.Other,
            Description = UiStrings.TableDetailAddIndexPrefix + spec.Name,
            Sql = sql,
        });
        Indexes.Add(new IndexInfo
        {
            Name = spec.Name,
            Fields = string.Join(", ", spec.Fields),
            IsUnique = spec.Unique,
            IsDescending = spec.Descending,
            Expression = spec.ComputedExpression,
            PendingState = PendingChangeKind.Added,
        });
        ActiveSubTabIndex = IndexesSubTabIndex;
        SelectedIndex = Indexes.FirstOrDefault(i => string.Equals(i.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    /// <summary>
    /// BUFFERED. Marks an index pending-Dropped (or un-adds a pending-Added one)
    /// and queues a DROP INDEX change. NO DDL runs here. Public for tests (the
    /// confirm + constraint-backed guard live in <see cref="DropIndexCommand"/>).
    /// </summary>
    public Task ExecuteDropIndexAsync(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName)) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

        var sql = SafeBuildIndex(() => DdlGenerator.BuildDropIndex(indexName));
        if (sql is null) return Task.CompletedTask;

        var existing = Indexes.FirstOrDefault(i => string.Equals(i.Name, indexName, StringComparison.OrdinalIgnoreCase));
        if (existing is { PendingState: PendingChangeKind.Added })
        {
            RemovePendingByMarker(PendingDdlChangeKind.Other, $"INDEX \"{indexName.Replace("\"", "\"\"")}\"");
            Indexes.Remove(existing);
            return Task.CompletedTask;
        }

        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.Other,
            Description = "Drop index " + indexName,
            Sql = sql,
        });
        if (existing is not null)
        {
            existing.PendingState = PendingChangeKind.Dropped;
            var idx = Indexes.IndexOf(existing);
            if (idx >= 0) Indexes[idx] = existing; // force the bound grid to re-render
        }
        return Task.CompletedTask;
    }

    private string? SafeBuildIndex(System.Func<string> build)
    {
        try
        {
            return build();
        }
        catch (System.ArgumentException ex)
        {
            ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.IndexExecuteFailedFormat, ex.Message);
            return null;
        }
    }

    // ─── Index statistics (Przelicz statystykę / Przelicz wszystkie) ──────
    //
    // SET STATISTICS INDEX recomputes a single index's selectivity. It runs in its own
    // short, AUTO-COMMITTED administrative transaction (ExecuteAutonomousBatchAsync) —
    // NOT the working transaction — so the operation completes immediately and the user
    // never has to press Commit (matching IBExpert). "Recompute all" passes the already-
    // loaded index list (no extra fetch); each statement is committed independently, so
    // a single failure doesn't abort the rest, and the failures are reported.

    // Informational (non-error) completion line shown under the sub-tabs next to
    // ErrorMessage. Set after a recompute completes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public bool CanRecomputeIndexStatistics => _ddlExecutor is not null && SelectedIndex is not null;
    public bool CanRecomputeAllIndexStatistics => _ddlExecutor is not null && Indexes.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRecomputeIndexStatistics))]
    private Task RecomputeIndexStatistics()
    {
        if (SelectedIndex is not { } index || string.IsNullOrWhiteSpace(index.Name))
        {
            return Task.CompletedTask;
        }
        return RecomputeStatisticsForAsync(new[] { index.Name }, single: true);
    }

    [RelayCommand(CanExecute = nameof(CanRecomputeAllIndexStatistics))]
    private Task RecomputeAllIndexStatistics()
    {
        var names = Indexes
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        return RecomputeStatisticsForAsync(names, single: false);
    }

    /// <summary>
    /// Recomputes <c>SET STATISTICS INDEX</c> for each named index in its OWN short
    /// auto-committed admin transaction (no working transaction, no manual Commit —
    /// IBExpert behaviour). A failure on one index is recorded and the rest still run.
    /// Refreshes the structure so the Statistics column shows the committed values, then
    /// surfaces a completion message. Public so a unit test can drive it with a
    /// disconnected executor (which exercises the all-failed branch).
    /// </summary>
    public async Task RecomputeStatisticsForAsync(IReadOnlyList<string> indexNames, bool single)
    {
        if (_ddlExecutor is null) return;
        if (indexNames is null || indexNames.Count == 0) return;

        ErrorMessage = null;
        StatusMessage = null;

        // Build one SET STATISTICS per (valid) index name; keep names + SQL aligned.
        var names = new List<string>(indexNames.Count);
        var sqls = new List<string>(indexNames.Count);
        foreach (var name in indexNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var sql = SafeBuildIndex(() => DdlGenerator.BuildSetIndexStatistics(name));
            if (sql is null) continue; // builder validation failed; ErrorMessage already set
            names.Add(name);
            sqls.Add(sql);
        }
        if (sqls.Count == 0) return;

        // Autonomous, auto-committed admin transaction(s) — leaves no pending tx.
        IReadOnlyList<string?> results;
        try
        {
            results = await _ddlExecutor.ExecuteAutonomousBatchAsync(sqls).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // No active connection — treat every statement as failed.
            results = System.Linq.Enumerable.Repeat<string?>(UiStrings.DataEditNotConnectedHint, sqls.Count).ToList();
        }

        var ok = 0;
        var failures = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var error = i < results.Count ? results[i] : "unknown";
            if (error is null) ok++;
            else failures.Add(names[i]);
        }

        // Refresh so the Statistics column reflects the (committed) recomputed selectivity.
        await RefreshStructureAsync().ConfigureAwait(true);

        // Completion info — set AFTER the refresh so the reload doesn't clear it.
        if (single)
        {
            StatusMessage = failures.Count == 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.IndexStatsRecomputedOneFormat, names[0])
                : null;
        }
        else
        {
            StatusMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.IndexStatsRecomputedAllFormat, ok, names.Count);
        }

        if (failures.Count > 0)
        {
            ErrorMessage = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.IndexStatsRecomputeFailedFormat,
                string.Join(", ", failures),
                string.Empty);
        }
    }

    // ─── Table description editing (Opis tab) ─────────────────────────────
    //
    // COMMENT ON TABLE participates in the working transaction (auto-begin via
    // DdlExecutor) like every other structural edit. Save persists the current
    // EditableDescription; Clear empties it and persists IS NULL. Both refresh.

    /// <summary>User-editable copy of the table description. Mirrors
    /// <see cref="Description"/> on load/refresh (via OnDescriptionChanged) so a
    /// refresh shows the persisted value; the user edits this independently
    /// until Save.</summary>
    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value)
    {
        // Keep the editable copy in sync whenever the persisted description
        // (re)loads. User edits to EditableDescription don't touch Description,
        // so there's no loop.
        EditableDescription = value ?? string.Empty;
    }

    public bool CanEditDescription => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanEditDescription))]
    private Task SaveDescription() => SaveDescriptionCoreAsync();

    [RelayCommand(CanExecute = nameof(CanEditDescription))]
    private Task ClearDescription()
    {
        EditableDescription = string.Empty;
        return SaveDescriptionCoreAsync();
    }

    // BUFFERED. Queues the COMMENT ON TABLE change (Opis tab already shows the
    // edited text via EditableDescription). Repeated description edits collapse
    // to a single queued statement. NO DDL runs here — Compile applies it.
    private Task SaveDescriptionCoreAsync()
    {
        if (_ddlExecutor is null) return Task.CompletedTask;
        ErrorMessage = null;
        var comment = string.IsNullOrWhiteSpace(EditableDescription) ? null : EditableDescription;
        var sql = DdlGenerator.BuildCommentTable(TableName, comment);
        RemovePendingByMarker(PendingDdlChangeKind.Other, "COMMENT ON TABLE");
        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.Other,
            Description = "Set table description",
            Sql = sql,
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// BUFFERED. Queues an ADD-FIELD change and shows the new column in the Pola
    /// grid as a pending-Added row. NO DDL runs here — the structure designer is
    /// "edit the model → Compile/Apply → auto-commit". Returns a completed task
    /// (kept Task-returning so the command's <c>await</c> stays unchanged).
    /// </summary>
    public Task ExecuteAddFieldAsync(FieldDefinition definition)
    {
        if (definition is null) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

        ErrorMessage = null;
        AddPendingAddField(definition);
        // Reflect in the working model: append a pending-Added row so the grid
        // shows the column the user just defined, before Compile.
        var display = BuildDisplayFieldInfo(definition);
        var row = new FieldRowViewModel(display, this) { PendingKind = PendingChangeKind.Added };
        EditableFields.Add(row);
        return Task.CompletedTask;
    }

    /// <summary>
    /// BUFFERED. Marks a column for deletion (kept visible, struck through) and
    /// queues a DROP-FIELD change. Dropping a not-yet-compiled pending-Added row
    /// instead removes it from the model and un-queues its ADD. NO DDL runs here.
    /// </summary>
    public Task ExecuteDropFieldAsync(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;

        ErrorMessage = null;
        var row = EditableFields.FirstOrDefault(r =>
            string.Equals(r.Name, fieldName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Original.Name, fieldName, StringComparison.OrdinalIgnoreCase));

        if (row is { PendingKind: PendingChangeKind.Added })
        {
            // Un-add: a column that was only queued (never in the catalog) just
            // disappears, and its queued ADD is removed — no DROP needed.
            RemovePendingAddField(row.Original.Name);
            row.Detach();
            EditableFields.Remove(row);
            if (ReferenceEquals(SelectedFieldRow, row)) SelectedFieldRow = null;
            return Task.CompletedTask;
        }

        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.DropField,
            Description = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditDescriptionDropFormat, fieldName),
            Sql = DdlGenerator.BuildDropField(TableName, fieldName),
        });
        if (row is not null) row.PendingKind = PendingChangeKind.Dropped;
        return Task.CompletedTask;
    }

    // Builds a display-only FieldInfo from a dialog FieldDefinition so a
    // pending-Added column can be wrapped in a FieldRowViewModel and shown in
    // the Pola grid before Compile. Position is appended past the current rows.
    private FieldInfo BuildDisplayFieldInfo(FieldDefinition def)
    {
        // FormatTypeOrDomain returns the DOMAIN NAME when a domain is used — which the
        // grid's Type/Size/Scale cells can't render (not a base type). Show the domain's
        // RESOLVED type instead (display parity with the inline domain mirror, #3); the
        // Domain column still drives the generated DDL.
        var displayType = DdlGenerator.FormatTypeOrDomain(def);
        if (!string.IsNullOrWhiteSpace(def.Domain))
        {
            foreach (var d in AvailableDomains)
            {
                if (string.Equals(d.Name, def.Domain, System.StringComparison.OrdinalIgnoreCase))
                {
                    displayType = d.Type;
                    break;
                }
            }
        }
        return new FieldInfo
        {
            Position = EditableFields.Count,
            Name = def.Name,
            Type = displayType,
            NotNull = def.NotNull,
            DefaultValue = string.IsNullOrWhiteSpace(def.DefaultValue) ? null : def.DefaultValue,
            Description = string.IsNullOrWhiteSpace(def.Description) ? null : def.Description,
            Domain = string.IsNullOrWhiteSpace(def.Domain) ? null : def.Domain,
            ComputedSource = string.IsNullOrWhiteSpace(def.ComputedExpression) ? null : def.ComputedExpression,
            IsPrimaryKey = def.PrimaryKey,
        };
    }

    // Removes the queued ADD-FIELD change for a column being un-added (matches on
    // the quoted field name the BuildAddField statement emits).
    private void RemovePendingAddField(string fieldName)
    {
        var marker = $"ADD \"{fieldName.Replace("\"", "\"\"")}\"";
        for (int i = PendingChanges.Count - 1; i >= 0; i--)
        {
            if (PendingChanges[i].Kind == PendingDdlChangeKind.AddField
                && PendingChanges[i].Sql.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                PendingChanges.RemoveAt(i);
                break;
            }
        }
    }

    // Force the next EnsureLoadedAsync to re-fetch fields/constraints/indexes/DDL
    // from the live catalog. Called after Add / Drop / Compile so the Pola grid
    // reflects the new structure immediately. Public surface for the view to
    // call after an external structural change (rollback, manual refresh).
    // Snapshots active sub-tab, selected-field name, sort column/direction,
    // current page, and selected-row PK values before discarding _loadTask;
    // restores them after the re-fetch completes so the user lands on the
    // same row they left.
    // Coalesces concurrent refreshes: when one is already running (e.g. Compile's
    // refresh hasn't finished when the post-commit refresh fires), the second call
    // joins the in-flight task instead of stacking a duplicate full reload.
    private Task? _refreshInFlight;

    public Task RefreshStructureAsync(System.Threading.CancellationToken ct = default)
    {
        if (_refreshInFlight is { } running)
        {
            Diagnostics.RefreshTrace.Log("RefreshStructure", $"coalesced (in-flight) table={TableName}");
            return running;
        }
        var task = RefreshStructureCoreAsync(ct);
        _refreshInFlight = task;
        return AwaitAndClearRefresh(task);
    }

    private async Task AwaitAndClearRefresh(Task task)
    {
        try { await task.ConfigureAwait(true); }
        finally { _refreshInFlight = null; }
    }

    private async Task RefreshStructureCoreAsync(System.Threading.CancellationToken ct)
    {
        Diagnostics.RefreshTrace.Log("RefreshStructure", $"begin table={TableName}");

        // Snapshot — capture by VALUE before the collections are cleared during the
        // structure reload's Fields/Constraints/Indexes re-population steps.
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

        // Remember the column SET so we can decide whether the data preview needs a
        // (potentially very expensive) reload — see below.
        var oldColumns = Fields.Select(f => f.Name).ToList();

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await LoadStructureCoreAsync(ct).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
        // Structure is current. Mark the load satisfied so EnsureLoadedAsync stays
        // idempotent (it must not re-run the full LoadAsync, which would re-issue
        // the data-preview SELECT *). ReloadDataPreviewAsync awaits this safely.
        _loadTask = Task.CompletedTask;

        // Clear the pending DDL queue — any user-pending edits no longer describe
        // the current schema (rolled back, compiled, or refreshed out from under them).
        PendingChanges.Clear();
        RestoreStructureSnapshot(snap);

        // Reload the data preview ONLY when the column SET actually changed
        // (add/drop/rename). A type/length/precision edit, a NOT NULL/default
        // toggle, or a constraint/index change keeps the same columns — re-running
        // `SELECT *` there is pure waste and, on a table with a MOD/computed column
        // or an ORDER BY over a non-indexed column, is the source of the post-commit
        // refresh storm (thousands of computed-column evaluations).
        var newColumns = Fields.Select(f => f.Name).ToList();
        if (!ColumnSetEqual(oldColumns, newColumns))
        {
            Diagnostics.RefreshTrace.Log("RefreshStructure", $"column set changed → reload data preview table={TableName}");
            await ReloadDataPreviewAsync(ct).ConfigureAwait(true);
        }
        else
        {
            Diagnostics.RefreshTrace.Log("RefreshStructure", $"column set unchanged → SKIP data preview table={TableName}");
        }

        // Notify the view (column-width preservation lives there).
        StructureRefreshed?.Invoke(this, System.EventArgs.Empty);
        Diagnostics.RefreshTrace.Log("RefreshStructure", $"end table={TableName}");
    }

    private static bool ColumnSetEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
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
        // refresh). Use this ONLY after a METADATA-lane commit/rollback (DDL may
        // have changed the schema). After a DATA-lane commit/rollback the schema
        // is unchanged — use RefreshDataAfterTransactionAsync instead.
        return RefreshStructureAsync(ct);
    }

    /// <summary>
    /// Lightweight post-transaction refresh for a DATA-lane commit/rollback: reloads
    /// ONLY the data preview, never the structure. A data edit (UPDATE/INSERT/DELETE)
    /// can't change the schema, so re-fetching fields/constraints/indexes/dependencies/
    /// DDL is pure waste — that full reload is what froze the UI for seconds and, while
    /// it tore down and rebuilt the Fields model, transiently dropped
    /// <see cref="HasPrimaryKey"/> to false ("Table has no primary key — only INSERT is
    /// available"). <see cref="ReloadDataPreviewAsync"/> keeps Fields/PK intact
    /// (EnsureLoadedAsync is idempotent — it does NOT reset the structure load) and is
    /// essential on rollback (the grid's optimistic writes must be reverted to the real
    /// DB values).
    /// </summary>
    public Task RefreshDataAfterTransactionAsync(System.Threading.CancellationToken ct = default)
        => ReloadDataPreviewAsync(ct);
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

        // BUFFERED: marks the column for deletion in the working model + queues a
        // DROP-FIELD change. No DDL runs until ⚡ Compile.
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
    /// BUFFERED. Reorders the column in the working model (visible immediately in
    /// the Pola grid) and queues an ALTER … POSITION change. NO DDL runs here.
    /// </summary>
    public Task ExecuteMoveAsync(string fieldName, int oneBasedPosition)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return Task.CompletedTask;
        if (_ddlExecutor is null) return Task.CompletedTask;
        ErrorMessage = null;

        // Visually reorder the bound EditableFields so the grid reflects the move.
        var row = EditableFields.FirstOrDefault(r =>
            string.Equals(r.Original.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            var target = Math.Clamp(oneBasedPosition - 1, 0, EditableFields.Count - 1);
            var current = EditableFields.IndexOf(row);
            if (current >= 0 && current != target)
            {
                EditableFields.RemoveAt(current);
                EditableFields.Insert(target, row);
                SelectedFieldRow = row;
            }
        }

        PendingChanges.Add(new PendingDdlChange
        {
            Kind = PendingDdlChangeKind.MoveField,
            Description = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditDescriptionMoveFormat, fieldName, oneBasedPosition),
            Sql = DdlGenerator.BuildMoveField(TableName, fieldName, oneBasedPosition),
        });
        return Task.CompletedTask;
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

    // ─── ISavableObjectEditor (Save-and-close / Save-and-disconnect WorkGuard) ──
    // Thin adapter over CompileAsync (applies the queued PendingChanges — the ONE save path); not a second mechanism.
    public async Task<EditorSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        await CompileAsync().ConfigureAwait(true);
        return ErrorMessage is null ? new EditorSaveResult(true, null) : new EditorSaveResult(false, ErrorMessage);
    }

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private async Task CompileAsync()
    {
        // Reports instead of exiting silently (Seam 6b) — see the contract on ISavableObjectEditor.
        if (_ddlExecutor is null)
        {
            ErrorMessage = UiStrings.NoConnectionMessage;
            return;
        }
        // Diff-based editor: an empty queue means there is genuinely nothing to write (and it is exactly
        // !HasPendingChanges, i.e. not dirty, so the WorkGuard never even asks), so this stays an ordinary
        // no-op rather than a reported failure (Seam 6b — the documented exception).
        if (PendingChanges.Count == 0) return;

        ErrorMessage = null;
        // Apply the WHOLE batch in ONE autonomous, auto-committed transaction:
        // join every queued statement and hand it to the executor once.
        // FirebirdDdlExecutor splits on top-level ';' (BEGIN/END aware) and runs
        // all statements in a single transaction — so a multi-step structural
        // edit is atomic (all-or-nothing). On failure the queue is left intact so
        // the user can fix the offending change and Compile again.
        var batch = string.Join(
            ";\n",
            PendingChanges.Select(c => c.Sql.TrimEnd().TrimEnd(';')));
        try
        {
            await _ddlExecutor.ExecuteAsync(batch).ConfigureAwait(true);
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

        // Full success — RefreshStructureAsync clears the pending queue and
        // re-reads Fields / Constraints / Indexes / DDL from the live catalog,
        // so the grids drop their pending markers and show the committed shape.
        // (DDL auto-committed; there is no metadata Commit/Rollback step.)
        await RefreshStructureAsync().ConfigureAwait(true);
    }

    public bool CanDiscardPending => HasPendingChanges;

    /// <summary>
    /// Discards every queued structural change and reprojects the grids back to
    /// the live-catalog (DB-truth) state — pending-Added rows vanish, dropped
    /// rows un-strike, modified rows reset — WITHOUT a database round-trip.
    /// Confirms first so an accidental click never throws away uncompiled work.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDiscardPending))]
    private async Task DiscardPendingChanges()
    {
        if (PendingChanges.Count == 0) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.FieldEditDiscardConfirmTitle,
            Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FieldEditDiscardConfirmFormat, TableName),
            ConfirmLabel = UiStrings.FieldEditDiscardConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        ErrorMessage = null;
        PendingChanges.Clear();

        // Fields: rebuild the editable wrappers from Fields (the catalog truth) —
        // drops pending-Added rows, resets Dropped/Modified markers + edited values.
        RebuildEditableFields();
        SelectedFieldRow = null;

        // Constraints / Indexes: remove pending-Added rows; un-mark Dropped ones.
        for (int i = Constraints.Count - 1; i >= 0; i--)
        {
            var c = Constraints[i];
            if (c.PendingState == PendingChangeKind.Added) Constraints.RemoveAt(i);
            else if (c.PendingState == PendingChangeKind.Dropped) { c.PendingState = PendingChangeKind.None; Constraints[i] = c; }
        }
        for (int i = Indexes.Count - 1; i >= 0; i--)
        {
            var x = Indexes[i];
            if (x.PendingState == PendingChangeKind.Added) Indexes.RemoveAt(i);
            else if (x.PendingState == PendingChangeKind.Dropped) { x.PendingState = PendingChangeKind.None; Indexes[i] = x; }
        }

        // Description back to the catalog value.
        EditableDescription = Description ?? string.Empty;
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
        OnPropertyChanged(nameof(CanDiscardPending));
        OnPropertyChanged(nameof(DdlWithPendingPreview));
        CompileCommand.NotifyCanExecuteChanged();
        DiscardPendingChangesCommand.NotifyCanExecuteChanged();
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
