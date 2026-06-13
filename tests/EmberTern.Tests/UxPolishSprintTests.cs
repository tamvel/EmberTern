using System;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// UX polish sprint (Session 5) coverage:
///   #1 false-modified detection (ComboBox load no longer corrupts TypeText/Domain)
///   #2 field-dependency navigation fires the open path
///   #4 trigger Insert/Update decode
///   #5 table context-menu visibility flags
///   #6 DROP TABLE generation + tab closing on delete
/// </summary>
public class UxPolishSprintTests
{
    // ─── #1 false-modified rows ───────────────────────────────────────────

    [Fact]
    public void FieldRow_FreshFromCatalog_IsNotModified()
    {
        // A row built straight from a FieldInfo (no user edit) must read as
        // unmodified — the prior bug nulled TypeText via the base-type ComboBox.
        var f = new FieldInfo { Name = "OPIS", Type = "VARCHAR(50)", Size = 50, Domain = "T_OPIS" };
        var row = new FieldRowViewModel(f);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void FieldRow_SelectedTypeItem_ReturnsBaseTypeForDisplay()
    {
        var f = new FieldInfo { Name = "OPIS", Type = "VARCHAR(50)", Size = 50 };
        var row = new FieldRowViewModel(f);
        // The ComboBox (base-type items) gets the base name, but TypeText keeps
        // the full string so IsModified stays false.
        Assert.Equal("VARCHAR", row.SelectedTypeItem);
        Assert.Equal("VARCHAR(50)", row.TypeText);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void FieldRow_SelectedTypeItem_NullWriteback_IsIgnored()
    {
        // Simulates the ComboBox resetting SelectedItem to null on load (value
        // not in items). The guard must leave TypeText untouched.
        var f = new FieldInfo { Name = "OPIS", Type = "VARCHAR(50)", Size = 50 };
        var row = new FieldRowViewModel(f);
        row.SelectedTypeItem = null;
        Assert.Equal("VARCHAR(50)", row.TypeText);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void FieldRow_SelectedTypeItem_SameBase_IsNoOp()
    {
        var f = new FieldInfo { Name = "OPIS", Type = "VARCHAR(50)", Size = 50 };
        var row = new FieldRowViewModel(f);
        row.SelectedTypeItem = "VARCHAR"; // same base — must not strip the size
        Assert.Equal("VARCHAR(50)", row.TypeText);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void FieldRow_SelectedTypeItem_RealChange_MarksModified()
    {
        var f = new FieldInfo { Name = "OPIS", Type = "VARCHAR(50)", Size = 50 };
        var row = new FieldRowViewModel(f);
        row.SelectedTypeItem = "INTEGER";
        Assert.Equal("INTEGER", row.TypeText);
        Assert.True(row.IsModified);
    }

    [Fact]
    public void FieldRow_DomainSpec_NullWriteback_DoesNotClearDomain()
    {
        var f = new FieldInfo { Name = "KWOTA", Type = "NUMERIC(15,2)", Size = 15, Scale = 2, Domain = "T_KWOTA" };
        var row = new FieldRowViewModel(f);
        // ComboBox can't resolve T_KWOTA (no AvailableDomains wired) → would set
        // SelectedDomainSpec to null. The guard must keep DomainName intact.
        row.SelectedDomainSpec = null;
        Assert.Equal("T_KWOTA", row.DomainName);
        Assert.False(row.IsModified);
    }

    // ─── #4 trigger Insert/Update decode ──────────────────────────────────

    [Theory]
    [InlineData(1, true, false, false)]   // BEFORE INSERT
    [InlineData(2, true, false, false)]   // AFTER INSERT
    [InlineData(3, false, true, false)]   // BEFORE UPDATE
    [InlineData(4, false, true, false)]   // AFTER UPDATE
    [InlineData(5, false, false, true)]   // BEFORE DELETE
    [InlineData(6, false, false, true)]   // AFTER DELETE
    [InlineData(17, true, true, false)]   // BEFORE INSERT OR UPDATE
    [InlineData(25, true, false, true)]   // BEFORE INSERT OR DELETE
    [InlineData(27, false, true, true)]   // BEFORE UPDATE OR DELETE
    [InlineData(113, true, true, true)]   // BEFORE INSERT OR UPDATE OR DELETE
    public void DecodeTriggerOps_MapsKnownTypes(int type, bool ins, bool upd, bool del)
    {
        var (i, u, d) = FirebirdTableDetailReader.DecodeTriggerOps(type);
        Assert.Equal(ins, i);
        Assert.Equal(upd, u);
        Assert.Equal(del, d);
    }

    [Fact]
    public void DecodeTriggerOps_DbLevelTrigger_IsAllFalse()
    {
        // DDL / DB triggers (type >= 8192) carry no DML semantics.
        var (i, u, d) = FirebirdTableDetailReader.DecodeTriggerOps(8192);
        Assert.False(i || u || d);
    }

    [Fact]
    public void DependedOnBySql_IncludesTriggerTypeColumn()
    {
        // Regression pin: the 4th column (RDB$TRIGGER_TYPE via LEFT JOIN) must
        // stay in the query so the panel can decode Insert/Update.
        Assert.Contains("RDB$TRIGGER_TYPE", FirebirdTableDetailReader.DependedOnBySql);
        Assert.Contains("LEFT JOIN RDB$TRIGGERS", FirebirdTableDetailReader.DependedOnBySql);
    }

    [Fact]
    public void FieldDependencyItem_TriggerMarks_ReflectFlags()
    {
        var dep = new DependencyInfo
        {
            ObjectName = "TR_X", ObjectType = "Trigger", FieldName = "ID",
            FiresOnInsert = true, FiresOnUpdate = false,
        };
        var item = new FieldDependencyItem(dep);
        Assert.Equal("✓", item.InsertMark);
        Assert.Equal(string.Empty, item.UpdateMark);
    }

    [Fact]
    public void FieldDependencyItem_NonTrigger_MarksAreBlank()
    {
        var dep = new DependencyInfo { ObjectName = "V_X", ObjectType = "View", FieldName = "ID" };
        var item = new FieldDependencyItem(dep);
        Assert.Equal(string.Empty, item.InsertMark);
        Assert.Equal(string.Empty, item.UpdateMark);
    }

    // ─── #2 dependency navigation ─────────────────────────────────────────

    [Fact]
    public void FieldDependencyItem_Navigate_FiresOpenObjectRequested()
    {
        var owner = new TableDetailTabViewModel("MY_T");
        MetadataObject? opened = null;
        owner.OpenObjectRequested += o => opened = o;

        var item = new FieldDependencyItem(
            new DependencyInfo { ObjectName = "TR_X", ObjectType = "Trigger", FieldName = "ID" },
            owner);

        Assert.True(item.NavigateCommand.CanExecute(null));
        item.NavigateCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Equal("TR_X", opened!.Name);
        Assert.Equal(MetadataObjectKind.Trigger, opened.Kind);
    }

    [Fact]
    public void FieldDependencyItem_NavigateSelected_RoutesThroughVm()
    {
        var owner = new TableDetailTabViewModel("MY_T");
        owner.DependedOnBy.Add(new DependencyInfo { ObjectName = "P_X", ObjectType = "Procedure", FieldName = "ID" });
        owner.Fields.Add(new FieldInfo { Name = "ID", Type = "INTEGER" });
        owner.SelectedField = owner.Fields[0];

        MetadataObject? opened = null;
        owner.OpenObjectRequested += o => opened = o;

        owner.SelectedFieldDependency = owner.FieldDependencies.First();
        owner.NavigateSelectedDependencyCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Equal("P_X", opened!.Name);
    }

    // ─── #6 DROP TABLE ────────────────────────────────────────────────────

    [Fact]
    public void BuildDropTable_QuotesIdentifier()
    {
        Assert.Equal("DROP TABLE \"MY_T\"", DdlGenerator.BuildDropTable("MY_T"));
        Assert.Equal("DROP TABLE \"lower\"", DdlGenerator.BuildDropTable("lower"));
    }

    [Fact]
    public void BuildDropTable_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropTable(""));
    }

    // ─── #5 table context-menu flags ──────────────────────────────────────

    [Fact]
    public void MetadataNode_TableGroup_FlagsCorrect()
    {
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Table);
        Assert.True(group.IsTableGroup);
        Assert.False(group.IsTableLeaf);
    }

    [Fact]
    public void MetadataNode_TableLeaf_FlagsCorrect()
    {
        using var h = new Harness();
        var leaf = MetadataNodeViewModel.CreateLeaf(h.Main.Metadata,
            new MetadataObject("MY_T", MetadataObjectKind.Table));
        Assert.True(leaf.IsTableLeaf);
        Assert.False(leaf.IsTableGroup);
    }

    [Fact]
    public void MetadataNode_ViewLeaf_NotTableFlags()
    {
        using var h = new Harness();
        var leaf = MetadataNodeViewModel.CreateLeaf(h.Main.Metadata,
            new MetadataObject("MY_V", MetadataObjectKind.View));
        Assert.False(leaf.IsTableLeaf);
        Assert.False(leaf.IsTableGroup);
    }

    [Fact]
    public void MetadataNode_DeleteTable_FiresDeleteTableRequested()
    {
        using var h = new Harness();
        MetadataObject? toDelete = null;
        h.Main.Metadata.DeleteTableRequested += o => toDelete = o;
        var leaf = MetadataNodeViewModel.CreateLeaf(h.Main.Metadata,
            new MetadataObject("MY_T", MetadataObjectKind.Table));
        leaf.DeleteTableCommand.Execute(null);
        Assert.NotNull(toDelete);
        Assert.Equal("MY_T", toDelete!.Name);
    }

    [Fact]
    public void MetadataNode_NewTable_FiresNewTableRequested()
    {
        using var h = new Harness();
        bool fired = false;
        h.Main.Metadata.NewTableRequested += () => fired = true;
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Table);
        group.NewTableCommand.Execute(null);
        Assert.True(fired);
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-ux-" + Guid.NewGuid().ToString("N"));
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
