using System;
using System.Collections.Generic;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Step 5 seam C2a per-step commit/rollback reconstruction (pure — no services / DB). The App
/// rebuilds each Sequenced step's outcome from the segment map + the per-statement results, mirroring
/// FirebirdScriptExecutor.RunSequencedAsync: a step commits only if ALL its planned statements ran and
/// none failed; otherwise it rolled back; a step with no results was never reached. The load-bearing
/// case is a Success statement whose step still rolled back (a later statement in the same step failed).
/// </summary>
public class ScriptExecutorStepStatusTests
{
    private static ScriptStatementResult Res(int index, bool ok)
        => new(index, "s", ScriptStatementKind.Dml, ok, RecordsAffected: ok ? 1 : null, RowCount: null,
               TimeSpan.Zero, ok ? null : "boom");

    [Fact]
    public void AllStepsSucceed_AllCommitted()
    {
        // create(step1) → insert(step2); both ran and succeeded.
        var statuses = ScriptExecutorTabViewModel.BuildStepStatuses(
            new[] { 1, 2 }, new[] { Res(0, true), Res(1, true) });

        Assert.Equal(ScriptStepStatus.Committed, statuses[1]);
        Assert.Equal(ScriptStepStatus.Committed, statuses[2]);
    }

    [Fact]
    public void PartialCommit_EarlierStepsCommitted_FailingStepRolledBack()
    {
        // Probe case C: create(1) · insert ok(2) · create index(3) · insert dup FAIL(4).
        var statuses = ScriptExecutorTabViewModel.BuildStepStatuses(
            new[] { 1, 2, 3, 4 },
            new[] { Res(0, true), Res(1, true), Res(2, true), Res(3, false) });

        Assert.Equal(ScriptStepStatus.Committed, statuses[1]);
        Assert.Equal(ScriptStepStatus.Committed, statuses[2]);
        Assert.Equal(ScriptStepStatus.Committed, statuses[3]);
        Assert.Equal(ScriptStepStatus.RolledBack, statuses[4]);
    }

    [Fact]
    public void SuccessStatementInAFailedDataStep_IsRolledBack()
    {
        // One data step of three statements (continue-on-error): ok, FAIL, ok — the whole step rolls
        // back, so the two Success statements did NOT persist. THE key nuance C2 exists to show.
        var statuses = ScriptExecutorTabViewModel.BuildStepStatuses(
            new[] { 1, 1, 1 }, new[] { Res(0, true), Res(1, false), Res(2, true) });

        Assert.Equal(ScriptStepStatus.RolledBack, statuses[1]);
    }

    [Fact]
    public void StopOnError_MidStepFailure_StepRolledBack_LaterStepNotRun()
    {
        // create(1) FAILS → its step rolls back; the following insert(2) is never reached.
        var statuses = ScriptExecutorTabViewModel.BuildStepStatuses(
            new[] { 1, 2 }, new[] { Res(0, false) });

        Assert.Equal(ScriptStepStatus.RolledBack, statuses[1]);
        Assert.Equal(ScriptStepStatus.NotRun, statuses[2]);
    }

    [Fact]
    public void CancelledMidStep_PartialRunNoFailure_RolledBack()
    {
        // A two-statement data step where only the first ran (cancelled before the second) and did not
        // fail — the step never committed, so it rolled back.
        var statuses = ScriptExecutorTabViewModel.BuildStepStatuses(
            new[] { 1, 1 }, new[] { Res(0, true) });

        Assert.Equal(ScriptStepStatus.RolledBack, statuses[1]);
    }

    [Fact]
    public void NonSequenced_EmptyMap_YieldsNoStatuses()
        => Assert.Empty(ScriptExecutorTabViewModel.BuildStepStatuses(Array.Empty<int>(), new[] { Res(0, true) }));

    [Fact]
    public void NoResultsAtAll_EveryStepNotRun()
    {
        var statuses = ScriptExecutorTabViewModel.BuildStepStatuses(
            new[] { 1, 2 }, Array.Empty<ScriptStatementResult>());

        Assert.Equal(ScriptStepStatus.NotRun, statuses[1]);
        Assert.Equal(ScriptStepStatus.NotRun, statuses[2]);
    }
}
