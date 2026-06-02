using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

public sealed class FirebirdMetadataReader
{
    private readonly FirebirdConnectionService _connectionService;

    public FirebirdMetadataReader(FirebirdConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task<IReadOnlyList<MetadataObject>> ListAsync(
        MetadataObjectKind kind,
        CancellationToken cancellationToken = default)
    {
        var sql = SqlFor(kind);
        var connection = _connectionService.RequireOpenConnection();

        // A short-lived, read-only tx so we never interfere with the user's
        // working transaction (different statement counter, different lifetime).
        var tx = (FbTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = tx;

            var results = new List<MetadataObject>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var name = reader.GetString(0).Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                // System tables are user-requested here; skip the IsSystemName filter so
                // RDB$/MON$/SEC$ rows are surfaced. All other kinds keep the safety net.
                if (!BypassSystemNameFilter(kind) && IsSystemName(name))
                {
                    continue;
                }
                results.Add(new MetadataObject(name, kind));
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return results;
        }
        catch (FbException ex)
        {
            try { await tx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }
            throw new MetadataReadException($"Could not read {kind.ToString().ToLowerInvariant()}s: {ex.Message}", ex);
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Internal so tests can verify the bypass list (SystemTable is the only kind that
    // *wants* RDB$/MON$/SEC$ names — that's the whole point of the view).
    internal static bool BypassSystemNameFilter(MetadataObjectKind kind)
        => kind == MetadataObjectKind.SystemTable;

    /// <summary>
    /// Returns the columns of a table or view (ordered by RDB$FIELD_POSITION),
    /// each with name + formatted SQL type, for SQL editor autocomplete after
    /// <c>ALIAS.</c>. Short-lived ReadCommitted transaction — independent from
    /// the user's working tx.
    /// </summary>
    public async Task<IReadOnlyList<ColumnSpec>> ListColumnsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<ColumnSpec>();

        var connection = _connectionService.RequireOpenConnection();
        var tx = (FbTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = ColumnsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = tx;
            cmd.Parameters.AddWithValue("@name", tableName);

            var columns = new List<ColumnSpec>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) continue;
                var name = reader.GetString(0).Trim();
                if (name.Length == 0) continue;
                var fieldType = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var fieldLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var fieldScale = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var fieldPrecision = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var subType = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var type = FirebirdTableDetailReader.FormatFieldType(fieldType, fieldLength, fieldScale, fieldPrecision, subType);
                columns.Add(new ColumnSpec(name, type));
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return columns;
        }
        catch (FbException ex)
        {
            try { await tx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }
            throw new MetadataReadException($"Could not read columns for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Joins RDB$RELATION_FIELDS to RDB$FIELDS so the autocomplete dropdown can
    // render "COLUMN : TYPE". Same mapping logic as the TableDetail Fields tab.
    internal const string ColumnsSql =
        "SELECT TRIM(rf.RDB$FIELD_NAME), " +
        "       ft.RDB$FIELD_TYPE, ft.RDB$FIELD_LENGTH, " +
        "       ft.RDB$FIELD_SCALE, ft.RDB$FIELD_PRECISION, " +
        "       ft.RDB$FIELD_SUB_TYPE " +
        "FROM RDB$RELATION_FIELDS rf " +
        "JOIN RDB$FIELDS ft ON ft.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
        "WHERE rf.RDB$RELATION_NAME = @name " +
        "ORDER BY rf.RDB$FIELD_POSITION";

    // Internal so tests can verify system-name filtering without a live connection.
    internal static bool IsSystemName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }
        return name.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("MON$", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SEC$", StringComparison.OrdinalIgnoreCase);
    }

    // Internal so tests can assert the SQL shape and FB-compat (no LIST(), no FB3-only syntax).
    internal static string SqlFor(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table =>
            "SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NULL " +
            "ORDER BY RDB$RELATION_NAME",
        MetadataObjectKind.View =>
            "SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NOT NULL " +
            "ORDER BY RDB$RELATION_NAME",
        MetadataObjectKind.Procedure =>
            "SELECT TRIM(RDB$PROCEDURE_NAME) FROM RDB$PROCEDURES " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$PROCEDURE_NAME",
        MetadataObjectKind.Trigger =>
            "SELECT TRIM(RDB$TRIGGER_NAME) FROM RDB$TRIGGERS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$TRIGGER_NAME",
        MetadataObjectKind.Function =>
            "SELECT TRIM(RDB$FUNCTION_NAME) FROM RDB$FUNCTIONS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$FUNCTION_NAME",
        MetadataObjectKind.Generator =>
            "SELECT TRIM(RDB$GENERATOR_NAME) FROM RDB$GENERATORS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$GENERATOR_NAME",
        MetadataObjectKind.Domain =>
            // RDB$FIELDS holds both user domains AND anonymous domains backing table columns.
            // The RDB$ prefix filter (applied client-side) strips the anonymous ones.
            "SELECT TRIM(RDB$FIELD_NAME) FROM RDB$FIELDS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$FIELD_NAME",
        MetadataObjectKind.Package =>
            // RDB$PACKAGES only exists on FB 3+; the query will error (and surface via
            // MetadataReadException) on FB 2.5 — acceptable, the category is empty there.
            "SELECT TRIM(RDB$PACKAGE_NAME) FROM RDB$PACKAGES " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$PACKAGE_NAME",
        MetadataObjectKind.Exception =>
            "SELECT TRIM(RDB$EXCEPTION_NAME) FROM RDB$EXCEPTIONS " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$EXCEPTION_NAME",
        MetadataObjectKind.Role =>
            "SELECT TRIM(RDB$ROLE_NAME) FROM RDB$ROLES " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$ROLE_NAME",
        MetadataObjectKind.User =>
            // SEC$USERS (FB 3+) requires admin/own privileges to read; on FB 2.5 there's
            // no catalog table at all. Both failures surface via MetadataReadException.
            "SELECT TRIM(SEC$USER_NAME) FROM SEC$USERS " +
            "ORDER BY SEC$USER_NAME",
        MetadataObjectKind.Index =>
            "SELECT TRIM(RDB$INDEX_NAME) FROM RDB$INDICES " +
            "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "ORDER BY RDB$INDEX_NAME",
        MetadataObjectKind.SystemTable =>
            // Inverse of the Table query: only system-owned relations, still tables (not views).
            // IsSystemName is bypassed (see BypassSystemNameFilter) so the RDB$ rows survive.
            "SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS " +
            "WHERE RDB$SYSTEM_FLAG = 1 AND RDB$VIEW_BLR IS NULL " +
            "ORDER BY RDB$RELATION_NAME",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

public sealed class MetadataReadException : Exception
{
    public MetadataReadException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
