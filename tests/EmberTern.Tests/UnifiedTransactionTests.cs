using System;
using System.IO;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// There is exactly ONE user transaction (the data attachment) and ONE Commit / Rollback pair.
///
/// <para>This file used to pin a dual-lane model — <c>DecideCommitLanes</c> /
/// <c>DecideRollbackLanes</c> deciding which of two transactions to settle — which existed only
/// because the SQL Editor silently routed DDL onto a second, "metadata" transaction. The SQL
/// Editor is a classic console now (one attachment, one transaction, NOWAIT, no routing), so
/// there is nothing to decide. These pin the collapsed model instead.</para>
/// </summary>
public class UnifiedTransactionTests
{
    private static MainWindowViewModel NewVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-tx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new MainWindowViewModel(new ConnectionProfileStore(dir), new FirebirdConnectionService());
    }

    [Fact]
    public void NothingOpen_NeitherCommitNorRollbackIsOffered()
    {
        var vm = NewVm();
        Assert.False(vm.CanCommitAll);
        Assert.False(vm.CanRollbackAll);
    }

    // The one transaction is the whole model — no lane reconciliation, no second Commit.
    [Fact]
    public void TheOnlyTransaction_IsTheDataTransaction()
    {
        var vm = NewVm();
        Assert.True(vm.IsTransactionIdle);
        Assert.False(vm.IsTransactionActive);
        // The dual-lane surface is gone: these members no longer exist, which is the point of the
        // refactor. (Kept as a comment rather than a reflection assert — the compiler pins it.)
    }

    // ─── Autonomous admin batch (SET STATISTICS auto-commit) ──────────────

    [Fact]
    public async Task ExecuteAdminBatch_NotConnected_Throws()
    {
        using var svc = new FirebirdConnectionService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExecuteAdminBatchAsync(new[] { "SET STATISTICS INDEX X" }));
    }

    [Fact]
    public async Task ExecuteAdminBatch_EmptyList_ReturnsEmpty()
    {
        using var svc = new FirebirdConnectionService();
        var result = await svc.ExecuteAdminBatchAsync(Array.Empty<string>());
        Assert.Empty(result);
    }
}
