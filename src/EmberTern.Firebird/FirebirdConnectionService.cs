using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Which physical attachment a command runs on. Three connections to the same database:
/// <see cref="Data"/> (#1) carries user SQL/DML (SQL editor F5, table-data edits, Execute
/// Procedure) and the data working transaction; <see cref="Metadata"/> (#2) carries read-only
/// catalog browsing and owns NO transaction — reads use an implicit per-command transaction
/// (see <see cref="MetadataLane"/>), so they never entangle with, block, or are blocked by the
/// user's working transaction; <see cref="Ddl"/> (#3) carries Compile/structure DDL and NOTHING
/// else — it never holds a working transaction, so DDL can always begin its own autonomous
/// transaction without waiting on the user to settle theirs. Separate attachments are required
/// because the managed FirebirdClient forbids two transactions on one FbConnection (gotcha #89).
/// </summary>
public enum ConnectionRole
{
    Data,
    Metadata,
    Ddl,
}

public sealed class FirebirdConnectionService : IDisposable
{
    static FirebirdConnectionService()
    {
        // Required for WIN1250 / WIN1252 / ISO8859_2 etc. — the FirebirdClient driver
        // resolves charsets via Encoding.GetEncoding("windows-1250") at OpenAsync time,
        // which throws on .NET unless this provider is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private FbConnection? _activeConnection;
    private FbConnection? _metadataConnection;
    private FbConnection? _ddlConnection;
    private ConnectionProfile? _activeProfile;

    // FbConnection is single-threaded — concurrent commands on the same connection
    // hang or throw. The application has multiple fire-and-forget code paths that
    // can hit this connection in parallel: metadata eager-load, user-click expand,
    // SQL editor autocomplete column fetch, DDL fetch, TableDetail load, F5 execute.
    // This lock serializes them all. Different from a transaction gate — it gates
    // COMMAND EXECUTION, not transaction begins. Readers attach to the user's
    // working tx (or a per-command implicit tx) regardless.
    //
    // Each connection has its OWN lock — commands on #1 and #2 are independent and
    // must not serialize against each other (that's the whole point of two
    // attachments: data work and metadata work proceed in parallel).
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _metadataCommandLock = new(1, 1);
    private readonly SemaphoreSlim _ddlCommandLock = new(1, 1);

    // Live debug sessions (Stage X / D2, spec §4.1). Each owns its OWN attachment + transaction — decision
    // 5: a session is not a lane. Tracked here only so disconnect/reconnect tears them down deterministically
    // (the attachments must not outlive the profile's connection). A session deregisters itself on dispose.
    private readonly List<DebugSessionConnection> _debugSessions = new();

    public bool IsConnected => _activeConnection is { State: System.Data.ConnectionState.Open };

    // True when the dedicated DDL attachment (#3) opened. When false we degrade to the Data
    // connection — and then (and ONLY then) DDL must wait for the data working transaction to
    // settle, because one FbConnection allows one transaction (gotcha #89).
    public bool DdlIsIndependent
        => _ddlConnection is { State: System.Data.ConnectionState.Open };

    // True when the metadata attachment (#2) opened successfully and is distinct from
    // the data attachment. When false (e.g. the server rejected the second attach), the
    // Metadata role transparently aliases the Data role so metadata work still functions
    // — it just shares the data connection/lock/transaction (pre-C2 behaviour).
    public bool MetadataIsIndependent
        => _metadataConnection is { State: System.Data.ConnectionState.Open };

    public ConnectionProfile? ActiveProfile => _activeProfile;

    public event EventHandler? ActiveConnectionChanged;

    // Raised when the in-memory active profile is replaced in place (user edited the
    // currently-connected connection and saved). Distinct from ActiveConnectionChanged
    // so consumers can refresh status/profile display WITHOUT triggering the heavier
    // connection-switch flow (workspace stash/reload, column-cache clear).
    public event EventHandler? ActiveProfileUpdated;

    /// <summary>
    /// Replaces the captured active profile with an edited copy of the SAME connection
    /// (matched by Id), so transaction-profile resolution (read at begin time) and the
    /// status bar immediately reflect the new settings. No reconnect: connection-string
    /// changes (host/db/credentials/charset) only take effect on the next reconnect;
    /// transaction profiles and status display update right away. Returns true when the
    /// active profile was actually replaced.
    /// </summary>
    public bool UpdateActiveProfile(ConnectionProfile profile)
    {
        if (!ShouldReplaceActiveProfile(_activeProfile, profile))
        {
            return false;
        }

        _activeProfile = profile;
        ActiveProfileUpdated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Pure decision so a unit test can pin it without a live connection: replace only
    // when something is active and the incoming edit targets that same connection Id.
    internal static bool ShouldReplaceActiveProfile(ConnectionProfile? active, ConnectionProfile? incoming)
        => active is not null && incoming is not null && active.Id == incoming.Id;

    internal SemaphoreSlim CommandLock => _commandLock;

    // Per-role command lock. Metadata falls back to the data lock when the second
    // attachment is unavailable, keeping serialization correct on the shared connection.
    internal SemaphoreSlim GetCommandLock(ConnectionRole role) => role switch
    {
        ConnectionRole.Metadata when MetadataIsIndependent => _metadataCommandLock,
        ConnectionRole.Ddl when DdlIsIndependent => _ddlCommandLock,
        _ => _commandLock,
    };

    public async Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (IsConnected)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }

        var connectionString = BuildConnectionString(profile);
        LogConnectionAttempt("Connect", profile, connectionString);
        var connection = new FbConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new ConnectionFailedException(MapErrorMessage(ex, profile), ex);
        }

        // FB3+ precondition gate (decision 8 / spec §1.3) — refuse a pre-FB3 server the moment the FIRST
        // attachment is open, BEFORE opening the Metadata/Ddl lanes (same server ⇒ same version, so gating
        // the first covers all three, and we never open extra attachments to an unsupported server). Not
        // error interpretation: a check on a fact we know, on an already-open connection — MapErrorMessage
        // stays untouched. Close cleanly so no half-open attachment is left behind.
        if (!IsSupportedServerVersion(connection.ServerVersion))
        {
            var serverVersion = connection.ServerVersion;
            await CloseAndDisposeAsync(connection).ConfigureAwait(false);
            throw new ConnectionFailedException(UnsupportedServerMessage(serverVersion));
        }

        _activeConnection = connection;
        _activeProfile = profile;

        // Open the second (metadata) attachment to the same database, best-effort.
        // Same profile/credentials, no pooling. If it fails (e.g. server connection
        // limit) we log and degrade: the Metadata role aliases the Data connection.
        try
        {
            var metadata = new FbConnection(connectionString);
            await metadata.OpenAsync(cancellationToken).ConfigureAwait(false);
            _metadataConnection = metadata;
        }
        catch (Exception ex)
        {
            _metadataConnection = null;
            LogConnectionAttempt("MetadataConnectFailed: " + ex.Message, profile, connectionString);
        }

        // Open the third (DDL) attachment. It carries Compile/structure DDL and nothing else,
        // and never holds a working transaction — so a Compile can always begin its own
        // autonomous transaction regardless of what the user left open on the Data lane
        // (the SQL editor's SELECT) or the Metadata lane. Best-effort: if it fails we degrade
        // to the Data connection, where the old "settle the working tx first" rule applies.
        try
        {
            var ddl = new FbConnection(connectionString);
            await ddl.OpenAsync(cancellationToken).ConfigureAwait(false);
            _ddlConnection = ddl;
        }
        catch (Exception ex)
        {
            _ddlConnection = null;
            LogConnectionAttempt("DdlConnectFailed: " + ex.Message, profile, connectionString);
        }

        ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a dedicated attachment for a new debug session and returns its
    /// <see cref="DebugSessionConnection"/> (spec §4.1) — its own <see cref="FbConnection"/> and its own
    /// transaction (begun with the explicit debug TPB, §4.2), independent of the Data/Metadata/Ddl lanes.
    /// The session is registered so <see cref="DisconnectAsync"/>/<see cref="Dispose"/> tears it down; it
    /// deregisters itself when disposed. Each session is another attachment — a server connection-limit
    /// refusal surfaces as a <see cref="ConnectionFailedException"/> (the thinking behind gotcha #89), never
    /// a broken app.
    /// </summary>
    public async Task<DebugSessionConnection> CreateDebugSessionAsync(
        DebugIsolation isolation, CancellationToken cancellationToken = default)
    {
        if (_activeProfile is null || !IsConnected)
        {
            throw new InvalidOperationException("No active Firebird connection.");
        }

        var profile = _activeProfile;
        var connectionString = BuildConnectionString(profile);
        var connection = new FbConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new ConnectionFailedException(MapErrorMessage(ex, profile), ex);
        }

        var session = new DebugSessionConnection(connection, isolation, this);
        try
        {
            await session.BeginAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false); // closes the attachment, no half-open state
            throw;
        }
        _debugSessions.Add(session);
        return session;
    }

    // A debug session deregisters itself here on dispose (called from DebugSessionConnection.DisposeAsync).
    internal void RemoveDebugSession(DebugSessionConnection session) => _debugSessions.Remove(session);

    // Tears down every live debug session — their attachments must not outlive the profile's connection.
    // Snapshots first because each DisposeAsync deregisters itself (mutating _debugSessions).
    private async Task TearDownDebugSessionsAsync()
    {
        if (_debugSessions.Count == 0)
        {
            return;
        }
        foreach (var session in _debugSessions.ToArray())
        {
            try { await session.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort teardown */ }
        }
        _debugSessions.Clear();
    }

    public async Task DisconnectAsync()
    {
        if (_activeConnection is null && _metadataConnection is null && _ddlConnection is null
            && _debugSessions.Count == 0)
        {
            return;
        }

        await TearDownDebugSessionsAsync().ConfigureAwait(false);

        await CloseAndDisposeAsync(_ddlConnection).ConfigureAwait(false);
        _ddlConnection = null;

        await CloseAndDisposeAsync(_metadataConnection).ConfigureAwait(false);
        _metadataConnection = null;

        await CloseAndDisposeAsync(_activeConnection).ConfigureAwait(false);
        _activeConnection = null;
        _activeProfile = null;
        ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task CloseAndDisposeAsync(FbConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort close — we still want to release the handle
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    public async Task TestConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var connectionString = BuildConnectionString(profile);
        LogConnectionAttempt("Test", profile, connectionString);
        await using var connection = new FbConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new ConnectionFailedException(MapErrorMessage(ex, profile), ex);
        }

        // Same FB3+ precondition as ConnectAsync (spec §1.3) — a Test against a pre-FB3 server refuses
        // with the same legible message rather than reporting a bare "success". The `await using` disposes
        // the connection either way, so nothing is left half-open.
        var supported = IsSupportedServerVersion(connection.ServerVersion);
        var serverVersion = connection.ServerVersion;
        await connection.CloseAsync().ConfigureAwait(false);
        if (!supported)
        {
            throw new ConnectionFailedException(UnsupportedServerMessage(serverVersion));
        }
    }

    /// <summary>
    /// Runs administrative maintenance statements (e.g. <c>SET STATISTICS INDEX</c>)
    /// each in its OWN short, auto-committed transaction on a transient connection to
    /// the active database — fully independent of the Data/Metadata working transactions
    /// (C2 lanes). This matches how IBExpert recomputes statistics: the operation
    /// completes immediately and leaves NO transaction pending for the user to Commit.
    /// A separate attachment is used precisely because the managed FbConnection allows
    /// only one transaction at a time, so we must not piggy-back on (or block) a working
    /// transaction. Returns a per-statement result aligned with <paramref name="statements"/>:
    /// null = success, otherwise the server error message (so a batch can continue past a
    /// single failure and report which ones failed).
    /// </summary>
    public async Task<IReadOnlyList<string?>> ExecuteAdminBatchAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken = default,
        IProgress<(int Index, string? Error)>? progress = null,
        FbTransactionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (statements.Count == 0)
        {
            return Array.Empty<string?>();
        }
        if (_activeProfile is null)
        {
            throw new InvalidOperationException("No active Firebird connection.");
        }

        var results = new string?[statements.Count];
        var connectionString = BuildConnectionString(_activeProfile);
        await using var connection = new FbConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < statements.Count; i++)
        {
            // Cancellation stops BEFORE the next statement — already-run ones are committed
            // (autonomous per-statement), the rest are simply not attempted. progress reports
            // each result as it completes so the caller (batch-results dialog) updates live.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            FbTransaction? tx = null;
            try
            {
                // Honour the caller's explicit TPB (Developer Mode = WAIT + lock timeout;
                // Standard = NOWAIT) so recompile waits for an in-use object instead of
                // failing immediately. Falls back to the driver default when unspecified
                // (e.g. SET STATISTICS callers that pass none).
                tx = (FbTransaction)(options is null
                    ? await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                    : await connection.BeginTransactionAsync(options).ConfigureAwait(false));
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = statements[i];
                cmd.CommandTimeout = 0;
                cmd.Transaction = tx;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                results[i] = null;
            }
            catch (FbException ex)
            {
                results[i] = ex.Message;
                if (tx is not null)
                {
                    try { await tx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { /* best-effort */ }
                }
            }
            finally
            {
                if (tx is not null)
                {
                    await tx.DisposeAsync().ConfigureAwait(false);
                }
            }

            progress?.Report((i, results[i]));
        }

        return results;
    }

    /// <summary>
    /// Runs all <paramref name="statements"/> as DDL in ONE transaction on the dedicated
    /// DDL attachment (#3) and auto-commits on success. That attachment carries nothing else
    /// and never holds a working transaction, so Compile is independent of whatever the user
    /// left open on the Data lane (an un-committed SQL-editor SELECT) or the Metadata lane.
    ///
    /// <para>Why a separate attachment is safe (measured on FB5, Lab DB): the cross-attachment
    /// "object … is in use" that once forced co-location onto the main connection is a
    /// TRANSIENT metadata-cache lock, not a permanent pin. It only bites a NOWAIT transaction;
    /// a WAIT transaction clears it in ~10 ms because the holding attachment releases its
    /// cached metadata on demand. An unrelated open transaction on another attachment does not
    /// block DDL at all. Hence <see cref="FirebirdDdlExecutor.BuildDdlTransactionOptions(bool)"/>
    /// always uses WAIT with a bounded lock timeout — that, not co-location, is the real fix.</para>
    ///
    /// Propagates the FbException (after rollback) to the caller. Holds the DDL lane's own
    /// command lock for the whole batch (gotcha #31); it does not serialize against data reads.
    /// </summary>
    public async Task ExecuteDdlAsync(IReadOnlyList<string> statements, FbTransactionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(options);
        if (statements.Count == 0) return;

        // Resolve the connection BEFORE acquiring the lock so a missing connection surfaces as
        // InvalidOperationException without leaking the lock. Capture the lane's lock ONCE
        // (gotcha #98/#120 — never re-invoke the accessor at Release()).
        var connection = RequireOpenConnection(ConnectionRole.Ddl);
        var commandLock = GetCommandLock(ConnectionRole.Ddl);

        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        FbTransaction? tx = null;
        try
        {
            tx = (FbTransaction)await connection.BeginTransactionAsync(options).ConfigureAwait(false);
            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = statement;
                cmd.CommandTimeout = 0;
                cmd.Transaction = tx;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Roll back first so the DDL connection has no active tx — then the
            // (env-gated) diagnostic dump can open its own short tx cleanly and show
            // which attachment still holds the object the ALTER tried to change.
            if (tx is not null)
            {
                try { await tx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { /* best-effort */ }
            }
            if (IsLockOrInUse(ex))
            {
                await DumpInUseDiagnosticsAsync(connection, ex, cancellationToken).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }
            commandLock.Release();
        }
    }

    private static bool IsLockOrInUse(Exception ex)
    {
        var m = ex.Message;
        return m is not null
            && (m.Contains("lock conflict", StringComparison.OrdinalIgnoreCase)
                || m.Contains("in use", StringComparison.OrdinalIgnoreCase)
                || m.Contains("unsuccessful metadata update", StringComparison.OrdinalIgnoreCase));
    }

    // Best-effort, env-gated MON$ dump on the failing transient connection. Uses a
    // fresh short read transaction so it sees committed MON$ rows. Never throws.
    private static async Task DumpInUseDiagnosticsAsync(FbConnection connection, Exception ex, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("EMBERTERN_TX_DIAG") is null) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("DDL-IN-USE failure: " + ex.Message.Trim());
            sb.AppendLine("--- active MON$TRANSACTIONS (attachment / state / isolation) ---");
            await using var dtx = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = dtx;
                    cmd.CommandText =
                        "SELECT t.MON$TRANSACTION_ID, t.MON$ATTACHMENT_ID, t.MON$STATE, t.MON$ISOLATION_MODE, " +
                        "a.MON$USER, a.MON$REMOTE_PROCESS " +
                        "FROM MON$TRANSACTIONS t LEFT JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID " +
                        "ORDER BY t.MON$TRANSACTION_ID";
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                            "  tx={0} att={1} state={2} isolation={3} user={4} process={5}",
                            reader.IsDBNull(0) ? 0 : reader.GetValue(0),
                            reader.IsDBNull(1) ? 0 : reader.GetValue(1),
                            reader.IsDBNull(2) ? -1 : reader.GetValue(2),
                            reader.IsDBNull(3) ? -1 : reader.GetValue(3),
                            reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
                            reader.IsDBNull(5) ? "" : reader.GetString(5).Trim())
                          .AppendLine();
                    }
                }
                await dtx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { try { await dtx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { } }
            FirebirdDiagnostics.AppendDebugLog(sb.ToString());
        }
        catch { /* diagnostics must never mask the original error */ }
    }

    public FbConnection RequireOpenConnection() => RequireOpenConnection(ConnectionRole.Data);

    // Returns the open connection for the given role. Metadata falls back to the data
    // connection when the second attachment is unavailable (degraded mode), so the
    // connection/lock/transaction triple stays consistent for a reader on that role.
    public FbConnection RequireOpenConnection(ConnectionRole role)
    {
        if (role == ConnectionRole.Metadata && MetadataIsIndependent)
        {
            return _metadataConnection!;
        }
        if (role == ConnectionRole.Ddl && DdlIsIndependent)
        {
            return _ddlConnection!;
        }
        if (_activeConnection is null || _activeConnection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("No active Firebird connection.");
        }
        return _activeConnection;
    }

    public void Dispose()
    {
        // Tear down live debug sessions first — their attachments must not outlive the service. Block
        // best-effort at shutdown; DebugSessionConnection.DisposeAsync uses ConfigureAwait(false) throughout,
        // so GetResult cannot deadlock on a captured context. Snapshot: each dispose deregisters itself.
        foreach (var session in _debugSessions.ToArray())
        {
            try { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best-effort */ }
        }
        _debugSessions.Clear();

        try
        {
            _ddlConnection?.Close();
        }
        catch
        {
            // ignore
        }
        _ddlConnection?.Dispose();
        _ddlConnection = null;

        try
        {
            _metadataConnection?.Close();
        }
        catch
        {
            // ignore
        }
        _metadataConnection?.Dispose();
        _metadataConnection = null;

        try
        {
            _activeConnection?.Close();
        }
        catch
        {
            // ignore
        }
        _activeConnection?.Dispose();
        _activeConnection = null;
        _activeProfile = null;
    }

    private static string BuildConnectionString(ConnectionProfile profile)
    {
        var builder = new FbConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(profile.Host) ? "localhost" : profile.Host,
            Port = profile.Port > 0 ? profile.Port : 3050,
            Database = profile.DatabasePath,
            UserID = profile.Username,
            Password = profile.Password,
            Charset = string.IsNullOrWhiteSpace(profile.Charset) ? CharsetCatalog.Default : profile.Charset,
            Dialect = profile.Dialect is 1 or 3 ? profile.Dialect : 3,
            Pooling = false,
            ServerType = FbServerType.Default,
        };

        if (!string.IsNullOrWhiteSpace(profile.ClientLibraryPath))
        {
            builder.ClientLibrary = profile.ClientLibraryPath;
        }

        return builder.ToString();
    }

    // DEBUG: writes every connect/test attempt (with password masked) to
    // %TEMP%\EmberTern-debug.log so we can compare against IBExpert's connection.
    // Tied to the Firebird 3 auth investigation — remove together with the
    // AuthPlugins debug block above.
    private static void LogConnectionAttempt(string operation, ConnectionProfile profile, string connectionString)
    {
        try
        {
            var masked = MaskPassword(connectionString);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {operation} profile=\"{profile.Name}\" connStr={masked}";
            Debug.WriteLine(line);

            var logPath = Path.Combine(Path.GetTempPath(), "EmberTern-debug.log");
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
            // logging must never break a connection attempt
        }
    }

    private static string MaskPassword(string connectionString)
    {
        try
        {
            var b = new FbConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(b.Password))
            {
                b.Password = "***";
            }
            return b.ToString();
        }
        catch
        {
            return "<could not parse>";
        }
    }

    // FB3+ precondition (decision 8 / spec §1.3). EmberTern requires Firebird 3.0 or later (the debugger's
    // sub-routines/packages are FB3+, and the managed driver is Srp-only so FB2.5 is already unreachable);
    // this makes a pre-FB3 server a legible refusal instead of a confusing auth failure. Reuses the app's
    // one version parser (FirebirdDdlReader.ParseServerMajor) rather than adding a second version-parsing
    // site. Pure over the version string, so it is unit-testable without a live server.
    //
    // Fail-OPEN on an unparseable version (ParseServerMajor → 0): a successfully-opened connection is FB3+
    // by construction (the driver only speaks Srp, introduced in FB3), so a version string we cannot read
    // must not produce a false rejection — reject ONLY a positively-identified pre-FB3 major (1 or 2).
    internal static bool IsSupportedServerVersion(string? serverVersion)
    {
        var major = FirebirdDdlReader.ParseServerMajor(serverVersion);
        return major == 0 || major >= 3;
    }

    // The refusal message for a pre-FB3 server — states the fact and names the required version. Built in
    // this (Firebird) layer beside MapErrorMessage, the established home for connection-failure messages;
    // EmberTern.App.UiStrings is unreachable here (App references Firebird, never the reverse).
    internal static string UnsupportedServerMessage(string? serverVersion)
    {
        var v = string.IsNullOrWhiteSpace(serverVersion) ? "unknown" : serverVersion.Trim();
        return $"Unsupported Firebird server ({v}). EmberTern requires Firebird 3.0 or later.";
    }

    internal static string MapErrorMessage(Exception ex, ConnectionProfile profile)
    {
        // Always surface the server's own message verbatim. We deliberately do not
        // interpret or categorize error causes (wrong password, missing user, plugin
        // mismatch, host down, …) — the raw server text is authoritative and the user
        // or admin can read it directly. No hints, no special cases, no chain scanning.
        var endpoint = $"{profile.Host}:{profile.Port}";
        return $"Could not connect to {endpoint}: {ex.Message}";
    }
}
