using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Constructs;
using EmberTern.Core.Sql.Language.Ergonomics;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Completion;

/// <summary>
/// Produces context-aware completion suggestions from a <see cref="SemanticModel"/> and a caret
/// offset — Etap 5 of the editor rebuild (design §5.7 / §22). It is a pure Core client of the
/// language front-end: it reads <b>only</b> the model (its AST token stream, scopes, symbols, and
/// metadata snapshot) — never a fresh scan of the raw text (§22.0). The App controller becomes thin
/// glue that maps <see cref="CompletionItem"/>s onto its completion window.
/// <para>
/// M2 (this) is the baseline: keywords + every known schema object + the symbols in scope at the
/// caret (aliases, variables, parameters, CTEs, cursors, NEW/OLD). M3 adds dot/qualifier → column
/// completion; M4 adds positional context ranking (after FROM → tables first, etc.). Each later
/// milestone slots in without changing this API or the baseline.
/// </para>
/// <para>
/// <b>Scope of this class — one responsibility, one owner.</b> It answers <i>what is legal at this
/// caret</i>: the candidate set, kind-ranked and position-boosted. It does <b>not</b> narrow by the
/// prefix the user has typed — that is <see cref="CompletionMatcher"/>'s single job. The split is
/// what lets an open list widen again on a backspace: the candidate set is a property of the
/// <i>position</i> the list opened at and is fixed for that session, while the prefix changes on
/// every keystroke. Folding the two together would force a re-query (and therefore a whole-document
/// re-parse, or a debounce-lagged model whose token offsets no longer match the caret) per character.
/// </para>
/// </summary>
public static class CompletionEngine
{
    /// <summary>
    /// Returns the completion candidates for <paramref name="offset"/> in
    /// <paramref name="model"/>. Never throws; returns <see cref="CompletionResult.Empty"/> for a
    /// null model. Items are ordered by <see cref="CompletionItem.SortPriority"/> desc then name.
    /// <para>The result is the <b>unfiltered</b> candidate set — run it through
    /// <see cref="CompletionMatcher.Filter"/> with the typed prefix to get the list to display.</para>
    /// </summary>
    public static CompletionResult GetCompletions(
        SemanticModel model,
        int offset,
        CompletionTrigger trigger = CompletionTrigger.Explicit)
    {
        if (model is null) return CompletionResult.Empty;

        // Dot/qualifier position → the qualifier's columns only (M3). Detected from the AST token
        // stream, never a fresh text scan (§22.0). A dot context short-circuits the baseline: in
        // "k.|" the user wants K's columns, not keywords.
        if (TryGetDotCompletions(model, offset, out var dot))
        {
            return dot;
        }

        var items = new List<CompletionItem>();
        var seen = new HashSet<(string, CompletionItemKind)>(NameKindComparer.Instance);

        AddInScopeSymbols(model, offset, items, seen);
        AddImplicitTableColumns(model, offset, items, seen);
        AddSchemaObjects(model, items, seen);
        AddKeywords(items, seen);

        // M4: rank by caret position (after FROM → tables first; EXECUTE PROCEDURE → procedures;
        // expression → columns/functions/in-scope). Ranking ONLY — never hides a correct item, so
        // ambiguous input degrades to the M2 baseline order.
        ApplyContextRanking(model, offset, items);

        Sort(items);
        return new CompletionResult(items);
    }

    // ── Dot / qualifier → columns (M3) ───────────────────────────────────────────────────────

    private static bool TryGetDotCompletions(SemanticModel model, int offset, out CompletionResult result)
    {
        result = CompletionResult.Empty;
        if (!TryResolveDotQualifier(model.Syntax.Tokens, offset, out var qualifier)) return false;

        var source = ResolveDotSource(model, model.ScopeAt(offset), qualifier);
        var table = source?.Name;
        var target = source?.Target;
        if (table is null)
        {
            // Dot context, but the qualifier didn't resolve — return an empty *dot* result so the
            // App doesn't fall back to the baseline (keywords/objects) after a ".".
            result = new CompletionResult(Array.Empty<CompletionItem>(), isDotContext: true, dotTargetTable: null);
            return true;
        }

        // ⭐ Not GetColumns: a selectable procedure in FROM contributes its OUTPUT parameters, and asking the
        // catalog for its columns returned an empty list — so `y.` after `FROM MY_PROC(:a) y` offered NOTHING
        // (measured 2026-08-12, the quiet half of the false-ET0002 report). One owner: FromSourceColumns.
        var cols = FromSourceColumns.Of(model.Metadata, table, target);
        var items = new List<CompletionItem>(cols.Count);
        foreach (var c in cols)
        {
            if (string.IsNullOrEmpty(c.Name)) continue;
            items.Add(ToColumnItem(c, table));
        }
        Sort(items);
        result = new CompletionResult(items, isDotContext: true, dotTargetTable: table);
        return true;
    }

    // Builds one column completion item, carrying the rich column as a ColumnSymbol so the App renders
    // the domain in the row and the full facts in the detail pane from ONE source (P2 / Package 5) — no
    // second lookup, no new model. Shared by the dot path (qualifier.col) and the unqualified
    // single-table path so both surfaces read identically.
    private static CompletionItem ToColumnItem(ColumnMetadata c, string table)
    {
        var sym = new ColumnSymbol(c.Name)
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
        return new CompletionItem(
            c.Name, c.Name, CompletionItemKind.Column, PriorityFor(CompletionItemKind.Column), c.Type, sym);
    }

    // Detects a "qualifier . [prefix]|" caret position from the token stream and returns the folded
    // qualifier name. Requires the qualifier, the dot, and the (optional) partial prefix to be
    // adjacent (no whitespace between them), so "k .x" / "k. from" don't count.
    private static bool TryResolveDotQualifier(IReadOnlyList<SqlToken> tokens, int offset, out string qualifier)
    {
        qualifier = string.Empty;

        // The last significant token that begins before the caret.
        int i = -1;
        for (int k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.IsEndOfFile) break;
            if (t.Start < offset) i = k; else break; // tokens are in source order
        }
        if (i < 0) return false;

        // Case A: caret immediately after a dot  ->  qualifier '.' |
        if (tokens[i].Kind == TokenKind.Dot)
        {
            if (offset > tokens[i].End) return false; // trailing content/whitespace after the dot
            return TryQualifierBeforeDot(tokens, i, out qualifier);
        }

        // Case B: caret within/at the end of a partial identifier that follows a dot -> q '.' pref|
        if (IsWord(tokens[i]) && offset <= tokens[i].End
            && i - 1 >= 0 && tokens[i - 1].Kind == TokenKind.Dot
            && tokens[i].Start == tokens[i - 1].End) // prefix adjacent to the dot
        {
            return TryQualifierBeforeDot(tokens, i - 1, out qualifier);
        }

        return false;
    }

    private static bool TryQualifierBeforeDot(IReadOnlyList<SqlToken> tokens, int dotIndex, out string qualifier)
    {
        qualifier = string.Empty;
        int q = dotIndex - 1;
        if (q < 0) return false;
        var qt = tokens[q];
        if (!IsWord(qt)) return false;
        if (tokens[dotIndex].Start != qt.End) return false; // dot must be adjacent to the qualifier
        qualifier = FoldedName(qt);
        return qualifier.Length > 0;
    }

    // Resolves the qualifier before the dot to the SOURCE it names: a FROM/JOIN alias or table name,
    // a NEW/OLD trigger record, a DDL-introduced object in scope, or (fallback) a catalog object
    // referenced directly. Mirrors the App's prior SqlAliasResolver-based logic conceptually.
    //
    // ⚠ Returns the resolved TARGET SYMBOL alongside the name, because the name alone does not say where the
    // column set lives — a selectable procedure's is its RETURNS list (FromSourceColumns). Returning only a
    // string is what made this path unable to answer for `FROM MY_PROC(:a) y`.
    private static (string? Name, Symbol? Target)? ResolveDotSource(SemanticModel model, Scope scope, string qualifier)
    {
        switch (scope.Resolve(qualifier))
        {
            case TableReferenceSymbol tref:
                // null Name for a derived table (its columns come from the subquery, not the catalog).
                return (tref.Target?.Name ?? tref.TargetName, tref.Target);
            case RecordAliasSymbol rec:
                return (rec.TargetTable, null); // a NEW/OLD record is always a relation
            case SchemaObjectSymbol so when IsTableLike(so.Kind):
                return (so.Name, null);
        }

        // Not aliased in this scope — the qualifier may be a catalog table/view referenced directly.
        var obj = model.Metadata.FindObject(qualifier);
        return obj is not null && IsTableLike(obj.Kind) ? (obj.Name, null) : null;
    }

    private static bool IsTableLike(SymbolKind kind)
        => kind is SymbolKind.Table or SymbolKind.View or SymbolKind.SystemTable;

    private static bool IsWord(SqlToken t)
        => t.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier or TokenKind.Keyword;

    private static string FoldedName(SqlToken t) => t.Kind switch
    {
        TokenKind.QuotedIdentifier => t.Value,
        TokenKind.Identifier or TokenKind.Keyword => t.Text.ToUpperInvariant(),
        _ => string.Empty,
    };

    // ── Sources ──────────────────────────────────────────────────────────────────────────────

    private static void AddInScopeSymbols(
        SemanticModel model, int offset, List<CompletionItem> items, HashSet<(string, CompletionItemKind)> seen)
    {
        foreach (var sym in model.SymbolsInScope(offset))
        {
            if (string.IsNullOrEmpty(sym.Name)) continue;
            var kind = MapKind(sym.Kind);
            if (kind == CompletionItemKind.Unknown) continue;
            AddItem(items, seen, sym.Name, kind, DetailFor(sym));
        }
    }

    // Unqualified column completion — the "implicit target" (user request, 2026-07-13). When the caret
    // is in an expression/value position AND the current scope has EXACTLY ONE table, offer that
    // table's columns without requiring an alias/qualifier (`FROM ROZLICZENIE WHERE |` → its columns).
    // The moment 2+ distinct tables are in scope (JOINs, comma joins, correlated outer tables) we stay
    // silent: the user must qualify (alias.col), so completion never dumps hundreds of ambiguous names
    // and multi-table SQL stays explicit. The binder already resolves bare columns the same way
    // (SemanticBinder.BindBareReference, one-owner rule); this mirrors that for the empty-caret case.
    // Not offered in table position (FROM/JOIN/…), which IsExpressionAnchor excludes.
    private static void AddImplicitTableColumns(
        SemanticModel model, int offset, List<CompletionItem> items, HashSet<(string, CompletionItemKind)> seen)
    {
        var prev = PreviousSignificantToken(model.Syntax.Tokens, offset);
        if (prev is null || !IsExpressionAnchor(prev)) return;

        // Resolve the in-scope tables at the clause keyword (the anchor), not the raw caret: an
        // explicit-trigger caret sitting in trailing whitespace (`… WHERE |`) is past the statement's
        // last token, where ScopeAt falls back to the script scope and loses the FROM tables. The
        // anchor token is always inside the statement span, so it names the right query scope.
        string? table = null;
        Symbol? tableTarget = null;
        foreach (var sym in model.SymbolsInScope(prev.Start))
        {
            if (sym is not TableReferenceSymbol { IsDerived: false, TargetName: { Length: > 0 } target } tsym) continue;
            if (table is null) { table = target; tableTarget = tsym.Target; continue; }
            if (!string.Equals(table, target, StringComparison.OrdinalIgnoreCase)) return; // 2+ tables → require qualification
        }
        if (table is null) return;

        // Same one-owner rule as the dot path: the single in-scope source may be a selectable procedure, whose
        // columns are its output parameters (`FROM MY_PROC(:a) WHERE |` must offer them, like any table).
        foreach (var c in FromSourceColumns.Of(model.Metadata, table, tableTarget))
        {
            if (string.IsNullOrEmpty(c.Name)) continue;
            if (!seen.Add((c.Name, CompletionItemKind.Column))) continue;
            items.Add(ToColumnItem(c, table));
        }
    }

    private static void AddSchemaObjects(
        SemanticModel model, List<CompletionItem> items, HashSet<(string, CompletionItemKind)> seen)
    {
        foreach (var obj in model.Metadata.AllObjects())
        {
            if (string.IsNullOrEmpty(obj.Name)) continue;
            var kind = MapKind(obj.Kind);
            if (kind == CompletionItemKind.Unknown) continue; // e.g. users — not SQL-referenceable
            AddItem(items, seen, obj.Name, kind, detail: null);
        }
    }

    /// <summary>
    /// The keyword vocabulary, minus every word another editor tool is responsible for.
    /// <para><b>One responsibility, one owner.</b> The editor has three: IntelliSense predicts
    /// <i>names</i>; Language Completion finishes <i>constructs</i> (Tab + a shown hint); Typing
    /// Ergonomics maintains <i>delimiter pairs</i> (<c>begin … end</c>). Offering another tool's word here
    /// means two systems competing for one keystroke — the developer types <c>wher</c> and gets
    /// <c>WHERE</c> in the list AND <c>⇥ where</c> beside it, or types <c>begin</c> and is offered a
    /// keyword that a pairing rule should have handled.</para>
    /// <para>Each owner <b>declares</b> its own vocabulary and this method reads those declarations —
    /// never a copy kept in step by hand. A construct added to the catalog, or a new keyword pair, leaves
    /// this list automatically, so the tools cannot drift apart.</para>
    /// <para>Only owned words go: everything else a developer writes (<c>from</c>, <c>join</c>, <c>and</c>,
    /// <c>values</c>, <c>create</c>, datatypes, functions…) is still offered here, because nothing else
    /// completes it.</para>
    /// </summary>
    private static void AddKeywords(List<CompletionItem> items, HashSet<(string, CompletionItemKind)> seen)
    {
        foreach (var kw in FirebirdSyntax.CompletionKeywords)
        {
            if (LanguageConstructCatalog.OwnedWords.Contains(kw)) continue;   // Language Completion
            if (KeywordPairCatalog.OwnedWords.Contains(kw)) continue;         // Typing Ergonomics
            AddItem(items, seen, kw, CompletionItemKind.Keyword, detail: null);
        }
    }

    private static void AddItem(
        List<CompletionItem> items,
        HashSet<(string, CompletionItemKind)> seen,
        string name,
        CompletionItemKind kind,
        string? detail)
    {
        if (!seen.Add((name, kind))) return;
        items.Add(new CompletionItem(name, name, kind, PriorityFor(kind), detail));
    }

    // ── Positional context ranking (M4) ──────────────────────────────────────────────────────

    // Boost added to the contextually-relevant kinds so they sort to the top. Chosen large enough
    // to clearly out-rank the base priorities without collapsing the ordering within a group.
    private const double ContextBoostValue = 100.0;

    private static void ApplyContextRanking(SemanticModel model, int offset, List<CompletionItem> items)
    {
        var prev = PreviousSignificantToken(model.Syntax.Tokens, offset);
        if (prev is null) return;
        var stmtKind = ContainingStatement(model.Syntax, offset)?.Kind;

        for (int j = 0; j < items.Count; j++)
        {
            var boost = ContextBoost(prev, stmtKind, items[j].Kind);
            if (boost != 0d)
            {
                items[j] = items[j] with { SortPriority = items[j].SortPriority + boost };
            }
        }
    }

    // The significant token immediately before the completion position — skipping the partial
    // identifier the caret is currently typing (so "from kontr|" yields FROM, not "kontr").
    private static SqlToken? PreviousSignificantToken(IReadOnlyList<SqlToken> tokens, int offset)
    {
        int i = -1;
        for (int k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.IsEndOfFile) break;
            if (t.Start < offset) i = k; else break;
        }
        if (i < 0) return null;

        // Caret inside/at the end of a word being typed → the anchor is the token before it.
        if (IsWord(tokens[i]) && offset <= tokens[i].End)
        {
            return i - 1 >= 0 ? tokens[i - 1] : null;
        }
        return tokens[i];
    }

    private static SqlStatement? ContainingStatement(SqlScript script, int offset)
    {
        SqlStatement? last = null;
        foreach (var s in script.Statements)
        {
            if (s.SpanContains(offset)) return s;
            if (s.Start <= offset) last = s; // caret at the tail of the last statement
        }
        return last;
    }

    private static double ContextBoost(SqlToken prev, StatementKind? stmtKind, CompletionItemKind itemKind)
    {
        // Table position: FROM / JOIN / INTO / UPDATE.
        if (IsKw(prev, "FROM") || IsKw(prev, "JOIN") || IsKw(prev, "INTO") || IsKw(prev, "UPDATE"))
        {
            return itemKind is CompletionItemKind.Table or CompletionItemKind.View
                or CompletionItemKind.SystemTable or CompletionItemKind.Cte
                ? ContextBoostValue : 0d;
        }

        // EXECUTE PROCEDURE name-position (only for the execute statement — NOT CREATE PROCEDURE).
        if (IsKw(prev, "PROCEDURE") && stmtKind == StatementKind.ExecuteProcedure)
        {
            return itemKind == CompletionItemKind.Procedure ? ContextBoostValue : 0d;
        }

        // Expression / value position → columns, functions, and in-scope locals.
        if (IsExpressionAnchor(prev))
        {
            return itemKind is CompletionItemKind.Column or CompletionItemKind.Function
                or CompletionItemKind.TableAlias or CompletionItemKind.Variable
                or CompletionItemKind.Parameter or CompletionItemKind.RecordAlias
                ? ContextBoostValue : 0d;
        }

        return 0d;
    }

    // A previous token that puts the caret in an expression/value position. Punctuation (comma,
    // "(", operators) and the clause keywords that introduce expressions. Kept deliberately broad:
    // a mis-boost only re-orders (correct items stay present), a miss just falls back to baseline.
    private static bool IsExpressionAnchor(SqlToken prev)
    {
        if (prev.Kind is TokenKind.Comma or TokenKind.LParen or TokenKind.Operator) return true;
        if (prev.Kind != TokenKind.Keyword) return false;
        return ExpressionKeywords.Contains(prev.Text);
    }

    private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WHERE", "HAVING", "ON", "SET", "WHEN", "THEN", "ELSE", "BY", "AND", "OR", "NOT",
        "LIKE", "IN", "BETWEEN", "RETURNING", "VALUES", "RETURN", "DISTINCT", "STARTING", "CONTAINING",
    };

    private static bool IsKw(SqlToken t, string keyword)
        => t.Kind == TokenKind.Keyword && string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase);

    // ── Ordering ─────────────────────────────────────────────────────────────────────────────

    private static void Sort(List<CompletionItem> items)
        => items.Sort(static (a, b) =>
        {
            int byPriority = b.SortPriority.CompareTo(a.SortPriority);
            return byPriority != 0
                ? byPriority
                : string.Compare(a.InsertText, b.InsertText, StringComparison.OrdinalIgnoreCase);
        });

    // ── Mapping / priority ───────────────────────────────────────────────────────────────────

    internal static CompletionItemKind MapKind(SymbolKind kind) => kind switch
    {
        SymbolKind.Table => CompletionItemKind.Table,
        SymbolKind.View => CompletionItemKind.View,
        SymbolKind.SystemTable => CompletionItemKind.SystemTable,
        SymbolKind.Procedure => CompletionItemKind.Procedure,
        SymbolKind.Function => CompletionItemKind.Function,
        SymbolKind.Trigger => CompletionItemKind.Trigger,
        SymbolKind.Domain => CompletionItemKind.Domain,
        SymbolKind.Exception => CompletionItemKind.Exception,
        SymbolKind.Sequence => CompletionItemKind.Sequence,
        SymbolKind.Role => CompletionItemKind.Role,
        SymbolKind.Package => CompletionItemKind.Package,
        SymbolKind.Index => CompletionItemKind.Index,
        SymbolKind.Column => CompletionItemKind.Column,
        SymbolKind.TableReference => CompletionItemKind.TableAlias,
        SymbolKind.Variable => CompletionItemKind.Variable,
        SymbolKind.Parameter => CompletionItemKind.Parameter,
        SymbolKind.Cte => CompletionItemKind.Cte,
        SymbolKind.Cursor => CompletionItemKind.Cursor,
        SymbolKind.RecordAlias => CompletionItemKind.RecordAlias,
        _ => CompletionItemKind.Unknown,
    };

    /// <summary>
    /// Baseline priority by kind (M4 refines by caret position). In-scope locals rank above catalog
    /// objects (they're the most relevant thing to type here); columns top the list for dot
    /// completion; keywords sit at the bottom so a table beats a keyword on an equal prefix.
    /// <para>Public so a caller that builds an item outside a model query — the App's on-demand column
    /// warm, whose columns aren't in the metadata snapshot yet — ranks it from THIS table rather than
    /// keeping a second copy that drifts.</para>
    /// </summary>
    public static double PriorityFor(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Column => 4.0,
        CompletionItemKind.TableAlias => 3.5,
        CompletionItemKind.Variable => 3.5,
        CompletionItemKind.Parameter => 3.5,
        CompletionItemKind.Cte => 3.5,
        CompletionItemKind.Cursor => 3.5,
        CompletionItemKind.RecordAlias => 3.5,
        CompletionItemKind.Table => 3.0,
        CompletionItemKind.View => 3.0,
        CompletionItemKind.Procedure => 3.0,
        CompletionItemKind.Function => 2.5,
        CompletionItemKind.Trigger => 2.0,
        CompletionItemKind.Domain => 2.0,
        CompletionItemKind.Exception => 2.0,
        CompletionItemKind.Sequence => 2.0,
        CompletionItemKind.Role => 2.0,
        CompletionItemKind.Package => 2.0,
        CompletionItemKind.Index => 2.0,
        CompletionItemKind.SystemTable => 2.0,
        CompletionItemKind.Keyword => 1.0,
        _ => 0.0,
    };

    // A column's type (or a symbol's data type) surfaces as the item detail for the ": TYPE" suffix.
    private static string? DetailFor(Symbol sym) => sym.DataType;

    private sealed class NameKindComparer : IEqualityComparer<(string, CompletionItemKind)>
    {
        public static readonly NameKindComparer Instance = new();

        public bool Equals((string, CompletionItemKind) x, (string, CompletionItemKind) y)
            => x.Item2 == y.Item2 && string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, CompletionItemKind) obj)
            => System.HashCode.Combine(
                obj.Item1 is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                obj.Item2);
    }
}
