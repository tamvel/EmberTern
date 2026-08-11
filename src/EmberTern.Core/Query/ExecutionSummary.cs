using System;
using System.Collections.Generic;
using EmberTern.Core.Localization;

namespace EmberTern.Core.Query;

/// <summary>The IBExpert-style one-line summary for a non-result statement (DML / EXECUTE
/// PROCEDURE / EXECUTE BLOCK): the rows it changed (insert / update / delete, from the MON$
/// before/after delta) + elapsed time. When the change counts weren't measured it falls back to
/// the driver's total <see cref="RecordsAffected"/>. The transaction-profile line ("Executed via
/// Data profile (Read Committed)") is logged separately by the VM. Pure — <see cref="BuildMessage()"/>
/// is unit-tested. (SELECT statements keep their existing "N rows in T ms" line.)
///
/// <para>⭐ <b>Etap C6 — every method comes in two overloads, and the layout is shared.</b> The no-argument
/// form renders English through <see cref="ExecutionEnglish"/>; the form taking a resolver renders whatever
/// language the App is in. Both walk the SAME composition code, so the two halves can differ only in their
/// WORDS — which is what makes the equality guard a real proof rather than a comparison of two transcriptions
/// (the C4b lesson). ⛔ Do not add a second composer for the localized path.</para>
///
/// <para>⚠ <b>A count is always argument {0}</b> of the message that carries it (ratified R3), because that
/// is what lets the App choose a plural category without Core knowing any grammar. It is read in exactly one
/// place, <see cref="LocalizableMessage.TryGetCount"/>.</para>
/// </summary>
public sealed record ExecutionSummary
{
    public long Inserts { get; init; }

    public long Updates { get; init; }

    public long Deletes { get; init; }

    /// <summary>Driver total affected rows (fallback when the MON$ delta wasn't captured, and
    /// null for procedures/blocks the driver doesn't count).</summary>
    public int? RecordsAffected { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>True when a MON$ before/after delta produced the per-operation change counts;
    /// false → fall back to <see cref="RecordsAffected"/>.</summary>
    public bool ChangesMeasured { get; init; }

    /// <summary>Total rows read (sequential + index) across all tables, from the MON$ delta.</summary>
    public long RowsRead { get; init; }

    /// <summary>True when per-table reads were captured (so <see cref="RowsRead"/> is meaningful).</summary>
    public bool ReadsMeasured { get; init; }

    public long TotalChanges => Inserts + Updates + Deletes;

    // ⚠ The separator between terms is punctuation, not language — class D in the migration's
    // classification, exactly like a file path or a SQL keyword. Keying it would invite a translation of
    // "·" and buy nothing.
    private const string TermSeparator = " · ";

    /// <summary>The Messages/status line, e.g. "inserted 8 · updated 16 · deleted 8 in 93 ms",
    /// or "0 rows affected in 4 ms" when nothing was changed / measured. Zero terms are omitted.</summary>
    public string BuildMessage() => BuildMessage(ExecutionEnglish.Resolve);

    /// <inheritdoc cref="BuildMessage()"/>
    /// <param name="resolve">Turns a message into text in the reader's language.</param>
    public string BuildMessage(Func<LocalizableMessage, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        long ms = (long)Elapsed.TotalMilliseconds;
        string terms;
        if (ChangesMeasured && TotalChanges > 0)
        {
            var parts = new List<string>(3);
            if (Inserts > 0) parts.Add(Line(resolve, QueryExecutionMessages.StatusInserted, Inserts));
            if (Updates > 0) parts.Add(Line(resolve, QueryExecutionMessages.StatusUpdated, Updates));
            if (Deletes > 0) parts.Add(Line(resolve, QueryExecutionMessages.StatusDeleted, Deletes));
            terms = string.Join(TermSeparator, parts);
        }
        else
        {
            terms = Line(resolve, QueryExecutionMessages.RowsAffected, RecordsAffected ?? 0);
        }

        return resolve(LocalizableMessage.Of(QueryExecutionMessages.StatusFormat, terms, ms));
    }

    /// <summary>The richer, multi-line summary for a non-result execution's info panel — what
    /// the statement DID (rows inserted/updated/deleted) and how much it READ. Examples:
    /// <code>
    /// Executed in 93 ms
    ///
    /// 8 rows inserted
    /// 16 rows updated
    /// 8 rows deleted
    ///
    /// 20552 rows read
    /// </code>
    /// or, when nothing was modified but work was done:
    /// <code>
    /// Executed in 21 ms
    ///
    /// 20552 rows read
    ///
    /// No data modifications detected.
    /// </code>
    /// When reads weren't measured it degrades to the compact affected-rows line.</summary>
    public string BuildDetailedMessage() => BuildDetailedMessage(ExecutionEnglish.Resolve);

    /// <inheritdoc cref="BuildDetailedMessage()"/>
    /// <param name="resolve">Turns a message into text in the reader's language.</param>
    public string BuildDetailedMessage(Func<LocalizableMessage, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        long ms = (long)Elapsed.TotalMilliseconds;
        var sb = new System.Text.StringBuilder();
        sb.Append(Line(resolve, QueryExecutionMessages.ExecutedIn, ms));

        if (!ChangesMeasured)
        {
            // No MON$ delta captured — the driver's total is all we honestly have.
            sb.Append(TermSeparator).Append(Line(resolve, QueryExecutionMessages.RowsAffected, RecordsAffected ?? 0));
            return sb.ToString();
        }

        var changeLines = new List<string>(3);
        if (Inserts > 0) changeLines.Add(Line(resolve, QueryExecutionMessages.RowsInserted, Inserts));
        if (Updates > 0) changeLines.Add(Line(resolve, QueryExecutionMessages.RowsUpdated, Updates));
        if (Deletes > 0) changeLines.Add(Line(resolve, QueryExecutionMessages.RowsDeleted, Deletes));

        bool hasChanges = changeLines.Count > 0;
        if (hasChanges)
        {
            sb.Append("\n\n").Append(string.Join("\n", changeLines));
        }
        if (ReadsMeasured && RowsRead > 0)
        {
            sb.Append("\n\n").Append(Line(resolve, QueryExecutionMessages.RowsRead, RowsRead));
        }
        if (!hasChanges)
        {
            // Significant work may still have happened (reads) — never the misleading
            // "0 rows affected"; say plainly that nothing was modified.
            sb.Append("\n\n").Append(resolve(LocalizableMessage.Of(QueryExecutionMessages.NoModifications)));
        }
        return sb.ToString();
    }

    /// <summary>The compact one-line form for the collapsed exec-info Expander header, e.g.
    /// "Executed in 54 ms · 14 inserted · 28 updated · 8 deleted · 376 read". Zero change terms
    /// are omitted; the read term shows only when measured; falls back to "· N rows affected"
    /// when the change delta wasn't measured. Single line (never wraps to the detailed body).
    /// The expanded body shows the per-table breakdown (<see cref="ExecutionActivity"/>).</summary>
    public string BuildCompactLine() => BuildCompactLine(ExecutionEnglish.Resolve);

    /// <inheritdoc cref="BuildCompactLine()"/>
    /// <param name="resolve">Turns a message into text in the reader's language.</param>
    public string BuildCompactLine(Func<LocalizableMessage, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        long ms = (long)Elapsed.TotalMilliseconds;
        var sb = new System.Text.StringBuilder();
        sb.Append(Line(resolve, QueryExecutionMessages.ExecutedIn, ms));

        if (!ChangesMeasured)
        {
            // No MON$ delta captured — the driver's total is all we honestly have.
            sb.Append(TermSeparator).Append(Line(resolve, QueryExecutionMessages.RowsAffected, RecordsAffected ?? 0));
            return sb.ToString();
        }

        if (Inserts > 0) Term(sb, resolve, QueryExecutionMessages.TermInserted, Inserts);
        if (Updates > 0) Term(sb, resolve, QueryExecutionMessages.TermUpdated, Updates);
        if (Deletes > 0) Term(sb, resolve, QueryExecutionMessages.TermDeleted, Deletes);
        if (ReadsMeasured && RowsRead > 0) Term(sb, resolve, QueryExecutionMessages.TermRead, RowsRead);
        return sb.ToString();
    }

    private static void Term(
        System.Text.StringBuilder sb, Func<LocalizableMessage, string> resolve, MessageKey key, long count)
        => sb.Append(TermSeparator).Append(Line(resolve, key, count));

    // One place where a key + its data becomes text. ⚠ The count goes in first (R3), so the App can read it
    // without knowing what the sentence says.
    private static string Line(Func<LocalizableMessage, string> resolve, MessageKey key, long count)
        => resolve(LocalizableMessage.Of(key, count));
}
