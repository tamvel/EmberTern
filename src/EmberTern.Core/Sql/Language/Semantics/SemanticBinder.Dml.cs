using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

// DML binding — Etap 6.9 convergence: INSERT / UPDATE / UPDATE OR INSERT / DELETE / MERGE. The embedded
// QUERIES are now taken from the AST — an INSERT/MERGE source is a QueryNode, and every embedded scalar/
// EXISTS subquery is a SubqueryExpression node — so the structural query walker (CollectTables + the
// `(SELECT` paren re-scan) is gone here too. What stays token-based is only what has no AST node: the
// DML TARGET (there is no DML clause tree — UPDATE `t`, INSERT INTO `t`, MERGE INTO `t` / USING `src`),
// and the SET / WHERE / VALUES column references (ordinary expression content). A CASE in an expression is
// walked through for its column refs; any subquery nested in it recurses into its own scope.
internal sealed partial class SemanticBinder
{
    private void BindDml(SqlStatement stmt)
    {
        var t = stmt.Tokens;
        int hi = t.Count;
        if (hi == 0) return;

        var span = TextSpan.FromBounds(t[0].Start, t[hi - 1].End);
        var scope = _root.NewChild(ScopeKind.Dml, span, stmt);

        // Query/subquery spans bound below and therefore skipped by the flat column-reference walk.
        var skip = new List<SqlNode>();

        switch (stmt)
        {
            case UpdateStatement u:
                // UPDATE <table> [alias] SET …
                DeclareTargetAt(t, 1, hi, scope, stmt);
                BindEmbedded(u.Subqueries, scope, stmt, skip);
                break;

            case UpdateOrInsertStatement uoi:
                DeclareTargetAfter(t, hi, "INTO", scope, stmt);
                BindEmbedded(uoi.Subqueries, scope, stmt, skip);
                break;

            case InsertStatement ins:
                DeclareTargetAfter(t, hi, "INTO", scope, stmt);
                if (ins.SourceQuery is { } src)
                {
                    BindQueryInto(src, scope, stmt); // source SELECT shares the DML scope (as the old walk did)
                    skip.Add(src);
                }
                BindEmbedded(ins.Subqueries, scope, stmt, skip);
                break;

            case DeleteStatement del:
                // DELETE FROM <table> [alias] — the target follows FROM.
                DeclareTargetAfter(t, hi, "FROM", scope, stmt);
                BindEmbedded(del.Subqueries, scope, stmt, skip);
                break;

            case MergeStatement mg:
                DeclareTargetAfter(t, hi, "INTO", scope, stmt);
                BindMergeSource(mg, t, hi, scope, stmt, skip);
                BindEmbedded(mg.Subqueries, scope, stmt, skip);
                break;
        }

        BindExpressionReferences(t, 0, hi, scope, stmt, skip);
    }

    // Binds a DML statement's embedded expressions: a subquery expression → its own correlated scope
    // (and its tokens are skipped by the column walk); a CASE → walked through for column refs (bound in
    // this scope), but any subquery nested inside it still recurses into its own scope + is skipped.
    private void BindEmbedded(IReadOnlyList<SqlNode> embedded, Scope scope, SqlStatement? stmt, List<SqlNode> skip)
    {
        foreach (var node in embedded)
        {
            if (node is SubqueryExpression sq)
            {
                if (sq.Query is not null) BindQueryNode(sq.Query, scope, stmt);
                skip.Add(sq);
            }
            else
            {
                // A CASE (or other structural expr) — bind the subqueries nested inside it, skip those.
                foreach (var inner in TopSubqueries(node))
                {
                    if (inner is SubqueryExpression isq && isq.Query is not null) BindQueryNode(isq.Query, scope, stmt);
                    skip.Add(inner);
                }
            }
        }
    }

    // The USING source of a MERGE: a subquery source ( (query) alias ) recurses into its own scope and
    // declares the alias as a derived table; a bare table source ( table alias ) declares the table.
    private void BindMergeSource(MergeStatement mg, IReadOnlyList<SqlToken> t, int hi, Scope scope, SqlStatement? stmt, List<SqlNode> skip)
    {
        int u = FindKeyword(t, 0, hi, "USING");
        if (u >= hi) return;

        if (mg.SourceQuery is { } src)
        {
            BindQueryNode(src, scope, stmt); // own scope (a derived table)
            skip.Add(src);
            // The alias follows the source's closing paren.
            int after = SkipParens(t, u + 1, hi);
            var (aliasTok, _) = ReadAlias(t, after, hi);
            DeclareDerivedTable(aliasTok, scope, stmt);
        }
        else
        {
            DeclareTargetAt(t, u + 1, hi, scope, stmt); // bare table source with optional alias
        }
    }

    // Declares the target table that immediately follows <paramref name="keyword"/> (INTO / FROM).
    private void DeclareTargetAfter(IReadOnlyList<SqlToken> t, int hi, string keyword, Scope scope, SqlStatement? stmt)
    {
        int i = FindKeyword(t, 0, hi, keyword);
        if (i < hi) DeclareTargetAt(t, i + 1, hi, scope, stmt);
    }

    // Declares the table (with optional alias) starting at token index i, when it is a real name (not a
    // clause keyword, a '(' derived table, or the end). Reads a dotted [schema.]table and an optional
    // [AS] alias, then hands off to the shared DeclareTable.
    private void DeclareTargetAt(IReadOnlyList<SqlToken> t, int i, int hi, Scope scope, SqlStatement? stmt)
    {
        if (i >= hi || !IsNameToken(At(t, i))) return;

        var tableTok = t[i];
        i++;
        while (At(t, i).Kind == TokenKind.Dot && IsNameToken(At(t, i + 1)))
        {
            tableTok = t[i + 1];
            i += 2;
        }

        var (aliasTok, _) = ReadAlias(t, i, hi);
        DeclareTable(tableTok, aliasTok, scope, stmt);
    }

    // Reads an optional "[AS] alias" at index i — a bare/quoted identifier that is not a clause keyword.
    // Returns (alias token or null, index after it).
    private (SqlToken? Alias, int Next) ReadAlias(IReadOnlyList<SqlToken> t, int i, int hi)
    {
        int j = i;
        if (IsKeyword(At(t, j), "AS")) j++;
        var cand = At(t, j);
        if (j < hi && IsNameToken(cand)) return (cand, j + 1);
        return (null, j);
    }

    private static int FindKeyword(IReadOnlyList<SqlToken> t, int from, int hi, string keyword)
    {
        for (int i = from; i < hi; i++)
        {
            if (IsKeyword(t[i], keyword)) return i;
        }
        return hi;
    }
}
