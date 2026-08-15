using System;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Licensing;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Firebird;

namespace EmberTern.App.Debugging;

/// <summary>
/// The production <see cref="IDebugSessionLauncher"/> (Stage X / D4): the one App place that touches the
/// Firebird debug backend. It opens a per-session <see cref="DebugSessionConnection"/> via
/// <see cref="FirebirdConnectionService.CreateDebugSessionAsync"/> (its own attachment + transaction,
/// spec §4.1), builds D2's <see cref="FirebirdDebugExecutor"/> (harness + read/write sets + savepoints),
/// constructs the pure-Core <see cref="DebugSession"/> over the routine body and starts it. The teardown
/// disposes the session connection (rollback + close — §4.4). If any step of the wiring throws, the
/// half-opened attachment is disposed so nothing leaks.
/// </summary>
internal sealed class FirebirdDebugSessionLauncher : IDebugSessionLauncher
{
    private readonly LicensedConnections _connections;

    /// <param name="connections">
    /// ⭐ The licensing seam, not the raw service: a debug session opens its OWN attachment, which is the same
    /// act as Connect and is gated by the same predicate (design §7). In practice this refusal is unreachable —
    /// <c>CreateDebugSessionAsync</c> hard-requires <c>IsConnected</c>, and a licence that forbids connecting
    /// never lets the user get connected — so it is defence in depth rather than a path the user meets.
    /// </param>
    public FirebirdDebugSessionLauncher(LicensedConnections connections)
        => _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public async Task<DebugRunHandle> LaunchAsync(
        DebugLaunchSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var connection = await _connections
            .OpenDebugSessionAsync(spec.Isolation, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            // The source-blob decode fallback (UTF-8-first, then the connection charset) for reconstructing a
            // stepped-into callee's source (D8) — the same resolution the metadata readers use.
            var fallback = EmberTern.Core.Connections.CharsetCatalog.Resolve(_connections.ActiveProfile?.Charset);
            var executor = await FirebirdDebugExecutor
                .CreateAsync(connection, spec.RoutineName, spec.Source, spec.Body, spec.Model, fallback, spec.Trigger, cancellationToken, spec.PackageName, spec.IsFunction)
                .ConfigureAwait(false);

            // D-function: the executor resolved the RETURNS base type ONCE (isFunctionRoot); passing it here
            // makes the root a function frame (RETURN via the Expression Harness). Null for every non-function
            // root, so the session is byte-identical to before.
            var session = new DebugSession(
                spec.Body, executor, spec.RoutineName, spec.RootValues, spec.Source, spec.Model,
                spec.Breakpoints, spec.DataBreakpoints, executor.RootReturnType);
            session.BreakOnException = spec.BreakOnException; // in force from the first resume
            session.Start(); // pushes the root frame (SAVEPOINT) + pauses at the first step point (breakpoint-aware)

            return new DebugRunHandle(session, async () => await connection.DisposeAsync().ConfigureAwait(false));
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
