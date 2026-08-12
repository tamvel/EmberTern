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

        // The CTE's output columns, used to resolve qualified references (cte.col) against the CTE's
        // OWN projection rather than the metadata catalog. An explicit column list (WITH c(a,b) AS …) is
        // authoritative; otherwise derive them from the body's anchor SELECT — but only unambiguous item
        // shapes, marking the set incomplete for *, t.*, or an unaliased expression (never invent a name).
        var explicitCols = cte.ColumnTokens is null
            ? Array.Empty<string>()
            : ReadNameList(cte.ColumnTokens, 0, cte.ColumnTokens.Count);
        IReadOnlyList<string> outputColumns;
        bool columnsComplete;
        if (explicitCols.Count > 0)
        {
            outputColumns = explicitCols;
            columnsComplete = true;
        }
        else
        {
            (outputColumns, columnsComplete) = ExtractCteProjection(cte.Body);
        }

        var sym = new CteSymbol(cteName)
        {
            Columns = explicitCols,
            OutputColumns = outputColumns,
            ColumnsComplete = columnsComplete,
            DeclarationSpan = TextSpan.Of(cte.NameToken),
            DeclaringStatement = stmt,
            QueryScope = qscope,
        };
        scope.Declare(sym);
        AddSymbol(sym);
        AddReference(cte.NameToken, sym, ReferenceRole.SchemaObject, isDefinition: true);
    }

    // ── CTE projection extraction (the CTE's own output columns, no metadata) ─────────────────────

    // Derives a CTE's OUTPUT column names from its body's anchor SELECT projection — Firebird names a
    // CTE's columns after its first (anchor) SELECT when no explicit column list is given. Returns the
    // names AND whether the set is COMPLETE. We accept ONLY item shapes whose output name is unambiguous:
    //   • a bare column           `col`         → col
    //   • a qualified column      `t.col`       → col
    //   • an explicit alias       `<expr> AS n` → n     (the alias is the tail of the item)
    // Anything else — `*`, `t.*`, an unaliased expression/function/literal, an unrecognised body, or an
    // empty projection — makes the set INCOMPLETE. We NEVER synthesise a name (§0 / Paramount Law): an
    // incomplete set lets the model degrade to "cannot verify → no diagnostic" instead of guessing.
    private static (IReadOnlyList<string> Names, bool Complete) ExtractCteProjection(QueryNode? body)
    {
        var select = AnchorSelect(body);
        if (select is null) return (Array.Empty<string>(), false);

        var t = select.Tokens;
        int hi = t.Count;
        int i = SkipSelectPrefix(t, hi); // past SELECT [FIRST v] [SKIP v] [DISTINCT | ALL]

        var names = new List<string>();
        bool complete = true;
        while (i < hi)
        {
            int itemEnd = TopLevelCommaEnd(t, i, hi);
            if (TryProjectionItemName(t, i, itemEnd, out var name)) names.Add(name);
            else complete = false;
            i = itemEnd < hi ? itemEnd + 1 : itemEnd; // step past the comma
        }

        // A CTE with no enumerable output name (empty / malformed projection) cannot be verified — never
        // treat an empty set as "complete" (that would flag every cte.col as unknown).
        if (names.Count == 0) complete = false;
        return (names, complete);
    }

    // The anchor SELECT of a CTE body: the leftmost SELECT of a set operation (its column names win in
    // Firebird), or the main query of a nested WITH. A RawQuery / null cannot be enumerated.
    private static SelectClause? AnchorSelect(QueryNode? q) => q switch
    {
        SelectQuery sq => sq.Select,
        SetOperationQuery so => AnchorSelect(so.Left),
        WithQuery wq => AnchorSelect(wq.Query),
        _ => null,
    };

    // Advances past a SelectClause's leading `SELECT [FIRST <v>] [SKIP <v>] [DISTINCT | ALL]` prefix to
    // the first projection-item token. `FIRST`/`SKIP` take an integer literal, a query parameter, or a
    // parenthesised expression — skipped as one token or one paren group (valid Firebird forms).
    private static int SkipSelectPrefix(IReadOnlyList<SqlToken> t, int hi)
    {
        int i = 0;
        if (i < hi && IsKeyword(t[i], "SELECT")) i++;
        while (i < hi && (IsKeyword(t[i], "FIRST") || IsKeyword(t[i], "SKIP")))
        {
            i++;
            if (i < hi && t[i].Kind == TokenKind.LParen) i = SkipParens(t, i, hi);
            else if (i < hi) i++;
        }
        if (i < hi && (IsKeyword(t[i], "DISTINCT") || IsKeyword(t[i], "ALL"))) i++;
        return i;
    }

    // The exclusive end of the projection item starting at <paramref name="lo"/>: the next top-level
    // (paren-depth 0) comma, or <paramref name="hi"/>. (A comma inside a function call / subquery is at
    // depth > 0 and does not split an item.)
    private static int TopLevelCommaEnd(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        int depth = 0;
        for (int i = lo; i < hi; i++)
        {
            var k = t[i].Kind;
            if (k == TokenKind.LParen) depth++;
            else if (k == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (k == TokenKind.Comma && depth == 0) return i;
        }
        return hi;
    }

    // The unambiguous output name of one projection item [lo, hi), or false when it cannot be named
    // without guessing (see ExtractCteProjection). Order matters: a top-level `*` is rejected first, then
    // a trailing top-level `AS <name>` wins, else only a pure `col` / `t.col` item is accepted.
    private static bool TryProjectionItemName(IReadOnlyList<SqlToken> t, int lo, int hi, out string name)
    {
        name = string.Empty;
        if (lo >= hi) return false;

        int depth = 0;
        for (int i = lo; i < hi; i++)
        {
            var tk = t[i];
            if (tk.Kind == TokenKind.LParen) { depth++; continue; }
            if (tk.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;

            // A top-level star (`*` or `t.*`) exposes columns we cannot enumerate.
            if (tk.Kind == TokenKind.Operator && tk.Text == "*") return false;

            // An explicit alias `<expr> AS <name>` — accepted only when the alias is the item's tail, so
            // an inner `CAST(x AS type)` (at depth > 0) can never be mistaken for it.
            if (IsKeyword(tk, "AS") && i + 2 == hi && IsNameToken(t[i + 1]))
            {
                name = FoldedName(t[i + 1])!;
                return true;
            }
        }

        // No alias: accept only a pure column reference as the whole item — `col` or `t.col`.
        if (hi - lo == 1 && IsNameToken(t[lo]))
        {
            name = FoldedName(t[lo])!;
            return true;
        }
        if (hi - lo == 3 && IsNameToken(t[lo]) && t[lo + 1].Kind == TokenKind.Dot && IsNameToken(t[lo + 2]))
        {
            name = FoldedName(t[lo + 2])!;
            return true;
        }

        return false; // an unaliased expression/function/literal — ambiguous, never guessed
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

            // ⭐⭐ A ':name' / '@name' host-parameter token, bound against the SCOPE CHAIN by the same
            // BindParameterToken the PSQL binder uses (S-6, 2026-08-05). No second mechanism, and no
            // special case for the colon.
            //
            // ⚠⚠ ITS ABSENCE WAS THE WHOLE OF THE REPORTED "no tooltip on :VariableName" DEFECT, and the
            // report's own correlation pointed at the wrong variable. Measured: the colon form resolves
            // identically to the bare form everywhere the PSQL binder walks (an assignment, an IF/WHILE
            // condition, INSERT … VALUES, EXECUTE PROCEDURE, a FOR SELECT header) — SqlLexer emits ':a' as
            // ONE Parameter token and BindParameterToken calls the same scope.Resolve. What had no binding
            // at all was a ':name' inside a QUERY CLAUSE, because this walk handled only 'AS', dotted and
            // bare names. The colon form is simply WHERE an embedded SELECT puts a local, which is why the
            // two looked connected.
            //
            // ⭐ The payoff is much wider than a tooltip: highlighting, Ctrl+Click, find-references and
            // diagnostics were all blind in that range.
            //
            // ⚠ SCOPE, and it needs no gate of its own: at TOP level there are no locals in scope, so
            // scope.Resolve returns null and the reference is recorded UNRESOLVED — which is exactly right
            // for a SQL-Editor smart parameter, and DiagnosticsEngine.IsInRoutineBody already keeps it
            // from being flagged outside a routine body. Inside a body the same call resolves it.
            if (tok.Kind == TokenKind.Parameter)
            {
                BindParameterToken(tok, scope);
                k++;
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
                // ⭐⭐ The same grammar gate the PSQL walker applies — and this walker had NONE, which is the
                // half of the defect class that never reached a bug report because its symptom is quieter
                // (2026-08-07). Where PSQL reports ET0003 on `DATEADD(MONTH, …)`, a query silently BINDS
                // MONTH to a column of that name if one in-scope table has it — wrong colour, wrong Quick
                // Info, wrong find-references — and reports ET0005 "Ambiguous column" if two do.
                //
                // ⚠ Positional, never vocabulary alone: `SELECT MONTH FROM SALES` must keep binding its
                // column. FirebirdGrammar decides from the construct the word sits in, so an ordinary
                // identifier — and a Firebird word outside such a construct — is untouched here.
                if (IsGrammarPinnedNonLocal(t, k)) { k++; continue; }

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
                // A CTE-backed reference resolves against the CTE's OWN projection (its output columns),
                // never the metadata catalog (which has no CTE) — that was the source of the false
                // "unknown column" on cte.col. A catalog table/view still resolves through ResolveColumn.
                // ⭐ Three sources, not two: a CTE resolves against its OWN projection, a SELECTABLE
                // PROCEDURE against its OUTPUT parameters (FromSourceColumns), and a catalog table/view
                // against its columns. The middle one was missing, so every `y.col` over
                // `FROM MY_PROC(:a) y` came back unresolved — a false ET0002 plus an empty completion list.
                var col = tref.Target is CteSymbol cte
                    ? ResolveCteColumn(cte, FoldedName(member))
                    : ResolveColumn(tref.TargetName, FoldedName(member), tref.Target);
                AddReference(member, col, ReferenceRole.Column);
                break;

            case RecordAliasSymbol rec:
                AddReference(qualifier, rec, ReferenceRole.RecordAlias);
                // A NEW/OLD record alias is always a RELATION — never a routine — so no target is passed.
                AddReference(member, ResolveColumn(rec.TargetTable, FoldedName(member), null), ReferenceRole.Column);
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
            if (sym is TableReferenceSymbol { TargetName: { } table } tsym)
            {
                var col = ResolveColumn(table, name, tsym.Target);
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

    // Resolves a qualified reference against a CTE's OWN output columns (not the catalog). Returns a
    // lightweight ColumnSymbol when the name is one of the CTE's projected/declared columns, else null.
    // A null here is flagged as an unknown column by the diagnostics engine ONLY when the CTE's column
    // set is COMPLETE (see DiagnosticsEngine.QualifierResolvesTable); for an incomplete set (a *, an
    // unaliased expression, …) the engine stays silent — we never guess a projection we can't enumerate.
    private readonly Dictionary<string, ColumnSymbol?> _cteColumnCache = new(StringComparer.Ordinal);

    private ColumnSymbol? ResolveCteColumn(CteSymbol cte, string? column)
    {
        if (string.IsNullOrEmpty(column)) return null;

        string? matched = null;
        foreach (var c in cte.OutputColumns)
        {
            if (string.Equals(c, column, StringComparison.OrdinalIgnoreCase)) { matched = c; break; }
        }
        if (matched is null) return null;

        var key = cte.Name + (char)0 + matched;
        if (_cteColumnCache.TryGetValue(key, out var cached)) return cached;

        var sym = new ColumnSymbol(matched) { OwningTable = cte.Name };
        AddSymbol(sym);
        _cteColumnCache[key] = sym;
        return sym;
    }

    private readonly Dictionary<string, ColumnSymbol?> _columnCache = new(StringComparer.OrdinalIgnoreCase);

    // <paramref name="target"/> is the FROM entry's RESOLVED target symbol, which decides WHERE the column
    // set comes from (a relation's columns vs a selectable procedure's output parameters — FromSourceColumns).
    // ⚠ It is deliberately absent from the cache key: the key is (name, column), and a name resolves to one
    // kind for the whole model, so two entries over the same name always want the same answer.
    private ColumnSymbol? ResolveColumn(string? table, string? column, Symbol? target)
    {
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column)) return null;
        // Composite cache key: table + a NUL separator + column (NUL cannot occur in an identifier, so
        // (table, column) maps to exactly one key). Built as (char)0 rather than a raw NUL in the source.
        var key = table + (char)0 + column;
        if (_columnCache.TryGetValue(key, out var cached)) return cached;

        ColumnSymbol? sym = null;
        foreach (var c in FromSourceColumns.Of(_metadata, table!, target))
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
