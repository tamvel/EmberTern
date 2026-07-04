using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Diagnostics;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Live database-session diagnostics for the Session Manager, from <c>MON$*</c> — no SYSDBA
/// required (a non-privileged user sees only their own attachments; the VM surfaces that).
///
/// Acquisition follows the proven readers (<see cref="FirebirdDiagnostics"/> /
/// <see cref="FirebirdPerfStatsReader"/>): runs on the METADATA lane, holds that lane's command
/// lock (captured once — gotcha #120), and reads with an implicit per-command transaction
/// (<c>cmd.Transaction = null</c>) so EVERY poll is a FRESH MON$ snapshot (the MON$-snapshot rule
/// — a long-lived reader silently freezes the view) and it never touches the user's data working
/// transaction. Returns Core DTOs only; holds all <c>Fb*</c> internally. Decoders reused from
/// <see cref="FirebirdDiagnostics"/>. Kill/cancel reuse
/// <see cref="FirebirdConnectionService.ExecuteAdminBatchAsync"/> (autonomous, auto-committed).
/// </summary>
public sealed class FirebirdSessionReader
{
    private readonly FirebirdConnectionService _connectionService;

    public FirebirdSessionReader(FirebirdConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    // --- SQL (kept as consts so the shape is unit-pinnable; FB 2.5+ column set, no FB4-only
    //     MON$SNAPSHOT_NUMBER so it stays portable across the supported engines) -------------

    // Attachment facts + attachment-scope record totals (MON$RECORD_STATS linked by MON$STAT_ID).
    internal const string SessionsSql =
        "SELECT a.MON$ATTACHMENT_ID, a.MON$USER, a.MON$ROLE, a.MON$REMOTE_PROCESS, " +
        "a.MON$REMOTE_ADDRESS, a.MON$REMOTE_PID, a.MON$REMOTE_PROTOCOL, a.MON$STATE, " +
        "a.MON$TIMESTAMP, a.MON$GARBAGE_COLLECTION, " +
        "rs.MON$RECORD_SEQ_READS, rs.MON$RECORD_IDX_READS, " +
        "rs.MON$RECORD_INSERTS, rs.MON$RECORD_UPDATES, rs.MON$RECORD_DELETES " +
        "FROM MON$ATTACHMENTS a " +
        "LEFT JOIN MON$RECORD_STATS rs ON rs.MON$STAT_ID = a.MON$STAT_ID " +
        "ORDER BY a.MON$ATTACHMENT_ID";

    // Active statements only — one row per running SQL; MON$SQL_TEXT is a text BLOB.
    internal const string StatementsSql =
        "SELECT MON$ATTACHMENT_ID, MON$STATEMENT_ID, MON$SQL_TEXT " +
        "FROM MON$STATEMENTS WHERE MON$STATE = 1 ORDER BY MON$ATTACHMENT_ID";

    internal const string TransactionsSql =
        "SELECT MON$TRANSACTION_ID, MON$ATTACHMENT_ID, MON$STATE, MON$TIMESTAMP, " +
        "MON$ISOLATION_MODE, MON$READ_ONLY, MON$AUTO_COMMIT, MON$AUTO_UNDO, " +
        "MON$OLDEST_TRANSACTION, MON$OLDEST_ACTIVE " +
        "FROM MON$TRANSACTIONS ORDER BY MON$TRANSACTION_ID";

    internal const string DatabaseStateSql =
        "SELECT MON$OLDEST_TRANSACTION, MON$OLDEST_ACTIVE, MON$OLDEST_SNAPSHOT, MON$NEXT_TRANSACTION " +
        "FROM MON$DATABASE";

    /// <summary>Reads the data (and, when independent, metadata) attachment ids so the reader can
    /// flag EmberTern's own sessions. Cheap; call once per poll (attachment ids are stable, but a
    /// reconnect changes them, so re-reading is harmless and keeps self-exclusion correct).</summary>
    public async Task<IReadOnlySet<long>> GetOwnAttachmentIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<long>();
        ids.Add(await ReadCurrentConnectionAsync(ConnectionRole.Data, cancellationToken).ConfigureAwait(false));
        if (_connectionService.MetadataIsIndependent)
        {
            ids.Add(await ReadCurrentConnectionAsync(ConnectionRole.Metadata, cancellationToken).ConfigureAwait(false));
        }
        ids.Remove(0);
        return ids;
    }

    /// <summary>All attachments (with their active statement + record totals), marking
    /// <paramref name="ownAttachmentIds"/> as self.</summary>
    public async Task<IReadOnlyList<SessionInfo>> ReadSessionsAsync(
        IReadOnlySet<long> ownAttachmentIds, CancellationToken cancellationToken = default)
    {
        var statements = await ReadActiveStatementsAsync(cancellationToken).ConfigureAwait(false);

        var connection = _connectionService.RequireOpenConnection(ConnectionRole.Metadata);
        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Metadata);
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = SessionsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = null; // implicit per-command tx → fresh MON$ snapshot each poll

            var results = new List<SessionInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = GetInt64(reader, 0);
                statements.TryGetValue(id, out var stmt);
                long seq = GetInt64(reader, 10), idx = GetInt64(reader, 11);
                long ins = GetInt64(reader, 12), upd = GetInt64(reader, 13), del = GetInt64(reader, 14);
                results.Add(new SessionInfo
                {
                    AttachmentId = id,
                    User = GetTrimmed(reader, 1),
                    Role = GetTrimmed(reader, 2),
                    Application = GetTrimmed(reader, 3),
                    Host = GetTrimmed(reader, 4),
                    RemotePid = reader.IsDBNull(5) ? null : (int)GetInt64(reader, 5),
                    Protocol = GetTrimmed(reader, 6),
                    StateCode = (int)GetInt64(reader, 7),
                    ConnectedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    GarbageCollectionAllowed = reader.IsDBNull(9) || GetInt64(reader, 9) != 0,
                    RecordReads = seq + idx,
                    RecordWrites = ins + upd + del,
                    CurrentStatement = stmt.Sql,
                    ActiveStatementId = stmt.StatementId,
                    IsSelf = ownAttachmentIds.Contains(id),
                });
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read MON$ATTACHMENTS: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>All live transactions, isolation decoded to words.</summary>
    public async Task<IReadOnlyList<TransactionInfo>> ReadTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection(ConnectionRole.Metadata);
        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Metadata);
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = TransactionsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = null;

            var results = new List<TransactionInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var isolation = reader.IsDBNull(4) ? -1 : (int)GetInt64(reader, 4);
                results.Add(new TransactionInfo
                {
                    TransactionId = GetInt64(reader, 0),
                    AttachmentId = GetInt64(reader, 1),
                    StateCode = (int)GetInt64(reader, 2),
                    StartedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    IsolationModeCode = isolation,
                    IsolationMode = ShortIsolation(isolation),
                    ReadOnly = !reader.IsDBNull(5) && GetInt64(reader, 5) != 0,
                    AutoCommit = !reader.IsDBNull(6) && GetInt64(reader, 6) != 0,
                    AutoUndo = !reader.IsDBNull(7) && GetInt64(reader, 7) != 0,
                    OldestTransaction = GetInt64(reader, 8),
                    OldestActive = GetInt64(reader, 9),
                });
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read MON$TRANSACTIONS: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>The database-wide transaction markers (OIT/OAT/OST/Next) from MON$DATABASE.</summary>
    public async Task<DatabaseTransactionState> ReadDatabaseStateAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection(ConnectionRole.Metadata);
        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Metadata);
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = DatabaseStateSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = null;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DatabaseTransactionState();
            }
            return new DatabaseTransactionState
            {
                OldestTransaction = GetInt64(reader, 0),
                OldestActive = GetInt64(reader, 1),
                OldestSnapshot = GetInt64(reader, 2),
                NextTransaction = GetInt64(reader, 3),
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read MON$DATABASE: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Cancels a running statement (soft — keeps the connection). Returns null on success,
    /// otherwise the server error. <c>DELETE FROM MON$STATEMENTS</c> (Firebird 2.5+).</summary>
    public async Task<string?> CancelStatementAsync(long statementId, CancellationToken cancellationToken = default)
    {
        var results = await _connectionService
            .ExecuteAdminBatchAsync(new[] { BuildCancelStatementSql(statementId) }, cancellationToken)
            .ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>Disconnects a session (hard — its work rolls back). Returns null on success,
    /// otherwise the server error. <c>DELETE FROM MON$ATTACHMENTS</c> (Firebird 2.5+).</summary>
    public async Task<string?> DisconnectSessionAsync(long attachmentId, CancellationToken cancellationToken = default)
    {
        var results = await _connectionService
            .ExecuteAdminBatchAsync(new[] { BuildDisconnectSessionSql(attachmentId) }, cancellationToken)
            .ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    // Numeric id → injection-safe to inline. Extracted so the destructive shapes are pinned.
    internal static string BuildCancelStatementSql(long statementId)
        => "DELETE FROM MON$STATEMENTS WHERE MON$STATEMENT_ID = "
           + statementId.ToString(CultureInfo.InvariantCulture);

    internal static string BuildDisconnectSessionSql(long attachmentId)
        => "DELETE FROM MON$ATTACHMENTS WHERE MON$ATTACHMENT_ID = "
           + attachmentId.ToString(CultureInfo.InvariantCulture);

    // --- helpers ---------------------------------------------------------------------------

    private async Task<Dictionary<long, (long StatementId, string Sql)>> ReadActiveStatementsAsync(
        CancellationToken cancellationToken)
    {
        var connection = _connectionService.RequireOpenConnection(ConnectionRole.Metadata);
        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Metadata);
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = StatementsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = null;

            var map = new Dictionary<long, (long, string)>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var att = GetInt64(reader, 0);
                if (map.ContainsKey(att))
                {
                    continue; // keep the first active statement per attachment
                }
                var statementId = GetInt64(reader, 1);
                var sql = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                map[att] = (statementId, sql);
            }
            return map;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read MON$STATEMENTS: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private async Task<long> ReadCurrentConnectionAsync(ConnectionRole role, CancellationToken cancellationToken)
    {
        var connection = _connectionService.RequireOpenConnection(role);
        var commandLock = _connectionService.GetCommandLock(role);
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CURRENT_CONNECTION FROM RDB$DATABASE";
            cmd.CommandTimeout = 0;
            cmd.Transaction = null;
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read the connection id: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private static long GetInt64(DbDataReader reader, int i)
        => reader.IsDBNull(i) ? 0L : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);

    private static string GetTrimmed(DbDataReader reader, int i)
        => reader.IsDBNull(i) ? string.Empty : reader.GetString(i).Trim();

    /// <summary>Compact isolation label for the grid (the verbose form lives in
    /// <see cref="FirebirdDiagnostics.DecodeIsolationMode"/>).</summary>
    internal static string ShortIsolation(int code) => code switch
    {
        0 => "Consistency",
        1 => "Snapshot",
        2 => "Read Committed",
        3 => "Read Committed",
        4 => "Read Committed",
        _ => "Unknown",
    };
}
