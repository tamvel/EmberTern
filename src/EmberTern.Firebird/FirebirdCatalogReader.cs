using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Performance;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>Reads the catalog slice the performance advisor needs — per table: its indexes
/// (ordered segment columns, unique/primary, active, selectivity, expression, FB5 partial
/// condition) and an index-statistics-derived row-count estimate. Metadata lane, capture the
/// command lock once (gotcha #98/#120); attaches to the metadata working tx when one is active,
/// otherwise the driver runs each SELECT in an implicit read tx — never touches the user's tx.
/// Returns Core DTOs only; holds all FbCommand internally. No table scan: cardinality is
/// estimated from unique-index selectivity, so it is cheap enough for interactive profiling.</summary>
public sealed class FirebirdCatalogReader
{
    // Index headers for one table. The condition column (FB5 partial index) is version-gated
    // (see BuildIndexHeaderSql) because RDB$CONDITION_SOURCE doesn't exist before FB5.
    internal static string BuildIndexHeaderSql(bool includeCondition) =>
        "SELECT i.RDB$INDEX_NAME, i.RDB$UNIQUE_FLAG, i.RDB$INDEX_INACTIVE, i.RDB$STATISTICS, " +
        "       i.RDB$EXPRESSION_SOURCE, " +
        (includeCondition ? "i.RDB$CONDITION_SOURCE" : "CAST(NULL AS BLOB SUB_TYPE 1)") + " AS CONDITION_SOURCE, " +
        "       (SELECT rc.RDB$CONSTRAINT_TYPE FROM RDB$RELATION_CONSTRAINTS rc " +
        "        WHERE rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME " +
        "          AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' ROWS 1) AS PK " +
        "FROM RDB$INDICES i " +
        "WHERE i.RDB$RELATION_NAME = @tableName " +
        "ORDER BY i.RDB$INDEX_NAME";

    // Segment columns for one table's indexes, ordered by position so the FIRST column of each
    // index is its leading segment (the one that decides predicate coverage).
    internal const string SegmentsSql =
        "SELECT s.RDB$INDEX_NAME, TRIM(s.RDB$FIELD_NAME) " +
        "FROM RDB$INDEX_SEGMENTS s " +
        "JOIN RDB$INDICES i ON i.RDB$INDEX_NAME = s.RDB$INDEX_NAME " +
        "WHERE i.RDB$RELATION_NAME = @tableName " +
        "ORDER BY s.RDB$INDEX_NAME, s.RDB$FIELD_POSITION";

    private readonly FirebirdConnectionService _connectionService;
    private readonly MetadataLane _metadataLane;

    public FirebirdCatalogReader(FirebirdConnectionService connectionService)
        : this(connectionService, new MetadataLane(connectionService))
    {
    }

    public FirebirdCatalogReader(
        FirebirdConnectionService connectionService,
        MetadataLane metadataLane)
    {
        _connectionService = connectionService;
        _metadataLane = metadataLane;
    }

    private FbConnection MetaConnection()
        => _metadataLane.RequireOpenConnection();
    private SemaphoreSlim MetaLock()
        => _metadataLane.CommandLock;
    private FbTransaction? MetaTx => _metadataLane.TransactionForCommand;

    /// <summary>Captures the catalog for the given tables (typically the ones the profiled
    /// statement touched). Unknown/empty input yields <see cref="CatalogModel.Empty"/>.</summary>
    public async Task<CatalogModel> CaptureAsync(
        IReadOnlyCollection<string> tables,
        CancellationToken cancellationToken = default)
    {
        if (tables is null || tables.Count == 0)
        {
            return CatalogModel.Empty;
        }

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — gotcha #98/#120
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // RDB$CONDITION_SOURCE is FB5+ — gate it so FB3/4 don't fail "column not found".
            bool includeCondition = FirebirdDdlReader.ParseServerMajor(connection.ServerVersion) >= 5;
            var headerSql = BuildIndexHeaderSql(includeCondition);

            var infos = new List<TableCatalogInfo>();
            foreach (var table in tables)
            {
                if (string.IsNullOrWhiteSpace(table))
                {
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var segments = await ReadSegmentsAsync(connection, table, cancellationToken).ConfigureAwait(false);
                var indexes = await ReadIndexesAsync(connection, headerSql, table, segments, cancellationToken).ConfigureAwait(false);
                infos.Add(new TableCatalogInfo
                {
                    Table = table,
                    Indexes = indexes,
                    RowCountEstimate = EstimateCardinality(indexes),
                });
            }
            return new CatalogModel { Tables = infos };
        }
        catch (FbException ex)
        {
            throw new PerformanceCaptureException(ex.Message?.Trim() ?? "Failed to read catalog statistics.", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private async Task<Dictionary<string, List<string>>> ReadSegmentsAsync(
        FbConnection connection, string table, CancellationToken cancellationToken)
    {
        var byIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = connection.CreateGuardedCommand(SegmentsSql);
        cmd.CommandTimeout = 0;
        cmd.Transaction = MetaTx;
        cmd.AddGuardedParameter("@tableName", table);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }
            var indexName = reader.GetString(0).Trim();
            var column = reader.GetString(1).Trim();
            if (!byIndex.TryGetValue(indexName, out var list))
            {
                list = new List<string>();
                byIndex[indexName] = list;
            }
            list.Add(column);
        }
        return byIndex;
    }

    private async Task<List<IndexModel>> ReadIndexesAsync(
        FbConnection connection, string headerSql, string table,
        Dictionary<string, List<string>> segments, CancellationToken cancellationToken)
    {
        var indexes = new List<IndexModel>();
        await using var cmd = connection.CreateGuardedCommand(headerSql);
        cmd.CommandTimeout = 0;
        cmd.Transaction = MetaTx;
        cmd.AddGuardedParameter("@tableName", table);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
            if (name.Length == 0)
            {
                continue;
            }
            var uniqueFlag = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
            var inactiveFlag = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
            var statistics = FirebirdTableDetailReader.NormalizeStatistics(reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3));
            var expression = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
            var condition = reader.IsDBNull(5) ? null : reader.GetString(5).Trim();
            var pk = reader.IsDBNull(6) ? null : reader.GetString(6);

            indexes.Add(new IndexModel
            {
                Name = name,
                Columns = segments.TryGetValue(name, out var cols) ? cols : new List<string>(),
                IsUnique = uniqueFlag == 1,
                IsPrimary = !string.IsNullOrWhiteSpace(pk),
                IsInactive = inactiveFlag == 1,
                Selectivity = statistics,
                Expression = string.IsNullOrEmpty(expression) ? null : expression,
                Condition = string.IsNullOrEmpty(condition) ? null : condition,
            });
        }
        return indexes;
    }

    /// <summary>Estimates table cardinality from unique-index selectivity: a unique key's
    /// selectivity is 1/N, so N ≈ 1 ÷ selectivity. Uses the smallest positive selectivity among
    /// usable unique indexes (largest implied N). Null when no unique index has computed stats.</summary>
    internal static long? EstimateCardinality(IReadOnlyList<IndexModel> indexes)
    {
        double? best = null;
        foreach (var idx in indexes)
        {
            if (!idx.IsUnique || idx.IsInactive)
            {
                continue;
            }
            if (idx.Selectivity is { } s && s > 0 && (best is null || s < best))
            {
                best = s;
            }
        }
        if (best is not { } sel || sel <= 0)
        {
            return null;
        }
        return (long)Math.Round(1.0 / sel);
    }
}
