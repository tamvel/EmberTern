using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

public sealed class FirebirdMetadataReader
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly MetadataLane _lane;

    public FirebirdMetadataReader(FirebirdConnectionService connectionService)
        : this(connectionService, new MetadataLane(connectionService))
    {
    }

    public FirebirdMetadataReader(FirebirdConnectionService connectionService, MetadataLane lane)
    {
        _connectionService = connectionService;
        _lane = lane;
    }

    // Connection + lock for this reader's lane. In production the reader is built with
    // the metadata TransactionService, so browsing runs on the metadata attachment and
    // never pins objects in the user's data working transaction. Falls back to the data
    // connection when no transaction service is injected (tests).
    private FbConnection LaneConnection()
        => _lane.RequireOpenConnection();
    private SemaphoreSlim LaneLock()
        => _lane.CommandLock;

    public async Task<IReadOnlyList<MetadataObject>> ListAsync(
        MetadataObjectKind kind,
        CancellationToken cancellationToken = default)
    {
        var connection = LaneConnection();
        var sql = SqlFor(kind, FirebirdDdlReader.ParseServerMajor(connection.ServerVersion));

        // Readers never open their own transaction. When the user has a working tx
        // active we attach to it; otherwise the managed driver runs the SELECT in
        // an implicit read tx (auto-committed per command). The connection's
        // CommandLock serializes us against every other reader / executor — FbConnection
        // is single-threaded and concurrent commands hang or throw.
        //
        // Capture the lock ONCE: LaneLock() resolves to the metadata or data semaphore
        // based on MetadataIsIndependent, which can flip mid-call (e.g. a connection-level
        // error breaks the metadata attachment). Re-evaluating it at Release would then
        // release a DIFFERENT semaphore than we acquired — permanently leaking the one we
        // hold. That semaphore lives on the long-lived connection service, so a leak
        // survives reconnect and only a process restart clears it (gotcha below).
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _lane.TransactionForCommand;

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
                // Triggers/indexes carry a second column: RDB$..._INACTIVE (1 = inactive,
                // 0/null = active). Every other kind's SqlFor returns name only.
                bool? isActive = null;
                if (reader.FieldCount > 1 && !reader.IsDBNull(1))
                {
                    isActive = Convert.ToInt32(reader.GetValue(1)) != 1;
                }
                results.Add(new MetadataObject(name, kind) { IsActive = isActive });
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read {kind.ToString().ToLowerInvariant()}s: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // Internal so tests can verify the bypass list (SystemTable is the only kind that
    // *wants* RDB$/MON$/SEC$ names — that's the whole point of the view).
    internal static bool BypassSystemNameFilter(MetadataObjectKind kind)
        => kind == MetadataObjectKind.SystemTable;

    /// <summary>
    /// Returns the number of objects of <paramref name="kind"/> WITHOUT fetching the
    /// list — a single <c>SELECT COUNT(*)</c>. Used to show the category label
    /// (e.g. <c>Tables (2356)</c>) right after connect while the full leaf list is
    /// deferred to first expansion. Same lane / lock / implicit-tx pattern as
    /// <see cref="ListAsync"/>; a failure (unsupported category on this FB version,
    /// missing privileges) surfaces as <see cref="MetadataReadException"/> so the
    /// caller can leave the count blank and keep the category expandable to retry.
    /// </summary>
    public async Task<int> CountAsync(
        MetadataObjectKind kind,
        CancellationToken cancellationToken = default)
    {
        var connection = LaneConnection();
        var sql = CountSqlFor(kind, FirebirdDdlReader.ParseServerMajor(connection.ServerVersion));
        // Capture the lock once — see ListAsync for why re-evaluating LaneLock() at
        // Release can leak a semaphore.
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _lane.TransactionForCommand;

            var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            // FB COUNT(*) is BIGINT in dialect 3 → comes back as long; ToInt32 is safe
            // for any realistic catalog size.
            return scalar is null or DBNull ? 0 : Convert.ToInt32(scalar);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not count {kind.ToString().ToLowerInvariant()}s: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // COUNT(*) mirror of SqlFor — same table + same SYSTEM_FLAG predicate, no ORDER BY,
    // no TRIM. The only place a server COUNT could diverge from the displayed list is
    // Domain: RDB$FIELDS holds one anonymous RDB$xxx backing-domain per inline column
    // type, which ListAsync strips client-side via IsSystemName. So the Domain count
    // MUST exclude RDB$-prefixed rows server-side, or it would report thousands more
    // than the user-domain list shows. For every other kind the COALESCE(SYSTEM_FLAG)=0
    // predicate already matches the displayed set (user objects are never RDB$/MON$/SEC$
    // named), and LoadGroupAsync overwrites Count with the real list size on expand
    // anyway, so any hair-thin edge (null/empty names) self-corrects.
    // Internal so tests can assert the shape without a live connection.
    // See SqlFor for why packaged routines are excluded on FB3+ (avoids counting a
    // packaged namesake as a top-level standalone procedure/function).
    internal static string CountSqlFor(MetadataObjectKind kind) => CountSqlFor(kind, 0);

    internal static string CountSqlFor(MetadataObjectKind kind, int serverMajor)
    {
        if (serverMajor >= 3 && kind == MetadataObjectKind.Procedure)
            return "SELECT COUNT(*) FROM RDB$PROCEDURES " +
                   "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$PACKAGE_NAME IS NULL";
        if (serverMajor >= 3 && kind == MetadataObjectKind.Function)
            return "SELECT COUNT(*) FROM RDB$FUNCTIONS " +
                   "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$PACKAGE_NAME IS NULL";
        return CountSqlForBase(kind);
    }

    private static string CountSqlForBase(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table =>
            "SELECT COUNT(*) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NULL",
        MetadataObjectKind.View =>
            "SELECT COUNT(*) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NOT NULL",
        MetadataObjectKind.Procedure =>
            "SELECT COUNT(*) FROM RDB$PROCEDURES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.Trigger =>
            "SELECT COUNT(*) FROM RDB$TRIGGERS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.Function =>
            "SELECT COUNT(*) FROM RDB$FUNCTIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.Generator =>
            "SELECT COUNT(*) FROM RDB$GENERATORS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.Domain =>
            "SELECT COUNT(*) FROM RDB$FIELDS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
            "AND RDB$FIELD_NAME NOT STARTING WITH 'RDB$'",
        MetadataObjectKind.Package =>
            "SELECT COUNT(*) FROM RDB$PACKAGES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.Exception =>
            "SELECT COUNT(*) FROM RDB$EXCEPTIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.Role =>
            "SELECT COUNT(*) FROM RDB$ROLES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.User =>
            "SELECT COUNT(*) FROM SEC$USERS",
        MetadataObjectKind.Index =>
            "SELECT COUNT(*) FROM RDB$INDICES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0",
        MetadataObjectKind.SystemTable =>
            "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 1 AND RDB$VIEW_BLR IS NULL",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

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

        var connection = LaneConnection();
        var sql = ColumnsSqlFor(FirebirdDdlReader.ParseServerMajor(connection.ServerVersion));
        // Capture the lock once — see ListAsync for why re-evaluating LaneLock() at
        // Release can leak a semaphore.
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _lane.TransactionForCommand;
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
                var fieldSource = reader.IsDBNull(6) ? null : reader.GetString(6);
                var notNull = !reader.IsDBNull(7) && reader.GetInt32(7) == 1;
                // Rich-but-optional Quick Info facts (Package 5, Stage A). RDB$DEFAULT_SOURCE /
                // RDB$DESCRIPTION / RDB$COMPUTED_SOURCE are BLOB SUB_TYPE TEXT — read as strings
                // like FieldsSql does.
                var defaultSource = reader.IsDBNull(8) ? null : reader.GetString(8);
                var description = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();
                var computedSource = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
                var isPk = !reader.IsDBNull(11) && reader.GetInt32(11) > 0;
                var isFk = !reader.IsDBNull(12) && reader.GetInt32(12) > 0;
                var fkTable = reader.IsDBNull(13) ? null : reader.GetString(13).Trim();
                // Measured against a live FB5 engine (a GENERATED ALWAYS and a GENERATED BY DEFAULT
                // column created side by side, catalog read back): 0 = ALWAYS, 1 = BY DEFAULT.
                // NULL = not an identity column. An unrecognised value maps to None rather than a
                // guess — under-reporting an identity is a loud INSERT failure; mis-reporting ALWAYS
                // as BY DEFAULT is a statement that cannot work.
                var identity = reader.IsDBNull(14)
                    ? IdentityKind.None
                    : reader.GetInt32(14) switch
                    {
                        0 => IdentityKind.Always,
                        1 => IdentityKind.ByDefault,
                        _ => IdentityKind.None,
                    };
                var type = FirebirdTableDetailReader.FormatFieldType(fieldType, fieldLength, fieldScale, fieldPrecision, subType);
                var domain = FirebirdTableDetailReader.NormalizeDomain(fieldSource);
                columns.Add(new ColumnSpec(name, type, domain, notNull)
                {
                    DefaultValue = FirebirdTableDetailReader.StripDefaultPrefix(defaultSource),
                    Description = string.IsNullOrEmpty(description) ? null : description,
                    IsComputed = !string.IsNullOrEmpty(computedSource),
                    IsPrimaryKey = isPk,
                    IsForeignKey = isFk,
                    ForeignKeyTable = string.IsNullOrEmpty(fkTable) ? null : fkTable,
                    Identity = identity,
                });
            }
            return columns;
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

    /// <summary>
    /// User-defined domains plus their formatted SQL type. Used by the
    /// AddFieldDialog's Domain ComboBox so the user can see what they're
    /// picking without cross-referencing the catalog. Filters out anonymous
    /// RDB$ backing-domains client-side (matches the <see cref="ListAsync"/>
    /// behaviour for <see cref="MetadataObjectKind.Domain"/>).
    /// </summary>
    public async Task<IReadOnlyList<DomainSpec>> ListDomainsAsync(CancellationToken cancellationToken = default)
    {
        var connection = LaneConnection();
        // Capture the lock once — see ListAsync for why re-evaluating LaneLock() at
        // Release can leak a semaphore.
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = DomainsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _lane.TransactionForCommand;

            var domains = new List<DomainSpec>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) continue;
                var name = reader.GetString(0).Trim();
                if (name.Length == 0) continue;
                if (IsSystemName(name)) continue;
                var fieldType = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var fieldLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var fieldScale = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var fieldPrecision = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var subType = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var notNull = !reader.IsDBNull(6) && reader.GetInt32(6) == 1;
                var charset = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
                if (string.IsNullOrEmpty(charset)) charset = null;
                var type = FirebirdTableDetailReader.FormatFieldType(fieldType, fieldLength, fieldScale, fieldPrecision, subType);
                domains.Add(new DomainSpec(name, type, notNull, charset));
            }
            return domains;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read domains: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    internal const string DomainsSql =
        "SELECT TRIM(f.RDB$FIELD_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH, " +
        "       f.RDB$FIELD_SCALE, f.RDB$FIELD_PRECISION, " +
        "       f.RDB$FIELD_SUB_TYPE, f.RDB$NULL_FLAG, " +
        "       cs.RDB$CHARACTER_SET_NAME " +
        "FROM RDB$FIELDS f " +
        "LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
        "WHERE COALESCE(f.RDB$SYSTEM_FLAG, 0) = 0 " +
        "ORDER BY f.RDB$FIELD_NAME";

    // Joins RDB$RELATION_FIELDS to RDB$FIELDS so the autocomplete dropdown can render
    // "COLUMN : TYPE : DOMAIN", and carries the rich-but-optional Quick Info facts
    // (Package 5, Stage A): default/computed expressions, description, and the
    // PK/FK/FK-target/identity classification. RDB$FIELD_SOURCE is the (possibly
    // anonymous) domain; the column's own RDB$NULL_FLAG overrides the domain's for
    // nullability.
    //
    // The PK/FK-flag + FK-target correlated-subquery fragments are lifted verbatim from
    // FirebirdTableDetailReader.FieldsSql (one proven source for that shape). Identity
    // detection uses ONLY RDB$IDENTITY_TYPE — the light editor path deliberately skips
    // FieldsSql's legacy generator-trigger blob scan (that heavier detection stays in the
    // Table Detail surface). RDB$IDENTITY_TYPE is FB3+, so the IS_IDENTITY expression is
    // version-gated (gotcha #146): on FB2.5 it projects a constant 0 rather than
    // referencing the missing column (which would throw). The projected column set — and
    // therefore the reader's ordinals — is identical across versions; only the identity
    // expression differs.
    //
    // Columns are cached per table after the first read (MainWindowViewModel._columnCache),
    // so the added correlated subqueries are a one-time per-table cost on first dot/hover,
    // not a per-keystroke one.
    internal static string ColumnsSqlFor(int serverMajor)
    {
        // The RAW RDB$IDENTITY_TYPE, not a 0/1 "is it one?" flag: its VALUE is the ALWAYS/BY DEFAULT
        // distinction (measured: 0 = ALWAYS, 1 = BY DEFAULT), and Firebird rejects an INSERT that names
        // a GENERATED ALWAYS column without OVERRIDING SYSTEM VALUE. Collapsing it to a bool — as this
        // did — discarded the only fact that tells generated DML which form is legal. NULL = not an
        // identity column. Still version-gated: the column is FB3+ (gotcha #146), and the projected
        // column set stays identical across versions so the reader's ordinals do not move.
        var identityExpr = serverMajor >= 3
            ? "rf.RDB$IDENTITY_TYPE"
            : "CAST(NULL AS SMALLINT)";
        return
            "SELECT TRIM(rf.RDB$FIELD_NAME), " +
            "       ft.RDB$FIELD_TYPE, ft.RDB$FIELD_LENGTH, " +
            "       ft.RDB$FIELD_SCALE, ft.RDB$FIELD_PRECISION, " +
            "       ft.RDB$FIELD_SUB_TYPE, " +
            "       rf.RDB$FIELD_SOURCE, " +
            "       COALESCE(rf.RDB$NULL_FLAG, ft.RDB$NULL_FLAG), " +
            "       rf.RDB$DEFAULT_SOURCE, " +
            "       rf.RDB$DESCRIPTION, " +
            "       ft.RDB$COMPUTED_SOURCE, " +
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
            "       (SELECT TRIM(rc_uq.RDB$RELATION_NAME) " +
            "          FROM RDB$RELATION_CONSTRAINTS rc_fk " +
            "          JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = rc_fk.RDB$CONSTRAINT_NAME " +
            "          JOIN RDB$RELATION_CONSTRAINTS rc_uq ON rc_uq.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ " +
            "          JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc_fk.RDB$INDEX_NAME " +
            "          WHERE rc_fk.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
            "            AND rc_fk.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY' " +
            "            AND s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME " +
            "          ROWS 1) AS FK_TABLE, " +
            "       " + identityExpr + " AS IDENTITY_TYPE " +
            "FROM RDB$RELATION_FIELDS rf " +
            "JOIN RDB$FIELDS ft ON ft.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
            "WHERE rf.RDB$RELATION_NAME = @name " +
            "ORDER BY rf.RDB$FIELD_POSITION";
    }

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

    // FB3+ stores packaged procedures/functions in the SAME catalog tables as standalone
    // ones, keyed by the same RDB$*_NAME. Without excluding packaged rows, a packaged
    // routine that shares a name with a standalone one leaks into the top-level
    // Functions/Procedures nodes (a visible duplicate) AND its args merge into the
    // standalone one's reconstruction (→ -901 "duplicate specification"). RDB$PACKAGE_NAME
    // is FB3+ only, so the filter is gated on the server major (packages don't exist on 2.5,
    // so there's nothing to exclude there anyway).
    internal static string SqlFor(MetadataObjectKind kind) => SqlFor(kind, 0);

    internal static string SqlFor(MetadataObjectKind kind, int serverMajor)
    {
        if (serverMajor >= 3 && kind == MetadataObjectKind.Procedure)
            return "SELECT TRIM(RDB$PROCEDURE_NAME) FROM RDB$PROCEDURES " +
                   "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$PACKAGE_NAME IS NULL " +
                   "ORDER BY RDB$PROCEDURE_NAME";
        if (serverMajor >= 3 && kind == MetadataObjectKind.Function)
            return "SELECT TRIM(RDB$FUNCTION_NAME) FROM RDB$FUNCTIONS " +
                   "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$PACKAGE_NAME IS NULL " +
                   "ORDER BY RDB$FUNCTION_NAME";
        return SqlForBase(kind);
    }

    // Internal so tests can assert the SQL shape and FB-compat (no LIST(), no FB3-only syntax).
    private static string SqlForBase(MetadataObjectKind kind) => kind switch
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
            // Second column RDB$TRIGGER_INACTIVE drives the tree's active/inactive styling
            // and lets bulk activate/deactivate skip objects already in the target state.
            "SELECT TRIM(RDB$TRIGGER_NAME), RDB$TRIGGER_INACTIVE FROM RDB$TRIGGERS " +
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
            // Second column RDB$INDEX_INACTIVE drives active/inactive styling (same as triggers).
            "SELECT TRIM(RDB$INDEX_NAME), RDB$INDEX_INACTIVE FROM RDB$INDICES " +
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
