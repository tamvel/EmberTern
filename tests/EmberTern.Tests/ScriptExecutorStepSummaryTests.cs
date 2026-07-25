using System;
using System.Collections.Generic;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Step 5 seam C3 "N of M steps committed" deployment summary (pure — no services / DB). It is
/// presentation only: <see cref="ScriptExecutorTabViewModel.BuildStepSummary"/> counts COMMITTED steps
/// of all planned steps by reusing the unchanged <c>BuildStepStatuses</c> reconstruction, and
/// <see cref="ScriptExecutorTabViewModel.BuildOutcomeStatus(ScriptRunOutcome, ScriptTransactionMode, int[])"/>
/// prepends it to the Sequenced deployment / cancelled summary. Single-transaction modes get no headline.
/// </summary>
public class ScriptExecutorStepSummaryTests
{
    private static ScriptStatementResult Res(int index, bool ok)
        => new(index, "s", ScriptStatementKind.Dml, ok, ok ? 1 : null, null, TimeSpan.Zero, ok ? null : "boom");

    private static ScriptRunOutcome Outcome(bool cancelled, params ScriptStatementResult[] results)
    {
        bool anyFailed = false;
        foreach (var r in results) if (!r.Success) anyFailed = true;
        return new ScriptRunOutcome(results, TransactionLeftOpen: false, AnyFailed: anyFailed, Cancelled: cancelled);
    }

    [Fact]
    public void StepSummary_AllCommitted()
        => Assert.Equal(
            string.Format(UiStrings.ScriptStatusSequencedStepsFormat, 2, 2),
            ScriptExecutorTabViewModel.BuildStepSummary(new[] { 1, 2 }, new[] { Res(0, true), Res(1, true) }));

    [Fact]
    public void StepSummary_PartialCommit_CountsOnlyCommittedOfAllPlanned()
    {
        // 4 steps; the last one rolled back (its statement failed) → 3 of 4 committed.
        var summary = ScriptExecutorTabViewModel.BuildStepSummary(
            new[] { 1, 2, 3, 4 }, new[] { Res(0, true), Res(1, true), Res(2, true), Res(3, false) });

        Assert.Equal(string.Format(UiStrings.ScriptStatusSequencedStepsFormat, 3, 4), summary);
    }

    [Fact]
    public void StepSummary_StopOnError_RolledBackAndNotRunAreNotCommitted()
    {
        // step1 committed, step2 failed (rolled back), step3 never reached (not run) → 1 of 3.
        var summary = ScriptExecutorTabViewModel.BuildStepSummary(
            new[] { 1, 2, 3 }, new[] { Res(0, true), Res(1, false) });

        Assert.Equal(string.Format(UiStrings.ScriptStatusSequencedStepsFormat, 1, 3), summary);
    }

    [Fact]
    public void StepSummary_NonSequenced_EmptyMap_IsEmpty()
        => Assert.Equal(string.Empty,
            ScriptExecutorTabViewModel.BuildStepSummary(Array.Empty<int>(), new[] { Res(0, true) }));

    [Fact]
    public void BuildOutcomeStatus_Sequenced_WithMap_PrependsTheStepHeadline()
    {
        var outcome = Outcome(cancelled: false, Res(0, true), Res(1, true), Res(2, true), Res(3, false));
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(
            outcome, ScriptTransactionMode.Sequenced, new[] { 1, 2, 3, 4 });

        Assert.Contains(string.Format(UiStrings.ScriptStatusSequencedStepsFormat, 3, 4), s, StringComparison.Ordinal);
        Assert.Contains("Deployment", s, StringComparison.Ordinal);   // still the deployment summary
    }

    [Fact]
    public void BuildOutcomeStatus_Sequenced_Cancelled_WithMap_PrependsHeadlineToCancelledMessage()
    {
        // Only step 1 ran (committed) before cancellation; steps 2 and 3 not run → 1 of 3.
        var outcome = Outcome(cancelled: true, Res(0, true));
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(
            outcome, ScriptTransactionMode.Sequenced, new[] { 1, 2, 3 });

        Assert.Contains(string.Format(UiStrings.ScriptStatusSequencedStepsFormat, 1, 3), s, StringComparison.Ordinal);
        Assert.Contains(UiStrings.ScriptStatusSequencedCancelled, s, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutcomeStatus_Sequenced_NoMap_HasNoHeadline_BackwardCompatible()
    {
        // The existing 2-arg contract: no segment map → no "of ... steps committed" headline.
        var outcome = Outcome(cancelled: false, Res(0, true), Res(1, true));
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(outcome, ScriptTransactionMode.Sequenced);

        Assert.DoesNotContain("steps committed", s, StringComparison.Ordinal);
        Assert.Contains("Deployment", s, StringComparison.Ordinal);
    }
}
