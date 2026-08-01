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

// Function Detail: a function opens in a dedicated surface (Editor Source/Easy /
// Description / Dependencies / DDL / Execute Result) — NOT a plain DDL tab and NOT the
// procedure VM. Mirrors the Procedure routing tests.
public class FunctionRoutingTests
{
    [Theory]
    [InlineData(MetadataObjectKind.Function, true)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Table, false)]
    public void OpensAsFunctionDetail_FunctionsOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsFunctionDetail(kind));

    [Fact]
    public void Factory_Function_HasCompileAndExecute()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateFunctionDetail(new MetadataObject("ADD_TAX", MetadataObjectKind.Function));

        Assert.True(vm.CanCompile);
        Assert.Equal("ADD_TAX", vm.FunctionName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_Function_OpensFunctionDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("ADD_TAX", MetadataObjectKind.Function));

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "ADD_TAX");
        Assert.Equal(VmTabKind.FunctionDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Function, tab.ObjectKind);
        Assert.NotNull(tab.FunctionDetail);
    }

    [Fact]
    public void DirectOpen_Function_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("ADD_TAX", MetadataObjectKind.Function);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "ADD_TAX");
    }

    [Fact]
    public void Restore_FunctionTab_NativeFunctionDetail_NotDdl()
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
                            Kind = CoreTabKind.FunctionDetail,
                            ObjectName = "ADD_TAX",
                            ObjectKind = MetadataObjectKind.Function,
                            DdlText = "CREATE OR ALTER FUNCTION \"ADD_TAX\" RETURNS INTEGER AS BEGIN RETURN 0; END",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "ADD_TAX");
        Assert.Equal(VmTabKind.FunctionDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Function, tab.ObjectKind);
        Assert.NotNull(tab.FunctionDetail);
        Assert.Contains("ADD_TAX", tab.FunctionDetail!.DdlText);
    }

    [Fact]
    public void Capture_FunctionTab_PersistsAsFunctionDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("ADD_TAX", MetadataObjectKind.Function));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.FunctionDetail && t.ObjectName == "ADD_TAX");
    }

    /// <summary>
    /// The Source/Easy default for a newly opened function is a <c>Preferences</c> value now (Settings Center
    /// etap 6 / §7.6), not a <c>WorkspaceState</c> flag — so it round-trips through the same
    /// <c>settings.dat</c> section every other preference uses, and reaches the editor at construction.
    /// </summary>
    [Fact]
    public void FunctionEasyModeDefault_RoundTripsThroughPreferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-fn-easy-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var h1 = new Harness(dir))
            {
                Assert.True(h1.Main.Preferences.Apply(
                    h1.Main.Preferences.Current with { FunctionEasyModeDefault = true }));
            }

            using var h2 = new Harness(dir);
            Assert.True(h2.Main.FunctionEasyModeDefault);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly bool _ownsDir;

        // dir == null: a private random temp dir, deleted on Dispose. dir != null: a caller-owned dir shared
        // across two sessions (for a disk round-trip) — the caller cleans it up, so session 1's Dispose does not
        // wipe it before session 2 reads it.
        public Harness(string? dir = null)
        {
            _ownsDir = dir is null;
            TempDir = dir ?? Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
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
            if (!_ownsDir) return;
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
