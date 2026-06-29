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

// Domain Detail: a domain opens in a dedicated 4-tab surface (Domain / Description /
// Used By / DDL) — NOT a plain DDL tab. The form is persisted via Save, which emits
// CREATE DOMAIN / ALTER DOMAIN / COMMENT ON DOMAIN — never a direct UPDATE on RDB$
// tables. ALTER DOMAIN edits are limited to default / check / description (verified
// on FB3 + FB5: charset/collation can NEVER be ALTERed → read-only after create).
public class DomainDetailTests
{
    // ─── Routing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Domain, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.Function, false)]
    [InlineData(MetadataObjectKind.Generator, false)]
    public void OpensAsDomainDetail_DomainsOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsDomainDetail(kind));

    [Fact]
    public void Factory_Domain_HasSaveAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateDomainDetail(new MetadataObject("D_ADRES", MetadataObjectKind.Domain));

        Assert.True(vm.CanSave);          // DDL executor wired
        Assert.True(vm.CanDelete);        // existing domain
        Assert.Equal("D_ADRES", vm.DomainName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_Domain_OpensDomainDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("D_ADRES", MetadataObjectKind.Domain);
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "D_ADRES");
        Assert.Equal(VmTabKind.DomainDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Domain, tab.ObjectKind);
        Assert.NotNull(tab.DomainDetail);
    }

    [Fact]
    public void DirectOpen_Domain_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("D_ADRES", MetadataObjectKind.Domain);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "D_ADRES");
    }

    [Fact]
    public void Restore_DomainTab_NativeDomainDetail_NotDdl()
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
                            Kind = CoreTabKind.DomainDetail,
                            ObjectName = "D_ADRES",
                            ObjectKind = MetadataObjectKind.Domain,
                            DdlText = "CREATE DOMAIN \"D_ADRES\" AS VARCHAR(50);",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "D_ADRES");
        Assert.Equal(VmTabKind.DomainDetail, tab.Kind);
        Assert.NotNull(tab.DomainDetail);
        Assert.Contains("D_ADRES", tab.DomainDetail!.DdlText);
    }

    [Fact]
    public void Capture_DomainTab_PersistsAsDomainDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("D_ADRES", MetadataObjectKind.Domain));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.DomainDetail && t.ObjectName == "D_ADRES");
    }

    [Fact]
    public void NewDomainCommand_ReNotifiesCanExecute_OnConnectionChange()
    {
        using var harness = new Harness();
        var fired = false;
        harness.Main.NewDomainCommand.CanExecuteChanged += (_, _) => fired = true;

        harness.Main.ApplyActiveConnectionChange("A");

        Assert.True(fired);
    }

    // ─── DDL generation (pure) ────────────────────────────────────────────

    [Fact]
    public void BuildCreateDomain_CharWithAllClauses()
    {
        var sql = DdlGenerator.BuildCreateDomain(new DomainInfo
        {
            Name = "D_ADRES",
            DataType = "VARCHAR",
            Length = 50,
            CharacterSet = "WIN1250",
            Collation = "PXW_PLK",
            DefaultValue = "'x'",
            CheckConstraint = "CHECK (VALUE <> '')",
            NotNull = true,
        });

        Assert.Contains("CREATE DOMAIN \"D_ADRES\" AS VARCHAR(50) CHARACTER SET WIN1250", sql);
        Assert.Contains("DEFAULT 'x'", sql);
        Assert.Contains("NOT NULL", sql);
        Assert.Contains("CHECK (VALUE <> '')", sql);
        Assert.Contains("COLLATE PXW_PLK", sql);
    }

    [Fact]
    public void BuildCreateDomain_Numeric()
    {
        var sql = DdlGenerator.BuildCreateDomain(new DomainInfo
        {
            Name = "D_KWOTA", DataType = "NUMERIC", Precision = 15, Scale = 2,
        });
        Assert.Equal("CREATE DOMAIN \"D_KWOTA\" AS NUMERIC(15,2)", sql);
    }

    [Fact]
    public void BuildCreateDomain_BlobSubType()
    {
        var sql = DdlGenerator.BuildCreateDomain(new DomainInfo { Name = "D_NOTE", DataType = "BLOB", SubType = 1 });
        Assert.Equal("CREATE DOMAIN \"D_NOTE\" AS BLOB SUB_TYPE 1", sql);
    }

    [Fact]
    public void BuildCreateDomain_PlainInteger()
    {
        var sql = DdlGenerator.BuildCreateDomain(new DomainInfo { Name = "D_ID", DataType = "INTEGER" });
        Assert.Equal("CREATE DOMAIN \"D_ID\" AS INTEGER", sql);
    }

    [Fact]
    public void BuildCreateDomain_SkipsNoneCharsetAndCollation()
    {
        var sql = DdlGenerator.BuildCreateDomain(new DomainInfo
        {
            Name = "D_NOTE", DataType = "BLOB", SubType = 1, CharacterSet = "NONE", Collation = "NONE",
        });
        Assert.DoesNotContain("CHARACTER SET", sql);
        Assert.DoesNotContain("COLLATE", sql);
    }

    [Fact]
    public void BuildCreateDomain_BareCheckIsWrapped()
    {
        var sql = DdlGenerator.BuildCreateDomain(new DomainInfo
        {
            Name = "D_POS", DataType = "INTEGER", CheckConstraint = "VALUE > 0",
        });
        Assert.Contains("CHECK (VALUE > 0)", sql);
    }

    [Fact]
    public void BuildCreateDomain_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCreateDomain(new DomainInfo { Name = "" }));

    [Fact]
    public void BuildAlterDomainSetDefault_Emits()
        => Assert.Equal("ALTER DOMAIN \"D_X\" SET DEFAULT 'y'", DdlGenerator.BuildAlterDomainSetDefault("D_X", "'y'"));

    [Fact]
    public void BuildAlterDomainDropDefault_Emits()
        => Assert.Equal("ALTER DOMAIN \"D_X\" DROP DEFAULT", DdlGenerator.BuildAlterDomainDropDefault("D_X"));

    [Theory]
    [InlineData("VALUE > 0", "ALTER DOMAIN \"D_X\" ADD CHECK (VALUE > 0)")]
    [InlineData("CHECK (VALUE > 0)", "ALTER DOMAIN \"D_X\" ADD CHECK (VALUE > 0)")]
    public void BuildAlterDomainAddCheck_NormalizesClause(string input, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildAlterDomainAddCheck("D_X", input));

    [Fact]
    public void BuildAlterDomainDropConstraint_Emits()
        => Assert.Equal("ALTER DOMAIN \"D_X\" DROP CONSTRAINT", DdlGenerator.BuildAlterDomainDropConstraint("D_X"));

    [Theory]
    [InlineData("D_X", "note", "COMMENT ON DOMAIN \"D_X\" IS 'note'")]
    [InlineData("D_X", "", "COMMENT ON DOMAIN \"D_X\" IS NULL")]
    [InlineData("D_X", null, "COMMENT ON DOMAIN \"D_X\" IS NULL")]
    [InlineData("D_X", "it's", "COMMENT ON DOMAIN \"D_X\" IS 'it''s'")]
    public void BuildCommentDomain_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentDomain(name, comment));

    [Fact]
    public void BuildDropDomain_Emits()
        => Assert.Equal("DROP DOMAIN \"D_X\"", DdlGenerator.BuildDropDomain("D_X"));

    [Fact]
    public void BuildAlterDomain_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAlterDomainDropDefault(""));

    // ─── VM Save SQL (BuildSaveSql) ───────────────────────────────────────

    [Fact]
    public void BuildSaveSql_New_EmitsCreateDomain()
    {
        var vm = new DomainDetailTabViewModel("D")
        {
            IsNew = true,
            EditableName = "D_NEW",
            DataType = "VARCHAR",
            Length = 50,
        };
        Assert.Equal("CREATE DOMAIN \"D_NEW\" AS VARCHAR(50)", vm.BuildSaveSql());
    }

    [Fact]
    public void BuildSaveSql_New_WithDescription_AppendsComment()
    {
        var vm = new DomainDetailTabViewModel("D")
        {
            IsNew = true,
            EditableName = "D_NEW",
            DataType = "INTEGER",
            EditableDescription = "an id",
        };
        var sql = vm.BuildSaveSql();
        Assert.Contains("CREATE DOMAIN \"D_NEW\" AS INTEGER", sql);
        Assert.Contains("COMMENT ON DOMAIN \"D_NEW\" IS 'an id'", sql);
    }

    [Fact]
    public void BuildSaveSql_Existing_NoChange_IsEmpty()
    {
        var vm = new DomainDetailTabViewModel("D_X");
        Assert.Equal(string.Empty, vm.BuildSaveSql());
    }

    [Fact]
    public void BuildSaveSql_Existing_SetDefault()
    {
        var vm = new DomainDetailTabViewModel("D_X") { DefaultValue = "0" };
        Assert.Equal("ALTER DOMAIN \"D_X\" SET DEFAULT 0", vm.BuildSaveSql());
    }

    [Fact]
    public void BuildSaveSql_Existing_AddCheck()
    {
        var vm = new DomainDetailTabViewModel("D_X") { CheckConstraint = "VALUE > 0" };
        Assert.Equal("ALTER DOMAIN \"D_X\" ADD CHECK (VALUE > 0)", vm.BuildSaveSql());
    }

    [Fact]
    public void BuildSaveSql_Existing_DescriptionChange_EmitsComment()
    {
        var vm = new DomainDetailTabViewModel("D_X") { EditableDescription = "hi" };
        Assert.Equal("COMMENT ON DOMAIN \"D_X\" IS 'hi'", vm.BuildSaveSql());
    }

    [Fact]
    public void Editability_NewVsExisting()
    {
        var fresh = new DomainDetailTabViewModel("D") { IsNew = true };
        Assert.True(fresh.IsDefinitionEditable);    // type fields editable when creating

        var existing = new DomainDetailTabViewModel("D_X");
        Assert.False(existing.IsDefinitionEditable); // read-only after create
    }

    // ─── Dirty tracking + gating ──────────────────────────────────────────

    [Fact]
    public void EditingDefault_MarksDirty()
    {
        var vm = new DomainDetailTabViewModel("D_X");
        Assert.False(vm.IsDirty);
        vm.DefaultValue = "0";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void NoExecutor_CannotSaveOrDelete()
    {
        var vm = new DomainDetailTabViewModel("D_X");
        Assert.False(vm.CanSave);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task ExecuteSave_NoExecutor_IsNoOp()
    {
        var vm = new DomainDetailTabViewModel("D_X") { DefaultValue = "0" };
        await vm.ExecuteSaveAsync();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void CanRevert_OnlyWhenDirtyAndExisting()
    {
        var existing = new DomainDetailTabViewModel("D_X");
        Assert.False(existing.CanRevertChanges);   // clean
        existing.DefaultValue = "0";
        Assert.True(existing.CanRevertChanges);     // dirty + existing

        var fresh = new DomainDetailTabViewModel("D") { IsNew = true, EditableName = "D_NEW" };
        Assert.False(fresh.CanRevertChanges);       // new — no DB state to revert to
    }

    [Fact]
    public void CanDelete_NotForNewDomain()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new DomainDetailTabViewModel("D", null, executor) { IsNew = true };
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task DeleteCommand_RaisesDeleteRequested_AfterConfirm()
    {
        using var svc = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(svc);
        var vm = new DomainDetailTabViewModel("D_X", null, executor);
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
        var vm = new DomainDetailTabViewModel("D_X");
        Assert.Null(vm.GetUnsavedWork());
    }

    [Fact]
    public void GetUnsavedWork_NewDirty_IsNewObject()
    {
        var vm = new DomainDetailTabViewModel("D") { IsNew = true, EditableName = "D_NEW" };
        vm.DefaultValue = "0"; // ensure dirty
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.NewObject, work!.Kind);
    }

    [Fact]
    public void GetUnsavedWork_ExistingDirty_IsModifiedSource()
    {
        var vm = new DomainDetailTabViewModel("D_X") { DefaultValue = "0" };
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
    }

    // ─── Used By navigation ────────────────────────────────────────────────

    [Fact]
    public void RequestOpen_FiresOpenObjectRequested_WithMappedKind()
    {
        var vm = new DomainDetailTabViewModel("D_X");
        MetadataObject? opened = null;
        vm.OpenObjectRequested += o => opened = o;

        vm.RequestOpen(new DependencyLeafNode
        {
            Dependency = new DependencyInfo { ObjectName = "KONTRAHENCI", ObjectType = "Table" },
        });

        Assert.NotNull(opened);
        Assert.Equal("KONTRAHENCI", opened!.Name);
        Assert.Equal(MetadataObjectKind.Table, opened.Kind);
    }

    // ─── Reader SQL shape + mapping ────────────────────────────────────────

    [Fact]
    public void DomainInfoSql_SelectsDefaultCheckCharsetCollation()
    {
        Assert.Contains("RDB$DEFAULT_SOURCE", FirebirdTableDetailReader.DomainInfoSql);
        Assert.Contains("RDB$VALIDATION_SOURCE", FirebirdTableDetailReader.DomainInfoSql);
        Assert.Contains("RDB$CHARACTER_SETS", FirebirdTableDetailReader.DomainInfoSql);
        Assert.Contains("RDB$COLLATIONS", FirebirdTableDetailReader.DomainInfoSql);
    }

    [Fact]
    public void DomainUsageColumnsSql_QueriesRelationFields_NotDependencies()
    {
        Assert.Contains("RDB$RELATION_FIELDS", FirebirdTableDetailReader.DomainUsageColumnsSql);
        Assert.Contains("RDB$FIELD_SOURCE = @name", FirebirdTableDetailReader.DomainUsageColumnsSql);
        Assert.DoesNotContain("RDB$DEPENDENCIES", FirebirdTableDetailReader.DomainUsageColumnsSql);
    }

    [Fact]
    public void DomainUsageDependenciesSql_UsesType9()
        => Assert.Contains("RDB$DEPENDED_ON_TYPE = 9", FirebirdTableDetailReader.DomainUsageDependenciesSql);

    [Fact]
    public void MapObjectType_9_IsDomain()
        => Assert.Equal("Domain", FirebirdTableDetailReader.MapObjectType(9));

    [Fact]
    public void BuildDomainInfo_VarChar()
    {
        var info = FirebirdTableDetailReader.BuildDomainInfo(
            "D_ADRES", fieldType: 37, charLength: 50, byteLength: 50, scale: null, precision: null,
            subType: null, charset: "WIN1250", collation: "PXW_PLK",
            defaultSource: "DEFAULT 'x'", checkSource: "CHECK (VALUE <> '')", notNull: true, description: " note ");

        Assert.Equal("VARCHAR", info.DataType);
        Assert.Equal(50, info.Length);
        Assert.Null(info.Precision);
        Assert.Null(info.SubType);
        Assert.Equal("WIN1250", info.CharacterSet);
        Assert.Equal("PXW_PLK", info.Collation);
        Assert.Equal("'x'", info.DefaultValue);          // DEFAULT prefix stripped
        Assert.Equal("CHECK (VALUE <> '')", info.CheckConstraint);
        Assert.True(info.NotNull);
        Assert.Equal("note", info.Description);          // trimmed
    }

    [Fact]
    public void BuildDomainInfo_Numeric()
    {
        var info = FirebirdTableDetailReader.BuildDomainInfo(
            "D_KWOTA", fieldType: 16, charLength: null, byteLength: 8, scale: -2, precision: 15,
            subType: 1, charset: null, collation: null, defaultSource: null, checkSource: null,
            notNull: false, description: null);

        Assert.Equal("NUMERIC", info.DataType);
        Assert.Equal(15, info.Precision);
        Assert.Equal(2, info.Scale);                     // abs(-2)
        Assert.Null(info.Length);
        Assert.Null(info.SubType);
        Assert.False(info.NotNull);
    }

    [Fact]
    public void BuildDomainInfo_Blob()
    {
        var info = FirebirdTableDetailReader.BuildDomainInfo(
            "D_NOTE", fieldType: 261, charLength: null, byteLength: 8, scale: 0, precision: null,
            subType: 1, charset: "NONE", collation: "NONE", defaultSource: null, checkSource: null,
            notNull: false, description: null);

        Assert.Equal("BLOB", info.DataType);
        Assert.Equal(1, info.SubType);
        Assert.Null(info.Length);
        Assert.Null(info.Precision);
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
