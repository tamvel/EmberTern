using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// TransactionService is THE user transaction — one, on the data attachment, NOWAIT.
/// It used to be parameterized by <c>ConnectionRole</c>, with a second "metadata" instance and a
/// degraded-mode fallback chain — machinery that existed only because the SQL Editor silently
/// routed DDL onto the metadata attachment. That routing is gone, so the role, the profile
/// selector and the fallback are gone: there is one constructor and one transaction.
/// </summary>
public class TransactionServiceTests
{
    // The TPB is fixed: read-committed / rec_version / write / NOWAIT. It is NOT read from the
    // stored per-connection profile — a legacy table-stability profile must never be able to make
    // the SQL console WAIT or lock whole tables.
    [Fact]
    public void UserTransaction_IsAlwaysReadCommittedNoWait()
    {
        var o = TransactionService.BuildTransactionOptions(TransactionProfile.ReadCommitted);
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Write));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.RecVersion));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.NoWait));
        Assert.False(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
    }

    [Fact]
    public void InitialStateIsIdle()
    {
        using var conn = new FirebirdConnectionService();
        using var tx = new TransactionService(conn);

        Assert.Equal(TransactionState.Idle, tx.State);
        Assert.True(tx.IsIdle);
        Assert.False(tx.IsActive);
        Assert.False(tx.IsError);
        Assert.False(tx.HasExecutedStatements);
        Assert.Equal(0, tx.StatementCount);
        Assert.Null(tx.ActiveTransaction);
    }

    [Fact]
    public async Task BeginAsync_WithoutConnection_ThrowsCleanly()
    {
        using var conn = new FirebirdConnectionService();
        using var tx = new TransactionService(conn);

        // No open connection — RequireOpenConnection throws InvalidOperationException
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => tx.BeginTransactionAsync());

        Assert.Equal(TransactionState.Idle, tx.State);
    }

    [Fact]
    public async Task RollbackAsync_WhenIdle_RemainsIdle()
    {
        using var conn = new FirebirdConnectionService();
        using var tx = new TransactionService(conn);

        await tx.RollbackAsync();

        Assert.Equal(TransactionState.Idle, tx.State);
    }

    [Fact]
    public void NotifyStatementExecuted_WhenIdle_DoesNothing()
    {
        using var conn = new FirebirdConnectionService();
        using var tx = new TransactionService(conn);

        tx.NotifyStatementExecuted();

        Assert.Equal(0, tx.StatementCount);
    }
}
