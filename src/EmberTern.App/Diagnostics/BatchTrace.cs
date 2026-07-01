using System;
using System.Globalization;
using EmberTern.Firebird;

namespace EmberTern.App.Diagnostics;

/// <summary>
/// Env-gated timing instrumentation for the bulk-operation pipeline, added during the
/// Batch Operations UX polish sprint to pin WHERE the pre-dialog delay is spent
/// (recompile fetches one source per object → thousands of sequential round-trips).
///
/// Writes one timestamped line per phase to the shared <c>%TEMP%\EmberTern-debug.log</c>,
/// but ONLY when the environment variable <c>EMBERTERN_BATCH_DIAG</c> is set — zero cost
/// otherwise (the <c>Enabled</c> guard short-circuits before any formatting/Stopwatch use).
///
/// Measurement procedure (run against the live FB schema):
///   1. set <c>EMBERTERN_BATCH_DIAG=1</c>, launch, connect.
///   2. invoke a bulk op (e.g. "Recompile all objects" on the connection node).
///   3. read the <c>BATCH [prepare-*]</c> lines — the gap between
///      <c>prepare-begin</c> and <c>exec-start</c> is the time the user waited with no
///      execution-view feedback BEFORE this sprint (now covered by the preparing view).
///      <c>list-enumerate</c> = the RDB$ COUNT/name query; <c>source-fetch</c> = the
///      per-object source loop (the dominant cost); <c>exec</c> = the actual DDL batch.
/// </summary>
internal static class BatchTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("EMBERTERN_BATCH_DIAG") is not null;

    public static bool IsEnabled => Enabled;

    public static void Log(string phase, string detail)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog(string.Format(
            CultureInfo.InvariantCulture, "BATCH [{0}] {1}", phase, detail));
    }

    /// <summary>Enumerated the object list for a kind (the RDB$ list query).</summary>
    public static void LogListEnumerate(string kind, int count, long elapsedMs)
        => Log("list-enumerate", string.Format(
            CultureInfo.InvariantCulture, "kind={0} objects={1} ms={2}", kind, count, elapsedMs));

    /// <summary>Finished the per-object source-fetch loop for a kind.</summary>
    public static void LogSourceFetch(string kind, int fetched, int failed, long elapsedMs)
        => Log("source-fetch", string.Format(
            CultureInfo.InvariantCulture, "kind={0} fetched={1} failed={2} ms={3}", kind, fetched, failed, elapsedMs));

    /// <summary>Preparation complete → about to switch to the live execution view.</summary>
    public static void LogPrepareDone(int steps, int preFailures, long elapsedMs)
        => Log("prepare-done", string.Format(
            CultureInfo.InvariantCulture, "steps={0} preFailures={1} totalPrepMs={2}", steps, preFailures, elapsedMs));

    /// <summary>The DDL batch finished (or was cancelled).</summary>
    public static void LogExecDone(int steps, long elapsedMs, bool cancelled)
        => Log("exec-done", string.Format(
            CultureInfo.InvariantCulture, "steps={0} ms={1} cancelled={2}", steps, elapsedMs, cancelled));
}
