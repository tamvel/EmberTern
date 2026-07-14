using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Server-side metadata search — the "the DB knows" part of Global Search: object
/// SOURCE bodies (procedure/function/trigger/view/package), exception MESSAGE, and
/// table FIELD names, via Firebird's <c>CONTAINING</c> (case-insensitive, byte-level).
/// Object NAMES are matched client-side from the Explorer's name cache
/// (<see cref="EmberTern.Core.Search.MetadataNameSearch"/>), not here.
///
/// Follows the reader conventions: metadata lane, capture-the-command-lock ONCE for
/// the whole batch (gotcha #120), attach to the working tx or run implicit
/// (<c>cmd.Transaction = ActiveTransaction</c>, gotcha #173), FB-version gate for the
/// FB3+ catalogs (gotcha #146), and <see cref="MetadataReadException"/> on FbException.
/// </summary>
public sealed class FirebirdMetadataSearchReader
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly MetadataLane _lane;

    public FirebirdMetadataSearchReader(FirebirdConnectionService connectionService)
        : this(connectionService, new MetadataLane(connectionService))
    {
    }

    public FirebirdMetadataSearchReader(FirebirdConnectionService connectionService, MetadataLane lane)
    {
        _connectionService = connectionService;
        _lane = lane;
    }

    private FbConnection LaneConnection()
        => _lane.RequireOpenConnection();
    private SemaphoreSlim LaneLock()
        => _lane.CommandLock;

    /// <summary>
    /// Runs the DB-side searches for <paramref name="query"/> and returns the hits
    /// (Source / Message / FieldName). Names are NOT searched here. Blank term → empty.
    /// </summary>
    public async Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(
        MetadataSearchQuery query, CancellationToken cancellationToken = default)
    {
        var hits = new List<MetadataSearchHit>();
        if (query is null || string.IsNullOrWhiteSpace(query.Term)) return hits;
        if (!query.MatchSource && !(query.MatchNames && query.Includes(MetadataObjectKind.Table)))
            return hits; // nothing this reader can contribute

        var connection = LaneConnection();
        int major = FirebirdDdlReader.ParseServerMajor(connection.ServerVersion);

        // Capture the lock ONCE for the whole batch (gotcha #120) — re-evaluating
        // LaneLock() at Release could release a different semaphore than acquired.
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (query.MatchSource)
            {
                if (query.Includes(MetadataObjectKind.Procedure))
                    await RunSourceAsync(hits, connection, MetadataObjectKind.Procedure, ProcedureSourceSql, query.Term, cancellationToken).ConfigureAwait(false);
                if (query.Includes(MetadataObjectKind.View))
                    await RunSourceAsync(hits, connection, MetadataObjectKind.View, ViewSourceSql, query.Term, cancellationToken).ConfigureAwait(false);
                if (query.Includes(MetadataObjectKind.Trigger))
                    await RunSourceAsync(hits, connection, MetadataObjectKind.Trigger, TriggerSourceSql, query.Term, cancellationToken).ConfigureAwait(false);
                // RDB$FUNCTION_SOURCE / RDB$PACKAGES only exist on FB 3+ (gotcha #146).
                if (major >= 3 && query.Includes(MetadataObjectKind.Function))
                    await RunSourceAsync(hits, connection, MetadataObjectKind.Function, FunctionSourceSql, query.Term, cancellationToken).ConfigureAwait(false);
                if (major >= 3 && query.Includes(MetadataObjectKind.Package))
                    await RunPackageSourceAsync(hits, connection, query.Term, cancellationToken).ConfigureAwait(false);
                if (query.Includes(MetadataObjectKind.Exception))
                    await RunMessageAsync(hits, connection, query.Term, cancellationToken).ConfigureAwait(false);
            }

            // Table field names are a name match the object-name cache can't cover.
            if (query.MatchNames && query.Includes(MetadataObjectKind.Table))
                await RunFieldsAsync(hits, connection, query.Term, cancellationToken).ConfigureAwait(false);

            return hits;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Metadata search failed: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // One source-blob query: name + source. Count occurrences in the source (ASCII-reliable;
    // non-ASCII best-effort — CONTAINING already matched, so a 0 client-side count falls back
    // to 1 rather than hiding the hit).
    private async Task RunSourceAsync(
        List<MetadataSearchHit> hits, FbConnection connection, MetadataObjectKind kind, string sql, string term, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        cmd.Transaction = _lane.TransactionForCommand;
        cmd.Parameters.AddWithValue("@term", term);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var name = reader.GetString(0).Trim();
            if (name.Length == 0) continue;
            var source = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            int count = SearchTextMatch.CountOccurrences(source, term, caseSensitive: false);
            hits.Add(new MetadataSearchHit(kind, name, SearchMatchLocation.Source, count == 0 ? 1 : count));
        }
    }

    private async Task RunPackageSourceAsync(List<MetadataSearchHit> hits, FbConnection connection, string term, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = PackageSourceSql;
        cmd.CommandTimeout = 0;
        cmd.Transaction = _lane.TransactionForCommand;
        cmd.Parameters.AddWithValue("@term", term);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var name = reader.GetString(0).Trim();
            if (name.Length == 0) continue;
            var header = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var body = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            int count = SearchTextMatch.CountOccurrences(header, term, caseSensitive: false)
                      + SearchTextMatch.CountOccurrences(body, term, caseSensitive: false);
            hits.Add(new MetadataSearchHit(MetadataObjectKind.Package, name, SearchMatchLocation.Source, count == 0 ? 1 : count));
        }
    }

    private async Task RunMessageAsync(List<MetadataSearchHit> hits, FbConnection connection, string term, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ExceptionMessageSql;
        cmd.CommandTimeout = 0;
        cmd.Transaction = _lane.TransactionForCommand;
        cmd.Parameters.AddWithValue("@term", term);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var name = reader.GetString(0).Trim();
            if (name.Length == 0) continue;
            var message = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            int count = SearchTextMatch.CountOccurrences(message, term, caseSensitive: false);
            hits.Add(new MetadataSearchHit(MetadataObjectKind.Exception, name, SearchMatchLocation.Message, count == 0 ? 1 : count));
        }
    }

    private async Task RunFieldsAsync(List<MetadataSearchHit> hits, FbConnection connection, string term, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = TableFieldSql;
        cmd.CommandTimeout = 0;
        cmd.Transaction = _lane.TransactionForCommand;
        cmd.Parameters.AddWithValue("@term", term);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
            var table = reader.GetString(0).Trim();
            var field = reader.GetString(1).Trim();
            if (table.Length == 0 || field.Length == 0) continue;
            int count = SearchTextMatch.CountOccurrences(field, term, caseSensitive: false);
            hits.Add(new MetadataSearchHit(MetadataObjectKind.Table, table, SearchMatchLocation.FieldName, count == 0 ? 1 : count, field));
        }
    }

    // Whether the source search for this kind needs FB 3+ catalogs. Internal so the
    // version-gate logic is unit-testable without a live connection.
    internal static bool RequiresFb3(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Function or MetadataObjectKind.Package;

    // SQL constants — internal so tests can pin the shape / FB compatibility
    // (CONTAINING is FB 2.5+; RDB$FUNCTION_SOURCE + RDB$PACKAGES are gated to FB 3+).
    internal const string ProcedureSourceSql =
        "SELECT TRIM(RDB$PROCEDURE_NAME), RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES " +
        "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$PROCEDURE_SOURCE CONTAINING @term " +
        "ORDER BY RDB$PROCEDURE_NAME";

    internal const string ViewSourceSql =
        "SELECT TRIM(RDB$RELATION_NAME), RDB$VIEW_SOURCE FROM RDB$RELATIONS " +
        "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NOT NULL AND RDB$VIEW_SOURCE CONTAINING @term " +
        "ORDER BY RDB$RELATION_NAME";

    internal const string TriggerSourceSql =
        "SELECT TRIM(RDB$TRIGGER_NAME), RDB$TRIGGER_SOURCE FROM RDB$TRIGGERS " +
        "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$TRIGGER_SOURCE CONTAINING @term " +
        "ORDER BY RDB$TRIGGER_NAME";

    internal const string FunctionSourceSql =
        "SELECT TRIM(RDB$FUNCTION_NAME), RDB$FUNCTION_SOURCE FROM RDB$FUNCTIONS " +
        "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$FUNCTION_SOURCE CONTAINING @term " +
        "ORDER BY RDB$FUNCTION_NAME";

    internal const string PackageSourceSql =
        "SELECT TRIM(RDB$PACKAGE_NAME), RDB$PACKAGE_HEADER_SOURCE, RDB$PACKAGE_BODY_SOURCE FROM RDB$PACKAGES " +
        "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
        "AND (RDB$PACKAGE_HEADER_SOURCE CONTAINING @term OR RDB$PACKAGE_BODY_SOURCE CONTAINING @term) " +
        "ORDER BY RDB$PACKAGE_NAME";

    internal const string ExceptionMessageSql =
        "SELECT TRIM(RDB$EXCEPTION_NAME), RDB$MESSAGE FROM RDB$EXCEPTIONS " +
        "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$MESSAGE CONTAINING @term " +
        "ORDER BY RDB$EXCEPTION_NAME";

    internal const string TableFieldSql =
        "SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME) FROM RDB$RELATION_FIELDS rf " +
        "JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND r.RDB$VIEW_BLR IS NULL " +
        "AND rf.RDB$FIELD_NAME CONTAINING @term " +
        "ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_NAME";
}
