using System;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Step 5 seam C2b-1 presentation of per-step commit/rollback status (pure — no services /
/// DB): each executed row is stamped with its step's outcome via
/// <see cref="ScriptExecutorTabViewModel.ApplyStepStatuses"/>, and the row exposes the observable
/// status + its derived colour flags / tooltip. The reconstruction itself (BuildStepStatuses) is
/// pinned separately and unchanged here.
/// </summary>
public class ScriptExecutorStepStatusPresentationTests
{
    private static ScriptStatementResult Res(int index, bool ok)
        => new(index, "s", ScriptStatementKind.Dml, ok, ok ? 1 : null, null, TimeSpan.Zero, ok ? null : "boom");

    private static ScriptResultRowViewModel Row(int index, int step, bool ok)
        => new(Res(index, ok), sourceOffset: 0, sourceLength: 3, step: step);

    [Fact]
    public void ApplyStepStatuses_PartialCommit_StampsEachRowWithItsStepOutcome()
    {
        var rows = new[] { Row(0, 1, true), Row(1, 2, true), Row(2, 3, true), Row(3, 4, false) };
        var results = new[] { Res(0, true), Res(1, true), Res(2, true), Res(3, false) };

        ScriptExecutorTabViewModel.ApplyStepStatuses(rows, new[] { 1, 2, 3, 4 }, results);

        Assert.Equal(ScriptStepStatus.Committed, rows[0].StepStatus);
        Assert.Equal(ScriptStepStatus.Committed, rows[1].StepStatus);
        Assert.Equal(ScriptStepStatus.Committed, rows[2].StepStatus);
        Assert.Equal(ScriptStepStatus.RolledBack, rows[3].StepStatus);
    }

    [Fact]
    public void ApplyStepStatuses_SuccessRowInAFailedDataStep_IsMarkedRolledBack()
    {
        // Step 1 = three data statements (ok, fail, ok); the whole step rolls back, so even the two
        // Success rows are marked RolledBack — the nuance the colouring exists to reveal.
        var rows = new[] { Row(0, 1, true), Row(1, 1, false), Row(2, 1, true) };
        var results = new[] { Res(0, true), Res(1, false), Res(2, true) };

        ScriptExecutorTabViewModel.ApplyStepStatuses(rows, new[] { 1, 1, 1 }, results);

        Assert.All(rows, r => Assert.Equal(ScriptStepStatus.RolledBack, r.StepStatus));
    }

    [Fact]
    public void ApplyStepStatuses_NonSequenced_EmptyMap_LeavesRowsUnstamped()
    {
        var rows = new[] { Row(0, 0, true) };
        ScriptExecutorTabViewModel.ApplyStepStatuses(rows, Array.Empty<int>(), new[] { Res(0, true) });

        Assert.Equal(ScriptStepStatus.NotRun, rows[0].StepStatus); // default; no colouring
        Assert.False(rows[0].IsStepCommitted);
        Assert.False(rows[0].IsStepRolledBack);
    }

    [Fact]
    public void Row_DerivedFlagsAndTooltip_FollowStepStatus_AndNotify()
    {
        var row = Row(0, 1, true);
        Assert.Equal(ScriptStepStatus.NotRun, row.StepStatus);   // default
        Assert.False(row.IsStepCommitted);
        Assert.False(row.IsStepRolledBack);

        bool notifiedCommitted = false;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScriptResultRowViewModel.IsStepCommitted)) notifiedCommitted = true;
        };

        row.StepStatus = ScriptStepStatus.Committed;
        Assert.True(row.IsStepCommitted);
        Assert.False(row.IsStepRolledBack);
        Assert.Equal(UiStrings.ScriptStepCommittedTooltip, row.StepStatusTooltip);
        Assert.True(notifiedCommitted);

        row.StepStatus = ScriptStepStatus.RolledBack;
        Assert.False(row.IsStepCommitted);
        Assert.True(row.IsStepRolledBack);
        Assert.Equal(UiStrings.ScriptStepRolledBackTooltip, row.StepStatusTooltip);
    }
}
