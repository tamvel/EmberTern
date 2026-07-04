using EmberTern.Core.Sql;

namespace EmberTern.Core.Trace;

/// <summary>
/// The kind of operation a traced <see cref="TraceEventKind.Statement"/> performs, derived
/// from its leading SQL keyword. Lets the grid show "UPDATE" instead of a generic "Statement"
/// and powers the operation filter (SELECT / INSERT / UPDATE / DELETE / EXECUTE / DDL). Non-
/// statement events (procedures/triggers/functions) don't carry one.
/// </summary>
public enum TraceSqlOperation
{
    /// <summary>Not a statement, or the SQL couldn't be classified.</summary>
    None,
    Select,
    Insert,
    Update,
    Delete,
    Merge,
    /// <summary>EXECUTE PROCEDURE / EXECUTE BLOCK.</summary>
    Execute,
    /// <summary>CREATE / ALTER / DROP / RECREATE / GRANT / REVOKE / COMMENT / SET — schema/DCL.</summary>
    Ddl,
    /// <summary>A recognised statement whose verb isn't one of the above.</summary>
    Other,
}

/// <summary>
/// Classifies a traced SQL statement by its leading keyword (comment/whitespace-tolerant via
/// the shared <see cref="SqlScanHelpers"/> scanner). Pure, textual, no grammar — the same
/// lightweight approach as <see cref="SqlStatementClassifier"/>, but finer-grained (that one
/// answers Data-vs-Metadata lane; this one names the operation for display + filtering).
/// </summary>
public static class TraceSqlOperationClassifier
{
    /// <summary>Returns the operation for a statement's SQL, or <see cref="TraceSqlOperation.None"/>
    /// for null/blank/unreadable input.</summary>
    public static TraceSqlOperation Classify(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return TraceSqlOperation.None;

        int i = 0;
        SqlScanHelpers.SkipTrivia(sql!, ref i);
        if (i >= sql!.Length || !IsWordStart(sql[i])) return TraceSqlOperation.None;

        var w = SqlScanHelpers.ReadWord(sql, ref i).ToUpperInvariant();
        return w switch
        {
            "SELECT" => TraceSqlOperation.Select,
            "WITH" => TraceSqlOperation.Select,      // CTE → a SELECT for display purposes
            "INSERT" => TraceSqlOperation.Insert,
            "UPDATE" => TraceSqlOperation.Update,
            "DELETE" => TraceSqlOperation.Delete,
            "MERGE" => TraceSqlOperation.Merge,
            "EXECUTE" => TraceSqlOperation.Execute, // EXECUTE PROCEDURE / EXECUTE BLOCK
            "CREATE" or "ALTER" or "DROP" or "RECREATE" or "COMMENT"
                or "GRANT" or "REVOKE" or "SET" or "DECLARE" => TraceSqlOperation.Ddl,
            _ => TraceSqlOperation.Other,
        };
    }

    /// <summary>The short upper-case label shown in the grid's Event column for a statement
    /// (e.g. "UPDATE"). Returns empty for <see cref="TraceSqlOperation.None"/>.</summary>
    public static string Label(TraceSqlOperation op) => op switch
    {
        TraceSqlOperation.None => string.Empty,
        TraceSqlOperation.Ddl => "DDL",
        _ => op.ToString().ToUpperInvariant(),
    };

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';
}
