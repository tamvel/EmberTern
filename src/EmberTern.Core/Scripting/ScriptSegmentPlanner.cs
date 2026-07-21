using System;
using System.Collections.Generic;
using EmberTern.Core.Sql;

namespace EmberTern.Core.Scripting;

/// <summary>
/// The transaction/wait policy a <see cref="ScriptTransactionMode.Sequenced"/> segment's
/// transaction should begin with. This is an INTENT, not a Firebird TPB: the Firebird layer maps
/// it to concrete transaction options (a schema segment reuses the same Developer-Mode-aware WAIT
/// policy that object-editor Compile uses; a data segment keeps the NOWAIT ReadCommitted default).
/// Kept in Core so the planner is pure and unit-testable without a live database.
/// </summary>
public enum SegmentTransactionPolicy
{
    /// <summary>Data-only segment — NOWAIT ReadCommitted. A deployment must never block on an
    /// ordinary row lock (review §2.4 / §5.1).</summary>
    DataNoWait,

    /// <summary>Schema (DDL/DCL) segment — WAIT with a bounded, Developer-Mode-aware timeout, so
    /// deploying an object another session is using waits for it rather than failing instantly
    /// (review §2.3 / §4.3 — the coverage gap Developer Mode was meant to close).</summary>
    SchemaWait,
}

/// <summary>
/// One committed unit of a <see cref="ScriptTransactionMode.Sequenced"/> run: a contiguous run of
/// statements that execute together in ONE transaction and are committed at the segment's end,
/// carrying the <see cref="Policy"/> its kind deserves. Segments are produced in source order and
/// cover every statement exactly once.
/// </summary>
public sealed record ScriptSegment(IReadOnlyList<ScriptStatement> Statements, SegmentTransactionPolicy Policy);

/// <summary>
/// Plans a parsed script into the ordered <see cref="ScriptSegment"/>s a Sequenced run commits one
/// at a time. Pure, deterministic, no live database — the whole decision is a function of the
/// statements' text, classified by the AST-based <see cref="SqlStatementClassifier"/> (never the
/// driver's statement enum — the single-classifier convergence the transaction review asks for).
///
/// <para><b>The rule (conservative v1).</b> Each schema statement
/// (<see cref="SqlStatementCategory.Schema"/> — DDL/DCL) is its OWN segment, committed immediately,
/// so any later statement sees the object it created (gotcha #213). Data statements accumulate into
/// their own segments between schema statements. Every segment is therefore homogeneous: a data
/// segment (NOWAIT) or a single schema statement (WAIT). Exactly one transaction is ever open at a
/// time, so the lane-split self-block (review §2.2(b), measured selective in Step 0 PROBE 1c)
/// cannot occur here by construction.</para>
///
/// <para><b>Deferred optimization.</b> Review §5.1 <em>permits</em> consecutive DDL to SHARE one
/// segment, and Step 0's PROBE 2a proved two INDEPENDENT <c>CREATE TABLE</c>s commit together
/// safely. This planner does not group them — but NOT because dependent DDL would break in one
/// transaction: it does not. Measured live on FB5 (Step 6 review), <c>CREATE TABLE T; CREATE INDEX
/// … ON T;</c> and <c>CREATE TABLE T; ALTER TABLE T ADD …;</c> both SUCCEED inside a single
/// transaction — Firebird applies each DDL's metadata change so a later DDL in the same transaction
/// sees it. (#213 is a DDL→<em>DML</em> rule: a transaction cannot use an object it created for
/// data/reads until commit; it does not apply to DDL→DDL.) Grouping is deferred simply because
/// committing after each schema statement is always correct — exactly what isql's
/// <c>SET AUTODDL ON</c> does — while grouping to reduce commit count would need object-dependency
/// analysis that does not exist yet; it is a future optimization, never a correctness risk.</para>
///
/// <para><b>Ambiguous statements.</b> A statement the classifier cannot confidently place
/// (<see cref="SqlStatementCategory.Ambiguous"/>) is treated as non-schema and grouped into a data
/// segment — the same safe assumption the classifier itself documents (a spurious data grouping
/// costs nothing; a missed schema boundary would only ever bite the vanishingly rare dynamic-DDL
/// case, which is statically undecidable anyway).</para>
///
/// <para>Disallowed statements (transaction/session control) are rejected up front by
/// <see cref="ScriptValidation"/> before planning; the planner assumes a runnable script.</para>
/// </summary>
public static class ScriptSegmentPlanner
{
    /// <summary>Splits <paramref name="statements"/> (in source order) into the segments a
    /// Sequenced run commits one at a time. Empty in → empty out.</summary>
    public static IReadOnlyList<ScriptSegment> Plan(IReadOnlyList<ScriptStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var segments = new List<ScriptSegment>();
        var openDataSegment = new List<ScriptStatement>();

        void FlushDataSegment()
        {
            if (openDataSegment.Count == 0) return;
            segments.Add(new ScriptSegment(openDataSegment.ToArray(), SegmentTransactionPolicy.DataNoWait));
            openDataSegment = new List<ScriptStatement>();
        }

        foreach (var statement in statements)
        {
            if (SqlStatementClassifier.Classify(statement.Text) == SqlStatementCategory.Schema)
            {
                // Close any open data segment first, then this schema statement stands alone so a
                // later dependent statement runs in a fresh transaction that can see it (#213).
                FlushDataSegment();
                segments.Add(new ScriptSegment(new[] { statement }, SegmentTransactionPolicy.SchemaWait));
            }
            else
            {
                openDataSegment.Add(statement);
            }
        }

        FlushDataSegment();
        return segments;
    }
}
