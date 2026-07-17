using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Export;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

// E6 (SQL Data Export — adapters) — the shared App glue that every grid uses: the SqlCopyCoordinator's
// lazy-capture-and-cache path and the SqlCopyController it feeds. These exercise the FULL mechanism a
// grid drives (capture → resolve → build → format) with an injected origin capturer + fake catalog, so
// the Table Data grid's behaviour is proven without a live engine (the actual schema read is covered by
// FirebirdResultOriginReaderTests + the live probe). One mechanism, one set of tests.
public class SqlCopyControllerTests
{
    // ── fixtures ─────────────────────────────────────────────────────────────
    private sealed class FakeCatalog : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public FakeCatalog Object(string name, SymbolKind kind)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public FakeCatalog Col(string table, string name, bool pk = false)
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, "INTEGER") { IsPrimaryKey = pk });
            return this;
        }

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToArray();
    }

    private static FakeCatalog Customers() => new FakeCatalog()
        .Col("CUSTOMERS", "CUSTOMER_ID", pk: true)
        .Col("CUSTOMERS", "NAME");

    private static ResultOrigin DirectCustomers() => new(
        new[]
        {
            new ColumnOrigin("CUSTOMERS", "CUSTOMER_ID", IsComputed: false, SqlValueKind.Integer),
            new ColumnOrigin("CUSTOMERS", "NAME", IsComputed: false, SqlValueKind.Text),
        },
        new OriginShape.DirectTable("CUSTOMERS"));

    private static readonly object?[] Row = { 7, "Ann" };

    // ── SqlCopyCoordinator — the lazy capture/cache path ─────────────────────
    [Fact]
    public async Task Coordinator_Captures_The_Origin_Once_And_Caches_It()
    {
        int captures = 0;
        var coordinator = new SqlCopyCoordinator(
            _ => { captures++; return Task.FromResult(DirectCustomers()); },
            () => Customers());

        await coordinator.GetAvailabilityAsync(ExportFormat.InsertScript);
        await coordinator.GetAvailabilityAsync(ExportFormat.UpdateScript);
        await coordinator.BuildAsync(ExportFormat.InsertScript, Row);

        Assert.Equal(1, captures); // the ~7 ms schema read happens exactly once per result set
    }

    [Fact]
    public async Task Coordinator_Reset_Re_Arms_The_Capture()
    {
        int captures = 0;
        var coordinator = new SqlCopyCoordinator(
            _ => { captures++; return Task.FromResult(DirectCustomers()); },
            () => Customers());

        await coordinator.GetAvailabilityAsync(ExportFormat.InsertScript);
        coordinator.Reset();
        await coordinator.GetAvailabilityAsync(ExportFormat.InsertScript);

        Assert.Equal(2, captures);
    }

    // A cold catalog (table known, columns not warmed) must warm-then-retry rather than report
    // "no primary key" forever — the difference between "I haven't looked" and "there isn't one".
    [Fact]
    public async Task Coordinator_Warms_A_Cold_Catalog_Then_Resolves()
    {
        var cold = new FakeCatalog().Object("CUSTOMERS", SymbolKind.Table); // known, no columns yet
        var warm = Customers();
        var current = cold;
        int warmCalls = 0;

        var coordinator = new SqlCopyCoordinator(
            _ => Task.FromResult(DirectCustomers()),
            () => current)
        {
            WarmColumns = table => { warmCalls++; current = warm; return Task.CompletedTask; },
        };

        var insert = await coordinator.GetAvailabilityAsync(ExportFormat.InsertScript);

        Assert.Equal(1, warmCalls);
        Assert.True(insert.IsAvailable);
    }

    // ── SqlCopyController — availability + tooltips + build ──────────────────
    [Fact]
    public async Task Controller_On_A_Direct_Table_Enables_Both_Actions_With_No_Tooltip()
    {
        var controller = ControllerFor(DirectCustomers(), Customers());

        await controller.RefreshAvailabilityAsync(hasResult: true);

        Assert.True(controller.CanCopyAsInsert);
        Assert.True(controller.CanCopyAsUpdate);
        Assert.Equal(string.Empty, controller.CopyAsInsertTooltip);
        Assert.Equal(string.Empty, controller.CopyAsUpdateTooltip);
    }

    [Fact]
    public async Task Controller_BuildFormatted_Insert_Returns_Formatted_Sql()
    {
        var controller = ControllerFor(DirectCustomers(), Customers());

        var built = await controller.BuildFormattedAsync(ExportFormat.InsertScript, Row);

        Assert.True(built.IsBuilt);
        // Through the shared SqlFormatter → lowercase keywords + de-aliased base columns.
        Assert.StartsWith("insert into", built.Text);
        Assert.Contains("customer_id", built.Text);
        Assert.Contains("'Ann'", built.Text);
    }

    [Fact]
    public async Task Controller_BuildFormatted_Update_Uses_The_Verified_Pk()
    {
        var controller = ControllerFor(DirectCustomers(), Customers());

        var built = await controller.BuildFormattedAsync(ExportFormat.UpdateScript, Row);

        Assert.True(built.IsBuilt);
        Assert.StartsWith("update", built.Text);
        Assert.Contains("where customer_id = 7", built.Text);
    }

    // A procedure result declares itself NotATable — a permanent, honest veto. Both actions disable and
    // the tooltip names the obstacle; a build attempt is refused with that sentence, never silent.
    [Fact]
    public async Task Controller_On_A_Not_A_Table_Source_Disables_Both_With_A_Reason()
    {
        var origin = ResultOrigin.None(ExportUnavailableReason.Of(ExportUnavailableCode.NotATable));
        var controller = ControllerFor(origin, new FakeCatalog());

        await controller.RefreshAvailabilityAsync(hasResult: true);

        Assert.False(controller.CanCopyAsInsert);
        Assert.False(controller.CanCopyAsUpdate);
        Assert.NotEqual(string.Empty, controller.CopyAsInsertTooltip);

        var built = await controller.BuildFormattedAsync(ExportFormat.InsertScript, Row);
        Assert.False(built.IsBuilt);
        Assert.NotEqual(string.Empty, built.Text);
    }

    // INSERT is available but UPDATE is not when the PK is not fully projected — the exact §6 asymmetry,
    // surfaced per-format so the menu can enable one and disable the other with its own reason.
    [Fact]
    public async Task Controller_Enables_Insert_But_Not_Update_When_The_Pk_Is_Not_Projected()
    {
        var origin = new ResultOrigin(
            new[] { new ColumnOrigin("CUSTOMERS", "NAME", IsComputed: false, SqlValueKind.Text) },
            new OriginShape.DirectTable("CUSTOMERS"));
        var controller = ControllerFor(origin, Customers());

        await controller.RefreshAvailabilityAsync(hasResult: true);

        Assert.True(controller.CanCopyAsInsert);
        Assert.False(controller.CanCopyAsUpdate);
        Assert.Equal(string.Empty, controller.CopyAsInsertTooltip);
        Assert.NotEqual(string.Empty, controller.CopyAsUpdateTooltip);
    }

    [Fact]
    public async Task Controller_Reset_Clears_Availability()
    {
        var controller = ControllerFor(DirectCustomers(), Customers());
        await controller.RefreshAvailabilityAsync(hasResult: true);
        Assert.True(controller.CanCopyAsInsert);

        controller.Reset();

        Assert.False(controller.CanCopyAsInsert);
        Assert.False(controller.CanCopyAsUpdate);
    }

    [Fact]
    public async Task Controller_With_No_Result_Reports_Nothing_Available()
    {
        var controller = ControllerFor(DirectCustomers(), Customers());

        await controller.RefreshAvailabilityAsync(hasResult: false);

        Assert.False(controller.CanCopyAsInsert);
        Assert.False(controller.CanCopyAsUpdate);
    }

    private static SqlCopyController ControllerFor(ResultOrigin origin, ISqlMetadataProvider catalog)
        => new(new SqlCopyCoordinator(_ => Task.FromResult(origin), () => catalog));
}
