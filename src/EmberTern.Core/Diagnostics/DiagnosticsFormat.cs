using System.Globalization;

namespace EmberTern.Core.Diagnostics;

/// <summary>Shared, presentation-neutral formatting for the diagnostics modules (pure).</summary>
public static class DiagnosticsFormat
{
    /// <summary>Compact age: <c>HH:MM:SS</c> past an hour, else <c>MM:SS</c>.</summary>
    public static string Age(double seconds)
    {
        var ts = System.TimeSpan.FromSeconds(seconds < 0 ? 0 : seconds);
        return ts.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
    }

    /// <summary>Nullable age → formatted string, or empty when unknown.</summary>
    public static string Age(double? seconds) => seconds is { } s ? Age(s) : string.Empty;
}
