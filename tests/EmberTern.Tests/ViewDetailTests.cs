using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

// View Detail V1: a view opens in a dedicated 6-tab surface (SQL / Fields /
// Dependencies / Data / Description / DDL) — NOT a plain DDL tab and NOT the
// table detail VM. The editable SQL source compiles via CREATE OR ALTER VIEW.
public class ViewDetailTests
{
    [Theory]
    [InlineData(MetadataObjectKind.View, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.SystemTable, false)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.Function, false)]
    public void OpensAsViewDetail_ViewsOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsViewDetail(kind));

    [Fact]
    public void Factory_View_HasCompileButNoData()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateViewDetail(new MetadataObject("V_CUSTOMERS", MetadataObjectKind.View));

        // The SQL source is editable → Compile is available (DDL executor wired).
        Assert.True(vm.CanCompile);
        // A view's data is read-only — no data editor, so no inline editing.
        Assert.Equal("V_CUSTOMERS", vm.ViewName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_View_OpensViewDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("V_ORDERS", MetadataObjectKind.View);
        // Same path as a double-click on the sidebar leaf. No live connection, so the
        // background load fails and is swallowed — but the tab is added synchronously.
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "V_ORDERS");
        Assert.Equal(VmTabKind.ViewDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.View, tab.ObjectKind);
        Assert.NotNull(tab.ViewDetail);
    }

    [Fact]
    public void DirectOpen_View_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("V_ORDERS", MetadataObjectKind.View);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "V_ORDERS");
    }

    [Fact]
    public void Restore_ViewTab_NativeViewDetail_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.RestoreWorkspace(new WorkspaceState
        {
            Workspaces =
            {
                ["A"] = new ConnectionWorkspace
                {
                    Tabs =
                    {
                        new WorkspaceTab
                        {
                            Kind = CoreTabKind.ViewDetail,
                            ObjectName = "V_ORDERS",
                            ObjectKind = MetadataObjectKind.View,
                            DdlText = "CREATE VIEW \"V_ORDERS\" AS SELECT 1 FROM RDB$DATABASE;",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "V_ORDERS");
        Assert.Equal(VmTabKind.ViewDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.View, tab.ObjectKind);
        Assert.NotNull(tab.ViewDetail);
        // Cached DDL seeds the DDL tab before the (lazy) re-fetch.
        Assert.Contains("V_ORDERS", tab.ViewDetail!.DdlText);
    }

    [Fact]
    public void Capture_ViewTab_PersistsAsViewDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("V_ORDERS", MetadataObjectKind.View));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.ViewDetail && t.ObjectName == "V_ORDERS");
    }

    // ─── ViewDetailTabViewModel unit behavior (no live FB) ────────────────

    [Fact]
    public void NoExecutor_CannotCompile()
    {
        var vm = new ViewDetailTabViewModel("V_X");
        Assert.False(vm.CanCompile);
    }

    [Fact]
    public async Task ExecuteCompile_NoExecutor_IsNoOp()
    {
        var vm = new ViewDetailTabViewModel("V_X") { SourceText = "CREATE OR ALTER VIEW V_X AS SELECT 1 FROM RDB$DATABASE" };
        await vm.ExecuteCompileAsync();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteCompile_EmptySource_IsNoOp()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateViewDetail(new MetadataObject("V_X", MetadataObjectKind.View));
        vm.SourceText = "   ";
        await vm.ExecuteCompileAsync();
        // Whitespace-only source short-circuits before any execution attempt.
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task NewMode_LoadAsync_IsNoOp()
    {
        // A New View VM has nothing to load until it's compiled; LoadAsync returns
        // immediately without touching readers (which are null here anyway).
        var vm = new ViewDetailTabViewModel("NEW_VIEW") { IsNew = true, SourceText = ViewDetailTabViewModel.NewViewTemplate };
        await vm.LoadAsync();
        Assert.False(vm.IsLoading);
        Assert.Empty(vm.Fields);
    }

    [Theory]
    [InlineData("CREATE VIEW V_FOO (A) AS SELECT 1 FROM RDB$DATABASE", "V_FOO")]
    [InlineData("create or alter view v_bar as select 1 from rdb$database", "V_BAR")]
    [InlineData("CREATE OR ALTER VIEW \"MixedCase\" AS SELECT 1", "MixedCase")]
    [InlineData("CREATE VIEW   V_SPACED\n(\n  X\n)\nAS SELECT 1", "V_SPACED")]
    [InlineData("CREATE TABLE T (X INTEGER)", null)]            // not a view
    [InlineData("SELECT * FROM FOO", null)]                      // not a CREATE
    [InlineData("CREATE OR REPLACE VIEW V AS SELECT 1", null)]   // OR must be OR ALTER
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryParseViewName_Cases(string? sql, string? expected)
        => Assert.Equal(expected, ViewDetailTabViewModel.TryParseViewName(sql));

    // ─── Follow-up #1: SQL formatting (reuses the shared SqlFormatter) ────

    [Fact]
    public void FormatSql_FormatsSourceWholesale_WhenNoSelectionCallback()
    {
        var vm = new ViewDetailTabViewModel("V_X") { SourceText = "SELECT 1 FROM RDB$DATABASE" };
        var expected = SqlFormatter.Format("SELECT 1 FROM RDB$DATABASE");

        vm.FormatSqlCommand.Execute(null);

        Assert.Equal(expected, vm.SourceText);
        Assert.NotEqual("SELECT 1 FROM RDB$DATABASE", vm.SourceText);
    }

    [Fact]
    public void FormatSql_UsesSelectionCallback_WhenWired()
    {
        var vm = new ViewDetailTabViewModel("V_X") { SourceText = "ignored full text" };
        string? replaced = null;
        vm.SelectedTextProvider = () => "SELECT 1 FROM RDB$DATABASE";
        vm.ReplaceSelectedOrAllText = t => replaced = t;

        vm.FormatSqlCommand.Execute(null);

        // The selection (not the full source) was formatted and pushed through replace.
        Assert.Equal(SqlFormatter.Format("SELECT 1 FROM RDB$DATABASE"), replaced);
    }

    [Fact]
    public void FormatSql_EmptySource_IsNoOp()
    {
        var vm = new ViewDetailTabViewModel("V_X") { SourceText = string.Empty };
        vm.FormatSqlCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SourceText);
    }

    // ─── Follow-up #2: editable description (COMMENT ON VIEW) ─────────────

    [Fact]
    public void Factory_View_CanEditDescription()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateViewDetail(new MetadataObject("V_X", MetadataObjectKind.View));
        Assert.True(vm.CanEditDescription);
    }

    [Fact]
    public void NoExecutor_CannotEditDescription()
    {
        var vm = new ViewDetailTabViewModel("V_X");
        Assert.False(vm.CanEditDescription);
    }

    [Fact]
    public void Description_MirrorsIntoEditableCopy()
    {
        var vm = new ViewDetailTabViewModel("V_X");
        vm.Description = "Order summary view";
        Assert.Equal("Order summary view", vm.EditableDescription);
    }

    [Theory]
    [InlineData("V_ORDERS", "Order summary", "COMMENT ON VIEW \"V_ORDERS\" IS 'Order summary'")]
    [InlineData("V_ORDERS", "", "COMMENT ON VIEW \"V_ORDERS\" IS NULL")]
    [InlineData("V_ORDERS", null, "COMMENT ON VIEW \"V_ORDERS\" IS NULL")]
    [InlineData("V_X", "it's", "COMMENT ON VIEW \"V_X\" IS 'it''s'")]
    public void BuildCommentView_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentView(name, comment));

    [Fact]
    public void BuildCommentView_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCommentView("", "x"));

    [Fact]
    public void BuildCommentTable_StillEmitsTableForm()
        => Assert.Equal("COMMENT ON TABLE \"T\" IS 'x'", DdlGenerator.BuildCommentTable("T", "x"));

    // ─── Follow-up #3: New View command re-enables on connection change ───

    [Fact]
    public void NewViewCommand_ReNotifiesCanExecute_OnConnectionChange()
    {
        using var harness = new Harness();
        var fired = false;
        harness.Main.NewViewCommand.CanExecuteChanged += (_, _) => fired = true;

        // The connection-change path must re-notify NewViewCommand (the regression:
        // it only re-notified NewTableCommand, so the New View button stayed stuck
        // at its construction-time CanExecute=false and never enabled on connect).
        harness.Main.ApplyActiveConnectionChange("A");

        Assert.True(fired);
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
