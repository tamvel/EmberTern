using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

// PSQL binding: CREATE PROCEDURE/FUNCTION/TRIGGER, EXECUTE BLOCK, anonymous blocks, EXECUTE
// PROCEDURE, and CREATE VIEW … AS SELECT. Builds a RoutineBody scope, declares parameters
// (+RETURNS) and (for triggers) the NEW/OLD record aliases, then binds the body by TRAVERSING the
// parser's PSQL body tree (Etap 6.9 / B1b) — a BlockStatement of blocks, IF/WHILE/FOR control flow,
// DECLARE variable/cursor declarations, and executable leaves. The binder is a pure AST CONSUMER: it
// no longer re-derives the body's structure from tokens (the structural token walker — FirstTopLevelBegin
// / FindTopLevelSemicolon / MatchingEndExclusive / SkipLocalSubprogram / the flat body scan — was
// deleted in B1b). The parser owns structure; the binder consumes it.
//
// The header (parameters / RETURNS / the trigger's FOR table) precedes the body's top-level AS and is
// NOT part of the body tree, so it stays token-based here — that is a routine's SIGNATURE, not its body
// structure (its node model is a later milestone). A leaf's INTERIOR (an assignment's expression, a DML
// leaf's clauses, a FOR cursor query, an IF/WHILE condition) also stays token-based for now: those are
// ordinary/query expressions the query tree deepens in B2/B3, not PSQL body structure. Binding one leaf
// range at a time yields exactly the reference set the old flat body scan produced — every body token
// belongs to exactly one node, and the per-token binding logic is unchanged.
//
// Firebird PSQL has no block-local variable declarations (all DECLAREs precede the outermost BEGIN),
// so the one RoutineBody scope is the whole body's scope; nested BEGIN/END blocks add no symbols and
// are not modelled as separate scopes (a deliberate, documented simplification). Body queries
// (FOR SELECT / singleton SELECT / subqueries) get their own Query child scopes so their FROM
// aliases and column references resolve correctly.
internal sealed partial class SemanticBinder
{
    // ── DDL entry ────────────────────────────────────────────────────────────────────────────

    private void BindDdl(DdlStatement ddl)
    {
        var t = ddl.Tokens;

        // Declare the object the DDL defines (best-effort; a known kind + name).
        var symKind = MapDdlKind(ddl.ObjectKind);
        if (symKind is SymbolKind k && !string.IsNullOrEmpty(ddl.ObjectName))
        {
            var nameTok = FindObjectNameToken(t, ddl.ObjectKind);
            var sym = new SchemaObjectSymbol(k, ddl.ObjectName!)
            {
                DeclaringStatement = ddl,
                DeclarationSpan = nameTok is { } nt ? TextSpan.Of(nt) : (TextSpan?)null,
            };
            _root.Declare(sym);
            AddSymbol(sym);
            if (nameTok is { } n) AddReference(n, sym, ReferenceRole.SchemaObject, isDefinition: true);
        }

        if (ddl.IsPsqlDefinition)
        {
            switch (ddl.ObjectKind)
            {
                case DdlObjectKind.Procedure:
                case DdlObjectKind.Function:
                    BindRoutineDefinition(ddl);
                    break;
                case DdlObjectKind.Trigger:
                    BindTriggerDefinition(ddl);
                    break;
                // Package: object declared; its header/body (a list of subprograms) is not bound in
                // Etap 4 — a documented deferral.
            }
        }
        else if (ddl.ObjectKind == DdlObjectKind.View)
        {
            BindCreateView(ddl);
        }
        // Other DDL (Table/Index/Domain/Sequence/Exception/…): the object symbol is declared; there
        // is no executable body to bind.
    }

    // ── CREATE PROCEDURE / FUNCTION ──────────────────────────────────────────────────────────

    private void BindRoutineDefinition(DdlStatement ddl)
    {
        var t = ddl.Tokens;
        int hi = t.Count;

        var keyword = ddl.ObjectKind == DdlObjectKind.Function ? "FUNCTION" : "PROCEDURE";
        int k = FindTopLevelKeyword(t, 0, hi, keyword);
        if (k >= hi) return;
        k++; // past the object keyword

        // Skip the object name (possibly quoted / dotted).
        while (k < hi && (IsNameToken(At(t, k)) || At(t, k).Kind == TokenKind.Dot)) k++;

        var scope = _root.NewChild(ScopeKind.RoutineBody, StatementSpan(ddl), ddl);

        // Input parameters: ( … )
        if (At(t, k).Kind == TokenKind.LParen)
        {
            int close = SkipParens(t, k, hi);
            BindParamList(t, k + 1, close - 1, scope, ddl, ParameterDirection.Input);
            k = close;
        }

        // RETURNS ( outputs )  |  RETURNS <type>
        if (IsKeyword(At(t, k), "RETURNS"))
        {
            k++;
            if (At(t, k).Kind == TokenKind.LParen)
            {
                int close = SkipParens(t, k, hi);
                BindParamList(t, k + 1, close - 1, scope, ddl, ParameterDirection.Output);
                k = close;
            }
            // else: a function's single return type — nothing to bind here.
        }

        // The body (after the header's top-level AS) is the parser's BlockStatement tree.
        BindBody(ddl.Body, scope, ddl);
    }

    // ── CREATE TRIGGER ───────────────────────────────────────────────────────────────────────

    private void BindTriggerDefinition(DdlStatement ddl)
    {
        var t = ddl.Tokens;
        int hi = t.Count;

        var scope = _root.NewChild(ScopeKind.RoutineBody, StatementSpan(ddl), ddl);

        // Table: the name after the top-level FOR keyword.
        string? table = null;
        int forIdx = FindTopLevelKeyword(t, 0, hi, "FOR");
        if (forIdx < hi && IsNameToken(At(t, forIdx + 1)))
        {
            var tableTok = t[forIdx + 1];
            table = FoldedName(tableTok);
            // Record the trigger's target table as a schema-object reference so it is coloured (and
            // Ctrl+Click-navigable) exactly like a table in FROM / UPDATE / INSERT INTO — closes the
            // P5a consistency gap. Only when metadata resolves it (mirrors BindNamedTable's precision).
            var target = ResolveObject(table);
            if (target is not null) AddReference(tableTok, target, ReferenceRole.SchemaObject);
        }

        // NEW / OLD record aliases bound to the trigger's table.
        DeclareRecordAlias(scope, "NEW", table, ddl);
        DeclareRecordAlias(scope, "OLD", table, ddl);

        // INSERTING / UPDATING / DELETING — trigger boolean context predicates. Declared so a bare
        // occurrence in the body resolves and gets the trigger-construct colour. Only inside a
        // trigger (never in a procedure/function body), so a like-named identifier elsewhere is
        // untouched.
        DeclareTriggerPredicate(scope, "INSERTING", ddl);
        DeclareTriggerPredicate(scope, "UPDATING", ddl);
        DeclareTriggerPredicate(scope, "DELETING", ddl);

        // The body (after the header's top-level AS) is the parser's BlockStatement tree.
        BindBody(ddl.Body, scope, ddl);
    }

    private void DeclareRecordAlias(Scope scope, string name, string? table, SqlStatement stmt)
    {
        var rec = new RecordAliasSymbol(name)
        {
            TargetTable = table,
            DeclaringStatement = stmt,
        };
        scope.Declare(rec);
        AddSymbol(rec);
    }

    private void DeclareTriggerPredicate(Scope scope, string name, SqlStatement stmt)
    {
        var pred = new TriggerPredicateSymbol(name) { DeclaringStatement = stmt };
        scope.Declare(pred);
        AddSymbol(pred);
    }

    // ── EXECUTE BLOCK ────────────────────────────────────────────────────────────────────────

    private void BindExecuteBlock(ExecuteBlockStatement stmt)
    {
        var t = stmt.Tokens;
        int hi = t.Count;
        var scope = _root.NewChild(ScopeKind.RoutineBody, StatementSpan(stmt), stmt);

        // Start after "EXECUTE BLOCK".
        int k = FindTopLevelKeyword(t, 0, hi, "BLOCK");
        k = k < hi ? k + 1 : 0;

        if (At(t, k).Kind == TokenKind.LParen)
        {
            int close = SkipParens(t, k, hi);
            BindParamList(t, k + 1, close - 1, scope, stmt, ParameterDirection.Input);
            k = close;
        }

        if (IsKeyword(At(t, k), "RETURNS"))
        {
            k++;
            if (At(t, k).Kind == TokenKind.LParen)
            {
                int close = SkipParens(t, k, hi);
                BindParamList(t, k + 1, close - 1, scope, stmt, ParameterDirection.Output);
                k = close;
            }
        }

        // The body (after the header's top-level AS) is the parser's BlockStatement tree.
        BindBody(stmt.Body, scope, stmt);
    }

    // ── Anonymous PSQL block (the body editor's DECLARE … BEGIN … END) ───────────────────────

    private void BindAnonymousBlock(AnonymousBlockStatement stmt)
    {
        var scope = _root.NewChild(ScopeKind.RoutineBody, StatementSpan(stmt), stmt);
        BindBody(stmt.Body, scope, stmt);
    }

    // ── EXECUTE PROCEDURE ────────────────────────────────────────────────────────────────────

    private void BindExecuteProcedure(ExecuteProcedureStatement exec)
    {
        var t = exec.Tokens;
        int hi = t.Count;

        // EXECUTE PROCEDURE <name> — record a reference to the procedure object.
        if (hi >= 3 && IsWord(t[2]))
        {
            AddReference(t[2], ResolveObject(exec.ProcedureName), ReferenceRole.SchemaObject);
        }

        // RETURNING_VALUES :a, :b, … — the targets are host/PSQL variables (recorded as references;
        // there is no local scope here, so they resolve only against nothing in a bare DSQL call).
        int rv = FindReturningValues(t, 0, hi);
        if (rv < hi)
        {
            for (int i = rv; i < hi; i++)
            {
                if (t[i].Kind == TokenKind.Parameter)
                {
                    AddReference(t[i], null, ReferenceRole.Variable);
                }
            }
        }
    }

    private static int FindReturningValues(IReadOnlyList<SqlToken> t, int from, int hi)
    {
        for (int i = from; i < hi; i++)
        {
            // RETURNING_VALUES is not a catalogued keyword — it lexes as an identifier.
            if (IsWord(t[i]) && string.Equals(t[i].Text, "RETURNING_VALUES", StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }
        return hi;
    }

    // ── CREATE VIEW … AS SELECT ──────────────────────────────────────────────────────────────

    private void BindCreateView(DdlStatement ddl)
    {
        var t = ddl.Tokens;
        int hi = t.Count;
        int asIdx = FindTopLevelKeyword(t, 0, hi, "AS");
        if (asIdx < hi)
        {
            BindQuery(t, asIdx + 1, hi, _root, ddl);
        }
    }

    // ── Body tree traversal (Etap 6.9 / B1b — the binder as an AST consumer) ─────────────────
    //
    // The parser attaches a BlockStatement `Body` tree to every PSQL surface (routine / trigger /
    // EXECUTE BLOCK definition, anonymous block). These methods TRAVERSE it: they own no structural
    // scanning — no BEGIN/END matching, no declaration-boundary scan, no local-subprogram skip. Each
    // node's role is fixed by the parser; the binder only declares symbols and records references.

    // Binds the symbols/references of a PSQL body tree into `scope`. Null-tolerant (defensive; the
    // parser always supplies a body for the surfaces bound here).
    private void BindBody(BlockStatement? body, Scope scope, SqlStatement stmt)
    {
        if (body is not null) BindBlock(body, scope, stmt);
    }

    // A BEGIN … END block: its DECLARE section (routine / EXECUTE BLOCK body only) then its statements.
    // The block's own BEGIN/END keyword tokens carry no references, so they are not scanned.
    private void BindBlock(BlockStatement block, Scope scope, SqlStatement stmt)
    {
        foreach (var decl in block.Declarations) BindDeclaration(decl, scope, stmt);
        foreach (var s in block.Statements) BindPsqlStatement(s, scope, stmt);
    }

    // Dispatches one PSQL body statement to the right binding. Control-flow nodes bind the references
    // in their OWN tokens (the condition / cursor query + INTO — everything before their first child)
    // then recurse into their child statement(s); leaves bind their whole interior; declarations are
    // handled here too so a stray DECLARE outside a block's declaration section still binds (defensive).
    // A body statement is a SqlNode because an embedded DSQL statement (SELECT / INSERT / … ) is the
    // reused top-level statement node (B5), not a PsqlStatement.
    private void BindPsqlStatement(SqlNode node, Scope scope, SqlStatement stmt)
    {
        switch (node)
        {
            case BlockStatement block:
                BindBlock(block, scope, stmt);
                break;

            case IfStatement s:
                BindControlHeader(s, s.Then, scope, stmt);
                if (s.Then is not null) BindPsqlStatement(s.Then, scope, stmt);
                if (s.Else is not null) BindPsqlStatement(s.Else, scope, stmt);
                break;

            case WhileStatement s:
                BindControlHeader(s, s.Body, scope, stmt);
                if (s.Body is not null) BindPsqlStatement(s.Body, scope, stmt);
                break;

            case ForSelectStatement s:
                BindControlHeader(s, s.Body, scope, stmt);
                if (s.Body is not null) BindPsqlStatement(s.Body, scope, stmt);
                break;

            case DeclareVariableStatement or DeclareCursorStatement:
                BindDeclaration((PsqlStatement)node, scope, stmt);
                break;

            case PsqlLeafStatement leaf:
                BindLeafReferences(leaf.Tokens, 0, leaf.Tokens.Count, scope, stmt);
                break;

            // An embedded DSQL statement reused node (B5) — a SELECT / INSERT / UPDATE / DELETE / MERGE /
            // EXECUTE inside the body. Its interior is bound exactly as the pre-B5 flat leaf scan bound it
            // (over the same tokens), so the reference set is unchanged; consuming its node structure
            // (SourceQuery / clauses) for richer binding is a later convergence step.
            case SqlStatement dsql:
                BindLeafReferences(dsql.Tokens, 0, dsql.Tokens.Count, scope, stmt);
                break;
        }
    }

    // Binds the references in a control-flow node's OWN tokens — the leading tokens up to its first
    // child statement (its condition / cursor query + INTO / the DO or THEN keyword). The child bodies
    // are bound by recursion; the inter-child gap (an ELSE keyword) carries no reference. The header is
    // an ordinary/query expression, so it rides the same token-based leaf scanner (deepened in B2/B3).
    private void BindControlHeader(PsqlStatement node, SqlNode? firstChild, Scope scope, SqlStatement stmt)
    {
        var toks = node.Tokens;
        int hi = toks.Count;
        if (firstChild is not null)
        {
            for (int i = 0; i < toks.Count; i++)
            {
                if (toks[i].Start >= firstChild.Start) { hi = i; break; }
            }
        }
        BindLeafReferences(toks, 0, hi, scope, stmt);
    }

    // Declares a DECLARE VARIABLE / DECLARE CURSOR node's symbol. The node type (produced by the parser)
    // already tells variable from cursor; the name token and — for a variable — its "name type [NOT NULL]
    // [= default]" segment are read from the declaration's OWN tokens (a leaf; no boundary scanning).
    private void BindDeclaration(PsqlStatement decl, Scope scope, SqlStatement stmt)
    {
        var toks = decl.Tokens;
        if (DeclNameToken(toks) is not { } nameTok) return;

        if (decl is DeclareCursorStatement)
        {
            var cursor = new CursorSymbol(FoldedName(nameTok) ?? string.Empty)
            {
                DeclarationSpan = TextSpan.Of(nameTok),
                DeclaringStatement = stmt,
            };
            scope.Declare(cursor);
            AddSymbol(cursor);
            AddReference(nameTok, cursor, ReferenceRole.Cursor, isDefinition: true);
            return;
        }

        // Variable: reconstruct "name type [NOT NULL] [= default]" (to before the trailing ';') for the
        // proven segment parser; take the declaration name/span from the name token.
        int last = toks.Count - 1;
        while (last >= 0 && toks[last].Kind == TokenKind.Semicolon) last--;
        int end = last >= 0 ? toks[last].End : nameTok.End;
        string segText = SourceBetween(nameTok.Start, end);
        var parsed = EmberTern.Core.Sql.ProcedureSignatureParser.ParseSegment(segText);
        var v = new VariableSymbol(parsed?.Name ?? FoldedName(nameTok) ?? string.Empty)
        {
            DataType = parsed?.TypeText,
            Nullable = parsed is null ? null : (parsed.NotNull ? false : (bool?)null),
            DefaultValue = parsed?.DefaultValue,
            DeclarationSpan = TextSpan.Of(nameTok),
            DeclaringStatement = stmt,
        };
        scope.Declare(v);
        AddSymbol(v);
        AddReference(nameTok, v, ReferenceRole.Variable, isDefinition: true);
    }

    // The name token of a DECLARE VARIABLE/CURSOR node — the first identifier after DECLARE and an
    // optional VARIABLE keyword (a cursor has no VARIABLE). Bounded to the declaration's own tokens;
    // null when the name is absent or lexes as a keyword (matches the pre-B1b resolution rule).
    private static SqlToken? DeclNameToken(IReadOnlyList<SqlToken> toks)
    {
        int k = 0;
        if (k < toks.Count && IsKeyword(At(toks, k), "DECLARE")) k++;
        if (k < toks.Count && IsKeyword(At(toks, k), "VARIABLE")) k++;
        return k < toks.Count && IsNameToken(toks[k]) ? toks[k] : (SqlToken?)null;
    }

    // ── Leaf-interior reference binding (token-based; the query tree deepens it in B2/B3) ─────
    //
    // Binds references within one executable leaf's / control-flow header's token range: FOR SELECT …
    // INTO, singleton SELECT … INTO, subqueries, NEW/OLD.col and other dotted refs resolvable in this
    // scope, :param tokens, and bare local (variable/parameter/cursor) references. This is NOT PSQL body
    // structure (that is the tree above) — it is the ordinary/query expression interior of a single
    // statement, which the query tree (B2/B3) will model; until then it stays a bounded token scan.
    private void BindLeafReferences(IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement stmt)
    {
        int k = lo;
        while (k < hi)
        {
            var tok = t[k];

            if (tok.Kind == TokenKind.LParen)
            {
                if (BeginsSubquery(t, k, hi))
                {
                    int close = SkipParens(t, k, hi);
                    BindQuery(t, k + 1, close - 1, scope, stmt);
                    k = close;
                    continue;
                }
                k++;
                continue;
            }

            if (IsKeyword(tok, "SELECT"))
            {
                int selectEnd = FindBodySelectEnd(t, k, hi);
                BindQuery(t, k, selectEnd, scope, stmt);
                k = BindOptionalInto(t, selectEnd, hi, scope);
                continue;
            }

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
                BindBareLocal(tok, scope);
                k++;
                continue;
            }

            k++;
        }
    }

    // The end of a body SELECT — the first top-level INTO / ';' / DO / BEGIN / END.
    private static int FindBodySelectEnd(IReadOnlyList<SqlToken> t, int k, int hi)
    {
        int depth = 0;
        for (int i = k; i < hi; i++)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0)
            {
                if (kind == TokenKind.Semicolon) return i;
                if (IsKeyword(t[i], "INTO") || IsKeyword(t[i], "DO")
                    || IsKeyword(t[i], "BEGIN") || IsKeyword(t[i], "END"))
                {
                    return i;
                }
            }
        }
        return hi;
    }

    // If the token at `at` is INTO, binds the following variable list (bare names and :params) up to
    // DO / ';' and returns the index after it; otherwise returns `at` unchanged.
    private int BindOptionalInto(IReadOnlyList<SqlToken> t, int at, int hi, Scope scope)
    {
        if (!IsKeyword(At(t, at), "INTO")) return at;
        int k = at + 1;
        while (k < hi)
        {
            var tok = t[k];
            if (tok.Kind == TokenKind.Semicolon || IsKeyword(tok, "DO")) break;
            if (tok.Kind == TokenKind.Parameter) { BindParameterToken(tok, scope); }
            else if (IsNameToken(tok) && !(At(t, k + 1).Kind == TokenKind.Dot)) { BindBareLocal(tok, scope); }
            k++;
        }
        return k;
    }

    private void BindParameterToken(SqlToken tok, Scope scope)
    {
        var name = ParamName(tok);
        if (name is null) return; // positional '?' — no name to bind
        var sym = scope.Resolve(name);
        var role = sym switch
        {
            ParameterSymbol => ReferenceRole.Parameter,
            VariableSymbol => ReferenceRole.Variable,
            _ => ReferenceRole.Variable,
        };
        AddReference(tok, sym, role);
    }

    // Records a reference only when the bare identifier resolves to a local (variable / parameter /
    // cursor / record alias). Bare identifiers that don't (columns without a qualifier, keywords,
    // functions) are left alone in a routine body — column resolution happens inside the body's
    // Query scopes.
    private void BindBareLocal(SqlToken tok, Scope scope)
    {
        var name = FoldedName(tok);
        var sym = scope.Resolve(name);
        switch (sym)
        {
            case ParameterSymbol:
                AddReference(tok, sym, ReferenceRole.Parameter);
                break;
            case VariableSymbol:
                AddReference(tok, sym, ReferenceRole.Variable);
                break;
            case CursorSymbol:
                AddReference(tok, sym, ReferenceRole.Cursor);
                break;
            case RecordAliasSymbol:
                AddReference(tok, sym, ReferenceRole.RecordAlias);
                break;
            case TriggerPredicateSymbol:
                AddReference(tok, sym, ReferenceRole.ContextVariable);
                break;
        }
    }

    // ── Parameter-list parsing ───────────────────────────────────────────────────────────────

    private void BindParamList(IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement stmt, ParameterDirection direction)
    {
        foreach (var (segLo, segHi) in SplitTopLevelCommasTok(t, lo, hi))
        {
            int ni = segLo;
            while (ni < segHi && !IsNameToken(t[ni])) ni++;
            if (ni >= segHi) continue;

            var nameTok = t[ni];
            string segText = SourceBetween(t[segLo].Start, t[segHi - 1].End);
            var parsed = EmberTern.Core.Sql.ProcedureSignatureParser.ParseSegment(segText);

            var p = new ParameterSymbol(parsed?.Name ?? FoldedName(nameTok) ?? string.Empty)
            {
                Direction = direction,
                DataType = parsed?.TypeText,
                Nullable = parsed is null ? null : (parsed.NotNull ? false : (bool?)null),
                DefaultValue = parsed?.DefaultValue,
                DeclarationSpan = TextSpan.Of(nameTok),
                DeclaringStatement = stmt,
            };
            scope.Declare(p);
            AddSymbol(p);
            AddReference(nameTok, p, ReferenceRole.Parameter, isDefinition: true);
        }
    }

    // ── Token / source helpers specific to PSQL ─────────────────────────────────────────────

    private string SourceBetween(int start, int end)
    {
        if (start < 0) start = 0;
        if (end > _script.Text.Length) end = _script.Text.Length;
        return end > start ? _script.Text.Substring(start, end - start) : string.Empty;
    }

    private TextSpan StatementSpan(SqlNode node) => new(node.Start, node.Length);

    private static string? ParamName(SqlToken tok)
    {
        var s = tok.Text;
        if (s.Length >= 2 && (s[0] == ':' || s[0] == '@'))
        {
            return s.Substring(1).ToUpperInvariant();
        }
        return null; // positional '?'
    }

    // First top-level occurrence (paren-depth 0) of a keyword, or hi.
    private static int FindTopLevelKeyword(IReadOnlyList<SqlToken> t, int from, int hi, string kw)
    {
        int depth = 0;
        for (int i = from; i < hi; i++)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && IsKeyword(t[i], kw)) return i;
        }
        return hi;
    }

    // Splits [lo, hi) into segments at top-level (paren-depth 0) commas.
    private static List<(int Lo, int Hi)> SplitTopLevelCommasTok(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var segs = new List<(int, int)>();
        int depth = 0, segStart = lo;
        for (int i = lo; i < hi; i++)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && kind == TokenKind.Comma)
            {
                if (i > segStart) segs.Add((segStart, i));
                segStart = i + 1;
            }
        }
        if (hi > segStart) segs.Add((segStart, hi));
        return segs;
    }

    // Maps the AST DDL object kind to a symbol kind (null when there is nothing meaningful to declare).
    private static SymbolKind? MapDdlKind(DdlObjectKind k) => k switch
    {
        DdlObjectKind.Table => SymbolKind.Table,
        DdlObjectKind.View => SymbolKind.View,
        DdlObjectKind.Index => SymbolKind.Index,
        DdlObjectKind.Sequence => SymbolKind.Sequence,
        DdlObjectKind.Generator => SymbolKind.Sequence,
        DdlObjectKind.Procedure => SymbolKind.Procedure,
        DdlObjectKind.Function => SymbolKind.Function,
        DdlObjectKind.Trigger => SymbolKind.Trigger,
        DdlObjectKind.Domain => SymbolKind.Domain,
        DdlObjectKind.Exception => SymbolKind.Exception,
        DdlObjectKind.Role => SymbolKind.Role,
        DdlObjectKind.Package => SymbolKind.Package,
        _ => null,
    };

    private static string? KindKeyword(DdlObjectKind k) => k switch
    {
        DdlObjectKind.Table => "TABLE",
        DdlObjectKind.View => "VIEW",
        DdlObjectKind.Index => "INDEX",
        DdlObjectKind.Sequence => "SEQUENCE",
        DdlObjectKind.Generator => "GENERATOR",
        DdlObjectKind.Procedure => "PROCEDURE",
        DdlObjectKind.Function => "FUNCTION",
        DdlObjectKind.Trigger => "TRIGGER",
        DdlObjectKind.Domain => "DOMAIN",
        DdlObjectKind.Exception => "EXCEPTION",
        DdlObjectKind.Role => "ROLE",
        DdlObjectKind.Package => "PACKAGE",
        _ => null,
    };

    // The token that names the DDL object — the first name token after the object-kind keyword
    // (skipping IF [NOT] EXISTS). Best-effort; null when not found.
    private static SqlToken? FindObjectNameToken(IReadOnlyList<SqlToken> t, DdlObjectKind kind)
    {
        var kw = KindKeyword(kind);
        if (kw is null) return null;
        int hi = t.Count;
        int i = FindTopLevelKeyword(t, 0, hi, kw);
        if (i >= hi) return null;
        i++;
        while (i < hi && At(t, i).Kind == TokenKind.Keyword
               && At(t, i).Text.ToUpperInvariant() is "IF" or "NOT" or "EXISTS") i++;
        if (i < hi && IsNameToken(t[i])) return t[i];
        return null;
    }
}
