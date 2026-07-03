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

    // Plain integers (no grouping) to match the existing "N rows in T ms" message style.
    private static string Part(string verb, long n)
        => string.Format(CultureInfo.InvariantCulture, "{0} {1}", verb, n);
}
