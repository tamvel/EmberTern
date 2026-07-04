using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EmberTern.Core.Trace;

/// <summary>
/// Parses the raw Firebird Services-API trace text stream into structured events.
/// The stream is the sequence of <c>Message</c> lines from
/// <c>FbService.ServiceOutput</c> (M2 wiring) — or, for tests, a captured
/// <c>trace.txt</c>. Two entry points:
/// <list type="bullet">
/// <item><see cref="ParseRecords(string)"/> — faithful, loss-free
/// <see cref="RawTraceRecord"/> per raw block (START and FINISH kept separate).</item>
/// <item><see cref="Parse(string)"/> / <see cref="Parse(string, IReadOnlyCollection{long})"/>
/// — the curated pipeline: folds <c>*_START</c>+<c>*_FINISH</c> pairs into one
/// <see cref="TraceEvent"/>, maps to the grid model, and stamps sequence/delta/self-flag.</item>
/// </list>
/// Design notes: the parser is a pure, stateless-per-call function built against the
/// REAL capture — it tolerates the config preamble, mid-stream trace-infra junk, missing
/// optional fields (params, records, per-table, perf), multi-line SQL, and CRLF/LF. It
/// deliberately does NOT compute the call hierarchy or fingerprints — it only preserves
/// the inputs those later passes need (<see cref="TraceEvent.ContextToken"/>,
/// <see cref="TraceEvent.TransactionId"/>, ordering). "Slow" severity is a UI/session
/// concern applied later; the parser sets only Error/System/Normal.
/// </summary>
public static class TraceLogParser
{
    // Header: "2026-07-03T19:10:34.8830 (7320:00000000053417C0) EXECUTE_STATEMENT_FINISH"
    // The event token is normally a single UPPER_SNAKE word, but error events carry a suffix —
    // "ERROR AT jrd8_execute" — so allow an optional " AT <fn>" tail.
    private static readonly Regex HeaderRx = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+)\s+\((?<pid>\d+):(?<tok>[0-9A-Fa-f]+)\)\s+(?<evt>[A-Z_]+(?: AT .+?)?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex AttachRx = new(
        @"\(ATT_(?<att>\d+),\s*(?<user>[^,]*),\s*(?<cs>[^,]*),\s*(?<addr>[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex ProcessRx = new(@"^(?<proc>.*\S):(?<pid>\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex TxRx = new(@"\(TRA_(?<tra>\d+),\s*(?<params>[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex StatementRx = new(@"^Statement (?<sid>\d+):\s*$", RegexOptions.Compiled);
    private static readonly Regex ProcedureRx = new(@"^Procedure (?<name>.+):\s*$", RegexOptions.Compiled);
    private static readonly Regex FunctionRx = new(@"^Function (?<name>.+):\s*$", RegexOptions.Compiled);
    private static readonly Regex TriggerRx = new(@"^Trigger (?<name>.+?)(?: \((?<evt>[^)]*)\))?:\s*$", RegexOptions.Compiled);
    private static readonly Regex ParamRx = new(@"^param(?<i>\d+) = (?<type>.+?), ""(?<val>.*)""\s*$", RegexOptions.Compiled);
    private static readonly Regex RecordsRx = new(@"^(?<n>\d+) records fetched\s*$", RegexOptions.Compiled);
    private static readonly Regex PerfRx = new(@"^\s+(?<ms>\d+) ms(?:,\s*(?<rest>.*\S))?\s*$", RegexOptions.Compiled);
    private static readonly Regex PerfTokenRx = new(@"(?<n>\d+)\s+(?<kind>read|write|fetch|mark)", RegexOptions.Compiled);

    // Firebird status-vector line, e.g. "335544665 : violation of PRIMARY or UNIQUE KEY constraint …"
    // or "-803 : attempt to store duplicate value …". The distinctive "<code> : <message>" shape
    // never collides with SQL/param/perf/table lines.
    private static readonly Regex StatusRx = new(@"^\s*-?\d+\s*:\s+\S.*$", RegexOptions.Compiled);

    private static readonly string[] TableColumns =
        { "Natural", "Index", "Update", "Insert", "Delete", "Backout", "Purge", "Expunge" };

    // ---------------------------------------------------------------- faithful parse

    public static IReadOnlyList<RawTraceRecord> ParseRecords(string text)
    {
        var records = new List<RawTraceRecord>();
        if (string.IsNullOrEmpty(text))
            return records;

        var lines = SplitLines(text);

        // Header-anchored block splitting: everything from one header line up to the
        // next header line is one block. Content before the first header (the config
        // preamble, "Trace session started" banners) is discarded.
        int i = 0;
        while (i < lines.Length && HeaderRx.Match(lines[i]) is { Success: false })
            i++;

        while (i < lines.Length)
        {
            int start = i;
            i++;
            while (i < lines.Length && !HeaderRx.IsMatch(lines[i]))
                i++;
            var record = ParseBlock(lines, start, i);
            if (record is not null)
                records.Add(record);
        }

        return records;
    }

    private static RawTraceRecord? ParseBlock(string[] lines, int start, int end)
    {
        var header = HeaderRx.Match(lines[start]);
        if (!header.Success)
            return null;

        var ts = ParseTimestamp(header.Groups["ts"].Value);

        long? att = null, tra = null, statementId = null, records = null, durationMs = null;
        long? pageReads = null, writes = null, fetches = null, marks = null;
        string? user = null, role = null, charset = null, addr = null, txParams = null;
        string? processName = null, sql = null, objectName = null, triggerEvent = null, errorText = null;
        int? clientPid = null;
        var inputParams = new List<RawTraceParam>();
        var returnParams = new List<RawTraceParam>();
        var tableReads = new List<RawTableRead>();

        var sqlBuffer = new StringBuilder();
        var errorBuffer = new StringBuilder();
        bool inSql = false, inReturns = false;
        string? tableHeaderLine = null; // non-null => currently reading the per-table block

        for (int k = start + 1; k < end; k++)
        {
            var line = lines[k];

            // per-table block: header, then stars, then rows until a blank/other line
            if (tableHeaderLine is not null)
            {
                if (line.Length == 0) { tableHeaderLine = null; continue; }
                if (line.TrimStart().StartsWith("*", StringComparison.Ordinal)) continue; // stars separator
                var row = ParseTableRow(tableHeaderLine, line);
                if (row is not null) { tableReads.Add(row); continue; }
                tableHeaderLine = null; // not a row → fall through to normal handling
            }

            if (IsTableHeader(line)) { tableHeaderLine = line; inSql = false; continue; }

            // The "-----" rule under "Statement N:" is a separator, never SQL — skip it before
            // the SQL-accumulation block would otherwise swallow it.
            if (IsDashes(line)) continue;

            // SQL accumulation ends at the first recognised terminator line (incl. a status-vector
            // error line, so an error block's SQL isn't polluted by the message).
            if (inSql)
            {
                if (ParamRx.IsMatch(line) || RecordsRx.IsMatch(line) || PerfRx.IsMatch(line)
                    || IsTableHeader(line) || StatusRx.IsMatch(line))
                    inSql = false;
                else { sqlBuffer.Append(line).Append('\n'); continue; }
            }

            // Status-vector / error line → accumulate as the block's error text.
            if (StatusRx.IsMatch(line))
            {
                if (errorBuffer.Length > 0) errorBuffer.Append('\n');
                errorBuffer.Append(line.Trim());
                continue;
            }

            if (line.Contains("(ATT_", StringComparison.Ordinal))
            {
                var m = AttachRx.Match(line);
                if (m.Success)
                {
                    att = ParseLong(m.Groups["att"].Value);
                    (user, role) = SplitUserRole(m.Groups["user"].Value);
                    charset = m.Groups["cs"].Value.Trim();
                    addr = m.Groups["addr"].Value.Trim();
                }
                continue;
            }

            if (line.Contains("(TRA_", StringComparison.Ordinal))
            {
                var m = TxRx.Match(line);
                if (m.Success) { tra = ParseLong(m.Groups["tra"].Value); txParams = m.Groups["params"].Value.Trim(); }
                continue;
            }

            var stmt = StatementRx.Match(line);
            if (stmt.Success) { statementId = ParseLong(stmt.Groups["sid"].Value); inSql = true; continue; }

            var proc = ProcedureRx.Match(line);
            if (proc.Success) { objectName = proc.Groups["name"].Value.Trim(); continue; }

            var func = FunctionRx.Match(line);
            if (func.Success) { objectName = func.Groups["name"].Value.Trim(); continue; }

            var trig = TriggerRx.Match(line);
            if (trig.Success && line.StartsWith("Trigger ", StringComparison.Ordinal))
            {
                objectName = trig.Groups["name"].Value.Trim();
                if (trig.Groups["evt"].Success) triggerEvent = trig.Groups["evt"].Value.Trim();
                continue;
            }

            if (line == "returns:") { inReturns = true; continue; }

            var pm = ParamRx.Match(line);
            if (pm.Success)
            {
                var val = pm.Groups["val"].Value;
                var param = new RawTraceParam(
                    int.Parse(pm.Groups["i"].Value, CultureInfo.InvariantCulture),
                    pm.Groups["type"].Value.Trim(),
                    val == "<NULL>" ? null : val);
                (inReturns ? returnParams : inputParams).Add(param);
                continue;
            }

            var rec = RecordsRx.Match(line);
            if (rec.Success) { records = ParseLong(rec.Groups["n"].Value); continue; }

            var perf = PerfRx.Match(line);
            if (perf.Success)
            {
                durationMs = ParseLong(perf.Groups["ms"].Value);
                if (perf.Groups["rest"].Success)
                {
                    foreach (Match t in PerfTokenRx.Matches(perf.Groups["rest"].Value))
                    {
                        var n = ParseLong(t.Groups["n"].Value);
                        switch (t.Groups["kind"].Value)
                        {
                            case "read": pageReads = n; break;
                            case "write": writes = n; break;
                            case "fetch": fetches = n; break;
                            case "mark": marks = n; break;
                        }
                    }
                }
                continue;
            }

            // Process line (path:pid) — only when it isn't one of the above. Guard against
            // ATT/TRA lines (handled) and require a real path with a trailing :digits.
            if (!inSql && !line.Contains("(ATT_", StringComparison.Ordinal) && !line.Contains("(TRA_", StringComparison.Ordinal))
            {
                var pr = ProcessRx.Match(line);
                if (pr.Success && processName is null && line.Length > 0 && (line[0] == '\t' || line[0] == ' '))
                {
                    processName = pr.Groups["proc"].Value.Trim();
                    clientPid = int.Parse(pr.Groups["pid"].Value, CultureInfo.InvariantCulture);
                    continue;
                }
            }
            // anything else (blank lines, trace-infra junk) is ignored — tolerant by design
        }

        if (sqlBuffer.Length > 0)
            sql = sqlBuffer.ToString().Trim('\n', '\r', ' ', '\t');

        var rawEvt = header.Groups["evt"].Value;
        if (errorBuffer.Length > 0)
            errorText = errorBuffer.ToString();
        else if (rawEvt.Contains("ERROR", StringComparison.Ordinal))
            errorText = rawEvt; // ERROR event without a parsed status line → still flag it

        return new RawTraceRecord
        {
            RawEventType = rawEvt,
            Timestamp = ts,
            ServerProcessId = int.Parse(header.Groups["pid"].Value, CultureInfo.InvariantCulture),
            ContextToken = header.Groups["tok"].Value,
            AttachmentId = att,
            UserName = user,
            RoleName = role,
            Charset = charset,
            RemoteAddress = addr,
            ProcessName = processName,
            ClientProcessId = clientPid,
            TransactionId = tra,
            TransactionParams = txParams,
            StatementId = statementId,
            Sql = string.IsNullOrEmpty(sql) ? null : sql,
            ObjectName = objectName,
            TriggerEvent = triggerEvent,
            Parameters = inputParams,
            ReturnValues = returnParams,
            RecordsFetched = records,
            DurationMs = durationMs,
            PageReads = pageReads,
            Writes = writes,
            Fetches = fetches,
            Marks = marks,
            TableReads = tableReads,
            ErrorText = errorText,
        };
    }

    // ---------------------------------------------------------------- fold + map

    public static IReadOnlyList<TraceEvent> Parse(string text) => Parse(text, Array.Empty<long>());

    /// <param name="selfAttachmentIds">EmberTern's own attachment ids (data/metadata lanes); matching
    /// events are flagged <see cref="TraceEvent.IsSelfActivity"/> so the UI can hide self-noise.</param>
    public static IReadOnlyList<TraceEvent> Parse(string text, IReadOnlyCollection<long> selfAttachmentIds)
    {
        var folder = new TraceEventFolder(selfAttachmentIds);
        var events = new List<TraceEvent>();
        foreach (var r in ParseRecords(text))
            if (folder.Push(r) is { } e)
                events.Add(e);
        return events;
    }

    /// <summary>True when <paramref name="line"/> is a raw trace block header
    /// (<c>TS (pid:token) EVENT</c>). Used by the streaming accumulator to find block boundaries.</summary>
    public static bool IsHeaderLine(string line) => HeaderRx.IsMatch(line);

    // ---------------------------------------------------------------- helpers

    internal static TraceEventKind MapKind(string rawEventType)
    {
        if (rawEventType.StartsWith("EXECUTE_STATEMENT", StringComparison.Ordinal)) return TraceEventKind.Statement;
        if (rawEventType.StartsWith("EXECUTE_PROCEDURE", StringComparison.Ordinal)) return TraceEventKind.Procedure;
        if (rawEventType.StartsWith("EXECUTE_FUNCTION", StringComparison.Ordinal)) return TraceEventKind.Function;
        if (rawEventType.StartsWith("EXECUTE_TRIGGER", StringComparison.Ordinal)) return TraceEventKind.Trigger;
        if (rawEventType is "ATTACH_DATABASE" or "DETACH_DATABASE") return TraceEventKind.Connection;
        if (rawEventType.Contains("TRANSACTION", StringComparison.Ordinal)
            || rawEventType is "COMMIT_RETAINING" or "ROLLBACK_RETAINING") return TraceEventKind.Transaction;
        return TraceEventKind.System;
    }

    internal static bool IsSystemKind(TraceEventKind kind)
        => kind is TraceEventKind.System or TraceEventKind.Connection or TraceEventKind.Transaction;

    internal static long SumRecordReads(IReadOnlyList<RawTableRead> reads)
    {
        long total = 0;
        foreach (var t in reads) total += t.RecordReads;
        return total;
    }

    private static bool IsTableHeader(string line)
        => line.StartsWith("Table", StringComparison.Ordinal)
           && line.Contains("Natural", StringComparison.Ordinal)
           && line.Contains("Index", StringComparison.Ordinal);

    private static bool IsDashes(string line)
    {
        if (line.Length < 5) return false;
        foreach (var c in line) if (c != '-') return false;
        return true;
    }

    private static RawTableRead? ParseTableRow(string headerLine, string row)
    {
        int natLeft = headerLine.IndexOf("Natural", StringComparison.Ordinal);
        if (natLeft < 0) return null;

        // Right-edge (exclusive) of each column = end index of its header word. Numbers are
        // right-aligned to these edges; a column's field spans [previous right-edge .. this right-edge].
        var rightEdge = new int[TableColumns.Length];
        for (int c = 0; c < TableColumns.Length; c++)
        {
            int at = headerLine.IndexOf(TableColumns[c], StringComparison.Ordinal);
            if (at < 0) return null;
            rightEdge[c] = at + TableColumns[c].Length;
        }

        var name = Slice(row, 0, natLeft).Trim();
        if (name.Length == 0) return null;

        long Col(int c)
        {
            int left = c == 0 ? natLeft : rightEdge[c - 1];
            var text = Slice(row, left, rightEdge[c]).Trim();
            return text.Length == 0 ? 0 : (ParseLong(text) ?? 0);
        }

        return new RawTableRead
        {
            TableName = name,
            Natural = Col(0),
            Indexed = Col(1),
            Update = Col(2),
            Insert = Col(3),
            Delete = Col(4),
            Backout = Col(5),
            Purge = Col(6),
            Expunge = Col(7),
        };
    }

    private static string Slice(string s, int start, int end)
    {
        if (start >= s.Length) return string.Empty;
        end = Math.Min(end, s.Length);
        if (end <= start) return string.Empty;
        return s.Substring(start, end - start);
    }

    private static (string? user, string? role) SplitUserRole(string userRole)
    {
        var idx = userRole.IndexOf(':');
        if (idx < 0) return (userRole.Trim(), null);
        return (userRole[..idx].Trim(), userRole[(idx + 1)..].Trim());
    }

    private static DateTimeOffset ParseTimestamp(string s)
    {
        // The stream has no timezone; parse the wall time and pin offset 0 for deterministic deltas.
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero);
        return DateTimeOffset.MinValue;
    }

    private static long? ParseLong(string s)
        => long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string[] SplitLines(string text)
    {
        var rawLines = text.Split('\n');
        for (int i = 0; i < rawLines.Length; i++)
            if (rawLines[i].EndsWith('\r'))
                rawLines[i] = rawLines[i][..^1];
        return rawLines;
    }
}
