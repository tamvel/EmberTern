using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

// Query binding — Etap 6.9 convergence: the binder is now an AST CONSUMER. It reads the parser's
// QueryNode tree (SelectQuery / SetOperationQuery / WithQuery, FromClause items, embedded subquery
// expressions) for STRUCTURE — which entries are FROM tables, which parens are subqueries, where the
// CTEs are — instead of re-scanning tokens for it. The structural token walkers this replaces
// (CollectTables / ParseTableList / ParseCteList / the FROM-first two-phase token scan + the
// BeginsSubquery paren re-scan) are DELETED. What remains token-based is purely EXPRESSION-level: within
// a clause's own tokens it records qualified (alias.col) and bare column references — ordinary expression
// content, the agreed structural-depth boundary, not query structure.
//
// The scope shape is unchanged: one Query scope per query (a WITH shares its scope with its main query
// and a set operation shares one scope across its arms, exactly as the old single-range walk did), while
// a derived table / correlated subquery / CTE body each recurse into their own child Query scope. The
// two-phase guarantee (the SELECT list, textually before FROM, still sees the FROM aliases) falls out for
// free: BindSelectInto declares every FromClause item before binding any clause's column references.
internal sealed partial class SemanticBinder
{
    // ── Entry: bind a QueryNode under a parent, in its own new Query scope ────────────────────

    /// <summary>Binds <paramref name="query"/> under <paramref name="parent"/> in a fresh
    /// <see cref="ScopeKind.Query"/> scope, returning it. Used for a statement's top query and for every
    /// nested query that owns a scope (a derived table, a correlated subquery, a CTE body).</summary>
    private Scope BindQueryNode(QueryNode query, Scope parent, SqlStatement? stmt)
    {
        var scope = parent.NewChild(ScopeKind.Query, NodeSpan(query), stmt);
        BindQueryInto(query, scope, stmt);
        return scope;
    }

    // Binds a query's declarations + references INTO an existing scope (no new scope). A WITH shares the
    // scope with its main query (so the CTEs are visible); a set operation shares one scope across its
    // arms (both arms' FROM tables land together, mirroring the pre-convergence single-range walk).
    private void BindQueryInto(QueryNode query, Scope scope, SqlStatement? stmt)
    {
        switch (query)
        {
            case WithQuery wq:
                foreach (var cte in wq.With.Ctes) BindCte(cte, scope, stmt);
                BindQueryInto(wq.Query, scope, stmt);
                break;

            case SetOperationQuery so:
                BindQueryInto(so.Left, scope, stmt);
                BindQueryInto(so.Right, scope, stmt);
                if (so.OrderBy is { } ob) BindClauseReferences(ob, scope, stmt);
                break;

            case SelectQuery sq:
                BindSelectInto(sq, scope, stmt);
                break;

            case RawQuery raw:
                // The query-level §0 valve — no clause structure recognised. Bind its interior as a flat
                // expression range (records what column/local references it can; no new machinery).
                BindExpressionReferences(raw.Tokens, 0, raw.Tokens.Count, scope, stmt, System.Array.Empty<SqlNode>());
                break;
        }
    }

    // A single SELECT core: declare its FROM items (so the projection — textually first — sees the
    // aliases), then bind each clause's embedded subqueries (own child scopes) + column references.
    private void BindSelectInto(SelectQuery sq, Scope scope, SqlStatement? stmt)
    {
        if (sq.From is { } from)
        {
            foreach (var item in from.Items) BindFromItem(item, scope, stmt);
        }
        BindClauseReferences(sq.Select, scope, stmt);
        BindClauseReferences(sq.Where, scope, stmt);
        BindClauseReferences(sq.GroupBy, scope, stmt);
        BindClauseReferences(sq.Having, scope, stmt);
        BindClauseReferences(sq.OrderBy, scope, stmt);
    }

    // ── WITH / CTE (from WithClause nodes) ────────────────────────────────────────────────────

    private void BindCte(CommonTableExpression cte, Scope scope, SqlStatement? stmt)
    {
        var qscope = BindQueryNode(cte.Body, scope, stmt);

        var cteName = FoldedName(cte.NameToken) ?? string.Empty;
        var sym = new CteSymbol(cteName)
        {
            Columns = cte.ColumnTokens is null ? Array.Empty<string>() : ReadNameList(cte.ColumnTokens, 0, cte.ColumnTokens.Count),
            DeclarationSpan = TextSpan.Of(cte.NameToken),
            DeclaringStatement = stmt,
            QueryScope = qscope,
        };
        scope.Declare(sym);
        AddSymbol(sym);
        AddReference(cte.NameToken, sym, ReferenceRole.SchemaObject, isDefinition: true);
    }

    // ── FROM items (from FromClause / JoinedTable nodes) ──────────────────────────────────────

    private void BindFromItem(FromItem item, Scope scope, SqlStatement? stmt)
    {
        switch (item)
        {
            case TableReference tr:
                BindTableReference(tr, scope, stmt);
                break;

            case DerivedTable dt:
                if (dt.Query is not null) BindQueryNode(dt.Query, scope, stmt);
                DeclareDerivedTable(dt.AliasToken, scope, stmt);
                break;

            case JoinedTable jt:
                BindFromItem(jt.Left, scope, stmt);
                BindFromItem(jt.Right, scope, stmt);
                // The ON condition is an ordinary expression (column refs); its subqueries are jt's
                // structural children (after Left/Right in Children) — bind them into correlated scopes.
                if (jt.OnTokens is { } on)
                {
                    var skip = new List<SqlNode>();
                    BindEmbedded(OnSubqueries(jt), scope, stmt, skip);
                    BindExpressionReferences(on, 0, on.Count, scope, stmt, skip);
                }
                break;
        }
    }

    // A named table entry from a FROM item node — [schema.]table [[AS] alias]. The dotted qualifier and
    // alias come from the node (NameToken = the last dotted segment; AliasToken = the alias), so no token
    // scan. Delegates the symbol creation to the shared DeclareTable (also used by DML targets, which have
    // no FROM-item node).
    private void BindTableReference(TableReference tr, Scope scope, SqlStatement? stmt)
    {
        if (tr.NameToken is { } tableTok) DeclareTable(tableTok, tr.AliasToken, scope, stmt);
    }

    // Declares a table reference — the shared symbol/reference logic behind both a FROM-item
    // <see cref="TableReference"/> (query binder) and a DML target/source identified from tokens (DML
    // binder). Resolves the table against an in-scope CTE or the metadata catalog; records the schema-
    // object reference on the name and the table-reference definition on the alias (or the name when
    // unaliased).
    private void DeclareTable(SqlToken tableTok, SqlToken? aliasTok, Scope scope, SqlStatement? stmt)
    {
        var tableName = FoldedName(tableTok) ?? string.Empty;
        var cte = scope.Resolve(tableName) as CteSymbol;
        Symbol? target = cte ?? (Symbol?)ResolveObject(tableName);

        bool isAlias = aliasTok is not null;
        var declTok = isAlias ? aliasTok! : tableTok;
        var refName = isAlias ? FoldedName(aliasTok!) ?? tableName : tableName;

        var tref = new TableReferenceSymbol(refName)
        {
            TargetName = tableName,
            Target = target,
            IsAlias = isAlias,
            DeclarationSpan = TextSpan.Of(declTok),
            DeclaringStatement = stmt,
        };
        scope.Declare(tref);
        AddSymbol(tref);

        if (target is not null) AddReference(tableTok, target, ReferenceRole.SchemaObject);
        AddReference(declTok, tref, ReferenceRole.TableReference, isDefinition: true);
    }

    private void DeclareDerivedTable(SqlToken? aliasTok, Scope scope, SqlStatement? stmt)
    {
        var derivedName = aliasTok is null ? string.Empty : FoldedName(aliasTok) ?? string.Empty;
        var derived = new TableReferenceSymbol(derivedName)
        {
            IsDerived = true,
            IsAlias = aliasTok is not null,
            DeclarationSpan = aliasTok is not null ? TextSpan.Of(aliasTok) : (TextSpan?)null,
            DeclaringStatement = stmt,
        };
        scope.Declare(derived);
        AddSymbol(derived);
        if (aliasTok is not null) AddReference(aliasTok, derived, ReferenceRole.TableReference, isDefinition: true);
    }

    // ── Clause column references (expression-level token walk; subqueries recurse into own scopes) ──

    // Binds one query clause: its embedded subquery expressions become correlated child scopes (they see
    // this scope's tables), and its ordinary tokens are scanned for qualified/bare column references —
    // skipping the subquery token ranges (bound in their own scope) but walking THROUGH a CASE (whose
    // condition/result column references belong to this scope).
    private void BindClauseReferences(QueryClause? clause, Scope scope, SqlStatement? stmt)
    {
        if (clause is null) return;
        var skip = new List<SqlNode>();
        BindEmbedded(clause.Children, scope, stmt, skip); // subqueries → correlated child scopes (+ skip)
        BindExpressionReferences(clause.Tokens, 0, clause.Tokens.Count, scope, stmt, skip);
    }

    // Walks token range [lo, hi) recording qualified (alias.col) and bare column/local references, but
    // stepping over the token span of each subquery in <paramref name="subqueries"/> (already bound into
    // its own child scope). CASE and ordinary parens are walked through — their column references belong
    // to this scope. Replaces the old BindColumnReferences (which re-scanned tokens for FROM lists and
    // `(SELECT` subquery openings; structure now comes from the AST).
    private void BindExpressionReferences(
        IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement? stmt, IReadOnlyList<SqlNode> subqueries)
    {
        int k = lo;
        while (k < hi)
        {
            var tok = t[k];

            // Step over an embedded subquery's tokens (its interior is bound in its own scope).
            int sqEnd = SubqueryTokenEnd(subqueries, tok.Start);
            if (sqEnd > tok.Start)
            {
                k++;
                while (k < hi && t[k].Start < sqEnd) k++;
                continue;
            }

            // An output-column alias (expr AS name) or a CAST type (CAST(x AS type)) — the token after
            // AS is a declaration/type, not a column reference.
            if (IsKeyword(tok, "AS"))
            {
                k++;
                if (k < hi && IsWord(t[k])) k++;
                continue;
            }

            if (IsNameToken(tok) && At(t, k + 1).Kind == TokenKind.Dot && IsWord(At(t, k + 2)))
            {
                BindDottedReference(tok, t[k + 2], scope);
                k += 3;
                continue;
            }

            if (IsNameToken(tok) && At(t, k + 1).Kind != TokenKind.LParen)
            {
                BindBareReference(tok, scope);
                k++;
                continue;
            }

            k++;
        }
    }

    // The end offset of the subquery whose span starts at exactly <paramref name="tokenStart"/>, or
    // tokenStart when no subquery starts there. (Subqueries are non-overlapping and each starts on a real
    // token — an EXISTS keyword or a '(' — so a start-offset match uniquely identifies one.)
    private static int SubqueryTokenEnd(IReadOnlyList<SqlNode> subqueries, int tokenStart)
    {
        foreach (var sq in subqueries)
        {
            if (sq.Start == tokenStart) return sq.End;
        }
        return tokenStart;
    }

    // The subquery expressions to bind + skip for a clause: the SubqueryExpression descendants that are
    // NOT nested inside another subquery (a subquery inside a CASE branch counts — CASE is descended
    // through; a subquery inside a subquery does not — its parent subquery's own binding handles it).
    private static IReadOnlyList<SqlNode> TopSubqueries(SqlNode node)
    {
        List<SqlNode>? acc = null;
        CollectTopSubqueries(node, ref acc);
        return acc ?? (IReadOnlyList<SqlNode>)Array.Empty<SqlNode>();
    }

    private static void CollectTopSubqueries(SqlNode node, ref List<SqlNode>? acc)
    {
        foreach (var child in node.Children)
        {
            if (child is SubqueryExpression sq)
            {
                (acc ??= new List<SqlNode>()).Add(sq); // do NOT descend — its interior is a separate scope
            }
            else
            {
                CollectTopSubqueries(child, ref acc); // descend through CASE / WhenClause / etc.
            }
        }
    }

    // A JoinedTable's ON-condition subqueries — its children after Left and Right.
    private static IReadOnlyList<SqlNode> OnSubqueries(JoinedTable jt)
    {
        var kids = jt.Children;
        if (kids.Count <= 2) return Array.Empty<SqlNode>();
        var list = new List<SqlNode>(kids.Count - 2);
        for (int i = 2; i < kids.Count; i++) list.Add(kids[i]);
        return list;
    }

    private static TextSpan NodeSpan(SqlNode node) => new(node.Start, node.Length);

    // Fallback when a SelectStatement has no modelled QueryNode (a malformed WITH). Binds the tokens as a
    // flat expression in a fresh Query scope — no table structure to discover, so it just records any
    // resolvable column/local references (matches the pre-convergence behaviour on such input).
    private void BindQueryFallback(IReadOnlyList<SqlToken> t, Scope parent, SqlStatement? stmt)
    {
        int hi = t.Count;
        var span = hi > 0 ? TextSpan.FromBounds(t[0].Start, t[hi - 1].End) : new TextSpan(0, 0);
        var scope = parent.NewChild(ScopeKind.Query, span, stmt);
        BindExpressionReferences(t, 0, hi, scope, stmt, System.Array.Empty<SqlNode>());
    }

    // ── Column-reference resolution ──────────────────────────────────────────────────────────

    private void BindDottedReference(SqlToken qualifier, SqlToken member, Scope scope)
    {
        var qualName = FoldedName(qualifier);
        var qsym = scope.Resolve(qualName);

        switch (qsym)
        {
            case TableReferenceSymbol tref:
                AddReference(qualifier, tref, ReferenceRole.Qualifier);
                AddReference(member, ResolveColumn(tref.TargetName, FoldedName(member)), ReferenceRole.Column);
                break;

            case RecordAliasSymbol rec:
                AddReference(qualifier, rec, ReferenceRole.RecordAlias);
                AddReference(member, ResolveColumn(rec.TargetTable, FoldedName(member)), ReferenceRole.Column);
                break;

            // Qualifier is not a known table/record alias (e.g. a package.function call, or an
            // unresolved alias). Leave it unrecorded to avoid noise.
        }
    }

    private void BindBareReference(SqlToken tok, Scope scope)
    {
        var name = FoldedName(tok);
        if (name is null) return;

        var local = scope.Resolve(name);
        switch (local)
        {
            case VariableSymbol:
                AddReference(tok, local, ReferenceRole.Variable);
                return;
            case ParameterSymbol:
                AddReference(tok, local, ReferenceRole.Parameter);
                return;
            case CursorSymbol:
                AddReference(tok, local, ReferenceRole.Cursor);
                return;
            case RecordAliasSymbol:
                AddReference(tok, local, ReferenceRole.RecordAlias);
                return;
        }

        // Bare column: resolve only when exactly one in-scope table owns a column by this name.
        ColumnSymbol? unique = null;
        int matches = 0;
        foreach (var sym in scope.VisibleSymbols())
        {
            if (sym is TableReferenceSymbol { TargetName: { } table })
            {
                var col = ResolveColumn(table, name);
                if (col is not null)
                {
                    matches++;
                    unique = col;
                    if (matches > 1) break;
                }
            }
        }

        if (matches == 1) AddReference(tok, unique, ReferenceRole.Column);
        else if (matches > 1) AddReference(tok, null, ReferenceRole.Column); // ambiguous — recorded unresolved
    }

    // ── Column symbols (cached so references to the same column share one symbol) ─────────────

    private readonly Dictionary<string, ColumnSymbol?> _columnCache = new(StringComparer.OrdinalIgnoreCase);

    private ColumnSymbol? ResolveColumn(string? table, string? column)
    {
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column)) return null;
        // Composite cache key: table + a NUL separator + column (NUL cannot occur in an identifier, so
        // (table, column) maps to exactly one key). Built as (char)0 rather than a raw NUL in the source.
        var key = table + (char)0 + column;
        if (_columnCache.TryGetValue(key, out var cached)) return cached;

        ColumnSymbol? sym = null;
        foreach (var c in _metadata.GetColumns(table!))
        {
            if (string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase))
            {
                sym = new ColumnSymbol(c.Name)
                {
                    OwningTable = table,
                    DataType = c.Type,
                    Domain = c.Domain,
                    Nullable = c.Nullable,
                    DefaultValue = c.DefaultValue,
                    Description = c.Description,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsForeignKey = c.IsForeignKey,
                    ForeignKeyTable = c.ForeignKeyTable,
                    IsComputed = c.IsComputed,
                    IsIdentity = c.IsIdentity,
                };
                AddSymbol(sym);
                break;
            }
        }

        _columnCache[key] = sym;
        return sym;
    }

    private static IReadOnlyList<string> ReadNameList(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var names = new List<string>();
        for (int i = lo; i < hi; i++)
        {
            if (IsNameToken(t[i]))
            {
                var n = FoldedName(t[i]);
                if (n is not null) names.Add(n);
            }
        }
        return names;
    }
}
