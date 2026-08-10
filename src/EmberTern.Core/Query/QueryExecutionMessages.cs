using EmberTern.Core.Localization;

namespace EmberTern.Core.Query;

/// <summary>
/// The message keys <see cref="ExecutionSummary"/> and <see cref="ExecutionActivity"/> produce — etap C6 of
/// decision <b>D‑3</b>.
///
/// <para>⭐ <b>Every key resolves to a WHOLE SENTENCE, never to a word.</b> The code these replace assembled
/// sentences from fragments — <c>"{n} {row|rows} {inserted}"</c>, <c>"{n} {inserted into} {table}"</c> — and
/// that assembly is English word order written into Core. A translator who is handed the whole sentence
/// chooses the order themselves; a translator handed <c>"inserted into"</c> cannot, because Polish puts the
/// count between the verb and the object and inflects the noun that is not even present in the fragment. So
/// the migration is a re-CUT of the sentences, not a substitution of words.</para>
///
/// <para>⭐ <b>Two English orderings are preserved on purpose, not merged.</b> The status line says
/// <c>"inserted 8"</c> (<see cref="StatusInserted"/>) and the compact chip says <c>"8 inserted"</c>
/// (<see cref="TermInserted"/>). They are different surfaces with different existing wording, and merging
/// them would change one of them — which C6 was told not to do. Keeping them apart also leaves a translator
/// free to phrase a status bar differently from a chip.</para>
///
/// <para>⚠ <b>Which of these are plural FAMILIES is not decided here, and cannot be.</b> A key that carries a
/// count may resolve to several category variants (<c>.one</c>, <c>.few</c>, <c>.other</c>, …) in the
/// catalog; whether it needs them is a property of the LANGUAGE, so it is declared per culture in
/// <c>Strings[.culture].resx</c> and resolved by the App. Core states only that a count is
/// <b>argument {0}</b> — see <see cref="ExecutionSummary"/>. ⛔ Do not add a "this key is plural" flag here:
/// it would make Core assert something about grammar it cannot know.</para>
/// </summary>
public static class QueryExecutionMessages
{
    // ── Whole-sentence lines carrying a row count (the detailed exec-info panel) ──────────────────────
    //
    // ⚠ These are the keys whose English catalog entries are plural families today. The producer passes the
    // count as {0} and says nothing about categories.

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey RowsInserted = new("Query.Exec.RowsInserted");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey RowsUpdated = new("Query.Exec.RowsUpdated");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey RowsDeleted = new("Query.Exec.RowsDeleted");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey RowsRead = new("Query.Exec.RowsRead");

    /// <summary>
    /// The driver's total when no MON$ delta was captured. Takes the row count as <c>{0}</c>.
    ///
    /// <para>⚠ This one had NO singular form before C6 — the old code emitted <c>"1 rows affected"</c>,
    /// reachable whenever the driver reported exactly one affected row. Giving it a plural family is what
    /// makes it translatable at all, and it corrects that as a side effect; the audit reported it before the
    /// contract was accepted, so the one-row English wording is a known, deliberate delta.</para>
    /// </summary>
    public static readonly MessageKey RowsAffected = new("Query.Exec.RowsAffected");

    // ── Fixed lines (no count) ───────────────────────────────────────────────────────────────────────

    /// <summary>Takes the elapsed milliseconds as <c>{0}</c>.</summary>
    public static readonly MessageKey ExecutedIn = new("Query.Exec.ExecutedIn");

    public static readonly MessageKey NoModifications = new("Query.Exec.NoModifications");

    // ── Status line (SQL Editor) — verb first ────────────────────────────────────────────────────────

    /// <summary>Wraps the assembled change terms: <c>{0}</c> the terms, <c>{1}</c> the elapsed ms.</summary>
    public static readonly MessageKey StatusFormat = new("Query.Exec.Status.Format");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey StatusInserted = new("Query.Exec.Status.Inserted");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey StatusUpdated = new("Query.Exec.Status.Updated");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey StatusDeleted = new("Query.Exec.Status.Deleted");

    // ── Compact terms (collapsed exec-info header + the per-table card) — count first ────────────────
    //
    // ⭐ ONE key per kind serves both surfaces. They carry identical English on the same module and the
    // same data; if a translation ever needs to tell them apart, splitting is a catalog change plus one
    // call site — no code shape depends on them being shared.

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey TermInserted = new("Query.Exec.Term.Inserted");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey TermUpdated = new("Query.Exec.Term.Updated");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey TermDeleted = new("Query.Exec.Term.Deleted");

    /// <summary>Takes the row count as <c>{0}</c>.</summary>
    public static readonly MessageKey TermRead = new("Query.Exec.Term.Read");

    // ── Per-table log lines (SQL Editor Messages) ────────────────────────────────────────────────────
    //
    // ⚠ The count is {0} and the table name is {1}. The English "inserted into" / "updated in" /
    // "deleted from" is a PHRASE, not a word: it is a preposition chosen by the verb, and the noun it
    // governs is inflected in Polish. There is nothing smaller than the whole line to key.

    /// <summary><c>{0}</c> row count, <c>{1}</c> table name.</summary>
    public static readonly MessageKey TableInserted = new("Query.Exec.Table.Inserted");

    /// <summary><c>{0}</c> row count, <c>{1}</c> table name.</summary>
    public static readonly MessageKey TableUpdated = new("Query.Exec.Table.Updated");

    /// <summary><c>{0}</c> row count, <c>{1}</c> table name.</summary>
    public static readonly MessageKey TableDeleted = new("Query.Exec.Table.Deleted");
}
