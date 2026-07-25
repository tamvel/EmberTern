using System.Collections.Generic;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>The facts a debugger launch needs from a trigger's <c>CREATE</c> header (spec §8.1): the table it
/// fires <b>for</b>, its <b>timing</b> (BEFORE/AFTER), and the DML <b>events</b> it declares (a multi-action
/// trigger declares several — the user picks one to simulate). Pure value.</summary>
public sealed record TriggerHeader(string TargetTable, TriggerTiming Timing, IReadOnlyList<TriggerEvent> Events);

/// <summary>
/// Reads a relation trigger's header facts from its parsed <see cref="DdlStatement"/> (Stage X / D10). Kept in
/// Core, beside <see cref="TriggerContext"/>, so <b>all</b> trigger-domain knowledge — the availability rules
/// <i>and</i> the header parse — lives in one place and the UI never re-derives it (the launch panel's action
/// selector reads <see cref="TriggerHeader.Events"/>, and NEW/OLD availability comes from
/// <see cref="TriggerContext"/>). Token-driven, not text search.
/// <para>
/// Returns <c>null</c> for anything that is not a debuggable relation trigger — a database-level trigger
/// (<c>ON CONNECT</c>/<c>DISCONNECT</c>/<c>TRANSACTION …</c>) or a DDL trigger (<c>BEFORE ANY DDL STATEMENT</c>,
/// <c>CREATE TABLE</c>, …) has no target table or no DML event, so the caller refuses it clearly (§8.1: those are
/// out of scope), rather than half-building a launch.</para>
/// </summary>
public static class TriggerHeaderReader
{
    public static TriggerHeader? Read(DdlStatement? trigger)
    {
        if (trigger is null || trigger.ObjectKind != DdlObjectKind.Trigger)
        {
            return null;
        }

        var tokens = trigger.Tokens;
        string? table = null;
        TriggerTiming? timing = null;
        var events = new List<TriggerEvent>();

        // Match header words by TEXT, not by token kind: Firebird lexes the reserved words (FOR/INSERT/UPDATE/
        // DELETE/AS) as Keyword but the non-reserved BEFORE/AFTER/ACTIVE as Identifier, so a kind filter would
        // drop the timing. The table name is still required to be an identifier (IsName), so a header word can
        // never be mistaken for it.
        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            switch (tok.Text.ToUpperInvariant())
            {
                case "FOR":
                case "ON":
                    // The target table is the name after FOR / ON. A database-level event (ON CONNECT / …) is a
                    // keyword, not a name, so it is never captured here — leaving `table` null → out of scope.
                    if (table is null && i + 1 < tokens.Count && IsName(tokens[i + 1]))
                    {
                        table = FoldName(tokens[i + 1]);
                    }
                    break;

                case "BEFORE":
                    timing ??= TriggerTiming.Before;
                    break;
                case "AFTER":
                    timing ??= TriggerTiming.After;
                    break;

                case "INSERT": AddEvent(events, TriggerEvent.Insert); break;
                case "UPDATE": AddEvent(events, TriggerEvent.Update); break;
                case "DELETE": AddEvent(events, TriggerEvent.Delete); break;

                case "AS":
                    // The header ends at the body's AS — stop before the body (whose own DML must not be read as
                    // trigger events; e.g. an AFTER-UPDATE trigger whose body does INSERT INTO AUDIT_LOG).
                    i = tokens.Count;
                    break;
            }
        }

        // A relation trigger needs all three; anything missing is a DB-level / DDL trigger (out of scope, §8.1).
        if (table is null || timing is null || events.Count == 0)
        {
            return null;
        }
        return new TriggerHeader(table, timing.Value, events);
    }

    private static bool IsName(SqlToken tok) => tok.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier;

    // Fold to Firebird's catalog convention: an unquoted identifier is uppercased (matching RDB$ and the
    // metadata query's ToUpperInvariant); a quoted identifier keeps its case. (A quoted, case-sensitive table
    // remains a documented §F boundary, as elsewhere in the debugger.)
    private static string FoldName(SqlToken tok)
        => tok.Kind == TokenKind.QuotedIdentifier ? tok.Value : tok.Value.ToUpperInvariant();

    private static void AddEvent(List<TriggerEvent> events, TriggerEvent e)
    {
        if (!events.Contains(e)) events.Add(e);
    }
}
