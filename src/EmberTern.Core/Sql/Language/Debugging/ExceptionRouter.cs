using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// Routes a raised <see cref="DebugError"/> through the call stack — the client-owned half of exception
/// control flow (spec §3.6). It is pure control flow: it matches a <c>WHEN … DO</c> handler read from the
/// AST (<see cref="WhenHandler"/> / <see cref="WhenCondition"/> — never re-parsed) and unwinds frames until
/// one catches. It never evaluates an expression, coerces a type, or interprets Firebird error semantics —
/// the error's identity (name / gds / sql / sqlstate) is what the driver already reported on the
/// <see cref="DebugError"/>; matching it against a handler's declared class is the interpreter's job.
/// <para>
/// Matching order is Firebird's: within a frame the innermost active block first; within a block its
/// handlers top-to-bottom; within a handler its conditions left-to-right (spec §3.6). A frame that fails to
/// catch is rolled back to its entry savepoint (§4.5 — a simulated frame's side effects are undone
/// atomically, exactly as a real call would be) and popped; routing continues in the caller. When no frame
/// catches, every frame — the root included — is rolled back, and the session faults.
/// </para>
/// </summary>
internal static class ExceptionRouter
{
    /// <summary>
    /// Routes <paramref name="error"/> through <paramref name="frames"/> (innermost last). Returns true when
    /// a frame caught it: the catching frame's control stack has been repositioned so the handler body is the
    /// next thing to run, and no frame at or below the catcher was rolled back (a <c>WHEN</c>-handling block's
    /// prior statements survive, §4.5). Returns false when nothing caught it: every frame has been rolled
    /// back to its savepoint and removed from <paramref name="frames"/>, and the session should fault.
    /// </summary>
    public static bool TryRoute(List<Frame> frames, DebugError error, IDebugExecutor executor)
    {
        while (frames.Count > 0)
        {
            var frame = frames[^1];
            if (TryHandleInFrame(frame, error))
            {
                return true;
            }

            // Unhandled in this frame — undo its side effects and propagate to the caller (spec §4.5).
            frame.CloseOpenCursors();
            executor.RollbackFrameSavepoint(frame.SavepointName);
            frames.RemoveAt(frames.Count - 1);
        }
        return false;
    }

    // Searches one frame's control stack from the innermost activation outward for a block whose WHEN
    // handlers match. On a match: abandons the inner activations (closing their cursors), completes the
    // catching block's remaining statements, marks it handling (so its own handler body cannot re-enter it),
    // and pushes the handler body as the next branch to run.
    private static bool TryHandleInFrame(Frame frame, DebugError error)
    {
        var control = frame.Control;
        for (int i = control.Count - 1; i >= 0; i--)
        {
            if (control[i] is not SequenceActivation { Block: { } block, HandlerActive: false } seq)
            {
                continue;
            }
            foreach (var handler in block.Handlers)
            {
                if (!Matches(handler, error))
                {
                    continue;
                }
                while (!ReferenceEquals(frame.Top, seq))
                {
                    frame.PopForUnwind();
                }
                seq.Index = seq.Items.Count; // the block's statements after the raise point are skipped
                seq.HandlerActive = true;    // this block cannot catch again while its handler runs
                frame.PushBranch(handler.Body);
                return true;
            }
        }
        return false;
    }

    // ── Handler matching — read from the AST, compared against the driver-reported error identity ──────

    // A handler matches when ANY of its conditions matches (they share one DO body); conditions are tried
    // left-to-right, but for a boolean "does it catch" the order is immaterial.
    private static bool Matches(WhenHandler handler, DebugError error)
    {
        foreach (var cond in handler.Conditions)
        {
            if (Matches(cond, error))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Matches(WhenCondition cond, DebugError error) => cond.Kind switch
    {
        WhenHandlerKind.Any => true,
        WhenHandlerKind.ExceptionName =>
            cond.ExceptionName is { } name && error.ExceptionName is { } raised
            && string.Equals(name, raised, StringComparison.OrdinalIgnoreCase),
        WhenHandlerKind.GdsCode => MatchesGds(cond, error),
        WhenHandlerKind.SqlCode =>
            Operand(cond) is { } op
            && int.TryParse(op, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code)
            && error.SqlCode == code,
        WhenHandlerKind.SqlState =>
            error.SqlState is { } state && SqlStateLiteral(cond) is { } lit
            && string.Equals(lit, state, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    // GDSCODE takes either a symbolic name (WHEN GDSCODE lock_conflict) or a raw number
    // (WHEN GDSCODE 335544345) — try the number first, else the symbol.
    private static bool MatchesGds(WhenCondition cond, DebugError error)
    {
        var op = Operand(cond);
        if (op is null)
        {
            return false;
        }
        if (long.TryParse(op, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
        {
            return error.GdsCode == num;
        }
        return error.GdsCodeSymbol is { } sym && string.Equals(op, sym, StringComparison.OrdinalIgnoreCase);
    }

    // The condition's operand text = the concatenated Text of its tokens after the leading keyword (ANY /
    // EXCEPTION / GDSCODE / SQLCODE / SQLSTATE), trivia excluded so a signed SQLCODE joins tightly ("-913").
    // Null when the condition has no operand (WHEN ANY). This reads the operand P1 deliberately left in the
    // condition's tokens (only an EXCEPTION name is surfaced as a field) — reading a leaf value, not
    // re-deriving structure the AST already owns.
    private static string? Operand(WhenCondition cond)
    {
        var tokens = cond.Tokens;
        if (tokens.Count <= 1)
        {
            return null;
        }
        var sb = new StringBuilder();
        for (int i = 1; i < tokens.Count; i++)
        {
            sb.Append(tokens[i].Text);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    // The SQLSTATE operand as its bare value — a string literal 'HHTTT' with the quotes stripped and doubled
    // '' collapsed. Falls back to the raw operand text when it is not a string literal (mid-edit).
    private static string? SqlStateLiteral(WhenCondition cond)
    {
        var tokens = cond.Tokens;
        if (tokens.Count > 1 && tokens[1].Kind == TokenKind.StringLiteral)
        {
            var raw = tokens[1].Text;
            if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
            {
                return raw.Substring(1, raw.Length - 2).Replace("''", "'");
            }
        }
        return Operand(cond);
    }
}
