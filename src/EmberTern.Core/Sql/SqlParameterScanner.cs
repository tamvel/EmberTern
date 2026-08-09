using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql;

/// <summary>One occurrence of a named parameter (<c>:name</c> or <c>@name</c>) in a SQL
/// statement — its name, source offset, total length (marker + name), and marker char.</summary>
public sealed record SqlParameter(string Name, int Offset, int Length, char Marker);

/// <summary>
/// Extracts named parameters (<c>:name</c> / <c>@name</c>) from a SQL statement for the "Smart SQL
/// Parameters" feature. Built on the shared <see cref="SqlLexer"/> (Etap 2): parameters are simply
/// the lexer's <see cref="TokenKind.Parameter"/> tokens, so string literals (<c>'…'</c>), quoted
/// identifiers (<c>"…"</c>) and comments are already opaque, and <c>::</c> is the cast operator
/// (not a parameter). EXECUTE BLOCK is excluded because its <c>:vars</c> are block locals, not
/// input parameters.
/// </summary>
public static class SqlParameterScanner
{
    /// <summary>Every <c>:name</c> / <c>@name</c> occurrence in order, with offsets. Positional
    /// <c>?</c> markers (which have no name) and everything inside literals/comments/quoted
    /// identifiers are excluded — a direct consequence of the lexer's token kinds.</summary>
    public static IReadOnlyList<SqlParameter> Scan(string? sql)
    {
        var result = new List<SqlParameter>();
        if (string.IsNullOrEmpty(sql)) return result;

        foreach (var token in SqlLexer.Tokenize(sql!))
        {
            if (token.Kind != TokenKind.Parameter || token.Length < 2) continue;
            char marker = token.Text[0];
            if (marker is ':' or '@')
            {
                result.Add(new SqlParameter(token.Text.Substring(1), token.Start, token.Length, marker));
            }
        }
        return result;
    }

    /// <summary>
    /// Rewrites every scanned <c>:name</c> / <c>@name</c> to the driver's <c>@name</c> marker,
    /// normalizing case-insensitive-equal names to the first occurrence's spelling, and returns the
    /// rewritten SQL plus the ordered unique parameter names (without the <c>@</c>). Literals and
    /// comments are untouched (they were never scanned). No parameters → the SQL is returned as-is.
    /// </summary>
    public static (string Sql, IReadOnlyList<string> Names) RewriteToDriverMarkers(string? sql)
    {
        var occurrences = Scan(sql);
        if (occurrences.Count == 0) return (sql ?? string.Empty, Array.Empty<string>());

        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var p in occurrences)
        {
            if (!canonical.ContainsKey(p.Name)) { canonical[p.Name] = p.Name; order.Add(p.Name); }
        }

        var sb = new StringBuilder(sql!.Length + occurrences.Count);
        int prev = 0;
        foreach (var p in occurrences) // ascending offset
        {
            sb.Append(sql, prev, p.Offset - prev);
            sb.Append('@').Append(canonical[p.Name]);
            prev = p.Offset + p.Length;
        }
        sb.Append(sql, prev, sql.Length - prev);
        return (sb.ToString(), order);
    }

    /// <summary>True when the statement is an EXECUTE BLOCK — its <c>:vars</c> are block locals,
    /// NOT input parameters, so it must be excluded from named-parameter collection. Determined
    /// from the parsed statement kind.</summary>
    public static bool IsExecuteBlock(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return false;
        var statements = SqlParser.Parse(sql!).Root.Statements;
        return statements.Count > 0 && statements[0] is ExecuteBlockStatement;
    }

    /// <summary>
    /// Every routine invocation in the statement, in source order — asked of the MODEL, not of the syntax.
    ///
    /// <para>⭐⭐ <b>This one line replaces a growing list of statement shapes, and that is the whole point.</b>
    /// This method used to test for <c>ExecuteProcedureStatement</c>; then also for a <c>SelectStatement</c> whose
    /// <c>FROM</c> held a call; and each version left the NEXT syntax silently unhandled — a PSQL
    /// <c>FOR SELECT … FROM P(…) INTO …</c>, an <c>INSERT … SELECT … FROM P(…)</c>, a CTE body, a
    /// <c>MERGE … USING P(…)</c>, a cursor declaration, a call nested in any subquery of any of those. The user
    /// reported the same defect three times, each time on a syntax the previous fix had not enumerated.</para>
    ///
    /// <para>⭐ <see cref="IRoutineInvocation"/> makes "a routine is invoked here with these arguments" a fact the
    /// tree carries, so this walk finds every one of those shapes — and every shape added later — without knowing
    /// what a statement is. ⛔ Do not add a statement-kind branch here; if a call is not found, the parser is not
    /// modelling it, and THAT is where the fix belongs (Contract #1).</para>
    ///
    /// <para>⚠ Nested calls are included deliberately. A placeholder that is the whole argument of a call inside a
    /// derived table is still provably that routine's parameter; which invocation owns which placeholder is
    /// decided per placeholder by <see cref="MapNamesToArgumentSlots"/>, not by picking one call per statement.</para>
    /// </summary>
    public static IReadOnlyList<IRoutineInvocation> RoutineInvocations(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return Array.Empty<IRoutineInvocation>();

        return SqlParser.Parse(sql!).Root
            .DescendantNodesAndSelf()
            .OfType<IRoutineInvocation>()
            .Where(c => c.RoutineName is { Length: > 0 })
            .ToList();
    }

    /// <summary>The catalog name to look a routine's parameters up under — the package qualifier is kept, so a
    /// packaged member resolves as <c>PKG.PROC</c> rather than a nonexistent standalone <c>PROC</c>.</summary>
    public static string CatalogName(IRoutineInvocation call)
        => call.PackageName is null ? call.RoutineName! : call.PackageName + "." + call.RoutineName;

    /// <summary>Where a placeholder's declared type comes from.</summary>
    public enum TypeSourceKind
    {
        /// <summary>Nothing provable — the value stays untyped.</summary>
        None,

        /// <summary>An input parameter of an invoked routine (<see cref="ParameterTypeSource.Slot"/>).</summary>
        RoutineParameter,

        /// <summary>A column of the table being written to (<see cref="ParameterTypeSource.ColumnName"/>).</summary>
        TableColumn,
    }

    /// <summary>
    /// The ONE answer a consumer gets about a placeholder: which database object declares its type, and which
    /// member of that object.
    ///
    /// <para>⭐⭐ <b>This exists so the consumer never asks "what kind of statement is this?".</b> A placeholder's
    /// type has exactly two provable origins in Firebird DML — it is a routine argument, or it is a value written
    /// into a column — and both are facts the AST carries (<see cref="IRoutineInvocation"/>,
    /// <see cref="IColumnValueTarget"/>). The resolver walks for both and returns this; the caller switches on
    /// <see cref="Kind"/> to pick a catalog, not on a syntax. ⛔ The user's standing rule: no per-statement
    /// branches, here or in any consumer. A shape whose pairing the AST cannot prove yields
    /// <see cref="TypeSourceKind.None"/>, which is an honest "unknown", not a gap to patch with an if.</para>
    ///
    /// <para>⚠ The OWNER travels with the member. A statement can invoke two routines, or write to a table while
    /// invoking a routine, so a bare slot or column name would be meaningless — and the caller would have to
    /// guess whose it is, which is the class of mistake this type prevents.</para>
    /// </summary>
    public readonly record struct ParameterTypeSource(
        TypeSourceKind Kind, string? Owner, int Slot, string? ColumnName)
    {
        /// <summary>Nothing provable about this placeholder.</summary>
        public static ParameterTypeSource None => new(TypeSourceKind.None, null, -1, null);

        /// <summary>Argument <paramref name="slot"/> of routine <paramref name="routine"/>.</summary>
        public static ParameterTypeSource Argument(string routine, int slot)
            => new(TypeSourceKind.RoutineParameter, routine, slot, null);

        /// <summary>Column <paramref name="column"/> of table <paramref name="table"/>.</summary>
        public static ParameterTypeSource Column(string table, string column)
            => new(TypeSourceKind.TableColumn, table, -1, column);

        /// <summary>True when a database object and one of its members are both named.</summary>
        public bool IsResolved => Kind != TypeSourceKind.None && Owner is { Length: > 0 };
    }

    /// <summary>
    /// For each name in <paramref name="names"/> (the ordered unique names <see cref="RewriteToDriverMarkers"/>
    /// returned, in that order), the 0-based <b>argument slot</b> it occupies in the statement's routine call —
    /// or <c>-1</c> when it does not provably occupy one.
    ///
    /// <para>⭐ <b>Why this exists: a placeholder is not the same thing as an input parameter, and counting them
    /// was the bug.</b> The caller used to type the placeholders only when
    /// <c>catalog.Count == names.Count</c>, and fall back to "Unknown" for all of them otherwise. Three ordinary
    /// statements break that equality while being perfectly typeable, and in each one the user loses every type:
    /// a call that omits parameters which have DEFAULTS (routine in ERP code), a call that repeats one
    /// placeholder, and a call with <c>RETURNING_VALUES :r</c> — whose target is a placeholder but is not an
    /// input argument at all.</para>
    ///
    /// <para>⭐ <b>And positional slots are not a guess.</b> Firebird binds <c>EXECUTE PROCEDURE</c> arguments
    /// positionally, so "the placeholder standing in argument slot <i>i</i> is input parameter <i>i</i>" is the
    /// language's own rule, read here from the parser's <see cref="ExecuteProcedureStatement.Arguments"/> spans
    /// rather than inferred from a count. This is strictly MORE provable than the equality it replaces, not a
    /// loosening of it.</para>
    ///
    /// <para>⚠ Deliberately strict in three ways, so nothing is ever typed on a hunch (rule #11):
    /// a slot counts only when the placeholder is the <b>whole</b> argument (<c>:a</c>, never <c>:a + 1</c> —
    /// there the parameter's own type is not the argument's); a name appearing in <b>two different</b> slots is
    /// ambiguous and gets <c>-1</c> (one value, two possibly-different declared types); and a statement that
    /// calls no routine maps entirely to <c>-1</c>.</para>
    ///
    /// <para>⚠ Resolved across <b>every</b> invocation in the statement, not one chosen call: a statement may
    /// invoke several routines (a join of two selectable procedures, a call inside a subquery), and each
    /// placeholder belongs to whichever one it stands in. A name that is a whole argument of <b>two different</b>
    /// routines, or of two different slots, is ambiguous and stays unmapped — one value cannot carry two declared
    /// types.</para>
    /// </summary>
    public static IReadOnlyList<ParameterTypeSource> ResolveTypeSources(
        string? sql, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var result = new ParameterTypeSource[names.Count];
        Array.Fill(result, ParameterTypeSource.None);
        if (string.IsNullOrEmpty(sql) || names.Count == 0) return result;

        var root = SqlParser.Parse(sql!).Root;
        var occurrences = Scan(sql);

        // name -> the single source it has; anything inconsistent marks it unprovable.
        var bound = new Dictionary<string, ParameterTypeSource>(StringComparer.OrdinalIgnoreCase);

        void Claim(string name, ParameterTypeSource source)
        {
            if (bound.TryGetValue(name, out var existing))
            {
                if (!existing.Equals(source)) bound[name] = ParameterTypeSource.None;
            }
            else
            {
                bound[name] = source;
            }
        }

        // Fact 1 — the placeholder is an argument of an invoked routine.
        foreach (var call in root.DescendantNodesAndSelf().OfType<IRoutineInvocation>())
        {
            if (call.RoutineName is not { Length: > 0 }) continue;
            for (int slot = 0; slot < call.Arguments.Count; slot++)
            {
                var name = WholeValueParameterName(sql!, call.Arguments[slot].Start, call.Arguments[slot].Length, occurrences);
                if (name is not null) Claim(name, ParameterTypeSource.Argument(CatalogName(call), slot));
            }
        }

        // Fact 2 — the placeholder is the value written into a named column.
        foreach (var target in root.DescendantNodesAndSelf().OfType<IColumnValueTarget>())
        {
            if (target.TargetTable is not { Length: > 0 } table) continue;
            foreach (var cv in target.ColumnValues)
            {
                var name = WholeValueParameterName(sql!, cv.Start, cv.Length, occurrences);
                if (name is not null) Claim(name, ParameterTypeSource.Column(table, cv.ColumnName));
            }
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (bound.TryGetValue(names[i], out var b)) result[i] = b;
        }

        return result;
    }

    // The parameter name filling this value span entirely, or null. "Entirely" is measured against the span with
    // surrounding whitespace trimmed — the parser's spans cover the value's tokens, so a value that is exactly one
    // placeholder has exactly one occurrence spanning all of it. ⚠ Anything else (`:a + 1`, a function call around
    // it) yields null: there the placeholder's own type is NOT the parameter's or the column's declared type.
    private static string? WholeValueParameterName(
        string sql, int spanStart, int spanLength, IReadOnlyList<SqlParameter> occurrences)
    {
        int start = spanStart;
        int end = spanStart + spanLength;
        if (end > sql.Length) end = sql.Length;
        while (start < end && char.IsWhiteSpace(sql[start])) start++;
        while (end > start && char.IsWhiteSpace(sql[end - 1])) end--;
        if (end <= start) return null;

        foreach (var p in occurrences)
        {
            if (p.Offset == start && p.Offset + p.Length == end) return p.Name;
        }
        return null;
    }

    // ⛔ `TryExtractExecuteProcedureName` was DELETED (2026-08-03) rather than left beside TryExtractRoutineCall.
    // It answered "which procedure does this EXECUTE PROCEDURE name", which is the same question one call shape
    // at a time — and keeping both would mean two methods that disagree about whether a selectable procedure in
    // a FROM clause counts as a routine call. That disagreement IS the defect this round fixed, so preserving a
    // way to reproduce it would be preserving the bug. One question, one answer (architecture: no parallel
    // implementations of one capability).
}
