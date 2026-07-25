using System;
using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X / D6 — the pure Cursor Bridge query builder (spec §7). Proves the frame-reference rewrite rules
/// against the REAL <see cref="SemanticModel"/> (strict whole-routine parse, gotcha #238) without a server:
/// bare local refs (binder) and colon/@ params (lexer) both become positional <c>?</c> parameters by span,
/// in source order; the INTO targets ride along for the positional column mapping.
/// </summary>
public class CursorBridgeTests
{
    private static (CursorQueryPlan Plan, string Source) Plan(string sql)
    {
        var loop = SqlParser.Parse(sql).Root.DescendantNodesAndSelf().OfType<ForSelectStatement>().Single();
        return (CursorBridge.Build(sql, loop), sql);
    }

    // A single-line FOR SELECT so the query slice (and thus the expected rewritten SQL) is deterministic.
    private const string ColonForm =
        "CREATE PROCEDURE P (PIN INTEGER) RETURNS (AOUT INTEGER) AS " +
        "DECLARE VARIABLE V1 INTEGER; DECLARE VARIABLE V2 INTEGER; BEGIN V1 = 5; " +
        "FOR SELECT ID, VAL FROM SOME_TABLE WHERE OWNER = :PIN AND FLAG = V1 INTO :V2, :AOUT DO " +
        "BEGIN SUSPEND; END END";

    private const string BareForm =
        "CREATE PROCEDURE P (PIN INTEGER) RETURNS (AOUT INTEGER) AS " +
        "DECLARE VARIABLE V1 INTEGER; DECLARE VARIABLE V2 INTEGER; BEGIN V1 = 5; " +
        "FOR SELECT ID, VAL FROM SOME_TABLE WHERE OWNER = PIN AND FLAG = V1 INTO V2, AOUT DO " +
        "BEGIN SUSPEND; END END";

    [Fact]
    public void ColonParam_Rewritten_BareLeftAsColumn()
    {
        // :PIN (unambiguous variable ref) → ?; bare V1 is a column ref in DSQL and is left untouched.
        var (plan, _) = Plan(ColonForm);
        Assert.Equal("SELECT ID, VAL FROM SOME_TABLE WHERE OWNER = ? AND FLAG = V1", plan.Sql);
        Assert.Equal(new[] { "PIN" }, plan.ParameterNames);
        Assert.Equal(new[] { "V2", "AOUT" }, plan.IntoTargets);
    }

    [Fact]
    public void BareRefs_LeftAsColumns_NotRewritten()
    {
        // No colon params in the query → nothing is rewritten (a bare name is a column in DSQL — rewriting it
        // mis-handles a column that shadows a frame variable/parameter name, §15.5 / SQL -804).
        var (plan, _) = Plan(BareForm);
        Assert.Equal("SELECT ID, VAL FROM SOME_TABLE WHERE OWNER = PIN AND FLAG = V1", plan.Sql);
        Assert.Empty(plan.ParameterNames);
        Assert.Equal(new[] { "V2", "AOUT" }, plan.IntoTargets);
    }

    [Fact]
    public void ColumnSharingAnOutputParamName_NotRewritten()
    {
        // LINE_NO is both a RETURNS parameter and the selected column; the SELECT-list column must stay a
        // column (the binder would resolve the bare name to the param — we must NOT act on that here).
        const string sql =
            "CREATE PROCEDURE P (P_ORDER INTEGER) RETURNS (LINE_NO INTEGER, AMOUNT NUMERIC(15,2)) AS BEGIN " +
            "FOR SELECT LINE_NO, QTY FROM ORDER_ITEMS WHERE ORDER_ID = :P_ORDER INTO :LINE_NO, :AMOUNT DO SUSPEND; END";
        var (plan, _) = Plan(sql);
        Assert.Equal("SELECT LINE_NO, QTY FROM ORDER_ITEMS WHERE ORDER_ID = ?", plan.Sql);
        Assert.Equal(new[] { "P_ORDER" }, plan.ParameterNames);
        Assert.Equal(new[] { "LINE_NO", "AMOUNT" }, plan.IntoTargets);
    }

    [Fact]
    public void NoFrameRefs_QueryUnchanged_NoParams()
    {
        const string sql =
            "CREATE PROCEDURE P RETURNS (AOUT INTEGER) AS BEGIN " +
            "FOR SELECT ID FROM SOME_TABLE WHERE OWNER = 1 INTO :AOUT DO SUSPEND; END";
        var (plan, _) = Plan(sql);
        Assert.Equal("SELECT ID FROM SOME_TABLE WHERE OWNER = 1", plan.Sql);
        Assert.Empty(plan.ParameterNames);
        Assert.Equal(new[] { "AOUT" }, plan.IntoTargets);
    }

    [Fact]
    public void RepeatedRef_EachOccurrenceBecomesItsOwnParam()
    {
        const string sql =
            "CREATE PROCEDURE P (PIN INTEGER) RETURNS (AOUT INTEGER) AS BEGIN " +
            "FOR SELECT ID FROM SOME_TABLE WHERE A = :PIN OR B = :PIN INTO :AOUT DO SUSPEND; END";
        var (plan, _) = Plan(sql);
        Assert.Equal("SELECT ID FROM SOME_TABLE WHERE A = ? OR B = ?", plan.Sql);
        Assert.Equal(new[] { "PIN", "PIN" }, plan.ParameterNames); // one bind per occurrence (positional)
    }

    [Fact]
    public void ForExecuteStatement_NoStaticQuery_Throws()
    {
        const string sql =
            "CREATE PROCEDURE P RETURNS (AOUT INTEGER) AS BEGIN " +
            "FOR EXECUTE STATEMENT 'select 1 from rdb$database' INTO :AOUT DO SUSPEND; END";
        var loop = SqlParser.Parse(sql).Root.DescendantNodesAndSelf().OfType<ForSelectStatement>().Single();
        Assert.Null(loop.Query);
        Assert.Throws<InvalidOperationException>(() => CursorBridge.Build(sql, loop));
    }
}
