namespace EmberTern.Core.Security;

/// <summary>Whether a privilege grantee (or membership member) is a user or a role.
/// Maps from <c>RDB$USER_PRIVILEGES.RDB$USER_TYPE</c> (8 = user, 13 = role).</summary>
public enum GranteeType
{
    User,
    Role,
}

/// <summary>The category of object a privilege applies to, decoded from
/// <c>RDB$USER_PRIVILEGES.RDB$OBJECT_TYPE</c>. Relation covers both tables and
/// views (object type 0) — they are distinguished by name against the metadata
/// listing, not by this code.</summary>
public enum PrivilegeObjectKind
{
    Relation,   // 0  — table or view
    Procedure,  // 5
    Function,   // 15
    Package,    // 18
    Sequence,   // 14 — generator
    Exception,  // 7
    Role,       // 13 — role membership ('M')
    Other,
}

/// <summary>A grantee (the left-hand side of GRANT/REVOKE): a user or a role.</summary>
public sealed record GranteeRef(string Name, GranteeType Type);

/// <summary>A server-level Firebird user (one row of <c>SEC$USERS</c>).
/// Users are global to the server's security database — not per-connected-DB.</summary>
public sealed class UserInfo
{
    public string UserName { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? MiddleName { get; init; }
    public string? LastName { get; init; }
    public bool Active { get; init; } = true;
    public bool Admin { get; init; }
    public string? Description { get; init; }
    public string? Plugin { get; init; }
}

/// <summary>A database role (one row of <c>RDB$ROLES</c>).</summary>
public sealed class RoleInfo
{
    public string Name { get; init; } = string.Empty;
    public string? Owner { get; init; }
    public string? Description { get; init; }
}

/// <summary>One raw privilege row decoded from <c>RDB$USER_PRIVILEGES</c>.
/// <para><see cref="Privilege"/>: S=SELECT I=INSERT U=UPDATE D=DELETE R=REFERENCES
/// X=EXECUTE G=USAGE M=role membership.</para>
/// <para><see cref="GrantOption"/>: 0=plain, 1=WITH GRANT OPTION, 2=WITH ADMIN
/// OPTION (membership).</para>
/// <para><see cref="ColumnName"/>: non-null only for column-level UPDATE/REFERENCES
/// grants.</para></summary>
public sealed record PrivilegeInfo(
    string Grantee,
    GranteeType GranteeType,
    string ObjectName,
    char Privilege,
    int GrantOption,
    string? ColumnName,
    PrivilegeObjectKind ObjectKind);

/// <summary>One role-membership edge (<c>RDB$USER_PRIVILEGES</c> rows with
/// privilege 'M'): <see cref="Member"/> is granted <see cref="RoleName"/>.</summary>
public sealed record MembershipInfo(
    string Member,
    GranteeType MemberType,
    string RoleName,
    bool WithAdminOption);

/// <summary>Static decoders for the Firebird security catalog integer/char codes.</summary>
public static class SecurityCatalog
{
    public static GranteeType DecodeGranteeType(int userType) => userType switch
    {
        13 => GranteeType.Role,
        _ => GranteeType.User, // 8 = user/PUBLIC; treat anything else as a user grantee
    };

    public static PrivilegeObjectKind DecodeObjectKind(int objectType) => objectType switch
    {
        0 => PrivilegeObjectKind.Relation,
        5 => PrivilegeObjectKind.Procedure,
        7 => PrivilegeObjectKind.Exception,
        13 => PrivilegeObjectKind.Role,
        14 => PrivilegeObjectKind.Sequence,
        15 => PrivilegeObjectKind.Function,
        18 => PrivilegeObjectKind.Package,
        _ => PrivilegeObjectKind.Other,
    };

    /// <summary>Human-readable name for a one-letter privilege code.</summary>
    public static string PrivilegeLabel(char privilege) => privilege switch
    {
        'S' => "SELECT",
        'I' => "INSERT",
        'U' => "UPDATE",
        'D' => "DELETE",
        'R' => "REFERENCES",
        'X' => "EXECUTE",
        'G' => "USAGE",
        'M' => "MEMBER",
        _ => privilege.ToString(),
    };
}
