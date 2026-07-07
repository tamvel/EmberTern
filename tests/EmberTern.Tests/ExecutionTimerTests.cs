using System;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the shared live execution-timer: the pure <see cref="ExecutionTimer.Format"/>
/// (mm:ss.f, hour promotion, negative clamp) and the Start/Stop state contract that every
/// execution surface (SQL Editor / Execute Procedure/Function / Script Executor) relies on.</summary>
public class ExecutionTimerTests
{
    [Theory]
    [InlineData(0, "00:00.0")]
    [InlineData(3400, "00:03.4")]
    [InlineData(65900, "01:05.9")]
    [InlineData(600000, "10:00.0")]
    public void Format_SubHour_IsMmSsTenths(int totalMs, string expected)
        => Assert.Equal(expected, ExecutionTimer.Format(TimeSpan.FromMilliseconds(totalMs)));

    [Fact]
    public void Format_PastAnHour_PromotesToHours()
        => Assert.Equal("1:02:03.4", ExecutionTimer.Format(new TimeSpan(0, 1, 2, 3, 400)));

    [Fact]
    public void Format_Negative_ClampsToZero()
        => Assert.Equal("00:00.0", ExecutionTimer.Format(TimeSpan.FromMilliseconds(-500)));

    [Fact]
    public void Idle_HasNoRunningStateAndEmptyText()
    {
        var timer = new ExecutionTimer();
        Assert.False(timer.IsRunning);
        Assert.Equal(string.Empty, timer.ElapsedText);
    }

    [Fact]
    public void Stop_WhenIdle_IsSafeNoOp()
    {
        var timer = new ExecutionTimer();
        timer.Stop(); // must not throw when never started
        Assert.False(timer.IsRunning);
        Assert.Equal(string.Empty, timer.ElapsedText);
    }

    [Fact]
    public void Start_SetsRunningAndSeedsZeroText_ThenStopClears()
    {
        var timer = new ExecutionTimer();

        timer.Start();
        Assert.True(timer.IsRunning);
        Assert.Equal("00:00.0", timer.ElapsedText); // seeded before the first tick

        timer.Stop();
        Assert.False(timer.IsRunning);
        Assert.Equal(string.Empty, timer.ElapsedText);
    }

    [Fact]
    public void Start_Twice_RestartsWithoutLeavingRunningStuck()
    {
        var timer = new ExecutionTimer();
        timer.Start();
        timer.Start(); // idempotent restart
        Assert.True(timer.IsRunning);
        timer.Stop();
        Assert.False(timer.IsRunning);
    }
}
