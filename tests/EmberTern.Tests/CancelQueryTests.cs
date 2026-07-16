using System;
using System.IO;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Cancel used to feel like it needed several clicks. Two causes, both fixed:
/// (1) the executor only passed the CancellationToken to the driver — nothing issued
///     FbCommand.Cancel()/fb_cancel_operation, so a statement still executing server-side was
///     never interrupted (pinned in FirebirdQueryExecutor.RegisterServerCancel);
/// (2) the button gave no feedback, so the click looked lost and the user clicked again — those
///     extra clicks were no-ops on an already-cancelled CTS. These pin the UI latch.
/// </summary>
public class CancelQueryTests
{
    private static MainWindowViewModel NewVm(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "embertern-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new MainWindowViewModel(new ConnectionProfileStore(dir), new FirebirdConnectionService());
    }

    [Fact]
    public void Cancel_IsNotClickable_WhenNothingIsRunning()
    {
        var vm = NewVm(out _);
        Assert.False(vm.IsExecuting);
        Assert.False(vm.CanCancelQuery);
        Assert.False(vm.CancelQueryCommand.CanExecute(null));
    }

    [Fact]
    public void Cancel_IsClickable_WhileExecuting()
    {
        var vm = NewVm(out _) ;
        vm.IsExecuting = true;
        Assert.True(vm.CanCancelQuery);
        Assert.True(vm.CancelQueryCommand.CanExecute(null));
    }

    [Fact]
    public void Cancel_LatchesAndDisablesItself_SoRepeatClicksAreImpossible()
    {
        var vm = NewVm(out _);
        vm.IsExecuting = true;

        vm.IsCancelling = true;                 // what CancelQuery() sets on the first click

        Assert.False(vm.CanCancelQuery);        // the button is now disabled…
        Assert.False(vm.CancelQueryCommand.CanExecute(null)); // …so a second click can't happen
    }

    [Fact]
    public void CancelLatch_IsReleased_WhenTheRunUnwinds()
    {
        var vm = NewVm(out _);
        vm.IsExecuting = true;
        vm.IsCancelling = true;

        vm.IsExecuting = false;                 // any exit path: success, error, or cancel

        Assert.False(vm.IsCancelling);          // latch cleared, ready for the next run
        Assert.False(vm.CanCancelQuery);
    }

    [Fact]
    public void CancellingStatus_StringExists()
        => Assert.False(string.IsNullOrWhiteSpace(UiStrings.CancellingStatus));
}
