using System;
using System.Data;
using System.Threading.Tasks;
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
    private FbTransaction? _activeTransaction;
    private TransactionState _state = TransactionState.Idle;
    private int _statementCount;

    public TransactionService(FirebirdConnectionService connectionService)
    {
        _connectionService = connectionService;
        _connectionService.ActiveConnectionChanged += OnConnectionChanged;
    }

    public TransactionState State => _state;
    public bool IsActive => _state == TransactionState.Active;
    public bool IsIdle => _state == TransactionState.Idle;
    public bool IsError => _state == TransactionState.Error;
    public int StatementCount => _statementCount;
    public bool HasExecutedStatements => _statementCount > 0;
    public FbTransaction? ActiveTransaction => _activeTransaction;

    public event EventHandler? TransactionStateChanged;

    public async Task BeginTransactionAsync()
    {
        if (_activeTransaction is not null)
        {
            return;
        }

        var connection = _connectionService.RequireOpenConnection();
        // Serialize against in-flight reader commands — FbConnection is single-threaded.
        await _connectionService.CommandLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _activeTransaction = (FbTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted)
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
            _connectionService.CommandLock.Release();
        }
    }

    public async Task CommitAsync()
    {
        if (_activeTransaction is null)
        {
            return;
        }

        var tx = _activeTransaction;
        await _connectionService.CommandLock.WaitAsync().ConfigureAwait(false);
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
            _connectionService.CommandLock.Release();
        }
    }

    public async Task RollbackAsync()
    {
        if (_activeTransaction is null)
        {
            SetState(TransactionState.Idle);
            return;
        }

        var tx = _activeTransaction;
        _activeTransaction = null;
        _statementCount = 0;
        await _connectionService.CommandLock.WaitAsync().ConfigureAwait(false);
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
            _connectionService.CommandLock.Release();
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
