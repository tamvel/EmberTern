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
/// Which physical attachment a command runs on. C2 opens two connections to the
/// same database: <see cref="Data"/> (#1) carries user SQL/DML and the data
/// working transaction; <see cref="Metadata"/> (#2) carries metadata browsing and
/// the metadata working transaction (DDL). Two attachments are required because the
/// managed FirebirdClient forbids two transactions on one FbConnection.
/// </summary>
public enum ConnectionRole
{
    Data,
    Metadata,
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

    public bool IsConnected => _activeConnection is { State: System.Data.ConnectionState.Open };

    // True when the metadata attachment (#2) opened successfully and is distinct from
    // the data attachment. When false (e.g. the server rejected the second attach), the
    // Metadata role transparently aliases the Data role so metadata work still functions
    // — it just shares the data connection/lock/transaction (pre-C2 behaviour).
    public bool MetadataIsIndependent
        => _metadataConnection is { State: System.Data.ConnectionState.Open };

    public ConnectionProfile? ActiveProfile => _activeProfile;

    public event EventHandler? ActiveConnectionChanged;

    internal SemaphoreSlim CommandLock => _commandLock;

    // Per-role command lock. Metadata falls back to the data lock when the second
    // attachment is unavailable, keeping serialization correct on the shared connection.
    internal SemaphoreSlim GetCommandLock(ConnectionRole role)
        => role == ConnectionRole.Metadata && MetadataIsIndependent ? _metadataCommandLock : _commandLock;

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

        ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        if (_activeConnection is null && _metadataConnection is null)
        {
            return;
        }

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
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new ConnectionFailedException(MapErrorMessage(ex, profile), ex);
        }
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
        if (_activeConnection is null || _activeConnection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("No active Firebird connection.");
        }
        return _activeConnection;
    }

    public void Dispose()
    {
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
