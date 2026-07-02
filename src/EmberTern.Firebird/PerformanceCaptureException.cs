using System;

namespace EmberTern.Firebird;

/// <summary>Wraps a Firebird error raised while capturing performance data (plan / stats),
/// so the App never sees an <c>FbException</c> and can degrade gracefully.</summary>
public sealed class PerformanceCaptureException : Exception
{
    public PerformanceCaptureException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
