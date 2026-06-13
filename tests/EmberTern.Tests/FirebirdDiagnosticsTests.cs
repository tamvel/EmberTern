using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class FirebirdDiagnosticsTests
{
    [Theory]
    [InlineData(0, "consistency (snapshot table stability)")]
    [InlineData(1, "concurrency (snapshot)")]
    [InlineData(2, "read committed (rec_version)")]
    [InlineData(3, "read committed (no_rec_version)")]
    [InlineData(4, "read committed (read_consistency)")]
    [InlineData(99, "unknown (99)")]
    public void DecodeIsolationMode_MapsKnownCodes(int code, string expected)
    {
        Assert.Equal(expected, FirebirdDiagnostics.DecodeIsolationMode(code));
    }

    [Theory]
    [InlineData(-1, "wait (infinite)")]
    [InlineData(0, "no wait")]
    [InlineData(5, "wait 5s")]
    public void DecodeLockTimeout_MapsKnownCodes(int code, string expected)
    {
        Assert.Equal(expected, FirebirdDiagnostics.DecodeLockTimeout(code));
    }

    [Fact]
    public void DecodeLockTimeout_MinValueIsNotApplicable()
    {
        Assert.Equal("n/a", FirebirdDiagnostics.DecodeLockTimeout(int.MinValue));
    }

    [Theory]
    [InlineData(0, "idle")]
    [InlineData(1, "active")]
    [InlineData(7, "unknown (7)")]
    public void DecodeTransactionState_MapsKnownCodes(int code, string expected)
    {
        Assert.Equal(expected, FirebirdDiagnostics.DecodeTransactionState(code));
    }

    [Theory]
    [InlineData(0, "idle")]
    [InlineData(1, "active")]
    public void DecodeAttachmentState_MapsKnownCodes(int code, string expected)
    {
        Assert.Equal(expected, FirebirdDiagnostics.DecodeAttachmentState(code));
    }

    // SQL-shape regression pins — the MON$ queries must target the right catalog
    // tables and columns. These are the source of truth for the manual verification.
    [Fact]
    public void CurrentTransactionSql_FiltersByCurrentTransaction()
    {
        Assert.Contains("MON$TRANSACTIONS", FirebirdDiagnostics.CurrentTransactionSql);
        Assert.Contains("MON$TRANSACTION_ID = CURRENT_TRANSACTION", FirebirdDiagnostics.CurrentTransactionSql);
        Assert.Contains("MON$ISOLATION_MODE", FirebirdDiagnostics.CurrentTransactionSql);
        Assert.Contains("MON$LOCK_TIMEOUT", FirebirdDiagnostics.CurrentTransactionSql);
    }

    [Fact]
    public void TransactionsSql_QueriesMonTransactions()
    {
        Assert.Contains("FROM MON$TRANSACTIONS", FirebirdDiagnostics.TransactionsSql);
        Assert.Contains("MON$ISOLATION_MODE", FirebirdDiagnostics.TransactionsSql);
        Assert.Contains("MON$LOCK_TIMEOUT", FirebirdDiagnostics.TransactionsSql);
        Assert.Contains("MON$READ_ONLY", FirebirdDiagnostics.TransactionsSql);
    }

    [Fact]
    public void AttachmentsSql_QueriesMonAttachments()
    {
        Assert.Contains("FROM MON$ATTACHMENTS", FirebirdDiagnostics.AttachmentsSql);
        Assert.Contains("MON$USER", FirebirdDiagnostics.AttachmentsSql);
        Assert.Contains("MON$REMOTE_ADDRESS", FirebirdDiagnostics.AttachmentsSql);
    }
}
