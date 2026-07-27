// Metadata mechanism — measurement probe. See MetadataPerfProbe.csproj for what this is and why.
//
//   dotnet run --project tools/probes/MetadataPerfProbe
//
// Requires the local FB5 DefaultInstance on localhost:3050. Creates and drops its OWN scratch database at
// an ASCII path (gotcha #149) — the lab database is never touched.

using System.Diagnostics;
using System.Globalization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;

// WIN1250 is not a .NET-built-in encoding — without this the raw FbConnection used to BUILD the scratch
// schema fails with "Invalid character set specified". FirebirdConnectionService does this in its own static
// constructor; this probe reaches for a bare FbConnection first, so it has to do it itself.
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

// ── Scale. Chosen to match the user's real database (CLAUDE.md records ~2 388 tables). ──────────────────
const int Tables = 2400;
const int Views = 200;
const int Procedures = 400;
const int Triggers = 1200;

var scratchDir = @"C:\Temp";
Directory.CreateDirectory(scratchDir);
var scratchPath = Path.Combine(scratchDir, "embertern_metaperf.fdb");

Console.WriteLine("Metadata mechanism — measurement probe");
Console.WriteLine($"Scratch database: {scratchPath}");
Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// PART B first — it needs no database at all, so it always produces numbers even if the server is down.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════

Console.WriteLine("── B. THE PROJECTION (UI thread) — real SidebarFlatController ──────────────────────────");
Console.WriteLine();
Console.WriteLine("What is measured: replacing a category's leaves the way LoadGroupAsync does it today —");
Console.WriteLine("ObservableCollection.Clear() then one Add per object — with the category EXPANDED.");
Console.WriteLine();
Console.WriteLine($"{"leaves",8} {"today (ms)",12} {"row ops",14} {"notifications",14} {"bulk-guarded (ms)",18}");

foreach (var n in new[] { 100, 250, 500, 1000, 2400 })
{
    MeasureProjection(n);
}

static void MeasureProjection(int leafCount)
{
    // Plain nodes — the controller is generic over `object` and asks its delegates everything, so this
    // exercises the REAL projection algorithm with no Avalonia and no ViewModels in the way.
    var group = new Node("Tables", isContainer: true) { IsExpanded = true };
    var root = new Node("connection", isContainer: true) { IsExpanded = true };
    root.Children.Add(group);

    var roots = new System.Collections.ObjectModel.ObservableCollection<object> { root };

    var notifications = 0;

    using var controller = new SidebarFlatController(
        roots,
        childrenSelector: o => ((Node)o).IsContainer ? ((Node)o).Children.Cast<object>() : null,
        isContainer: o => ((Node)o).IsContainer,
        hasChildren: o => ((Node)o).Children.Count > 0,
        isExpanded: o => ((Node)o).IsExpanded,
        setExpanded: (o, v) => ((Node)o).IsExpanded = v);

    controller.Rows.CollectionChanged += (_, _) => notifications++;

    var leaves = Enumerable.Range(0, leafCount)
        .Select(i => new Node("OBJ_" + i.ToString(CultureInfo.InvariantCulture), isContainer: false))
        .ToList();

    // (1) TODAY: exactly what MetadataNodeViewModel.SetLeaves does — Clear, then Add one at a time.
    var sw = Stopwatch.StartNew();
    group.Children.Clear();
    foreach (var leaf in leaves) group.Children.Add(leaf);
    sw.Stop();

    var today = sw.Elapsed.TotalMilliseconds;
    var todayNotifications = notifications;

    // (2) THE SAME WORK under the bulk guard the FILTER path already uses (BeginUpdate/EndUpdate), which
    //     re-projects ONCE instead of after every single Add.
    notifications = 0;
    var sw2 = Stopwatch.StartNew();
    controller.BeginUpdate();
    group.Children.Clear();
    foreach (var leaf in leaves) group.Children.Add(leaf);
    controller.EndUpdate();
    sw2.Stop();

    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "{0,8} {1,12:F1} {2,14} {3,14} {4,18:F1}",
        leafCount, today, Estimate(leafCount), todayNotifications, sw2.Elapsed.TotalMilliseconds));
}

// Rows touched: every Add re-splices the whole child block (remove all, insert all) → ~n²/2 either way.
static string Estimate(int n) => ((long)n * (n + 1)).ToString("N0", CultureInfo.InvariantCulture);

Console.WriteLine();
Console.WriteLine("── B2. A WHOLE Refresh, as the user feels it ───────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("RefreshAsync reloads EVERY category (after connect they are all IsLoaded), so the cost is");
Console.WriteLine("the sum over all 13 — and each one pays the quadratic price only while it is EXPANDED.");
Console.WriteLine();
Console.WriteLine("BEFORE = the load path unguarded (as it was until 2026-07-27).");
Console.WriteLine("AFTER  = the load path under the bulk guard, which is what LoadGroupAsync + RefreshAsync");
Console.WriteLine("         + the connect-time prefetch now do.");
Console.WriteLine();
Console.WriteLine($"{"expanded categories",21} {"BEFORE (ms)",13} {"AFTER (ms)",12}");

foreach (var expanded in new[] { 0, 1, 2 })
{
    var before = MeasureWholeRefresh(expanded, guarded: false);
    var after = MeasureWholeRefresh(expanded, guarded: true);
    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture, "{0,21} {1,13:F0} {2,12:F0}", expanded, before, after));
}

static double MeasureWholeRefresh(int expandedCategories, bool guarded)
{
    // Realistic category sizes for an ERP schema of ~2 400 tables.
    (string Name, int Count)[] sizes =
    {
        ("Table", 2400), ("View", 200), ("Procedure", 400), ("Trigger", 1200), ("Function", 60),
        ("Generator", 300), ("Domain", 150), ("Package", 10), ("Exception", 80), ("Role", 20),
        ("User", 10), ("Index", 3000), ("SystemTable", 56),
    };

    var root = new Node("connection", isContainer: true) { IsExpanded = true };
    var groups = new List<Node>();
    foreach (var (name, _) in sizes)
    {
        var g = new Node(name, isContainer: true);
        root.Children.Add(g);
        groups.Add(g);
    }

    // The user typically has a category or two open.
    for (var i = 0; i < expandedCategories && i < groups.Count; i++) groups[i].IsExpanded = true;

    var roots = new System.Collections.ObjectModel.ObservableCollection<object> { root };
    using var controller = new SidebarFlatController(
        roots,
        childrenSelector: o => ((Node)o).IsContainer ? ((Node)o).Children.Cast<object>() : null,
        isContainer: o => ((Node)o).IsContainer,
        hasChildren: o => ((Node)o).Children.Count > 0,
        isExpanded: o => ((Node)o).IsExpanded,
        setExpanded: (o, v) => ((Node)o).IsExpanded = v);

    // Pre-populate once, so we measure a REFRESH (replace) rather than a first load.
    for (var i = 0; i < sizes.Length; i++)
    {
        foreach (var leaf in MakeLeaves(sizes[i].Count)) groups[i].Children.Add(leaf);
    }

    var sw = Stopwatch.StartNew();
    // RefreshAsync wraps the whole 13-category loop; each LoadGroupAsync also wraps itself (nesting-safe).
    if (guarded) controller.BeginUpdate();
    for (var i = 0; i < sizes.Length; i++)
    {
        if (guarded) controller.BeginUpdate();
        // Exactly MetadataNodeViewModel.SetLeaves: clear, then add one at a time.
        groups[i].Children.Clear();
        foreach (var leaf in MakeLeaves(sizes[i].Count)) groups[i].Children.Add(leaf);
        if (guarded) controller.EndUpdate();
    }
    if (guarded) controller.EndUpdate();
    sw.Stop();

    return sw.Elapsed.TotalMilliseconds;
}

static List<Node> MakeLeaves(int n)
    => Enumerable.Range(0, n).Select(i => new Node("O" + i.ToString(CultureInfo.InvariantCulture), false)).ToList();

Console.WriteLine();
Console.WriteLine("── B3. ONE object added, the two ways ──────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("The Data Import bug: a table was created and the tree did not show it. The obvious repair is");
Console.WriteLine("a full RefreshAsync; the one that shipped inserts a single leaf in place, because the module");
Console.WriteLine("already knows the name. Below: the PROJECTION cost of each (the full refresh also pays ~172 ms");
Console.WriteLine("of catalog reads that the in-place insert does not pay at all).");
Console.WriteLine();

MeasureOneObjectAdded();

static void MeasureOneObjectAdded()
{
    const int Leaves = 2400;

    var group = new Node("Tables", isContainer: true) { IsExpanded = true };
    var root = new Node("connection", isContainer: true) { IsExpanded = true };
    root.Children.Add(group);
    var roots = new System.Collections.ObjectModel.ObservableCollection<object> { root };

    using var controller = new SidebarFlatController(
        roots,
        childrenSelector: o => ((Node)o).IsContainer ? ((Node)o).Children.Cast<object>() : null,
        isContainer: o => ((Node)o).IsContainer,
        hasChildren: o => ((Node)o).Children.Count > 0,
        isExpanded: o => ((Node)o).IsExpanded,
        setExpanded: (o, v) => ((Node)o).IsExpanded = v);

    foreach (var leaf in MakeLeaves(Leaves)) group.Children.Add(leaf);

    // (1) The full-refresh repair: re-read and replace the category's leaves, now guarded.
    var sw = Stopwatch.StartNew();
    controller.BeginUpdate();
    group.Children.Clear();
    foreach (var leaf in MakeLeaves(Leaves + 1)) group.Children.Add(leaf);
    controller.EndUpdate();
    sw.Stop();

    // (2) What shipped: MetadataNodeViewModel.InsertLeafInPlace — one Insert at the sorted position.
    var notifications = 0;
    controller.Rows.CollectionChanged += (_, _) => notifications++;
    var sw2 = Stopwatch.StartNew();
    group.Children.Insert(1200, new Node("IMPORT_NOWA", isContainer: false));
    sw2.Stop();

    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "  full refresh of the category ({0} leaves, guarded): {1,6:F1} ms  + a catalog round trip",
        Leaves, sw.Elapsed.TotalMilliseconds));
    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "  one leaf inserted in place:                        {0,6:F1} ms  + no catalog round trip ({1} row notifications)",
        sw2.Elapsed.TotalMilliseconds, notifications));
}

Console.WriteLine();

// ════════════════════════════════════════════════════════════════════════════════════════════════════════
// PART A — the catalog, at realistic scale.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════

Console.WriteLine("── A. THE CATALOG — real FirebirdMetadataReader against a realistic schema ──────────────");
Console.WriteLine();

var profile = new EmberTern.Core.Connections.ConnectionProfile
{
    Name = "metaperf",
    Host = "localhost",
    Port = 3050,
    DatabasePath = scratchPath,
    Username = "SYSDBA",
    Password = "masterkey",
    Charset = "WIN1250",
    Dialect = 3,
};

try
{
    await BuildScratchAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine("Could not build the scratch database: " + ex.Message);
    return 2;
}

var connectionService = new FirebirdConnectionService();
await connectionService.ConnectAsync(profile);
var transactionService = new TransactionService(connectionService);
var lane = new MetadataLane(connectionService, transactionService);
var reader = new FirebirdMetadataReader(connectionService, lane);

Console.WriteLine($"Connected. Server: {connectionService.RequireOpenConnection().ServerVersion}");
Console.WriteLine();

MetadataObjectKind[] categories =
{
    MetadataObjectKind.Table, MetadataObjectKind.View, MetadataObjectKind.Procedure,
    MetadataObjectKind.Trigger, MetadataObjectKind.Function, MetadataObjectKind.Generator,
    MetadataObjectKind.Domain, MetadataObjectKind.Package, MetadataObjectKind.Exception,
    MetadataObjectKind.Role, MetadataObjectKind.User, MetadataObjectKind.Index,
    MetadataObjectKind.SystemTable,
};

// Warm the connection so the first category does not carry the attachment's own setup cost.
await reader.CountAsync(MetadataObjectKind.Table);

Console.WriteLine($"{"category",14} {"objects",9} {"COUNT(*) ms",12} {"full list ms",13}");

double totalCount = 0, totalList = 0;
foreach (var kind in categories)
{
    double countMs = 0, listMs = 0;
    var objects = 0;

    try
    {
        var sw = Stopwatch.StartNew();
        await reader.CountAsync(kind);
        sw.Stop();
        countMs = sw.Elapsed.TotalMilliseconds;

        var sw2 = Stopwatch.StartNew();
        var list = await reader.ListAsync(kind);
        sw2.Stop();
        listMs = sw2.Elapsed.TotalMilliseconds;
        objects = list.Count;
    }
    catch (MetadataReadException ex)
    {
        Console.WriteLine($"{kind,14} — unavailable: {ex.Message}");
        continue;
    }

    totalCount += countMs;
    totalList += listMs;

    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "{0,14} {1,9} {2,12:F1} {3,13:F1}", kind, objects, countMs, listMs));
}

Console.WriteLine();
Console.WriteLine(string.Format(
    CultureInfo.InvariantCulture,
    "TOTAL over {0} categories:  COUNT-only {1:F0} ms   ·   FULL LISTS {2:F0} ms   ·   ratio {3:F1}×",
    categories.Length, totalCount, totalList, totalList / Math.Max(0.001, totalCount)));
Console.WriteLine();
Console.WriteLine("Connect today runs the FULL-LISTS column (ConnectionNodeViewModel.LoadCategoriesAsync");
Console.WriteLine("prefetches every category); manual Refresh runs it again.");

// ── One more question worth a number: what does ONE object cost to look up on its own? ──────────────────
{
    var sw = Stopwatch.StartNew();
    await reader.CountAsync(MetadataObjectKind.Table);
    sw.Stop();
    Console.WriteLine();
    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "A single catalog round trip costs ~{0:F1} ms — the floor for any targeted refresh.",
        sw.Elapsed.TotalMilliseconds));
}

await connectionService.DisconnectAsync();
Console.WriteLine();
Console.WriteLine("Done. The scratch database is left in place for repeat runs; delete it when finished:");
Console.WriteLine("  " + scratchPath);
return 0;

// ── Scratch schema ──────────────────────────────────────────────────────────────────────────────────────

async Task BuildScratchAsync()
{
    if (File.Exists(scratchPath))
    {
        Console.WriteLine("Reusing the existing scratch database (delete it to rebuild).");
        return;
    }

    Console.WriteLine($"Building a scratch schema: {Tables} tables, {Views} views, {Procedures} procedures, {Triggers} triggers…");
    var sw = Stopwatch.StartNew();

    var csb = new FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder
    {
        DataSource = "localhost",
        Port = 3050,
        Database = scratchPath,
        UserID = "SYSDBA",
        Password = "masterkey",
        Charset = "WIN1250",
        Dialect = 3,
        ServerType = FirebirdSql.Data.FirebirdClient.FbServerType.Default,
    };

    FirebirdSql.Data.FirebirdClient.FbConnection.CreateDatabase(csb.ToString(), overwrite: true);

    await using var connection = new FirebirdSql.Data.FirebirdClient.FbConnection(csb.ToString());
    await connection.OpenAsync();

    async Task ExecAsync(FirebirdSql.Data.FirebirdClient.FbTransaction tx, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        await cmd.ExecuteNonQueryAsync();
    }

    // DDL is committed in batches — a Firebird transaction cannot use an object whose DDL it has not
    // committed (gotcha #213), and the triggers below reference the tables.
    var tx = await connection.BeginTransactionAsync();
    for (var i = 0; i < Tables; i++)
    {
        await ExecAsync(tx, $"CREATE TABLE T_{i:D5} (ID INTEGER NOT NULL PRIMARY KEY, CODE VARCHAR(20), NAME VARCHAR(100), QTY INTEGER, PRICE NUMERIC(15,2))");
        if (i % 200 == 199) { await tx.CommitAsync(); tx = await connection.BeginTransactionAsync(); }
    }
    await tx.CommitAsync();

    tx = await connection.BeginTransactionAsync();
    for (var i = 0; i < Views; i++)
    {
        await ExecAsync(tx, $"CREATE VIEW V_{i:D5} AS SELECT ID, CODE FROM T_{i % Tables:D5}");
        if (i % 100 == 99) { await tx.CommitAsync(); tx = await connection.BeginTransactionAsync(); }
    }
    await tx.CommitAsync();

    tx = await connection.BeginTransactionAsync();
    for (var i = 0; i < Procedures; i++)
    {
        await ExecAsync(tx, $"CREATE PROCEDURE P_{i:D5} (P_ID INTEGER) RETURNS (R_NAME VARCHAR(100)) AS BEGIN SELECT NAME FROM T_{i % Tables:D5} WHERE ID = :P_ID INTO :R_NAME; SUSPEND; END");
        if (i % 100 == 99) { await tx.CommitAsync(); tx = await connection.BeginTransactionAsync(); }
    }
    await tx.CommitAsync();

    tx = await connection.BeginTransactionAsync();
    for (var i = 0; i < Triggers; i++)
    {
        await ExecAsync(tx, $"CREATE TRIGGER TR_{i:D5} FOR T_{i % Tables:D5} ACTIVE BEFORE INSERT POSITION 0 AS BEGIN IF (NEW.QTY IS NULL) THEN NEW.QTY = 0; END");
        if (i % 200 == 199) { await tx.CommitAsync(); tx = await connection.BeginTransactionAsync(); }
    }
    await tx.CommitAsync();

    sw.Stop();
    Console.WriteLine($"Scratch schema built in {sw.Elapsed.TotalSeconds:F0} s.");
}

// A minimal stand-in for a tree node: the controller asks its delegates for everything, so this is all the
// shape it needs. Using the REAL controller rather than a re-implementation is the point — a re-implemented
// algorithm would measure my model of the code, not the code.
internal sealed class Node : System.ComponentModel.INotifyPropertyChanged
{
    public Node(string name, bool isContainer)
    {
        Name = name;
        IsContainer = isContainer;
    }

    public string Name { get; }
    public bool IsContainer { get; }
    public System.Collections.ObjectModel.ObservableCollection<Node> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Name;
}
