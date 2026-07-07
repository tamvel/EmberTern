using System;
using System.Collections.Generic;
using EmberTern.Core.Scripting;
using FirebirdSql.Data.Isql;

namespace EmberTern.Firebird;

/// <summary>
/// Splits a full SQL script into individual executable statements by delegating to the
/// managed driver's <see cref="FbScript"/> (its <c>SqlStringParser</c>) — so SET TERM,
/// PSQL bodies (procedures / triggers / functions / packages), EXECUTE BLOCK, string
/// literals and comments are all handled correctly, and each statement is classified via
/// the driver's <see cref="SqlStatementType"/>. This deliberately does NOT reuse
/// <see cref="FirebirdDdlExecutor"/>'s custom splitter, which has no SET TERM support and
/// is only meant for EmberTern's own single-object Compile payloads.
///
/// Returns Core <see cref="ScriptStatement"/> DTOs — all driver types stay inside this
/// class (the one-way layering rule). Parse is offline: <c>FbScript.Parse()</c> needs no
/// database connection.
/// </summary>
public sealed class FirebirdScriptParser
{
    /// <summary>
    /// Parses <paramref name="script"/> into ordered executable statements (SET TERM
    /// directives are consumed by the driver and never returned; empty/blank statements are
    /// dropped). Each statement's <see cref="ScriptStatement.SourceOffset"/> is a best-effort
    /// character offset located by a forward search of the original text (-1 if not found).
    /// Propagates the driver's parse exception on a malformed script.
    /// </summary>
    public IReadOnlyList<ScriptStatement> Parse(string script)
    {
        var result = new List<ScriptStatement>();
        if (string.IsNullOrWhiteSpace(script)) return result;

        var fbScript = new FbScript(script);
        fbScript.Parse();

        int cursor = 0;
        foreach (FbStatement statement in fbScript.Results)
        {
            var trimmed = (statement.Text ?? string.Empty).Trim();
            if (trimmed.Length == 0) continue;
            var (offset, length) = Locate(script, trimmed, ref cursor);
            result.Add(new ScriptStatement(trimmed, MapKind(statement.StatementType), offset, length));
        }
        return result;
    }

    // Forward search for the (verbatim) statement text in the original script. FbScript.Text
    // is a substring of the source minus its terminator, so an ordinal IndexOf from a running
    // cursor locates each statement in order (and disambiguates duplicate statements). Best-effort:
    // any mismatch yields (-1, 0) and navigation degrades gracefully.
    private static (int Offset, int Length) Locate(string script, string trimmed, ref int cursor)
    {
        int idx = script.IndexOf(trimmed, cursor, StringComparison.Ordinal);
        if (idx < 0) return (-1, 0);
        cursor = idx + trimmed.Length;
        return (idx, trimmed.Length);
    }

    // Maps the driver's fine-grained statement type onto the coarse script category. Internal
    // + InternalsVisibleTo("EmberTern.Tests") so the mapping is directly unit-pinnable, though
    // the parser is normally tested end-to-end through Parse.
    internal static ScriptStatementKind MapKind(SqlStatementType type) => type switch
    {
        SqlStatementType.Select => ScriptStatementKind.Select,

        SqlStatementType.Insert or SqlStatementType.Update or SqlStatementType.Delete
            or SqlStatementType.Merge or SqlStatementType.InsertCursor => ScriptStatementKind.Dml,

        SqlStatementType.ExecuteProcedure => ScriptStatementKind.ExecuteProcedure,
        SqlStatementType.ExecuteBlock => ScriptStatementKind.ExecuteBlock,

        SqlStatementType.Commit or SqlStatementType.Rollback
            or SqlStatementType.SetTransaction => ScriptStatementKind.TransactionControl,

        SqlStatementType.Connect or SqlStatementType.Disconnect
            or SqlStatementType.CreateDatabase or SqlStatementType.DropDatabase
            or SqlStatementType.SetDatabase or SqlStatementType.SetNames
            or SqlStatementType.SetSQLDialect or SqlStatementType.SetAutoDDL
            or SqlStatementType.ShowSQLDialect => ScriptStatementKind.SessionControl,

        // Everything DDL-shaped (Create*/Alter*/Drop*/Recreate*/Declare*/Grant/Revoke/
        // CommentOn/CreateGenerator/SetGenerator/SetStatistics/…) participates in the tx.
        _ => IsDdl(type) ? ScriptStatementKind.Ddl : ScriptStatementKind.Unknown,
    };

    private static bool IsDdl(SqlStatementType type)
    {
        var name = type.ToString();
        return name.StartsWith("Create", StringComparison.Ordinal)
            || name.StartsWith("Alter", StringComparison.Ordinal)
            || name.StartsWith("Drop", StringComparison.Ordinal)
            || name.StartsWith("Recreate", StringComparison.Ordinal)
            || name.StartsWith("Declare", StringComparison.Ordinal)
            || type is SqlStatementType.Grant or SqlStatementType.Revoke
                or SqlStatementType.CommentOn or SqlStatementType.SetGenerator
                or SqlStatementType.SetStatistics;
    }
}
