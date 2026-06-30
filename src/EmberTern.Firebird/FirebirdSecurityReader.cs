using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Security;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Reads the Firebird security catalog for the Security Manager: users
/// (<c>SEC$USERS</c>, server-level), roles (<c>RDB$ROLES</c>), and object /
/// membership privileges (<c>RDB$USER_PRIVILEGES</c>). Runs on the metadata lane
/// with the same capture-once command-lock pattern as the other readers
/// (gotchas #98 / #120); never opens its own transaction.
/// </summary>
public sealed class FirebirdSecurityReader
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    public FirebirdSecurityReader(FirebirdConnectionService connectionService)
        : this(connectionService, null)
    {
    }

    public FirebirdSecurityReader(FirebirdConnectionService connectionService, TransactionService? transactionService)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    private FbConnection LaneConnection()
        => _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
    private SemaphoreSlim LaneLock()
        => _transactionService?.CommandLock ?? _connectionService.CommandLock;
    private FbTransaction? LaneTx => _transactionService?.ActiveTransaction;

    // Object-type codes we surface in the Privileges grid; excludes charset (11) /
    // collation (17) noise and role-membership rows (handled separately).
    internal const string ObjectPrivilegesSql =
        "SELECT TRIM(p.RDB$RELATION_NAME), p.RDB$PRIVILEGE, p.RDB$GRANT_OPTION, " +
        "       TRIM(p.RDB$FIELD_NAME), p.RDB$USER_TYPE, p.RDB$OBJECT_TYPE " +
        "FROM RDB$USER_PRIVILEGES p " +
        "WHERE TRIM(p.RDB$USER) = @grantee AND p.RDB$PRIVILEGE <> 'M' " +
        "AND p.RDB$OBJECT_TYPE IN (0, 5, 7, 14, 15, 18)";

    internal const string MembershipSql =
        "SELECT TRIM(p.RDB$USER), p.RDB$USER_TYPE, TRIM(p.RDB$RELATION_NAME), p.RDB$GRANT_OPTION " +
        "FROM RDB$USER_PRIVILEGES p " +
        "WHERE p.RDB$PRIVILEGE = 'M' " +
        "ORDER BY p.RDB$USER, p.RDB$RELATION_NAME";

    internal const string UsersSql =
        "SELECT TRIM(SEC$USER_NAME), SEC$FIRST_NAME, SEC$MIDDLE_NAME, SEC$LAST_NAME, " +
        "       SEC$ACTIVE, SEC$ADMIN, CAST(SEC$DESCRIPTION AS VARCHAR(1000)), TRIM(SEC$PLUGIN) " +
        "FROM SEC$USERS ORDER BY SEC$USER_NAME";

    internal const string RolesSql =
        "SELECT TRIM(RDB$ROLE_NAME), TRIM(RDB$OWNER_NAME), CAST(RDB$DESCRIPTION AS VARCHAR(1000)) " +
        "FROM RDB$ROLES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY RDB$ROLE_NAME";

    /// <summary>Server-level users from <c>SEC$USERS</c>. Requires admin (or own-row)
    /// privileges; a failure surfaces as <see cref="MetadataReadException"/>.</summary>
    public async Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var connection = LaneConnection();
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = UsersSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = LaneTx;

            var users = new List<UserInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) continue;
                users.Add(new UserInfo
                {
                    UserName = reader.GetString(0).Trim(),
                    FirstName = Str(reader, 1),
                    MiddleName = Str(reader, 2),
                    LastName = Str(reader, 3),
                    Active = !reader.IsDBNull(4) && reader.GetBoolean(4),
                    Admin = !reader.IsDBNull(5) && reader.GetBoolean(5),
                    Description = Str(reader, 6),
                    Plugin = Str(reader, 7),
                });
            }
            return users;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read users: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Database roles from <c>RDB$ROLES</c> (non-system), with owner and
    /// description.</summary>
    public async Task<IReadOnlyList<RoleInfo>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var connection = LaneConnection();
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = RolesSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = LaneTx;

            var roles = new List<RoleInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) continue;
                roles.Add(new RoleInfo
                {
                    Name = reader.GetString(0).Trim(),
                    Owner = Str(reader, 1),
                    Description = Str(reader, 2),
                });
            }
            return roles;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read roles: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Object + column privileges granted to <paramref name="grantee"/>
    /// (excludes role membership). One <see cref="PrivilegeInfo"/> per catalog row.</summary>
    public async Task<IReadOnlyList<PrivilegeInfo>> ListPrivilegesAsync(
        GranteeRef grantee,
        CancellationToken cancellationToken = default)
    {
        if (grantee is null || string.IsNullOrWhiteSpace(grantee.Name))
            return Array.Empty<PrivilegeInfo>();

        var connection = LaneConnection();
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = ObjectPrivilegesSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = LaneTx;
            cmd.Parameters.AddWithValue("@grantee", grantee.Name.Trim());

            var rows = new List<PrivilegeInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                var objName = reader.GetString(0).Trim();
                var priv = reader.GetString(1).Trim();
                if (priv.Length == 0) continue;
                var grantOption = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                var column = Str(reader, 3);
                var userType = reader.IsDBNull(4) ? 8 : Convert.ToInt32(reader.GetValue(4));
                var objectType = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5));
                rows.Add(new PrivilegeInfo(
                    grantee.Name.Trim(),
                    SecurityCatalog.DecodeGranteeType(userType),
                    objName,
                    priv[0],
                    grantOption,
                    column,
                    SecurityCatalog.DecodeObjectKind(objectType)));
            }
            return rows;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read privileges for {grantee.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>All role-membership edges (<c>RDB$USER_PRIVILEGES</c> rows with
    /// privilege 'M'). The VM filters by member or role as needed.</summary>
    public async Task<IReadOnlyList<MembershipInfo>> ListMembershipAsync(CancellationToken cancellationToken = default)
    {
        var connection = LaneConnection();
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = MembershipSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = LaneTx;

            var rows = new List<MembershipInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(2)) continue;
                var member = reader.GetString(0).Trim();
                var userType = reader.IsDBNull(1) ? 8 : Convert.ToInt32(reader.GetValue(1));
                var role = reader.GetString(2).Trim();
                var grantOption = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                if (member.Length == 0 || role.Length == 0) continue;
                rows.Add(new MembershipInfo(
                    member,
                    SecurityCatalog.DecodeGranteeType(userType),
                    role,
                    grantOption == 2));
            }
            return rows;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read role membership: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private static string? Str(System.Data.Common.DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var s = reader.GetValue(ordinal)?.ToString();
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
