using EmberTern.Core.Connections;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

// Pins the per-profile TPB mapping so the IBExpert-matching profiles can't
// silently regress (e.g. back to the driver's IsolationLevel.ReadCommitted
// mapping, which ends in isc_tpb_WAIT).
public class TransactionTpbTests
{
    private static FbTransactionBehavior Behavior(TransactionProfile profile)
        => TransactionService.BuildTransactionOptions(profile).TransactionBehavior;

    // --- Read Committed (default): write + read_committed + rec_version + nowait ---

    [Fact]
    public void ReadCommitted_IsReadWrite()
        => Assert.True(Behavior(TransactionProfile.ReadCommitted).HasFlag(FbTransactionBehavior.Write));

    [Fact]
    public void ReadCommitted_IsReadCommitted()
        => Assert.True(Behavior(TransactionProfile.ReadCommitted).HasFlag(FbTransactionBehavior.ReadCommitted));

    [Fact]
    public void ReadCommitted_UsesRecordVersion()
        => Assert.True(Behavior(TransactionProfile.ReadCommitted).HasFlag(FbTransactionBehavior.RecVersion));

    [Fact]
    public void ReadCommitted_IsNoWait()
        => Assert.True(Behavior(TransactionProfile.ReadCommitted).HasFlag(FbTransactionBehavior.NoWait));

    [Fact]
    public void ReadCommitted_IsNotWaitAndNotConsistency()
    {
        var b = Behavior(TransactionProfile.ReadCommitted);
        Assert.False(b.HasFlag(FbTransactionBehavior.Wait));
        Assert.False(b.HasFlag(FbTransactionBehavior.Consistency));
    }

    // --- Snapshot: write + concurrency + nowait ---

    [Fact]
    public void Snapshot_IsConcurrencyReadWriteNoWait()
    {
        var b = Behavior(TransactionProfile.Snapshot);
        Assert.True(b.HasFlag(FbTransactionBehavior.Concurrency));
        Assert.True(b.HasFlag(FbTransactionBehavior.Write));
        Assert.True(b.HasFlag(FbTransactionBehavior.NoWait));
    }

    [Fact]
    public void Snapshot_IsNotReadCommittedAndNotConsistency()
    {
        var b = Behavior(TransactionProfile.Snapshot);
        Assert.False(b.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.False(b.HasFlag(FbTransactionBehavior.Consistency));
    }

    // --- Read Only Table Stability: read + consistency (no nowait per spec) ---

    [Fact]
    public void ReadOnlyTableStability_IsReadConsistency()
    {
        var b = Behavior(TransactionProfile.ReadOnlyTableStability);
        Assert.True(b.HasFlag(FbTransactionBehavior.Read));
        Assert.True(b.HasFlag(FbTransactionBehavior.Consistency));
    }

    [Fact]
    public void ReadOnlyTableStability_IsNotWriteAndNotNoWait()
    {
        var b = Behavior(TransactionProfile.ReadOnlyTableStability);
        Assert.False(b.HasFlag(FbTransactionBehavior.Write));
        Assert.False(b.HasFlag(FbTransactionBehavior.NoWait));
    }

    // --- Read Write Table Stability: write + consistency (no nowait per spec) ---

    [Fact]
    public void ReadWriteTableStability_IsWriteConsistency()
    {
        var b = Behavior(TransactionProfile.ReadWriteTableStability);
        Assert.True(b.HasFlag(FbTransactionBehavior.Write));
        Assert.True(b.HasFlag(FbTransactionBehavior.Consistency));
    }

    [Fact]
    public void ReadWriteTableStability_IsNotReadAndNotNoWait()
    {
        var b = Behavior(TransactionProfile.ReadWriteTableStability);
        Assert.False(b.HasFlag(FbTransactionBehavior.Read));
        Assert.False(b.HasFlag(FbTransactionBehavior.NoWait));
    }
}
