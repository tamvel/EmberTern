using System;
using System.IO;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Regression for the post-commit metadata refresh STORM (gotcha #119): a transaction
// settle must NOT blanket-refresh every open TableDetail tab. The pure selector
// SelectRefreshTargets is the heart of the fix. Also covers the Domain→Type field-sync
// bug (selecting a domain must not blank the Type column).
public class RefreshStormFixTests
{
    // ─── refresh-storm: scoped refresh targets, never a blanket fan-out ────

    [Fact]
    public void DataRollback_RefreshesOnlyEditedTabs_NotAll()
    {
        using var h = new Harness();
        var (detailA, tabA) = MakeTableTab(h, "A");
        var (detailB, tabB) = MakeTableTab(h, "B");
        var (_, tabC) = MakeTableTab(h, "C");
        detailB.HasPendingDataEdits = true; // only B was edited
        var tabs = new[] { tabA, tabB, tabC };

        var targets = MainWindowViewModel.SelectRefreshTargets(
            MainWindowViewModel.PostTransactionRefresh.DataOnly, tabs, tabA);

        var only = Assert.Single(targets);
        Assert.Same(detailB, only.Detail);
        Assert.False(only.Structure); // data-preview reload, not structure
    }

    [Fact]
    public void MetadataRollback_RefreshesOnlyActiveTab_NotAll()
    {
        using var h = new Harness();
        var (detailA, tabA) = MakeTableTab(h, "A");
        var (_, tabB) = MakeTableTab(h, "B");
        var tabs = new[] { tabA, tabB };

        var targets = MainWindowViewModel.SelectRefreshTargets(
            MainWindowViewModel.PostTransactionRefresh.Structure, tabs, tabA);

        var only = Assert.Single(targets);
        Assert.Same(detailA, only.Detail); // the ACTIVE tab only
        Assert.True(only.Structure);
    }

    [Fact]
    public void Settle_WhenActiveTabIsNotTableDetail_RefreshesNothing()
    {
        // The user's reported scenario: editing/executing a PROCEDURE (active tab is
        // not a TableDetail) settles a tx — open table tabs must NOT be refreshed.
        using var h = new Harness();
        var (_, tabA) = MakeTableTab(h, "A");
        var (_, tabB) = MakeTableTab(h, "B");
        var tabs = new[] { tabA, tabB };

        Assert.Empty(MainWindowViewModel.SelectRefreshTargets(
            MainWindowViewModel.PostTransactionRefresh.Structure, tabs, activeTab: null));
    }

    [Fact]
    public void NoneRefresh_SelectsNothing()
    {
        using var h = new Harness();
        var (detail, tab) = MakeTableTab(h, "A");
        detail.HasPendingDataEdits = true;
        Assert.Empty(MainWindowViewModel.SelectRefreshTargets(
            MainWindowViewModel.PostTransactionRefresh.None, new[] { tab }, tab));
    }

    // ─── Domain → Type field sync (Type column must not go blank) ──────────

    [Fact]
    public void SelectingDomain_FillsTypeCell_KeepsDomainAsCanonicalType()
    {
        var owner = new ProcedureDetailTabViewModel("P");
        owner.SetAvailableDomains(new[] { new DomainSpec("T_KODPOCZ", "VARCHAR(6)") });
        var row = new ProcedureVariableRowViewModel(owner);

        row.DomainName = "T_KODPOCZ";

        Assert.Equal("VARCHAR", row.BaseType); // Type cell NOT blank
        Assert.Equal(6, row.Size);
        Assert.Equal("T_KODPOCZ", row.DomainName);
        Assert.Equal("T_KODPOCZ", row.ToVariable().TypeText); // canonical = the domain name
    }

    [Fact]
    public void PickingBaseType_OverDomain_ClearsDomain()
    {
        var owner = new ProcedureDetailTabViewModel("P");
        owner.SetAvailableDomains(new[] { new DomainSpec("T_KODPOCZ", "VARCHAR(6)") });
        var row = new ProcedureVariableRowViewModel(owner) { DomainName = "T_KODPOCZ" };

        row.BaseType = "INTEGER"; // user overrides to a plain type

        Assert.Null(row.DomainName);
        Assert.Equal("INTEGER", row.ToVariable().TypeText);
    }

    [Fact]
    public void DomainTypedVariable_ResolvesTypeCell_WhenDomainsArriveAfterLoad()
    {
        var owner = new ProcedureDetailTabViewModel("P");
        var row = ProcedureVariableRowViewModel.From(
            new ProcedureVariable { Name = "V", TypeText = "T_KODPOCZ" }, owner);
        Assert.Equal("T_KODPOCZ", row.DomainName); // detected as a domain at load

        owner.SetAvailableDomains(new[] { new DomainSpec("T_KODPOCZ", "VARCHAR(6)") });

        Assert.Equal("VARCHAR", row.BaseType); // Type cell resolved once domains arrived
        Assert.Equal("T_KODPOCZ", row.ToVariable().TypeText); // canonical preserved
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private static (TableDetailTabViewModel detail, WorkspaceTabViewModel tab) MakeTableTab(Harness h, string name)
    {
        var obj = new MetadataObject(name, MetadataObjectKind.Table);
        var detail = h.Main.CreateTableDetail(obj);
        var tab = WorkspaceTabViewModel.CreateTableDetail(h.Main, obj, detail, connectionProfileId: null);
        return (detail, tab);
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
