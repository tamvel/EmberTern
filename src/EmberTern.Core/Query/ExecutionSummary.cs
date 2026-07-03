using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmberTern.Core.Query;

/// <summary>The IBExpert-style one-line summary for a non-result statement (DML / EXECUTE
/// PROCEDURE / EXECUTE BLOCK): the rows it changed (insert / update / delete, from the MON$
/// before/after delta) + elapsed time. When the change counts weren't measured it falls back to
/// the driver's total <see cref="RecordsAffected"/>. The transaction-profile line ("Executed via
/// Data profile (Read Committed)") is logged separately by the VM. Pure — <see cref="BuildMessage"/>
/// is unit-tested. (SELECT statements keep their existing "N rows in T ms" line.)</summary>
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

    /// <summary>The Messages/status line, e.g. "inserted 8 · updated 16 · deleted 8 in 93 ms",
    /// or "0 rows affected in 4 ms" when nothing was changed / measured. Zero terms are omitted.</summary>
    public string BuildMessage()
    {
        long ms = (long)Elapsed.TotalMilliseconds;
        if (ChangesMeasured && TotalChanges > 0)
        {
            var parts = new List<string>(3);
            if (Inserts > 0) parts.Add(Part("inserted", Inserts));
            if (Updates > 0) parts.Add(Part("updated", Updates));
            if (Deletes > 0) parts.Add(Part("deleted", Deletes));
            return string.Format(CultureInfo.CurrentCulture, "{0} in {1} ms", string.Join(" · ", parts), ms);
        }
        return string.Format(CultureInfo.CurrentCulture, "{0} rows affected in {1} ms", RecordsAffected ?? 0, ms);
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
    public string BuildDetailedMessage()
    {
        long ms = (long)Elapsed.TotalMilliseconds;
        var sb = new System.Text.StringBuilder();
        sb.Append("Executed in ").Append(ms.ToString(CultureInfo.CurrentCulture)).Append(" ms");

        if (!ChangesMeasured)
        {
            // No MON$ delta captured — the driver's total is all we honestly have.
            sb.Append(" · ").Append((RecordsAffected ?? 0).ToString(CultureInfo.CurrentCulture)).Append(" rows affected");
            return sb.ToString();
        }

        var changeLines = new List<string>(3);
        if (Inserts > 0) changeLines.Add(Rows(Inserts, "inserted"));
        if (Updates > 0) changeLines.Add(Rows(Updates, "updated"));
        if (Deletes > 0) changeLines.Add(Rows(Deletes, "deleted"));

        bool hasChanges = changeLines.Count > 0;
        if (hasChanges)
        {
            sb.Append("\n\n").Append(string.Join("\n", changeLines));
        }
        if (ReadsMeasured && RowsRead > 0)
        {
            sb.Append("\n\n").Append(Rows(RowsRead, "read"));
        }
        if (!hasChanges)
        {
            // Significant work may still have happened (reads) — never the misleading
            // "0 rows affected"; say plainly that nothing was modified.
            sb.Append("\n\nNo data modifications detected.");
        }
        return sb.ToString();
    }

    /// <summary>The compact one-line form for the collapsed exec-info Expander header, e.g.
    /// "Executed in 54 ms · 14 inserted · 28 updated · 8 deleted · 376 read". Zero change terms
    /// are omitted; the read term shows only when measured; falls back to "· N rows affected"
    /// when the change delta wasn't measured. Single line (never wraps to the detailed body).
    /// The expanded body shows the per-table breakdown (<see cref="ExecutionActivity"/>).</summary>
    public string BuildCompactLine()
    {
        long ms = (long)Elapsed.TotalMilliseconds;
        var sb = new System.Text.StringBuilder();
        sb.Append("Executed in ").Append(ms.ToString(CultureInfo.InvariantCulture)).Append(" ms");

        if (!ChangesMeasured)
        {
            // No MON$ delta captured — the driver's total is all we honestly have.
            sb.Append(" · ").Append((RecordsAffected ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" rows affected");
            return sb.ToString();
        }

        if (Inserts > 0) sb.Append(" · ").Append(Count(Inserts, "inserted"));
        if (Updates > 0) sb.Append(" · ").Append(Count(Updates, "updated"));
        if (Deletes > 0) sb.Append(" · ").Append(Count(Deletes, "deleted"));
        if (ReadsMeasured && RowsRead > 0) sb.Append(" · ").Append(Count(RowsRead, "read"));
        return sb.ToString();
    }

    // "14 inserted" / "376 read" — plain integers (no grouping), for the compact one-liner.
    private static string Count(long n, string verb)
        => string.Format(CultureInfo.InvariantCulture, "{0} {1}", n, verb);

    // Plain integers (no grouping) to match the existing "N rows in T ms" message style.
    private static string Part(string verb, long n)
        => string.Format(CultureInfo.InvariantCulture, "{0} {1}", verb, n);

    // "8 rows inserted" / "1 row inserted" / "20552 rows read".
    private static string Rows(long n, string verb)
        => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", n, n == 1 ? "row" : "rows", verb);
}
