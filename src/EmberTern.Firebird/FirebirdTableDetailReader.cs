using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

public sealed class FirebirdTableDetailReader
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    public FirebirdTableDetailReader(FirebirdConnectionService connectionService)
        : this(connectionService, null)
    {
    }

    public FirebirdTableDetailReader(FirebirdConnectionService connectionService, TransactionService? transactionService)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    // Readers never open their own transaction. When the user has a working tx
    // active we attach to it; otherwise the managed driver runs the SELECT in
    // an implicit read tx (auto-committed per command). Either way we don't
    // touch the user's tx state and we can't race against other readers.

    public async Task<IReadOnlyList<FieldInfo>> GetFieldsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<FieldInfo>();

        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = FieldsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            cmd.Parameters.AddWithValue("@tableName", tableName);

            var results = new List<FieldInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var position = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                var fieldType = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var fieldLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var fieldScale = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var fieldPrecision = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var nullFlag = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                var defaultSource = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
                var computedSource = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();
                var description = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();
                var subType = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10);
                var pkCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11);
                var fkCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12);
                var fieldSource = reader.IsDBNull(13) ? null : reader.GetString(13).Trim();
                var charsetName = reader.IsDBNull(14) ? null : reader.GetString(14).Trim();
                var unqCount = reader.IsDBNull(15) ? 0 : reader.GetInt32(15);
                var fkTable = reader.IsDBNull(16) ? null : reader.GetString(16).Trim();

                results.Add(new FieldInfo
                {
                    Position = position,
                    Name = name,
                    Type = FormatFieldType(fieldType, fieldLength, fieldScale, fieldPrecision, subType),
                    Size = fieldLength,
                    Scale = fieldScale is null ? null : Math.Abs(fieldScale.Value),
                    NotNull = nullFlag == 1,
                    DefaultValue = StripDefaultPrefix(defaultSource),
                    ComputedSource = string.IsNullOrEmpty(computedSource) ? null : computedSource,
                    Description = string.IsNullOrEmpty(description) ? null : description,
                    IsPrimaryKey = pkCount > 0,
                    IsForeignKey = fkCount > 0,
                    IsUnique = unqCount > 0,
                    Domain = NormalizeDomain(fieldSource),
                    Charset = string.IsNullOrEmpty(charsetName) ? null : charsetName,
                    ForeignKeyTable = string.IsNullOrEmpty(fkTable) ? null : fkTable,
                });
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read columns for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<IndexInfo>();

        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = IndexesSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            cmd.Parameters.AddWithValue("@tableName", tableName);

            var results = new List<IndexInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
                var uniqueFlag = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
                var indexDirection = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var fields = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
                var constraintType = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
                var inactiveFlag = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var statistics = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6);
                var expression = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();

                results.Add(new IndexInfo
                {
                    Name = name,
                    Fields = fields,
                    IsUnique = uniqueFlag == 1,
                    IsDescending = indexDirection == 1,
                    IsActive = inactiveFlag != 1,
                    Statistics = statistics,
                    Expression = string.IsNullOrEmpty(expression) ? null : expression,
                    IndexType = NormalizeIndexType(constraintType),
                });
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read indexes for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    public async Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<ConstraintInfo>();

        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = ConstraintsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            cmd.Parameters.AddWithValue("@tableName", tableName);

            var results = new List<ConstraintInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(BuildConstraintInfo(
                    name: reader.IsDBNull(0) ? null : reader.GetString(0),
                    rawKind: reader.IsDBNull(1) ? null : reader.GetString(1),
                    fields: reader.IsDBNull(2) ? null : reader.GetString(2),
                    refTable: reader.IsDBNull(4) ? null : reader.GetString(4),
                    refFields: reader.IsDBNull(5) ? null : reader.GetString(5),
                    checkSource: reader.IsDBNull(6) ? null : reader.GetString(6),
                    indexName: reader.IsDBNull(7) ? null : reader.GetString(7),
                    updateRule: reader.IsDBNull(8) ? null : reader.GetString(8),
                    deleteRule: reader.IsDBNull(9) ? null : reader.GetString(9),
                    indexDirection: reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10)));
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read constraints for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    public async Task<string> GetDescriptionAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return string.Empty;

        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT RDB$DESCRIPTION FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @tableName";
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            cmd.Parameters.AddWithValue("@tableName", tableName);

            string? description = null;
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    && !reader.IsDBNull(0))
                {
                    description = reader.GetString(0);
                }
            }
            return NormalizeDescription(description);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read description for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    /// <summary>
    /// Returns up to <paramref name="limit"/> rows from the table — preview only,
    /// not the user's "Execute query" path. Attaches to the user's working tx
    /// when active; otherwise the driver runs the SELECT in an implicit read tx.
    /// </summary>
    public async Task<QueryResult> GetDataPreviewAsync(
        string tableName,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName))
        {
            return new QueryResult();
        }

        var connection = _connectionService.RequireOpenConnection();
        var sw = Stopwatch.StartNew();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            // Quote the identifier so case-sensitive / reserved-word table names
            // still work. Internal quotes get doubled per SQL convention.
            cmd.CommandText = $"SELECT FIRST {limit} * FROM \"{tableName.Replace("\"", "\"\"")}\"";
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;

            var columns = new List<QueryColumn>();
            var rows = new List<object?[]>();
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(new QueryColumn(reader.GetName(i), reader.GetFieldType(i) ?? typeof(object)));
                }

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var row = new object?[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    rows.Add(row);
                }
            }
            sw.Stop();
            return new QueryResult
            {
                Columns = columns,
                Rows = rows,
                Elapsed = sw.Elapsed,
                Truncated = rows.Count >= limit,
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not preview data for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    // Internal so tests can verify mapping from raw catalog strings without a live FB.
    internal static ConstraintInfo BuildConstraintInfo(
        string? name,
        string? rawKind,
        string? fields,
        string? refTable,
        string? refFields,
        string? checkSource,
        string? indexName = null,
        string? updateRule = null,
        string? deleteRule = null,
        int? indexDirection = null)
        => new()
        {
            Name = name?.Trim() ?? string.Empty,
            ConstraintType = rawKind?.Trim() ?? string.Empty,
            Fields = fields?.Trim() ?? string.Empty,
            RefTable = refTable?.Trim() ?? string.Empty,
            RefFields = refFields?.Trim() ?? string.Empty,
            CheckClause = NormalizeCheckSource(checkSource),
            IndexName = indexName?.Trim() ?? string.Empty,
            UpdateRule = updateRule?.Trim() ?? string.Empty,
            DeleteRule = deleteRule?.Trim() ?? string.Empty,
            IsDescending = indexDirection == 1,
        };

    // RDB$TRIGGER_SOURCE (check-constraint body) is wrapped as "CHECK (...)" in the
    // catalog. We trim outer whitespace; the wrapping is preserved so users see the
    // exact form Firebird stores.
    internal static string NormalizeCheckSource(string? raw)
        => string.IsNullOrEmpty(raw) ? string.Empty : raw.Trim();

    internal static string NormalizeDescription(string? raw)
        => string.IsNullOrEmpty(raw) ? string.Empty : raw.Trim();

    // RDB$FIELD_SOURCE is the domain name. Anonymous backing domains created by
    // Firebird for inline column definitions (CHAR(10), INTEGER, COMPUTED BY, …)
    // are named "RDB$<n>" — those aren't real user domains and shouldn't show
    // up in the Domena column. Trim + filter; everything else passes through.
    internal static string? NormalizeDomain(string? rawFieldSource)
    {
        if (string.IsNullOrEmpty(rawFieldSource)) return null;
        var trimmed = rawFieldSource.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.StartsWith("RDB$", StringComparison.Ordinal)) return null;
        return trimmed;
    }

    internal const string ConstraintsSql =
        "SELECT rc.RDB$CONSTRAINT_NAME, rc.RDB$CONSTRAINT_TYPE, " +
        "       (SELECT LIST(TRIM(s.RDB$FIELD_NAME), ', ') " +
        "        FROM RDB$INDEX_SEGMENTS s " +
        "        WHERE s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME) AS FIELDS, " +
        "       fk.RDB$CONST_NAME_UQ, " +
        "       (SELECT TRIM(rc2.RDB$RELATION_NAME) " +
        "        FROM RDB$RELATION_CONSTRAINTS rc2 " +
        "        WHERE rc2.RDB$CONSTRAINT_NAME = fk.RDB$CONST_NAME_UQ) AS REF_TABLE, " +
        "       (SELECT LIST(TRIM(s2.RDB$FIELD_NAME), ', ') " +
        "        FROM RDB$INDEX_SEGMENTS s2 " +
        "        JOIN RDB$RELATION_CONSTRAINTS rc3 ON rc3.RDB$INDEX_NAME = s2.RDB$INDEX_NAME " +
        "        WHERE rc3.RDB$CONSTRAINT_NAME = fk.RDB$CONST_NAME_UQ) AS REF_FIELDS, " +
        "       chk_src.RDB$TRIGGER_SOURCE, " +
        "       rc.RDB$INDEX_NAME, " +
        "       fk.RDB$UPDATE_RULE, " +
        "       fk.RDB$DELETE_RULE, " +
        "       idx.RDB$INDEX_TYPE " +
        "FROM RDB$RELATION_CONSTRAINTS rc " +
        "LEFT JOIN RDB$REF_CONSTRAINTS fk ON fk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
        "LEFT JOIN RDB$CHECK_CONSTRAINTS chk ON chk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
        "LEFT JOIN RDB$TRIGGERS chk_src ON chk_src.RDB$TRIGGER_NAME = chk.RDB$TRIGGER_NAME " +
        "                              AND chk_src.RDB$TRIGGER_TYPE = 1 " +
        "LEFT JOIN RDB$INDICES idx ON idx.RDB$INDEX_NAME = rc.RDB$INDEX_NAME " +
        "WHERE rc.RDB$RELATION_NAME = @tableName " +
        "ORDER BY rc.RDB$CONSTRAINT_TYPE, rc.RDB$CONSTRAINT_NAME";

    internal const string FieldsSql =
        "SELECT rf.RDB$FIELD_POSITION, rf.RDB$FIELD_NAME, " +
        "       ft.RDB$FIELD_TYPE, ft.RDB$FIELD_LENGTH, " +
        "       ft.RDB$FIELD_SCALE, ft.RDB$FIELD_PRECISION, " +
        "       rf.RDB$NULL_FLAG, rf.RDB$DEFAULT_SOURCE, " +
        "       ft.RDB$COMPUTED_SOURCE, rf.RDB$DESCRIPTION, " +
        "       ft.RDB$FIELD_SUB_TYPE, " +
        "       (SELECT COUNT(*) FROM RDB$INDEX_SEGMENTS s " +
        "          JOIN RDB$RELATION_CONSTRAINTS rc ON rc.RDB$INDEX_NAME = s.RDB$INDEX_NAME " +
        "          WHERE rc.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "            AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' " +
        "            AND s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME) AS PK_FLAG, " +
        "       (SELECT COUNT(*) FROM RDB$INDEX_SEGMENTS s " +
        "          JOIN RDB$RELATION_CONSTRAINTS rc ON rc.RDB$INDEX_NAME = s.RDB$INDEX_NAME " +
        "          WHERE rc.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "            AND rc.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY' " +
        "            AND s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME) AS FK_FLAG, " +
        "       rf.RDB$FIELD_SOURCE, " +
        "       cs.RDB$CHARACTER_SET_NAME, " +
        "       (SELECT COUNT(*) FROM RDB$INDEX_SEGMENTS s " +
        "          JOIN RDB$RELATION_CONSTRAINTS rc ON rc.RDB$INDEX_NAME = s.RDB$INDEX_NAME " +
        "          WHERE rc.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "            AND rc.RDB$CONSTRAINT_TYPE = 'UNIQUE' " +
        "            AND s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME) AS UNQ_FLAG, " +
        "       (SELECT TRIM(rc_uq.RDB$RELATION_NAME) " +
        "          FROM RDB$RELATION_CONSTRAINTS rc_fk " +
        "          JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = rc_fk.RDB$CONSTRAINT_NAME " +
        "          JOIN RDB$RELATION_CONSTRAINTS rc_uq ON rc_uq.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ " +
        "          JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc_fk.RDB$INDEX_NAME " +
        "          WHERE rc_fk.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "            AND rc_fk.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY' " +
        "            AND s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME " +
        "          ROWS 1) AS FK_TABLE " +
        "FROM RDB$RELATION_FIELDS rf " +
        "JOIN RDB$FIELDS ft ON ft.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
        "LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = ft.RDB$CHARACTER_SET_ID " +
        "WHERE rf.RDB$RELATION_NAME = @tableName " +
        "ORDER BY rf.RDB$FIELD_POSITION";

    internal const string IndexesSql =
        "SELECT i.RDB$INDEX_NAME, i.RDB$UNIQUE_FLAG, i.RDB$INDEX_TYPE, " +
        "       (SELECT LIST(TRIM(s2.RDB$FIELD_NAME), ',') " +
        "        FROM RDB$INDEX_SEGMENTS s2 " +
        "        WHERE s2.RDB$INDEX_NAME = i.RDB$INDEX_NAME) AS FIELDS, " +
        "       (SELECT rc.RDB$CONSTRAINT_TYPE " +
        "        FROM RDB$RELATION_CONSTRAINTS rc " +
        "        WHERE rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME " +
        "          AND rc.RDB$CONSTRAINT_TYPE IN ('PRIMARY KEY', 'FOREIGN KEY') " +
        "        ROWS 1) AS CONSTRAINT_TYPE, " +
        "       i.RDB$INDEX_INACTIVE, " +
        "       i.RDB$STATISTICS, " +
        "       i.RDB$EXPRESSION_SOURCE " +
        "FROM RDB$INDICES i " +
        "WHERE i.RDB$RELATION_NAME = @tableName " +
        "ORDER BY i.RDB$INDEX_NAME";

    // Constraint subquery is narrowed to PRIMARY KEY / FOREIGN KEY (UNIQUE
    // constraint backing indexes are surfaced through IsUnique). Anything
    // outside that set comes back null → empty IndexType.
    internal static string NormalizeIndexType(string? constraintType)
    {
        if (string.IsNullOrWhiteSpace(constraintType)) return string.Empty;
        var trimmed = constraintType.Trim();
        if (string.Equals(trimmed, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase)) return "PRIMARY KEY";
        if (string.Equals(trimmed, "FOREIGN KEY", StringComparison.OrdinalIgnoreCase)) return "FOREIGN KEY";
        return string.Empty;
    }

    // Internal so tests can verify the integer → string mapping without a live DB.
    internal static string FormatFieldType(int fieldType, int? length, int? scale, int? precision, int? subType)
    {
        // NUMERIC/DECIMAL is encoded as an integer type (SMALLINT/INTEGER/BIGINT) with
        // a non-zero RDB$FIELD_SUB_TYPE and a negative scale. The "(p,s)" form is what
        // SQL clients want to see.
        if ((fieldType == 7 || fieldType == 8 || fieldType == 16) && scale is { } s && s < 0)
        {
            var p = precision ?? 0;
            return string.Format(
                CultureInfo.InvariantCulture,
                subType == 2 ? "DECIMAL({0},{1})" : "NUMERIC({0},{1})",
                p,
                Math.Abs(s));
        }

        return fieldType switch
        {
            7 => "SMALLINT",
            8 => "INTEGER",
            9 => "QUAD",
            10 => "FLOAT",
            11 => "D_FLOAT",
            12 => "DATE",
            13 => "TIME",
            14 => length is { } l ? string.Format(CultureInfo.InvariantCulture, "CHAR({0})", l) : "CHAR",
            16 => "BIGINT",
            23 => "BOOLEAN",
            24 => "DECFLOAT(16)",
            25 => "DECFLOAT(34)",
            27 => "DOUBLE PRECISION",
            28 => "TIME WITH TIME ZONE",
            29 => "TIMESTAMP WITH TIME ZONE",
            35 => "TIMESTAMP",
            37 => length is { } l ? string.Format(CultureInfo.InvariantCulture, "VARCHAR({0})", l) : "VARCHAR",
            40 => length is { } l ? string.Format(CultureInfo.InvariantCulture, "CSTRING({0})", l) : "CSTRING",
            45 => "BLOB_ID",
            261 => "BLOB",
            _ => string.Format(CultureInfo.InvariantCulture, "TYPE_{0}", fieldType),
        };
    }

    internal static bool IsPrimaryConstraint(string? constraintType)
        => string.Equals(constraintType, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase);

    // RDB$DEFAULT_SOURCE includes the leading "DEFAULT " keyword (sometimes "default ").
    // Strip it so the grid shows just the value the user wrote.
    internal static string? StripDefaultPrefix(string? defaultSource)
    {
        if (string.IsNullOrEmpty(defaultSource)) return null;
        const string prefix = "DEFAULT ";
        if (defaultSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return defaultSource.Substring(prefix.Length).Trim();
        }
        return defaultSource;
    }
}
