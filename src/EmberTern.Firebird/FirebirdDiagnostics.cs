using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Read-only transaction diagnostics for the C1/C2 investigation. Surfaces the
/// real server-side picture — the active transaction id, its TPB parameters as
/// the engine actually recorded them (MON$TRANSACTIONS), and the live
/// attachments (MON$ATTACHMENTS). No UI; this is infrastructure for manual
/// verification (e.g. "did nowait take effect?", "which transactions are pinning
/// the database?").
///
/// Follows the same access pattern as the other readers: holds the connection's
/// CommandLock for the command body and attaches to the user's working
/// transaction when one is active (so CURRENT_TRANSACTION resolves to the working
/// tx). Never opens its own transaction.
/// </summary>
public sealed class FirebirdDiagnostics
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    public FirebirdDiagnostics(FirebirdConnectionService connectionService, TransactionService? transactionService = null)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    /// <summary>The Firebird transaction id seen by the current command path.</summary>
    public async Task<long> GetCurrentTransactionIdAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CURRENT_TRANSACTION FROM RDB$DATABASE";
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read current transaction id: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    /// <summary>
    /// One-line human-readable summary of the transaction the current command path
    /// sees (id + isolation + lock resolution + read-only flag). Used by the
    /// EMBERTERN_TX_DIAG begin-time log hook and by manual verification.
    /// </summary>
    public async Task<string> DescribeCurrentTransactionAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = CurrentTransactionSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return "no MON$TRANSACTIONS row for CURRENT_TRANSACTION";
            }

            var id = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
            var isolation = reader.IsDBNull(1) ? -1 : Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
            var lockTimeout = reader.IsDBNull(2) ? int.MinValue : Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
            var readOnly = !reader.IsDBNull(3) && Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture) != 0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "tx={0} isolation=[{1}] lock=[{2}] readOnly={3}",
                id, DecodeIsolationMode(isolation), DecodeLockTimeout(lockTimeout), readOnly);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not describe current transaction: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    /// <summary>All transactions currently visible in MON$TRANSACTIONS.</summary>
    public async Task<IReadOnlyList<MonTransactionInfo>> GetTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = TransactionsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            var results = new List<MonTransactionInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                var attachmentId = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                var state = reader.IsDBNull(2) ? -1 : Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
                var isolation = reader.IsDBNull(3) ? -1 : Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture);
                var lockTimeout = reader.IsDBNull(4) ? int.MinValue : Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture);
                var readOnly = !reader.IsDBNull(5) && Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture) != 0;
                results.Add(new MonTransactionInfo(
                    id, attachmentId, state, DecodeTransactionState(state),
                    isolation, DecodeIsolationMode(isolation),
                    lockTimeout, DecodeLockTimeout(lockTimeout), readOnly));
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read MON$TRANSACTIONS: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    /// <summary>All attachments currently visible in MON$ATTACHMENTS.</summary>
    public async Task<IReadOnlyList<MonAttachmentInfo>> GetAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection();
        await _connectionService.CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = AttachmentsSql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService?.ActiveTransaction;
            var results = new List<MonAttachmentInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                var user = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                var role = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                var remoteAddress = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
                var remoteProcess = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim();
                var state = reader.IsDBNull(5) ? -1 : Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture);
                var attachmentName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6).Trim();
                results.Add(new MonAttachmentInfo(
                    id, user, role, remoteAddress, remoteProcess, state,
                    DecodeAttachmentState(state), attachmentName));
            }
            return results;
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read MON$ATTACHMENTS: {ex.Message}", ex);
        }
        finally
        {
            _connectionService.CommandLock.Release();
        }
    }

    /// <summary>
    /// Full text report: current transaction, every live transaction, every
    /// attachment. Intended for the manual C1/C2 verification — dump it to the log
    /// or a file and read it side by side with IBExpert.
    /// </summary>
    public async Task<string> BuildReportAsync(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== EmberTern transaction diagnostics ===");
        sb.Append("current: ").AppendLine(await DescribeCurrentTransactionAsync(cancellationToken).ConfigureAwait(false));

        sb.AppendLine("--- MON$TRANSACTIONS ---");
        foreach (var t in await GetTransactionsAsync(cancellationToken).ConfigureAwait(false))
        {
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "  tx={0} att={1} state={2} isolation={3} lock={4} readOnly={5}",
                t.TransactionId, t.AttachmentId, t.StateText, t.IsolationMode, t.LockTimeout, t.ReadOnly)
              .AppendLine();
        }

        sb.AppendLine("--- MON$ATTACHMENTS ---");
        foreach (var a in await GetAttachmentsAsync(cancellationToken).ConfigureAwait(false))
        {
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "  att={0} user={1} role={2} remote={3} process={4} state={5}",
                a.AttachmentId, a.User, a.Role, a.RemoteAddress, a.RemoteProcess, a.StateText)
              .AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends a timestamped line to the same %TEMP%\EmberTern-debug.log the
    /// connection service uses. Best-effort — never throws.
    /// </summary>
    public static void AppendDebugLog(string line)
    {
        try
        {
            var stamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}";
            var logPath = Path.Combine(Path.GetTempPath(), "EmberTern-debug.log");
            File.AppendAllText(logPath, stamped + Environment.NewLine);
        }
        catch
        {
            // logging must never break the caller
        }
    }

    // MON$ISOLATION_MODE codes (Firebird 2.1+):
    //   0 = consistency (snapshot table stability)
    //   1 = concurrency (snapshot)
    //   2 = read committed, record version
    //   3 = read committed, no record version
    //   4 = read committed, read consistency (Firebird 4+)
    internal static string DecodeIsolationMode(int code) => code switch
    {
        0 => "consistency (snapshot table stability)",
        1 => "concurrency (snapshot)",
        2 => "read committed (rec_version)",
        3 => "read committed (no_rec_version)",
        4 => "read committed (read_consistency)",
        _ => $"unknown ({code})",
    };

    // MON$LOCK_TIMEOUT: -1 = wait (infinite), 0 = no wait, N>0 = wait N seconds.
    internal static string DecodeLockTimeout(int code) => code switch
    {
        int.MinValue => "n/a",
        -1 => "wait (infinite)",
        0 => "no wait",
        > 0 => $"wait {code}s",
        _ => $"unknown ({code})",
    };

    // MON$STATE for transactions: 0 = idle, 1 = active.
    internal static string DecodeTransactionState(int code) => code switch
    {
        0 => "idle",
        1 => "active",
        _ => $"unknown ({code})",
    };

    // MON$STATE for attachments: 0 = idle, 1 = active.
    internal static string DecodeAttachmentState(int code) => code switch
    {
        0 => "idle",
        1 => "active",
        _ => $"unknown ({code})",
    };

    // Columns ordered to match the DescribeCurrentTransactionAsync reader:
    // 0 id, 1 isolation, 2 lockTimeout, 3 readOnly.
    internal const string CurrentTransactionSql =
        "SELECT MON$TRANSACTION_ID, MON$ISOLATION_MODE, MON$LOCK_TIMEOUT, MON$READ_ONLY " +
        "FROM MON$TRANSACTIONS WHERE MON$TRANSACTION_ID = CURRENT_TRANSACTION";

    // NOTE: kept as separate consts (not interpolated) so tests can pin the shape.
    internal const string TransactionsSql =
        "SELECT MON$TRANSACTION_ID, MON$ATTACHMENT_ID, MON$STATE, " +
        "MON$ISOLATION_MODE, MON$LOCK_TIMEOUT, MON$READ_ONLY " +
        "FROM MON$TRANSACTIONS ORDER BY MON$TRANSACTION_ID";

    internal const string AttachmentsSql =
        "SELECT MON$ATTACHMENT_ID, MON$USER, MON$ROLE, MON$REMOTE_ADDRESS, " +
        "MON$REMOTE_PROCESS, MON$STATE, MON$ATTACHMENT_NAME " +
        "FROM MON$ATTACHMENTS ORDER BY MON$ATTACHMENT_ID";
}

/// <summary>One MON$TRANSACTIONS row, decoded for human reading.</summary>
public sealed record MonTransactionInfo(
    long TransactionId,
    long AttachmentId,
    int StateCode,
    string StateText,
    int IsolationModeCode,
    string IsolationMode,
    int LockTimeoutCode,
    string LockTimeout,
    bool ReadOnly);

/// <summary>One MON$ATTACHMENTS row, decoded for human reading.</summary>
public sealed record MonAttachmentInfo(
    long AttachmentId,
    string User,
    string Role,
    string RemoteAddress,
    string RemoteProcess,
    int StateCode,
    string StateText,
    string AttachmentName);
