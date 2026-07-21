using System;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Step 5 seam C2b-2 surfacing of "not run" statements (pure — no services / DB): a Sequenced
/// stop-on-error / cancellation leaves later statements unexecuted, so they produce NO result row.
/// <see cref="ScriptExecutorTabViewModel.FindNotRunStatements"/> reconstructs their indices from the
/// plan (segment map) minus the indices the results cover, and a synthesized
/// <see cref="ScriptResultRowViewModel"/> renders each as a muted, neither-success-nor-failure row.
/// </summary>
public class ScriptExecutorNotRunTests
{
    private static ScriptStatementResult Res(int index, bool ok)
        => new(index, "s", ScriptStatementKind.Dml, ok, ok ? 1 : null, null, TimeSpan.Zero, ok ? null : "boom");

    [Fact]
    public void FindNotRun_StopOnError_ReturnsTheUnexecutedSuffix()
    {
        // 4 statements planned; the run failed at index 1 (stop-on-error), so 2 and 3 never ran.
        var notRun = ScriptExecutorTabViewModel.FindNotRunStatements(
            new[] { 1, 2, 3, 4 }, new[] { Res(0, true), Res(1, false) });

        Assert.Equal(new[] { 2, 3 }, notRun);
    }

    [Fact]
    public void FindNotRun_Cancelled_ReturnsEverythingAfterThePoint()
    {
        // Only the first statement ran before cancellation; the other two are not run.
        var notRun = ScriptExecutorTabViewModel.FindNotRunStatements(
            new[] { 1, 2, 3 }, new[] { Res(0, true) });

        Assert.Equal(new[] { 1, 2 }, notRun);
    }

    [Fact]
    public void FindNotRun_AllRan_ReturnsNone()
        => Assert.Empty(ScriptExecutorTabViewModel.FindNotRunStatements(
            new[] { 1, 2 }, new[] { Res(0, true), Res(1, true) }));

    [Fact]
    public void FindNotRun_NonSequenced_EmptyMap_ReturnsNone()
        => Assert.Empty(ScriptExecutorTabViewModel.FindNotRunStatements(
            Array.Empty<int>(), new[] { Res(0, true) }));

    [Fact]
    public void FindNotRun_NoResultsAtAll_EveryStatementNotRun()
        => Assert.Equal(new[] { 0, 1 }, ScriptExecutorTabViewModel.FindNotRunStatements(
            new[] { 1, 2 }, Array.Empty<ScriptStatementResult>()));

    [Fact]
    public void SynthesizedRow_IsMutedNeitherSuccessNorFailure()
    {
        var statement = new ScriptStatement("insert into t values (1)", ScriptStatementKind.Dml,
            SourceOffset: 42, SourceLength: 24);

        var row = new ScriptResultRowViewModel(statement, index: 3, step: 4);

        Assert.True(row.IsNotRun);
        Assert.False(row.IsFailed);
        Assert.False(row.IsSucceeded);        // never coloured green
        Assert.Equal(UiStrings.ScriptResultNotRun, row.Result);
        Assert.Equal(ScriptStepStatus.NotRun, row.StepStatus);
        Assert.Equal(4, row.Line);            // index + 1
        Assert.Equal("4", row.StepText);      // its would-be step number
        Assert.Equal(42, row.SourceOffset);   // navigable back to the source
        Assert.True(row.HasSourceRange);
        Assert.Equal(UiStrings.ScriptResultNotRunTooltip, row.ResultTooltip);
        Assert.Equal(string.Empty, row.RowsText);
        Assert.Equal(string.Empty, row.Duration);
    }

    [Fact]
    public void ExecutedRow_IsSucceeded_ExcludesNotRun()
    {
        var ok = new ScriptResultRowViewModel(Res(0, true), sourceOffset: 0, sourceLength: 1);
        Assert.True(ok.IsSucceeded);
        Assert.False(ok.IsNotRun);
        Assert.Null(ok.ResultTooltip);

        var failed = new ScriptResultRowViewModel(Res(1, false), sourceOffset: 0, sourceLength: 1);
        Assert.False(failed.IsSucceeded);
        Assert.False(failed.IsNotRun);
    }
}
