using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    }

    public string TableName { get; }

    public ObservableCollection<FieldInfo> Fields { get; }
    public ObservableCollection<IndexInfo> Indexes { get; }
    public ObservableCollection<ConstraintInfo> Constraints { get; }

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
