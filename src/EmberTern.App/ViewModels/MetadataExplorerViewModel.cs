using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Commands;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class MetadataExplorerViewModel : ViewModelBase
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly FirebirdMetadataReader _reader;
    private readonly SidebarFlatController _sidebar;

    public MetadataExplorerViewModel(FirebirdConnectionService connectionService, FirebirdMetadataReader reader)
    {
        _connectionService = connectionService;
        _reader = reader;
        Connections = new ObservableCollection<ConnectionNodeViewModel>();
        RootNodes = new ObservableCollection<object>();
        // ⚠⚠ SAMA POPRAWNA WARTOŚĆ TO NIE JEST ODŚWIEŻONY EKRAN. `ShowEmptyState` liczy się z `RootNodes`,
        // a wiązanie odpytuje właściwość WYŁĄCZNIE po `PropertyChanged` — bez tej subskrypcji stan pusty
        // zniknąłby dopiero przy następnej zmianie czegoś innego. Ten sam błąd złapały testy w M3.3b i M3b.2,
        // za każdym razem dopiero po podsadzeniu naruszenia; asercją jest tu POWIADOMIENIE, nie wartość.
        RootNodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyState));
        // Flat projection of RootNodes for the single-VSP sidebar ListBox (replaces the
        // nested-VSP TreeView). Created once; it tracks RootNodes.CollectionChanged so it
        // survives ReloadConnections (which clears + refills the same instance).
        _sidebar = new SidebarFlatController(
            RootNodes,
            childrenSelector: SidebarChildren,
            isContainer: SidebarIsContainer,
            hasChildren: SidebarHasChildren,
            isExpanded: SidebarExpanded,
            setExpanded: SidebarSetExpanded,
            isVisible: SidebarVisible);
        // Refresh is only meaningful while a database is connected. The event fires
        // on the async-continuation thread (gotcha #11), so marshal the CanExecute
        // re-evaluation onto the UI thread.
        _connectionService.ActiveConnectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshCommand.NotifyCanExecuteChanged);
    }

    // Refresh only makes sense with an active connection (matches the enable/disable
    // behaviour of the other connection-dependent toolbar actions).
    private bool CanRefresh => _connectionService.IsConnected;

    // Flat list of every loaded ConnectionNodeViewModel, regardless of whether
    // the node currently sits inside a folder or at the root. Populated by
    // MainWindowViewModel.ReloadConnections alongside RootNodes.
    public ObservableCollection<ConnectionNodeViewModel> Connections { get; }

    // The tree's actual ItemsSource — a mix of FolderNodeViewModel and
    // root-level ConnectionNodeViewModel instances, ordered by SortOrder.
    public ObservableCollection<object> RootNodes { get; }

    // The flattened, single-level projection the sidebar ListBox binds to. Same nodes,
    // same order, only the currently-visible (expanded) rows — a stable-extent single VSP.
    public ObservableCollection<SidebarRow> SidebarRows => _sidebar.Rows;

    /// <summary>
    /// Stan pusty paska bocznego (M5 / M‑3 klasa A): panel nie ma NICZEGO w korzeniu, czyli pierwsze
    /// uruchomienie albo usunięcie wszystkiego.
    /// </summary>
    /// <remarks>
    /// ⭐ Liczy się z <see cref="RootNodes"/>, a NIE z <see cref="SidebarRows"/> — i to jest różnica
    /// merytoryczna, nie stylistyczna. `SidebarRows` jest projekcją FILTROWANĄ, więc bramka na niej
    /// pokazywałaby „dodaj połączenie" użytkownikowi, który ma połączenia i tylko wpisał filtr bez
    /// trafień. To są dwa różne stany puste i tylko pierwszy jest w zakresie M‑3.
    /// ⚠ Świadomie NIE liczy się z <see cref="Connections"/>: użytkownik z samymi folderami i zerem
    /// połączeń widzi foldery, więc panel nie jest pusty i podpowiedź pierwszego kroku byłaby szumem.
    /// </remarks>
    public bool ShowEmptyState => RootNodes.Count == 0;

    // Chevron click → flip the underlying node's expansion (drives the projection).
    public void ToggleSidebarRow(SidebarRow? row) => _sidebar.Toggle(row);

    /// <summary>
    /// Nawigacja pozioma klawiaturą — bliźniak <see cref="ToggleSidebarRow"/>. ⭐ Reguła żyje w JEDNYM
    /// miejscu (<see cref="SidebarFlatController.Navigate"/>) i obsługuje zarówno drzewo połączenia, jak
    /// i drzewa „Zależności", więc oba nie mogą się rozjechać.
    /// </summary>
    public SidebarRow? NavigateSidebarRow(SidebarRow row, bool forward) => _sidebar.Navigate(row, forward);

    // Node-access delegates for the flat controller (kept here so the node-type knowledge
    // stays with the explorer that owns the hierarchy).
    private static IEnumerable<object>? SidebarChildren(object node) => node switch
    {
        FolderNodeViewModel f => f.Connections,
        ConnectionNodeViewModel c => c.Children,
        MetadataNodeViewModel m when m.IsGroup => m.Children,
        _ => null,
    };

    // Structural: can this node host children? (drives subscription + recursion — a category
    // is a container even while empty, so its lazy populate is observed.)
    private static bool SidebarIsContainer(object node) => node switch
    {
        FolderNodeViewModel => true,
        ConnectionNodeViewModel => true,
        MetadataNodeViewModel m => m.IsGroup,
        _ => false,
    };

    // Does the node currently HAVE children? (drives the chevron — no expander for an empty
    // category, a disconnected connection, or a folder with no connections.)
    private static bool SidebarHasChildren(object node) => node switch
    {
        FolderNodeViewModel f => f.Connections.Count > 0,
        ConnectionNodeViewModel c => c.Children.Count > 0,
        MetadataNodeViewModel m => m.IsGroup && m.Children.Any(x => !x.IsPlaceholder),
        _ => false,
    };

    private static bool SidebarExpanded(object node) => node switch
    {
        FolderNodeViewModel f => f.IsExpanded,
        ConnectionNodeViewModel c => c.IsExpanded,
        MetadataNodeViewModel m => m.IsExpanded,
        _ => false,
    };

    private static void SidebarSetExpanded(object node, bool value)
    {
        switch (node)
        {
            case FolderNodeViewModel f: f.IsExpanded = value; break;
            case ConnectionNodeViewModel c: c.IsExpanded = value; break;
            case MetadataNodeViewModel m: m.IsExpanded = value; break;
        }
    }

    // Only metadata nodes are hidden by the filter (zero-match categories / non-matching
    // leaves); connections and folders are always shown.
    private static bool SidebarVisible(object node) => node is not MetadataNodeViewModel m || m.IsVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectSelectedCommand))]
    private ConnectionNodeViewModel? _selectedConnection;

    partial void OnSelectedConnectionChanged(ConnectionNodeViewModel? oldValue, ConnectionNodeViewModel? newValue)
    {
        // Toolbar Connect/Disconnect/Reconnect enabled-state depends on the selected
        // node's IsConnected. Resubscribe on selection change so flips invalidate
        // CanExecute on those commands.
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedConnectionPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedConnectionPropertyChanged;
        }
    }

    private void OnSelectedConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionNodeViewModel.IsConnected))
        {
            ConnectSelectedCommand.NotifyCanExecuteChanged();
            DisconnectSelectedCommand.NotifyCanExecuteChanged();
            ReconnectSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string FilterPlaceholder => UiStrings.MetadataFilterPlaceholder;
    public string RefreshTooltip => UiStrings.MetadataRefreshTooltip;

    /// <summary>
    /// Re-renders every caption the sidebar owns in the current language.
    ///
    /// <para>⚠⚠ <b>The whole tree was outside the language-change chain.</b>
    /// <c>MainWindowViewModel.OnLanguageChanged</c> re-published itself, its Performance panel and every open
    /// TAB — the metadata explorer is held by the window, not by a tab, so nothing reached it. Two visible
    /// symptoms, one cause: the filter's placeholder (a computed property nobody notified) and the category
    /// names (stored values on the nodes), both correct only after a restart.</para>
    ///
    /// <para>⭐ Two halves, deliberately: a nudge for what this view model computes, and a walk for what the
    /// NODES store — the node decides which of its captions is a word and which is the user's own object
    /// name.</para>
    /// </summary>
    internal void RefreshLocalizedText()
    {
        OnPropertyChanged(string.Empty);

        foreach (var node in RootNodes)
        {
            switch (node)
            {
                case FolderNodeViewModel folder:
                    foreach (var connection in folder.Connections)
                    {
                        RefreshConnection(connection);
                    }

                    break;

                case ConnectionNodeViewModel connection:
                    RefreshConnection(connection);
                    break;
            }
        }

        static void RefreshConnection(ConnectionNodeViewModel connection)
        {
            foreach (var category in connection.Children)
            {
                category.RefreshLocalizedText();
            }
        }
    }

    public event Action<MetadataObject>? OpenDdlRequested;
    public event Action<string>? CopyNameRequested;
    public event Action<string>? StatusReported;
    // Raised whenever the loaded object set grows/changes — a category finished loading (prefetch on
    // connect, a user expand, or a refresh). The editor's language service listens (coalesced) and
    // rebuilds its semantic model so newly-loaded objects (notably views + selectable procedures used
    // in FROM) start resolving for highlight / Ctrl-nav / Quick Info. Fires on the UI thread
    // (LoadGroupAsync runs there).
    public event Action? ObjectsChanged;

    // Monotonic generation of the loaded-object set — bumped every time ObjectsChanged fires (a
    // category finished loading). The editor reads this to tell whether its cached semantic model
    // was built against an older metadata state: a deliberate completion trigger (Ctrl+Space) rebuilds
    // when the generation moved, even if the document text didn't. This closes the "IntelliSense is
    // dead until I edit" gap — the model's synchronous refresh was previously text-version-gated only,
    // so metadata that loaded after the model was first built (prefetch on connect) never reached a
    // Ctrl+Space unless a keystroke bumped the text version. See RaiseObjectsChanged.
    private int _objectsGeneration;

    /// <summary>The current generation of the loaded-object set (see <see cref="_objectsGeneration"/>).
    /// Increases whenever a category's objects load. Read by the SQL editor to decide whether a
    /// metadata-only change should force a semantic-model rebuild on the next deliberate trigger.</summary>
    public int ObjectsGeneration => _objectsGeneration;

    // Bumps the generation, then raises ObjectsChanged. One entry point so the two can never drift.
    private void RaiseObjectsChanged()
    {
        _objectsGeneration++;
        ObjectsChanged?.Invoke();
    }

    /// <summary>
    /// Raised at the START of a manual metadata refresh: the SCHEMA may have changed, so every per-object
    /// cache read from it (columns, routine parameters, object detail) is now suspect and must be dropped.
    /// A distinct signal from <see cref="ObjectsChanged"/>, which says only that the loaded object LIST grew.
    /// <para>
    /// ⭐⭐ ITS ABSENCE WAS THE SECOND HALF OF THE REPORTED "diagnostics do not refresh after a metadata
    /// refresh" (S-2, 2026-08-05). <see cref="RefreshAsync"/> dropped the object-NAME index
    /// (<see cref="InvalidateNameCache"/>) and nothing else; the column / routine-parameter / object-detail
    /// caches were cleared only when the user switched CONNECTION. So a refresh rebuilt every open editor's
    /// semantic model — against the same stale columns. A column added to a table stayed "unknown" for the
    /// rest of the session, on every open tab, no matter how often the user refreshed.
    /// </para>
    /// <para>
    /// ⚠ Raised BEFORE the reload, not after: the per-category <see cref="ObjectsChanged"/> signals fire
    /// during the reload and each schedules a model rebuild, so a cache cleared afterwards would be cleared
    /// behind a rebuild that had already read it.
    /// </para>
    /// <para>
    /// ⚠ Dropping the caches is only safe because the diagnostics engine can now tell "not loaded" from
    /// "absent" (<c>ISqlMetadataProvider.KnowsColumns</c>). Without that, this event alone would turn every
    /// refresh into the very false-positive storm the other half of S-2 removed — the two halves are one fix.
    /// </para>
    /// </summary>
    public event Action? SchemaInvalidated;

    /// <summary>Raised ONCE when a connection's background prefetch has finished loading every metadata
    /// category — the definitive "metadata is complete" lifecycle event (Package 5 closure). Unlike the
    /// per-category <see cref="ObjectsChanged"/> (which the editor debounces for incremental updates),
    /// this is the authoritative signal that every open SQL editor should now do its final rebuild +
    /// full warm (columns + object detail + routine parameters) and publish one complete Semantic Model.
    /// Fires on the UI thread (prefetch resumes there).</summary>
    public event Action? MetadataReady;

    /// <summary>Raises <see cref="MetadataReady"/>. Called by the connection node once its prefetch
    /// loop completes.</summary>
    internal void NotifyMetadataReady() => MetadataReady?.Invoke();

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐ POSTĘP PREFETCHU KATEGORII — sekcja postępu paska statusu (M3b.2, §19.34)
    //
    // ⚠⚠ Dlaczego NIE `MetadataReady` jako sygnał końca dla paska: to zdarzenie **nie nastąpi** przy
    // nieudanym połączeniu (nie ma wtedy `ActiveConnectionChanged`, więc nie ma prefetchu), przy
    // rozłączeniu w trakcie, ani gdy `LoadGroupAsync` rzuci coś poza dwoma wyjątkami, które łapie.
    // Każda z tych ścieżek zostawiłaby zapalony pasek — pułapka §19.7.4. Flaga poniżej gaśnie we
    // WŁASNYM `finally`, więc żadna ścieżka wyjścia nie jest w stanie jej pominąć.
    // ⛔ `MetadataReady` zostaje bez zmian i nadal służy swojemu celowi (edytory przebudowują model);
    // pasek statusu go nie używa.
    //
    // ⚠ Tylko PREFETCH PO POŁĄCZENIU. `RefreshAsync` wykonuje tę samą pracę i ma własne `try/finally`,
    // więc raportowanie byłoby darmowe — ale użytkownik świadomie zawęził zakres do połączenia
    // (2026-08-04). ⛔ Nie podłączać odświeżania bez jego decyzji.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Czy trwa prefetch kategorii po połączeniu.</summary>
    [ObservableProperty]
    private bool _isLoadingMetadata;

    /// <summary>Ile kategorii jest już wczytanych, i ile ich jest — licznik postępu fazy 3.
    /// ⭐ Suma jest ZNANA (<c>CategoryOrder.Length</c>), więc to jedyne źródło postępu w tej ścieżce,
    /// które uczciwie wypełnia tryb procentowy.</summary>
    [ObservableProperty]
    private int _metadataCategoriesLoaded;

    [ObservableProperty]
    private int _metadataCategoriesTotal;

    /// <summary>Otwiera fazę 3 dla paska statusu. Wołane przez węzeł połączenia, który jest jej właścicielem.
    /// ⚠ Musi być sparowane z <see cref="EndMetadataPrefetch"/> w <c>finally</c>.</summary>
    internal void BeginMetadataPrefetch(int total)
    {
        MetadataCategoriesTotal = total;
        MetadataCategoriesLoaded = 0;
        IsLoadingMetadata = true;
    }

    internal void ReportMetadataPrefetch(int loaded) => MetadataCategoriesLoaded = loaded;

    internal void EndMetadataPrefetch()
    {
        IsLoadingMetadata = false;
        MetadataCategoriesLoaded = 0;
        MetadataCategoriesTotal = 0;
    }
    // Tree object-lifecycle dispatch. The owner (MainWindowViewModel) REUSES its existing
    // New*/detail-editor/DROP/Execute flows — these are just the tree's entry points.
    public event Action<MetadataObjectKind>? NewObjectRequested;
    public event Action<MetadataObject>? DeleteObjectRequested;
    public event Action<MetadataObject>? ExecuteProcedureRequested;
    public event Action<MetadataObject>? DebugProcedureRequested;
    public event Action<MetadataObject>? DebugTriggerRequested;
    public event Action<MetadataObject>? DebugFunctionRequested;
    public event Action<MetadataObjectKind>? RecompileGroupRequested;
    // Single trigger activate/deactivate (bool = activate).
    public event Action<MetadataObject, bool>? SetObjectActiveRequested;
    // Bulk trigger activate/deactivate over the visible (filtered) set or all.
    public event Action<TriggerBulkRequest>? BulkSetActiveRequested;

    /// <summary>Reflect a trigger activate/deactivate in the tree WITHOUT a full <see cref="RefreshAsync"/>
    /// — flip the matching LOADED trigger leaves in place. No collection change → no reproject, so the
    /// sidebar keeps its scroll position, selection, and expanded groups (the whole point: single/batch
    /// trigger ops no longer make the tree jump). <paramref name="names"/> null = every loaded trigger
    /// leaf (scope All); otherwise only the named ones (case-insensitive). Unloaded groups have no leaf
    /// nodes yet → nothing to update (they fetch fresh state on first expand). The schema is unchanged
    /// (same triggers, only active flags), so the name cache and filter are left intact.</summary>
    internal void ApplyTriggerActiveStateInPlace(IEnumerable<string>? names, bool active)
    {
        var set = names is null ? null : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var connection in Connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }
            foreach (var group in connection.Children)
            {
                if (!group.IsGroup || group.Kind != MetadataObjectKind.Trigger)
                {
                    continue;
                }
                foreach (var leaf in group.AllLeaves)
                {
                    if (leaf.Object is { } o && (set is null || set.Contains(o.Name)))
                    {
                        leaf.SetActiveState(active);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reflect a newly CREATED object in the tree without a full <see cref="RefreshAsync"/> — insert one leaf
    /// into its category at the sorted position, in place.
    /// <para>
    /// ⭐ <b>Why this exists rather than a 21st <c>RefreshAsync</c> call.</b> A full refresh re-reads all 13
    /// categories and replaces every category's leaves one <c>Add</c> at a time, which re-projects the sidebar
    /// per added leaf — measured at over a second on the UI thread with one big category expanded. The caller
    /// here already KNOWS what changed, so it says so: one insert, no catalog round trip, and the scroll
    /// position, selection and expanded groups survive. Same idea as
    /// <see cref="ApplyTriggerActiveStateInPlace"/>, which already did this for trigger activation.
    /// </para>
    /// <para>
    /// A category that is not loaded has no leaves to insert into — its <c>(N)</c> label is bumped instead, so
    /// it stays truthful until its first expand fetches the real list.
    /// </para>
    /// </summary>
    internal void ApplyObjectAddedInPlace(MetadataObject obj)
    {
        var filter = (FilterText ?? string.Empty).Trim();
        Func<MetadataNodeViewModel, bool>? displayed = filter.Length == 0
            ? null
            : leaf => !leaf.IsPlaceholder && leaf.GroupLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);

        foreach (var group in LoadedGroupsOfKind(obj.Kind))
        {
            if (!group.IsLoaded)
            {
                // Unloaded: no leaf list to touch, but the count is on screen and must stay right.
                group.Count = (group.Count ?? 0) + 1;
                AddToNameCache(group, obj.Name);
                continue;
            }

            // Idempotent: a second report of the same object must not double the leaf (a refresh may
            // already have picked it up).
            if (group.HasLeaf(obj.Name)) continue;

            group.InsertLeafInPlace(MetadataNodeViewModel.CreateLeaf(this, obj), displayed);
            group.Count = group.AllLeaves.Count;
            RefreshFilterCounters(group, displayed);
            AddToNameCache(group, obj.Name);
        }

        // The loaded object set changed — open editors rebuild their semantic model, so the new object
        // starts resolving for highlighting / Ctrl-nav / completion without waiting for a refresh.
        RaiseObjectsChanged();
    }

    /// <summary>The counterpart of <see cref="ApplyObjectAddedInPlace"/>: an object the application itself
    /// DROPPED (the import undoing a table it created) leaves the tree the same way it entered it.</summary>
    internal void ApplyObjectRemovedInPlace(MetadataObject obj)
    {
        var filter = (FilterText ?? string.Empty).Trim();
        Func<MetadataNodeViewModel, bool>? displayed = filter.Length == 0
            ? null
            : leaf => !leaf.IsPlaceholder && leaf.GroupLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);

        foreach (var group in LoadedGroupsOfKind(obj.Kind))
        {
            if (!group.IsLoaded)
            {
                group.Count = Math.Max(0, (group.Count ?? 0) - 1);
                RemoveFromNameCache(group, obj.Name);
                continue;
            }

            if (!group.RemoveLeafInPlace(obj.Name)) continue;

            group.Count = group.AllLeaves.Count;
            RefreshFilterCounters(group, displayed);
            RemoveFromNameCache(group, obj.Name);
        }

        RaiseObjectsChanged();
    }

    // Every category node of this kind under a CONNECTED connection (there is one active connection, but
    // the loop mirrors ApplyTriggerActiveStateInPlace rather than assuming it).
    private IEnumerable<MetadataNodeViewModel> LoadedGroupsOfKind(MetadataObjectKind kind)
    {
        foreach (var connection in Connections)
        {
            if (!connection.IsConnected) continue;
            foreach (var group in connection.Children)
            {
                if (group.IsGroup && group.Kind == kind) yield return group;
            }
        }
    }

    // While a filter is active the label shows the MATCH count and a zero-match category hides; an in-place
    // change must keep both honest. No filter → the two are already null/visible and stay that way.
    private static void RefreshFilterCounters(MetadataNodeViewModel group, Func<MetadataNodeViewModel, bool>? displayed)
    {
        if (displayed is null) return;
        group.FilterMatchCount = group.Children.Count;
        group.IsVisible = group.Children.Count > 0;
    }

    // The name index feeds the filter and type-ahead for categories that were never expanded. We know the one
    // name that changed, so patch it rather than dropping the whole index (which would cost 13 catalog reads
    // on the next keystroke). A cache that was never built stays unbuilt — it will read the new state anyway.
    private void AddToNameCache(MetadataNodeViewModel group, string name)
    {
        if (_nameCache is null || !_nameCache.TryGetValue(group, out var names)) return;
        if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return;
        var updated = new List<string>(names) { name };
        updated.Sort(StringComparer.Ordinal);
        _nameCache[group] = updated;
    }

    private void RemoveFromNameCache(MetadataNodeViewModel group, string name)
    {
        if (_nameCache is null || !_nameCache.TryGetValue(group, out var names)) return;
        _nameCache[group] = names
            .Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Suspends the sidebar's flat projection for a bulk load, then re-projects ONCE. Pair the two in a
    /// <c>try/finally</c>; nesting is safe, so an outer loop and the individual loads inside it may both use it.
    /// <para>
    /// ⭐ <b>Why the load path needs it.</b> Replacing a category's leaves is a mass mutation — <c>Clear</c>
    /// then one <c>Add</c> per object — and each <c>Add</c> re-splices the whole child block, so the projection
    /// is quadratic in the object count: measured 878 ms for one 2 400-leaf category, and 1 142 ms for a whole
    /// refresh with a single category expanded, all on the UI thread. The guard already existed and was applied
    /// to the FILTER path only; this is the same guard on the other mass mutation.
    /// </para>
    /// </summary>
    internal void BeginSidebarBulkUpdate() => _sidebar.BeginUpdate();

    internal void EndSidebarBulkUpdate() => _sidebar.EndUpdate();

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        Diagnostics.RefreshTrace.Log("RefreshTree", "begin");
        // The schema may have changed under us, so every per-object cache read from it is suspect. Announced
        // FIRST: the per-category ObjectsChanged signals below each schedule a model rebuild, and a cache
        // cleared afterwards would be cleared behind a rebuild that already read it. See SchemaInvalidated.
        SchemaInvalidated?.Invoke();
        // ⭐ ONE projection for the whole refresh. Each LoadGroupAsync guards itself too (so an expand or the
        // connect-time prefetch is covered wherever it is called from), but the guard is nesting-safe, so
        // wrapping the loop collapses 13 re-projections into one.
        BeginSidebarBulkUpdate();
        try
        {
            // Only connected nodes have anything to refresh.
            foreach (var connection in Connections)
            {
                if (!connection.IsConnected)
                {
                    continue;
                }

                foreach (var group in connection.Children)
                {
                    if (!group.IsGroup)
                    {
                        continue;
                    }

                    // Lazy model: a group is either LOADED (user expanded it → it holds the
                    // real leaf list) or NOT loaded (still showing only its COUNT). Reload
                    // the full list for loaded/expanded groups; for the rest just re-fetch
                    // the COUNT so the "(N)" label stays current without dragging the whole
                    // list back. LoadGroupAsync clears+repopulates only AFTER its fetch
                    // succeeds, so a transient error keeps the old data instead of blanking.
                    if (group.IsLoaded || group.IsExpanded)
                    {
                        await LoadGroupAsync(group).ConfigureAwait(true);
                    }
                    else
                    {
                        await LoadCountAsync(group).ConfigureAwait(true);
                    }
                }
            }
        }
        finally
        {
            EndSidebarBulkUpdate();
        }

        // Schema may have changed — drop the cached object-name index so the next
        // filter / type-ahead refetches.
        InvalidateNameCache();
        await ApplyFilterAsync().ConfigureAwait(true);
        Diagnostics.RefreshTrace.Log("RefreshTree", "end");
    }

    /// <summary>
    /// Fetches ONLY the object count for a category and stamps it on the group label
    /// (<c>Tables (2356)</c>) without loading the leaf list. Called once per category
    /// right after connect (see <see cref="ConnectionNodeViewModel.LoadCategoriesAsync"/>)
    /// so the user gets the full category breakdown immediately while the potentially
    /// thousands-strong leaf lists stay deferred to first expansion. Never calls
    /// <see cref="ApplyFilter"/> — counts load with an empty filter at connect, and
    /// re-running the filter per category would be O(n·categories) for no benefit.
    /// </summary>
    internal async Task LoadCountAsync(MetadataNodeViewModel group)
    {
        if (!group.IsGroup || group.IsLoaded || group.IsLoading)
        {
            return;
        }

        if (!_connectionService.IsConnected)
        {
            return;
        }

        try
        {
            group.Count = await _reader.CountAsync(group.Kind).ConfigureAwait(true);
        }
        catch (MetadataReadException)
        {
            // Unsupported on this FB version (e.g. Packages/Users on 2.5) or no
            // privilege — leave the count blank; the category stays expandable to retry.
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Prosi widok o zaznaczenie wiersza i PRZEWINIĘCIE go w pole widzenia.
    /// <para>⚠ Przewinięcie jest sprawą widoku, nie modelu: to płaska lista wirtualizująca, więc
    /// „pokaż" znaczy „wywołaj <c>ScrollIntoView</c> na kontrolce". VM ustala WIERSZ, widok go pokazuje —
    /// ta sama granica, którą trzyma cały ten etap.</para>
    /// </summary>
    public event Action<SidebarRow>? RevealRowRequested;

    /// <summary>
    /// Rozwija właściwą kategorię, znajduje obiekt po nazwie i prosi widok o pokazanie go.
    ///
    /// <para>⭐ <b>Rozwinięcie musi być POCZEKANE, a nie tylko zażądane.</b> Ustawienie
    /// <c>IsExpanded</c> odpala <see cref="LoadGroupAsync"/> jako „fire and forget"
    /// (<c>_ = _owner.LoadGroupAsync(this)</c>), więc szukanie liścia zaraz po tym trafiłoby
    /// w kategorię, która jeszcze nie ma dzieci — i pozycja menu po cichu nic by nie robiła przy
    /// pierwszym użyciu, a działała przy drugim. Dlatego ładowanie jest tu wywołane wprost.</para>
    ///
    /// <para>⚠ Nazwy obiektów Firebirda porównujemy bez uwzględniania wielkości liter: identyfikator
    /// niecytowany jest składany do wersalików, a nazwa zakładki bierze się z katalogu.</para>
    /// </summary>
    internal async Task<bool> RevealObjectAsync(MetadataObjectKind kind, string name)
    {
        if (!_connectionService.IsConnected || string.IsNullOrEmpty(name)) return false;

        var connection = Connections.FirstOrDefault(c => c.IsConnected);
        if (connection is null) return false;

        var group = connection.Children.FirstOrDefault(g => g.IsGroup && g.Kind == kind);
        if (group is null) return false;

        connection.IsExpanded = true;

        if (!group.IsLoaded && !group.IsLoading)
        {
            await LoadGroupAsync(group).ConfigureAwait(true);
        }

        group.IsExpanded = true;

        var leaf = group.Children.FirstOrDefault(
            c => c.IsActionable
                 && string.Equals(c.Object?.Name, name, StringComparison.OrdinalIgnoreCase));
        if (leaf is null) return false;

        // ⚠ Wiersz szukamy PO rozwinięciu, bo dopiero wtedy istnieje w płaskiej projekcji — a jeśli
        //   projekcja jeszcze go nie ma (filtr!), nie udajemy sukcesu.
        var row = SidebarRows.FirstOrDefault(r => ReferenceEquals(r.Node, leaf));
        if (row is null) return false;

        SelectedNode = leaf;
        RevealRowRequested?.Invoke(row);
        return true;
    }

    internal async Task LoadGroupAsync(MetadataNodeViewModel group)
    {
        if (!group.IsGroup || group.IsLoading)
        {
            return;
        }

        if (!_connectionService.IsConnected)
        {
            return;
        }

        group.IsLoading = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var objects = await _reader.ListAsync(group.Kind).ConfigureAwait(true);
            Diagnostics.ScrollTrace.Rebuild($"LoadGroup {group.Kind} ({objects.Count} leaves — Children rebuilt)");
            // ⭐ The mass mutation goes under the bulk guard — see BeginSidebarBulkUpdate. Without it every
            // one of the N Adds below re-splices the whole child block (Θ(N²) on the UI thread); with it the
            // sidebar re-projects once. The guard is held across the filter re-application as well, so one
            // load costs exactly one projection instead of two.
            BeginSidebarBulkUpdate();
            try
            {
                // SetLeaves loads the master list AND sets Children to the full set; the
                // active filter (if any) is re-applied just below via ApplyFilterToGroup.
                group.SetLeaves(objects.Select(obj => MetadataNodeViewModel.CreateLeaf(this, obj)));
                group.Count = objects.Count;
                group.MarkLoaded();
                // The loaded object set grew — bump the generation and let open editors refresh their
                // semantic model so this category's objects (e.g. views / procedures referenced in FROM)
                // begin resolving.
                RaiseObjectsChanged();
                sw.Stop();
                Diagnostics.PerfTrace.LogGroupLoad(group.Kind.ToString(), objects.Count, sw.ElapsedMilliseconds);

                // Filter ONLY the group we just loaded — never the whole tree. The old global
                // ApplyFilter() here was the cause of the "expanding one category expands the
                // others" bug (#4): loading a category re-ran the global filter, which
                // re-expanded every other loaded matching group. A single group's filtering
                // touches no siblings and changes no other branch's expand state.
                // ⚠ Order matters: ApplyFilterToGroup branches on IsLoaded, so MarkLoaded above must
                // already have run or a filtered load would count from the name cache instead of its leaves.
                var filter = (FilterText ?? string.Empty).Trim();
                if (filter.Length > 0)
                {
                    ApplyFilterToGroup(group, hasFilter: true, filter);
                }
            }
            finally
            {
                EndSidebarBulkUpdate();
            }
        }
        catch (MetadataReadException ex)
        {
            StatusReported?.Invoke(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            StatusReported?.Invoke(ex.Message);
        }
        finally
        {
            group.IsLoading = false;
        }
    }

    internal void RequestOpenDdl(MetadataObject obj) => OpenDdlRequested?.Invoke(obj);
    internal void RequestCopyName(string name) => CopyNameRequested?.Invoke(name);
    internal void RequestNewObject(MetadataObjectKind kind) => NewObjectRequested?.Invoke(kind);
    internal void RequestDeleteObject(MetadataObject obj) => DeleteObjectRequested?.Invoke(obj);

    /// <summary>
    /// The node behind the sidebar's primary selected row — a <see cref="ConnectionNodeViewModel"/>,
    /// <see cref="FolderNodeViewModel"/> or <see cref="MetadataNodeViewModel"/>. Fed by the view's
    /// selection handler, exactly as <see cref="SelectedConnection"/> and <c>SetSelectedTriggers</c>
    /// already are: the selection is a fact the list owns, and every consumer of it belongs here.
    ///
    /// <para>⚠ Not observable, on purpose. Nothing binds to it — <see cref="ResolveCommand"/> reads it at
    /// the moment a key is pressed — so raising change notifications on every arrow-key move through a
    /// long tree would be pure noise.</para>
    /// </summary>
    internal object? SelectedNode { get; set; }

    /// <summary>
    /// The command the Object Explorer offers for <paramref name="id"/> at <c>CommandScope.Tree</c>, or
    /// null when the current selection has nothing to offer — which is what makes the gesture fall through
    /// instead of appearing to work.
    ///
    /// <para>⭐ Every arm reuses the node's OWN command, the same one its context menu invokes. In
    /// particular <c>DeleteObject</c> routes to <see cref="MetadataNodeViewModel.DeleteCommand"/>, which
    /// raises the existing confirmation dialog — so F8 opens a question, never drops an object outright.
    /// A shortcut is a second trigger for a command, never a second path to the action.</para>
    /// </summary>
    internal System.Windows.Input.ICommand? ResolveCommand(CommandId id) => id switch
    {
        CommandId.NewObject => SelectedNode is MetadataNodeViewModel { SupportsNew: true } group
            ? group.NewCommand
            : null,
        CommandId.DeleteObject => SelectedNode is MetadataNodeViewModel { CanDeleteLeaf: true } leaf
            ? leaf.DeleteCommand
            : null,
        // Refresh is one global re-read of the tree (every connection node's command calls the same
        // RefreshAsync), so any connected connection answers for it — and routing through the node's
        // command keeps the ban on a further direct RefreshAsync() call site intact.
        CommandId.RefreshMetadata => SelectedConnection?.RefreshMetadataCommand,
        _ => null,
    };
    internal void RequestExecuteProcedure(MetadataObject obj) => ExecuteProcedureRequested?.Invoke(obj);
    internal void RequestDebugProcedure(MetadataObject obj) => DebugProcedureRequested?.Invoke(obj);
    internal void RequestDebugTrigger(MetadataObject obj) => DebugTriggerRequested?.Invoke(obj);
    internal void RequestDebugFunction(MetadataObject obj) => DebugFunctionRequested?.Invoke(obj);
    internal void RequestRecompileGroup(MetadataObjectKind kind) => RecompileGroupRequested?.Invoke(kind);
    internal void RequestSetObjectActive(MetadataObject obj, bool activate) => SetObjectActiveRequested?.Invoke(obj, activate);
    internal void RequestBulkSetActive(TriggerBulkRequest request) => BulkSetActiveRequested?.Invoke(request);

    // ── Multi-select trigger bulk ("Selected" scope) ──────────────────────────────────────────
    // The sidebar ListBox is the source of the multi-selection; the view pushes it here on every
    // SelectionChanged. Held on this singleton so the Selected commands + their count are available
    // no matter which node's context menu is open. Not persisted (a rebuild/filter clears it).
    private IReadOnlyList<MetadataObject> _selectedTriggers = Array.Empty<MetadataObject>();

    /// <summary>How many trigger leaves are currently multi-selected — the count shown in the
    /// "Activate/Deactivate selected" confirmation and used to gate those commands.</summary>
    public int SelectedTriggerCount => _selectedTriggers.Count;
    public bool HasSelectedTriggers => _selectedTriggers.Count > 0;

    /// <summary>Called by the view on every sidebar selection change with the selected rows.</summary>
    internal void SetSelectedTriggers(IEnumerable<SidebarRow> selectedRows)
    {
        _selectedTriggers = ExtractSelectedTriggers(selectedRows);
        OnPropertyChanged(nameof(SelectedTriggerCount));
        OnPropertyChanged(nameof(HasSelectedTriggers));
        ActivateSelectedTriggersCommand.NotifyCanExecuteChanged();
        DeactivateSelectedTriggersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Pure: the distinct trigger <see cref="MetadataObject"/>s among the selected rows
    /// (connection / folder / category / placeholder / non-trigger leaves are ignored).</summary>
    internal static IReadOnlyList<MetadataObject> ExtractSelectedTriggers(IEnumerable<SidebarRow> rows)
        => rows.Select(r => r.Node)
               .OfType<MetadataNodeViewModel>()
               .Where(n => n.IsTriggerLeaf && n.Object is not null)
               .Select(n => n.Object!)
               .ToList();

    [RelayCommand(CanExecute = nameof(HasSelectedTriggers))]
    private void ActivateSelectedTriggers() => RequestSelectedTriggerBulk(activate: true);

    [RelayCommand(CanExecute = nameof(HasSelectedTriggers))]
    private void DeactivateSelectedTriggers() => RequestSelectedTriggerBulk(activate: false);

    private void RequestSelectedTriggerBulk(bool activate)
    {
        var names = _selectedTriggers.Select(t => t.Name).ToList();
        if (names.Count == 0) return;
        RequestBulkSetActive(new TriggerBulkRequest(
            MetadataObjectKind.Trigger, activate, BatchOperationScope.Selected, names));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
    private void EditSelected() => SelectedConnection?.EditCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
    private void CopySelected() => SelectedConnection?.CopyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
    private void DeleteSelected() => SelectedConnection?.DeleteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanConnectSelected))]
    private void ConnectSelected() => SelectedConnection?.ConnectCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDisconnectSelected))]
    private void DisconnectSelected() => SelectedConnection?.DisconnectCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDisconnectSelected))]
    private void ReconnectSelected() => SelectedConnection?.ReconnectCommand.Execute(null);

    private bool HasSelectedConnection() => SelectedConnection is not null;
    private bool CanConnectSelected() => SelectedConnection is { IsConnected: false };
    private bool CanDisconnectSelected() => SelectedConnection is { IsConnected: true };

    // ─── Filter debounce ──────────────────────────────────────────────────
    // TextBox.Text writes the source on every keystroke; without debounce, ApplyFilter
    // ran per character — on a big schema that's a visible stutter while typing. We
    // coalesce keystrokes into one ApplyFilter ~350 ms after the user stops (300 too
    // twitchy for fast typists, 500 reads as laggy). The timer is created lazily and
    // guarded: in unit tests / headless there's no dispatcher loop, so we fall back to
    // applying synchronously (keeps the old immediate behaviour the tests rely on).
    private const int FilterDebounceMs = 350;
    private DispatcherTimer? _filterDebounce;

    partial void OnFilterTextChanged(string value) => ScheduleFilter();

    private void ScheduleFilter()
    {
        try
        {
            if (_filterDebounce is null)
            {
                _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FilterDebounceMs) };
                _filterDebounce.Tick += (_, _) =>
                {
                    _filterDebounce!.Stop();
                    _ = ApplyFilterAsync();
                };
            }

            _filterDebounce.Stop();
            _filterDebounce.Start();
        }
        catch
        {
            // No usable dispatcher (unit tests / headless) — apply immediately.
            _ = ApplyFilterAsync();
        }
    }

    // ─── Session name cache (shared by filter + type-ahead) ───────────────
    // Lazy load gives the tree its fast connect, but it also means NOTHING is loaded to
    // search when the user filters or types. So the first filter / type-ahead builds a
    // flat name index per category — object NAMES only (strings, not VMs): cheap memory,
    // no layout cost, one round-trip per category, cached for the session. This is what
    // lets the filter show "Views (1)" for an un-expanded category and lets type-ahead
    // find an object in a category that was never expanded. Invalidated on
    // disconnect / reload / refresh (schema may have changed). Keyed by the group VM,
    // which is rebuilt on every ReloadConnections — hence the aggressive invalidation.
    private Dictionary<MetadataNodeViewModel, IReadOnlyList<string>>? _nameCache;
    private Task? _nameCacheTask;

    internal void InvalidateNameCache()
    {
        _nameCache = null;
        _nameCacheTask = null;
    }

    // Idempotent: the first caller starts the build, later callers join the same Task
    // (gotcha #23). After it completes, _nameCache is populated.
    internal Task EnsureNameCacheAsync() => _nameCacheTask ??= BuildNameCacheAsync();

    private async Task BuildNameCacheAsync()
    {
        var cache = new Dictionary<MetadataNodeViewModel, IReadOnlyList<string>>();
        foreach (var connection in Connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }
            foreach (var group in connection.Children)
            {
                if (!group.IsGroup)
                {
                    continue;
                }
                try
                {
                    var objects = await _reader.ListAsync(group.Kind).ConfigureAwait(true);
                    var names = new List<string>(objects.Count);
                    foreach (var o in objects)
                    {
                        names.Add(o.Name);
                    }
                    cache[group] = names;
                }
                catch (MetadataReadException) { cache[group] = Array.Empty<string>(); }
                catch (InvalidOperationException) { cache[group] = Array.Empty<string>(); }
            }
        }
        _nameCache = cache;
    }

    // Name search over the already-loaded name cache (zero DB round-trips) — the
    // "names" half of Global Search. Ensures the cache first, then runs the pure
    // MetadataNameSearch matcher over every group, keyed by kind. The source / field /
    // message half lives in FirebirdMetadataSearchReader. Only groups whose kind the
    // query includes contribute.
    internal async Task<IReadOnlyList<MetadataSearchHit>> SearchNamesAsync(MetadataSearchQuery query)
    {
        if (!query.MatchNames || string.IsNullOrWhiteSpace(query.Term))
            return Array.Empty<MetadataSearchHit>();
        await EnsureNameCacheAsync().ConfigureAwait(true);
        var cache = _nameCache;
        if (cache is null) return Array.Empty<MetadataSearchHit>();
        var groups = cache.Select(kv => (kv.Key.Kind, kv.Value));
        return MetadataNameSearch.MatchAll(groups, query);
    }

    // ─── Filter ───────────────────────────────────────────────────────────
    // IBExpert-style: while a filter is active, each category shows its MATCH count
    // ("Views (1)") and categories with zero matches HIDE — so the user sees where the
    // hits are without expanding anything. Match counts for un-expanded categories come
    // from the name cache (no list load); loaded categories also hide their non-matching
    // leaves in place. Crucially we NEVER auto-expand: opening a category is the user's
    // explicit action (see #4). Cleared filter restores every category + leaf to visible
    // and the total-count label.
    private int _filterGeneration;

    internal async Task ApplyFilterAsync()
    {
        Diagnostics.ScrollTrace.Rebuild("ApplyFilter (leaf collections rebuilt)");
        var generation = ++_filterGeneration;
        var filter = (FilterText ?? string.Empty).Trim();
        var hasFilter = filter.Length > 0;

        if (hasFilter)
        {
            // Need the name cache to count matches in un-expanded categories.
            await EnsureNameCacheAsync().ConfigureAwait(true);
            if (generation != _filterGeneration)
            {
                return; // superseded by a newer keystroke
            }
        }

        // Suspend the flat projection while the filter rebuilds each group's Children
        // item-by-item, then re-project ONCE (EndUpdate → Rebuild). Without this, clearing a
        // filter with a big category expanded would splice per restored leaf (O(n²)). The
        // final projection hides zero-match categories and shows the matching leaves.
        _sidebar.BeginUpdate();
        try
        {
            foreach (var connection in Connections)
            {
                foreach (var group in connection.Children)
                {
                    if (group.IsGroup)
                    {
                        ApplyFilterToGroup(group, hasFilter, filter);
                    }
                }
            }
        }
        finally
        {
            _sidebar.EndUpdate();
        }
    }

    // Internal so tests can drive the loaded-group path directly. For an un-expanded
    // group the match count comes from the name cache (when built); the group's leaves
    // are NOT loaded by filtering.
    internal void ApplyFilterToGroup(MetadataNodeViewModel group, bool hasFilter, string filter)
    {
        if (!hasFilter)
        {
            group.FilterMatchCount = null;
            group.IsVisible = true;
            // Restore the full leaf set (only meaningful for a loaded group).
            if (group.IsLoaded)
            {
                group.ApplyLeafFilter(null);
            }
            return;
        }

        int matches;
        if (group.IsLoaded)
        {
            // Loaded: rebuild Children to ONLY the matching leaves — do NOT hide
            // non-matches in place. A hidden-but-present leaf still occupies a VSP slot
            // the panel must realize/measure, corrupting the scroll extent on large
            // categories (the scroll-lag root cause). Match count = displayed rows.
            group.ApplyLeafFilter(leaf => !leaf.IsPlaceholder
                && leaf.GroupLabel.Contains(filter, StringComparison.OrdinalIgnoreCase));
            matches = group.Children.Count;
        }
        else
        {
            // Un-expanded: count from the name cache without loading the leaf list.
            matches = _nameCache is not null && _nameCache.TryGetValue(group, out var names)
                ? CountMatches(names, filter)
                : 0;
        }

        group.FilterMatchCount = matches;
        group.IsVisible = matches > 0;
        // NO auto-expand: the user opens the category they want; opening one branch
        // must never change another branch's expand state (#4).
    }

    // Pure substring match count (case-insensitive), matching the leaf-filter predicate.
    internal static int CountMatches(IEnumerable<string> names, string filter)
    {
        var count = 0;
        foreach (var name in names)
        {
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }
}

/// <summary>The set of objects a bulk activate/deactivate applies to: every object of the kind
/// (<see cref="All"/>), the current filter result (<see cref="Visible"/>), or the user's manual
/// multi-selection (<see cref="Selected"/>).</summary>
public enum BatchOperationScope { All, Visible, Selected }

/// <summary>
/// A bulk activate/deactivate request raised from a trigger category node (All/Visible) or from
/// the sidebar multi-selection (Selected). <paramref name="Scope"/> chooses the target set;
/// <paramref name="Names"/> carries the explicit object names for <see cref="BatchOperationScope.Visible"/>
/// and <see cref="BatchOperationScope.Selected"/> (empty for <see cref="BatchOperationScope.All"/>,
/// which the owner resolves from the reader). Only <see cref="MetadataObjectKind.Trigger"/> is used
/// today, but the shape is kind-agnostic.
/// </summary>
public sealed record TriggerBulkRequest(
    MetadataObjectKind Kind,
    bool Activate,
    BatchOperationScope Scope,
    IReadOnlyList<string> Names);
