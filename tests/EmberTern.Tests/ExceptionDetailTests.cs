using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

// Exception Detail: a custom EXCEPTION opens in a dedicated 4-tab surface
// (Exception / Description / Dependencies / DDL) — NOT a plain DDL tab. The form
// (name + message + description) is persisted via Compile, which emits
// CREATE / ALTER EXCEPTION + COMMENT ON EXCEPTION — never a direct UPDATE on RDB$.
// Closest analog: Generator / Domain Detail (form-based, no PSQL body).
public class ExceptionDetailTests
{
    // ─── Routing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Exception, true)]
    [InlineData(MetadataObjectKind.Generator, false)]
    [InlineData(MetadataObjectKind.Domain, false)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    public void OpensAsExceptionDetail_ExceptionsOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsExceptionDetail(kind));

    [Fact]
    public void Factory_Exception_HasCompileAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateExceptionDetail(new MetadataObject("E_X", MetadataObjectKind.Exception));

        Assert.True(vm.CanCompile);    // DDL executor wired
        Assert.True(vm.CanDelete);     // existing exception
        Assert.Equal("E_X", vm.ExceptionName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_Exception_OpensExceptionDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("E_CUSTOMER_NOT_FOUND", MetadataObjectKind.Exception);
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "E_CUSTOMER_NOT_FOUND");
        Assert.Equal(VmTabKind.ExceptionDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Exception, tab.ObjectKind);
        Assert.NotNull(tab.ExceptionDetail);
    }

    [Fact]
    public void DirectOpen_Exception_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("E_ORDER_LOCKED", MetadataObjectKind.Exception);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "E_ORDER_LOCKED");
    }

    [Fact]
    public void Restore_ExceptionTab_NativeExceptionDetail_NotDdl()
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
                            Kind = CoreTabKind.ExceptionDetail,
                            ObjectName = "E_NEGATIVE_AMOUNT",
                            ObjectKind = MetadataObjectKind.Exception,
                            DdlText = "CREATE EXCEPTION \"E_NEGATIVE_AMOUNT\" 'x';",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "E_NEGATIVE_AMOUNT");
        Assert.Equal(VmTabKind.ExceptionDetail, tab.Kind);
        Assert.NotNull(tab.ExceptionDetail);
        Assert.Contains("E_NEGATIVE_AMOUNT", tab.ExceptionDetail!.DdlText);
    }

    [Fact]
    public void Capture_ExceptionTab_PersistsAsExceptionDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("E_CUSTOMER_NOT_FOUND", MetadataObjectKind.Exception));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.ExceptionDetail && t.ObjectName == "E_CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public void NewExceptionCommand_ReNotifiesCanExecute_OnConnectionChange()
    {
        using var harness = new Harness();
        var fired = false;
        harness.Main.NewExceptionCommand.CanExecuteChanged += (_, _) => fired = true;

        harness.Main.ApplyActiveConnectionChange("A");

        Assert.True(fired);
    }

    // ─── DDL generation (pure) ────────────────────────────────────────────

    [Theory]
    [InlineData("XXX_BLAD_TEST", "Niepoprawny rodzaj dokumentacji", "CREATE EXCEPTION \"XXX_BLAD_TEST\" 'Niepoprawny rodzaj dokumentacji'")]
    [InlineData("E_X", "", "CREATE EXCEPTION \"E_X\" ''")]
    [InlineData("E_X", "it's", "CREATE EXCEPTION \"E_X\" 'it''s'")]
    public void BuildCreateException_Cases(string name, string? message, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCreateException(name, message));

    [Theory]
    [InlineData("XXX_BLAD_TEST", "Nowa tresc wyjatku", "ALTER EXCEPTION \"XXX_BLAD_TEST\" 'Nowa tresc wyjatku'")]
    [InlineData("E_X", "don't", "ALTER EXCEPTION \"E_X\" 'don''t'")]
    public void BuildAlterException_Cases(string name, string? message, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildAlterException(name, message));

    [Fact]
    public void BuildDropException_Emits()
        => Assert.Equal("DROP EXCEPTION \"XXX_BLAD_TEST\"", DdlGenerator.BuildDropException("XXX_BLAD_TEST"));

    [Theory]
    [InlineData("E_X", "note", "COMMENT ON EXCEPTION \"E_X\" IS 'note'")]
    [InlineData("E_X", "", "COMMENT ON EXCEPTION \"E_X\" IS NULL")]
    [InlineData("E_X", null, "COMMENT ON EXCEPTION \"E_X\" IS NULL")]
    [InlineData("E_X", "it's", "COMMENT ON EXCEPTION \"E_X\" IS 'it''s'")]
    public void BuildCommentException_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentException(name, comment));

    [Fact]
    public void BuildCreateException_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCreateException("", "x"));

    [Fact]
    public void BuildAlterException_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAlterException("", "x"));

    [Fact]
    public void BuildDropException_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropException(""));

    // ─── VM Compile SQL (BuildCompileSql) ─────────────────────────────────

    [Fact]
    public void BuildCompileSql_New_EmitsCreate()
    {
        var vm = new ExceptionDetailTabViewModel("E")
        {
            IsNew = true,
            EditableName = "XXX_BLAD_TEST",
            Message = "Niepoprawny rodzaj dokumentacji",
        };
        Assert.Equal("CREATE EXCEPTION \"XXX_BLAD_TEST\" 'Niepoprawny rodzaj dokumentacji'", vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_New_WithDescription_AppendsComment()
    {
        var vm = new ExceptionDetailTabViewModel("E")
        {
            IsNew = true,
            EditableName = "E_NEW",
            Message = "msg",
            EditableDescription = "opis",
        };
        var sql = vm.BuildCompileSql();
        Assert.Contains("CREATE EXCEPTION \"E_NEW\" 'msg'", sql);
        Assert.Contains("COMMENT ON EXCEPTION \"E_NEW\" IS 'opis'", sql);
    }

    [Fact]
    public void BuildCompileSql_Existing_NoChange_IsEmpty()
    {
        // Fresh existing VM: message + description equal their (empty) baselines.
        var vm = new ExceptionDetailTabViewModel("E_X");
        Assert.Equal(string.Empty, vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_Existing_MessageChange_EmitsAlter()
    {
        var vm = new ExceptionDetailTabViewModel("E_X") { Message = "Nowa tresc" };
        Assert.Equal("ALTER EXCEPTION \"E_X\" 'Nowa tresc'", vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_Existing_DescriptionChange_EmitsComment()
    {
        var vm = new ExceptionDetailTabViewModel("E_X") { EditableDescription = "opis" };
        Assert.Equal("COMMENT ON EXCEPTION \"E_X\" IS 'opis'", vm.BuildCompileSql());
    }

    [Fact]
    public void BuildCompileSql_Existing_MessageAndDescription_EmitsBoth()
    {
        var vm = new ExceptionDetailTabViewModel("E_X") { Message = "m", EditableDescription = "d" };
        var sql = vm.BuildCompileSql();
        Assert.Contains("ALTER EXCEPTION \"E_X\" 'm'", sql);
        Assert.Contains("COMMENT ON EXCEPTION \"E_X\" IS 'd'", sql);
    }

    // ─── Dirty tracking + gating ──────────────────────────────────────────

    [Fact]
    public void EditingMessage_MarksDirty()
    {
        var vm = new ExceptionDetailTabViewModel("E_X");
        Assert.False(vm.IsDirty);
        vm.Message = "changed";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditingDescription_MarksDirty()
    {
        var vm = new ExceptionDetailTabViewModel("E_X");
        Assert.False(vm.IsDirty);
        vm.EditableDescription = "opis";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void NoExecutor_CannotCompileOrDelete()
    {
        var vm = new ExceptionDetailTabViewModel("E_X");
        Assert.False(vm.CanCompile);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task ExecuteCompile_NoExecutor_ReportsNoConnection()
    {
        // Seam 6b — was "…_IsNoOp" (ErrorMessage null). A missing executor is an INABILITY, not a no-op:
        // SaveAsync reads "no error" as success. (An empty DIFF stays a no-op — the documented exception.)
        var vm = new ExceptionDetailTabViewModel("E_X") { Message = "m" };
        await vm.ExecuteCompileAsync();
        Assert.Equal(UiStrings.NoConnectionMessage, vm.ErrorMessage);
    }

    [Fact]
    public void CanRevert_OnlyWhenDirtyAndExisting()
    {
        var existing = new ExceptionDetailTabViewModel("E_X");
        Assert.False(existing.CanRevertChanges);   // clean
        existing.Message = "m";
        Assert.True(existing.CanRevertChanges);     // dirty + existing

        var fresh = new ExceptionDetailTabViewModel("E") { IsNew = true, EditableName = "E_NEW" };
        Assert.False(fresh.CanRevertChanges);       // new — no DB state to revert to
    }

    [Fact]
    public void CanDelete_NotForNewException()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new ExceptionDetailTabViewModel("E", null, executor) { IsNew = true };
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task DeleteCommand_RaisesDeleteRequested_AfterConfirm()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new ExceptionDetailTabViewModel("E_X", null, executor);
        var raised = false;
        vm.DeleteRequested += _ => { raised = true; return Task.CompletedTask; };

        // No ConfirmationRequested handler → RequestConfirmAsync auto-proceeds.
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.True(raised);
    }

    [Fact]
    public void NewException_UppercasesName()
    {
        var vm = new ExceptionDetailTabViewModel("E") { IsNew = true, EditableName = string.Empty };
        vm.ClearDirty();
        vm.EditableName = "xxx_blad_test";
        Assert.Equal("XXX_BLAD_TEST", vm.EditableName);
    }

    // ─── Unsaved-work (WorkGuard) ──────────────────────────────────────────

    [Fact]
    public void GetUnsavedWork_Clean_IsNull()
    {
        var vm = new ExceptionDetailTabViewModel("E_X");
        Assert.Null(vm.GetUnsavedWork());
    }

    [Fact]
    public void GetUnsavedWork_NewDirty_IsNewObject()
    {
        var vm = new ExceptionDetailTabViewModel("E") { IsNew = true, EditableName = "E_NEW" };
        vm.Message = "m"; // ensure dirty
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.NewObject, work!.Kind);
    }

    [Fact]
    public void GetUnsavedWork_ExistingDirty_IsModifiedSource()
    {
        var vm = new ExceptionDetailTabViewModel("E_X") { Message = "m" };
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
    }

    // ─── DDL preview (live from form) ─────────────────────────────────────

    [Fact]
    public void DdlText_TracksForm()
    {
        var vm = new ExceptionDetailTabViewModel("E") { IsNew = true, EditableName = "E_NEW" };
        vm.Message = "boom";
        Assert.Contains("CREATE EXCEPTION \"E_NEW\" 'boom'", vm.DdlText);
    }

    // ─── Dependency navigation ─────────────────────────────────────────────

    [Fact]
    public void RequestOpen_FiresOpenObjectRequested_WithMappedKind()
    {
        var vm = new ExceptionDetailTabViewModel("E_X");
        MetadataObject? opened = null;
        vm.OpenObjectRequested += o => opened = o;

        vm.RequestOpen(new DependencyLeafNode
        {
            Dependency = new DependencyInfo { ObjectName = "SP_CREATE_ORDER", ObjectType = "Procedure" },
        });

        Assert.NotNull(opened);
        Assert.Equal("SP_CREATE_ORDER", opened!.Name);
        Assert.Equal(MetadataObjectKind.Procedure, opened.Kind);
    }

    // ─── Reader SQL shape (exception dependencies use type 7) ─────────────

    [Fact]
    public void ExceptionDependencySql_UsesType7()
    {
        Assert.Contains("RDB$DEPENDENT_TYPE = 7", FirebirdTableDetailReader.ExceptionDependsOnSql);
        Assert.Contains("RDB$DEPENDED_ON_TYPE = 7", FirebirdTableDetailReader.ExceptionDependedOnBySql);
    }

    [Fact]
    public void MapObjectType_7_IsException()
        => Assert.Equal("Exception", FirebirdTableDetailReader.MapObjectType(7));

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
