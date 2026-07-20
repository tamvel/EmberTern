using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;

namespace EmberTern.App.Debugging;

/// <summary>
/// The App-side seam that turns a parsed routine + launch choices into a <b>started</b>
/// <see cref="DebugSession"/> plus its teardown (Stage X / D4). It exists so
/// <see cref="EmberTern.App.ViewModels.DebuggerTabViewModel"/> can be unit-tested against a fake — the
/// production implementation (<see cref="FirebirdDebugSessionLauncher"/>) opens a
/// <see cref="DebugSessionConnection"/> and wires D2's <see cref="FirebirdDebugExecutor"/>, while a test
/// launcher returns a session over a fake <see cref="IDebugExecutor"/> with a no-op teardown (no server).
/// <para>The launcher owns the one place App touches the Firebird debug backend; the VM never sees a
/// driver type. Parsing (source → body → model) stays in the VM (pure Core) so it is testable and the
/// launch panel's parameters + pre-flight are derived without a launcher.</para>
/// </summary>
internal interface IDebugSessionLauncher
{
    /// <summary>Opens a debug session's own attachment + transaction, builds the executor, constructs the
    /// <see cref="DebugSession"/> over <paramref name="spec"/>'s body, and <b>starts it</b> (paused at the
    /// first step point). The returned handle's <see cref="DebugRunHandle.DisposeAsync"/> rolls the debug
    /// transaction back and disposes the attachment (§4.4 — the default contract of a debug run).</summary>
    Task<DebugRunHandle> LaunchAsync(DebugLaunchSpec spec, CancellationToken cancellationToken = default);
}

/// <summary>The inputs a launch needs: the routine's full source (span backing), its parsed body + semantic
/// model (both from the strict whole-routine parse — gotcha #238), the display name, the input-parameter
/// arguments seeding the root frame (§9.3; for a trigger, the synthetic-keyed NEW/OLD context values), the
/// chosen transaction isolation (§4.2), — for a trigger — the <paramref name="Trigger"/> context (§8.1:
/// target table + simulated event/timing + the NEW/OLD column→synthetic mapping), and — for a package member
/// launched as the ROOT (D11 seam C) — the <paramref name="PackageName"/> it belongs to (so its sibling calls
/// resolve and its catalog params are keyed by package; <c>Source</c> is then the member reconstructed as a
/// standalone <c>CREATE PROCEDURE</c>). <c>Trigger</c> is null for a procedure/function; <c>PackageName</c> is
/// null for every standalone routine (D4–D10). The two are mutually exclusive.</summary>
internal sealed record DebugLaunchSpec(
    string Source,
    BlockStatement Body,
    SemanticModel Model,
    string RoutineName,
    IReadOnlyDictionary<string, object?> RootValues,
    DebugIsolation Isolation,
    TriggerContext? Trigger = null,
    string? PackageName = null,
    // The owner's breakpoint / data-breakpoint sets (D12), passed so the session SHARES them from Start — a
    // breakpoint on the first statement is then active before the first step, and the panel edits the live
    // objects. Null keeps the pre-D12 behaviour (the session owns fresh sets). BreakOnException seeds the
    // session's toggle so it is in force from the first resume.
    BreakpointSet? Breakpoints = null,
    DataBreakpointSet? DataBreakpoints = null,
    bool BreakOnException = false);

/// <summary>A live, started debug session and its teardown. Disposing rolls back + closes the session's
/// attachment (best-effort, idempotent). The <see cref="Session"/> is already <see cref="DebugSession.Start"/>ed
/// (paused at entry) when the handle is returned.</summary>
internal sealed class DebugRunHandle : IAsyncDisposable
{
    private readonly Func<ValueTask> _teardown;
    private bool _disposed;

    public DebugRunHandle(DebugSession session, Func<ValueTask> teardown)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _teardown = teardown ?? throw new ArgumentNullException(nameof(teardown));
    }

    public DebugSession Session { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _teardown().ConfigureAwait(false);
    }
}
