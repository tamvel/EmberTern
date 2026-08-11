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

        var scope = NewDmlScope(stmt, _root);
        var skip = BindDmlTablesAndQueries(stmt, scope);

        BindExpressionReferences(t, 0, hi, scope, stmt, skip);
    }

    /// <summary>The scope a DML statement's own tables live in — a child of <paramref name="parent"/>, so
    /// the target alias is visible to that statement and to nothing else. <c>_root</c> for a top-level
    /// statement; the enclosing routine-body scope for a DML leaf inside a PSQL body (which is what keeps
    /// <c>:variables</c> resolving through the chain).</summary>
    private Scope NewDmlScope(SqlStatement stmt, Scope parent)
    {
        var t = stmt.Tokens;
        var span = TextSpan.FromBounds(t[0].Start, t[t.Count - 1].End);
        return parent.NewChild(ScopeKind.Dml, span, stmt);
    }

    // ⭐⭐ THE ONE OWNER of "which tables a DML statement brings into scope" — its TARGET (and a MERGE's
    // USING source), plus its embedded queries/subqueries. Returns the nodes the caller's expression walk
    // must skip (they were bound in their own scopes).
    //
    // ⚠ It is shared with the PSQL body binder ON PURPOSE, and that is a FIX, not a tidy-up: until
    // 2026-08-10 BindBodyStatement was a second, parallel dispatch over the same five statement kinds that
    // bound the embedded subqueries and then simply NEVER DECLARED THE TARGET. So every UPDATE / INSERT /
    // DELETE / UPDATE OR INSERT / MERGE written inside a procedure, trigger or EXECUTE BLOCK — i.e. most of
    // an ERP codebase — resolved NO table at all, while the identical statement at the top level resolved
    // fine. The visible half was a table name with no colour; the invisible half is worse and is the reason
    // this must stay ONE method: with the alias undeclared, BindDottedReference records nothing for
    // `alias.column` (it deliberately stays silent on an unresolved qualifier), so the whole statement's
    // columns had no hover, no Ctrl+Click, no find-references and no unknown-column check either.
    private List<SqlNode> BindDmlTablesAndQueries(SqlStatement stmt, Scope scope)
    {
        var t = stmt.Tokens;
        int hi = t.Count;

        // Query/subquery spans bound here and therefore skipped by the caller's flat reference walk.
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

        return skip;
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
