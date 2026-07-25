using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.Debugging;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Seam 6d — after a tab compiles its object, every OTHER tab showing that object is reloaded, so nowhere in
// the app is left on the old text. What is asserted here is the DECISION (which tabs), because the reload
// itself is a database read: against no connection it changes nothing observable, so asserting the action
// alone would pass for the wrong reasons. The reload each tab then performs is its own existing RefreshAsync.
public class TabRefreshAfterCompileTests
{
    [Fact]
    public void ATabShowingTheSameObjectIsRefreshed()
    {
        using var h = new Harness();
        var source = h.AddProcedureTab("SP_X");
        var sibling = h.AddProcedureTab("SP_X");

        var targets = h.Main.TabsNeedingRefreshAfterCompile(source);

        Assert.Same(sibling, Assert.Single(targets));
    }

    [Fact]
    public void TheTabThatCompiledIsNotRefreshed()
    {
        // It already agrees with the database — it is the one that just wrote it.
        using var h = new Harness();
        var source = h.AddProcedureTab("SP_X");

        Assert.Empty(h.Main.TabsNeedingRefreshAfterCompile(source));
    }

    [Fact]
    public void ADifferentObjectIsNotRefreshed()
    {
        using var h = new Harness();
        var source = h.AddProcedureTab("SP_X");
        h.AddProcedureTab("SP_Y");                                    // same kind, other name
        h.AddTriggerTab("SP_X");                                      // same name, other kind

        Assert.Empty(h.Main.TabsNeedingRefreshAfterCompile(source));
    }

    [Fact]
    public void ASiblingWithUnsavedWorkIsLeftAlone()
    {
        // Refreshing reloads from the database and resets the dirty state, so refreshing a dirty sibling would
        // destroy edits the user has not saved. Stale text is a nuisance; discarded work is rule #11.
        using var h = new Harness();
        var source = h.AddProcedureTab("SP_X");
        var dirty = h.AddProcedureTab("SP_X");
        dirty.ProcedureDetail!.SourceText = "create or alter procedure sp_x as begin end";

        Assert.NotNull(dirty.UnsavedWork);
        Assert.Empty(h.Main.TabsNeedingRefreshAfterCompile(source));
    }

    [Fact]
    public void ADebuggerTabIsNeverARefreshTarget()
    {
        // Reloading it would reset the source its session was built from — the Draft model's business, not
        // this seam's — and would tear down a live session.
        using var h = new Harness();
        var source = h.AddProcedureTab("SP_X");
        h.AddDebuggerTab("SP_X");

        Assert.Empty(h.Main.TabsNeedingRefreshAfterCompile(source));
    }

    [Fact]
    public void ADebuggerTabCarriesTheKindOfTheRoutineItDebugs()
    {
        // It used to be hard-coded to Procedure, which silently made a debugged trigger match the wrong
        // object — harmless until this seam started keying sibling tabs on (kind, name).
        using var h = new Harness();
        var trigger = h.AddDebuggerTab("TR_X", MetadataObjectKind.Trigger);

        Assert.Equal(MetadataObjectKind.Trigger, trigger.ObjectKind);
    }

    [Fact]
    public void RefreshingADebuggerTabDoesNothing()
    {
        // The exclusion above is enforced by the selection; this pins the tab's own dispatch too, so a future
        // caller cannot reach the debugger's source through it by accident.
        using var h = new Harness();
        var debuggerTab = h.AddDebuggerTab("SP_X");

        Assert.True(debuggerTab.RefreshAsync().IsCompletedSuccessfully);
    }

    // A debugger tab needs a launcher to exist; nothing here ever launches one.
    private sealed class NoopLauncher : IDebugSessionLauncher
    {
        public Task<DebugRunHandle> LaunchAsync(
            DebugLaunchSpec spec, System.Threading.CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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

        public WorkspaceTabViewModel AddProcedureTab(string name)
        {
            var obj = new MetadataObject(name, MetadataObjectKind.Procedure);
            var tab = WorkspaceTabViewModel.CreateProcedureDetail(
                Main, obj, new ProcedureDetailTabViewModel(name), null);
            Main.WorkspaceTabs.Add(tab);
            return tab;
        }

        public WorkspaceTabViewModel AddTriggerTab(string name)
        {
            var obj = new MetadataObject(name, MetadataObjectKind.Trigger);
            var tab = WorkspaceTabViewModel.CreateTriggerDetail(
                Main, obj, new TriggerDetailTabViewModel(name), null);
            Main.WorkspaceTabs.Add(tab);
            return tab;
        }

        public WorkspaceTabViewModel AddDebuggerTab(string name, MetadataObjectKind kind = MetadataObjectKind.Procedure)
        {
            var debugger = new DebuggerTabViewModel(
                name,
                _ => Task.FromResult<string?>("create procedure " + name + " as begin end"),
                new NoopLauncher());
            var tab = WorkspaceTabViewModel.CreateDebugger(Main, debugger, name, null, kind);
            Main.WorkspaceTabs.Add(tab);
            return tab;
        }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
