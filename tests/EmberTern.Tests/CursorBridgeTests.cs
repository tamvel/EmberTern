using System;
using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
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

    // ══ A trigger's NEW/OLD in the cursor query (P6, 2026-08-07) ══════════════════════════════
    //
    // ⭐ The shape D10 REFUSED to step. NEW.ID in a cursor's WHERE is a VALUE the frame already holds, so it
    // binds as a positional parameter exactly like :variable — the refusal's premise (the harness's synthetic
    // variables do not exist inside a separately-opened DSQL cursor) was true and its conclusion was not,
    // because the cursor never needed them.
    private const string TriggerCursor =
        "CREATE TRIGGER TR FOR T ACTIVE BEFORE UPDATE POSITION 0 AS " +
        "DECLARE VARIABLE Q INTEGER; BEGIN " +
        "FOR SELECT oi.QTY FROM ITEMS oi WHERE oi.OID = NEW.ID AND oi.PREV = OLD.ID INTO :Q DO " +
        "BEGIN Q = Q; END END";

    private static (SemanticModel Model, ForSelectStatement Loop, TriggerContext Context) TriggerPlan(string sql)
    {
        var root = SqlParser.Parse(sql).Root;
        var model = SemanticModel.Build(root, metadata: null);
        var loop = root.DescendantNodesAndSelf().OfType<ForSelectStatement>().Single();
        var columns = ContextSubstitution.BuildColumns(model, new TextSpan(0, sql.Length));
        var context = new TriggerContext("T", TriggerEvent.Update, TriggerTiming.Before, columns);
        return (model, loop, context);
    }

    [Fact]
    public void TriggerContextReference_BecomesABoundParameter()
    {
        var (model, loop, context) = TriggerPlan(TriggerCursor);
        var plan = CursorBridge.Build(TriggerCursor, loop, model, context);

        // Both NEW.ID and OLD.ID leave the SQL as positional parameters — nothing named survives, because a
        // separately-opened DSQL cursor has no frame variables to name.
        Assert.Equal("SELECT oi.QTY FROM ITEMS oi WHERE oi.OID = ? AND oi.PREV = ?", plan.Sql);

        // …and each is bound from the synthetic frame variable that ContextSubstitution assigned it. Compared
        // against the assignment rather than against "ET_CTX_0"/"ET_CTX_1": the naming convention belongs to
        // ContextSubstitution, and a literal here would be a second copy of it.
        var newId = context.Columns.Single(c => c.Record == TriggerRecord.New && c.Column == "ID").Synthetic;
        var oldId = context.Columns.Single(c => c.Record == TriggerRecord.Old && c.Column == "ID").Synthetic;
        Assert.Equal(new[] { newId, oldId }, plan.ParameterNames);
    }

    // ⚠ The opposite direction, and it is the one that keeps the change honest: WITHOUT a trigger context the
    // bridge must not touch a dotted name. A cursor over an ordinary table alias (`oi.OID`) looks exactly like
    // `NEW.ID` to a text scan, so a rewrite that were not reference-driven would silently turn a column into a
    // bind parameter — the §15.5 defect (SQL -804) one construct further along.
    [Fact]
    public void WithoutATriggerContext_DottedNamesAreLeftAlone()
    {
        var (_, loop, _) = TriggerPlan(TriggerCursor);
        var plan = CursorBridge.Build(TriggerCursor, loop);

        Assert.Equal(
            "SELECT oi.QTY FROM ITEMS oi WHERE oi.OID = NEW.ID AND oi.PREV = OLD.ID",
            plan.Sql);
        Assert.Empty(plan.ParameterNames);
    }

    // ⚠ Mixed: a :variable and a NEW.col in one query must come out in SOURCE order, because the bind values
    // are positional. An ordering slip would bind the right values to the wrong placeholders — a wrong result
    // set rather than an error, which is the failure mode worth a test of its own.
    [Fact]
    public void ColonParamsAndContextReferences_ShareOnePositionalOrder()
    {
        const string sql =
            "CREATE TRIGGER TR FOR T ACTIVE BEFORE UPDATE POSITION 0 AS " +
            "DECLARE VARIABLE Q INTEGER; DECLARE VARIABLE LIM INTEGER; BEGIN " +
            "FOR SELECT oi.QTY FROM ITEMS oi WHERE oi.OID = NEW.ID AND oi.QTY < :LIM AND oi.P = OLD.ID INTO :Q DO " +
            "BEGIN Q = Q; END END";

        var (model, loop, context) = TriggerPlan(sql);
        var plan = CursorBridge.Build(sql, loop, model, context);

        Assert.Equal(
            "SELECT oi.QTY FROM ITEMS oi WHERE oi.OID = ? AND oi.QTY < ? AND oi.P = ?",
            plan.Sql);

        var newId = context.Columns.Single(c => c.Record == TriggerRecord.New && c.Column == "ID").Synthetic;
        var oldId = context.Columns.Single(c => c.Record == TriggerRecord.Old && c.Column == "ID").Synthetic;
        Assert.Equal(new[] { newId, "LIM", oldId }, plan.ParameterNames);
    }
}
