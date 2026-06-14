namespace EmberTern.Core.Sql;

/// <summary>
/// Which transaction lane a free-text SQL statement should run on. The SQL Editor
/// uses this to auto-route a single Execute (F5): data operations to the Data lane
/// (connection #1, data profile), structural operations to the Metadata lane
/// (connection #2, metadata profile).
/// <para>
/// <see cref="Ambiguous"/> is reported when the leading token can't be classified
/// confidently (e.g. SET TERM, SET TRANSACTION, an unrecognised keyword, or empty
/// input). The caller routes Ambiguous to the Data lane — the safest choice
/// (read_committed + nowait never blocks tables or metadata). The three-valued enum
/// is kept so callers/tests can distinguish a confident Data verdict from a fallback.
/// </para>
/// </summary>
public enum StatementLane
{
    Data,
    Metadata,
    Ambiguous,
}

/// <summary>
/// Classifies a free-text SQL statement into a <see cref="StatementLane"/> by its
/// leading keyword(s). Pure string scanning — no SQL grammar, no Avalonia/Firebird
/// deps — mirroring the lightweight approach of <see cref="SqlFormatter"/>.
/// </summary>
/// <remarks>
/// Classification is by the FIRST significant statement only. The query executor
/// sends one command to the driver per Execute, so a multi-statement script run with
/// a single F5 is already a degenerate case; we classify by its leading statement.
/// <para>
/// EXECUTE BLOCK is classified as Data: Firebird PSQL cannot contain DDL inside a
/// block, and an EXECUTE BLOCK is a data/result-set construct. The one residual gap —
/// dynamic DDL via <c>EXECUTE STATEMENT 'CREATE …'</c> built from a variable — is
/// statically undecidable and vanishingly rare; it runs harmlessly on the Data lane.
/// </para>
/// </remarks>
public static class SqlStatementClassifier
{
    public static StatementLane Classify(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return StatementLane.Ambiguous;
        }

        int i = 0;
        var first = ReadKeyword(sql, ref i);
        if (first.Length == 0)
        {
            return StatementLane.Ambiguous;
        }

        switch (first.ToUpperInvariant())
        {
            // --- Data: reads + DML + procedure/block execution ---
            case "SELECT":
            case "WITH":      // CTE → always resolves to a SELECT
            case "INSERT":
            case "UPDATE":
            case "DELETE":
            case "MERGE":
            case "EXECUTE":   // EXECUTE PROCEDURE / EXECUTE BLOCK — see remarks
                return StatementLane.Data;

            // --- Metadata: DDL ---
            case "CREATE":    // incl. CREATE OR ALTER (leading token is CREATE)
            case "ALTER":
            case "DROP":
            case "RECREATE":
            case "COMMENT":   // COMMENT ON …
            case "DECLARE":   // DECLARE EXTERNAL FUNCTION / FILTER (top-level)
            // --- Metadata: DCL (permission structure) ---
            case "GRANT":
            case "REVOKE":
                return StatementLane.Metadata;

            case "SET":
            {
                // SET GENERATOR / SET STATISTICS are structural; SET TERM /
                // SET TRANSACTION / others are directives or session-level → ambiguous.
                var second = ReadKeyword(sql, ref i).ToUpperInvariant();
                return second switch
                {
                    "GENERATOR" or "STATISTICS" => StatementLane.Metadata,
                    _ => StatementLane.Ambiguous,
                };
            }

            default:
                return StatementLane.Ambiguous;
        }
    }

    // Skips leading whitespace + line/block comments, then reads one identifier run
    // (letters/digits/underscore/$). Advances <paramref name="i"/> past it. Returns
    // "" when only whitespace/comments remain.
    private static string ReadKeyword(string sql, ref int i)
    {
        SkipTrivia(sql, ref i);
        int start = i;
        while (i < sql.Length && IsIdentifierChar(sql[i]))
        {
            i++;
        }
        return sql.Substring(start, i - start);
    }

    private static void SkipTrivia(string sql, ref int i)
    {
        while (i < sql.Length)
        {
            char c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }
                i = i + 1 < sql.Length ? i + 2 : sql.Length;
                continue;
            }

            break;
        }
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
