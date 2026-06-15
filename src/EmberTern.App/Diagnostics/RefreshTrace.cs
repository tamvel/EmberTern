using System;
using EmberTern.Firebird;

namespace EmberTern.App.Diagnostics;

/// <summary>
/// Temporary instrumentation for the post-edit / post-transaction refresh
/// pipeline (Save → Commit → RefreshStructure → ReloadDataPreview → RefreshTree).
/// Writes one timestamped line per step to the shared
/// <c>%TEMP%\EmberTern-debug.log</c> — but ONLY when the environment variable
/// <c>EMBERTERN_REFRESH_DIAG</c> is set, so it costs nothing in normal use.
///
/// Added during the refresh-storm investigation so the execution path can be
/// verified against a Firebird trace. Safe to leave in place; flip the env var
/// to turn it on.
/// </summary>
internal static class RefreshTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("EMBERTERN_REFRESH_DIAG") is not null;

    public static bool IsEnabled => Enabled;

    public static void Log(string message)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog("REFRESH " + message);
    }

    public static void Log(string scope, string detail)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog($"REFRESH [{scope}] {detail}");
    }
}
