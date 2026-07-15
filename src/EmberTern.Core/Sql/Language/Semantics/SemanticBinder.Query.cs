using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

// Query binding: SELECT / subqueries / CTEs. Builds a nested Query scope, declares FROM/JOIN table
// references (the alias -> table binding), recurses into subqueries as child scopes, and records
// qualified (alias.col) and — when metadata disambiguates — bare column references.
internal sealed partial class SemanticBinder
{
    // Clause words that end a FROM/JOIN table entry or the table list (only unquoted keyword
    // tokens count; a quoted "WHERE" is a real name). Mirrors SqlAliasResolver's terminator set.
    private static readonly HashSet<string> TableListTerminators = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "INTERSECT", "EXCEPT",
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "NATURAL",
        "RETURNING", "INTO", "SET", "VALUES", "USING", "WITH", "PLAN", "FOR",
        "WHEN", "MATCHED", "MATCHING", "AND", "OR",
        "ROWS", "OFFSET", "FETCH", "LIMIT", "FIRST", "SKIP",
    };

    /// <summary>Binds a query occupying tokens <c>[lo, hi)</c> under <paramref name="parent"/>,
    /// returning the new query scope. Two phases so that column references anywhere in the query —
    /// including the SELECT list, which precedes FROM textually — see the table aliases: phase 1
    /// collects the FROM/JOIN table references into the scope, phase 2 resolves column references.</summary>
    private Scope BindQuery(IReadOnlyList<SqlToken> t, int lo, int hi, Scope parent, SqlStatement? stmt)
    {
        var span = lo < hi ? TextSpan.FromBounds(t[lo].Start, t[hi - 1].End) : new TextSpan(lo < t.Count ? t[lo].Start : 0, 0);
        var scope = parent.NewChild(ScopeKind.Query, span, stmt);

        int i = lo;
        if (IsKeyword(At(t, i), "WITH"))
        {
            i++;
            if (IsKeyword(At(t, i), "RECURSIVE")) i++;
            i = ParseCteList(t, i, hi, scope, stmt);
        }

        var ranges = CollectTables(t, i, hi, scope, stmt);
        BindColumnReferences(t, i, hi, scope, stmt, ranges);
        return scope;
    }

    // WITH [RECURSIVE] name [(cols)] AS ( subquery ) [, name AS (…) ]*  — returns the index of the
    // main query that follows the CTE list.
    private int ParseCteList(IReadOnlyList<SqlToken> t, int i, int hi, Scope scope, SqlStatement? stmt)
    {
        while (i < hi)
        {
            var nameTok = At(t, i);
            if (!IsNameToken(nameTok)) break;
            var cteName = FoldedName(nameTok) ?? string.Empty;
            i++;

            IReadOnlyList<string> cols = Array.Empty<string>();
            if (At(t, i).Kind == TokenKind.LParen)
            {
                int close = SkipParens(t, i, hi);
                cols = ReadNameList(t, i + 1, close - 1);
                i = close;
            }

            if (IsKeyword(At(t, i), "AS")) i++;

            Scope? qscope = null;
            if (At(t, i).Kind == TokenKind.LParen)
            {
                int close = SkipParens(t, i, hi);
                qscope = BindQuery(t, i + 1, close - 1, scope, stmt);
                i = close;
            }

            var cte = new CteSymbol(cteName)
            {
                Columns = cols,
                DeclarationSpan = TextSpan.Of(nameTok),
                DeclaringStatement = stmt,
                QueryScope = qscope,
            };
            scope.Declare(cte);
            AddSymbol(cte);
            AddReference(nameTok, cte, ReferenceRole.SchemaObject, isDefinition: true);

            if (At(t, i).Kind == TokenKind.Comma) { i++; continue; }
            break;
        }
        return i;
    }

    // ── Phase 1: collect the FROM/JOIN table references ──────────────────────────────────────
    //
    // Scans [lo, hi) and, for each top-level FROM/JOIN, parses its table list into `scope` (declaring
    // a TableReferenceSymbol per entry and binding derived-table subqueries). Returns the token-index
    // ranges the table lists consumed, so phase 2 skips them (a table/alias is never misread as a
    // column). Non-FROM subqueries (SELECT-list / WHERE correlated) are NOT recursed here — their own
    // FROM tables belong to their child scope; phase 2 binds them once this scope's tables are known.
    private List<(int Lo, int Hi)> CollectTables(IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement? stmt)
    {
        var ranges = new List<(int, int)>();
        int k = lo;
        while (k < hi)
        {
            var tok = t[k];

            // Step over a correlated subquery so its inner FROM isn't scooped into this scope
            // (gotcha #18 — the outer scan must not descend into nested scopes).
            if (tok.Kind == TokenKind.LParen && BeginsSubquery(t, k, hi))
            {
                k = SkipParens(t, k, hi);
                continue;
            }

            if (IsKeyword(tok, "FROM") || IsKeyword(tok, "JOIN"))
            {
                int start = k;
                k = ParseTableList(t, k + 1, hi, scope, stmt);
                ranges.Add((start, k));
                continue;
            }

            k++;
        }
        return ranges;
    }

    // ── Phase 2: resolve column / local references ───────────────────────────────────────────
    //
    // Scans [lo, hi) with every table already in scope (so the SELECT list — textually before FROM —
    // resolves its columns; the two-phase split is exactly what fixes `select k.nazwa from t k`).
    // Skips the phase-1 table-list ranges, recurses into non-FROM subqueries as correlated child
    // scopes, and records qualified + bare column references.
    private void BindColumnReferences(IReadOnlyList<SqlToken> t, int lo, int hi, Scope scope, SqlStatement? stmt, List<(int Lo, int Hi)> tableRanges)
    {
        int k = lo;
        while (k < hi)
        {
            // A FROM/JOIN table list consumed in phase 1 — tables/aliases/derived tables already bound.
            int rangeEnd = RangeEndIfInside(tableRanges, k);
            if (rangeEnd > k) { k = rangeEnd; continue; }

            var tok = t[k];

            if (tok.Kind == TokenKind.LParen)
            {
                if (BeginsSubquery(t, k, hi))
                {
                    int close = SkipParens(t, k, hi);
                    BindQuery(t, k + 1, close - 1, scope, stmt); // correlated child scope, sees outer tables
                    k = close;
                    continue;
                }
                k++; // a normal parenthesised expression — scan its contents in the same loop
                continue;
            }

            // An output-column alias (`expr AS name`) or a CAST type (`CAST(x AS type)`) — the token
            // after AS is a declaration/type, not a column reference. Skip it.
            if (IsKeyword(tok, "AS"))
            {
                k++;
                if (k < hi && IsWord(t[k])) k++;
                continue;
            }

            // qualifier.column
            if (IsNameToken(tok) && At(t, k + 1).Kind == TokenKind.Dot && IsWord(At(t, k + 2)))
            {
                BindDottedReference(tok, t[k + 2], scope);
                k += 3;
                continue;
            }

            // bare column / local reference (not a function-call head)
            if (IsNameToken(tok) && At(t, k + 1).Kind != TokenKind.LParen)
            {
                BindBareReference(tok, scope);
                k++;
                continue;
            }

            k++;
        }
    }

    // The end index of the (non-overlapping) table-list range that contains token index k, or k
    // itself when k is inside no range.
    private static int RangeEndIfInside(List<(int Lo, int Hi)> ranges, int k)
    {
        foreach (var r in ranges)
        {
            if (k >= r.Lo && k < r.Hi) return r.Hi;
        }
        return k;
    }

    // Parses a FROM/JOIN table list starting at i, declaring a TableReferenceSymbol per entry, and
    // binding derived-table subqueries. Returns the index of the terminator that ended the list.
    private int ParseTableList(IReadOnlyList<SqlToken> t, int i, int hi, Scope scope, SqlStatement? stmt)
    {
        while (i < hi)
        {
            var tok = At(t, i);
            if (tok.Kind == TokenKind.LParen)
            {
                i = BindDerivedTable(t, i, hi, scope, stmt);
            }
            else if (!IsWord(tok) || IsTableListTerminator(tok))
            {
                return i;
            }
            else
            {
                i = BindNamedTable(t, i, hi, scope, stmt);
            }

            if (At(t, i).Kind == TokenKind.Comma) { i++; continue; }
            return i;
        }
        return i;
    }

    // Binds a single named table entry — <c>table [alias]</c>, with an optional dotted qualifier —
    // declaring its TableReferenceSymbol and references. Returns the index after the entry (its
    // alias, when present). Shared by FROM/JOIN lists and DML targets/sources.
    private int BindNamedTable(IReadOnlyList<SqlToken> t, int i, int hi, Scope scope, SqlStatement? stmt)
    {
        var tableTok = At(t, i);
        var tableName = FoldedName(tableTok) ?? string.Empty;
        i++;
        while (At(t, i).Kind == TokenKind.Dot && IsNameToken(At(t, i + 1)))
        {
            tableTok = t[i + 1];
            tableName = FoldedName(t[i + 1]) ?? string.Empty;
            i += 2;
        }

        var cte = scope.Resolve(tableName) as CteSymbol;
        Symbol? target = cte ?? (Symbol?)ResolveObject(tableName);

        var (alias, afterAlias) = ReadOptionalAlias(t, i, hi);
        i = afterAlias;
        bool isAlias = alias is not null;
        var refName = isAlias ? FoldedName(alias!) ?? tableName : tableName;
        var declTok = isAlias ? alias! : tableTok;

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

        if (target is not null)
        {
            AddReference(tableTok, target, ReferenceRole.SchemaObject);
        }
        AddReference(declTok, tref, ReferenceRole.TableReference, isDefinition: true);
        return i;
    }

    // Binds a derived table — <c>( subquery ) [AS] alias</c>. Returns the index after the alias.
    private int BindDerivedTable(IReadOnlyList<SqlToken> t, int i, int hi, Scope scope, SqlStatement? stmt)
    {
        int close = SkipParens(t, i, hi);
        BindQuery(t, i + 1, close - 1, scope, stmt);
        i = close;

        var (aliasTok, next) = ReadOptionalAlias(t, i, hi);
        i = next;
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
        if (aliasTok is not null)
        {
            AddReference(aliasTok, derived, ReferenceRole.TableReference, isDefinition: true);
        }
        return i;
    }

    // Reads an optional "[AS] alias" and returns (alias token or null, index after it). An alias
    // must be a bare/quoted identifier that is not a clause terminator (so a keyword like WHERE or
    // JOIN never becomes an alias).
    private static (SqlToken? Alias, int Next) ReadOptionalAlias(IReadOnlyList<SqlToken> t, int i, int hi)
    {
        int j = i;
        if (IsKeyword(At(t, j), "AS")) j++;
        var cand = At(t, j);
        if (j < hi && IsNameToken(cand) && !IsTableListTerminator(cand))
        {
            return (cand, j + 1);
        }
        // "AS" with no following name (malformed) — consume the AS but yield no alias.
        return (null, j);
    }

    private static bool IsTableListTerminator(SqlToken t)
        => t.Kind == TokenKind.Keyword && TableListTerminators.Contains(t.Text);

    // True when the '(' at i opens a subquery — its first significant inner token is SELECT or WITH.
    private static bool BeginsSubquery(IReadOnlyList<SqlToken> t, int lparen, int hi)
    {
        int n = lparen + 1;
        if (n >= hi) return false;
        return IsKeyword(t[n], "SELECT") || IsKeyword(t[n], "WITH");
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
            // unresolved alias). Leave it unrecorded to avoid noise — a later etap with a deeper
            // AST can classify these precisely.
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
        // Requires metadata; without it we record nothing (keeps bare-column refs high-precision).
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

        if (matches == 1)
        {
            AddReference(tok, unique, ReferenceRole.Column);
        }
        else if (matches > 1)
        {
            AddReference(tok, null, ReferenceRole.Column); // ambiguous — recorded unresolved
        }
        // matches == 0: not a known column — skip (may be a function / output alias / unknown).
    }

    // ── Column symbols (cached so references to the same column share one symbol) ─────────────

    private readonly Dictionary<string, ColumnSymbol?> _columnCache =
        new(StringComparer.OrdinalIgnoreCase);

    private ColumnSymbol? ResolveColumn(string? table, string? column)
    {
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column)) return null;
        // Composite cache key: table + a NUL separator + column. NUL cannot occur in an identifier,
        // so (table, column) maps to exactly one key (a plain space could collide with a quoted
        // identifier that contains a space). The separator is built as (char)0 rather than a raw NUL
        // byte in the source -- a raw NUL makes git treat the file as binary and breaks grep/diff.
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
