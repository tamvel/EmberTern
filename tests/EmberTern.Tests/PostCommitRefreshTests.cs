using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// After a transaction settles, a TableDetail tab must refresh by LANE: a metadata
/// (DDL) commit/rollback warrants a full structure reload; a data (DML) commit/rollback
/// must only reload the data preview — re-reading the schema is wasted work that froze
/// the UI and transiently dropped the primary-key detection ("Table has no primary key
/// — only INSERT is available"). These tests pin the routing decision and that the
/// data-only refresh leaves the structure (Fields/PK) intact.
/// </summary>
public class PostCommitRefreshTests
{
    [Fact]
    public void DecidePostTransactionRefresh_Commit_NeverRefreshes()
    {
        // A COMMIT never refreshes — the UI already shows the committed state, and a
        // post-commit reload opens an extra transaction that re-fires any ON-COMMIT
        // database trigger (the user's XXX_WS_TRANS_ON_COMMIT audit storm).
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: true, metadataSettled: false, wasRollback: false));
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: false, metadataSettled: true, wasRollback: false));
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: true, metadataSettled: true, wasRollback: false));
    }

    [Fact]
    public void DecidePostTransactionRefresh_Rollback_RoutesByLane()
    {
        // data rolled back, metadata not → DATA-only reload (revert optimistic grid writes)
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.DataOnly,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: true, metadataSettled: false, wasRollback: true));
        // metadata rolled back → full structure reload (revert reverted DDL)
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.Structure,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: false, metadataSettled: true, wasRollback: true));
        // both coalesced → structure wins (its reload re-reads the data preview too)
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.Structure,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: true, metadataSettled: true, wasRollback: true));
        // nothing settled → no refresh
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(dataSettled: false, metadataSettled: false, wasRollback: true));
    }

    [Fact]
    public async Task RefreshDataAfterTransaction_KeepsStructureAndPrimaryKey()
    {
        // A data-lane refresh must NOT tear down the structure. Build a VM with a PK
        // field + loaded data, run the data-after-transaction refresh, and confirm the
        // Fields and primary-key state survive — this is what stops the spurious
        // "Table has no primary key" message after a data Commit.
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = true, NotNull = true });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAME" });
        vm.RefreshPrimaryKeyColumns();
        vm.DataResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("ID", typeof(int)), new QueryColumn("NAME", typeof(string)) },
            Rows = new[] { new object?[] { 1, "Alice" } },
        };

        Assert.True(vm.HasPrimaryKey);

        await vm.RefreshDataAfterTransactionAsync();

        Assert.True(vm.HasPrimaryKey);
        Assert.Equal(new[] { "ID" }, vm.PrimaryKeyColumns);
        Assert.Equal(2, vm.Fields.Count);
        Assert.Equal(string.Empty, vm.EditModeHint);
    }
}
