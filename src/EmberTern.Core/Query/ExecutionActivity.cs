using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Localization;
using EmberTern.Core.Performance;

namespace EmberTern.Core.Query;

/// <summary>One row change against a table in the Execution Summary — an insert, an update, or a
/// delete, with its row count. Modelled as a small type hierarchy so the view can pick a
/// per-kind icon + colour via type-based DataTemplates (no converter).
///
/// <para>⭐ <b>Etap C6.</b> <see cref="TermKey"/> and <see cref="TableKey"/> are what a localized surface
/// renders; <see cref="Verb"/>, <see cref="LogPhrase"/> and <see cref="Text"/> are the English half of the
/// dual form and stay exactly as they were, so the existing tests keep proving the wording.</para>
///
/// <para>⚠ <b><see cref="Verb"/> is no longer bound in XAML, and must not be again.</b> It used to be — two
/// views drew the count and the verb as separate, differently coloured runs, which silently pinned English
/// word order into the LAYOUT: Polish puts the count after the verb. The card now renders one localized
/// sentence and colours the number inside it, so the order belongs to the translator.</para>
/// </summary>
public abstract record TableChange(long Count)
{
    /// <summary>"inserted" / "updated" / "deleted". ⚠ English half of the dual form — see the type remarks.</summary>
    public abstract string Verb { get; }

    /// <summary>The grammatical phrase joining a count to a table for a one-line log entry —
    /// "inserted into" / "updated in" / "deleted from" (IBExpert-style). ⚠ English half of the dual form.
    /// ⛔ Not decomposable: the preposition is chosen by the verb and governs a noun that inflects.</summary>
    public abstract string LogPhrase { get; }

    /// <summary>The whole "{count} inserted" sentence, count as <c>{0}</c>. The card and the collapsed
    /// exec-info header render this.</summary>
    public abstract MessageKey TermKey { get; }

    /// <summary>The whole "{count} inserted into {table}" sentence — count <c>{0}</c>, table <c>{1}</c>.</summary>
    public abstract MessageKey TableKey { get; }

    /// <summary>"14 inserted" — count + verb, for tests and any single-line rendering.</summary>
    public string Text => string.Format(CultureInfo.InvariantCulture, "{0} {1}", Count, Verb);
}

public sealed record InsertChange(long Count) : TableChange(Count)
{
    public override string Verb => "inserted";
    public override string LogPhrase => "inserted into";
    public override MessageKey TermKey => QueryExecutionMessages.TermInserted;
    public override MessageKey TableKey => QueryExecutionMessages.TableInserted;
}

public sealed record UpdateChange(long Count) : TableChange(Count)
{
    public override string Verb => "updated";
    public override string LogPhrase => "updated in";
    public override MessageKey TermKey => QueryExecutionMessages.TermUpdated;
    public override MessageKey TableKey => QueryExecutionMessages.TableUpdated;
}

public sealed record DeleteChange(long Count) : TableChange(Count)
{
    public override string Verb => "deleted";
    public override string LogPhrase => "deleted from";
    public override MessageKey TermKey => QueryExecutionMessages.TermDeleted;
    public override MessageKey TableKey => QueryExecutionMessages.TableDeleted;
}

/// <summary>One table's changes in the Execution Summary: the table name plus the insert /
/// update / delete rows written to it (present-only). Reads are deliberately NOT here — the
/// Execution Summary answers "what was CHANGED", while read/analysis lives in the Performance
/// tab (Table Access / Findings / Advisor).</summary>
public sealed record TableActivityLine(string Table, IReadOnlyList<TableChange> Changes);

/// <summary>Turns the per-table MON$ delta (<see cref="PerTableReadRow"/>) into an IBExpert-style
/// "what changed" breakdown for the exec-info panel's expanded body. Reuses the SAME captured
/// data — no new acquisition. ONLY tables that were written to appear (insert/update/delete &gt; 0);
/// a table that was merely read produces no entry (reads belong to the Performance tab). Tables
/// are ordered by total changes desc, ties by name. Pure + unit-tested.</summary>
public static class ExecutionActivity
{
    public static IReadOnlyList<TableActivityLine> Build(IReadOnlyList<PerTableReadRow>? reads)
    {
        if (reads is null || reads.Count == 0)
        {
            return Array.Empty<TableActivityLine>();
        }

        // Changed tables only — most changes first, ties broken by name for a stable order.
        // Read-only tables are dropped: the Execution Summary is about modifications.
        var ordered = reads
            .Where(r => r.TotalChanges > 0)
            .OrderByDescending(r => r.TotalChanges)
            .ThenBy(r => r.Table, StringComparer.OrdinalIgnoreCase);

        var result = new List<TableActivityLine>();
        foreach (var r in ordered)
        {
            var changes = new List<TableChange>(3);
            if (r.Inserts > 0) changes.Add(new InsertChange(r.Inserts));
            if (r.Updates > 0) changes.Add(new UpdateChange(r.Updates));
            if (r.Deletes > 0) changes.Add(new DeleteChange(r.Deletes));
            result.Add(new TableActivityLine(r.Table, changes));
        }
        return result;
    }

    /// <summary>The same per-table "what changed" breakdown as <see cref="Build"/>, flattened to
    /// IBExpert-style one-line log entries ("14 inserted into ORDERS", "8 deleted from ORDERS")
    /// for the SQL Editor's Messages log. Reuses <see cref="Build"/> — one filter/ordering rule —
    /// so the SQL Editor shows the same detail as the Procedure/Function panels from the same
    /// data. Empty when nothing was changed / no per-table delta was captured.</summary>
    public static IReadOnlyList<string> BuildLogLines(IReadOnlyList<PerTableReadRow>? reads)
        => BuildLogLines(reads, ExecutionEnglish.Resolve);

    /// <inheritdoc cref="BuildLogLines(IReadOnlyList{PerTableReadRow})"/>
    /// <param name="reads">The per-table MON$ delta.</param>
    /// <param name="resolve">Turns a message into text in the reader's language.</param>
    public static IReadOnlyList<string> BuildLogLines(
        IReadOnlyList<PerTableReadRow>? reads, Func<LocalizableMessage, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        var lines = new List<string>();
        foreach (var table in Build(reads))
        {
            foreach (var change in table.Changes)
            {
                // ⚠ Count first (R3), table second — the whole line is one key, so a language that puts the
                // table before the count simply writes it that way.
                lines.Add(resolve(LocalizableMessage.Of(change.TableKey, change.Count, table.Table)));
            }
        }
        return lines;
    }
}
