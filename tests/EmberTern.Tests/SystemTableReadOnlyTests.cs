using System;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

// System-table support: a system table opens in the rich TableDetail view (not a
// plain DDL tab) but is fully READ-ONLY. Read-only is NOT a flag — it falls out of
// the capability-based design: the single CreateTableDetail factory omits the data
// editor + DDL executor for non-table kinds, so every existing capability gate
// (CanEditData / CanAddField / CanManageConstraints / CanManageIndexes / CanCompile /
// CanEditDescription) turns off. Both the direct-open path and the workspace-restore
// path go through that one factory, so the two cannot diverge.
public class SystemTableReadOnlyTests
{
    [Theory]
    [InlineData(MetadataObjectKind.Table, true)]
    [InlineData(MetadataObjectKind.SystemTable, true)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.Function, false)]
    [InlineData(MetadataObjectKind.Package, false)]
    [InlineData(MetadataObjectKind.Domain, false)]
    [InlineData(MetadataObjectKind.Index, false)]
    [InlineData(MetadataObjectKind.Generator, false)]
    public void OpensAsTableDetail_TableShapedKindsOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsTableDetail(kind));

    [Fact]
    public void Factory_SystemTable_IsReadOnly()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateTableDetail(new MetadataObject("RDB$RELATIONS", MetadataObjectKind.SystemTable));

        // No data editing.
        Assert.False(vm.CanEditData);
        Assert.True(vm.IsDataReadOnly);
        Assert.False(vm.CanAddRow);

        // No structure / constraint / index / description editing — all derive from
        // the absent DDL executor.
        Assert.False(vm.CanAddField);
        Assert.False(vm.CanEditField);
        Assert.False(vm.CanCreateForeignKey);
        Assert.False(vm.CanManageConstraints);
        Assert.False(vm.CanManageIndexes);
        Assert.False(vm.CanCompile);
        Assert.False(vm.CanEditDescription);
    }

    [Fact]
    public void Factory_Table_RemainsFullyEditable()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateTableDetail(new MetadataObject("CUSTOMERS", MetadataObjectKind.Table));

        // Capabilities for a normal user table are unchanged by this milestone.
        Assert.True(vm.CanEditData);
        Assert.False(vm.IsDataReadOnly);
        Assert.True(vm.CanAddRow);
        Assert.True(vm.CanAddField);
        Assert.True(vm.CanCreateForeignKey);
        Assert.True(vm.CanManageConstraints);
        Assert.True(vm.CanManageIndexes);
        Assert.True(vm.CanEditDescription);
    }

    [Fact]
    public void DirectOpen_SystemTable_OpensTableDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("RDB$RELATIONS", MetadataObjectKind.SystemTable);
        // Same path as a double-click on the sidebar leaf. No live connection, so the
        // background load fails and is swallowed — but the tab is added synchronously
        // before that await, which is what we assert.
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "RDB$RELATIONS");
        Assert.Equal(VmTabKind.TableDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.SystemTable, tab.ObjectKind);
        Assert.NotNull(tab.TableDetail);
        Assert.False(tab.TableDetail!.CanEditData);
        Assert.False(tab.TableDetail.CanAddField);
    }

    [Fact]
    public void DirectOpen_SystemTable_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("RDB$FIELDS", MetadataObjectKind.SystemTable);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        // Dedup keys on (Kind, Name) — a system table is no different.
        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "RDB$FIELDS");
    }

    [Fact]
    public void Restore_SystemTableTab_PreservesReadOnlyCapabilities()
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
                            Kind = CoreTabKind.TableDetail,
                            ObjectName = "RDB$RELATIONS",
                            ObjectKind = MetadataObjectKind.SystemTable,
                            DdlText = "CREATE TABLE \"RDB$RELATIONS\" ...",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "RDB$RELATIONS");
        Assert.Equal(VmTabKind.TableDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.SystemTable, tab.ObjectKind);
        Assert.NotNull(tab.TableDetail);
        // The whole point of the single factory: restored read-only == opened read-only.
        Assert.False(tab.TableDetail!.CanEditData);
        Assert.False(tab.TableDetail.CanAddField);
        Assert.False(tab.TableDetail.CanManageConstraints);
        Assert.False(tab.TableDetail.CanManageIndexes);
        Assert.False(tab.TableDetail.CanEditDescription);
    }

    [Fact]
    public void Restore_TableTab_RemainsEditable()
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
                            Kind = CoreTabKind.TableDetail,
                            ObjectName = "CUSTOMERS",
                            ObjectKind = MetadataObjectKind.Table,
                            DdlText = "CREATE TABLE CUSTOMERS ...",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "CUSTOMERS");
        Assert.Equal(MetadataObjectKind.Table, tab.ObjectKind);
        Assert.NotNull(tab.TableDetail);
        Assert.True(tab.TableDetail!.CanEditData);
        Assert.True(tab.TableDetail.CanAddField);
        Assert.True(tab.TableDetail.CanManageConstraints);
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
