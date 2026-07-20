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

// Package Detail: a package opens in a dedicated 6-tab surface (Package / Body /
// Members / Description / Dependencies / DDL) — NOT a plain DDL tab. A package has
// TWO editable source artifacts; Compile runs the header first, then the body. The
// member list comes from the catalog; member navigation is a text-find (no parser).
public class PackageDetailTests
{
    // PKG_ORDERS sources matching Lab/EmberTern_Lab.fdb (header + body).
    private const string LabHeader =
        "CREATE OR ALTER PACKAGE PKG_ORDERS\nAS\nBEGIN\n" +
        "  FUNCTION  ORDER_TOTAL(P_ORDER_ID INTEGER) RETURNS NUMERIC(15,2);\n" +
        "  PROCEDURE RECALC_ORDER(P_ORDER_ID INTEGER);\nEND";
    private const string LabBody =
        "RECREATE PACKAGE BODY PKG_ORDERS\nAS\nBEGIN\n" +
        "  FUNCTION ORDER_TOTAL(P_ORDER_ID INTEGER) RETURNS NUMERIC(15,2)\n  AS\n  BEGIN\n    RETURN 0;\n  END\n" +
        "  PROCEDURE RECALC_ORDER(P_ORDER_ID INTEGER)\n  AS\n  BEGIN\n    UPDATE ORDERS SET TOTAL_AMOUNT = 0;\n  END\nEND";

    // ─── Routing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Package, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Function, false)]
    [InlineData(MetadataObjectKind.Generator, false)]
    [InlineData(MetadataObjectKind.Domain, false)]
    public void OpensAsPackageDetail_PackagesOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsPackageDetail(kind));

    [Fact]
    public void Factory_Package_HasCompileAndDelete()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreatePackageDetail(new MetadataObject("PKG_ORDERS", MetadataObjectKind.Package));

        Assert.True(vm.CanCompile);   // DDL executor wired
        Assert.True(vm.CanDelete);    // existing package
        Assert.Equal("PKG_ORDERS", vm.PackageName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_Package_OpensPackageDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("PKG_ORDERS", MetadataObjectKind.Package));

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "PKG_ORDERS");
        Assert.Equal(VmTabKind.PackageDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Package, tab.ObjectKind);
        Assert.NotNull(tab.PackageDetail);
    }

    [Fact]
    public void DirectOpen_Package_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("PKG_ORDERS", MetadataObjectKind.Package);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "PKG_ORDERS");
    }

    [Fact]
    public void NewPackageCommand_ReNotifiesCanExecute_OnConnectionChange()
    {
        using var harness = new Harness();
        var fired = false;
        harness.Main.NewPackageCommand.CanExecuteChanged += (_, _) => fired = true;

        harness.Main.ApplyActiveConnectionChange("A");

        Assert.True(fired);
    }

    // ─── Persistence ────────────────────────────────────────────────────────

    [Fact]
    public void Restore_PackageTab_NativePackageDetail_NotDdl()
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
                            Kind = CoreTabKind.PackageDetail,
                            ObjectName = "PKG_ORDERS",
                            ObjectKind = MetadataObjectKind.Package,
                            DdlText = "CREATE PACKAGE PKG_ORDERS\nAS\nBEGIN\nEND",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "PKG_ORDERS");
        Assert.Equal(VmTabKind.PackageDetail, tab.Kind);
        Assert.NotNull(tab.PackageDetail);
        Assert.Contains("PKG_ORDERS", tab.PackageDetail!.DdlText);
    }

    [Fact]
    public void Capture_PackageTab_PersistsAsPackageDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("PKG_ORDERS", MetadataObjectKind.Package));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.PackageDetail && t.ObjectName == "PKG_ORDERS");
    }

    // ─── DDL generation (pure) ────────────────────────────────────────────

    [Fact]
    public void BuildCreateOrAlterPackageHeader_WrapsSourceAfterAs()
    {
        var sql = DdlGenerator.BuildCreateOrAlterPackageHeader("PKG_X", "BEGIN\n  PROCEDURE P;\nEND");
        Assert.Equal("CREATE OR ALTER PACKAGE PKG_X\nAS\nBEGIN\n  PROCEDURE P;\nEND", sql);
    }

    [Fact]
    public void BuildRecreatePackageBody_WrapsSourceAfterAs()
    {
        var sql = DdlGenerator.BuildRecreatePackageBody("PKG_X", "BEGIN\nEND");
        Assert.Equal("RECREATE PACKAGE BODY PKG_X\nAS\nBEGIN\nEND", sql);
    }

    [Fact]
    public void BuildPackageDdl_HeaderAndBody_WhenBodyPresent()
    {
        var ddl = DdlGenerator.BuildPackageDdl("PKG_X", "BEGIN\n  PROCEDURE P;\nEND", "BEGIN\n  PROCEDURE P AS BEGIN END\nEND");
        Assert.Contains("CREATE PACKAGE PKG_X", ddl);
        Assert.Contains("RECREATE PACKAGE BODY PKG_X", ddl);
    }

    [Fact]
    public void BuildPackageDdl_HeaderOnly_WhenNoBody()
    {
        var ddl = DdlGenerator.BuildPackageDdl("PKG_X", "BEGIN\nEND", null);
        Assert.Contains("CREATE PACKAGE PKG_X", ddl);
        Assert.DoesNotContain("PACKAGE BODY", ddl);
    }

    [Fact]
    public void BuildPackageDdl_LowercaseName_IsQuoted()
        => Assert.Contains("CREATE PACKAGE \"pkg_x\"", DdlGenerator.BuildPackageDdl("pkg_x", "BEGIN\nEND", null));

    [Fact]
    public void BuildDropPackage_Emits()
        => Assert.Equal("DROP PACKAGE \"PKG_X\"", DdlGenerator.BuildDropPackage("PKG_X"));

    [Theory]
    [InlineData("PKG_X", "note", "COMMENT ON PACKAGE \"PKG_X\" IS 'note'")]
    [InlineData("PKG_X", "", "COMMENT ON PACKAGE \"PKG_X\" IS NULL")]
    [InlineData("PKG_X", null, "COMMENT ON PACKAGE \"PKG_X\" IS NULL")]
    [InlineData("PKG_X", "it's", "COMMENT ON PACKAGE \"PKG_X\" IS 'it''s'")]
    public void BuildCommentPackage_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentPackage(name, comment));

    [Fact]
    public void BuildCreateOrAlterPackageHeader_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCreateOrAlterPackageHeader("", "BEGIN END"));

    // The header + body the generator emits stay single statements through the
    // PSQL-aware DDL splitter (declarations / nested routine bodies have internal ';').
    [Fact]
    public void SplitStatements_PackageHeader_StaysOneStatement()
        => Assert.Single(FirebirdDdlExecutor.SplitStatements(LabHeader));

    [Fact]
    public void SplitStatements_PackageBody_StaysOneStatement()
        => Assert.Single(FirebirdDdlExecutor.SplitStatements(LabBody));

    // ─── Member list / grouping (catalog-sourced, no parsing) ──────────────

    [Fact]
    public void SetMembers_GroupsFunctionsAndProcedures()
    {
        var vm = new PackageDetailTabViewModel("PKG_ORDERS");
        vm.SetMembers(new[]
        {
            new PackageMember("ORDER_TOTAL", PackageMemberKind.Function),
            new PackageMember("RECALC_ORDER", PackageMemberKind.Procedure),
        });

        Assert.True(vm.HasMembers);
        Assert.Equal(2, vm.MemberGroups.Count);
        var funcs = vm.MemberGroups.Single(g => g.Header.StartsWith("Functions", StringComparison.Ordinal));
        var procs = vm.MemberGroups.Single(g => g.Header.StartsWith("Procedures", StringComparison.Ordinal));
        Assert.Equal("ORDER_TOTAL", funcs.Children.Single().DisplayName);
        Assert.Equal("RECALC_ORDER", procs.Children.Single().DisplayName);
    }

    [Fact]
    public void SetMembers_OmitsEmptyGroups()
    {
        var vm = new PackageDetailTabViewModel("PKG_ONLY_FUNC");
        vm.SetMembers(new[] { new PackageMember("F1", PackageMemberKind.Function) });

        Assert.Single(vm.MemberGroups);
        Assert.StartsWith("Functions", vm.MemberGroups.Single().Header);
    }

    [Fact]
    public void SetMembers_Empty_ShowsEmptyState()
    {
        var vm = new PackageDetailTabViewModel("PKG_EMPTY");
        vm.SetMembers(Array.Empty<PackageMember>());

        Assert.False(vm.HasMembers);
        Assert.True(vm.ShowMembersEmpty);
    }

    // ─── Member navigation (PackageSourceScanner — text-find, no parser) ───

    [Fact]
    public void FindMemberOffset_LocatesFunctionAndProcedure()
    {
        var fnOffset = PackageSourceScanner.FindMemberOffset(LabBody, PackageMemberKind.Function, "ORDER_TOTAL");
        var spOffset = PackageSourceScanner.FindMemberOffset(LabBody, PackageMemberKind.Procedure, "RECALC_ORDER");
        Assert.True(fnOffset >= 0);
        Assert.True(spOffset >= 0);
        // The offset points AT the member name token.
        Assert.Equal("ORDER_TOTAL", LabBody.Substring(fnOffset, "ORDER_TOTAL".Length));
        Assert.Equal("RECALC_ORDER", LabBody.Substring(spOffset, "RECALC_ORDER".Length));
    }

    [Fact]
    public void FindMemberOffset_DoesNotMatchWrongKind()
        // RECALC_ORDER is a PROCEDURE, never preceded by FUNCTION.
        => Assert.Equal(-1, PackageSourceScanner.FindMemberOffset(LabBody, PackageMemberKind.Function, "RECALC_ORDER"));

    [Fact]
    public void FindMemberOffset_NotFound_ReturnsMinusOne()
        => Assert.Equal(-1, PackageSourceScanner.FindMemberOffset(LabBody, PackageMemberKind.Function, "NOPE"));

    [Theory]
    [InlineData(null, "X")]
    [InlineData("FUNCTION X", null)]
    public void FindMemberOffset_NullInputs_ReturnMinusOne(string? text, string? name)
        => Assert.Equal(-1, PackageSourceScanner.FindMemberOffset(text, PackageMemberKind.Function, name));

    [Fact]
    public void ResolveMemberLocation_PrefersBody()
    {
        var vm = new PackageDetailTabViewModel("PKG_ORDERS")
        {
            HeaderSource = LabHeader,
            BodySource = LabBody,
        };
        var loc = vm.ResolveMemberLocation(new PackageMember("ORDER_TOTAL", PackageMemberKind.Function));
        Assert.NotNull(loc);
        Assert.True(loc!.InBody);
        Assert.Equal("ORDER_TOTAL".Length, loc.Length);
    }

    [Fact]
    public void ResolveMemberLocation_FallsBackToHeader_WhenNotInBody()
    {
        var vm = new PackageDetailTabViewModel("PKG_ORDERS")
        {
            HeaderSource = LabHeader,
            BodySource = string.Empty, // no body yet
        };
        var loc = vm.ResolveMemberLocation(new PackageMember("RECALC_ORDER", PackageMemberKind.Procedure));
        Assert.NotNull(loc);
        Assert.False(loc!.InBody);
    }

    [Fact]
    public void ResolveMemberLocation_NotFound_ReturnsNull()
    {
        var vm = new PackageDetailTabViewModel("PKG_ORDERS") { HeaderSource = LabHeader, BodySource = LabBody };
        Assert.Null(vm.ResolveMemberLocation(new PackageMember("GHOST", PackageMemberKind.Function)));
    }

    [Fact]
    public void NavigateToMember_SwitchesToBodySubTab()
    {
        var vm = new PackageDetailTabViewModel("PKG_ORDERS") { HeaderSource = LabHeader, BodySource = LabBody };
        PackageMemberLocation? raised = null;
        vm.NavigateToMemberRequested += loc => raised = loc;

        vm.NavigateToMember(new PackageMember("ORDER_TOTAL", PackageMemberKind.Function));

        Assert.NotNull(raised);
        Assert.True(raised!.InBody);
        Assert.Equal(PackageDetailTabViewModel.BodySubTabIndex, vm.ActiveSubTabIndex);
    }

    // ─── Debug member entry point (D11 seam C) ─────────────────────────────

    [Fact] // a PROCEDURE member raises DebugMemberRequested with its name
    public void RequestDebugMember_Procedure_RaisesWithName()
    {
        var vm = new PackageDetailTabViewModel("PKG_DBG");
        string? raised = null;
        vm.DebugMemberRequested += name => raised = name;

        vm.RequestDebugMember(new PackageMember("PUB_RUN", PackageMemberKind.Procedure));

        Assert.Equal("PUB_RUN", raised);
    }

    [Fact] // a FUNCTION member is not launchable as a debug root (§F) → no-op
    public void RequestDebugMember_Function_IsNoOp()
    {
        var vm = new PackageDetailTabViewModel("PKG_DBG");
        var raised = false;
        vm.DebugMemberRequested += _ => raised = true;

        vm.RequestDebugMember(new PackageMember("ORDER_TOTAL", PackageMemberKind.Function));
        vm.RequestDebugMember(null);

        Assert.False(raised);
    }

    [Fact] // the context-menu visibility gate: only procedure member nodes offer Debug
    public void MemberNode_IsProcedure_GatesDebug()
    {
        var vm = new PackageDetailTabViewModel("PKG_DBG");
        vm.SetMembers(new[]
        {
            new PackageMember("ORDER_TOTAL", PackageMemberKind.Function),
            new PackageMember("PUB_RUN", PackageMemberKind.Procedure),
        });

        var func = vm.MemberGroups.Single(g => g.Header.StartsWith("Functions", StringComparison.Ordinal)).Children.Single();
        var proc = vm.MemberGroups.Single(g => g.Header.StartsWith("Procedures", StringComparison.Ordinal)).Children.Single();
        Assert.False(func.IsProcedure);
        Assert.True(proc.IsProcedure);
    }

    // ─── TryParsePackageName (New flow reopen-by-name) ─────────────────────

    [Theory]
    [InlineData("CREATE OR ALTER PACKAGE PKG_ORDERS\nAS\nBEGIN\nEND", "PKG_ORDERS")]
    [InlineData("CREATE PACKAGE PKG_X AS BEGIN END", "PKG_X")]
    [InlineData("create package my_pkg as begin end", "MY_PKG")]
    [InlineData("CREATE PACKAGE \"MixedCase\" AS BEGIN END", "MixedCase")]
    [InlineData("ALTER TABLE FOO ADD BAR INTEGER", null)]
    [InlineData("", null)]
    public void TryParsePackageName_Cases(string sql, string? expected)
        => Assert.Equal(expected, PackageDetailTabViewModel.TryParsePackageName(sql));

    // ─── Dirty tracking / Revert ───────────────────────────────────────────

    [Fact]
    public void EditingHeaderOrBody_MarksDirty()
    {
        var vm = new PackageDetailTabViewModel("PKG_ORDERS");
        Assert.False(vm.IsDirty);
        vm.HeaderSource = "CREATE OR ALTER PACKAGE PKG_ORDERS AS BEGIN END";
        Assert.True(vm.IsDirty);

        var vm2 = new PackageDetailTabViewModel("PKG_ORDERS");
        vm2.BodySource = "RECREATE PACKAGE BODY PKG_ORDERS AS BEGIN END";
        Assert.True(vm2.IsDirty);
    }

    [Fact]
    public void Revert_Gated_OnDirtyAndNotNew()
    {
        var clean = new PackageDetailTabViewModel("P");
        Assert.False(clean.CanRevertChanges);          // clean

        var dirty = new PackageDetailTabViewModel("P");
        dirty.HeaderSource = "x";
        Assert.True(dirty.CanRevertChanges);            // dirty + existing

        var fresh = new PackageDetailTabViewModel("P") { IsNew = true };
        fresh.HeaderSource = "x";
        Assert.False(fresh.CanRevertChanges);           // new → no DB state to revert to
    }

    [Fact]
    public void GetUnsavedWork_NullWhenClean_KindByNewFlag()
    {
        var clean = new PackageDetailTabViewModel("PKG_X");
        Assert.Null(clean.GetUnsavedWork());

        var modified = new PackageDetailTabViewModel("PKG_X");
        modified.HeaderSource = "x";
        Assert.Equal(UnsavedWorkKind.ModifiedSource, modified.GetUnsavedWork()!.Kind);

        var fresh = new PackageDetailTabViewModel("PKG_X") { IsNew = true };
        fresh.HeaderSource = "x";
        Assert.Equal(UnsavedWorkKind.NewObject, fresh.GetUnsavedWork()!.Kind);
    }

    [Fact]
    public void NoExecutor_CannotCompileOrDelete()
    {
        var vm = new PackageDetailTabViewModel("PKG_X"); // no readers/executor
        Assert.False(vm.CanCompile);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public async Task Compile_EmptyHeader_NoOp()
    {
        // No executor + empty header → ExecuteCompileAsync returns early, no throw.
        var vm = new PackageDetailTabViewModel("PKG_X");
        await vm.ExecuteCompileAsync();
        Assert.Null(vm.ErrorMessage);
    }

    // ─── Reader SQL shape pins (no live DB) ────────────────────────────────

    [Fact]
    public void PackageFunctionsSql_QueriesRdbFunctionsByPackageName()
    {
        Assert.Contains("RDB$FUNCTIONS", FirebirdTableDetailReader.PackageFunctionsSql);
        Assert.Contains("RDB$PACKAGE_NAME = @name", FirebirdTableDetailReader.PackageFunctionsSql);
    }

    [Fact]
    public void PackageProceduresSql_QueriesRdbProceduresByPackageName()
    {
        Assert.Contains("RDB$PROCEDURES", FirebirdTableDetailReader.PackageProceduresSql);
        Assert.Contains("RDB$PACKAGE_NAME = @name", FirebirdTableDetailReader.PackageProceduresSql);
    }

    [Fact]
    public void PackageDependencySql_UsesType18()
    {
        Assert.Contains("RDB$DEPENDENT_TYPE = 18", FirebirdTableDetailReader.PackageDependsOnSql);
        Assert.Contains("RDB$DEPENDED_ON_TYPE = 18", FirebirdTableDetailReader.PackageDependedOnBySql);
    }

    [Fact]
    public void MapObjectType_18_IsPackage()
        => Assert.Equal("Package", FirebirdTableDetailReader.MapObjectType(18));

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
