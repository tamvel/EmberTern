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
/// Pins the object-lifecycle fix: <see cref="MainWindowViewModel.CloseTabsForObject"/>
/// is the SINGLE authority for closing an object's tab(s) after an in-app delete, and it
/// covers EVERY object tab kind (the reported bug: deleting a procedure/view/etc. left its
/// Detail tab open because the old whitelist only matched Ddl + TableDetail).
/// </summary>
public class ObjectLifecycleCloseTabsTests
{
    // Every DDL-producing object kind that opens a Detail (or DDL) tab must be closable.
    public static TheoryData<MetadataObjectKind> AllObjectKinds() => new()
    {
        MetadataObjectKind.Table,
        MetadataObjectKind.View,
        MetadataObjectKind.Procedure,
        MetadataObjectKind.Trigger,
        MetadataObjectKind.Function,
        MetadataObjectKind.Package,
        MetadataObjectKind.Domain,
        MetadataObjectKind.Generator,
        MetadataObjectKind.Exception,
        MetadataObjectKind.Index,
    };

    [Theory]
    [MemberData(nameof(AllObjectKinds))]
    public void CloseTabsForObject_ClosesDetailTab_ForEveryObjectKind(MetadataObjectKind kind)
    {
        using var h = new Harness();
        var tab = AddDetailTab(h, kind, "OBJ_A");
        Assert.Contains(tab, h.Main.WorkspaceTabs);

        h.Main.CloseTabsForObject(kind, "OBJ_A");

        Assert.DoesNotContain(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public void CloseTabsForObject_ClosesReadOnlyDdlTab()
    {
        using var h = new Harness();
        var tab = WorkspaceTabViewModel.CreateDdl(
            h.Main, new MetadataObject("PROC_X", MetadataObjectKind.Procedure), "CREATE …", null);
        h.Main.WorkspaceTabs.Add(tab);

        h.Main.CloseTabsForObject(MetadataObjectKind.Procedure, "PROC_X");

        Assert.DoesNotContain(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public void CloseTabsForObject_LeavesOtherObjectsOpen()
    {
        using var h = new Harness();
        var target = AddDetailTab(h, MetadataObjectKind.Procedure, "GONE");
        var sameKindOther = AddDetailTab(h, MetadataObjectKind.Procedure, "STAYS");
        var sameNameOtherKind = AddDetailTab(h, MetadataObjectKind.View, "GONE");

        h.Main.CloseTabsForObject(MetadataObjectKind.Procedure, "GONE");

        Assert.DoesNotContain(target, h.Main.WorkspaceTabs);
        Assert.Contains(sameKindOther, h.Main.WorkspaceTabs);      // different name → kept
        Assert.Contains(sameNameOtherKind, h.Main.WorkspaceTabs);  // different kind → kept
    }

    [Fact]
    public void CloseTabsForObject_NoMatch_IsNoOp()
    {
        using var h = new Harness();
        var tab = AddDetailTab(h, MetadataObjectKind.Domain, "D1");

        h.Main.CloseTabsForObject(MetadataObjectKind.Domain, "NOPE");

        Assert.Contains(tab, h.Main.WorkspaceTabs);
    }

    private static WorkspaceTabViewModel AddDetailTab(Harness h, MetadataObjectKind kind, string name)
    {
        var obj = new MetadataObject(name, kind);
        WorkspaceTabViewModel tab = kind switch
        {
            MetadataObjectKind.Table => WorkspaceTabViewModel.CreateTableDetail(h.Main, obj, new TableDetailTabViewModel(name), null),
            MetadataObjectKind.View => WorkspaceTabViewModel.CreateViewDetail(h.Main, obj, new ViewDetailTabViewModel(name), null),
            MetadataObjectKind.Procedure => WorkspaceTabViewModel.CreateProcedureDetail(h.Main, obj, new ProcedureDetailTabViewModel(name), null),
            MetadataObjectKind.Trigger => WorkspaceTabViewModel.CreateTriggerDetail(h.Main, obj, new TriggerDetailTabViewModel(name), null),
            MetadataObjectKind.Function => WorkspaceTabViewModel.CreateFunctionDetail(h.Main, obj, new FunctionDetailTabViewModel(name), null),
            MetadataObjectKind.Package => WorkspaceTabViewModel.CreatePackageDetail(h.Main, obj, new PackageDetailTabViewModel(name), null),
            MetadataObjectKind.Domain => WorkspaceTabViewModel.CreateDomainDetail(h.Main, obj, new DomainDetailTabViewModel(name), null),
            MetadataObjectKind.Generator => WorkspaceTabViewModel.CreateGeneratorDetail(h.Main, obj, new GeneratorDetailTabViewModel(name), null),
            MetadataObjectKind.Exception => WorkspaceTabViewModel.CreateExceptionDetail(h.Main, obj, new ExceptionDetailTabViewModel(name), null),
            MetadataObjectKind.Index => WorkspaceTabViewModel.CreateIndexDetail(h.Main, obj, new IndexDetailTabViewModel(name), null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        h.Main.WorkspaceTabs.Add(tab);
        return tab;
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-lifecycle-" + Guid.NewGuid().ToString("N"));
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
