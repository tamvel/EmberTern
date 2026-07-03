using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Performance;

namespace EmberTern.Core.Query;

/// <summary>One row change against a table in the Execution Summary — an insert, an update, or a
/// delete, with its row count. Modelled as a small type hierarchy so the view can pick a
/// per-kind icon + colour via type-based DataTemplates (no converter). Pure — the display verb
/// is fixed English text.</summary>
public abstract record TableChange(long Count)
{
    /// <summary>"inserted" / "updated" / "deleted".</summary>
    public abstract string Verb { get; }

    /// <summary>The grammatical phrase joining a count to a table for a one-line log entry —
    /// "inserted into" / "updated in" / "deleted from" (IBExpert-style).</summary>
    public abstract string LogPhrase { get; }

    /// <summary>"14 inserted" — count + verb, for tests and any single-line rendering.</summary>
    public string Text => string.Format(CultureInfo.InvariantCulture, "{0} {1}", Count, Verb);
}

public sealed record InsertChange(long Count) : TableChange(Count)
{
    public override string Verb => "inserted";
    public override string LogPhrase => "inserted into";
}

public sealed record UpdateChange(long Count) : TableChange(Count)
{
    public override string Verb => "updated";
    public override string LogPhrase => "updated in";
}

public sealed record DeleteChange(long Count) : TableChange(Count)
{
    public override string Verb => "deleted";
    public override string LogPhrase => "deleted from";
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
    {
        var lines = new List<string>();
        foreach (var table in Build(reads))
        {
            foreach (var change in table.Changes)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0} {1} {2}", change.Count, change.LogPhrase, table.Table));
            }
        }
        return lines;
    }
}
