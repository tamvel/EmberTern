using System;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

public enum TransactionState
{
    Idle,
    Active,
    Error,
}

public sealed class TransactionService : IDisposable
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly ConnectionRole _role;
    private readonly Func<ConnectionProfile?, TransactionProfile> _profileSelector;
    // Degraded-mode fallback (metadata lane only): when the second attachment is
    // unavailable the metadata service transparently delegates its whole transaction
    // lifecycle to the data service, so there is only ever ONE transaction on the
    // shared connection. Null for the data lane.
    private readonly TransactionService? _fallback;
    private FbTransaction? _activeTransaction;
    private TransactionState _state = TransactionState.Idle;
    private int _statementCount;

    // Convenience ctor: the data lane, reading DataTransactionProfile. Keeps existing
    // call sites (and tests) building a plain data-lane service.
    public TransactionService(FirebirdConnectionService connectionService)
        : this(connectionService, ConnectionRole.Data,
               p => p?.DataTransactionProfile ?? TransactionProfile.ReadCommitted)
    {
    }

    public TransactionService(
        FirebirdConnectionService connectionService,
        ConnectionRole role,
        Func<ConnectionProfile?, TransactionProfile> profileSelector,
        TransactionService? fallback = null)
    {
        _connectionService = connectionService;
        _role = role;
        _profileSelector = profileSelector;
        _fallback = fallback;
        _connectionService.ActiveConnectionChanged += OnConnectionChanged;
    }

    public ConnectionRole Role => _role;

    // True when this metadata-lane service must delegate to the data lane because the
    // second attachment failed to open (or was never independent). Always false for the
    // data lane.
    private bool ShouldDelegate
        => _role == ConnectionRole.Metadata && _fallback is not null && !_connectionService.MetadataIsIndependent;

    // The role this service actually runs commands on right now: its own when the
    // metadata attachment is independent, the data role otherwise.
    private ConnectionRole EffectiveRole
        => _role == ConnectionRole.Metadata && _connectionService.MetadataIsIndependent
            ? ConnectionRole.Metadata
            : ConnectionRole.Data;

    public TransactionState State => ShouldDelegate ? _fallback!.State : _state;
    public bool IsActive => State == TransactionState.Active;
    public bool IsIdle => State == TransactionState.Idle;
    public bool IsError => State == TransactionState.Error;
    public int StatementCount => ShouldDelegate ? _fallback!.StatementCount : _statementCount;
    public bool HasExecutedStatements => StatementCount > 0;
    public FbTransaction? ActiveTransaction => ShouldDelegate ? _fallback!.ActiveTransaction : _activeTransaction;

    // Connection + lock for the lane this service runs on. Readers/executors that hold
    // a reference to this service use these so the connection, lock, and transaction
    // always come from the same lane (and follow degraded-mode aliasing automatically).
    public FbConnection RequireOpenConnection() => _connectionService.RequireOpenConnection(EffectiveRole);
    internal SemaphoreSlim CommandLock => _connectionService.GetCommandLock(EffectiveRole);

    public event EventHandler? TransactionStateChanged;

    public async Task BeginTransactionAsync()
    {
        if (ShouldDelegate)
        {
            await _fallback!.BeginTransactionAsync().ConfigureAwait(false);
            return;
        }

        if (_activeTransaction is not null)
        {
            return;
        }

        var connection = _connectionService.RequireOpenConnection(EffectiveRole);
        var commandLock = _connectionService.GetCommandLock(EffectiveRole);
        // Serialize against in-flight reader commands — FbConnection is single-threaded.
        await commandLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Explicit TPB instead of IsolationLevel.ReadCommitted. The managed
            // driver maps IsolationLevel.ReadCommitted to a TPB that ends in
            // isc_tpb_WAIT — which makes EmberTern block indefinitely on a lock
            // conflict. We build the TPB from the connection's selected profile
            // (default ReadCommitted = write/read_committed/rec_version/nowait,
            // matching IBExpert's default "Data transaction"). The profile is read
            // at begin time, so changing it only affects the NEXT transaction.
            _activeTransaction = (FbTransaction)await connection
                .BeginTransactionAsync(BuildTransactionOptions(ResolveActiveProfile()))
                .ConfigureAwait(false);
            _statementCount = 0;
            SetState(TransactionState.Active);
        }
        catch (FbException ex)
        {
            SetState(TransactionState.Error);
            throw new TransactionFailedException($"Could not start transaction: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }

        // Best-effort: when EMBERTERN_TX_DIAG is set, append the real server-side
        // TPB parameters (from MON$TRANSACTIONS) to the debug log so we can confirm
        // nowait actually took effect. Zero cost when the env var is unset.
        await LogTransactionParametersIfEnabledAsync().ConfigureAwait(false);
    }

    // The profile to use for the next transaction. Read from the active connection via
    // this lane's selector (DataTransactionProfile or MetadataTransactionProfile);
    // falls back to the safe default when nothing is connected.
    private TransactionProfile ResolveActiveProfile()
        => _profileSelector(_connectionService.ActiveProfile);

    // Maps each IBExpert-style profile to its TPB. Internal + static so a unit
    // test can pin every mapping without a live Firebird. Access-mode note:
    // ReadCommitted and Snapshot are read-write data transactions (the spec listed
    // only their isolation flags); the two Table Stability profiles carry the
    // explicit read/write the user specified.
    internal static FbTransactionOptions BuildTransactionOptions(TransactionProfile profile) => new()
    {
        TransactionBehavior = profile switch
        {
            // isc_tpb_write + read_committed + rec_version + nowait — the safe default.
            TransactionProfile.ReadCommitted =>
                FbTransactionBehavior.Write
                | FbTransactionBehavior.ReadCommitted
                | FbTransactionBehavior.RecVersion
                | FbTransactionBehavior.NoWait,
            // isc_tpb_write + concurrency + nowait — stable snapshot, still writable.
            TransactionProfile.Snapshot =>
                FbTransactionBehavior.Write
                | FbTransactionBehavior.Concurrency
                | FbTransactionBehavior.NoWait,
            // isc_tpb_read + consistency — read-only table stability. No nowait per
            // spec; consistency locks whole tables and CAN block other users.
            TransactionProfile.ReadOnlyTableStability =>
                FbTransactionBehavior.Read
                | FbTransactionBehavior.Consistency,
            // isc_tpb_write + consistency — read-write table stability. CAN block others.
            TransactionProfile.ReadWriteTableStability =>
                FbTransactionBehavior.Write
                | FbTransactionBehavior.Consistency,
            _ =>
                FbTransactionBehavior.Write
                | FbTransactionBehavior.ReadCommitted
                | FbTransactionBehavior.RecVersion
                | FbTransactionBehavior.NoWait,
        },
    };

    private async Task LogTransactionParametersIfEnabledAsync()
    {
        if (Environment.GetEnvironmentVariable("EMBERTERN_TX_DIAG") is null)
        {
            return;
        }

        try
        {
            var diagnostics = new FirebirdDiagnostics(_connectionService, this);
            var summary = await diagnostics.DescribeCurrentTransactionAsync().ConfigureAwait(false);
            FirebirdDiagnostics.AppendDebugLog("TX-BEGIN " + summary);
        }
        catch
        {
            // Diagnostics must never break a transaction begin.
        }
    }

    public async Task CommitAsync()
    {
        if (ShouldDelegate)
        {
            await _fallback!.CommitAsync().ConfigureAwait(false);
            return;
        }

        if (_activeTransaction is null)
        {
            return;
        }

        var commandLock = _connectionService.GetCommandLock(EffectiveRole);
        var tx = _activeTransaction;
        await commandLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await tx.CommitAsync().ConfigureAwait(false);
            _activeTransaction = null;
            _statementCount = 0;
            SetState(TransactionState.Idle);
        }
        catch (FbException ex)
        {
            SetState(TransactionState.Error);
            throw new TransactionFailedException($"Commit failed: {ex.Message}", ex);
        }
        finally
        {
            if (_activeTransaction is null)
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }
            commandLock.Release();
        }
    }

    public async Task RollbackAsync()
    {
        if (ShouldDelegate)
        {
            await _fallback!.RollbackAsync().ConfigureAwait(false);
            return;
        }

        if (_activeTransaction is null)
        {
            SetState(TransactionState.Idle);
            return;
        }

        var commandLock = _connectionService.GetCommandLock(EffectiveRole);
        var tx = _activeTransaction;
        _activeTransaction = null;
        _statementCount = 0;
        await commandLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await tx.RollbackAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort rollback — handle gone-connection gracefully
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
            commandLock.Release();
            SetState(TransactionState.Idle);
        }
    }

    public void NotifyStatementExecuted()
    {
        if (ShouldDelegate)
        {
            _fallback!.NotifyStatementExecuted();
            return;
        }
        if (_activeTransaction is null)
        {
            return;
        }
        _statementCount++;
        TransactionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (!_connectionService.IsConnected && _activeTransaction is not null)
        {
            // Connection is gone — the transaction is implicitly dead. Drop our handle.
            var tx = _activeTransaction;
            _activeTransaction = null;
            _statementCount = 0;
            try { tx.Dispose(); } catch { }
            SetState(TransactionState.Idle);
        }
    }

    private void SetState(TransactionState newState)
    {
        if (_state == newState)
        {
            TransactionStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        _state = newState;
        TransactionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _connectionService.ActiveConnectionChanged -= OnConnectionChanged;
        try { _activeTransaction?.Dispose(); } catch { }
        _activeTransaction = null;
    }
}

public sealed class TransactionFailedException : Exception
{
    public TransactionFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
