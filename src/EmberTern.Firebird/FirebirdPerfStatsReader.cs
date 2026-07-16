using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Performance;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>Captures per-table Natural/Index reads for the DATA connection via MON$
/// (verified on FB 5.0.3, no SYSDBA, no trace). Two lanes, deliberately:
///
///  • <see cref="GetDataAttachmentIdAsync"/> reads <c>CURRENT_CONNECTION</c> on the DATA
///    connection (the id whose stats we want), cached once by the caller.
///  • <see cref="SnapshotAsync"/> reads the MON$ counters on the METADATA connection,
///    filtered to that data attachment id. Reading from the metadata lane is what makes a
///    fresh before/after snapshot possible: each short read gets its own implicit tx (a new
///    MON$ snapshot), and it never touches the user's data working transaction, which must
///    stay open (manual-tx rule). The delta of two snapshots around the execution is the
///    query's per-table reads.
///
/// Returns Core DTOs only; holds all FbCommand internally.</summary>
public sealed class FirebirdPerfStatsReader
{
    // MON$ATTACHMENTS ⋈ MON$TABLE_STATS ⋈ MON$RECORD_STATS at connection scope, filtered to
    // one attachment; system tables excluded. Names are space-padded → TRIM.
    internal const string SnapshotSql =
        "SELECT TRIM(ts.MON$TABLE_NAME), rs.MON$RECORD_SEQ_READS, rs.MON$RECORD_IDX_READS, " +
        "rs.MON$RECORD_INSERTS, rs.MON$RECORD_UPDATES, rs.MON$RECORD_DELETES " +
        "FROM MON$ATTACHMENTS a " +
        "JOIN MON$TABLE_STATS ts ON ts.MON$STAT_ID = a.MON$STAT_ID " +
        "JOIN MON$RECORD_STATS rs ON rs.MON$STAT_ID = ts.MON$RECORD_STAT_ID " +
        "WHERE a.MON$ATTACHMENT_ID = @att " +
        "AND ts.MON$TABLE_NAME NOT STARTING WITH 'RDB$' " +
        "AND ts.MON$TABLE_NAME NOT STARTING WITH 'MON$' " +
        "AND ts.MON$TABLE_NAME NOT STARTING WITH 'SEC$'";

    private readonly FirebirdConnectionService _connectionService;
    private readonly MetadataLane _metadataLane;
    private readonly TransactionService? _dataTransactionService;

    public FirebirdPerfStatsReader(
        FirebirdConnectionService connectionService,
        MetadataLane metadataLane,
        TransactionService? dataTransactionService)
    {
        _connectionService = connectionService;
        _metadataLane = metadataLane;
        _dataTransactionService = dataTransactionService;
    }

    /// <summary>Reads the DATA connection's attachment id (<c>CURRENT_CONNECTION</c>).
    /// Cache it once per connection — it's stable for the connection's life.</summary>
    public async Task<long> GetDataAttachmentIdAsync(CancellationToken cancellationToken = default)
    {
        var connection = _dataTransactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
        var commandLock = _dataTransactionService?.CommandLock ?? _connectionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CURRENT_CONNECTION FROM RDB$DATABASE";
            cmd.CommandTimeout = 0;
            if (_dataTransactionService?.ActiveTransaction is { } tx)
            {
                cmd.Transaction = tx;
            }
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(value);
        }
        catch (FbException ex)
        {
            throw new PerformanceCaptureException(ex.Message?.Trim() ?? "Failed to read the connection id.", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Snapshots the per-table read counters for <paramref name="attachmentId"/>
    /// on the metadata lane. Each call is a fresh MON$ snapshot.</summary>
    public async Task<IReadOnlyList<PerTableReadRow>> SnapshotAsync(long attachmentId, CancellationToken cancellationToken = default)
    {
        var connection = _metadataLane.RequireOpenConnection();
        // Capture the lock once (gotcha #120).
        var commandLock = _metadataLane.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = SnapshotSql;
            cmd.CommandTimeout = 0;
            // Attach to the metadata working tx if one is active; otherwise the driver runs
            // this read in a fresh implicit tx — which gives the fresh MON$ snapshot we need.
            if (_metadataLane.TransactionForCommand is { } tx)
            {
                cmd.Transaction = tx;
            }
            cmd.Parameters.AddWithValue("@att", attachmentId);

            var rows = new List<PerTableReadRow>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                long seq = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                long idx = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                long ins = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                long upd = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                long del = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                if (name.Length > 0)
                {
                    rows.Add(new PerTableReadRow(name, seq, idx, ins, upd, del));
                }
            }
            return rows;
        }
        catch (FbException ex)
        {
            throw new PerformanceCaptureException(ex.Message?.Trim() ?? "Failed to read per-table statistics.", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }
}
