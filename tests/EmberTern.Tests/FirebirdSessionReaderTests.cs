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
}
