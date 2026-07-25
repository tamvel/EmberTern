using System;
using System.Collections.Generic;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Step 5 seam C1 segment presentation (pure — no services / DB): the App reconstructs each
/// statement's committed-step number from the SAME <see cref="ScriptSegmentPlanner"/> the engine ran,
/// and only for Sequenced (a single-transaction run has no steps). Statements carry real SQL text
/// because the map is built over the AST-based classifier via the planner.
/// </summary>
public class ScriptExecutorSegmentPresentationTests
{
    private static ScriptStatement St(string text)
        => new(text, ScriptStatementKind.Unknown, SourceOffset: 0, SourceLength: text.Length);

    private static IReadOnlyList<ScriptStatement> Script(params string[] texts)
    {
        var list = new List<ScriptStatement>();
        foreach (var t in texts) list.Add(St(t));
        return list;
    }

    [Fact]
    public void SegmentMap_MixedMigration_SplitsAtEachSchemaBoundary()
    {
        // create → insert → insert → create index → insert  ⇒  steps 1, 2, 2, 3, 4
        var script = Script(
            "create table t (id integer, note varchar(10))",
            "insert into t values (1, 'a')",
            "insert into t values (2, 'b')",
            "create index ix_t on t (note)",
            "insert into t values (3, 'c')");

        Assert.Equal(new[] { 1, 2, 2, 3, 4 }, ScriptExecutorTabViewModel.BuildSegmentMap(script, ScriptTransactionMode.Sequenced));
    }

    [Fact]
    public void SegmentMap_AllData_IsOneStep()
        => Assert.Equal(new[] { 1, 1, 1 }, ScriptExecutorTabViewModel.BuildSegmentMap(
            Script("insert into t values (1)", "update t set x = 2", "select * from t"),
            ScriptTransactionMode.Sequenced));

    [Fact]
    public void SegmentMap_AllDdl_IsOneStepEach()
        => Assert.Equal(new[] { 1, 2 }, ScriptExecutorTabViewModel.BuildSegmentMap(
            Script("create table a (id integer)", "create table b (id integer)"),
            ScriptTransactionMode.Sequenced));

    [Theory]
    [InlineData(ScriptTransactionMode.Manual)]
    [InlineData(ScriptTransactionMode.AutoCommitOnSuccess)]
    public void SegmentMap_NonSequenced_IsEmpty(ScriptTransactionMode mode)
        => Assert.Empty(ScriptExecutorTabViewModel.BuildSegmentMap(
            Script("create table t (id integer)", "insert into t values (1)"), mode));

    [Fact]
    public void SegmentMap_EmptyScript_IsEmpty()
        => Assert.Empty(ScriptExecutorTabViewModel.BuildSegmentMap(Array.Empty<ScriptStatement>(), ScriptTransactionMode.Sequenced));

    private static ScriptResultRowViewModel Row(int step)
        => new(new ScriptStatementResult(0, "insert into t values (1)", ScriptStatementKind.Dml, true, 1, null, TimeSpan.Zero, null),
               sourceOffset: 0, sourceLength: 3, step: step);

    [Fact]
    public void Row_StepText_ShowsNumberForSequenced_BlankOtherwise()
    {
        Assert.Equal("2", Row(2).StepText);
        Assert.Equal(string.Empty, Row(0).StepText);   // single-transaction run — no step
    }
}
