using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Signatures;

/// <summary>
/// Produces parameter/signature help for a call or DML site from a <see cref="SemanticModel"/> and a
/// caret offset — Etap 5 / M6 (design §8 / §5.10). Like the completion engine it is a pure Core
/// client of the language front-end: it reads <b>only</b> the model — the containing statement's AST
/// token stream and the metadata snapshot (<see cref="ISqlMetadataProvider.GetRoutineParameters"/> /
/// <see cref="ISqlMetadataProvider.GetColumns"/>) — never a fresh scan of the raw text (§22.0). The
/// App popup (M7) is thin glue.
/// <para>Scope (§8): EXECUTE PROCEDURE (with or without parens), a function/procedure call in an
/// expression, INSERT column-list ↔ VALUES ↔ INSERT…SELECT projection, and UPDATE SET assignments.
/// Count-mismatch <i>diagnostics</i> are Etap 7, not here. A CREATE PROCEDURE/FUNCTION <i>declaration</i>
/// site has no callee, so it produces no signature — it is a completion/type-list concern, not
/// signature help.</para>
/// </summary>
public static class SignatureHelpEngine
{
    /// <summary>The active-parameter signature at <paramref name="offset"/>, or <c>null</c> when the
    /// caret is not at a recognised call/DML parameter site. Never throws.</summary>
    public static SignatureInfo? GetSignature(SemanticModel model, int offset)
    {
        if (model is null) return null;
        var stmt = ContainingStatement(model.Syntax, offset);
        if (stmt is null) return null;
        var tokens = stmt.Tokens;
        var meta = model.Metadata;

        // 1) Inside a "(…)" — a routine call, or an INSERT column-list / VALUES paren. The innermost
        //    enclosing paren wins, so a function call nested inside VALUES(…) shows the function.
        var openParen = FindEnclosingParen(tokens, offset);
        if (openParen >= 0)
        {
            var callee = CalleeWordBefore(tokens, openParen);
            if (callee is not null)
            {
                var routine = TryRoutineSignature(meta, callee, tokens, openParen, offset, kindHint: null);
                if (routine is not null) return routine;
            }

            // UPDATE OR INSERT INTO t (cols) VALUES (…) has the same INTO + column-list + VALUES shape as
            // a plain INSERT, so it drives the same column↔value Parameter Helper.
            if (stmt.Kind is StatementKind.Insert or StatementKind.UpdateOrInsert)
            {
                var insert = TryInsertParenSignature(tokens, openParen, offset, meta);
                if (insert is not null) return insert;
            }

            // Inside parens but not a recognised call/INSERT paren → no signature.
            return null;
        }

        // 2) Non-paren statement-level sites.
        return stmt switch
        {
            ExecuteProcedureStatement ep => TryExecuteProcedureNoParens(ep, tokens, offset, meta),
            UpdateStatement => TryUpdateSetSignature(tokens, offset, meta),
            InsertStatement => TryInsertSelectSignature(tokens, offset, meta),
            _ => null,
        };
    }

    /// <summary>The INSERT target table when the caret sits at an INSERT column-list / VALUES /
    /// INSERT…SELECT projection position — returned <b>even when the table's columns aren't loaded
    /// yet</b>, so the App can warm them, rebuild the model, and retry <see cref="GetSignature"/>
    /// (the double-click INSERT/VALUES helper needs the columns to exist; on a fresh editor they are
    /// not cached, so <see cref="GetSignature"/> would otherwise return <c>null</c> and the helper
    /// would silently not appear). <c>null</c> when the caret is not at such a site. Never throws.</summary>
    public static string? TryGetInsertTargetTable(SemanticModel model, int offset)
    {
        if (model is null) return null;
        var stmt = ContainingStatement(model.Syntax, offset);
        if (stmt is null || stmt.Kind is not (StatementKind.Insert or StatementKind.UpdateOrInsert)) return null;
        var tokens = stmt.Tokens;

        var table = WordAfterKeyword(tokens, "INTO");
        if (table is null) return null;

        var openParen = FindEnclosingParen(tokens, offset);
        if (openParen >= 0)
        {
            // Only the VALUES(…) paren or the column-list "(…)" right after the table name is an
            // insert-column position — a nested function call inside VALUES (whose preceding word is
            // some other identifier) is not. Mirrors TryInsertParenSignature's own gate.
            var before = openParen - 1 >= 0 ? tokens[openParen - 1] : null;
            if (before is { Kind: TokenKind.Keyword } && Eq(before.Text, "VALUES")) return table;
            if (before is not null && IsWord(before) && Eq(FoldName(before), table)) return table;
            return null;
        }

        // INSERT … SELECT projection.
        int selectKw = IndexOfKeyword(tokens, "SELECT");
        return selectKw >= 0 && offset > tokens[selectKw].End ? table : null;
    }

    // ── Routine calls (EXECUTE PROCEDURE / function call / selectable proc) ────────────────────

    private static SignatureInfo? TryRoutineSignature(
        ISqlMetadataProvider meta,
        string callee,
        IReadOnlyList<SqlToken> tokens,
        int openParen,
        int offset,
        SignatureKind? kindHint)
    {
        var inputs = meta.GetRoutineParameters(callee)
            .Where(p => p.Direction == ParameterDirection.Input)
            .ToList();

        // Only produce a signature for something the snapshot actually knows to be a routine —
        // otherwise "WHERE (…)" / "foo(…)" (a built-in the catalog doesn't carry) would show noise.
        var obj = meta.FindObject(callee);
        var isKnownRoutine = obj is { Kind: SymbolKind.Procedure or SymbolKind.Function };
        if (inputs.Count == 0 && !isKnownRoutine) return null;

        var active = CountTopLevelCommasInParen(tokens, openParen, offset);
        var kind = kindHint ?? (obj?.Kind == SymbolKind.Function ? SignatureKind.Function : SignatureKind.Procedure);
        return new SignatureInfo(callee, inputs.Select(ToParam).ToList(), active, kind);
    }

    private static SignatureInfo? TryExecuteProcedureNoParens(
        ExecuteProcedureStatement ep, IReadOnlyList<SqlToken> tokens, int offset, ISqlMetadataProvider meta)
    {
        if (ep.ProcedureName is not { Length: > 0 } name) return null;

        // "EXECUTE PROCEDURE P a, b" — args follow the name with no parens. Anchor on the PROCEDURE
        // keyword, then the name token; count top-level commas from just after the name to the caret.
        int procKw = IndexOfKeyword(tokens, "PROCEDURE");
        if (procKw < 0) return null;
        int nameIdx = NextWordIndex(tokens, procKw + 1);
        if (nameIdx < 0) return null;
        if (offset <= tokens[nameIdx].End) return null; // caret still on/before the name

        var inputs = meta.GetRoutineParameters(name)
            .Where(p => p.Direction == ParameterDirection.Input)
            .ToList();
        var obj = meta.FindObject(name);
        if (inputs.Count == 0 && obj is not { Kind: SymbolKind.Procedure or SymbolKind.Function }) return null;

        var active = CountTopLevelCommas(tokens, nameIdx, offset, stopKeywords: null);
        return new SignatureInfo(name, inputs.Select(ToParam).ToList(), active, SignatureKind.Procedure);
    }

    // ── INSERT ─────────────────────────────────────────────────────────────────────────────────

    private static SignatureInfo? TryInsertParenSignature(
        IReadOnlyList<SqlToken> tokens, int openParen, int offset, ISqlMetadataProvider meta)
    {
        var table = WordAfterKeyword(tokens, "INTO");
        if (table is null) return null;

        var before = openParen - 1 >= 0 ? tokens[openParen - 1] : null;
        var active = CountTopLevelCommasInParen(tokens, openParen, offset);

        // VALUES (v1, v2, …) → map each value position to the target column.
        if (before is { Kind: TokenKind.Keyword } && Eq(before.Text, "VALUES"))
        {
            var targets = TargetColumns(tokens, table, meta);
            return targets.Count == 0 ? null : new SignatureInfo(table, targets, active, SignatureKind.Insert);
        }

        // Column-list "(c1, c2, …)" right after the table name → show the table's columns.
        if (before is not null && IsWord(before) && Eq(FoldName(before), table))
        {
            var cols = meta.GetColumns(table);
            if (cols.Count == 0) return null;
            var targets = cols.Select(c => ToParam(c, c.Name)).ToList();
            return new SignatureInfo(table, targets, active, SignatureKind.Insert);
        }

        return null;
    }

    // INSERT INTO t [(cols)] SELECT a, b, … — map the projection position to the target column.
    private static SignatureInfo? TryInsertSelectSignature(
        IReadOnlyList<SqlToken> tokens, int offset, ISqlMetadataProvider meta)
    {
        var table = WordAfterKeyword(tokens, "INTO");
        if (table is null) return null;

        int selectKw = IndexOfKeyword(tokens, "SELECT");
        if (selectKw < 0 || offset <= tokens[selectKw].End) return null;

        var targets = TargetColumns(tokens, table, meta);
        if (targets.Count == 0) return null;

        var active = CountTopLevelCommas(tokens, selectKw, offset, ProjectionStops);
        return new SignatureInfo(table, targets, active, SignatureKind.Insert);
    }

    // The INSERT target columns: the explicit column list when present, else the table's own columns.
    private static List<SignatureParameter> TargetColumns(
        IReadOnlyList<SqlToken> tokens, string table, ISqlMetadataProvider meta)
    {
        var cols = meta.GetColumns(table);
        var explicitList = ExplicitInsertColumns(tokens, table);
        if (explicitList.Count > 0)
        {
            var byName = cols.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            return explicitList
                .Select(n => byName.TryGetValue(n, out var c) ? ToParam(c, n) : new SignatureParameter(n, string.Empty))
                .ToList();
        }
        return cols.Select(c => ToParam(c, c.Name)).ToList();
    }

    // The names in the "(c1, c2, …)" column list right after the INSERT target table, or empty when
    // there is no explicit list.
    private static List<string> ExplicitInsertColumns(IReadOnlyList<SqlToken> tokens, string table)
    {
        var result = new List<string>();
        int into = IndexOfKeyword(tokens, "INTO");
        if (into < 0) return result;
        int nameIdx = NextWordIndex(tokens, into + 1);
        if (nameIdx < 0) return result;
        int open = nameIdx + 1;
        if (open >= tokens.Count || tokens[open].Kind != TokenKind.LParen) return result;

        int depth = 0;
        for (int i = open; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { depth--; if (depth == 0) break; continue; }
            if (depth == 1 && IsWord(t)) result.Add(FoldName(t));
        }
        return result;
    }

    // ── INSERT count reuse (Stage 7 / S2 — DiagnosticsEngine.InsertCountMismatch) ──────────────

    /// <summary>
    /// The (column-count, value-count) and the source span of the <c>VALUES</c> list of an
    /// <c>INSERT INTO t (c1, …) VALUES (v1, …)</c> — computed with this engine's own INSERT token-shape
    /// reading so the Diagnostics engine's <c>InsertCountMismatch</c> check reuses one INSERT parser
    /// rather than standing up a parallel scanner (design §10). Returns <c>null</c> (⇒ nothing to compare,
    /// stay silent) unless the statement has BOTH an explicit column list AND a single, cleanly-parseable
    /// <c>VALUES</c> row: an <c>INSERT … SELECT</c> / <c>… DEFAULT VALUES</c>, a missing/empty/malformed
    /// list, or a (non-Firebird) multi-row <c>VALUES</c> all yield <c>null</c>. Counts are of top-level
    /// (depth-1) comma-separated items, so a comma inside a function call or nested paren never inflates
    /// them. Pure over <paramref name="tokens"/>; never throws.
    /// </summary>
    internal static (int Columns, int Values, int ValuesStart, int ValuesLength)? InsertColumnAndValueCounts(
        IReadOnlyList<SqlToken> tokens)
    {
        int into = IndexOfKeyword(tokens, "INTO");
        if (into < 0) return null;
        int nameIdx = NextWordIndex(tokens, into + 1);
        if (nameIdx < 0) return null;

        // Skip a dotted target name (schema.table) to reach the position right after the table.
        int afterName = nameIdx + 1;
        while (afterName + 1 < tokens.Count
               && tokens[afterName].Kind == TokenKind.Dot && IsWord(tokens[afterName + 1]))
        {
            afterName += 2;
        }

        // An explicit column list "(...)" must immediately follow the table name; without it there is
        // nothing to compare against (columns default to the table's own — not a written-list mismatch).
        if (afterName >= tokens.Count || tokens[afterName].Kind != TokenKind.LParen) return null;
        int columns = CountParenItems(tokens, afterName);
        if (columns <= 0) return null;

        // VALUES (...) — the row paren immediately after the top-level VALUES keyword.
        int valuesKw = IndexOfKeyword(tokens, "VALUES");
        if (valuesKw < 0) return null; // INSERT … SELECT / DEFAULT VALUES — not a columns↔VALUES check
        int valOpen = valuesKw + 1 < tokens.Count && tokens[valuesKw + 1].Kind == TokenKind.LParen
            ? valuesKw + 1 : -1;
        if (valOpen < 0) return null;

        int values = CountParenItems(tokens, valOpen);
        if (values <= 0) return null;

        int valClose = MatchingParen(tokens, valOpen);
        if (valClose < 0) return null;

        // Firebird has no multi-row VALUES; a comma right after the row paren means a second row (or
        // malformed input) — never guess, stay silent.
        if (valClose + 1 < tokens.Count && tokens[valClose + 1].Kind == TokenKind.Comma) return null;

        int start = tokens[valOpen].Start;
        return (columns, values, start, tokens[valClose].End - start);
    }

    // The number of top-level (depth-1) comma-separated items inside the paren opening at
    // <paramref name="openIdx"/>. Returns 0 for an empty paren, a malformed list (empty/leading/trailing
    // segment), or an unbalanced paren — the caller treats 0 as "don't compare" (prefer silence). A comma
    // nested inside a deeper paren (a function argument) is content, not a separator.
    private static int CountParenItems(IReadOnlyList<SqlToken> tokens, int openIdx)
    {
        if (openIdx >= tokens.Count || tokens[openIdx].Kind != TokenKind.LParen) return 0;
        int depth = 0, items = 0, segTokens = 0;
        for (int i = openIdx; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t.Kind)
            {
                case TokenKind.LParen:
                    depth++;
                    if (depth > 1) segTokens++;
                    break;
                case TokenKind.RParen:
                    depth--;
                    if (depth == 0) return segTokens > 0 ? items + 1 : 0; // final segment; 0 = empty/trailing
                    segTokens++;
                    break;
                case TokenKind.Comma when depth == 1:
                    if (segTokens == 0) return 0; // empty segment (leading/double comma) → malformed
                    items++;
                    segTokens = 0;
                    break;
                default:
                    if (depth >= 1) segTokens++;
                    break;
            }
        }
        return 0; // unbalanced
    }

    // The index of the ')' matching the '(' at openIdx, or -1 when unbalanced.
    private static int MatchingParen(IReadOnlyList<SqlToken> tokens, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.LParen) depth++;
            else if (tokens[i].Kind == TokenKind.RParen && --depth == 0) return i;
        }
        return -1;
    }

    // ── UPDATE SET ───────────────────────────────────────────────────────────────────────────

    private static SignatureInfo? TryUpdateSetSignature(
        IReadOnlyList<SqlToken> tokens, int offset, ISqlMetadataProvider meta)
    {
        var table = WordAfterKeyword(tokens, "UPDATE");
        if (table is null) return null;

        int setKw = IndexOfKeyword(tokens, "SET");
        if (setKw < 0 || offset <= tokens[setKw].End) return null;

        // Bail once we've passed the assignment list (WHERE/ORDER/…) — the caret must be within SET.
        int stopIdx = IndexOfAnyKeyword(tokens, SetStops, setKw + 1);
        if (stopIdx >= 0 && offset > tokens[stopIdx].Start) return null;

        var cols = meta.GetColumns(table);
        var byName = cols.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        // The assigned column of each top-level "col = expr" segment (the word before its first
        // depth-0 '='). Segments are separated by depth-0 commas between SET and WHERE/end.
        var assigned = SetAssignmentColumns(tokens, setKw, stopIdx);
        if (assigned.Count == 0) return null;

        var active = CountTopLevelCommas(tokens, setKw, offset, SetStops);
        var targets = assigned
            .Select(n => byName.TryGetValue(n, out var c) ? ToParam(c, n) : new SignatureParameter(n, string.Empty))
            .ToList();
        return new SignatureInfo(table, targets, active, SignatureKind.Update);
    }

    // The column name of each top-level assignment in "SET c1 = …, c2 = …": the word at the start of
    // each depth-0 comma-separated segment (before its '=').
    private static List<string> SetAssignmentColumns(IReadOnlyList<SqlToken> tokens, int setKw, int stopIdx)
    {
        var result = new List<string>();
        int end = stopIdx >= 0 ? stopIdx : tokens.Count;
        int depth = 0;
        bool segmentStart = true;
        for (int i = setKw + 1; i < end; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (t.Kind == TokenKind.Comma) { segmentStart = true; continue; }
            if (segmentStart && IsWord(t))
            {
                result.Add(FoldName(t));
                segmentStart = false;
            }
        }
        return result;
    }

    // ── Token scanning helpers (over a statement's significant tokens) ─────────────────────────

    // The innermost '(' still open at the caret, or -1. A '(' whose matching ')' is also before the
    // caret has been popped, so it is not "enclosing".
    private static int FindEnclosingParen(IReadOnlyList<SqlToken> tokens, int offset)
    {
        var stack = new Stack<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Start >= offset) break;
            if (t.Kind == TokenKind.LParen) stack.Push(i);
            else if (t.Kind == TokenKind.RParen && stack.Count > 0) stack.Pop();
        }
        return stack.Count > 0 ? stack.Peek() : -1;
    }

    // The identifier immediately before a '(' — a call callee. Keywords (e.g. WHERE, IN, VALUES) are
    // not callees, so they return null and the paren is handled by the INSERT/other paths.
    private static string? CalleeWordBefore(IReadOnlyList<SqlToken> tokens, int openParen)
    {
        int i = openParen - 1;
        if (i < 0) return null;
        var t = tokens[i];
        return t.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier ? FoldName(t) : null;
    }

    private static int CountTopLevelCommasInParen(IReadOnlyList<SqlToken> tokens, int openParen, int offset)
        => CountTopLevelCommas(tokens, openParen, offset, stopKeywords: null);

    // Counts depth-0 commas in tokens (startIndex, caret). Depth is relative to startIndex; a depth-0
    // ')' ends the region (paren close), as does a depth-0 stop keyword.
    private static int CountTopLevelCommas(
        IReadOnlyList<SqlToken> tokens, int startIndex, int offset, ISet<string>? stopKeywords)
    {
        int depth = 0, commas = 0;
        for (int i = startIndex + 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Start >= offset) break;
            switch (t.Kind)
            {
                case TokenKind.LParen: depth++; break;
                case TokenKind.RParen: if (depth == 0) return commas; depth--; break;
                case TokenKind.Comma: if (depth == 0) commas++; break;
                case TokenKind.Keyword:
                    if (depth == 0 && stopKeywords is not null && stopKeywords.Contains(t.Text)) return commas;
                    break;
            }
        }
        return commas;
    }

    private static int IndexOfKeyword(IReadOnlyList<SqlToken> tokens, string kw, int from = 0)
    {
        int depth = 0;
        for (int i = from; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && t.Kind == TokenKind.Keyword && Eq(t.Text, kw)) return i;
        }
        return -1;
    }

    private static int IndexOfAnyKeyword(IReadOnlyList<SqlToken> tokens, ISet<string> kws, int from)
    {
        int depth = 0;
        for (int i = from; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && t.Kind == TokenKind.Keyword && kws.Contains(t.Text)) return i;
        }
        return -1;
    }

    private static int NextWordIndex(IReadOnlyList<SqlToken> tokens, int from)
    {
        for (int i = from; i < tokens.Count; i++)
        {
            if (IsWord(tokens[i])) return i;
        }
        return -1;
    }

    private static string? WordAfterKeyword(IReadOnlyList<SqlToken> tokens, string kw)
    {
        int kwIdx = IndexOfKeyword(tokens, kw);
        if (kwIdx < 0) return null;
        int w = NextWordIndex(tokens, kwIdx + 1);
        return w < 0 ? null : FoldName(tokens[w]);
    }

    private static SqlStatement? ContainingStatement(SqlScript script, int offset)
    {
        SqlStatement? last = null;
        foreach (var s in script.Statements)
        {
            if (s.SpanContains(offset)) return s;
            if (s.Start <= offset) last = s;
        }
        return last;
    }

    private static bool IsWord(SqlToken t)
        => t.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier;

    private static string FoldName(SqlToken t) => t.Kind switch
    {
        TokenKind.QuotedIdentifier => t.Value,
        _ => t.Text.ToUpperInvariant(),
    };

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static SignatureParameter ToParam(RoutineParameterMetadata p)
        => new(p.Name, p.Type, p.Direction, p.Nullable, p.DefaultValue, p.Description);

    private static SignatureParameter ToParam(ColumnMetadata c, string name)
        => new(name, c.Type, ParameterDirection.Input, c.Nullable, c.DefaultValue, c.Description);

    private static readonly HashSet<string> SetStops = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "ORDER", "GROUP", "HAVING", "PLAN", "RETURNING", "ROWS",
    };

    private static readonly HashSet<string> ProjectionStops = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "WHERE", "GROUP", "ORDER", "HAVING", "PLAN", "INTO", "ROWS", "UNION",
    };
}
