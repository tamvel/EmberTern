using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the pure Sequenced-mode segment planner (no driver, no database). The planner classifies
/// each statement with the AST-based <see cref="EmberTern.Core.Sql.SqlStatementClassifier"/>, so
/// these use real SQL text; the <see cref="ScriptStatementKind"/> on each statement is
/// deliberately IRRELEVANT to the plan (proven by <see cref="Plan_UsesAstClassification_NotDriverKind"/>).
/// </summary>
public class ScriptSegmentPlannerTests
{
    private static ScriptStatement St(string text, ScriptStatementKind kind = ScriptStatementKind.Unknown)
        => new(text, kind, SourceOffset: 0, SourceLength: text.Length);

    private static IReadOnlyList<ScriptSegment> Plan(params ScriptStatement[] statements)
        => ScriptSegmentPlanner.Plan(statements);

    [Fact]
    public void Plan_EmptyScript_ReturnsNoSegments()
        => Assert.Empty(ScriptSegmentPlanner.Plan(new List<ScriptStatement>()));

    [Fact]
    public void Plan_AllData_IsOneNoWaitSegment()
    {
        var plan = Plan(
            St("insert into t values (1)"),
            St("update t set x = 2"),
            St("select * from t"));

        var seg = Assert.Single(plan);
        Assert.Equal(SegmentTransactionPolicy.DataNoWait, seg.Policy);
        Assert.Equal(3, seg.Statements.Count);
    }

    [Fact]
    public void Plan_SingleDdl_IsOneWaitSegment()
    {
        var seg = Assert.Single(Plan(St("create table t (id integer)")));
        Assert.Equal(SegmentTransactionPolicy.SchemaWait, seg.Policy);
        Assert.Single(seg.Statements);
    }

    [Fact]
    public void Plan_CreateThenInsert_SplitsAtTheSchemaBoundary()
    {
        // The #213 defect: the INSERT must land in a NEW segment (a fresh transaction) so it can
        // see the just-created table.
        var plan = Plan(
            St("create table t (id integer)"),
            St("insert into t values (1)"));

        Assert.Equal(2, plan.Count);
        Assert.Equal(SegmentTransactionPolicy.SchemaWait, plan[0].Policy);
        Assert.Equal(SegmentTransactionPolicy.DataNoWait, plan[1].Policy);
        Assert.Equal("insert into t values (1)", plan[1].Statements.Single().Text);
    }

    [Fact]
    public void Plan_DataSchemaData_ProducesThreeHomogeneousSegments()
    {
        var plan = Plan(
            St("insert into a values (1)"),
            St("insert into a values (2)"),
            St("create table b (id integer)"),
            St("insert into b values (3)"));

        Assert.Equal(3, plan.Count);

        Assert.Equal(SegmentTransactionPolicy.DataNoWait, plan[0].Policy);
        Assert.Equal(2, plan[0].Statements.Count);

        Assert.Equal(SegmentTransactionPolicy.SchemaWait, plan[1].Policy);
        Assert.Single(plan[1].Statements);

        Assert.Equal(SegmentTransactionPolicy.DataNoWait, plan[2].Policy);
        Assert.Single(plan[2].Statements);
    }

    [Fact]
    public void Plan_ConsecutiveDdl_IsNotGrouped_EachIsItsOwnSegment()
    {
        // Conservative v1: consecutive DDL is committed one-at-a-time (isql SET AUTODDL ON), never
        // grouped — grouping needs dependency analysis we do not have (see the planner's docs).
        var plan = Plan(
            St("create table a (id integer)"),
            St("create table b (id integer)"));

        Assert.Equal(2, plan.Count);
        Assert.All(plan, s => Assert.Equal(SegmentTransactionPolicy.SchemaWait, s.Policy));
        Assert.All(plan, s => Assert.Single(s.Statements));
    }

    [Fact]
    public void Plan_Dcl_IsASchemaSegment()
    {
        // GRANT / REVOKE change the catalog → schema segment (WAIT), same as DDL.
        var plan = Plan(
            St("grant select on t to someuser"),
            St("insert into t values (1)"));

        Assert.Equal(2, plan.Count);
        Assert.Equal(SegmentTransactionPolicy.SchemaWait, plan[0].Policy);
        Assert.Equal(SegmentTransactionPolicy.DataNoWait, plan[1].Policy);
    }

    [Fact]
    public void Plan_UsesAstClassification_NotDriverKind()
    {
        // The driver-derived Kind on each statement disagrees with its text on purpose. The planner
        // must follow the AST classification (the text), not the Kind: the CREATE is a schema
        // boundary even though it is tagged Dml, and the INSERT is data even though tagged Ddl.
        var plan = Plan(
            St("create table t (id integer)", ScriptStatementKind.Dml),
            St("insert into t values (1)", ScriptStatementKind.Ddl));

        Assert.Equal(2, plan.Count);
        Assert.Equal(SegmentTransactionPolicy.SchemaWait, plan[0].Policy);
        Assert.Equal(SegmentTransactionPolicy.DataNoWait, plan[1].Policy);
    }

    [Fact]
    public void Plan_CoversEveryStatementOnceInOrder()
    {
        var input = new[]
        {
            St("insert into a values (1)"),
            St("create table b (id integer)"),
            St("update a set x = 2"),
            St("create index ix_b on b (id)"),
            St("select * from a"),
        };

        var flattened = ScriptSegmentPlanner.Plan(input).SelectMany(s => s.Statements).ToList();

        Assert.Equal(input.Length, flattened.Count);
        Assert.Equal(input.Select(s => s.Text), flattened.Select(s => s.Text));
    }
}
