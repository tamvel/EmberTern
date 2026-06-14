using System;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The UI exposes a SINGLE Commit / Rollback pair; the app decides which lane(s) to
/// settle. These pin the pure lane-selection decisions (Commit settles every open lane,
/// Rollback reverts every active/error lane, both when both are open) and the autonomous
/// admin-batch contract used for SET STATISTICS (auto-committed, no working tx left).
/// </summary>
public class UnifiedTransactionTests
{
    [Fact]
    public void DecideCommitLanes_BothActive_CommitsBoth()
        => Assert.Equal((true, true), MainWindowViewModel.DecideCommitLanes(dataActive: true, metadataIndependent: true, metadataActive: true));

    [Fact]
    public void DecideCommitLanes_OnlyData()
        => Assert.Equal((true, false), MainWindowViewModel.DecideCommitLanes(dataActive: true, metadataIndependent: true, metadataActive: false));

    [Fact]
    public void DecideCommitLanes_OnlyMetadata()
        => Assert.Equal((false, true), MainWindowViewModel.DecideCommitLanes(dataActive: false, metadataIndependent: true, metadataActive: true));

    [Fact]
    public void DecideCommitLanes_MetadataNotIndependent_IsIgnored()
        => Assert.Equal((true, false), MainWindowViewModel.DecideCommitLanes(dataActive: true, metadataIndependent: false, metadataActive: true));

    [Fact]
    public void DecideRollbackLanes_ErrorStateCounts()
        => Assert.Equal((true, true), MainWindowViewModel.DecideRollbackLanes(dataActive: false, dataError: true, metadataIndependent: true, metadataActive: false, metadataError: true));

    [Fact]
    public void DecideRollbackLanes_NothingPending()
        => Assert.Equal((false, false), MainWindowViewModel.DecideRollbackLanes(dataActive: false, dataError: false, metadataIndependent: true, metadataActive: false, metadataError: false));

    [Fact]
    public void DecideRollbackLanes_MetadataNotIndependent_IsIgnored()
        => Assert.Equal((true, false), MainWindowViewModel.DecideRollbackLanes(dataActive: true, dataError: false, metadataIndependent: false, metadataActive: true, metadataError: true));

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
