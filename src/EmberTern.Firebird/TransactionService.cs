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

/// <summary>
/// THE user's working transaction — one per connection, on the DATA attachment.
///
/// <para>Everything the user does by hand runs in it: SQL Editor F5 (queries AND DDL — the editor
/// is a classic SQL console, one attachment, one transaction), table-data edits, Execute Procedure,
/// and the Script Executor. It auto-<i>begins</i>, never auto-<i>commits</i> (architecture rule #3),
/// and is settled only by the single Commit / Rollback the user presses.</para>
///
/// <para>There is exactly one of these. It used to be parameterized by
/// <see cref="ConnectionRole"/> with a second, "metadata" instance and a degraded-mode fallback
/// chain — machinery that existed solely because the SQL Editor silently routed DDL onto the
/// metadata attachment and began a hidden second transaction there. That routing is gone, so the
/// role, the profile selector, the fallback delegation and the whole dual-lane commit model are
/// gone with it. Metadata reading is now a read-only concern: see <see cref="MetadataLane"/>.
/// Autonomous DDL (object-editor Compile) has its own attachment and never touches this class:
/// see <see cref="FirebirdDdlExecutor"/>.</para>
/// </summary>
public sealed class TransactionService : IDisposable
{
    private readonly FirebirdConnectionService _connectionService;
    private FbTransaction? _activeTransaction;
    private TransactionState _state = TransactionState.Idle;
    private int _statementCount;

    public TransactionService(FirebirdConnectionService connectionService)
    {
        _connectionService = connectionService;
        _connectionService.ActiveConnectionChanged += OnConnectionChanged;
    }

    public TransactionState State => _state;
    public bool IsActive => State == TransactionState.Active;
    public bool IsIdle => State == TransactionState.Idle;
    public bool IsError => State == TransactionState.Error;
    public int StatementCount => _statementCount;
    public bool HasExecutedStatements => StatementCount > 0;
    public FbTransaction? ActiveTransaction => _activeTransaction;

    // The data attachment + its command lock. Readers/executors that hold this service use these
    // so connection, lock and transaction always come from the same lane.
    public FbConnection RequireOpenConnection() => _connectionService.RequireOpenConnection(ConnectionRole.Data);
    internal SemaphoreSlim CommandLock => _connectionService.GetCommandLock(ConnectionRole.Data);

    public event EventHandler? TransactionStateChanged;

    /// <summary>
    /// Begins the working transaction. <paramref name="options"/> is normally null — the safe
    /// NOWAIT read-committed default (see <see cref="ResolveActiveProfile"/>). The ONE caller that
    /// passes options is <see cref="FirebirdScriptExecutor"/>, for an all-DDL script under
    /// auto-commit; see the note on <see cref="ResolveActiveProfile"/> for why that is safe.
    /// </summary>
    public async Task BeginTransactionAsync(FbTransactionOptions? options = null)
    {
        if (_activeTransaction is not null)
        {
            return;
        }

        var connection = _connectionService.RequireOpenConnection(ConnectionRole.Data);
        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Data);
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
                .BeginTransactionAsync(options ?? BuildTransactionOptions(ResolveActiveProfile()))
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

    // The DEFAULT for the user transaction is ALWAYS the safe NOWAIT read-committed profile. The
    // per-connection TPB profile is not user-configurable, so a stored legacy profile — e.g.
    // table-stability, which locks whole tables — must never silently make the SQL console WAIT or
    // block other sessions. The TransactionProfile enum + the persisted fields are vestigial and
    // slated for removal in their own pass; this method is where that decision is enforced.
    //
    // ONE deliberate exception, added with the Script Executor Dev Mode integration: a caller may
    // pass explicit options to BeginTransactionAsync. Only FirebirdScriptExecutor does, and only for
    // an ALL-DDL script under auto-commit — two conditions that together preserve everything this
    // rule protects:
    //   • all-DDL  ⇒ there is no DML in the transaction, so no data operation can be made to WAIT;
    //   • auto-commit ⇒ the transaction is settled inside the run, so a WAIT policy can never be
    //     left open for the SQL Editor's next F5 to inherit (BeginTransactionAsync early-returns on
    //     an active transaction, so a left-open script tx WOULD otherwise become the console's).
    // The console itself never passes options and is therefore unchanged: still always NOWAIT.
    private static TransactionProfile ResolveActiveProfile() => TransactionProfile.ReadCommitted;

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
        if (_activeTransaction is null)
        {
            return;
        }

        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Data);
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
        if (_activeTransaction is null)
        {
            SetState(TransactionState.Idle);
            return;
        }

        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Data);
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
