using System;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Data Import's <b>own</b> working transaction, on its own attachment (etap I7.5, amending design §4.5).
/// <para>
/// ⭐ <b>Why the module stopped sharing the console's transaction.</b> The deciding argument is not
/// performance and not convenience — it is that a button must do exactly what it says. While the import wrote
/// into THE one user working transaction, pressing <b>Commit</b> in the import also committed whatever the
/// user happened to have uncommitted in the SQL Editor. A module must not be able to persist somebody else's
/// work as a side effect of persisting its own (rule #11 / §0.5). Independence removes the possibility rather
/// than warning about it.
/// </para>
/// <para>
/// <b>The cost, stated because it is real:</b> a <c>SELECT</c> in the SQL Editor will NOT see imported rows
/// until the import is committed — the two transactions are genuinely isolated (measured: A's uncommitted rows
/// are invisible to B). And two transactions can now collide: writing a row the console holds uncommitted
/// fails immediately under NOWAIT (measured: SQLSTATE 40001 in ~28 ms) instead of silently sharing it. Both
/// were accepted deliberately when this decision was ratified.
/// </para>
/// <para>
/// It is the debugger's model, on the debugger's fundament (<see cref="FirebirdSessionConnection"/>) — the one
/// difference is what it means by an unsettled transaction at teardown. A debug run's writes are meant to be
/// discarded (spec §4.4), so the debugger lets them go silently; an import's writes were the whole point, so
/// the surface must ASK before anything tears this down.
/// </para>
/// </summary>
public sealed class ImportSessionConnection : IAsyncDisposable
{
    private readonly FirebirdConnectionService _owner;
    private readonly FirebirdSessionConnection _session;
    private bool _disposed;

    internal ImportSessionConnection(FbConnection connection, FirebirdConnectionService owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _session = new FirebirdSessionConnection(
            connection ?? throw new ArgumentNullException(nameof(connection)),
            BuildImportTransactionOptions());
    }

    /// <summary>The session's dedicated attachment.</summary>
    public FbConnection Connection => _session.Connection;

    /// <summary>The import's own transaction, or <c>null</c> when nothing is open.</summary>
    public FbTransaction? Transaction => _session.Transaction;

    /// <summary>True while the import holds an unsettled transaction — the fact the close guard asks for.</summary>
    public bool IsActive => _session.IsActive;

    /// <summary>Rows written into the current transaction and not yet settled. Zero after every Commit or
    /// Rollback, so "is there anything to lose" is a number, not a guess.</summary>
    public long UncommittedRows { get; private set; }

    internal SemaphoreSlim CommandLock => _session.CommandLock;

    /// <summary>Opens the import transaction if none is open — the auto-begin the writer performs before its
    /// first row, and the re-open <c>Batched</c> needs after each commit. <b>Never</b> an auto-commit.</summary>
    public Task BeginAsync(CancellationToken cancellationToken = default)
        => _session.BeginAsync(cancellationToken);

    /// <summary>Counts rows the server accepted into the open transaction. Called by the writer; reset by a
    /// settle.</summary>
    internal void CountWritten(long rows)
    {
        if (rows > 0) UncommittedRows += rows;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _session.CommitAsync(cancellationToken).ConfigureAwait(false);
        UncommittedRows = 0;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _session.RollbackAsync(cancellationToken).ConfigureAwait(false);
        UncommittedRows = 0;
    }

    /// <summary>Ends the session: rolls back anything unsettled and closes the attachment, then deregisters.
    /// Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _session.DisposeAsync().ConfigureAwait(false);
        UncommittedRows = 0;
        _owner.RemoveSession(this);
    }

    /// <summary>
    /// The import's TPB: write + read committed (rec_version) + <b>NOWAIT</b> — the same shape as the user's
    /// working transaction, and NOWAIT for the same reason it is used everywhere else in EmberTern: a lock met
    /// on somebody else's transaction must become an error at a known row, never a silent hang. Measured: a
    /// same-row collision surfaces in ~28 ms as SQLSTATE 40001.
    /// <para>Pure + internal so a test pins the shape without a live server (gotcha #85 — never a bare
    /// <c>IsolationLevel</c>).</para>
    /// </summary>
    internal static FbTransactionOptions BuildImportTransactionOptions() => new()
    {
        TransactionBehavior = FbTransactionBehavior.Write
                              | FbTransactionBehavior.ReadCommitted
                              | FbTransactionBehavior.RecVersion
                              | FbTransactionBehavior.NoWait,
    };
}
