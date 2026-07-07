using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Shared live "elapsed time" indicator for a running execution. ONE mechanism, reused by every
/// execution surface (SQL Editor, Execute Procedure/Function, Script Executor): a
/// <see cref="Stopwatch"/> driven by a ~100 ms <see cref="DispatcherTimer"/> that publishes
/// <see cref="ElapsedText"/> live while <see cref="IsRunning"/> is true. Modeled on the batch-results
/// duration timer (BatchResultsViewModel). The FINAL time is shown by the existing execution-metrics
/// display (QueryStatsText / ExecutionSummary / exec-info bar), so the live indicator clears to empty
/// on <see cref="Stop"/> — no duplication of the final "N ms".
/// </summary>
public sealed partial class ExecutionTimer : ObservableObject
{
    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _timer;

    /// <summary>True while an execution is in progress — drives the indicator's visibility.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedDisplay))]
    private bool _isRunning;

    /// <summary>Live elapsed time (mm:ss.f), updated ~10×/s while running; empty when idle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedDisplay))]
    private string _elapsedText = string.Empty;

    /// <summary>The single cohesive label shown in the toolbar — "Elapsed: mm:ss.f" while running,
    /// empty when idle. One binding target so the label + time never drift apart visually.</summary>
    public string ElapsedDisplay => IsRunning
        ? string.Format(CultureInfo.InvariantCulture, UiStrings.ExecutionElapsedFormat, ElapsedText)
        : string.Empty;

    /// <summary>Starts (or restarts) the live timer at the exact moment execution begins.
    /// Idempotent — a second Start restarts from zero.</summary>
    public void Start()
    {
        _stopwatch.Restart();
        ElapsedText = Format(TimeSpan.Zero);
        IsRunning = true;
        _timer ??= CreateTimer();
        _timer.Start();
    }

    /// <summary>Stops the live timer immediately — success, error, or cancel — and clears the display.
    /// The final elapsed time is surfaced by the existing execution-metrics display.</summary>
    public void Stop()
    {
        _timer?.Stop();
        _stopwatch.Stop();
        IsRunning = false;
        ElapsedText = string.Empty;
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        t.Tick += (_, _) => ElapsedText = Format(_stopwatch.Elapsed);
        return t;
    }

    /// <summary>mm:ss.f (tenths of a second), promoting to h:mm:ss.f past an hour. Pure — unit-tested.</summary>
    internal static string Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture);
    }
}
