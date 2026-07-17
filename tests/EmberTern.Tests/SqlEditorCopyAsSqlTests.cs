using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Export;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Query;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// QA repro (E6 follow-up): the SQL Editor's Copy-as-INSERT/UPDATE did not reach the clipboard, while the
// Table Data grid — same Core pipeline — worked. This drives MainWindowViewModel.CopyRowAsSqlAsync with a
// controller whose coordinator resolves without a live connection, so we can prove whether the VM path
// (build → ClipboardWriteRequested → message) reaches the clipboard. If it does, the fault is in the view.
public class SqlEditorCopyAsSqlTests
{
    private sealed class FakeCatalog : ISqlMetadataProvider
    {
        public ObjectMetadata? FindObject(string name)
            => string.Equals(name, "CUSTOMERS", StringComparison.OrdinalIgnoreCase)
                ? new ObjectMetadata("CUSTOMERS", SymbolKind.Table)
                : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => string.Equals(tableOrView, "CUSTOMERS", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    new ColumnMetadata("CUSTOMER_ID", "INTEGER") { IsPrimaryKey = true },
                    new ColumnMetadata("NAME", "VARCHAR"),
                }
                : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects()
            => new[] { new ObjectMetadata("CUSTOMERS", SymbolKind.Table) };
    }

    private static ResultOrigin DirectCustomers() => new(
        new[]
        {
            new ColumnOrigin("CUSTOMERS", "CUSTOMER_ID", IsComputed: false, SqlValueKind.Integer),
            new ColumnOrigin("CUSTOMERS", "NAME", IsComputed: false, SqlValueKind.Text),
        },
        new OriginShape.DirectTable("CUSTOMERS"));

    private static SqlCopyController ResolvingController() => new(
        new SqlCopyCoordinator(_ => Task.FromResult(DirectCustomers()), () => new FakeCatalog()));

    [Fact]
    public async Task CopyRowAsSql_ReachesTheClipboard_WhenResolved()
    {
        using var h = new Harness();
        h.Main.CurrentResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("CUSTOMER_ID", typeof(int)), new QueryColumn("NAME", typeof(string)) },
            Rows = new[] { new object?[] { 1, "Alice" } },
        };
        h.Main.SqlCopy = ResolvingController();

        string? captured = null;
        h.Main.ClipboardWriteRequested += t => { captured = t; return Task.CompletedTask; };

        var ok = await h.Main.CopyRowAsSqlAsync(ExportFormat.InsertScript, h.Main.CurrentResult!.Rows[0]);

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.StartsWith("insert into customers", captured);
    }

    [Fact]
    public async Task CopyRowAsSql_Update_ReachesTheClipboard_WhenResolved()
    {
        using var h = new Harness();
        h.Main.CurrentResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("CUSTOMER_ID", typeof(int)), new QueryColumn("NAME", typeof(string)) },
            Rows = new[] { new object?[] { 1, "Alice" } },
        };
        h.Main.SqlCopy = ResolvingController();

        string? captured = null;
        h.Main.ClipboardWriteRequested += t => { captured = t; return Task.CompletedTask; };

        var ok = await h.Main.CopyRowAsSqlAsync(ExportFormat.UpdateScript, h.Main.CurrentResult!.Rows[0]);

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.Contains("where customer_id = 1", captured);
    }

    // The bug's shape: when the row cannot be captured the copy must NOT silently do nothing — it says so
    // and never claims success. (The old code silently returned on a -1 row index.)
    [Fact]
    public async Task CopyRowAsSql_NullRow_ReportsAndDoesNotClaimSuccess()
    {
        using var h = new Harness();
        h.Main.CurrentResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("CUSTOMER_ID", typeof(int)) },
            Rows = new[] { new object?[] { 1 } },
        };
        h.Main.SqlCopy = ResolvingController();

        var invoked = false;
        h.Main.ClipboardWriteRequested += _ => { invoked = true; return Task.CompletedTask; };

        var ok = await h.Main.CopyRowAsSqlAsync(ExportFormat.InsertScript, null);

        Assert.False(ok);
        Assert.False(invoked);
        Assert.Contains(h.Main.Messages, m => m.Text.Contains("row", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
