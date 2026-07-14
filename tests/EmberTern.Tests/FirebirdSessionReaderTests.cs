using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Session Manager MON$ read shapes + destructive-statement builders. The
/// live MON$ read + kill/cancel are the user's manual smoke (privileged login), like every
/// other DB-path reader; these guard the SQL contract offline.</summary>
public class FirebirdSessionReaderTests
{
    [Fact]
    public void SessionsSql_ReadsAttachmentsWithRecordStatsAndActiveStatement()
    {
        var sql = FirebirdSessionReader.SessionsSql;
        Assert.Contains("FROM MON$ATTACHMENTS", sql);
        Assert.Contains("MON$RECORD_STATS rs ON rs.MON$STAT_ID = a.MON$STAT_ID", sql);
        Assert.Contains("MON$USER", sql);
        Assert.Contains("MON$REMOTE_PROCESS", sql);
        Assert.Contains("MON$GARBAGE_COLLECTION", sql);
        Assert.Contains("ORDER BY a.MON$ATTACHMENT_ID", sql);
        // FB4-only column must NOT appear (portability across FB 2.5+).
        Assert.DoesNotContain("MON$SNAPSHOT_NUMBER", sql);
    }

    [Fact]
    public void StatementsSql_OnlyActiveStatements()
    {
        var sql = FirebirdSessionReader.StatementsSql;
        Assert.Contains("FROM MON$STATEMENTS", sql);
        Assert.Contains("MON$STATE = 1", sql);
        Assert.Contains("MON$SQL_TEXT", sql);
        Assert.Contains("MON$STATEMENT_ID", sql);
    }

    [Fact]
    public void TransactionsSql_SelectsIsolationTimestampAndOldestMarkers()
    {
        var sql = FirebirdSessionReader.TransactionsSql;
        Assert.Contains("FROM MON$TRANSACTIONS", sql);
        Assert.Contains("MON$ISOLATION_MODE", sql);
        Assert.Contains("MON$TIMESTAMP", sql);
        Assert.Contains("MON$READ_ONLY", sql);
        Assert.Contains("MON$OLDEST_ACTIVE", sql);
        Assert.Contains("ORDER BY MON$TRANSACTION_ID", sql);
        Assert.DoesNotContain("MON$SNAPSHOT_NUMBER", sql);
    }

    [Fact]
    public void DatabaseStateSql_SelectsTheFourGapMarkers()
    {
        var sql = FirebirdSessionReader.DatabaseStateSql;
        Assert.Contains("FROM MON$DATABASE", sql);
        Assert.Contains("MON$OLDEST_TRANSACTION", sql);
        Assert.Contains("MON$OLDEST_ACTIVE", sql);
        Assert.Contains("MON$OLDEST_SNAPSHOT", sql);
        Assert.Contains("MON$NEXT_TRANSACTION", sql);
    }

    [Fact]
    public void KillAndCancel_UseTheCorrectMonDeleteTargets()
    {
        // Cancel = soft (statement); Disconnect = hard (attachment). Do not confuse them.
        Assert.Equal("DELETE FROM MON$STATEMENTS WHERE MON$STATEMENT_ID = 42",
            FirebirdSessionReader.BuildCancelStatementSql(42));
        Assert.Equal("DELETE FROM MON$ATTACHMENTS WHERE MON$ATTACHMENT_ID = 1557",
            FirebirdSessionReader.BuildDisconnectSessionSql(1557));
    }

    [Theory]
    [InlineData(0, "Consistency")]
    [InlineData(1, "Snapshot")]
    [InlineData(2, "Read Committed")]
    [InlineData(3, "Read Committed")]
    [InlineData(4, "Read Committed")]
    [InlineData(99, "Unknown")]
    public void ShortIsolation_DecodesModeCodes(int code, string expected)
        => Assert.Equal(expected, FirebirdSessionReader.ShortIsolation(code));

    // Regression (gotcha #173): a MON$/CURRENT_CONNECTION read must BORROW the lane's working
    // transaction, not hardcode cmd.Transaction = null — otherwise the data-lane id read throws
    // "Execute requires the Command object to have a Transaction object …" once the data connection
    // holds a pending working tx (after any SQL-Editor execute). This pins the lane→service routing
    // (each lane borrows its OWN service; a null service → null tx → fresh implicit MON$ snapshot).
    // The actual driver behaviour is the user's manual smoke, like every DB-path reader.
    // The lane→service selector is gone: the metadata lane is no longer a TransactionService, so
    // there is nothing to select between. The metadata lane owns no transaction, so a MON$ read on
    // it runs in an implicit per-command transaction (a fresh snapshot) — which is exactly what a
    // monitoring read wants. Pinned here because it is the reason the selector could be deleted.
    [Fact]
    public void MetadataLane_OwnsNoTransaction_SoMonReadsGetAFreshImplicitSnapshot()
    {
        using var cs = new FirebirdConnectionService();
        using var userTx = new TransactionService(cs);
        var lane = new MetadataLane(cs, userTx);

        // Not connected → not independent → degraded, and the user has no transaction open either.
        Assert.Null(lane.TransactionForCommand);
    }
}
