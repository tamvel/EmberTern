using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Pins the MON$ join verified against live FB 5.0.3 (design cycle) so it can't silently
// drift. The live read itself is smoke-verified against a real engine.
public class FirebirdPerfStatsReaderTests
{
    [Fact]
    public void SnapshotSql_JoinsAttachmentTableAndRecordStats()
    {
        var sql = FirebirdPerfStatsReader.SnapshotSql;
        Assert.Contains("MON$ATTACHMENTS", sql);
        Assert.Contains("MON$TABLE_STATS", sql);
        Assert.Contains("MON$RECORD_STATS", sql);
        Assert.Contains("MON$RECORD_SEQ_READS", sql);
        Assert.Contains("MON$RECORD_IDX_READS", sql);
    }

    [Fact]
    public void SnapshotSql_FiltersToAttachmentAndExcludesSystemTables()
    {
        var sql = FirebirdPerfStatsReader.SnapshotSql;
        Assert.Contains("a.MON$ATTACHMENT_ID = @att", sql);
        Assert.Contains("NOT STARTING WITH 'RDB$'", sql);
        Assert.Contains("NOT STARTING WITH 'MON$'", sql);
        Assert.Contains("NOT STARTING WITH 'SEC$'", sql);
    }
}
