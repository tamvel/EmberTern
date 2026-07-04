using System;
using System.Collections.Generic;

namespace EmberTern.Core.Trace;

/// <summary>
/// Stateful folder that turns a stream of faithful <see cref="RawTraceRecord"/>s into
/// curated <see cref="TraceEvent"/>s, one record at a time. Folds each <c>*_START</c>
/// into its matching <c>*_FINISH</c> (per-<c>ContextToken</c>+kind LIFO stack) and stamps
/// monotonic id/sequence, delta, span, reads-summary and the self-activity flag.
/// <para>
/// Shared by the batch <see cref="TraceLogParser.Parse(string)"/> and the live
/// <see cref="TraceStreamAccumulator"/> so folding lives in ONE place and streaming
/// output is identical to batch — including a START and its FINISH arriving in separate
/// Services-API messages (the folder holds the open START across calls).
/// </para>
/// </summary>
public sealed class TraceEventFolder
{
    private readonly IReadOnlyCollection<long> _selfIds;
    private readonly Dictionary<string, Stack<RawTraceRecord>> _open = new(StringComparer.Ordinal);
    private long _id;
    private DateTimeOffset? _prevStart;

    public TraceEventFolder(IReadOnlyCollection<long>? selfAttachmentIds = null)
        => _selfIds = selfAttachmentIds ?? Array.Empty<long>();

    /// <summary>Feeds one raw record. Returns the folded event, or null for a <c>*_START</c>
    /// marker (which is buffered until its <c>*_FINISH</c> arrives).</summary>
    public TraceEvent? Push(RawTraceRecord r)
    {
        var kind = TraceLogParser.MapKind(r.RawEventType);

        if (r.RawEventType.EndsWith("_START", StringComparison.Ordinal))
        {
            var key = FoldKey(r.ContextToken, kind);
            (_open.TryGetValue(key, out var stack) ? stack : _open[key] = new Stack<RawTraceRecord>()).Push(r);
            return null; // START markers never become their own event
        }

        RawTraceRecord? startPair = null;
        if (r.RawEventType.EndsWith("_FINISH", StringComparison.Ordinal))
        {
            var key = FoldKey(r.ContextToken, kind);
            if (_open.TryGetValue(key, out var stack) && stack.Count > 0)
                startPair = stack.Pop();
        }

        var duration = r.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        DateTimeOffset startTime, endTime;
        if (startPair is not null)
        {
            startTime = startPair.Timestamp;
            endTime = r.Timestamp;
            duration ??= endTime - startTime;
        }
        else
        {
            // FINISH-only (statement/trigger) or a standalone event: the logged timestamp is the end.
            endTime = r.Timestamp;
            startTime = endTime - (duration ?? TimeSpan.Zero);
        }

        long? reads = r.TableReads.Count > 0 ? TraceLogParser.SumRecordReads(r.TableReads) : null;
        var severity = r.ErrorText is not null ? TraceEventSeverity.Error
            : TraceLogParser.IsSystemKind(kind) ? TraceEventSeverity.System
            : TraceEventSeverity.Normal;

        _id++;
        var ev = new TraceEvent
        {
            Id = _id,
            Sequence = _id,
            Kind = kind,
            Severity = severity,
            StartTime = startTime,
            EndTime = endTime,
            Duration = duration,
            DeltaMs = _prevStart is { } p ? (long)Math.Round((startTime - p).TotalMilliseconds) : null,
            Sql = r.Sql,
            ObjectName = r.ObjectName,
            TransactionId = r.TransactionId,
            AttachmentId = r.AttachmentId,
            ContextToken = r.ContextToken,
            IsSelfActivity = r.AttachmentId is { } a && _selfIds.Contains(a),
            RowsFetched = r.RecordsFetched,
            Reads = reads,
            ErrorText = r.ErrorText,
        };
        _prevStart = startTime;
        return ev;
    }

    /// <summary>Clears all state (open START stacks, id/delta counters) — a fresh session.</summary>
    public void Reset()
    {
        _open.Clear();
        _id = 0;
        _prevStart = null;
    }

    private static string FoldKey(string token, TraceEventKind kind) => token + "|" + kind;
}
