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

// Generator Detail: a generator opens in a dedicated 3-tab surface (Generator /
// Dependencies / DDL) — NOT a plain DDL tab. The form (current / initial /
// increment / description) is persisted via Save, which emits CREATE SEQUENCE /
// ALTER SEQUENCE / COMMENT ON SEQUENCE — never a direct UPDATE on RDB$ tables.
public class GeneratorDetailTests
{
    // ─── Routing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Generator, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.Function, false)]
    [InlineData(MetadataObjectKind.Domain, false)]
    public void OpensAsGeneratorDetail_GeneratorsOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsGeneratorDetail(kind));

    [Fact]
    public void Factory_Generator_HasSaveAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateGeneratorDetail(new MetadataObject("GEN_X", MetadataObjectKind.Generator));

        Assert.True(vm.CanSave);          // DDL executor wired
        Assert.True(vm.CanDelete);        // existing generator
        Assert.Equal("GEN_X", vm.GeneratorName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_Generator_OpensGeneratorDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("GEN_ORDERS", MetadataObjectKind.Generator);
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "GEN_ORDERS");
        Assert.Equal(VmTabKind.GeneratorDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Generator, tab.ObjectKind);
        Assert.NotNull(tab.GeneratorDetail);
    }

    [Fact]
    public void DirectOpen_Generator_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("GEN_ORDERS", MetadataObjectKind.Generator);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "GEN_ORDERS");
    }

    [Fact]
    public void Restore_GeneratorTab_NativeGeneratorDetail_NotDdl()
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
                            Kind = CoreTabKind.GeneratorDetail,
                            ObjectName = "GEN_ORDERS",
                            ObjectKind = MetadataObjectKind.Generator,
                            DdlText = "CREATE SEQUENCE \"GEN_ORDERS\";",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "GEN_ORDERS");
        Assert.Equal(VmTabKind.GeneratorDetail, tab.Kind);
        Assert.NotNull(tab.GeneratorDetail);
        Assert.Contains("GEN_ORDERS", tab.GeneratorDetail!.DdlText);
    }

    [Fact]
    public void Capture_GeneratorTab_PersistsAsGeneratorDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("GEN_ORDERS", MetadataObjectKind.Generator));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.GeneratorDetail && t.ObjectName == "GEN_ORDERS");
    }

    [Fact]
    public void NewGeneratorCommand_ReNotifiesCanExecute_OnConnectionChange()
    {
        using var harness = new Harness();
        var fired = false;
        harness.Main.NewGeneratorCommand.CanExecuteChanged += (_, _) => fired = true;

        harness.Main.ApplyActiveConnectionChange("A");

        Assert.True(fired);
    }

    // ─── DDL generation (pure) ────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1, "CREATE SEQUENCE \"GEN_X\"")]
    [InlineData(10, 1, "CREATE SEQUENCE \"GEN_X\" START WITH 10")]
    [InlineData(0, 5, "CREATE SEQUENCE \"GEN_X\" INCREMENT BY 5")]
    [InlineData(100, 2, "CREATE SEQUENCE \"GEN_X\" START WITH 100 INCREMENT BY 2")]
    public void BuildCreateSequence_Cases(long start, long inc, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCreateSequence("GEN_X", start, inc));

    // Version-INDEPENDENT raw-counter set — GEN_ID(,0) becomes exactly the value on
    // both FB3 and FB5 (empirically verified). This is what the Current Value edit uses.
    [Fact]
    public void BuildSetGenerator_Emits()
        => Assert.Equal("SET GENERATOR \"GEN_X\" TO 41247", DdlGenerator.BuildSetGenerator("GEN_X", 41247));

    // Version-DEPENDENT (FB3: GEN_ID(,0)=v; FB5: =v-1). Kept + shape-pinned, but the
    // VM uses SET GENERATOR for the Current Value to avoid the FB5 off-by-one.
    [Fact]
    public void BuildAlterSequenceRestart_Emits()
        => Assert.Equal("ALTER SEQUENCE \"GEN_X\" RESTART WITH 41247", DdlGenerator.BuildAlterSequenceRestart("GEN_X", 41247));

    [Fact]
    public void BuildAlterSequenceStartWith_Emits()
        => Assert.Equal("ALTER SEQUENCE \"GEN_X\" START WITH 5", DdlGenerator.BuildAlterSequenceStartWith("GEN_X", 5));

    [Fact]
    public void BuildAlterSequenceIncrement_Emits()
        => Assert.Equal("ALTER SEQUENCE \"GEN_X\" INCREMENT BY 3", DdlGenerator.BuildAlterSequenceIncrement("GEN_X", 3));

    [Fact]
    public void BuildDropSequence_Emits()
        => Assert.Equal("DROP SEQUENCE \"GEN_X\"", DdlGenerator.BuildDropSequence("GEN_X"));

    [Theory]
    [InlineData("GEN_X", "note", "COMMENT ON SEQUENCE \"GEN_X\" IS 'note'")]
    [InlineData("GEN_X", "", "COMMENT ON SEQUENCE \"GEN_X\" IS NULL")]
    [InlineData("GEN_X", null, "COMMENT ON SEQUENCE \"GEN_X\" IS NULL")]
    [InlineData("GEN_X", "it's", "COMMENT ON SEQUENCE \"GEN_X\" IS 'it''s'")]
    public void BuildCommentSequence_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentSequence(name, comment));

    [Fact]
    public void BuildCreateSequence_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCreateSequence("", 0, 1));

    // ─── VM Save SQL (BuildSaveSql) ───────────────────────────────────────

    [Fact]
    public void BuildSaveSql_New_Default_EmitsStartWith0AndSetGenerator0()
    {
        // New generator with default initial 0. forceStartWith records
        // RDB$INITIAL_VALUE=0 (even FB4+ would otherwise default it to 1), and
        // SET GENERATOR TO 0 pins the runtime counter so Current == Initial == 0.
        var vm = new GeneratorDetailTabViewModel("G")
        {
            IsNew = true,
            EditableName = "GEN_NEW",
            InitialValue = 0,
            Increment = 1,
            CurrentValue = 0,
        };
        Assert.Equal("CREATE SEQUENCE \"GEN_NEW\" START WITH 0;\nSET GENERATOR \"GEN_NEW\" TO 0", vm.BuildSaveSql());
    }

    [Fact]
    public void BuildSaveSql_New_WithDescription_AppendsComment()
    {
        var vm = new GeneratorDetailTabViewModel("G")
        {
            IsNew = true,
            EditableName = "GEN_NEW",
            InitialValue = 0,
            Increment = 1,
            CurrentValue = 0,
            EditableDescription = "counter",
        };
        var sql = vm.BuildSaveSql();
        Assert.Contains("CREATE SEQUENCE \"GEN_NEW\" START WITH 0", sql);
        Assert.Contains("SET GENERATOR \"GEN_NEW\" TO 0", sql);
        Assert.Contains("COMMENT ON SEQUENCE \"GEN_NEW\" IS 'counter'", sql);
    }

    [Fact]
    public void BuildSaveSql_New_WithInitial_CurrentEqualsInitial()
    {
        // The user-reported bug: after creating with Initial Value 10000, Current Value
        // showed 9999 (FB5 stores START WITH at initial-increment). The fix pins the
        // runtime counter to the initial value via SET GENERATOR so Current == Initial
        // on BOTH FB3 and FB5 (verified). NO RESTART (version-dependent / off-by-one).
        var vm = new GeneratorDetailTabViewModel("G")
        {
            IsNew = true,
            EditableName = "GEN_NEW",
            InitialValue = 10000,
            Increment = 1,
            CurrentValue = 0,
        };
        var sql = vm.BuildSaveSql();
        Assert.Equal("CREATE SEQUENCE \"GEN_NEW\" START WITH 10000;\nSET GENERATOR \"GEN_NEW\" TO 10000", sql);
        Assert.DoesNotContain("RESTART", sql);
    }

    [Fact]
    public void BuildSaveSql_New_WithIncrement_EmitsIncrementBy()
    {
        var vm = new GeneratorDetailTabViewModel("G")
        {
            IsNew = true,
            EditableName = "GEN_NEW",
            InitialValue = 100,
            Increment = 5,
            CurrentValue = 0,
        };
        Assert.Equal("CREATE SEQUENCE \"GEN_NEW\" START WITH 100 INCREMENT BY 5;\nSET GENERATOR \"GEN_NEW\" TO 100", vm.BuildSaveSql());
    }

    [Fact]
    public void BuildCreateSequence_ForceStartWith_EmitsStartWith0()
        => Assert.Equal("CREATE SEQUENCE \"GEN_X\" START WITH 0", DdlGenerator.BuildCreateSequence("GEN_X", 0, 1, forceStartWith: true));

    [Fact]
    public void BuildSaveSql_Existing_NoChange_IsEmpty()
    {
        // Fresh existing VM: properties equal their (default) baselines → no statements.
        var vm = new GeneratorDetailTabViewModel("GEN_X");
        Assert.Equal(string.Empty, vm.BuildSaveSql());
    }

    [Fact]
    public void BuildSaveSql_Existing_CurrentChange_EmitsSetGenerator()
    {
        // Existing generator's Current Value edit → version-independent SET GENERATOR
        // (NOT RESTART WITH, which is off-by-one on FB5).
        var vm = new GeneratorDetailTabViewModel("GEN_X") { CurrentValue = 9999 };
        Assert.Equal("SET GENERATOR \"GEN_X\" TO 9999", vm.BuildSaveSql());
        Assert.DoesNotContain("RESTART", vm.BuildSaveSql());
    }

    [Theory]
    [InlineData(-1, 0)]   // FB5 pre-first-use sentinel for a START WITH 0 sequence
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(41247, 41247)]
    public void NormalizeDisplayCurrentValue_ClampsNegativeToZero(long raw, long expected)
        => Assert.Equal(expected, GeneratorDetailTabViewModel.NormalizeDisplayCurrentValue(raw));

    [Fact]
    public void Editability_NewVsExisting()
    {
        var fresh = new GeneratorDetailTabViewModel("G") { IsNew = true };
        Assert.True(fresh.IsDefinitionEditable);       // Initial/Increment editable when creating
        Assert.False(fresh.IsCurrentValueEditable);    // Current Value is a consequence, read-only

        var existing = new GeneratorDetailTabViewModel("GEN_X");
        Assert.False(existing.IsDefinitionEditable);   // definition is read-only after create
        Assert.True(existing.IsCurrentValueEditable);  // runtime counter editable (SET GENERATOR)
    }

    [Fact]
    public void CanRefreshCurrentValue_OnlyExistingWithReader()
    {
        var noReader = new GeneratorDetailTabViewModel("GEN_X");
        Assert.False(noReader.CanRefreshCurrentValue);   // no reader

        using var harness = new Harness();
        var existing = harness.Main.CreateGeneratorDetail(new MetadataObject("GEN_X", MetadataObjectKind.Generator));
        Assert.True(existing.CanRefreshCurrentValue);    // reader wired, existing
    }

    // ─── Dirty tracking + gating ──────────────────────────────────────────

    [Fact]
    public void EditingCurrentValue_MarksDirty()
    {
        var vm = new GeneratorDetailTabViewModel("GEN_X");
        Assert.False(vm.IsDirty);
        vm.CurrentValue = 42;
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void NoExecutor_CannotSaveOrDelete()
    {
        var vm = new GeneratorDetailTabViewModel("GEN_X");
        Assert.False(vm.CanSave);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task ExecuteSave_NoExecutor_IsNoOp()
    {
        var vm = new GeneratorDetailTabViewModel("GEN_X") { CurrentValue = 5 };
        await vm.ExecuteSaveAsync();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void CanRevert_OnlyWhenDirtyAndExisting()
    {
        var existing = new GeneratorDetailTabViewModel("GEN_X");
        Assert.False(existing.CanRevertChanges);   // clean
        existing.CurrentValue = 1;
        Assert.True(existing.CanRevertChanges);     // dirty + existing

        var fresh = new GeneratorDetailTabViewModel("G") { IsNew = true, EditableName = "GEN_NEW" };
        Assert.False(fresh.CanRevertChanges);       // new — no DB state to revert to
    }

    [Fact]
    public void CanDelete_NotForNewGenerator()
    {
        // Executor wired but IsNew → Delete is unavailable (nothing to drop yet).
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new GeneratorDetailTabViewModel("G", null, null, executor) { IsNew = true };
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task DeleteCommand_RaisesDeleteRequested_AfterConfirm()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new GeneratorDetailTabViewModel("GEN_X", null, null, executor);
        var raised = false;
        vm.DeleteRequested += _ => { raised = true; return Task.CompletedTask; };

        // No ConfirmationRequested handler → RequestConfirmAsync auto-proceeds.
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.True(raised);
    }

    // ─── Unsaved-work (WorkGuard) ──────────────────────────────────────────

    [Fact]
    public void GetUnsavedWork_Clean_IsNull()
    {
        var vm = new GeneratorDetailTabViewModel("GEN_X");
        Assert.Null(vm.GetUnsavedWork());
    }

    [Fact]
    public void GetUnsavedWork_NewDirty_IsNewObject()
    {
        var vm = new GeneratorDetailTabViewModel("G") { IsNew = true, EditableName = "GEN_NEW" };
        vm.CurrentValue = 1; // ensure dirty
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.NewObject, work!.Kind);
    }

    [Fact]
    public void GetUnsavedWork_ExistingDirty_IsModifiedSource()
    {
        var vm = new GeneratorDetailTabViewModel("GEN_X") { CurrentValue = 1 };
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
    }

    // ─── Dependency navigation ─────────────────────────────────────────────

    [Fact]
    public void RequestOpen_FiresOpenObjectRequested_WithMappedKind()
    {
        var vm = new GeneratorDetailTabViewModel("GEN_X");
        MetadataObject? opened = null;
        vm.OpenObjectRequested += o => opened = o;

        vm.RequestOpen(new DependencyLeafNode
        {
            Dependency = new DependencyInfo { ObjectName = "TR_AUDIT", ObjectType = "Trigger" },
        });

        Assert.NotNull(opened);
        Assert.Equal("TR_AUDIT", opened!.Name);
        Assert.Equal(MetadataObjectKind.Trigger, opened.Kind);
    }

    // ─── Reader SQL shape (generator dependencies use type 14) ────────────

    [Fact]
    public void GeneratorDependencySql_UsesType14()
    {
        Assert.Contains("RDB$DEPENDENT_TYPE = 14", FirebirdTableDetailReader.GeneratorDependsOnSql);
        Assert.Contains("RDB$DEPENDED_ON_TYPE = 14", FirebirdTableDetailReader.GeneratorDependedOnBySql);
    }

    [Fact]
    public void MapObjectType_14_IsGenerator()
        => Assert.Equal("Generator", FirebirdTableDetailReader.MapObjectType(14));

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
