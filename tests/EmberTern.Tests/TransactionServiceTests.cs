using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class TransactionServiceTests
{
    [Fact]
    public void ConvenienceCtor_IsDataLane()
    {
        using var conn = new FirebirdConnectionService();
        using var tx = new TransactionService(conn);
        Assert.Equal(ConnectionRole.Data, tx.Role);
    }

    [Fact]
    public void MetadataLaneCtor_HasMetadataRole()
    {
        using var conn = new FirebirdConnectionService();
        using var data = new TransactionService(conn);
        using var meta = new TransactionService(
            conn, ConnectionRole.Metadata,
            p => p?.MetadataTransactionProfile ?? TransactionProfile.ReadCommitted, data);
        Assert.Equal(ConnectionRole.Metadata, meta.Role);
    }

    [Fact]
    public async Task MetadataLane_DegradesToDataLane_WhenNoSecondAttachment_ThrowsCleanlyWithoutConnection()
    {
        using var conn = new FirebirdConnectionService();
        using var data = new TransactionService(conn);
        using var meta = new TransactionService(
            conn, ConnectionRole.Metadata,
            p => p?.MetadataTransactionProfile ?? TransactionProfile.ReadCommitted, data);

        // Not connected → metadata is not independent → metadata Begin delegates to the
        // data lane, whose RequireOpenConnection throws InvalidOperationException.
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => meta.BeginTransactionAsync());
        Assert.Equal(TransactionState.Idle, meta.State);
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
