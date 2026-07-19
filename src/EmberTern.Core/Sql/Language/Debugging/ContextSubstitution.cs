using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>The result of substituting a trigger's <c>NEW</c>/<c>OLD</c> and
/// <c>INSERTING</c>/<c>UPDATING</c>/<c>DELETING</c> occurrences out of a source fragment (spec §8.1): the
/// rewritten <see cref="Fragment"/> (ready for the harness — every context reference replaced by a synthetic
/// frame variable or a boolean literal) plus the synthetic names the fragment <b>reads</b> (inject their
/// values) and may <b>write</b> (return them for write-back — only <c>NEW</c> columns of a BEFORE trigger).</summary>
public sealed record ContextRewrite(
    string Fragment,
    IReadOnlyList<string> ContextReads,
    IReadOnlyList<string> ContextWrites);

/// <summary>
/// The <b>one</b> engine that removes a trigger's context (<c>NEW</c>/<c>OLD</c> record columns and the
/// <c>INSERTING</c>/<c>UPDATING</c>/<c>DELETING</c> predicates) from a source fragment so it can run inside the
/// harness's anonymous <c>EXECUTE BLOCK</c>, where none of them exist (spec §8.1). Designed to also serve the
/// §3.6 handler error context (<c>GDSCODE</c>/<c>SQLSTATE</c>/<c>RDB$ERROR</c>) — one mechanism, two consumers.
/// Pure Core.
/// <para>
/// <b>Entirely reference-driven — never a text search.</b> Every rewrite is anchored on a resolved
/// <see cref="SymbolReference"/> from the <see cref="SemanticModel"/> (the binder already records
/// <c>NEW</c>/<c>OLD</c> as <see cref="ReferenceRole.RecordAlias"/> + the following column as
/// <see cref="ReferenceRole.Column"/>, and the predicates as <see cref="ReferenceRole.ContextVariable"/>). We
/// enumerate the references inside the region and replace their spans — so a <c>'NEW.'</c> inside a string
/// literal, a comment, or a quoted identifier is never touched (it has no such reference). This mirrors the
/// span-driven rewrites the executor already uses (<c>RewriteColonRefsToBare</c>, <c>CursorBridge</c>); it is a
/// generalisation of that proven pattern, not a new class of mechanism. A context occurrence the model did not
/// resolve is left verbatim — the server then reports an honest error rather than a silently guessed rewrite
/// (§F).
/// </para>
/// <para>
/// <b>Synthetic naming.</b> Each distinct <c>NEW.col</c>/<c>OLD.col</c> gets a stable, compact synthetic name
/// (<c>ET_CTX_i</c>) assigned once over the whole body (<see cref="BuildColumns"/>), so the same column is the
/// same frame variable everywhere — in every statement, in the frame, and in the Variables window. An
/// index-based name (rather than <c>ET_CTX_NEW_&lt;col&gt;</c>) keeps every synthetic a valid, short identifier
/// regardless of column-name length (FB3's 31-char identifier limit). This class is the <b>single owner</b> of
/// the convention; the metadata layer (which resolves each column's base type) and the write-back both consume
/// the names it hands out.
/// </para>
/// </summary>
public static class ContextSubstitution
{
    private const string SyntheticPrefix = "ET_CTX_";

    /// <summary>Scans a trigger body's <paramref name="scope"/> for every distinct <c>NEW.col</c>/<c>OLD.col</c>
    /// reference and assigns each a stable synthetic frame-variable name (the mapping the whole session shares).
    /// Purely reference-driven — a <see cref="ReferenceRole.RecordAlias"/> reference immediately followed by a
    /// <see cref="ReferenceRole.Column"/> reference (how the binder records <c>NEW.col</c>). The column name
    /// comes from the reference's own text, not a source scan.</summary>
    public static IReadOnlyList<ContextColumn> BuildColumns(SemanticModel model, TextSpan scope)
    {
        ArgumentNullException.ThrowIfNull(model);

        var refs = InScope(model, scope);
        var columns = new List<ContextColumn>();
        var seen = new HashSet<(TriggerRecord, string)>();
        for (int i = 0; i < refs.Count; i++)
        {
            if (refs[i].Role != ReferenceRole.RecordAlias) continue;
            if (i + 1 >= refs.Count || refs[i + 1].Role != ReferenceRole.Column) continue;
            if (!TryRecord(refs[i].Text, out var record)) continue;

            string column = Fold(refs[i + 1].Text);
            i++; // the Column reference is paired with this RecordAlias — consume it
            if (column.Length == 0) continue;

            if (seen.Add((record, column)))
            {
                columns.Add(new ContextColumn(record, column, SyntheticPrefix + columns.Count));
            }
        }
        return columns;
    }

    /// <summary>Rewrites the source <paramref name="region"/> for the harness: each <c>NEW.col</c>/<c>OLD.col</c>
    /// reference becomes its synthetic frame variable (from <paramref name="context"/>.<see cref="TriggerContext.Columns"/>),
    /// and each <c>INSERTING</c>/<c>UPDATING</c>/<c>DELETING</c> predicate becomes <c>TRUE</c>/<c>FALSE</c> for the
    /// simulated <see cref="TriggerContext.Event"/>. Reports the synthetic names the fragment reads (inject) and
    /// may write (return). <c>NEW</c> columns are reported as writes only when <see cref="TriggerContext.NewWritable"/>
    /// (a BEFORE trigger) — over-inclusive there (a merely-read <c>NEW.col</c> written back returns its own value,
    /// harmlessly), never missing a real write; <c>OLD</c> is never written back.
    /// <para>
    /// <paramref name="colonReferences"/> controls how a rewritten column reference names its synthetic frame
    /// variable in the fragment text: bare (<c>ET_CTX_0</c>) for a PSQL expression (an assignment RHS, an
    /// <c>IF</c>/<c>WHILE</c> condition), or colon-prefixed (<c>:ET_CTX_0</c>) inside an embedded <b>DSQL</b>
    /// statement (<c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>/<c>MERGE</c>/<c>SELECT … INTO</c>), where Firebird
    /// reads a bare name as a <b>column</b> (gotcha #247). The reported reads/writes are always the bare synthetic
    /// (the harness declares + injects them bare); only the fragment reference is qualified.</para></summary>
    public static ContextRewrite Substitute(
        SemanticModel model, string source, TextSpan region, TriggerContext context, bool colonReferences = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        var lookup = BuildLookup(context.Columns);
        var refs = InScope(model, region);
        var edits = new List<Edit>();
        var reads = new List<string>();
        var writes = new List<string>();
        var seenRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenWrite = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < refs.Count; i++)
        {
            var r = refs[i];

            // NEW.col / OLD.col — a RecordAlias reference paired with the following Column reference.
            if (r.Role == ReferenceRole.RecordAlias
                && i + 1 < refs.Count && refs[i + 1].Role == ReferenceRole.Column
                && TryRecord(r.Text, out var record))
            {
                var member = refs[i + 1];
                i++; // consume the paired Column reference
                string column = Fold(member.Text);
                if (lookup.TryGetValue((record, column), out var synthetic))
                {
                    // Replace the whole NEW.col span (qualifier .. member) with the synthetic name — colon-prefixed
                    // inside a DSQL statement, bare inside a PSQL expression (gotcha #247). Reads/writes stay bare.
                    edits.Add(new Edit(r.Span.Start, member.Span.End, colonReferences ? ":" + synthetic : synthetic));
                    if (seenRead.Add(synthetic)) reads.Add(synthetic);
                    if (record == TriggerRecord.New && context.NewWritable && seenWrite.Add(synthetic))
                    {
                        writes.Add(synthetic);
                    }
                }
                continue;
            }

            // INSERTING / UPDATING / DELETING — a boolean literal for the simulated event.
            if (r.Role == ReferenceRole.ContextVariable && TryPredicate(r.Text, out var predicate))
            {
                edits.Add(new Edit(r.Span.Start, r.Span.End, predicate == context.Event ? "TRUE" : "FALSE"));
            }
        }

        return new ContextRewrite(ApplyEdits(source, region, edits), reads, writes);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private readonly record struct Edit(int Start, int End, string Replacement);

    // The references inside a region, in document order (the order the binder recorded them, which keeps a
    // NEW.col's RecordAlias and Column references adjacent — BindDottedReference records them back-to-back).
    // Declarations are skipped (a use, never a definition).
    private static List<SymbolReference> InScope(SemanticModel model, TextSpan region)
    {
        var result = new List<SymbolReference>();
        foreach (var r in model.References)
        {
            if (r.IsDefinition) continue;
            if (r.Span.Start < region.Start || r.Span.End > region.End) continue;
            result.Add(r);
        }
        return result;
    }

    private static Dictionary<(TriggerRecord, string), string> BuildLookup(IReadOnlyList<ContextColumn> columns)
    {
        var map = new Dictionary<(TriggerRecord, string), string>();
        foreach (var c in columns)
        {
            map[(c.Record, c.Column)] = c.Synthetic; // columns are already folded + distinct
        }
        return map;
    }

    // Rebuilds region [region.Start, region.End) with the (non-overlapping) edits applied. Reference spans do
    // not overlap, so a stable sort by Start yields a clean splice.
    private static string ApplyEdits(string source, TextSpan region, List<Edit> edits)
    {
        int start = Math.Clamp(region.Start, 0, source.Length);
        int end = Math.Clamp(region.End, start, source.Length);
        if (edits.Count == 0)
        {
            return source.Substring(start, end - start);
        }

        edits.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var sb = new StringBuilder(end - start + 16);
        int cursor = start;
        foreach (var edit in edits)
        {
            int es = Math.Clamp(edit.Start, start, end);
            int ee = Math.Clamp(edit.End, es, end);
            if (es < cursor) continue; // defensive: overlapping/backwards edit — skip
            sb.Append(source, cursor, es - cursor);
            sb.Append(edit.Replacement);
            cursor = ee;
        }
        sb.Append(source, cursor, end - cursor);
        return sb.ToString();
    }

    private static bool TryRecord(string text, out TriggerRecord record)
    {
        var t = text.Trim();
        if (string.Equals(t, "NEW", StringComparison.OrdinalIgnoreCase)) { record = TriggerRecord.New; return true; }
        if (string.Equals(t, "OLD", StringComparison.OrdinalIgnoreCase)) { record = TriggerRecord.Old; return true; }
        record = default;
        return false;
    }

    private static bool TryPredicate(string text, out TriggerEvent predicate)
    {
        var t = text.Trim();
        if (string.Equals(t, "INSERTING", StringComparison.OrdinalIgnoreCase)) { predicate = TriggerEvent.Insert; return true; }
        if (string.Equals(t, "UPDATING", StringComparison.OrdinalIgnoreCase)) { predicate = TriggerEvent.Update; return true; }
        if (string.Equals(t, "DELETING", StringComparison.OrdinalIgnoreCase)) { predicate = TriggerEvent.Delete; return true; }
        predicate = default;
        return false;
    }

    // Fold a column name to Firebird's unquoted-identifier convention (uppercase). A quoted, case-sensitive
    // column identifier is a documented §F boundary (the lab zoo — like real ERP triggers — uses ASCII
    // unquoted names); it would fold wrongly here, matching neither the catalog nor the reference.
    private static string Fold(string text) => text.Trim().ToUpperInvariant();
}
