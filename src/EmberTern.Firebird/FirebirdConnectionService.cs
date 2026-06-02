using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

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
    private ConnectionProfile? _activeProfile;

    // Serializes all "owned" BeginTransactionAsync calls on _activeConnection.
    // Firebird/managed driver rejects a second concurrent tx on the same
    // connection with "Parallel transactions are not supported". The borrow-
    // or-begin pattern handles the user-tx-vs-reader-tx case, but reader-vs-
    // reader (e.g. lazy-loading two TableDetail tabs after reconnect) and
    // reader-vs-executor (F5 mid-load) still need a real lock.
    //
    // Held only while a Begin is in flight AND while an owned tx remains
    // active. Borrowed txs don't touch this gate — multiple borrowers can
    // share the user's tx freely.
    private readonly SemaphoreSlim _transactionGate = new(1, 1);

    public bool IsConnected => _activeConnection is { State: System.Data.ConnectionState.Open };

    public ConnectionProfile? ActiveProfile => _activeProfile;

    public event EventHandler? ActiveConnectionChanged;

    internal SemaphoreSlim TransactionGate => _transactionGate;

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
        ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        if (_activeConnection is null)
        {
            return;
        }

        try
        {
            await _activeConnection.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort close — we still want to release the handle
        }

        await _activeConnection.DisposeAsync().ConfigureAwait(false);
        _activeConnection = null;
        _activeProfile = null;
        ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
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

    public FbConnection RequireOpenConnection()
    {
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

    private static string MapErrorMessage(Exception ex, ConnectionProfile profile)
    {
        var endpoint = $"{profile.Host}:{profile.Port}";

        if (ex is FbException fb)
        {
            return MapFbException(fb, profile, endpoint);
        }

        if (FindInner<SocketException>(ex) is { } socket)
        {
            return MapSocketError(socket, endpoint);
        }

        if (FindInner<System.IO.IOException>(ex) is { } io)
        {
            return $"Network error while connecting to {endpoint}: {io.Message}";
        }

        if (FindInner<TimeoutException>(ex) is not null)
        {
            return $"Connection to {endpoint} timed out.";
        }

        return $"Could not connect to {endpoint}: {ex.Message}";
    }

    private static string MapFbException(FbException fb, ConnectionProfile profile, string endpoint)
    {
        var msg = fb.Message ?? string.Empty;
        var lower = msg.ToLowerInvariant();

        if (lower.Contains("login") || lower.Contains("password") || fb.ErrorCode == 335544472)
        {
            return $"Invalid username or password for '{profile.Username}'.";
        }

        if (lower.Contains("file") && (lower.Contains("not found") || lower.Contains("no such")))
        {
            return $"Database file not found: {profile.DatabasePath}";
        }

        if (lower.Contains("unavailable database") || lower.Contains("i/o error") && lower.Contains("open"))
        {
            return $"Database is unavailable: {profile.DatabasePath}";
        }

        if (lower.Contains("character set") || lower.Contains("charset"))
        {
            return $"Unsupported character set: {profile.Charset}";
        }

        if (FindInner<SocketException>(fb) is { } socket)
        {
            return MapSocketError(socket, endpoint);
        }

        return $"Firebird error: {Sanitize(msg)}";
    }

    private static string MapSocketError(SocketException ex, string endpoint)
    {
        return ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => $"Could not reach host {endpoint}: connection refused (is Firebird running?).",
            SocketError.HostNotFound or SocketError.HostUnreachable => $"Host not found: {endpoint}.",
            SocketError.TimedOut => $"Connection to {endpoint} timed out.",
            SocketError.NetworkUnreachable => $"Network unreachable: {endpoint}.",
            _ => $"Could not reach host {endpoint} ({ex.SocketErrorCode}).",
        };
    }

    private static T? FindInner<T>(Exception ex) where T : Exception
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is T match)
            {
                return match;
            }
        }
        return null;
    }

    private static string Sanitize(string msg)
    {
        var trimmed = msg.Trim();
        return trimmed.Length > 240 ? trimmed[..240] + "…" : trimmed;
    }
}
