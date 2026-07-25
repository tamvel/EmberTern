using System;
using System.Globalization;
using EmberTern.Firebird;

namespace EmberTern.App.Diagnostics;

/// <summary>
/// TEMPORARY live instrumentation for the Stage Q / Q3 investigation: the code-action light bulb does not
/// reach the screen in the real app, while Ctrl+. works at the same caret and every headless test passes.
/// Writes one timestamped line per decision in the ShowBulb / PositionBulb / HideBulb path to the shared
/// <c>%TEMP%\EmberTern-debug.log</c>.
/// <para>
/// <b>Default ON</b> for the duration of this investigation (the ScrollTrace precedent) — the point is that
/// the user reproduces once and sends the log, without having to remember an environment variable. Set
/// <c>EMBERTERN_BULB_DIAG=0</c> to silence it. <b>Delete this class, and its call sites, once the cause is
/// found</b> — instrumentation that outlives its investigation becomes noise nobody trusts.
/// </para>
/// <para>
/// Procedure: launch, connect, type a query with an ambiguous column (two joined tables sharing a column
/// name), click into that column, then read <c>%TEMP%\EmberTern-debug.log</c>. Every line is prefixed
/// <c>BULB</c>.
/// </para>
/// </summary>
internal static class BulbTrace
{
    private static readonly bool Enabled =
        !string.Equals(Environment.GetEnvironmentVariable("EMBERTERN_BULB_DIAG"), "0", StringComparison.Ordinal);

    public static bool IsEnabled => Enabled;

    public static void Log(string message)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog("BULB " + message);
    }

    public static void Log(FormattableString message)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog("BULB " + message.ToString(CultureInfo.InvariantCulture));
    }
}
