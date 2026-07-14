using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

// PSQL binding: CREATE PROCEDURE/FUNCTION/TRIGGER, EXECUTE BLOCK, anonymous blocks, EXECUTE
// PROCEDURE, and CREATE VIEW … AS SELECT. Builds a RoutineBody scope, declares parameters
// (+RETURNS), DECLARE variables/cursors, and (for triggers) the NEW/OLD record aliases, then binds
// body references — FOR SELECT … INTO, embedded queries, NEW/OLD.col, and local variable/parameter
// uses.
//
// Firebird PSQL has no block-local variable declarations (all DECLAREs precede the outermost BEGIN),
// so the RoutineBody scope is the whole body's scope; nested BEGIN/END blocks add no symbols and are
// not modelled as separate scopes in Etap 4 (a deliberate, documented simplification). Body queries
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
            // else: a function's single return type — skipped up to AS below.
        }

        int asIdx = FindTopLevelKeyword(t, k, hi, "AS");
        int bodyLo = asIdx < hi ? asIdx + 1 : hi;
        BindRoutineBody(t, bodyLo, hi, scope, ddl);
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

        int asIdx = FindTopLevelKeyword(t, 0, hi, "AS");
        int bodyLo = asIdx < hi ? asIdx + 1 : hi;
        BindRoutineBody(t, bodyLo, hi, scope, ddl);
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

    private void BindExecuteBlock(SqlStatement stmt)
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

        int asIdx = FindTopLevelKeyword(t, k, hi, "AS");
        int bodyLo = asIdx < hi ? asIdx + 1 : hi;
        BindRoutineBody(t, bodyLo, hi, scope, stmt);
    }

    // ── Anonymous PSQL block (the body editor's DECLARE … BEGIN … END) ───────────────────────

    private void BindAnonymousBlock(SqlStatement stmt)
    {
        var t = stmt.Tokens;
        var scope = _root.NewChild(ScopeKind.RoutineBody, StatementSpan(stmt), stmt);
        BindRoutineBody(t, 0, t.Count, scope, stmt);
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

    // ── Shared routine-body binding ──────────────────────────────────────────────────────────

    private void BindRoutineBody(IReadOnlyList<SqlToken> t, int bodyLo, int bodyHi, Scope scope, SqlStatement stmt)
    {
        int mainBegin = FirstTopLevelBegin(t, bodyLo, bodyHi);
        ScanDeclarations(t, bodyLo, mainBegin, scope, stmt);
        BindBodyReferences(t, mainBegin < bodyHi ? mainBegin : bodyLo, bodyHi, scope, stmt);
    }

    // DECLARE [VARIABLE] name type [NOT NULL] [= default];  and  DECLARE name … CURSOR …;  and
    // FB3 local DECLARE PROCEDURE/FUNCTION (skipped past). Scans the declaration section that
    // precedes the outermost BEGIN.
    private void ScanDeclarations(IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement stmt)
    {
        int k = lo;
        while (k < hi)
        {
            if (!IsKeyword(At(t, k), "DECLARE")) { k++; continue; }

            int declStart = k;
            k++; // past DECLARE
            if (IsKeyword(At(t, k), "VARIABLE")) k++;

            // Local subprogram — skip its whole declaration (to the END of its body).
            if (IsKeyword(At(t, k), "PROCEDURE") || IsKeyword(At(t, k), "FUNCTION"))
            {
                k = SkipLocalSubprogram(t, k, hi);
                continue;
            }

            var nameTok = At(t, k);
            if (!IsNameToken(nameTok)) { k = Math.Max(k + 1, declStart + 1); continue; }
            k++;

            int declEnd = FindTopLevelSemicolon(t, k, hi); // token index of ';' (or hi)
            bool isCursor = ContainsKeyword(t, k, declEnd, "CURSOR");

            if (isCursor)
            {
                var cursor = new CursorSymbol(FoldedName(nameTok) ?? string.Empty)
                {
                    DeclarationSpan = TextSpan.Of(nameTok),
                    DeclaringStatement = stmt,
                };
                scope.Declare(cursor);
                AddSymbol(cursor);
                AddReference(nameTok, cursor, ReferenceRole.Cursor, isDefinition: true);
            }
            else
            {
                // Reconstruct "name type [NOT NULL] [= default]" from source for the proven segment
                // parser; take the declaration name/span from the token.
                string segText = SourceBetween(nameTok.Start, declEnd < hi ? t[declEnd - 1].End : (t.Count > 0 ? t[t.Count - 1].End : 0));
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

            k = declEnd < hi ? declEnd + 1 : hi;
        }
    }

    // Binds references within the executable body: FOR SELECT … INTO, singleton SELECT … INTO,
    // subqueries, NEW/OLD.col and other dotted refs resolvable in this scope, :param tokens, and
    // bare local (variable/parameter/cursor) references.
    private void BindBodyReferences(IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement stmt)
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

    // First top-level BEGIN (paren-depth 0), or hi.
    private static int FirstTopLevelBegin(IReadOnlyList<SqlToken> t, int from, int hi)
        => FindTopLevelKeyword(t, from, hi, "BEGIN");

    // Index of the first top-level (paren-depth 0) ';' at or after `from`, or hi.
    private static int FindTopLevelSemicolon(IReadOnlyList<SqlToken> t, int from, int hi)
    {
        int depth = 0;
        for (int i = from; i < hi; i++)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && kind == TokenKind.Semicolon) return i;
        }
        return hi;
    }

    private static bool ContainsKeyword(IReadOnlyList<SqlToken> t, int lo, int hi, string kw)
    {
        for (int i = lo; i < hi && i < t.Count; i++)
        {
            if (IsKeyword(t[i], kw)) return true;
        }
        return false;
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

    // Advances past a local DECLARE PROCEDURE/FUNCTION declaration — through its BEGIN…END body and
    // an optional trailing ';'. Uses the shared BEGIN/END scan (starting at the subprogram keyword).
    private static int SkipLocalSubprogram(IReadOnlyList<SqlToken> t, int k, int hi)
    {
        // Find the body BEGIN (after the header/AS), then its matching END.
        int begin = FirstTopLevelBegin(t, k, hi);
        if (begin >= hi)
        {
            // No body — a forward/header declaration; skip to its ';'.
            int semi = FindTopLevelSemicolon(t, k, hi);
            return semi < hi ? semi + 1 : hi;
        }
        int endExcl = MatchingEndExclusive(t, begin, hi);
        if (endExcl < hi && t[endExcl].Kind == TokenKind.Semicolon) endExcl++;
        return endExcl;
    }

    // With t[begin] on a BEGIN, returns the token index just past the END that closes it (CASE-aware).
    private static int MatchingEndExclusive(IReadOnlyList<SqlToken> t, int begin, int hi)
    {
        int depth = 0;
        for (int i = begin; i < hi; i++)
        {
            if (IsKeyword(t[i], "BEGIN") || IsKeyword(t[i], "CASE")) depth++;
            else if (IsKeyword(t[i], "END")) { depth--; if (depth == 0) return i + 1; }
        }
        return hi;
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
