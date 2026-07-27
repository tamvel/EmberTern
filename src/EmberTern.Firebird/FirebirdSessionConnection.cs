using System;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// One module's <b>own attachment and own transaction</b> — the shared fundament under
/// <see cref="DebugSessionConnection"/> and <see cref="ImportSessionConnection"/>.
/// <para>
/// ⭐ <b>Why this exists at all.</b> A lane (Data/Metadata/Ddl) is a per-profile singleton, so it can carry
/// exactly one transaction; a module that needs its own — the debugger, and since I7.5 Data Import — cannot
/// live on one. Measured on FB5 (2026-07-26): the driver refuses a second transaction on one
/// <see cref="FbConnection"/> (<i>"Parallel transactions are not supported"</i>), while two attachments hold
/// two genuinely independent transactions, each committing or rolling back without touching the other. So
/// "another independent transaction" means "another attachment", and this is that attachment.
/// </para>
/// <para>
/// ⭐ <b>Composition, deliberately.</b> This was extracted OUT of <see cref="DebugSessionConnection"/> and the
/// debugger now holds one instead of inheriting from it, so the debugger's public surface is byte-identical
/// and its tests plus <c>DebuggerFidelityProbe</c> remain an untouched regression proof. The debugger is a
/// closed, user-QA-confirmed subsystem; a shared fundament is worth having, changing that subsystem's type
/// shape to get it is not.
/// </para>
/// <para>
/// <b>What lives here:</b> the attachment, one transaction with an <b>explicit TPB</b> (never a bare
/// <c>IsolationLevel</c> — gotcha #85), the per-session command lock captured once per wire operation
/// (#31/#98/#120/#236 — a session never flips lanes, so there is exactly one lock), and Begin/Commit/Rollback
/// with an idempotent teardown.
/// <b>What does NOT:</b> anything a particular module means by it. Frame savepoints are the debugger's;
/// choosing the TPB is the caller's.
/// </para>
/// </summary>
public sealed class FirebirdSessionConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly FbTransactionOptions _options;
    private FbConnection? _connection;
    private FbTransaction? _transaction;
    private bool _disposed;

    internal FirebirdSessionConnection(FbConnection connection, FbTransactionOptions options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The session's dedicated attachment.</summary>
    public FbConnection Connection =>
        _connection ?? throw new ObjectDisposedException(nameof(FirebirdSessionConnection));

    /// <summary>The session's own transaction (null before <see cref="BeginAsync"/> / after it settles).</summary>
    public FbTransaction? Transaction => _transaction;

    /// <summary>True while the session's transaction is open.</summary>
    public bool IsActive => _transaction is not null;

    /// <summary>Serializes wire operations on this session. A caller captures it ONCE per acquire/release —
    /// re-evaluating an accessor at Release is how a semaphore leaks (#98/#120).</summary>
    internal SemaphoreSlim CommandLock => _commandLock;

    /// <summary>
    /// Opens the session transaction with the TPB it was created for. Idempotent while one is open, and
    /// <b>re-openable after a settle</b> — which is what <c>Batched</c> import needs: commit every N rows, then
    /// carry on in a fresh transaction.
    /// </summary>
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null) return;

        var connection = Connection;
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _transaction = (FbTransaction)await connection
                .BeginTransactionAsync(_options, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>Commits the session transaction. A no-op when none is open.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;

        var tx = _transaction;
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            _transaction = null;
        }
        finally
        {
            if (_transaction is null) await tx.DisposeAsync().ConfigureAwait(false);
            _commandLock.Release();
        }
    }

    /// <summary>Rolls the session transaction back. Best-effort: a connection that is already gone is not an
    /// error at teardown time.</summary>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;

        var tx = _transaction;
        _transaction = null;
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort rollback — a gone connection is handled gracefully
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
            _commandLock.Release();
        }
    }

    /// <summary>Runs one non-query on this session's connection + transaction, serialized on its command lock
    /// (captured once, #98/#120).</summary>
    internal async Task ExecuteNonQueryLockedAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = Connection;
        var tx = _transaction ?? throw new InvalidOperationException("The session transaction is not open.");
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.Transaction = tx;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>Rolls back anything still open and closes the attachment. Idempotent.
    /// <para>
    /// ⚠ Rollback is the teardown default because a session that is being torn down has, by definition, not
    /// been settled by the user. A module for which losing that work would be a surprise must ASK BEFORE it
    /// gets here — which is what the pending-work registry is for.
    /// </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await RollbackAsync().ConfigureAwait(false);

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            try { await connection.CloseAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _commandLock.Dispose();
    }
}
