using System;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly FirebirdConnectionService _service;

    public FirebirdDebugSessionLauncher(FirebirdConnectionService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    public async Task<DebugRunHandle> LaunchAsync(
        DebugLaunchSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var connection = await _service
            .CreateDebugSessionAsync(spec.Isolation, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            // The source-blob decode fallback (UTF-8-first, then the connection charset) for reconstructing a
            // stepped-into callee's source (D8) — the same resolution the metadata readers use.
            var fallback = EmberTern.Core.Connections.CharsetCatalog.Resolve(_service.ActiveProfile?.Charset);
            var executor = await FirebirdDebugExecutor
                .CreateAsync(connection, spec.RoutineName, spec.Source, spec.Body, spec.Model, fallback, spec.Trigger, cancellationToken, spec.PackageName)
                .ConfigureAwait(false);

            var session = new DebugSession(
                spec.Body, executor, spec.RoutineName, spec.RootValues, spec.Source, spec.Model,
                spec.Breakpoints, spec.DataBreakpoints);
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
