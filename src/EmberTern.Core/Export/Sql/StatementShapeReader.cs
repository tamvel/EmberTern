using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// Reads <b>signal B</b> off the executed statement's AST: <em>is this a shape where the server's
/// per-column provenance can be trusted?</em>
/// <para>
/// This exists because signal A is blind to exactly the cases that matter most. For
/// <c>select CUSTOMER_ID, NAME from CUSTOMERS union all select PRODUCT_ID, NAME from PRODUCTS</c> the
/// driver reports a clean, key-complete <c>CUSTOMERS</c> result — <b>only leg 1</b> — and an UPDATE built
/// from it would write a <c>PRODUCT_ID</c> value into a real <c>CUSTOMERS</c> row. No schema metadata can
/// detect that. The AST can, trivially, and EmberTern already produces it (Etap 6.9 / B2+B3 made
/// <c>SelectQuery.From</c> and <c>SetOperationQuery</c> first-class nodes — this is that foundation
/// paying off).
/// </para>
/// <para>
/// Pure and offline. It reports <b>facts</b>, never a verdict — <see cref="ResultOriginResolver"/> owns
/// the vetoes, so there is one place where "may we generate?" is decided.
/// </para>
/// </summary>
public static class StatementShapeReader
{
    /// <summary>Analyses the SQL the grid's rows came from. Anything that is not exactly one
    /// confidently-modelled <c>SELECT</c> reports <see cref="StatementShape.NotUnderstood"/>.</summary>
    public static OriginShape Read(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return NotUnderstood;

        // The STRICT parse (`;`-only segmentation) — the executor's own boundary authority, and the grid
        // holds the result of a statement the executor ran. The lenient split is for the read-only
        // semantic model, where a mis-split degrades IntelliSense; here it would change which statement
        // we are reasoning about, so it must not be used.
        var parsed = SqlParser.Parse(sql!);
        var statements = parsed.Root.Statements;
        if (statements.Count != 1) return NotUnderstood;

        // A RawStatement, a DDL/DML statement, or a SELECT the query parser could not model all land here.
        return statements[0] is SelectStatement { Query: { } query }
            ? new OriginShape.Statement(Analyze(query))
            : NotUnderstood;
    }

    private static readonly OriginShape NotUnderstood =
        new OriginShape.Statement(StatementShape.NotUnderstood);

    private static StatementShape Analyze(QueryNode query) => query switch
    {
        SetOperationQuery => new StatementShape { IsUnderstood = true, IsSetOperation = true },

        // Understood, but not resolvable YET: the WITH itself parses fine (B3 models CTE bodies as real
        // queries) — what is missing is resolving a FROM reference back to the CTE that declares it, which
        // is name resolution, not shape. Reported as its own fact rather than folded into "not understood"
        // precisely so the message can say EmberTern cannot yet do this, instead of implying the query is
        // at fault.
        WithQuery => new StatementShape { IsUnderstood = true, IsWithQuery = true },
        SelectQuery s => Analyze(s),
        // RawQuery (the query-level §0 valve) and any future node: not understood ⇒ refuse.
        _ => StatementShape.NotUnderstood,
    };

    private static StatementShape Analyze(SelectQuery s)
    {
        var facts = new StatementShape
        {
            IsUnderstood = true,
            HasGroupBy = s.GroupBy is not null,
            FromItemCount = s.From?.Items.Count ?? 0,
        };

        if (s.From is null || s.From.Items.Count != 1) return facts;

        switch (s.From.Items[0])
        {
            case JoinedTable:
                return facts with { HasJoin = true };

            // A derived table is TRANSPARENT, not a veto: the driver reports the inner query's real base
            // table for `select … from (select … from CUSTOMERS) x`, so that result genuinely is one
            // table's rows. But the same wrapper can hide a UNION or a join — so the inner shape is
            // analysed and folded in, and the outer query's own GROUP BY is OR-ed on top.
            case DerivedTable { Query: { } inner }:
            {
                var innerFacts = Analyze(inner);
                return innerFacts with { HasGroupBy = innerFacts.HasGroupBy || facts.HasGroupBy };
            }

            case DerivedTable:
                return StatementShape.NotUnderstood; // parens held no recognisable query

            default:
                return facts; // a plain TableReference — the one resolvable shape
        }
    }
}
