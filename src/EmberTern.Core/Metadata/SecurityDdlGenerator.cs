using System;
using System.Text;
using EmberTern.Core.Security;

namespace EmberTern.Core.Metadata;

/// <summary>
/// Pure (no I/O) generator for Firebird security DCL/DDL: users, roles, role
/// membership, and object/column privileges. All grammar here was verified
/// empirically against Firebird 5.0 (see the Security Manager milestone). Reuses
/// <see cref="DdlGenerator.Quote"/> for identifier quoting (only quotes when
/// needed, so SHOUTY_CASE names stay bare and match the catalog).
/// </summary>
public static class SecurityDdlGenerator
{
    // ─── Users (server-level: writes the security database) ────────────────

    /// <summary><c>CREATE USER "n" PASSWORD 'p' [FIRSTNAME …] [ACTIVE|INACTIVE]
    /// [GRANT ADMIN ROLE]</c>.</summary>
    public static string BuildCreateUser(UserInfo user, string password)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(user.UserName))
            throw new ArgumentException("User name is required.", nameof(user));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required for a new user.", nameof(password));

        var sb = new StringBuilder();
        sb.Append("CREATE USER ").Append(DdlGenerator.QuoteLight(user.UserName.Trim()));
        sb.Append(" PASSWORD ").Append(Literal(password));
        AppendNameClauses(sb, user);
        sb.Append(user.Active ? " ACTIVE" : " INACTIVE");
        if (user.Admin) sb.Append(" GRANT ADMIN ROLE");
        return sb.ToString();
    }

    /// <summary><c>ALTER USER "n" [PASSWORD 'p'] FIRSTNAME … {ACTIVE|INACTIVE}
    /// {GRANT|REVOKE} ADMIN ROLE</c>. Password is emitted only when
    /// <paramref name="newPassword"/> is non-empty (so an edit that leaves the
    /// password field blank keeps the existing password). Name/active/admin are
    /// always set to the target state (idempotent).</summary>
    public static string BuildAlterUser(UserInfo user, string? newPassword)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(user.UserName))
            throw new ArgumentException("User name is required.", nameof(user));

        var sb = new StringBuilder();
        sb.Append("ALTER USER ").Append(DdlGenerator.QuoteLight(user.UserName.Trim()));
        if (!string.IsNullOrEmpty(newPassword))
            sb.Append(" PASSWORD ").Append(Literal(newPassword));
        AppendNameClauses(sb, user);
        sb.Append(user.Active ? " ACTIVE" : " INACTIVE");
        sb.Append(user.Admin ? " GRANT ADMIN ROLE" : " REVOKE ADMIN ROLE");
        return sb.ToString();
    }

    /// <summary><c>DROP USER "n"</c>.</summary>
    public static string BuildDropUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("User name is required.", nameof(userName));
        return "DROP USER " + DdlGenerator.QuoteLight(userName.Trim());
    }

    /// <summary><c>COMMENT ON USER "n" IS …</c> (null/blank → IS NULL).
    /// Writes <c>SEC$DESCRIPTION</c>.</summary>
    public static string BuildCommentUser(string userName, string? comment)
        => BuildComment("USER", userName, comment);

    private static void AppendNameClauses(StringBuilder sb, UserInfo user)
    {
        // Always emit the three name clauses so an edit can also CLEAR a name
        // (FIRSTNAME '' wipes it). Trim, default null → ''.
        sb.Append(" FIRSTNAME ").Append(Literal(user.FirstName?.Trim() ?? string.Empty));
        sb.Append(" MIDDLENAME ").Append(Literal(user.MiddleName?.Trim() ?? string.Empty));
        sb.Append(" LASTNAME ").Append(Literal(user.LastName?.Trim() ?? string.Empty));
    }

    // ─── Roles (per-database) ──────────────────────────────────────────────

    /// <summary><c>CREATE ROLE "n"</c>.</summary>
    public static string BuildCreateRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name is required.", nameof(roleName));
        return "CREATE ROLE " + DdlGenerator.QuoteLight(roleName.Trim());
    }

    /// <summary><c>DROP ROLE "n"</c>.</summary>
    public static string BuildDropRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name is required.", nameof(roleName));
        return "DROP ROLE " + DdlGenerator.QuoteLight(roleName.Trim());
    }

    /// <summary><c>COMMENT ON ROLE "n" IS …</c> (null/blank → IS NULL).
    /// Writes <c>RDB$ROLES.RDB$DESCRIPTION</c>.</summary>
    public static string BuildCommentRole(string roleName, string? comment)
        => BuildComment("ROLE", roleName, comment);

    // ─── Role membership ───────────────────────────────────────────────────

    /// <summary><c>GRANT "ROLE" TO {USER "u" | "r"} [WITH ADMIN OPTION]</c>.</summary>
    public static string BuildGrantRole(string roleName, GranteeRef member, bool withAdminOption)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name is required.", nameof(roleName));
        var sb = new StringBuilder();
        sb.Append("GRANT ").Append(DdlGenerator.QuoteLight(roleName.Trim()));
        sb.Append(" TO ").Append(Grantee(member));
        if (withAdminOption) sb.Append(" WITH ADMIN OPTION");
        return sb.ToString();
    }

    /// <summary><c>REVOKE "ROLE" FROM {USER "u" | "r"}</c>.</summary>
    public static string BuildRevokeRole(string roleName, GranteeRef member)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name is required.", nameof(roleName));
        return "REVOKE " + DdlGenerator.QuoteLight(roleName.Trim()) + " FROM " + Grantee(member);
    }

    // ─── Object / column privileges ────────────────────────────────────────

    /// <summary>
    /// <c>GRANT &lt;priv&gt;[(col)] ON [&lt;kw&gt;] "obj" TO &lt;grantee&gt; [WITH GRANT OPTION]</c>.
    /// <paramref name="privilege"/> is the one-letter code (S/I/U/D/R/X/G);
    /// <paramref name="column"/> is non-null only for column-level UPDATE/REFERENCES.
    /// </summary>
    public static string BuildGrantPrivilege(
        PrivilegeObjectKind objectKind,
        string objectName,
        char privilege,
        GranteeRef grantee,
        string? column,
        bool withGrantOption)
    {
        var sb = new StringBuilder("GRANT ");
        AppendPrivilegeTarget(sb, objectKind, objectName, privilege, column);
        sb.Append(" TO ").Append(Grantee(grantee));
        if (withGrantOption) sb.Append(" WITH GRANT OPTION");
        return sb.ToString();
    }

    /// <summary><c>REVOKE &lt;priv&gt;[(col)] ON [&lt;kw&gt;] "obj" FROM &lt;grantee&gt;</c>.</summary>
    public static string BuildRevokePrivilege(
        PrivilegeObjectKind objectKind,
        string objectName,
        char privilege,
        GranteeRef grantee,
        string? column)
    {
        var sb = new StringBuilder("REVOKE ");
        AppendPrivilegeTarget(sb, objectKind, objectName, privilege, column);
        sb.Append(" FROM ").Append(Grantee(grantee));
        return sb.ToString();
    }

    private static void AppendPrivilegeTarget(
        StringBuilder sb,
        PrivilegeObjectKind objectKind,
        string objectName,
        char privilege,
        string? column)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required.", nameof(objectName));

        sb.Append(PrivilegeKeyword(privilege));
        if (!string.IsNullOrWhiteSpace(column))
            sb.Append('(').Append(DdlGenerator.QuoteLight(column.Trim())).Append(')');
        sb.Append(" ON ");
        var kw = ObjectKeyword(objectKind);
        if (kw.Length > 0) sb.Append(kw).Append(' ');
        sb.Append(DdlGenerator.QuoteLight(objectName.Trim()));
    }

    /// <summary>The GRANT verb keyword for a one-letter privilege code.</summary>
    public static string PrivilegeKeyword(char privilege) => privilege switch
    {
        'S' => "SELECT",
        'I' => "INSERT",
        'U' => "UPDATE",
        'D' => "DELETE",
        'R' => "REFERENCES",
        'X' => "EXECUTE",
        'G' => "USAGE",
        _ => throw new ArgumentException($"Unsupported privilege code '{privilege}'.", nameof(privilege)),
    };

    /// <summary>The <c>ON &lt;keyword&gt;</c> token for an object kind. Empty for a
    /// relation (table/view) — Firebird wants a bare <c>ON "T"</c> there.</summary>
    public static string ObjectKeyword(PrivilegeObjectKind kind) => kind switch
    {
        PrivilegeObjectKind.Relation => string.Empty,
        PrivilegeObjectKind.Procedure => "PROCEDURE",
        PrivilegeObjectKind.Function => "FUNCTION",
        PrivilegeObjectKind.Package => "PACKAGE",
        PrivilegeObjectKind.Sequence => "SEQUENCE",
        PrivilegeObjectKind.Exception => "EXCEPTION",
        _ => throw new ArgumentException($"Object kind {kind} is not grantable.", nameof(kind)),
    };

    // ─── Shared helpers ────────────────────────────────────────────────────

    /// <summary>Renders a grantee: <c>USER "n"</c> for a user, bare <c>"n"</c> for a
    /// role. The USER qualifier disambiguates a user from a same-named role and was
    /// verified live (<c>GRANT R TO USER U</c>).</summary>
    private static string Grantee(GranteeRef grantee)
    {
        if (grantee is null) throw new ArgumentNullException(nameof(grantee));
        if (string.IsNullOrWhiteSpace(grantee.Name))
            throw new ArgumentException("Grantee name is required.", nameof(grantee));
        var n = DdlGenerator.QuoteLight(grantee.Name.Trim());
        return grantee.Type == GranteeType.User ? "USER " + n : n;
    }

    private static string BuildComment(string objectType, string name, string? comment)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Object name is required.", nameof(name));
        var n = DdlGenerator.QuoteLight(name.Trim());
        return string.IsNullOrWhiteSpace(comment)
            ? $"COMMENT ON {objectType} {n} IS NULL"
            : $"COMMENT ON {objectType} {n} IS {Literal(comment)}";
    }

    // SQL string literal: 'text' with single quotes doubled. Null → ''.
    private static string Literal(string? text)
        => "'" + (text ?? string.Empty).Replace("'", "''") + "'";
}
