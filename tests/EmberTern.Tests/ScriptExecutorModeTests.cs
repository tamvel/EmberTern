using System;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Script Executor's execution-mode picker mapping + the per-mode description and outcome
/// summary (Step 5 seam A — pure VM helpers, no services / DB). The third mode, Sequenced, is the
/// deployment mode; its summary must state the non-atomic reality rather than a single verdict.
/// </summary>
public class ScriptExecutorModeTests
{
    [Theory]
    [InlineData(0, ScriptTransactionMode.Manual)]
    [InlineData(1, ScriptTransactionMode.AutoCommitOnSuccess)]
    [InlineData(2, ScriptTransactionMode.Sequenced)]
    [InlineData(3, ScriptTransactionMode.Manual)]   // out of range → the safe default
    [InlineData(-1, ScriptTransactionMode.Manual)]
    public void ResolveMode_MapsPickerIndex(int index, ScriptTransactionMode expected)
        => Assert.Equal(expected, ScriptExecutorTabViewModel.ResolveMode(index));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ResolveModeDescription_MatchesTheMode(int index)
    {
        var expected = index switch
        {
            1 => UiStrings.ScriptModeAutoCommitDescription,
            2 => UiStrings.ScriptModeSequencedDescription,
            _ => UiStrings.ScriptModeManualDescription,
        };
        Assert.Equal(expected, ScriptExecutorTabViewModel.ResolveModeDescription(index));
    }

    [Fact]
    public void SequencedDescription_StatesTheNonAtomicTradeoff()
        => Assert.Contains("not all-or-nothing", UiStrings.ScriptModeSequencedDescription, StringComparison.OrdinalIgnoreCase);

    private static ScriptRunOutcome Outcome(int ok, int failed, bool leftOpen, bool cancelled = false)
    {
        var results = new System.Collections.Generic.List<ScriptStatementResult>();
        for (int i = 0; i < ok; i++)
            results.Add(new ScriptStatementResult(i, "s", ScriptStatementKind.Ddl, true, null, null, TimeSpan.FromMilliseconds(3), null));
        for (int i = 0; i < failed; i++)
            results.Add(new ScriptStatementResult(ok + i, "s", ScriptStatementKind.Dml, false, null, null, TimeSpan.FromMilliseconds(3), "boom"));
        return new ScriptRunOutcome(results, leftOpen, AnyFailed: failed > 0, Cancelled: cancelled);
    }

    [Fact]
    public void BuildOutcomeStatus_Sequenced_AllSucceeded_IsADeploymentSummary()
    {
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(Outcome(3, 0, leftOpen: false), ScriptTransactionMode.Sequenced);
        Assert.Contains("Deployment", s, StringComparison.Ordinal);
        Assert.Contains("not all-or-nothing", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildOutcomeStatus_Sequenced_WithFailure_StillDeploymentSummary_NotSingleVerdict()
    {
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(Outcome(2, 1, leftOpen: false), ScriptTransactionMode.Sequenced);
        Assert.Contains("Deployment", s, StringComparison.Ordinal);
        Assert.DoesNotContain(UiStrings.ScriptStatusRolledBack, s, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutcomeStatus_Sequenced_Cancelled_UsesTheSequencedCancelledMessage()
    {
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(Outcome(1, 0, leftOpen: false, cancelled: true), ScriptTransactionMode.Sequenced);
        Assert.Equal(UiStrings.ScriptStatusSequencedCancelled, s);
    }

    [Fact]
    public void BuildOutcomeStatus_AutoCommit_Unchanged()
    {
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(Outcome(3, 0, leftOpen: false), ScriptTransactionMode.AutoCommitOnSuccess);
        Assert.Contains(UiStrings.ScriptStatusCommitted, s, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutcomeStatus_Manual_Unchanged_ReportsOpenTransaction()
    {
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(Outcome(3, 0, leftOpen: true), ScriptTransactionMode.Manual);
        Assert.Contains("Commit or Rollback", s, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutcomeStatus_NonSequencedCancelled_UsesGenericCancelled()
    {
        var s = ScriptExecutorTabViewModel.BuildOutcomeStatus(Outcome(1, 0, leftOpen: true, cancelled: true), ScriptTransactionMode.Manual);
        Assert.Equal(UiStrings.ScriptStatusCancelled, s);
    }
}
