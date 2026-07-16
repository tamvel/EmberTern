using System;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The binder's cursor-USAGE recognition (Stage 7 / S6 groundwork). The binder already models cursor
/// DECLARATIONS (<c>DECLARE … CURSOR</c>); this pins that it now also models cursor USAGES — an
/// <c>OPEN</c> / <c>FETCH</c> / <c>CLOSE</c> operand becomes an ordinary <see cref="SymbolReference"/>
/// with <see cref="ReferenceRole.Cursor"/>, resolved to the declaration when in scope and unresolved
/// otherwise. This is general semantic infrastructure (navigation, find-refs, rename, highlighting,
/// quick info, diagnostics all read it), not diagnostics-specific. Pure — no window, no DB.
/// </summary>
public class CursorUsageBindingTests
{
    private static SemanticModel Build(string sql) => SemanticModel.Build(sql);

    // A cursor declared and used in a CREATE PROCEDURE — OPEN/CLOSE operands resolve to the cursor.
    [Fact]
    public void OpenAndCloseCursor_ResolveToDeclaredCursor()
    {
        const string sql =
            "create procedure p\n" +
            "as\n" +
            "declare c cursor for (select 1 from rdb$database);\n" +
            "begin\n" +
            "  open c;\n" +
            "  close c;\n" +
            "end";
        var model = Build(sql);

        foreach (var op in new[] { "open c", "close c" })
        {
            int at = sql.IndexOf(op, StringComparison.Ordinal) + op.Length - 1; // on the cursor name
            var r = model.ReferenceAt(at);
            Assert.NotNull(r);
            Assert.Equal(ReferenceRole.Cursor, r!.Role);
            Assert.True(r.IsResolved);
            Assert.IsType<CursorSymbol>(r.Symbol);
        }
    }

    // An undeclared cursor operand is still recorded — as an UNRESOLVED cursor reference.
    [Fact]
    public void OpenUndeclaredCursor_IsRecordedUnresolved()
    {
        const string sql = "execute block as begin open nosuch; end";
        var model = Build(sql);

        int openC = sql.IndexOf("open nosuch", StringComparison.Ordinal) + "open ".Length;
        var r = model.ReferenceAt(openC);
        Assert.NotNull(r);
        Assert.Equal(ReferenceRole.Cursor, r!.Role);
        Assert.False(r.IsResolved);
    }

    // FETCH binds its cursor operand AND still binds the INTO target variables (the cursor op does not
    // swallow the rest of the statement).
    [Fact]
    public void FetchCursor_BindsCursorAndIntoVariables()
    {
        const string sql =
            "create procedure p\n" +
            "as\n" +
            "declare c cursor for (select 1 from rdb$database);\n" +
            "declare variable v integer;\n" +
            "begin\n" +
            "  fetch c into :v;\n" +
            "end";
        var model = Build(sql);

        int fetchC = sql.IndexOf("fetch c", StringComparison.Ordinal) + "fetch ".Length;
        var cur = model.ReferenceAt(fetchC);
        Assert.Equal(ReferenceRole.Cursor, cur!.Role);
        Assert.True(cur.IsResolved);

        int vIdx = sql.IndexOf(":v", StringComparison.Ordinal);
        var v = model.ReferenceAt(vIdx);
        Assert.Equal(ReferenceRole.Variable, v!.Role);
        Assert.True(v.IsResolved);
    }
}
