using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

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
/// </summary>
public static class CursorBridge
{
    /// <summary>Builds the DSQL cursor query for <paramref name="loop"/> (which must have a
    /// <see cref="ForSelectStatement.Query"/>). Throws <see cref="InvalidOperationException"/> when it does
    /// not — a <c>FOR EXECUTE STATEMENT</c> / unrecognised cursor has no static SELECT to bridge (the caller
    /// surfaces that as a §F boundary).</summary>
    public static CursorQueryPlan Build(string source, ForSelectStatement loop)
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
