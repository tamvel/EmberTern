using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Core.Trace;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace EmberTern.Firebird;

/// <summary>Lifecycle state of a live trace session.</summary>
public enum TraceSessionState
{
    Stopped,
    Starting,
    Running,
    Paused,
    Stopping,
    Faulted,
}

/// <summary>A trace-session failure surfaced to the UI (wraps the driver's <c>FbException</c>).</summary>
public sealed class TraceException : Exception
{
    public TraceException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Drives a live Firebird Services-API trace session (the M1-verified managed
/// <see cref="FbTrace"/>, no <c>fbtrace.exe</c>/<c>fbclient.dll</c>). Owns the
/// long-running stream and the memory-bounded <see cref="TraceEventRingBuffer"/>:
/// server output lines are folded into <see cref="TraceEvent"/>s by a
/// <see cref="TraceStreamAccumulator"/> and pushed into the buffer (drop-oldest with a
/// visible <see cref="DroppedCount"/> — never blocks the server, never grows without
/// bound). Start/Stop/Suspend/Resume map to the driver 1:1.
/// <para>
/// Threading: <c>FbTrace.Start</c> blocks and streams until the session is stopped, so it
/// runs on a background task; a SEPARATE control <see cref="FbTrace"/> lists/stops/suspends
/// the session by id (the standard Firebird trace dance). The pure, testable logic (event
/// flag translation, service connection string, buffering, folding) lives here / in Core;
/// the wire behaviour (session-id discovery, streaming, stop) is manual-smoke — it needs a
/// live FB with a privileged service login and cannot be unit-tested.
/// </para>
/// <para>
/// This is the reusable *pattern* — a bounded-buffer streaming session — for a future
/// Diagnostics Center (Session/Lock/Transaction monitors). It is deliberately standalone,
/// NOT a shared base class (that abstraction waits for its second consumer).
/// </para>
/// </summary>
public sealed class FirebirdTraceService : IAsyncDisposable
{
    private static readonly Regex SessionIdRx = new(@"Session ID:\s*(?<id>\d+)", RegexOptions.Compiled);

    private readonly FirebirdConnectionService _connectionService;

    private FbTrace? _streamTrace;
    private Task? _pumpTask;
    private TraceStreamAccumulator? _accumulator;
    private string? _sessionName;
    private string? _serviceConnectionString;
    private int? _sessionId;
    private TraceSessionState _state = TraceSessionState.Stopped;

    public FirebirdTraceService(FirebirdConnectionService connectionService, int bufferCapacity = 50_000)
    {
        _connectionService = connectionService;
        Buffer = new TraceEventRingBuffer(bufferCapacity);
    }

    public TraceSessionState State => _state;

    public TraceEventRingBuffer Buffer { get; }

    /// <summary>Events dropped because the buffer was full (surfaced in the UI so the loss is honest).</summary>
    public long DroppedCount => Buffer.DroppedCount;

    /// <summary>Raised (on the trace/background thread) with each batch of newly folded events. The
    /// consuming VM must marshal to the UI thread.</summary>
    public event EventHandler<IReadOnlyList<TraceEvent>>? EventsReceived;

    public event EventHandler? StateChanged;

    /// <summary>Starts a session for the active connection's database. <paramref name="selfAttachmentIds"/>
    /// are EmberTern's own attachment ids (data + metadata lanes) so the session's own noise is flagged
    /// and hidden. Returns once the background stream has been kicked off.</summary>
    public Task StartAsync(TraceSessionConfig config, IReadOnlyCollection<long> selfAttachmentIds, CancellationToken cancellationToken = default)
    {
        if (_state is not TraceSessionState.Stopped and not TraceSessionState.Faulted)
            throw new InvalidOperationException($"Cannot start a trace session while it is {_state}.");

        var profile = _connectionService.ActiveProfile
            ?? throw new TraceException("Connect to a database before starting the Activity Monitor.");

        SetState(TraceSessionState.Starting);
        Buffer.Clear(resetDropped: true);
        _sessionId = null;
        _accumulator = new TraceStreamAccumulator(selfAttachmentIds);
        _sessionName = "EmberTern-" + Guid.NewGuid().ToString("N")[..12];
        _serviceConnectionString = BuildServiceConnectionString(profile);

        try
        {
            var trace = new FbTrace(FbTraceVersion.Detect, _serviceConnectionString);
            trace.DatabasesConfigurations.Add(BuildDatabaseConfig(config, profile.DatabasePath));
            trace.ServiceOutput += OnServiceOutput;
            _streamTrace = trace;

            _pumpTask = Task.Run(() =>
            {
                try
                {
                    SetState(TraceSessionState.Running);
                    trace.Start(_sessionName); // blocks + streams until the session is stopped
                }
                catch (Exception ex)
                {
                    SetState(TraceSessionState.Faulted);
                    EventsReceived?.Invoke(this, Array.Empty<TraceEvent>()); // wake the consumer to re-read State
                    LastError = ex;
                }
            }, cancellationToken);
        }
        catch (FbException ex)
        {
            SetState(TraceSessionState.Faulted);
            throw new TraceException("Failed to start the trace session. A privileged (SYSDBA / trace) service login is required.", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>The last background-stream error, if the session faulted.</summary>
    public Exception? LastError { get; private set; }

    private void OnServiceOutput(object? sender, ServiceOutputEventArgs e)
    {
        var accumulator = _accumulator;
        if (accumulator is null || string.IsNullOrEmpty(e.Message))
            return;

        var events = accumulator.Append(e.Message);
        if (events.Count == 0)
            return;

        foreach (var ev in events)
            Buffer.Add(ev);
        EventsReceived?.Invoke(this, events);
    }

    /// <summary>Suspends the server session (Pause). No output arrives while suspended.</summary>
    public Task PauseAsync(CancellationToken ct = default) => ControlAsync((t, id) => t.Suspend(id), TraceSessionState.Paused, ct);

    /// <summary>Resumes a suspended session.</summary>
    public Task ResumeAsync(CancellationToken ct = default) => ControlAsync((t, id) => t.Resume(id), TraceSessionState.Running, ct);

    /// <summary>Stops the session server-side (which unblocks the streaming task) and flushes the last
    /// buffered block. Idempotent.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_state is TraceSessionState.Stopped or TraceSessionState.Stopping)
            return;

        SetState(TraceSessionState.Stopping);
        try
        {
            await ControlAsync((t, id) => t.Stop(id), TraceSessionState.Stopping, cancellationToken).ConfigureAwait(false);

            if (_pumpTask is { } pump)
                await pump.ConfigureAwait(false); // Start() returns once the session is stopped

            // Emit the final buffered block (a block only closes when the next header arrives).
            if (_accumulator is { } accumulator)
            {
                var tail = accumulator.Flush();
                if (tail.Count > 0)
                {
                    foreach (var ev in tail) Buffer.Add(ev);
                    EventsReceived?.Invoke(this, tail);
                }
            }
        }
        finally
        {
            DetachStream();
            SetState(TraceSessionState.Stopped);
        }
    }

    /// <summary>Clears the buffered events (keeps the running session).</summary>
    public void Clear() => Buffer.Clear();

    // Runs a control command (Stop/Suspend/Resume) via a SEPARATE service connection, discovering
    // the session id by name from List() output. The streaming FbTrace is busy blocking in Start().
    private async Task ControlAsync(Action<FbTrace, int> command, TraceSessionState newState, CancellationToken ct)
    {
        if (_serviceConnectionString is null)
            return;

        try
        {
            var id = _sessionId ??= await ResolveSessionIdAsync(ct).ConfigureAwait(false);
            if (id is null)
                return;

            await Task.Run(() =>
            {
                var control = new FbTrace(FbTraceVersion.Detect, _serviceConnectionString);
                command(control, id.Value);
            }, ct).ConfigureAwait(false);

            if (newState is TraceSessionState.Paused or TraceSessionState.Running)
                SetState(newState);
        }
        catch (FbException ex)
        {
            throw new TraceException("Trace control command failed.", ex);
        }
    }

    // Lists running sessions and returns the id of ours (matched by the unique session name).
    private Task<int?> ResolveSessionIdAsync(CancellationToken ct)
        => Task.Run<int?>(() =>
        {
            var control = new FbTrace(FbTraceVersion.Detect, _serviceConnectionString!);
            int? found = null;
            int? pendingId = null;
            control.ServiceOutput += (_, e) =>
            {
                foreach (var raw in (e.Message ?? string.Empty).Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    var m = SessionIdRx.Match(line);
                    if (m.Success) pendingId = int.Parse(m.Groups["id"].Value);
                    else if (pendingId is { } id && _sessionName is { } name && line.Contains(name, StringComparison.Ordinal))
                        found = id;
                }
            };
            control.List();
            return found;
        }, ct);

    private void DetachStream()
    {
        if (_streamTrace is { } t)
            t.ServiceOutput -= OnServiceOutput;
        _streamTrace = null;
        _pumpTask = null;
    }

    private void SetState(TraceSessionState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch { /* best-effort teardown */ }
    }

    // ---------------------------------------------------------------- pure, testable translation

    /// <summary>Translates the Core preset into the driver's event flags. <c>PrintPerf</c> is always
    /// on (it carries duration + the per-table read block — the diagnostic payload). Procedure/function
    /// include START so pairs fold; triggers only report FINISH (matching Firebird's default).</summary>
    internal static FbDatabaseTraceEvents BuildDatabaseEvents(TraceSessionConfig c)
    {
        FbDatabaseTraceEvents e = FbDatabaseTraceEvents.PrintPerf;
        if (c.IncludeStatements) e |= FbDatabaseTraceEvents.StatementFinish;
        if (c.IncludeProcedures) e |= FbDatabaseTraceEvents.ProcedureStart | FbDatabaseTraceEvents.ProcedureFinish;
        if (c.IncludeFunctions) e |= FbDatabaseTraceEvents.FunctionStart | FbDatabaseTraceEvents.FunctionFinish;
        if (c.IncludeTriggers) e |= FbDatabaseTraceEvents.TriggerFinish;
        if (c.IncludeErrors) e |= FbDatabaseTraceEvents.Errors;
        if (c.IncludeConnections) e |= FbDatabaseTraceEvents.Connections;
        if (c.IncludeTransactions) e |= FbDatabaseTraceEvents.Transactions;
        return e;
    }

    /// <summary>Builds a Services (no-database) connection string from the profile.</summary>
    internal static string BuildServiceConnectionString(ConnectionProfile profile)
        => new FbConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(profile.Host) ? "localhost" : profile.Host,
            Port = profile.Port > 0 ? profile.Port : 3050,
            UserID = profile.Username,
            Password = profile.Password,
            ServerType = FbServerType.Default,
        }.ToString();

    private static FbDatabaseTraceConfiguration BuildDatabaseConfig(TraceSessionConfig config, string databasePath)
        => new()
        {
            // NOTE(smoke): DatabaseName is a server-side regex over attaching DB paths. A literal
            // Windows path may need escaping to match exactly; tune against a live server. Scoping to
            // the connected DB avoids tracing other databases on a shared server.
            DatabaseName = databasePath,
            Enabled = true,
            Events = BuildDatabaseEvents(config),
            TimeThreshold = TimeSpan.FromMilliseconds(config.TimeThresholdMs),
            MaxSQLLength = config.MaxSqlLength,
        };
}
