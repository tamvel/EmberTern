using System;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>The isolation a debug session runs under — user-selectable at launch (spec §4.2/§12.4). A
/// routine normally called under SNAPSHOT sees different data under READ COMMITTED, so the choice is
/// surfaced, never silently defaulted.</summary>
public enum DebugIsolation
{
    /// <summary>READ COMMITTED, REC_VERSION, NOWAIT (the spec §4.2 default).</summary>
    ReadCommitted,

    /// <summary>SNAPSHOT (concurrency), NOWAIT — a stable view for the whole session.</summary>
    Snapshot,
}

/// <summary>
/// A single debug session's own attachment + transaction (Stage X / D2, spec §4.1). Decision 5: a debug
/// session is <b>not a lane</b> — lanes (Data/Metadata/Ddl) are per-profile singletons, but a session needs
/// its <b>own</b> transaction, and two debug tabs are two sessions are two transactions, impossible on one
/// lane. So a session owns a dedicated <see cref="FbConnection"/> + one <see cref="FbTransaction"/>, created
/// on session start and disposed on session end; its lifetime is bound to the <i>session</i>, not the
/// profile. It never touches the Data lane (a debug rollback there would destroy the user's uncommitted
/// work — rule #11).
/// <para>
/// The transaction's TPB is explicit (never a bare <c>IsolationLevel</c>, gotcha #85): READ COMMITTED /
/// SNAPSHOT + NOWAIT (§4.2 — <c>NOWAIT</c> turns a lock met on the user's Data transaction into a step-level
/// error at a known line, not a silent hang). It exposes the <b>frame savepoints</b> the interpreter's
/// call-atomicity model needs (spec §4.5): <see cref="SetSavepointAsync"/> on frame entry,
/// <see cref="ReleaseSavepointAsync"/> on normal exit, <see cref="RollbackToSavepointAsync"/> on an
/// unhandled exit. Every wire operation is serialized on the session's own command lock, captured once per
/// acquire/release (gotchas #31/#98/#120/#236 — a session connection never flips lanes, so it is a single
/// lock; interleaving is fine, concurrency is not).
/// </para>
/// <para>
/// D2 seam (a): the connection + TPB + savepoints only. The <c>EXECUTE BLOCK</c> harness that runs steps
/// (the <c>FirebirdDebugExecutor : IDebugExecutor</c>) is seam (c) and builds on this — it captures
/// <see cref="CommandLock"/> once per wire op and binds commands to <see cref="Transaction"/>.
/// </para>
/// </summary>
public sealed class DebugSessionConnection : IAsyncDisposable
{
    private readonly FirebirdConnectionService _owner;

    // ⭐ I7.5: the attachment, the transaction, the command lock and the teardown all moved into the shared
    // FirebirdSessionConnection, which Data Import now uses too. This type HOLDS one rather than being one, so
    // every member below stays byte-identical to what the debugger has always exposed — its tests and
    // DebuggerFidelityProbe remain an untouched regression proof of a closed subsystem.
    private readonly FirebirdSessionConnection _session;
    private bool _disposed;

    internal DebugSessionConnection(FbConnection connection, DebugIsolation isolation, FirebirdConnectionService owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _session = new FirebirdSessionConnection(
            connection ?? throw new ArgumentNullException(nameof(connection)),
            BuildDebugTransactionOptions(isolation));
        Isolation = isolation;
    }

    /// <summary>The isolation this session runs under (chosen at launch).</summary>
    public DebugIsolation Isolation { get; }

    /// <summary>The session's dedicated attachment.</summary>
    public FbConnection Connection => _session.Connection;

    /// <summary>The session's own transaction (null before <see cref="BeginAsync"/> / after settle).</summary>
    public FbTransaction? Transaction => _session.Transaction;

    /// <summary>True while the session's transaction is open.</summary>
    public bool IsActive => _session.IsActive;

    // The session connection's own command lock — a reader/executor (seam c) captures it ONCE per wire
    // operation and binds its command to Transaction. A session connection never flips lanes, so there is
    // exactly one lock (no lane-resolving accessor hazard, #98/#120).
    internal SemaphoreSlim CommandLock => _session.CommandLock;

    // Begins the session transaction with the explicit debug TPB (§4.2). Called by the factory
    // (FirebirdConnectionService.CreateDebugSessionAsync) right after the attachment opens.
    internal Task BeginAsync(CancellationToken cancellationToken = default)
        => _session.BeginAsync(cancellationToken);

    /// <summary>Sets a SAVEPOINT on entry to a simulated frame (spec §4.5). Named by the frame's
    /// <c>SavepointName</c> (<c>ET_DBG_FRAME_{id}</c>).</summary>
    public Task SetSavepointAsync(string name, CancellationToken cancellationToken = default)
        => _session.ExecuteNonQueryLockedAsync(SavepointStatement(SavepointOp.Set, name), cancellationToken);

    /// <summary>Releases a frame's savepoint on its NORMAL exit (spec §4.5).</summary>
    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        => _session.ExecuteNonQueryLockedAsync(SavepointStatement(SavepointOp.Release, name), cancellationToken);

    /// <summary>Rolls the transaction back to a frame's savepoint on its UNHANDLED exit (spec §4.5) — the
    /// simulated frame's side effects are undone atomically, as a real call's would be.</summary>
    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        => _session.ExecuteNonQueryLockedAsync(SavepointStatement(SavepointOp.RollbackTo, name), cancellationToken);

    /// <summary>Commits the debug transaction — the rare, explicit <c>Commit debug transaction</c> case
    /// (spec §4.4); the default at session end is <see cref="RollbackAsync"/>.</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default)
        => _session.CommitAsync(cancellationToken);

    /// <summary>Rolls the debug transaction back — the default contract of a debug run (spec §4.4).</summary>
    public Task RollbackAsync(CancellationToken cancellationToken = default)
        => _session.RollbackAsync(cancellationToken);

    /// <summary>Ends the session: rolls back any open transaction (§4.4 default) and disposes the
    /// attachment, then deregisters from the connection service. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _session.DisposeAsync().ConfigureAwait(false);
        _owner.RemoveSession(this);
    }

    // ── Pure helpers (unit-testable without a live server) ────────────────────────────────────────

    internal enum SavepointOp
    {
        Set,
        Release,
        RollbackTo,
    }

    // The explicit debug TPB (§4.2): write + (read_committed rec_version | concurrency) + NOWAIT. NOWAIT is
    // deliberate — the debug transaction WILL meet locks held by the user's Data transaction, and a
    // step-level error at a known line beats a silent hang. Pure + internal so a test pins both shapes
    // without a live Firebird (mirrors FirebirdDdlExecutor.BuildDdlTransactionOptions). Never a bare
    // IsolationLevel (gotcha #85).
    internal static FbTransactionOptions BuildDebugTransactionOptions(DebugIsolation isolation) => new()
    {
        TransactionBehavior = isolation switch
        {
            DebugIsolation.Snapshot =>
                FbTransactionBehavior.Write
                | FbTransactionBehavior.Concurrency
                | FbTransactionBehavior.NoWait,
            _ =>
                FbTransactionBehavior.Write
                | FbTransactionBehavior.ReadCommitted
                | FbTransactionBehavior.RecVersion
                | FbTransactionBehavior.NoWait,
        },
    };

    // Builds the savepoint statement text. The name is app-generated (ET_DBG_FRAME_{id}); it is validated as
    // a bare SQL identifier anyway so no path can ever inject through it. Pure + internal so a test pins the
    // exact SQL (SAVEPOINT / RELEASE SAVEPOINT / ROLLBACK TO SAVEPOINT) without a live server.
    internal static string SavepointStatement(SavepointOp op, string name)
    {
        if (!IsValidSavepointName(name))
        {
            throw new ArgumentException($"Invalid savepoint name: '{name}'.", nameof(name));
        }
        return op switch
        {
            SavepointOp.Set => $"SAVEPOINT {name}",
            SavepointOp.Release => $"RELEASE SAVEPOINT {name}",
            SavepointOp.RollbackTo => $"ROLLBACK TO SAVEPOINT {name}",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }

    // A bare Firebird identifier: a letter/underscore then letters/digits/underscores. Frame savepoint names
    // (ET_DBG_FRAME_0, …) always satisfy this; anything else is rejected rather than concatenated into SQL.
    internal static bool IsValidSavepointName(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }
        return true;
    }
}
