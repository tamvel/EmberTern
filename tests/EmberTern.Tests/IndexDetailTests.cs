using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

// Index Detail: an index opens in a dedicated 2-tab surface (Index / DDL) — NOT a
// plain DDL tab. An index is read-mostly in Firebird (verified on FB 5.0.3): the only
// editable properties are Active/Inactive (plain indexes only — PK/FK/UNIQUE backing
// indexes reject it) and the description (COMMENT ON INDEX). No rename, no structural
// ALTER, no New-Index flow (creation lives in Table Detail → Indexes).
public class IndexDetailTests
{
    // ─── Routing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Index, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Generator, false)]
    [InlineData(MetadataObjectKind.Domain, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    public void OpensAsIndexDetail_IndexesOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsIndexDetail(kind));

    [Fact]
    public void Factory_Index_HasCompileAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateIndexDetail(new MetadataObject("IDX_X", MetadataObjectKind.Index));

        Assert.True(vm.CanCompile);            // DDL executor wired
        Assert.True(vm.CanDelete);             // plain index (not yet loaded → not constraint-backed)
        Assert.True(vm.CanRecomputeStatistics);
        Assert.Equal("IDX_X", vm.IndexName);
    }

    [Fact]
    public void DirectOpen_Index_OpensIndexDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("IDX_ORDERS_DATE", MetadataObjectKind.Index);
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "IDX_ORDERS_DATE");
        Assert.Equal(VmTabKind.IndexDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Index, tab.ObjectKind);
        Assert.NotNull(tab.IndexDetail);
    }

    [Fact]
    public void DirectOpen_Index_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("IDX_ORDERS_DATE", MetadataObjectKind.Index);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "IDX_ORDERS_DATE");
    }

    [Fact]
    public void Restore_IndexTab_NativeIndexDetail_NotDdl()
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
                            Kind = CoreTabKind.IndexDetail,
                            ObjectName = "IDX_ORDERS_DATE",
                            ObjectKind = MetadataObjectKind.Index,
                            DdlText = "CREATE INDEX \"IDX_ORDERS_DATE\" ON \"ORDERS\" (\"ORDER_DATE\");",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "IDX_ORDERS_DATE");
        Assert.Equal(VmTabKind.IndexDetail, tab.Kind);
        Assert.NotNull(tab.IndexDetail);
        Assert.Contains("IDX_ORDERS_DATE", tab.IndexDetail!.DdlText);
    }

    [Fact]
    public void Capture_IndexTab_PersistsAsIndexDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("IDX_ORDERS_DATE", MetadataObjectKind.Index));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.IndexDetail && t.ObjectName == "IDX_ORDERS_DATE");
    }

    // ─── DDL generation (pure) ────────────────────────────────────────────

    [Fact]
    public void BuildAlterIndexActive_Emits()
        => Assert.Equal("ALTER INDEX \"IDX_X\" ACTIVE", DdlGenerator.BuildAlterIndexActive("IDX_X"));

    [Fact]
    public void BuildAlterIndexInactive_Emits()
        => Assert.Equal("ALTER INDEX \"IDX_X\" INACTIVE", DdlGenerator.BuildAlterIndexInactive("IDX_X"));

    [Fact]
    public void BuildAlterIndexActive_EscapesInternalQuotes()
        => Assert.Equal("ALTER INDEX \"I\"\"X\" ACTIVE", DdlGenerator.BuildAlterIndexActive("I\"X"));

    [Theory]
    [InlineData("IDX_X", "note", "COMMENT ON INDEX \"IDX_X\" IS 'note'")]
    [InlineData("IDX_X", "", "COMMENT ON INDEX \"IDX_X\" IS NULL")]
    [InlineData("IDX_X", null, "COMMENT ON INDEX \"IDX_X\" IS NULL")]
    [InlineData("IDX_X", "it's", "COMMENT ON INDEX \"IDX_X\" IS 'it''s'")]
    public void BuildCommentIndex_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentIndex(name, comment));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildAlterIndexActive_ThrowsOnEmpty(string name)
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAlterIndexActive(name));

    [Fact]
    public void BuildIndexDdl_PlainSingleField()
    {
        var sql = DdlGenerator.BuildIndexDdl(new IndexDetailInfo
        {
            Name = "IDX_ORDERS_DATE", Table = "ORDERS", Fields = "ORDER_DATE",
        });
        Assert.Equal("CREATE INDEX \"IDX_ORDERS_DATE\" ON \"ORDERS\" (\"ORDER_DATE\");", sql);
    }

    [Fact]
    public void BuildIndexDdl_UniqueDescendingComposite()
    {
        var sql = DdlGenerator.BuildIndexDdl(new IndexDetailInfo
        {
            Name = "IX", Table = "T", Fields = "A,B", IsUnique = true, IsDescending = true,
        });
        Assert.Equal("CREATE UNIQUE DESCENDING INDEX \"IX\" ON \"T\" (\"A\", \"B\");", sql);
    }

    [Fact]
    public void BuildIndexDdl_ExpressionIndex_DoesNotDoubleParen()
    {
        // RDB$EXPRESSION_SOURCE already carries its own parens — emit as-is.
        var sql = DdlGenerator.BuildIndexDdl(new IndexDetailInfo
        {
            Name = "IX", Table = "T", Expression = "(UPPER(NAME))",
        });
        Assert.Equal("CREATE INDEX \"IX\" ON \"T\" COMPUTED BY (UPPER(NAME));", sql);
    }

    [Fact]
    public void BuildIndexDdl_Inactive_AppendsNote()
    {
        var sql = DdlGenerator.BuildIndexDdl(new IndexDetailInfo
        {
            Name = "IX", Table = "T", Fields = "A", IsActive = false,
        });
        Assert.Contains("/* INACTIVE */", sql);
    }

    [Fact]
    public void BuildIndexDdl_WithDescription_AppendsComment()
    {
        var sql = DdlGenerator.BuildIndexDdl(new IndexDetailInfo
        {
            Name = "IX", Table = "T", Fields = "A", Description = "lookup",
        });
        Assert.Contains("COMMENT ON INDEX \"IX\" IS 'lookup'", sql);
    }

    // ─── VM Compile SQL (BuildCompileSql) ─────────────────────────────────

    [Fact]
    public void BuildCompileSql_NoChange_IsEmpty()
    {
        var vm = new IndexDetailTabViewModel("IDX_X");
        Assert.Equal(string.Empty, vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_DeactivatePlainIndex_EmitsAlterInactive()
    {
        // Fresh VM baselines Active=true; setting false on a plain (non-constraint) index
        // emits ALTER INDEX … INACTIVE.
        var vm = new IndexDetailTabViewModel("IDX_X") { IsActive = false };
        Assert.Equal("ALTER INDEX \"IDX_X\" INACTIVE", vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_DescriptionChange_EmitsComment()
    {
        var vm = new IndexDetailTabViewModel("IDX_X") { EditableDescription = "lookup index" };
        Assert.Equal("COMMENT ON INDEX \"IDX_X\" IS 'lookup index'", vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_ConstraintBacked_DoesNotEmitActiveChange()
    {
        // A PK/UNIQUE/FK backing index can't be (de)activated — even if IsActive flips,
        // no ALTER is emitted (the toggle is also disabled in the UI).
        var vm = new IndexDetailTabViewModel("PK_T") { ConstraintType = "PRIMARY KEY", IsActive = false };
        Assert.Equal(string.Empty, vm.BuildCompileSql());
        Assert.DoesNotContain("ALTER INDEX", vm.BuildCompileSql());
    }

    // ─── Constraint-backed gating ─────────────────────────────────────────

    [Fact]
    public void ConstraintBacked_DisablesActiveAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateIndexDetail(new MetadataObject("PK_T", MetadataObjectKind.Index));
        vm.ConstraintType = "PRIMARY KEY";

        Assert.True(vm.IsConstraintBacked);
        Assert.False(vm.IsActiveEditable);          // Active toggle disabled
        Assert.False(vm.CanDelete);                 // Drop disabled
        Assert.NotEqual(string.Empty, vm.ConstraintBackedNote); // explanatory note shown
        Assert.Contains("PRIMARY KEY", vm.ConstraintBackedNote);
    }

    [Fact]
    public void PlainIndex_AllowsActiveAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateIndexDetail(new MetadataObject("IDX_X", MetadataObjectKind.Index));
        Assert.False(vm.IsConstraintBacked);
        Assert.True(vm.IsActiveEditable);
        Assert.True(vm.CanDelete);
        Assert.Equal(string.Empty, vm.ConstraintBackedNote);
    }

    [Fact]
    public void SortDirectionText_ReflectsDescending()
    {
        var asc = new IndexDetailTabViewModel("IX") { IsDescending = false };
        var desc = new IndexDetailTabViewModel("IX") { IsDescending = true };
        Assert.Equal("ASCENDING", asc.SortDirectionText);
        Assert.Equal("DESCENDING", desc.SortDirectionText);
    }

    // ─── Dirty tracking + gating ──────────────────────────────────────────

    [Fact]
    public void EditingActive_MarksDirty()
    {
        var vm = new IndexDetailTabViewModel("IDX_X");
        Assert.False(vm.IsDirty);
        vm.IsActive = false;
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditingDescription_MarksDirty()
    {
        var vm = new IndexDetailTabViewModel("IDX_X");
        Assert.False(vm.IsDirty);
        vm.EditableDescription = "x";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void NoExecutor_CannotCompileOrDelete()
    {
        var vm = new IndexDetailTabViewModel("IDX_X");
        Assert.False(vm.CanCompile);
        Assert.False(vm.CanDelete);
        Assert.False(vm.CanRecomputeStatistics);
    }

    [Fact]
    public async Task ExecuteCompile_NoExecutor_IsNoOp()
    {
        var vm = new IndexDetailTabViewModel("IDX_X") { IsActive = false };
        await vm.ExecuteCompileAsync();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void CanRevert_OnlyWhenDirty()
    {
        var vm = new IndexDetailTabViewModel("IDX_X");
        Assert.False(vm.CanRevertChanges);  // clean
        vm.IsActive = false;
        Assert.True(vm.CanRevertChanges);   // dirty
    }

    [Fact]
    public async Task DeleteCommand_RaisesDeleteRequested_AfterConfirm()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new IndexDetailTabViewModel("IDX_X", null, executor);
        var raised = false;
        vm.DeleteRequested += _ => { raised = true; return Task.CompletedTask; };

        // No ConfirmationRequested handler → RequestConfirmAsync auto-proceeds.
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.True(raised);
    }

    [Fact]
    public async Task DeleteCommand_ConstraintBacked_DoesNotRaise()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new IndexDetailTabViewModel("PK_T", null, executor) { ConstraintType = "UNIQUE" };
        var raised = false;
        vm.DeleteRequested += _ => { raised = true; return Task.CompletedTask; };

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.False(raised); // CanDelete is false → command no-ops
    }

    // ─── Unsaved-work (WorkGuard) ──────────────────────────────────────────

    [Fact]
    public void GetUnsavedWork_Clean_IsNull()
        => Assert.Null(new IndexDetailTabViewModel("IDX_X").GetUnsavedWork());

    [Fact]
    public void GetUnsavedWork_Dirty_IsModifiedSource()
    {
        var vm = new IndexDetailTabViewModel("IDX_X") { EditableDescription = "x" };
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
    }

    // ─── Reader SQL shape + helper ─────────────────────────────────────────

    [Fact]
    public void IndexDetailSql_QueriesIndicesByNameWithDescriptionAndUnique()
    {
        var sql = FirebirdTableDetailReader.IndexDetailSql;
        Assert.Contains("RDB$INDICES", sql);
        Assert.Contains("@name", sql);
        Assert.Contains("RDB$DESCRIPTION", sql);
        Assert.Contains("RDB$RELATION_NAME", sql);
        Assert.Contains("RDB$EXPRESSION_SOURCE", sql);
        // The single-index query keeps UNIQUE in the constraint subquery (the grid
        // query narrows to PK/FK) so a UNIQUE-backing index is recognized.
        Assert.Contains("'UNIQUE'", sql);
    }

    [Theory]
    [InlineData("PRIMARY KEY", "PRIMARY KEY")]
    [InlineData("FOREIGN KEY", "FOREIGN KEY")]
    [InlineData("UNIQUE", "UNIQUE")]
    [InlineData("unique", "UNIQUE")]
    [InlineData("CHECK", "")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void NormalizeConstraintIndexType_Cases(string? raw, string expected)
        => Assert.Equal(expected, FirebirdTableDetailReader.NormalizeConstraintIndexType(raw));

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
