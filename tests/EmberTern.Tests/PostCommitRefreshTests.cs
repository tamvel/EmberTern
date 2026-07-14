using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// After the (single) user transaction settles, the refresh is chosen by WHAT THE TRANSACTION DID,
/// not by which lane it ran on — there is only one lane now.
/// <list type="bullet">
/// <item>DML-only commit → NOTHING. The UI already shows the committed state, and a post-commit
/// reload opens an extra transaction that re-fires any ON-COMMIT database trigger (the user's
/// XXX_WS_TRANS_ON_COMMIT audit storm — gotcha #119).</item>
/// <item>DML rollback → reload the data preview only (revert the optimistic grid writes). Re-reading
/// the schema is wasted work that froze the UI and transiently dropped PK detection.</item>
/// <item>DDL transaction → full structure reload on EITHER outcome: on commit the object becomes
/// visible for the first time (uncommitted DDL is invisible to the read-only metadata attachment);
/// on rollback it must disappear again.</item>
/// </list>
/// </summary>
public class PostCommitRefreshTests
{
    [Fact]
    public void DmlOnlyCommit_NeverRefreshes()
        => Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(settled: true, schemaChanged: false, wasRollback: false));

    [Fact]
    public void DmlRollback_ReloadsDataPreviewOnly()
        => Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.DataOnly,
            MainWindowViewModel.DecidePostTransactionRefresh(settled: true, schemaChanged: false, wasRollback: true));

    // The behavioural change the user signed off on: an object created in the SQL Editor appears
    // only after Commit — so a DDL COMMIT is exactly when the structure must be reloaded.
    [Fact]
    public void DdlCommit_ReloadsStructure()
        => Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.Structure,
            MainWindowViewModel.DecidePostTransactionRefresh(settled: true, schemaChanged: true, wasRollback: false));

    [Fact]
    public void DdlRollback_ReloadsStructure()
        => Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.Structure,
            MainWindowViewModel.DecidePostTransactionRefresh(settled: true, schemaChanged: true, wasRollback: true));

    [Fact]
    public void NothingSettled_NeverRefreshes()
    {
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(settled: false, schemaChanged: true, wasRollback: true));
        Assert.Equal(
            MainWindowViewModel.PostTransactionRefresh.None,
            MainWindowViewModel.DecidePostTransactionRefresh(settled: false, schemaChanged: false, wasRollback: false));
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
