using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The Cursor Bridge (Stage X / D6, spec §7) — the <b>pure</b> half. A PSQL <c>FOR SELECT</c> cursor lives
/// inside one compiled PSQL request, but our steps run in separate <c>EXECUTE BLOCK</c>s, so a PSQL cursor
/// cannot survive between steps. The bridge maps it to a <b>real DSQL cursor</b> on the session connection:
/// the loop's cursor query is <em>just a SELECT</em>, held open and fetched one row per iteration
/// (<see cref="CursorQueryPlan"/> is executed by the Firebird half, <c>CursorHandle</c>).
/// <para>
/// This class builds the DSQL SELECT + its ordered bind parameters as a <b>pure function</b> of (source,
/// loop) — mirroring <see cref="HarnessBuilder"/>, testable without a server. A frame variable referenced in
/// the cursor query is rewritten <b>by span</b> (never text search) to a positional <c>?</c> parameter that
/// the executor binds from the current frame.
/// </para>
/// <para>
/// <b>Only the colon/at form is rewritten</b> (a <see cref="TokenKind.Parameter"/> token — <c>:name</c> /
/// <c>@name</c>), which is Firebird's <b>unambiguous</b> variable-reference syntax inside a query and a
/// <em>native DSQL bind parameter</em> once the query is extracted. A <em>bare</em> identifier in a query is a
/// <b>column</b> reference in DSQL — and deliberately NOT rewritten here, even though the binder resolves a
/// bare name that shadows a frame variable/parameter to the variable (locals shadow columns in its resolution
/// order). Rewriting those broke live: a routine that both <c>RETURNS (LINE_NO …)</c> and does
/// <c>SELECT LINE_NO …</c> mis-rewrote the <em>column</em> to <c>?</c> → SQL -804 "Data type unknown" (§15.5).
/// Trusting the colon form matches Firebird's own disambiguation and never mangles a column (§F: correctness
/// over reach — a bare local ref that Firebird would resolve as a variable is rare, ambiguous, and surfaces as
/// an honest step-level "column unknown" if it cannot bind, never a silent wrong result).
/// </para>
/// <para>
/// ⭐⭐ <b>A TRIGGER's <c>NEW.col</c> / <c>OLD.col</c> is bridged the same way</b> (P6, 2026-08-07), which
/// removed D10's refusal ("a FOR SELECT cursor that references NEW/OLD is not supported — step over the
/// loop"). That refusal rested on a true premise with the wrong conclusion: it is correct that the harness's
/// synthetic context variables do not exist inside a separately-opened DSQL cursor — but the cursor never
/// needed them, because <c>NEW.col</c> in a cursor query is a VALUE, and the frame already holds it. So the
/// reference is rewritten to a positional <c>?</c> and bound from the frame, exactly like <c>:variable</c>.
/// No new mechanism: the same span-driven rewrite, the same parameter list, the same binder.
/// </para>
/// <para>
/// ⚠ <b>Binding at OPEN is what Firebird itself does</b>, and that is why this is faithful rather than merely
/// convenient: a compiled trigger evaluates its cursor's parameters when the cursor opens, so a body that
/// assigns <c>NEW.col</c> DURING the loop does not change the rows an already-open cursor returns. Reading
/// the frame once, at open, reproduces that — re-reading per fetch would be the unfaithful choice.
/// </para>
/// </summary>
public static class CursorBridge
{
    /// <summary>Builds the DSQL cursor query for <paramref name="loop"/> (which must have a
    /// <see cref="ForSelectStatement.Query"/>). Throws <see cref="InvalidOperationException"/> when it does
    /// not — a <c>FOR EXECUTE STATEMENT</c> / unrecognised cursor has no static SELECT to bridge (the caller
    /// surfaces that as a §F boundary).
    /// <para>
    /// ⭐⭐ <paramref name="model"/> + <paramref name="context"/> are supplied for a TRIGGER frame, and they are
    /// what lifted D10's <c>NEW</c>/<c>OLD</c> refusal (P6, 2026-08-07). See the class remarks.
    /// </para></summary>
    public static CursorQueryPlan Build(
        string source, ForSelectStatement loop,
        SemanticModel? model = null, TriggerContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(loop);
        if (loop.Query is null)
            throw new InvalidOperationException("CursorBridge: the FOR loop has no static cursor query (FOR EXECUTE STATEMENT / unrecognised).");

        var query = loop.Query;
        int qStart = Math.Clamp(query.Start, 0, source.Length);
        int qEnd = Math.Clamp(query.Start + query.Length, qStart, source.Length);

        // Each :name / @name parameter token inside the cursor query, in source order — the unambiguous
        // frame-variable references (the lexer already classified them; not a structural token scan).
        var refs = new List<(int Start, int End, string Name)>();
        foreach (var tok in query.Tokens)
        {
            if (tok.Kind != TokenKind.Parameter) continue;
            if (tok.Start < qStart || tok.Start >= qEnd) continue;
            refs.Add((tok.Start, tok.End, tok.Text.TrimStart(':', '@').ToUpperInvariant()));
        }

        // ⭐ A trigger's NEW.col / OLD.col references join the SAME list — they are frame values, so they
        // bind exactly like a :variable. The pairing comes from ContextSubstitution, which already owns
        // "what is a context reference and which synthetic frame variable holds it"; the bridge only
        // decides that here the reference becomes a `?` rather than a name.
        if (model is not null && context is not null)
        {
            foreach (var occurrence in ContextSubstitution.ReferencesIn(
                         model, TextSpan.FromBounds(qStart, qEnd), context))
            {
                refs.Add((occurrence.Span.Start, occurrence.Span.End, occurrence.Synthetic));
            }
        }

        refs.Sort((a, b) => a.Start.CompareTo(b.Start));

        var sb = new StringBuilder(qEnd - qStart + 16);
        var names = new List<string>(refs.Count);
        int cursor = qStart;
        foreach (var (start, end, name) in refs)
        {
            if (start < cursor) continue; // defensive: overlapping span (should not happen — tokens are disjoint)
            sb.Append(source, cursor, start - cursor);
            sb.Append('?');
            names.Add(name);
            cursor = Math.Min(end, qEnd);
        }
        sb.Append(source, cursor, qEnd - cursor);

        return new CursorQueryPlan(sb.ToString(), names, loop.IntoTargets);
    }
}

/// <summary>The executable plan for a <c>FOR SELECT</c> cursor: the DSQL <see cref="Sql"/> (colon/@ frame
/// refs rewritten to positional <c>?</c>), the ordered <see cref="ParameterNames"/> to bind from the frame,
/// and the <see cref="IntoTargets"/> the fetched columns map onto positionally.</summary>
public sealed record CursorQueryPlan(
    string Sql,
    IReadOnlyList<string> ParameterNames,
    IReadOnlyList<string> IntoTargets);
