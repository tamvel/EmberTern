using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>One result leaf in the Search Results tree — an object (optionally a nested
/// field) with its match count. Opening reuses the Explorer's open path.</summary>
public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(MetadataObjectKind kind, SearchResultLeaf leaf)
    {
        Kind = kind;
        ObjectName = leaf.ObjectName;
        DetailName = leaf.DetailName;
        MatchCount = leaf.MatchCount;
        Location = leaf.Location;
    }

    public MetadataObjectKind Kind { get; }
    public string ObjectName { get; }
    public string? DetailName { get; }
    public int MatchCount { get; }
    public SearchMatchLocation Location { get; }

    // "PROC_A" or "TOWARY.ID_MAGAZYN" (field hit), with a "[n]" occurrence badge.
    public string Label => DetailName is null ? ObjectName : $"{ObjectName}.{DetailName}";
    public string DisplayLabel => string.Format(CultureInfo.CurrentCulture, "{0} [{1}]", Label, MatchCount);
    public string IconGeometryKey => MetadataNodeViewModel.GeometryKeyFor(Kind);
    public string IconResourceKey => MetadataNodeViewModel.ResourceKeyFor(Kind);

    // The metadata object to open on double-click — a field hit opens its table.
    public MetadataObject Target => new(ObjectName, Kind);

    // Leaves have no children; present but always-true so the shared TreeViewItem
    // IsExpanded style binds cleanly across group + leaf node types (gotcha #156).
    public bool IsExpanded => true;
}

/// <summary>One result group (per object kind), header "Procedures (5)".</summary>
public sealed class SearchResultGroupViewModel
{
    public SearchResultGroupViewModel(SearchResultGroup group)
    {
        Kind = group.Kind;
        Header = string.Format(CultureInfo.CurrentCulture, "{0} ({1})", PluralFor(group.Kind), group.Leaves.Count);
        Items = new ObservableCollection<SearchResultItemViewModel>(
            group.Leaves.Select(l => new SearchResultItemViewModel(group.Kind, l)));
    }

    public MetadataObjectKind Kind { get; }
    public string Header { get; }
    public string IconGeometryKey => MetadataNodeViewModel.GeometryKeyFor(Kind);
    public string IconResourceKey => MetadataNodeViewModel.ResourceKeyFor(Kind);
    public ObservableCollection<SearchResultItemViewModel> Items { get; }
    public bool IsExpanded => true;

    private static string PluralFor(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => UiStrings.MetadataGroupTables,
        MetadataObjectKind.View => UiStrings.MetadataGroupViews,
        MetadataObjectKind.Procedure => UiStrings.MetadataGroupProcedures,
        MetadataObjectKind.Trigger => UiStrings.MetadataGroupTriggers,
        MetadataObjectKind.Function => UiStrings.MetadataGroupFunctions,
        MetadataObjectKind.Generator => UiStrings.MetadataGroupGenerators,
        MetadataObjectKind.Domain => UiStrings.MetadataGroupDomains,
        MetadataObjectKind.Package => UiStrings.MetadataGroupPackages,
        MetadataObjectKind.Exception => UiStrings.MetadataGroupExceptions,
        _ => kind.ToString(),
    };
}

/// <summary>
/// A Global Search results tab: runs the query (names from the Explorer cache + source /
/// field / message from <see cref="FirebirdMetadataSearchReader"/>), groups the hits into
/// the results tree, and lazily loads a read-only DDL preview for the selected leaf.
/// Not persisted; a fresh tab per phrase (no overwrite).
/// </summary>
public sealed partial class GlobalSearchTabViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;
    private readonly FirebirdMetadataSearchReader _searchReader;
    private readonly FirebirdDdlReader _ddlReader;
    private readonly MetadataSearchQuery _query;
    private readonly Dictionary<(MetadataObjectKind, string), string> _previewCache = new();

    public GlobalSearchTabViewModel(
        MainWindowViewModel owner,
        FirebirdMetadataSearchReader searchReader,
        FirebirdDdlReader ddlReader,
        MetadataSearchQuery query)
    {
        _owner = owner;
        _searchReader = searchReader;
        _ddlReader = ddlReader;
        _query = query;
    }

    public string Term => _query.Term;
    public ObservableCollection<SearchResultGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasResults;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SearchResultItemViewModel? _selectedItem;

    // TwoWay-bound to the TreeView's SelectedItem (heterogeneous: group or leaf). Driving
    // selection through the VM keeps the view out of the control's internals — the crash
    // was a code-behind handler touching the named TreeView field before it was assigned.
    [ObservableProperty] private object? _selectedNode;

    [ObservableProperty] private string _previewText = string.Empty;

    public bool HasSelection => SelectedItem is not null;

    partial void OnSelectedNodeChanged(object? value)
    {
        // A leaf drives the preview; selecting a group keeps the current preview.
        if (value is SearchResultItemViewModel item) SelectedItem = item;
    }

    /// <summary>Runs the search and populates the results tree. Errors surface as status text.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        IsSearching = true;
        StatusText = UiStrings.GlobalSearchSearching;
        try
        {
            var nameHits = await _owner.Metadata.SearchNamesAsync(_query).ConfigureAwait(true);
            var dbHits = await _searchReader.SearchAsync(_query, cancellationToken).ConfigureAwait(true);

            var groups = MetadataSearchResults.Group(nameHits.Concat(dbHits));
            Groups.Clear();
            int objectCount = 0;
            foreach (var g in groups)
            {
                Groups.Add(new SearchResultGroupViewModel(g));
                objectCount += g.Leaves.Count;
            }

            HasResults = objectCount > 0;
            StatusText = objectCount == 0
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.GlobalSearchNoResults, Term)
                : string.Format(CultureInfo.CurrentCulture, UiStrings.GlobalSearchResultCount, objectCount, Term);
        }
        catch (MetadataReadException ex)
        {
            Groups.Clear();
            HasResults = false;
            StatusText = ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    partial void OnSelectedItemChanged(SearchResultItemViewModel? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(SearchResultItemViewModel? item)
    {
        if (item is null) { PreviewText = string.Empty; return; }
        var key = (item.Kind, item.ObjectName);
        if (_previewCache.TryGetValue(key, out var cached)) { PreviewText = cached; return; }
        try
        {
            var ddl = await _ddlReader.FetchDdlAsync(item.Target).ConfigureAwait(true);
            _previewCache[key] = ddl;
            // Guard against a race: only apply if the selection hasn't moved on.
            if (ReferenceEquals(SelectedItem, item)) PreviewText = ddl;
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            // InvalidOperationException = no open connection (e.g. clicked after a disconnect).
            if (ReferenceEquals(SelectedItem, item))
                PreviewText = string.Format(CultureInfo.CurrentCulture, UiStrings.GlobalSearchPreviewError, ex.Message);
        }
    }

    /// <summary>Opens the selected object via the Explorer's existing open path
    /// (dedup + OpensAsXxxDetail dispatch) — a field hit opens its table.</summary>
    public void Open(SearchResultItemViewModel? item)
    {
        if (item is null) return;
        _owner.Metadata.RequestOpenDdl(item.Target);
    }
}
