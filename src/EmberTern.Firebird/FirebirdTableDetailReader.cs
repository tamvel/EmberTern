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
    // Two lanes (C2): structure reads (fields / indexes / constraints / description /
    // dependencies / DDL refresh) run on the METADATA lane; the Dane data preview +
    // row count run on the DATA lane. A single reader instance serves both — each
    // method picks the lane.
    private readonly TransactionService? _metadataTransactionService;
    private readonly TransactionService? _dataTransactionService;

    public FirebirdTableDetailReader(FirebirdConnectionService connectionService)
        : this(connectionService, null, null)
    {
    }

    // Back-compat: a single transaction service drives both lanes (tests / legacy).
    public FirebirdTableDetailReader(FirebirdConnectionService connectionService, TransactionService? transactionService)
        : this(connectionService, transactionService, transactionService)
    {
    }

    public FirebirdTableDetailReader(
        FirebirdConnectionService connectionService,
        TransactionService? metadataTransactionService,
        TransactionService? dataTransactionService)
    {
        _connectionService = connectionService;
        _metadataTransactionService = metadataTransactionService;
        _dataTransactionService = dataTransactionService;
    }

    // Readers never open their own transaction. When a working tx is active on the lane
    // we attach to it; otherwise the managed driver runs the SELECT in an implicit read
    // tx (auto-committed per command). Either way we don't touch the user's tx state.
    private FbConnection MetaConnection()
        => _metadataTransactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
    private SemaphoreSlim MetaLock()
        => _metadataTransactionService?.CommandLock ?? _connectionService.CommandLock;
    private FbTransaction? MetaTx => _metadataTransactionService?.ActiveTransaction;

    private FbConnection DataConnection()
        => _dataTransactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
    private SemaphoreSlim DataLock()
        => _dataTransactionService?.CommandLock ?? _connectionService.CommandLock;
    private FbTransaction? DataTx => _dataTransactionService?.ActiveTransaction;

    public async Task<IReadOnlyList<FieldInfo>> GetFieldsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<FieldInfo>();

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = FieldsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
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
                var aiFlag = reader.IsDBNull(17) ? 0 : reader.GetInt32(17);

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
                    IsAutoIncrement = aiFlag > 0,
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
            commandLock.Release();
        }
    }

    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<IndexInfo>();

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = IndexesSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
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
                var statistics = NormalizeStatistics(reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6));
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
            commandLock.Release();
        }
    }

    public async Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<ConstraintInfo>();

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = ConstraintsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
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
            commandLock.Release();
        }
    }

    public async Task<string> GetDescriptionAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return string.Empty;

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT RDB$DESCRIPTION FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @tableName";
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
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
            commandLock.Release();
        }
    }

    // ─── Stored-procedure metadata (Procedure Detail) ──────────────────────
    //
    // A procedure lives in RDB$PROCEDURES (not RDB$RELATIONS) and its dependency
    // rows carry RDB$*_TYPE = 5 (Procedure), so the table/view description +
    // dependency queries above DON'T apply — these are procedure-scoped. Same
    // metadata-lane access pattern (MetaConnection/MetaLock/MetaTx) as the rest.

    public async Task<string> GetProcedureDescriptionAsync(
        string procedureName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(procedureName)) return string.Empty;

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT RDB$DESCRIPTION FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @name";
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            cmd.Parameters.AddWithValue("@name", procedureName);

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
            throw new MetadataReadException($"Could not read description for procedure {procedureName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Reads a procedure's parameters of one direction.
    /// <paramref name="paramType"/> is the RDB$PARAMETER_TYPE value: 0 = input,
    /// 1 = output. Position in the returned list is the 1-based display index
    /// within that direction.</summary>
    public async Task<IReadOnlyList<ProcedureParameterInfo>> GetProcedureParametersAsync(
        string procedureName,
        int paramType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(procedureName)) return Array.Empty<ProcedureParameterInfo>();

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = ProcedureParametersSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            cmd.Parameters.AddWithValue("@name", procedureName);
            cmd.Parameters.AddWithValue("@pt", (short)paramType);

            var results = new List<ProcedureParameterInfo>();
            int position = 0;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
                var fieldType = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var subType = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var fieldLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var fieldPrecision = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var fieldScale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var nullFlag = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                var defaultSource = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
                var description = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();

                results.Add(new ProcedureParameterInfo
                {
                    Position = ++position,
                    Name = name,
                    Type = FormatFieldType(fieldType, fieldLength, fieldScale, fieldPrecision, subType),
                    NotNull = nullFlag == 1,
                    DefaultValue = StripDefaultPrefix(defaultSource),
                    Description = string.IsNullOrEmpty(description) ? null : description,
                });
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read parameters for procedure {procedureName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    public async Task<(IReadOnlyList<DependencyInfo> DependsOn, IReadOnlyList<DependencyInfo> DependedOnBy)> GetProcedureDependenciesAsync(
        string procedureName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(procedureName))
        {
            return (Array.Empty<DependencyInfo>(), Array.Empty<DependencyInfo>());
        }

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dependsOn = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = ProcedureDependsOnSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", procedureName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependsOn.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)),
                    });
                }
            }

            var dependedOnBy = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = ProcedureDependedOnBySql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", procedureName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependedOnBy.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                    });
                }
            }

            return (dependsOn, dependedOnBy);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read dependencies for procedure {procedureName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // Reuses the structured field columns + FormatFieldType so the parameter type
    // matches the Pola-grid convention. ORDER BY the catalog parameter number;
    // the 1-based display Position is assigned client-side per direction.
    internal const string ProcedureParametersSql =
        "SELECT TRIM(pp.RDB$PARAMETER_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
        "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, " +
        "       COALESCE(pp.RDB$NULL_FLAG, f.RDB$NULL_FLAG), " +
        "       pp.RDB$DEFAULT_SOURCE, pp.RDB$DESCRIPTION " +
        "FROM RDB$PROCEDURE_PARAMETERS pp " +
        "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE " +
        "WHERE pp.RDB$PROCEDURE_NAME = @name AND pp.RDB$PARAMETER_TYPE = @pt " +
        "ORDER BY pp.RDB$PARAMETER_NUMBER";

    // Procedure dependencies use RDB$*_TYPE = 5 (Procedure). "Depends on" = what
    // this procedure references (it is the DEPENDENT); "depended on by" = what
    // references this procedure (it is the DEPENDED_ON). One @name reference per
    // query, so no distinct-name binding is needed (cf. the table dependency
    // UNION queries — see DependsOnSql comment, gotcha #47).
    internal const string ProcedureDependsOnSql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDED_ON_NAME), TRIM(d.RDB$FIELD_NAME), " +
        "    CAST(d.RDB$DEPENDED_ON_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDENT_NAME) = @name AND d.RDB$DEPENDENT_TYPE = 5 " +
        "ORDER BY 3, 1";

    internal const string ProcedureDependedOnBySql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), " +
        "    CAST(d.RDB$DEPENDENT_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDED_ON_NAME) = @name AND d.RDB$DEPENDED_ON_TYPE = 5 " +
        "ORDER BY 2, 1";

    // ─── Function metadata (Function Detail) ────────────────────────────────
    //
    // A PSQL function lives in RDB$FUNCTIONS (standalone: RDB$PACKAGE_NAME IS NULL); its
    // arguments are in RDB$FUNCTION_ARGUMENTS with the return value at RDB$RETURN_ARGUMENT,
    // and its dependency rows carry RDB$*_TYPE = 15 (Function). Same metadata-lane access
    // pattern (MetaConnection/MetaLock/MetaTx) as the procedure readers.

    public async Task<string> GetFunctionDescriptionAsync(
        string functionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(functionName)) return string.Empty;

        var connection = MetaConnection();
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT RDB$DESCRIPTION FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name";
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            cmd.Parameters.AddWithValue("@name", functionName);

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
            throw new MetadataReadException($"Could not read description for function {functionName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Reads a function's catalog signature — the input arguments (the return
    /// value split off via RDB$RETURN_ARGUMENT), the formatted return type, and the
    /// DETERMINISTIC flag. Two queries under one lock (the args + the RDB$FUNCTIONS row).</summary>
    public async Task<FunctionSignatureInfo> GetFunctionSignatureAsync(
        string functionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(functionName)) return new FunctionSignatureInfo();

        var connection = MetaConnection();
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int returnArgPos = 0;
            bool deterministic = false;
            await using (var infoCmd = connection.CreateCommand())
            {
                infoCmd.CommandText = FunctionInfoSql;
                infoCmd.CommandTimeout = 0;
                infoCmd.Transaction = MetaTx;
                infoCmd.Parameters.AddWithValue("@name", functionName);
                await using var r = await infoCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    returnArgPos = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    deterministic = !r.IsDBNull(1) && r.GetInt32(1) != 0;
                }
            }

            var arguments = new List<ProcedureParameterInfo>();
            var returnType = string.Empty;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = FunctionArgumentsSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", functionName);
                int displayPosition = 0;
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var argPos = reader.IsDBNull(0) ? -1 : reader.GetInt32(0);
                    var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                    var fieldType = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    var subType = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                    var fieldLength = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                    var fieldPrecision = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                    var fieldScale = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                    var nullFlag = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
                    var defaultSource = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();
                    var description = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();

                    var type = FormatFieldType(fieldType, fieldLength, fieldScale, fieldPrecision, subType);
                    if (argPos == returnArgPos)
                    {
                        returnType = type;
                    }
                    else
                    {
                        arguments.Add(new ProcedureParameterInfo
                        {
                            Position = ++displayPosition,
                            Name = name,
                            Type = type,
                            NotNull = nullFlag == 1,
                            DefaultValue = StripDefaultPrefix(defaultSource),
                            Description = string.IsNullOrEmpty(description) ? null : description,
                        });
                    }
                }
            }

            return new FunctionSignatureInfo
            {
                Arguments = arguments,
                ReturnType = returnType,
                Deterministic = deterministic,
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read signature for function {functionName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    public async Task<(IReadOnlyList<DependencyInfo> DependsOn, IReadOnlyList<DependencyInfo> DependedOnBy)> GetFunctionDependenciesAsync(
        string functionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(functionName))
        {
            return (Array.Empty<DependencyInfo>(), Array.Empty<DependencyInfo>());
        }

        var connection = MetaConnection();
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dependsOn = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = FunctionDependsOnSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", functionName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependsOn.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)),
                    });
                }
            }

            var dependedOnBy = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = FunctionDependedOnBySql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", functionName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependedOnBy.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                    });
                }
            }

            return (dependsOn, dependedOnBy);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read dependencies for function {functionName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    internal const string FunctionInfoSql =
        "SELECT RDB$RETURN_ARGUMENT, RDB$DETERMINISTIC_FLAG " +
        "FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name";

    // Columns: 0=position 1=name 2=type 3=subtype 4=length 5=precision 6=scale
    // 7=null-flag 8=default 9=description. The return value (no name) is the row at
    // RDB$RETURN_ARGUMENT; every other row is an input argument.
    internal const string FunctionArgumentsSql =
        "SELECT fa.RDB$ARGUMENT_POSITION, TRIM(fa.RDB$ARGUMENT_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
        "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, " +
        "       COALESCE(fa.RDB$NULL_FLAG, f.RDB$NULL_FLAG), " +
        "       fa.RDB$DEFAULT_SOURCE, fa.RDB$DESCRIPTION " +
        "FROM RDB$FUNCTION_ARGUMENTS fa " +
        "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = fa.RDB$FIELD_SOURCE " +
        "WHERE fa.RDB$FUNCTION_NAME = @name " +
        "ORDER BY fa.RDB$ARGUMENT_POSITION";

    // Function dependencies use RDB$*_TYPE = 15 (Function). "Depends on" = what this
    // function references (it is the DEPENDENT); "depended on by" = what references this
    // function (it is the DEPENDED_ON). One @name reference per query (cf. gotcha #47).
    internal const string FunctionDependsOnSql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDED_ON_NAME), TRIM(d.RDB$FIELD_NAME), " +
        "    CAST(d.RDB$DEPENDED_ON_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDENT_NAME) = @name AND d.RDB$DEPENDENT_TYPE = 15 " +
        "ORDER BY 3, 1";

    internal const string FunctionDependedOnBySql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), " +
        "    CAST(d.RDB$DEPENDENT_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDED_ON_NAME) = @name AND d.RDB$DEPENDED_ON_TYPE = 15 " +
        "ORDER BY 2, 1";

    // ─── Generators / sequences (Generator Detail) ─────────────────────────
    //
    // A generator lives in RDB$GENERATORS; dependency rows carry RDB$*_TYPE = 14
    // (Generator). The current value is read with GEN_ID(name, 0) (no bump). The
    // initial value + increment live in RDB$GENERATORS (FB3+) — on FB 2.5 those
    // columns don't exist, so a server-version gate defaults them to 0 / 1 rather
    // than issuing a doomed "column not found" query (which could mark an active
    // working tx for rollback). Same metadata-lane access pattern as the rest.

    /// <summary>Reads a generator's current value, initial value, increment, and
    /// description. Current value is best-effort (GEN_ID may be blocked on
    /// system-owned sequences); initial/increment are read from RDB$GENERATORS on
    /// FB3+ and default to 0 / 1 on older servers.</summary>
    public async Task<GeneratorInfo> GetGeneratorInfoAsync(
        string generatorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(generatorName)) return new GeneratorInfo();

        var connection = MetaConnection();
        var serverMajor = FirebirdDdlReader.ParseServerMajor(connection.ServerVersion);
        // Capture the lock ONCE — see gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long initial = 0, increment = 1;
            string description = string.Empty;

            // FB3+ exposes RDB$INITIAL_VALUE / RDB$GENERATOR_INCREMENT; older catalogs
            // only have the description column.
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = serverMajor >= 3
                    ? "SELECT RDB$INITIAL_VALUE, RDB$GENERATOR_INCREMENT, RDB$DESCRIPTION " +
                      "FROM RDB$GENERATORS WHERE RDB$GENERATOR_NAME = @name"
                    : "SELECT RDB$DESCRIPTION FROM RDB$GENERATORS WHERE RDB$GENERATOR_NAME = @name";
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", generatorName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (serverMajor >= 3)
                    {
                        if (!reader.IsDBNull(0)) initial = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                        if (!reader.IsDBNull(1)) increment = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                        if (!reader.IsDBNull(2)) description = reader.GetString(2);
                    }
                    else if (!reader.IsDBNull(0))
                    {
                        description = reader.GetString(0);
                    }
                }
            }

            long current = 0;
            // GEN_ID(name, 0) returns the current value without bumping it. Best-effort:
            // some FB versions/permissions block GEN_ID on system sequences.
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT GEN_ID({DdlGenerator.Quote(generatorName)}, 0) FROM RDB$DATABASE";
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is not null && result != DBNull.Value)
                {
                    current = Convert.ToInt64(result, CultureInfo.InvariantCulture);
                }
            }
            catch (FbException)
            {
                // current value stays 0 — definition is still useful
            }

            return new GeneratorInfo
            {
                Name = generatorName,
                CurrentValue = current,
                InitialValue = initial,
                Increment = increment,
                Description = NormalizeDescription(description),
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read generator {generatorName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Reads ONLY the generator's current value (GEN_ID(name, 0), no bump) —
    /// the lightweight path behind the "Refresh Current Value" button, so a running
    /// ERP's live counter can be re-checked without reloading the whole object. Returns
    /// the raw counter (the VM normalizes a negative pre-first-use sentinel for display).</summary>
    public async Task<long> GetGeneratorCurrentValueAsync(
        string generatorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(generatorName)) return 0;

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — see gotcha #98
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT GEN_ID({DdlGenerator.Quote(generatorName)}, 0) FROM RDB$DATABASE";
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is not null && result != DBNull.Value
                ? Convert.ToInt64(result, CultureInfo.InvariantCulture)
                : 0;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read current value for generator {generatorName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    public async Task<(IReadOnlyList<DependencyInfo> DependsOn, IReadOnlyList<DependencyInfo> DependedOnBy)> GetGeneratorDependenciesAsync(
        string generatorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(generatorName))
        {
            return (Array.Empty<DependencyInfo>(), Array.Empty<DependencyInfo>());
        }

        var connection = MetaConnection();
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dependsOn = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = GeneratorDependsOnSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", generatorName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependsOn.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)),
                    });
                }
            }

            var dependedOnBy = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = GeneratorDependedOnBySql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", generatorName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependedOnBy.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                    });
                }
            }

            return (dependsOn, dependedOnBy);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read dependencies for generator {generatorName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // Generator dependencies use RDB$*_TYPE = 14 (Generator). A generator references
    // nothing, so "depends on" is normally empty; "depended on by" lists triggers /
    // procedures that call GEN_ID / NEXT VALUE FOR on it. One @name per query (gotcha #47).
    internal const string GeneratorDependsOnSql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDED_ON_NAME), TRIM(d.RDB$FIELD_NAME), " +
        "    CAST(d.RDB$DEPENDED_ON_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDENT_NAME) = @name AND d.RDB$DEPENDENT_TYPE = 14 " +
        "ORDER BY 3, 1";

    internal const string GeneratorDependedOnBySql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), " +
        "    CAST(d.RDB$DEPENDENT_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDED_ON_NAME) = @name AND d.RDB$DEPENDED_ON_TYPE = 14 " +
        "ORDER BY 2, 1";

    // ─── Domains (Domain Detail) ────────────────────────────────────────────
    //
    // A domain lives in RDB$FIELDS (the same catalog table that backs every column
    // type). Its full definition — type, length, precision/scale, sub-type, charset,
    // collation, default, CHECK, NOT NULL, description — is read in one row.

    /// <summary>Reads a single user domain → <see cref="DomainInfo"/>. The CHECK,
    /// DEFAULT and DESCRIPTION text blobs are decoded via the driver (GetString),
    /// consistent with the other description/source reads in this reader.</summary>
    public async Task<DomainInfo> GetDomainInfoAsync(
        string domainName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(domainName)) return new DomainInfo();

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — see gotcha #98
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = DomainInfoSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            cmd.Parameters.AddWithValue("@name", domainName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DomainInfo { Name = domainName };
            }

            var fieldType = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            var byteLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
            var charLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var scale = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var precision = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var subType = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
            var notNull = !reader.IsDBNull(7) && reader.GetInt32(7) == 1;
            var defaultSource = reader.IsDBNull(8) ? null : reader.GetString(8);
            var checkSource = reader.IsDBNull(9) ? null : reader.GetString(9);
            var charset = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            var collation = reader.IsDBNull(11) ? null : reader.GetString(11).Trim();
            var description = reader.IsDBNull(12) ? null : reader.GetString(12);

            return BuildDomainInfo(domainName.Trim(), fieldType, charLength, byteLength, scale, precision,
                subType, charset, collation, defaultSource, checkSource, notNull, description);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read domain {domainName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>"Used By" for a domain: table/view columns that declare the domain
    /// (RDB$RELATION_FIELDS — the primary case, NOT recorded in RDB$DEPENDENCIES, cf.
    /// gotcha #46) PLUS any PSQL objects referencing it via RDB$DEPENDENCIES type 9.
    /// Returned as one flat list the VM groups with BuildDependencyTree (single tree).</summary>
    public async Task<IReadOnlyList<DependencyInfo>> GetDomainUsageAsync(
        string domainName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(domainName)) return Array.Empty<DependencyInfo>();

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — see gotcha #98
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var usage = new List<DependencyInfo>();

            // Table / view columns built on the domain.
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = DomainUsageColumnsSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", domainName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    usage.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = (!reader.IsDBNull(2) && reader.GetInt32(2) == 1) ? "View" : "Table",
                    });
                }
            }

            // PSQL objects (procedures/triggers/computed fields/…) referencing the domain.
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = DomainUsageDependenciesSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", domainName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    usage.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                    });
                }
            }

            return usage;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read usage for domain {domainName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    internal const string DomainInfoSql =
        "SELECT TRIM(f.RDB$FIELD_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH, f.RDB$CHARACTER_LENGTH, " +
        "       f.RDB$FIELD_SCALE, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SUB_TYPE, " +
        "       f.RDB$NULL_FLAG, f.RDB$DEFAULT_SOURCE, f.RDB$VALIDATION_SOURCE, " +
        "       cs.RDB$CHARACTER_SET_NAME, co.RDB$COLLATION_NAME, f.RDB$DESCRIPTION " +
        "FROM RDB$FIELDS f " +
        "LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
        "LEFT JOIN RDB$COLLATIONS co ON co.RDB$COLLATION_ID = f.RDB$COLLATION_ID " +
        "    AND co.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
        "WHERE f.RDB$FIELD_NAME = @name";

    // Table + view columns whose type IS this domain (RDB$FIELD_SOURCE = domain).
    // VIEW_BLR distinguishes view (1) from table (0).
    internal const string DomainUsageColumnsSql =
        "SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), " +
        "    CASE WHEN r.RDB$VIEW_BLR IS NULL THEN 0 ELSE 1 END " +
        "FROM RDB$RELATION_FIELDS rf " +
        "JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "WHERE rf.RDB$FIELD_SOURCE = @name " +
        "ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_NAME";

    // PSQL references to the domain (type 9 = field/domain in RDB$DEPENDENCIES).
    internal const string DomainUsageDependenciesSql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), " +
        "    CAST(d.RDB$DEPENDENT_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDED_ON_NAME) = @name AND d.RDB$DEPENDED_ON_TYPE = 9 " +
        "ORDER BY 2, 1";

    // Internal so tests can verify the catalog → DomainInfo mapping without a live DB.
    internal static DomainInfo BuildDomainInfo(
        string name, int fieldType, int? charLength, int? byteLength, int? scale, int? precision,
        int? subType, string? charset, string? collation, string? defaultSource, string? checkSource,
        bool notNull, string? description)
    {
        var formatted = FormatFieldType(fieldType, charLength ?? byteLength, scale, precision, subType);
        var baseType = StripTypeArgs(formatted);

        var isNumeric = (fieldType == 7 || fieldType == 8 || fieldType == 16) && scale is { } s && s < 0;
        var isChar = fieldType is 14 or 37 or 40;
        var isBlob = fieldType == 261;

        return new DomainInfo
        {
            Name = name,
            DataType = baseType,
            Length = isChar ? (charLength ?? byteLength) : null,
            Precision = isNumeric ? precision : null,
            Scale = isNumeric && scale is { } sv ? Math.Abs(sv) : null,
            SubType = isBlob ? subType : null,
            CharacterSet = string.IsNullOrEmpty(charset) ? null : charset,
            Collation = string.IsNullOrEmpty(collation) ? null : collation,
            DefaultValue = StripDefaultPrefix(defaultSource),
            CheckConstraint = string.IsNullOrWhiteSpace(checkSource) ? null : checkSource.Trim(),
            NotNull = notNull,
            Description = NormalizeDescription(description),
        };
    }

    // Strips the "(size[,scale])" / " SUB_TYPE n" suffix off a formatted type to
    // recover the bare type name (e.g. "VARCHAR(80)" → "VARCHAR").
    private static string StripTypeArgs(string formatted)
    {
        var paren = formatted.IndexOf('(');
        if (paren >= 0) return formatted[..paren].Trim();
        var sub = formatted.IndexOf(" SUB_TYPE", StringComparison.OrdinalIgnoreCase);
        return (sub >= 0 ? formatted[..sub] : formatted).Trim();
    }

    // ─── Triggers (Trigger Detail) ──────────────────────────────────────────
    //
    // Trigger metadata is in RDB$TRIGGERS (relation + bit-encoded RDB$TRIGGER_TYPE +
    // sequence + inactive); dependency rows carry RDB$*_TYPE = 2 (Trigger). Same
    // metadata-lane access pattern (MetaConnection/MetaLock/MetaTx) as the rest.

    /// <summary>Reads a relation trigger's structured header (table, timing, events,
    /// position, active) by decoding RDB$TRIGGER_TYPE. A DB-level / DDL trigger
    /// (type ≥ 8192) decodes to an empty event set — out of scope for the editor.</summary>
    public async Task<TriggerHeaderInfo> GetTriggerHeaderAsync(string triggerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(triggerName)) return new TriggerHeaderInfo();

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — see gotcha #98
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT TRIM(RDB$RELATION_NAME), RDB$TRIGGER_TYPE, RDB$TRIGGER_SEQUENCE, RDB$TRIGGER_INACTIVE " +
                "FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @name";
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            cmd.Parameters.AddWithValue("@name", triggerName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new TriggerHeaderInfo();
            }
            var table = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
            // RDB$TRIGGER_TYPE is BIGINT (DB-level triggers carry huge values) — read
            // wide so an int overflow can't throw on a DDL trigger.
            long type = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            int sequence = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
            int inactive = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture);

            var (isBefore, ins, upd, del) = DecodeTriggerHeader(type);
            return new TriggerHeaderInfo
            {
                Table = table,
                IsBefore = isBefore,
                FiresInsert = ins,
                FiresUpdate = upd,
                FiresDelete = del,
                Position = sequence,
                Active = inactive != 1,
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read trigger header for {triggerName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Decodes RDB$TRIGGER_TYPE into (timing, events). Bit 0 is the timing
    /// (odd = BEFORE, even = AFTER for relation triggers); events reuse
    /// <see cref="DecodeTriggerOps"/>. DB-level / DDL triggers (type ≥ 8192, or ≤ 0)
    /// yield no events — they aren't relation triggers.</summary>
    internal static (bool IsBefore, bool Insert, bool Update, bool Delete) DecodeTriggerHeader(long triggerType)
    {
        if (triggerType <= 0 || triggerType >= 8192) return (true, false, false, false);
        bool isBefore = (triggerType & 1) == 1;
        var (ins, upd, del) = DecodeTriggerOps((int)triggerType);
        return (isBefore, ins, upd, del);
    }

    public async Task<string> GetTriggerDescriptionAsync(string triggerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(triggerName)) return string.Empty;

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — see gotcha #98
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT RDB$DESCRIPTION FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @name";
            cmd.CommandTimeout = 0;
            cmd.Transaction = MetaTx;
            cmd.Parameters.AddWithValue("@name", triggerName);

            string? description = null;
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0))
                {
                    description = reader.GetString(0);
                }
            }
            return NormalizeDescription(description);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read description for trigger {triggerName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // Trigger dependencies use RDB$*_TYPE = 2 (Trigger). "Depends on" = what this
    // trigger references (it is the DEPENDENT — tables/columns/generators/…);
    // "depended on by" = what references this trigger (typically nothing). One @name
    // reference per query, so no distinct-name binding needed (cf. gotcha #47).
    internal const string TriggerDependsOnSql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDED_ON_NAME), TRIM(d.RDB$FIELD_NAME), " +
        "    CAST(d.RDB$DEPENDED_ON_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDENT_NAME) = @name AND d.RDB$DEPENDENT_TYPE = 2 " +
        "ORDER BY 3, 1";

    internal const string TriggerDependedOnBySql =
        "SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), " +
        "    CAST(d.RDB$DEPENDENT_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDED_ON_NAME) = @name AND d.RDB$DEPENDED_ON_TYPE = 2 " +
        "ORDER BY 2, 1";

    public async Task<(IReadOnlyList<DependencyInfo> DependsOn, IReadOnlyList<DependencyInfo> DependedOnBy)> GetTriggerDependenciesAsync(
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(triggerName))
        {
            return (Array.Empty<DependencyInfo>(), Array.Empty<DependencyInfo>());
        }

        var connection = MetaConnection();
        var commandLock = MetaLock();   // capture once — see gotcha #98
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dependsOn = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = TriggerDependsOnSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", triggerName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependsOn.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)),
                    });
                }
            }

            var dependedOnBy = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = TriggerDependedOnBySql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@name", triggerName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dependedOnBy.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                    });
                }
            }

            return (dependsOn, dependedOnBy);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read dependencies for trigger {triggerName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    public async Task<(IReadOnlyList<DependencyInfo> DependsOn, IReadOnlyList<DependencyInfo> DependedOnBy)> GetDependenciesAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName))
        {
            return (Array.Empty<DependencyInfo>(), Array.Empty<DependencyInfo>());
        }

        var connection = MetaConnection();
        // Capture the lock ONCE: MetaLock() resolves to the metadata or data
        // semaphore via MetadataIsIndependent, which can flip mid-call (e.g. the
        // metadata attachment breaks). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the
        // one we hold (it lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it). See gotcha #98.
        var commandLock = MetaLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dependsOn = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = DependsOnSql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                // Bind each distinct parameter name — see DependsOnSql comment
                // for why we don't reuse @tableName across branches.
                cmd.Parameters.AddWithValue("@tableName", tableName);
                cmd.Parameters.AddWithValue("@t2", tableName);
                cmd.Parameters.AddWithValue("@t3", tableName);
                cmd.Parameters.AddWithValue("@t4", tableName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Column order: (OBJ_NAME, FIELD_NAME, OBJ_TYPE).
                    dependsOn.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)),
                    });
                }
            }

            var dependedOnBy = new List<DependencyInfo>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = DependedOnBySql;
                cmd.CommandTimeout = 0;
                cmd.Transaction = MetaTx;
                cmd.Parameters.AddWithValue("@tableName", tableName);
                cmd.Parameters.AddWithValue("@t2", tableName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Column order: (DEPENDENT_NAME, FIELD_NAME, DEPENDENT_TYPE, TRIGGER_TYPE).
                    bool? firesInsert = null, firesUpdate = null;
                    if (!reader.IsDBNull(3))
                    {
                        var (ins, upd, _) = DecodeTriggerOps(reader.GetInt32(3));
                        firesInsert = ins;
                        firesUpdate = upd;
                    }
                    dependedOnBy.Add(new DependencyInfo
                    {
                        ObjectName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                        FieldName = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                        ObjectType = MapObjectType(reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)),
                        FiresOnInsert = firesInsert,
                        FiresOnUpdate = firesUpdate,
                    });
                }
            }

            // Related Tables — sourced ONLY from FK constraints. Each row from
            // these queries is appended as a Table-typed dependency so the VM's
            // categoriser puts it in the Tables group like any other entry.
            await AppendFkResultsAsync(connection, FkOutgoingSql, tableName, dependsOn, cancellationToken).ConfigureAwait(false);
            await AppendFkResultsAsync(connection, FkIncomingSql, tableName, dependedOnBy, cancellationToken).ConfigureAwait(false);

            return (dependsOn, dependedOnBy);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read dependencies for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>
    /// Returns one page worth of rows from the table — preview only, not the
    /// user's "Execute query" path. Attaches to the user's working tx when
    /// active; otherwise the driver runs the SELECT in an implicit read tx.
    ///
    /// <paramref name="page"/> is 1-based; <paramref name="pageSize"/> is the
    /// row cap. The query uses Firebird's <c>ROWS m TO n</c> syntax (2.5+).
    /// </summary>
    public Task<QueryResult> GetDataPreviewAsync(
        string tableName,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => GetDataPreviewAsync(tableName, page, pageSize, null, cancellationToken);

    /// <summary>
    /// Variant that appends an <c>ORDER BY</c> clause. The <paramref name="orderBy"/>
    /// string is inserted verbatim after the FROM — caller is responsible for
    /// quoting identifiers (the VM wraps column names in <c>"..."</c> per Firebird
    /// convention to handle reserved words).
    /// </summary>
    public async Task<QueryResult> GetDataPreviewAsync(
        string tableName,
        int page,
        int pageSize,
        string? orderBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName))
        {
            return new QueryResult();
        }

        var (startRow, endRow) = ComputeRowRange(page, pageSize);

        var connection = DataConnection();
        var sw = Stopwatch.StartNew();
        // Capture the lock ONCE (see the MetaLock note above / gotcha #98).
        var commandLock = DataLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = BuildDataPreviewSql(tableName, startRow, endRow, orderBy);
            cmd.CommandTimeout = 0;
            cmd.Transaction = DataTx;

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
                Truncated = rows.Count >= pageSize,
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not preview data for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>
    /// Returns the row count of the table capped at <paramref name="cap"/>.
    /// Implemented as <c>SELECT COUNT(*) FROM (SELECT FIRST cap 1 AS X ...)</c>
    /// so the engine doesn't scan the whole table on big tables — once it has
    /// counted <paramref name="cap"/> rows it stops. A return value equal to
    /// <paramref name="cap"/> means "≥ cap rows"; less means exact count.
    /// </summary>
    public async Task<int> GetRowCountAsync(
        string tableName,
        int cap,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) return 0;
        if (cap <= 0) return 0;

        var connection = DataConnection();
        // Capture the lock ONCE (see the MetaLock note above / gotcha #98).
        var commandLock = DataLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = BuildRowCountSql(tableName, cap);
            cmd.CommandTimeout = 0;
            cmd.Transaction = DataTx;
            var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return raw switch
            {
                null => 0,
                int i => i,
                long l => (int)Math.Min(l, int.MaxValue),
                _ => Convert.ToInt32(raw, CultureInfo.InvariantCulture),
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not count rows for {tableName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // Page math kept internal + static so tests pin the boundary behavior.
    // page is 1-based; pageSize > 0. Clamps below 1 to (1, pageSize).
    internal static (int StartRow, int EndRow) ComputeRowRange(int page, int pageSize)
    {
        if (pageSize <= 0) pageSize = 1;
        if (page < 1) page = 1;
        var start = (page - 1) * pageSize + 1;
        var end = page * pageSize;
        return (start, end);
    }

    private async Task AppendFkResultsAsync(
        FbConnection connection,
        string sql,
        string tableName,
        List<DependencyInfo> target,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        cmd.Transaction = MetaTx;
        cmd.Parameters.AddWithValue("@tableName", tableName);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var name = reader.GetString(0).Trim();
            if (name.Length == 0) continue;
            target.Add(new DependencyInfo
            {
                ObjectName = name,
                FieldName = null,
                ObjectType = "Table",
            });
        }
    }

    // Internal so tests can pin the SQL shape — quoted identifier, optional
    // ORDER BY between FROM and ROWS, and the 1-based inclusive ROWS m TO n
    // window. Embedded as literals (not parameters) because FB 2.5 doesn't
    // bind parameters in ROWS clauses; safe with integers.
    internal static string BuildDataPreviewSql(string tableName, int startRow, int endRow, string? orderBy)
    {
        var quoted = tableName.Replace("\"", "\"\"");
        var sb = new System.Text.StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture, "SELECT * FROM \"{0}\"", quoted);
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            sb.Append(" ORDER BY ").Append(orderBy.Trim());
        }
        sb.AppendFormat(CultureInfo.InvariantCulture, " ROWS {0} TO {1}", startRow, endRow);
        return sb.ToString();
    }

    // SELECT COUNT(*) FROM (SELECT FIRST {cap} 1 AS X FROM "T") — bounded
    // row counter. Without the inner FIRST {cap}, COUNT(*) on a 50M-row
    // table is a sequential scan; with it, the engine stops after cap rows.
    internal static string BuildRowCountSql(string tableName, int cap)
    {
        var quoted = tableName.Replace("\"", "\"\"");
        return string.Format(
            CultureInfo.InvariantCulture,
            "SELECT COUNT(*) FROM (SELECT FIRST {0} 1 AS X FROM \"{1}\") sub",
            cap,
            quoted);
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

    // DependsOn — what this table uses. Reproduces IBExpert's coverage as four
    // UNION ALL branches, but skips IBExpert's broken self-join on RDB$RELATIONS
    // (which causes 365k natural-scan reads). Column projection is identical
    // across branches: (name, field_name, type) so the reader keeps one shape.
    //   1) User-defined domains the table references via RDB$RELATION_FIELDS.
    //      RDB$<n> anonymous backing domains for inline types are excluded.
    //      Type is hardcoded to 9 ("Domain").
    //   2) Direct RDB$DEPENDENCIES rows where this table is the dependent
    //      (computed cols, defaults, check expressions referencing other
    //      objects). Filtered to dependent-type 0 (relation) not inside a
    //      package. The depended-on side's real type rides along.
    //   3) Same as (2) but for rows inside a package — surfaced as type 18
    //      ("Package") so they land in the Packages category.
    //   4) Indirect via domain: when a domain (DEPENDENT_TYPE = 3) depends on
    //      something, every relation whose field uses that domain inherits the
    //      dependency. Inner-join RDB$RELATION_FIELDS to find this table's
    //      domain-mediated upstream targets.
    // Each branch uses a distinct parameter name (@tableName / @t2 / @t3 / @t4)
    // so the FB driver doesn't have to do multi-reference name resolution —
    // empirically that path drops bindings on branches past the first INNER
    // JOIN, leaving indirect-via-domain rows behind.
    //
    // Every branch carries `RDB$DEPENDED_ON_TYPE <> 0` so Relation-typed rows
    // (Tables AND Views) NEVER come from this catalog. Tables are sourced
    // exclusively from the FK queries (see FkOutgoingSql / FkIncomingSql);
    // Views still flow through RDB$DEPENDENCIES, but only via the dedicated
    // VIEW_BLR-gated branch in DependedOnBySql.
    internal const string DependsOnSql =
        "SELECT DISTINCT " +
        "    CAST(TRIM(rf.RDB$FIELD_SOURCE) AS VARCHAR(64)) AS OBJ_NAME, " +
        "    CAST(NULL AS VARCHAR(64)) AS FIELD_NAME, " +
        "    CAST(9 AS INTEGER) AS OBJ_TYPE " +
        "FROM RDB$RELATION_FIELDS rf " +
        "WHERE TRIM(rf.RDB$RELATION_NAME) = @tableName " +
        "  AND rf.RDB$FIELD_SOURCE NOT STARTING WITH 'RDB$' " +
        "UNION ALL " +
        "SELECT DISTINCT " +
        "    CAST(TRIM(d.RDB$DEPENDED_ON_NAME) AS VARCHAR(64)), " +
        "    CAST(TRIM(d.RDB$FIELD_NAME) AS VARCHAR(64)), " +
        "    CAST(d.RDB$DEPENDED_ON_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDENT_NAME) = @t2 " +
        "  AND d.RDB$DEPENDENT_TYPE = 0 " +
        "  AND d.RDB$PACKAGE_NAME IS NULL " +
        "  AND d.RDB$DEPENDED_ON_TYPE <> 0 " +
        "UNION ALL " +
        "SELECT DISTINCT " +
        "    CAST(TRIM(d.RDB$PACKAGE_NAME) AS VARCHAR(64)), " +
        "    CAST(TRIM(d.RDB$FIELD_NAME) AS VARCHAR(64)), " +
        "    CAST(18 AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "WHERE TRIM(d.RDB$DEPENDENT_NAME) = @t3 " +
        "  AND d.RDB$DEPENDENT_TYPE = 0 " +
        "  AND d.RDB$PACKAGE_NAME IS NOT NULL " +
        "UNION ALL " +
        "SELECT DISTINCT " +
        "    CAST(TRIM(d.RDB$DEPENDED_ON_NAME) AS VARCHAR(64)), " +
        "    CAST(TRIM(d.RDB$FIELD_NAME) AS VARCHAR(64)), " +
        "    CAST(d.RDB$DEPENDED_ON_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "INNER JOIN RDB$RELATION_FIELDS f ON f.RDB$FIELD_SOURCE = d.RDB$DEPENDENT_NAME " +
        "WHERE d.RDB$DEPENDENT_TYPE = 3 " +
        "  AND TRIM(f.RDB$RELATION_NAME) = @t4 " +
        "  AND d.RDB$DEPENDED_ON_TYPE <> 0 " +
        "ORDER BY 1, 2";

    // DependedOnBy — objects that depend on this table. Two branches:
    //   1) Direct dependents via RDB$DEPENDENCIES. CHECK_<n> / RDB$<n>
    //      system-named triggers and DEPENDENT_TYPE=3 anonymous-field rows
    //      are excluded.
    //   2) Indirect via domain: relations whose fields use a domain that
    //      depends on this table — those relations transitively use it too.
    //      Inner-join RDB$RELATION_FIELDS to find the using relation; left-join
    //      RDB$RELATIONS to read RDB$VIEW_BLR so we can distinguish Tables
    //      (type 0) from Views (type 1) without falling back to "Object (3)".
    //      IBExpert ships a broken cross-join here that scans RDB$RELATIONS
    //      365k times for a table like NAGL; this version stays on the index.
    // Branch 1: direct dependents (Procedures / Triggers / Views / etc.).
    // Branch 2: indirect-via-domain, VIEWS ONLY.
    // Both branches exclude RDB$DEPENDENT_TYPE = 0 (Relation) — Tables come
    // exclusively from FkIncomingSql. The CHECK_ / RDB$ exclusion stays scoped
    // to RDB$DEPENDENT_TYPE = 2 (Trigger) so user procedures / views named
    // CHECK_<something> (e.g. CHECK_ZAKSIEGWREJVAT) pass through.
    // 4th column = RDB$TRIGGER_TYPE for trigger dependents (LEFT JOIN, so it's
    // NULL for non-triggers), used to decode INSERT/UPDATE operation flags in
    // the field-dependencies panel. The table-level Zależności tree reads only
    // columns 0-2 and ignores column 3, so the extra column is harmless there.
    internal const string DependedOnBySql =
        "SELECT DISTINCT " +
        "    CAST(TRIM(d.RDB$DEPENDENT_NAME) AS VARCHAR(64)), " +
        "    CAST(TRIM(d.RDB$FIELD_NAME) AS VARCHAR(64)), " +
        "    CAST(d.RDB$DEPENDENT_TYPE AS INTEGER), " +
        "    CAST(tr.RDB$TRIGGER_TYPE AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "LEFT JOIN RDB$TRIGGERS tr ON tr.RDB$TRIGGER_NAME = d.RDB$DEPENDENT_NAME AND d.RDB$DEPENDENT_TYPE = 2 " +
        "WHERE d.RDB$DEPENDED_ON_TYPE = 0 " +
        "  AND TRIM(d.RDB$DEPENDED_ON_NAME) = @tableName " +
        "  AND d.RDB$DEPENDENT_TYPE <> 3 " +
        "  AND d.RDB$DEPENDENT_TYPE <> 0 " +
        "  AND NOT (d.RDB$DEPENDENT_TYPE = 2 AND TRIM(d.RDB$DEPENDENT_NAME) STARTING WITH 'CHECK_') " +
        "  AND NOT (d.RDB$DEPENDENT_TYPE = 2 AND TRIM(d.RDB$DEPENDENT_NAME) STARTING WITH 'RDB$') " +
        "UNION ALL " +
        "SELECT DISTINCT " +
        "    CAST(TRIM(f.RDB$RELATION_NAME) AS VARCHAR(64)), " +
        "    CAST(TRIM(d.RDB$FIELD_NAME) AS VARCHAR(64)), " +
        "    CAST(1 AS INTEGER), " +
        "    CAST(NULL AS INTEGER) " +
        "FROM RDB$DEPENDENCIES d " +
        "INNER JOIN RDB$RELATION_FIELDS f ON f.RDB$FIELD_SOURCE = d.RDB$DEPENDENT_NAME " +
        "INNER JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = f.RDB$RELATION_NAME " +
        "WHERE d.RDB$DEPENDENT_TYPE = 3 " +
        "  AND TRIM(d.RDB$DEPENDED_ON_NAME) = @t2 " +
        "  AND r.RDB$VIEW_BLR IS NOT NULL " +
        "ORDER BY 1, 2";

    /// <summary>
    /// Decodes a Firebird <c>RDB$TRIGGER_TYPE</c> into (firesOnInsert,
    /// firesOnUpdate, firesOnDelete). Firebird packs up to three DML "slots"
    /// into the type: <c>((type + 1) >> (2*slot + 1)) &amp; 3</c> yields
    /// 1=INSERT / 2=UPDATE / 3=DELETE / 0=none for slot 0,1,2. DB-level / DDL
    /// triggers (type ≥ 8192) carry no DML semantics → all false. Internal for
    /// unit testing without a live FB.
    /// </summary>
    internal static (bool insert, bool update, bool delete) DecodeTriggerOps(int triggerType)
    {
        if (triggerType >= 8192 || triggerType <= 0) return (false, false, false);
        bool ins = false, upd = false, del = false;
        long n = triggerType + 1;
        for (int slot = 0; slot < 3; slot++)
        {
            var v = (n >> (2 * slot + 1)) & 3;
            if (v == 1) ins = true;
            else if (v == 2) upd = true;
            else if (v == 3) del = true;
        }
        return (ins, upd, del);
    }

    // FK queries — the SOLE source of Related Tables, per spec. No indirect
    // chains, no recursive walks, no trigger / procedure / view / domain /
    // computed-column derivations. Just plain FOREIGN KEY constraints joined
    // through RDB$REF_CONSTRAINTS ↔ RDB$RELATION_CONSTRAINTS.
    internal const string FkOutgoingSql =
        "SELECT DISTINCT " +
        "    CAST(TRIM(pk.RDB$RELATION_NAME) AS VARCHAR(64)) AS TARGET_TABLE " +
        "FROM RDB$REF_CONSTRAINTS rc " +
        "JOIN RDB$RELATION_CONSTRAINTS fk ON fk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
        "JOIN RDB$RELATION_CONSTRAINTS pk ON pk.RDB$CONSTRAINT_NAME = rc.RDB$CONST_NAME_UQ " +
        "WHERE TRIM(fk.RDB$RELATION_NAME) = @tableName " +
        "ORDER BY pk.RDB$RELATION_NAME";

    internal const string FkIncomingSql =
        "SELECT DISTINCT " +
        "    CAST(TRIM(fk.RDB$RELATION_NAME) AS VARCHAR(64)) AS REFERENCING_TABLE " +
        "FROM RDB$REF_CONSTRAINTS rc " +
        "JOIN RDB$RELATION_CONSTRAINTS fk ON fk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
        "JOIN RDB$RELATION_CONSTRAINTS pk ON pk.RDB$CONSTRAINT_NAME = rc.RDB$CONST_NAME_UQ " +
        "WHERE TRIM(pk.RDB$RELATION_NAME) = @tableName " +
        "ORDER BY fk.RDB$RELATION_NAME";

    // RDB$OBJECT_TYPE codes (RDB$DEPENDED_ON_TYPE / RDB$DEPENDENT_TYPE share the
    // same enum). Unknown codes fall back to "Object (N)".
    internal static string MapObjectType(int? objectType) => objectType switch
    {
        0 => "Table",
        1 => "View",
        2 => "Trigger",
        5 => "Procedure",
        7 => "Exception",
        8 => "User",
        // 9 = RDB$OBJECT_TYPE "Field" in catalog literature, but in our dependency
        // tree it always represents a domain reference (the DependsOn query
        // hardcodes 9 for RDB$FIELD_SOURCE rows). Surface it as "Domain".
        9 => "Domain",
        10 => "Index",
        14 => "Generator",
        15 => "Function",
        18 => "Package",
        null => string.Empty,
        _ => string.Format(CultureInfo.InvariantCulture, "Object ({0})", objectType.Value),
    };

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
        // CHECK source via a correlated scalar subquery, NOT a join. A Firebird
        // CHECK constraint is backed by several triggers (BEFORE INSERT type 1,
        // BEFORE UPDATE type 3, …) so RDB$CHECK_CONSTRAINTS holds one row per
        // trigger. Joining it to RDB$RELATION_CONSTRAINTS multiplied each CHECK
        // into N grid rows (one per backing trigger), all sharing the constraint
        // name but only the type-1 row carrying the source — the rest came back
        // with a NULL source. The scalar subquery collapses that to exactly one
        // row per constraint (ROWS 1 on the type-1 trigger), and yields NULL for
        // non-CHECK constraints without affecting their row count.
        "       (SELECT t.RDB$TRIGGER_SOURCE " +
        "        FROM RDB$CHECK_CONSTRAINTS chk " +
        "        JOIN RDB$TRIGGERS t ON t.RDB$TRIGGER_NAME = chk.RDB$TRIGGER_NAME " +
        "        WHERE chk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
        "          AND t.RDB$TRIGGER_TYPE = 1 " +
        "        ROWS 1) AS CHECK_SOURCE, " +
        "       rc.RDB$INDEX_NAME, " +
        "       fk.RDB$UPDATE_RULE, " +
        "       fk.RDB$DELETE_RULE, " +
        "       idx.RDB$INDEX_TYPE " +
        "FROM RDB$RELATION_CONSTRAINTS rc " +
        "LEFT JOIN RDB$REF_CONSTRAINTS fk ON fk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
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
        "          ROWS 1) AS FK_TABLE, " +
        // AI = 1 when this column is auto-incremented. Detected via two paths:
        //   (a) FB3+ identity: RDB$RELATION_FIELDS.RDB$IDENTITY_TYPE IS NOT NULL.
        //   (b) Legacy trigger pattern: a BEFORE INSERT user-trigger on this table
        //       whose source mentions GEN_ID( and NEW.<field name>. BLOB SUB_TYPE
        //       TEXT supports CONTAINING since FB 2.5.
        // NOTE: RDB$IDENTITY_TYPE exists from FB3. On FB2.5 the column is missing
        // and this query throws an FbException — the Pola load step catches it
        // via SafeLoadAsync and only the Fields tab shows an error; other tabs
        // continue to render. Pragmatic trade-off given the FB5-primary user base.
        "       CASE WHEN (rf.RDB$IDENTITY_TYPE IS NOT NULL " +
        "                 OR EXISTS(SELECT 1 FROM RDB$TRIGGERS t " +
        "                             WHERE t.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
        "                               AND t.RDB$TRIGGER_TYPE = 1 " +
        "                               AND COALESCE(t.RDB$SYSTEM_FLAG, 0) = 0 " +
        "                               AND t.RDB$TRIGGER_SOURCE CONTAINING 'GEN_ID(' " +
        "                               AND t.RDB$TRIGGER_SOURCE CONTAINING 'NEW.' || TRIM(rf.RDB$FIELD_NAME))) " +
        "            THEN 1 ELSE 0 END AS IS_AI " +
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

    // Firebird stores index selectivity in RDB$INDICES.RDB$STATISTICS as a DOUBLE
    // in [0, 1]. For an index whose statistics have never been computed — a freshly
    // created index, or any index on an EMPTY table (SET STATISTICS on zero rows
    // leaves selectivity undefined) — Firebird uses the sentinel value -1, NOT NULL.
    // Passing that straight to the grid renders a meaningless "-1.000000" in every
    // such cell. Treat any negative selectivity as "unknown" → null → blank cell.
    internal static double? NormalizeStatistics(double? raw)
        => raw is { } v && v < 0 ? null : raw;

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
