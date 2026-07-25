using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.Firebird;

/// <summary>
/// The Cursor Bridge (Stage X / D6, spec §7) — the <b>live</b> half: a real DSQL cursor (an
/// <see cref="FbDataReader"/>) held open on the debug session connection, in the debug transaction, and
/// fetched one row per <see cref="FetchNext"/> while the user steps the loop body. The query text + bind
/// parameters come from the pure <see cref="CursorBridge"/>; this wraps the resulting reader as an
/// <see cref="IDebugCursor"/> and maps each fetched column onto the loop's <c>INTO</c> targets positionally.
/// <para>
/// <b>Locking (gotcha #236, spec §7).</b> Interleaving is fine; concurrency is not. The reader is held open
/// across steps, but the session's command lock is taken <b>per wire operation</b> (each fetch, the close) —
/// <em>never</em> for the cursor's lifetime, which would deadlock every harness step inside the loop. The lock
/// is captured once per acquire/release pair (#98/#120). The session is single-threaded by construction (one
/// debug session drives it), so this is safe interleaving, not concurrency.
/// </para>
/// </summary>
internal sealed class CursorHandle : IDebugCursor
{
    private readonly DebugSessionConnection _session;
    private readonly FbCommand _command;
    private readonly FbDataReader _reader;
    private readonly IReadOnlyList<string> _intoTargets;
    private bool _closed;

    internal CursorHandle(
        DebugSessionConnection session, FbCommand command, FbDataReader reader, IReadOnlyList<string> intoTargets)
    {
        _session = session;
        _command = command;
        _reader = reader;
        _intoTargets = intoTargets;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?>? FetchNext()
        => _closed ? null : Await(FetchNextAsync());

    private async Task<IReadOnlyDictionary<string, object?>?> FetchNextAsync()
    {
        var gate = _session.CommandLock; // capture once (#98/#120)
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!await _reader.ReadAsync().ConfigureAwait(false)) return null;

            // A row was fetched. Map result columns onto the INTO targets positionally; an empty INTO list
            // (a FETCH-driven AS CURSOR loop) still returns a non-null empty row so the loop iterates.
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            int n = Math.Min(_intoTargets.Count, _reader.FieldCount);
            for (int i = 0; i < n; i++)
            {
                // Driver-native value kept as-is (no conversion — §F, FB4+ type round-trip).
                row[_intoTargets[i]] = _reader.IsDBNull(i) ? null : _reader.GetValue(i);
            }
            return row;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        Await(CloseAsync());
    }

    private async Task CloseAsync()
    {
        var gate = _session.CommandLock; // capture once (#98/#120)
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _reader.DisposeAsync().ConfigureAwait(false);
            await _command.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // Sync-over-async bridge (the IDebugExecutor/IDebugCursor contract is synchronous; the session is async).
    // Deadlock-safe: ConfigureAwait(false) throughout + stepping runs off the UI thread (mirrors
    // FirebirdDebugExecutor.Await).
    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static void Await(Task task) => task.GetAwaiter().GetResult();
}
