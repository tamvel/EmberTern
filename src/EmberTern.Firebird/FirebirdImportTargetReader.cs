using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Reads what the import needs to know about its destination: the columns, and the triggers that can rewrite
/// what gets stored.
/// <para>
/// ⭐ <b>A thin adapter, deliberately.</b> The columns come from
/// <see cref="FirebirdMetadataReader.ListColumnsAsync"/> — the codebase's existing owner of "what columns does
/// this table have", which already reports the type, nullability, DEFAULT, computed flag and the ALWAYS/BY
/// DEFAULT identity distinction the INSERT builder depends on. A second column query here would be a second
/// answer to one question, and the two would eventually disagree about something that decides whether a
/// generated INSERT is valid.
/// </para>
/// <para>
/// The ONE thing it adds is the BEFORE INSERT trigger list, because no existing reader surfaces exactly that:
/// a trigger can overwrite an imported value, and a user who does not know one is there cannot understand the
/// result (design R6). It never changes what the import does — it is shown, and that is all.
/// </para>
/// <para>
/// Everything runs on the <b>Metadata lane</b>: read-only, implicit per-command transactions, so browsing a
/// target can neither block the user's working transaction nor be blocked by it.
/// </para>
/// </summary>
public sealed class FirebirdImportTargetReader
{
    private readonly FirebirdMetadataReader _metadataReader;
    private readonly MetadataLane _lane;

    public FirebirdImportTargetReader(FirebirdMetadataReader metadataReader, MetadataLane lane)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _lane = lane ?? throw new ArgumentNullException(nameof(lane));
    }

    /// <summary>
    /// Resolves one target. Returns <c>null</c> when the table has no columns in the catalog — i.e. it is not
    /// there (renamed, dropped, or a profile written against a different database). That is a
    /// <see cref="ImportDiagnosticCode.TargetNotFound"/> readiness item, not an exception: a stale profile is
    /// an ordinary situation the strip explains, not a crash (§4.8.5).
    /// </summary>
    public async Task<ImportTarget?> ReadTargetAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return null;

        var columns = await _metadataReader.ListColumnsAsync(tableName, cancellationToken).ConfigureAwait(false);
        if (columns.Count == 0) return null;

        var triggers = await ReadBeforeInsertTriggersAsync(tableName, cancellationToken).ConfigureAwait(false);
        return new ImportTarget(tableName, columns, triggers);
    }

    /// <summary>
    /// The target's ACTIVE BEFORE INSERT triggers.
    /// <para>
    /// The event decoding is <see cref="FirebirdTableDetailReader.DecodeTriggerHeader"/>, reused rather than
    /// re-derived: <c>RDB$TRIGGER_TYPE</c> is a bit-encoded value where a multi-action trigger
    /// (<c>BEFORE INSERT OR UPDATE</c>) is not simply "type 1", and a database-level or DDL trigger is not a
    /// relation trigger at all. Hand-rolling `type = 1` here would silently miss every multi-action trigger —
    /// which is exactly the kind that tends to rewrite an inserted value.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadBeforeInsertTriggersAsync(
        string tableName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return Array.Empty<string>();

        var connection = _lane.RequireOpenConnection();
        var commandLock = _lane.CommandLock;   // capture once — gotchas #98 / #120
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateGuardedCommand(
                "SELECT TRIM(RDB$TRIGGER_NAME), RDB$TRIGGER_TYPE FROM RDB$TRIGGERS " +
                "WHERE RDB$RELATION_NAME = @name " +
                "  AND COALESCE(RDB$TRIGGER_INACTIVE, 0) = 0 " +
                "  AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
                "ORDER BY RDB$TRIGGER_SEQUENCE, RDB$TRIGGER_NAME");
            cmd.CommandTimeout = 0;
            cmd.Transaction = _lane.TransactionForCommand;
            cmd.AddGuardedParameter("@name", tableName);

            var triggers = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                // BIGINT: a DB-level trigger carries a value far outside the relation-trigger range.
                var triggerType = reader.GetInt64(1);
                var (isBefore, firesOnInsert, _, _) = FirebirdTableDetailReader.DecodeTriggerHeader(triggerType);
                if (!isBefore || !firesOnInsert) continue;

                var name = reader.GetString(0).Trim();
                if (name.Length > 0) triggers.Add(name);
            }
            return triggers;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException(
                $"Could not read BEFORE INSERT triggers for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }
}
