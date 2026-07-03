using System;
using EmberTern.Core.Trace;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Timeline-ready invariants of the Activity Monitor event model
/// (M1 foundation). The span (StartTime..SpanEnd) and hierarchy
/// (ParentEventId/Depth) fields are what let a future Timeline View reuse the
/// exact same <see cref="TraceEvent"/> the Grid and Call Tree use — these tests
/// exist so that promise can't silently regress.
/// </summary>
public class TraceModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 3, 19, 10, 34, TimeSpan.Zero);

    private static TraceEvent Base(TraceEventKind kind = TraceEventKind.Statement) => new()
    {
        Id = 1,
        Sequence = 1,
        Kind = kind,
        StartTime = T0,
    };

    [Fact]
    public void SpanEnd_UsesExplicitEndTime_WhenPresent()
    {
        var e = Base() with { EndTime = T0.AddMilliseconds(120) };
        Assert.Equal(T0.AddMilliseconds(120), e.SpanEnd);
        Assert.True(e.HasSpan);
    }

    [Fact]
    public void SpanEnd_DerivesFromDuration_WhenNoExplicitEndTime()
    {
        var e = Base() with { Duration = TimeSpan.FromMilliseconds(75) };
        Assert.Equal(T0.AddMilliseconds(75), e.SpanEnd);
        Assert.True(e.HasSpan);
    }

    [Fact]
    public void SpanEnd_IsStartInstant_WhenNeitherEndNorDurationKnown()
    {
        var e = Base();
        Assert.Equal(T0, e.SpanEnd);
        Assert.False(e.HasSpan); // zero-length: not worth a timeline bar
    }

    [Fact]
    public void SpanEnd_NeverPrecedesStart_EvenIfEndTimeIsEarlier()
    {
        // Defensive: a malformed pair must not produce a negative-width bar.
        var e = Base() with { EndTime = T0.AddMilliseconds(-50) };
        Assert.Equal(T0, e.SpanEnd);
        Assert.False(e.HasSpan);
    }

    [Fact]
    public void ZeroDuration_ProducesInstantSpan()
    {
        var e = Base() with { Duration = TimeSpan.Zero, RowsFetched = 3 };
        Assert.Equal(T0, e.SpanEnd);
        Assert.False(e.HasSpan);
    }

    [Fact]
    public void CallHierarchy_ParentAndDepth_AreCarriedOnTheSameModel()
    {
        var statement = Base() with { Id = 10, Sequence = 5 };
        var trigger = Base(TraceEventKind.Trigger) with
        {
            Id = 11,
            Sequence = 6,
            ParentEventId = statement.Id,
            Depth = 1,
            ObjectName = "TR_ORDERS_BI",
        };

        Assert.Null(statement.ParentEventId);
        Assert.Equal(0, statement.Depth);
        Assert.Equal(10, trigger.ParentEventId);
        Assert.Equal(1, trigger.Depth);
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var e = Base();
        Assert.Equal(TraceEventSeverity.Normal, e.Severity);
        Assert.Null(e.EndTime);
        Assert.Null(e.Duration);
        Assert.Null(e.ParentEventId);
        Assert.Null(e.Sql);
        Assert.Null(e.Fingerprint);
        Assert.Null(e.TransactionId);
        Assert.False(e.IsSelfActivity);
    }

    [Fact]
    public void DefaultPreset_IsTheOpinionatedV1Preset()
    {
        var p = TraceSessionConfig.DefaultPreset;
        Assert.True(p.IncludeStatements);
        Assert.True(p.IncludeProcedures);
        Assert.True(p.IncludeFunctions);
        Assert.True(p.IncludeTriggers);
        Assert.True(p.IncludeErrors);
        Assert.False(p.IncludeConnections);   // noise for reverse-engineering
        Assert.False(p.IncludeTransactions);  // grouping keys off per-event tx id
        Assert.Equal(0, p.TimeThresholdMs);   // capture the fast statements too
        Assert.True(p.ExcludeSelfActivity);   // don't drown the ERP in self-noise
    }
}
