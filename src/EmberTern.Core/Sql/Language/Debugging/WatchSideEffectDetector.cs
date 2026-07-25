using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// Flags a Watch expression that is <b>not a pure value expression</b> — i.e. one that can have a side effect
/// when auto-re-evaluated after every step (spec §9.5: "an automatic watch must be flagged when it is not a
/// pure expression"). It reuses the one <see cref="SqlLexer"/> (no new parser — Developer Contract) to look
/// for a side-effecting keyword among the fragment's <b>tokens</b>: a keyword only matches as a bare token,
/// so a string literal (<c>'please UPDATE'</c>) or a quoted identifier (<c>"UPDATE"</c>) — whose raw
/// <see cref="SqlToken.Text"/> keeps its delimiters — never trips it.
/// <para>
/// It is deliberately conservative and lexical: over-flagging (a warning) is the safe direction, and it makes
/// no attempt at semantic analysis (a UDF with hidden side effects cannot be detected without executing —
/// that is inherently the server's domain). This is a warning cue, not a guarantee. A future, richer
/// pre-validation via <c>EditorLanguageService</c> is backlog, not this milestone.
/// </para>
/// </summary>
public static class WatchSideEffectDetector
{
    // The observable side-effect vocabulary — DML + EXECUTE (PROCEDURE/STATEMENT/BLOCK) + POST_EVENT. A pure
    // arithmetic / function / scalar-subquery (SELECT …) watch contains none of these; a plain assignment
    // (v = 5) is evaluated as a boolean comparison in Expression mode, so it is not listed (no side effect).
    private static readonly HashSet<string> SideEffectKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "EXECUTE", "POST_EVENT",
    };

    /// <summary>True when <paramref name="fragment"/> contains a side-effecting keyword token (so a Watch on
    /// it should be flagged as impure). False for a pure value expression.</summary>
    public static bool HasSideEffect(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return false;
        }
        foreach (var token in SqlLexer.Tokenize(fragment))
        {
            if (token.IsEndOfFile)
            {
                break;
            }
            if (SideEffectKeywords.Contains(token.Text))
            {
                return true;
            }
        }
        return false;
    }
}
