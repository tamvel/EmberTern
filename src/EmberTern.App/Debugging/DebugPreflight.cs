using System;
using System.Collections.Generic;
using EmberTern.App.Controls;
using EmberTern.App.Localization;
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
    /// <summary>How the shared <see cref="Controls.MessageBanner"/> renders this row: a blocking item is an
    /// error, anything else a warning the user may proceed past. Kept as the item's own decision so the view
    /// carries no severity logic (and no brush ever leaks into the data).</summary>
    public MessageSeverity BannerSeverity => IsBlocking ? MessageSeverity.Error : MessageSeverity.Warning;
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
    /// <param name="irreversible">
    /// ⭐ True when the source carries a §4.6 boundary — <c>IN AUTONOMOUS TRANSACTION</c> or generator use —
    /// i.e. an effect the debug session's rollback cannot undo.
    /// <para>It is an <c>out</c> of the SAME scan that produces the items, deliberately: the launch panel needs
    /// the sentences and the debug view needs the yes/no, and answering them from two scans would let the bar
    /// and the pre-flight list disagree about the very same code. ⛔ Do not re-derive this by looking for the
    /// warning TEXT in <paramref name="items"/> — that keys a data-safety decision on a localized string.</para>
    /// </param>
    public static IReadOnlyList<DebugPreflightItem> Scan(
        SemanticModel model, string source, bool hasStepPoints, out bool irreversible)
    {
        var items = new List<DebugPreflightItem>();
        irreversible = false;

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
                // ⭐ THE THIRD surface that shows a diagnostic's text — the debugger's launch pre-flight, which a
                // C5 inventory keyed on the word "diagnostic" did not see (the loop variable is `d`). Resolved
                // here, at the moment the item is built, like the other two. ⚠ No language hook and that is
                // measured, not overlooked: the launch panel's items are rebuilt by PrepareAsync on every launch
                // and Restart, and the panel is replaced by the session's surfaces once a run starts — so a
                // pre-flight list never outlives the operation that produced it.
                items.Add(new DebugPreflightItem(DebugPreflightSeverity.Warning, Loc.Format(d.Message)));
            }
        }

        var boundaries = ScanBoundaries(source);
        irreversible = boundaries.autonomous || boundaries.generator;

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
