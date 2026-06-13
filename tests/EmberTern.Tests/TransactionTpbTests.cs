using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

// Pins the working-transaction TPB so the IBExpert-matching profile
// (write + read_committed + rec_version + nowait) can't silently regress back to
// the driver's IsolationLevel.ReadCommitted mapping (which ends in isc_tpb_WAIT).
public class TransactionTpbTests
{
    [Fact]
    public void WorkingTransaction_IsReadWrite()
    {
        var options = TransactionService.BuildWorkingTransactionOptions();
        Assert.True(options.TransactionBehavior.HasFlag(FbTransactionBehavior.Write));
    }

    [Fact]
    public void WorkingTransaction_IsReadCommitted()
    {
        var options = TransactionService.BuildWorkingTransactionOptions();
        Assert.True(options.TransactionBehavior.HasFlag(FbTransactionBehavior.ReadCommitted));
    }

    [Fact]
    public void WorkingTransaction_UsesRecordVersion()
    {
        var options = TransactionService.BuildWorkingTransactionOptions();
        Assert.True(options.TransactionBehavior.HasFlag(FbTransactionBehavior.RecVersion));
    }

    [Fact]
    public void WorkingTransaction_IsNoWait()
    {
        var options = TransactionService.BuildWorkingTransactionOptions();
        Assert.True(options.TransactionBehavior.HasFlag(FbTransactionBehavior.NoWait));
    }

    [Fact]
    public void WorkingTransaction_IsNotWait()
    {
        // The whole point of the change: must NOT carry the WAIT flag.
        var options = TransactionService.BuildWorkingTransactionOptions();
        Assert.False(options.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
    }

    [Fact]
    public void WorkingTransaction_IsNotConsistency()
    {
        // Consistency (table stability) would be far too aggressive — it locks
        // whole tables and is exactly what we want to avoid.
        var options = TransactionService.BuildWorkingTransactionOptions();
        Assert.False(options.TransactionBehavior.HasFlag(FbTransactionBehavior.Consistency));
    }
}
