using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

// Etap 6.9 / B3.1 — a tiny shared helper for building a node's Children array from an optional principal
// child plus incidental ones, without each node re-writing the same null-filtering/allocation. Callers
// pass children in source order (a statement's principal query precedes its trailing subqueries; a
// FOR-loop cursor precedes its body), so the result is source-ordered by construction — the invariant
// StructuralAstDifferentialTests.AssertWellFormed enforces.
internal static class AstChildren
{
    /// <summary>A principal child (a source/body/cursor <see cref="QueryNode"/>) followed by a list of
    /// incidental children (embedded subquery expressions), nulls filtered. Empty when both are empty.</summary>
    public static SqlNode[] Of(SqlNode? head, IReadOnlyList<SqlNode>? rest)
    {
        int n = (head is null ? 0 : 1) + (rest?.Count ?? 0);
        if (n == 0) return Array.Empty<SqlNode>();
        var arr = new SqlNode[n];
        int k = 0;
        if (head is not null) arr[k++] = head;
        if (rest is not null)
        {
            for (int i = 0; i < rest.Count; i++) arr[k++] = rest[i];
        }
        return arr;
    }

    /// <summary>Two optional children in source order (<paramref name="a"/> before <paramref name="b"/>),
    /// nulls filtered.</summary>
    public static SqlNode[] Of(SqlNode? a, SqlNode? b)
    {
        if (a is null && b is null) return Array.Empty<SqlNode>();
        if (b is null) return new[] { a! };
        if (a is null) return new[] { b };
        return new[] { a, b };
    }
}
