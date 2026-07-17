using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Debugging;

/// <summary>Severity of a <see cref="DebugPreflightItem"/> — mapped to a theme brush by the view.</summary>
public enum DebugPreflightSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One line of the launch panel's pre-flight report (spec §9.2). <see cref="IsBlocking"/> items
/// prevent the session from starting (there is nothing to debug); warnings are surfaced but the user may
/// proceed.</summary>
public sealed record DebugPreflightItem(DebugPreflightSeverity Severity, string Message, bool IsBlocking = false)
{
    /// <summary>A severity glyph so the row conveys severity without a colour (rule #1: no brush leaks into
    /// data). ⛔ blocking / error, ⚠ warning, ℹ info.</summary>
    public string Glyph => Severity switch
    {
        DebugPreflightSeverity.Error => "⛔",
        DebugPreflightSeverity.Warning => "⚠",
        _ => "ℹ",
    };
}

/// <summary>
/// The launch panel's pre-flight scan (Stage X / D4, spec §9.2 + §4.6). Tells the user what a run cannot
/// promise <b>before</b> it starts: unresolved names (reusing the existing <see cref="DiagnosticsEngine"/> —
/// the debugger adds no analysis), plus the two §4.6 data-safety boundaries that survive the debug rollback
/// (IN AUTONOMOUS TRANSACTION, generator/sequence use), and the §F "no step points" refusal.
/// <para>
/// The §4.6 detection is a deliberately conservative <b>lexical</b> scan (keyword sequences over the
/// <see cref="SqlLexer"/> token stream, so matches inside string literals and comments are excluded) — it is
/// a safety <i>warning</i>, not structural analysis, and never suppresses a launch. Pure: no server, no
/// re-parse of structure (Developer Contract #1 concerns structure; this is a lexical heuristic that names a
/// boundary, and over-warning is safe while under-warning is a §F hazard).
/// </para>
/// </summary>
internal static class DebugPreflight
{
    public static IReadOnlyList<DebugPreflightItem> Scan(SemanticModel model, string source, bool hasStepPoints)
    {
        var items = new List<DebugPreflightItem>();

        if (!hasStepPoints)
        {
            items.Add(new DebugPreflightItem(
                DebugPreflightSeverity.Error, UiStrings.DebuggerPreflightUnsteppable, IsBlocking: true));
            // A routine with no step points cannot be debugged; the boundary scan below is moot.
            return items;
        }

        // Reuse the diagnostics engine for unresolved names (Error severity only — Info/Warning is editor
        // noise here). Non-blocking: an "unknown object" is usually metadata-not-loaded in this read-only
        // view, and the routine itself compiled, so we surface but never block.
        foreach (var d in DiagnosticsEngine.Analyze(model))
        {
            if (d.Severity == DiagnosticSeverity.Error)
            {
                items.Add(new DebugPreflightItem(DebugPreflightSeverity.Warning, d.Message));
            }
        }

        var boundaries = ScanBoundaries(source);
        if (boundaries.autonomous)
        {
            items.Add(new DebugPreflightItem(DebugPreflightSeverity.Warning, UiStrings.DebuggerPreflightAutonomousTx));
        }
        if (boundaries.generator)
        {
            items.Add(new DebugPreflightItem(DebugPreflightSeverity.Warning, UiStrings.DebuggerPreflightGenerator));
        }

        return items;
    }

    // Conservative keyword-sequence scan over the token stream: IN AUTONOMOUS TRANSACTION, and generator use
    // (GEN_ID, or NEXT VALUE FOR). Strings/comments are excluded by construction (the lexer emits them as
    // their own kinds / as trivia), so a literal 'IN AUTONOMOUS TRANSACTION' text never false-matches.
    private static (bool autonomous, bool generator) ScanBoundaries(string source)
    {
        var tokens = SqlLexer.Tokenize(source);
        bool autonomous = false, generator = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind is not (TokenKind.Identifier or TokenKind.Keyword)) continue;

            if (Is(t, "GEN_ID"))
            {
                generator = true;
            }
            else if (Is(t, "AUTONOMOUS") && i > 0 && Is(tokens[i - 1], "IN"))
            {
                autonomous = true;
            }
            else if (Is(t, "NEXT") && i + 2 < tokens.Count && Is(tokens[i + 1], "VALUE") && Is(tokens[i + 2], "FOR"))
            {
                generator = true;
            }
        }

        return (autonomous, generator);
    }

    private static bool Is(SqlToken token, string keyword)
        => string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase);
}
