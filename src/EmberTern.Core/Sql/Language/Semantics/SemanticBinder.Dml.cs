using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

// DML binding: INSERT / UPDATE / UPDATE OR INSERT / DELETE / MERGE. Declares the target (and, for
// MERGE, source) table references, then reuses the query-body walk to bind any embedded SELECT /
// subqueries and to resolve column references (qualified always; bare when metadata disambiguates).
internal sealed partial class SemanticBinder
{
    private void BindDml(SqlStatement stmt)
    {
        var t = stmt.Tokens;
        int hi = t.Count;
        if (hi == 0) return;

        var span = TextSpan.FromBounds(t[0].Start, t[hi - 1].End);
        var scope = _root.NewChild(ScopeKind.Dml, span, stmt);

        // Declare the primary target(s). DELETE's target follows FROM, which the query-body walk
        // already handles, so it is not pre-declared here (avoids a duplicate).
        switch (stmt)
        {
            case UpdateStatement:
                // UPDATE <table> [alias] SET …
                if (hi > 1 && IsWord(At(t, 1)) && !IsTableListTerminator(t[1]))
                {
                    BindNamedTable(t, 1, hi, scope, stmt);
                }
                break;

            case UpdateOrInsertStatement:
            case InsertStatement:
                BindTargetAfter(t, hi, scope, stmt, "INTO");
                break;

            case MergeStatement:
                BindTargetAfter(t, hi, scope, stmt, "INTO");
                BindTargetAfter(t, hi, scope, stmt, "USING");
                break;
        }

        // Bind embedded FROM/JOIN (INSERT … SELECT), subqueries, and column references over the whole
        // statement, two-phase (§ two-phase Query binder): phase 1 collects any FROM/JOIN tables into
        // the scope alongside the pre-declared target(s), phase 2 resolves column references so the
        // SET / VALUES / RETURNING columns see every table. Pre-declared target tokens resolve as
        // their table reference (or fall through harmlessly), so this does not re-declare them.
        var ranges = CollectTables(t, 0, hi, scope, stmt);
        BindColumnReferences(t, 0, hi, scope, stmt, ranges);
    }

    // Finds the first <paramref name="keyword"/> and binds the table that follows it as a target.
    private void BindTargetAfter(IReadOnlyList<SqlToken> t, int hi, Scope scope, SqlStatement stmt, string keyword)
    {
        for (int i = 0; i < hi; i++)
        {
            if (IsKeyword(t[i], keyword))
            {
                int j = i + 1;
                if (j < hi && (IsWord(At(t, j)) || At(t, j).Kind == TokenKind.LParen) && !IsTableListTerminator(At(t, j)))
                {
                    if (At(t, j).Kind == TokenKind.LParen)
                    {
                        BindDerivedTable(t, j, hi, scope, stmt);
                    }
                    else
                    {
                        BindNamedTable(t, j, hi, scope, stmt);
                    }
                }
                return;
            }
        }
    }
}
