using System;
using System.Collections.Generic;

namespace EmberTern.Core.Trace;

/// <summary>
/// Turns the LIVE line-by-line Services-API output (each <c>FbService.ServiceOutput</c>
/// <c>Message</c>) into curated <see cref="TraceEvent"/>s as they arrive. Buffers lines
/// into header-anchored blocks; a block completes when the NEXT header arrives (or on
/// <see cref="Flush"/> at session end), at which point it is parsed and folded through a
/// shared <see cref="TraceEventFolder"/> — so a START and its FINISH in separate messages
/// pair correctly and the streamed result is identical to a batch parse of the same text.
/// Not thread-safe by itself: the trace service owns one accumulator and feeds it from the
/// single ServiceOutput callback, pushing results into the (thread-safe) ring buffer.
/// </summary>
public sealed class TraceStreamAccumulator
{
    private readonly TraceEventFolder _folder;
    private readonly List<string> _block = new();
    private bool _seenHeader;

    public TraceStreamAccumulator(IReadOnlyCollection<long>? selfAttachmentIds = null)
        => _folder = new TraceEventFolder(selfAttachmentIds);

    /// <summary>Feeds one raw message (which may itself contain several newline-separated lines).
    /// Returns any events completed by the newly-arrived header(s) — usually empty until a block
    /// closes.</summary>
    public IReadOnlyList<TraceEvent> Append(string message)
    {
        if (string.IsNullOrEmpty(message))
            return Array.Empty<TraceEvent>();

        List<TraceEvent>? emitted = null;
        foreach (var raw in message.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;

            if (TraceLogParser.IsHeaderLine(line))
            {
                AppendRange(ref emitted, FlushBlock()); // complete the previous block
                _block.Add(line);                       // start the new one
                _seenHeader = true;
            }
            else if (_seenHeader)
            {
                _block.Add(line); // body line (preamble before the first header is ignored)
            }
        }

        return (IReadOnlyList<TraceEvent>?)emitted ?? Array.Empty<TraceEvent>();
    }

    /// <summary>Completes the final buffered block. Call once when the trace session stops so the
    /// last event isn't left un-emitted (a block only closes when the next header arrives).</summary>
    public IReadOnlyList<TraceEvent> Flush() => FlushBlock();

    private IReadOnlyList<TraceEvent> FlushBlock()
    {
        if (_block.Count == 0)
            return Array.Empty<TraceEvent>();

        var text = string.Join("\n", _block);
        _block.Clear();

        List<TraceEvent>? outp = null;
        foreach (var record in TraceLogParser.ParseRecords(text)) // 0..1 record for a single block
            if (_folder.Push(record) is { } e)
                (outp ??= new List<TraceEvent>()).Add(e);
        return (IReadOnlyList<TraceEvent>?)outp ?? Array.Empty<TraceEvent>();
    }

    private static void AppendRange(ref List<TraceEvent>? target, IReadOnlyList<TraceEvent> items)
    {
        if (items.Count == 0) return;
        (target ??= new List<TraceEvent>()).AddRange(items);
    }
}
