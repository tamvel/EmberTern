using System;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Firebird;

namespace EmberTern.App.Licensing;

/// <summary>
/// ⭐⭐ <b>The ONE place EmberTern opens a new database attachment.</b>
///
/// <para>⚠ <b>The domain is "opens a new attachment", not "the button the user pressed."</b> Connect, Test
/// connection, a debug session and an import session are the same act, so they are gated by the same
/// predicate. Ratified with the user 2026-08-15: ⛔ <b>there is deliberately no exception for Test
/// connection</b> — an exception there would be a fully working database round-trip on an expired licence,
/// which is most of what a developer needs a connection for in the first place.</para>
///
/// <para>⭐ <b>Why a seam rather than four checks.</b> A check written at each call site is a check the fifth
/// call site forgets, silently, with a green build — the exact shape <c>FirebirdCommandGuard</c> exists for
/// on the charset side. So the four openers are reachable from one file, and
/// <c>LicensingConnectionSeamTests</c> fails the build if anything else calls them. ⛔ If a new call site
/// genuinely cannot use this seam, that is a design conversation, not a reason to widen the guard.</para>
///
/// <para>⚠ <b>A null licence permits everything</b>, and that is not a hole: it is the shape a designer, a
/// unit test and a <c>Debug</c> run all take, and in a <c>Release</c> build <see cref="App"/> always supplies
/// one. The gate's real off-switch is <see cref="LicensingPolicy.GateEnabled"/>, which is a compile-time
/// <c>const</c> and lives in exactly one file.</para>
/// </summary>
internal sealed class LicensedConnections
{
    private readonly FirebirdConnectionService _service;
    private readonly LicenseService? _license;

    internal LicensedConnections(FirebirdConnectionService service, LicenseService? license)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _license = license;
    }

    /// <summary>⭐ Whether a new attachment may be opened right now. The banner and the seam ask the same thing.</summary>
    internal bool Allows => _license is null || _license.AllowsConnecting;

    /// <summary>
    /// The profile currently attached, straight from the service.
    ///
    /// <para>⚠ A pass-through, and deliberately so: reading which profile is attached is not opening one, so
    /// it needs no gate. It is here only because a caller that opens through this seam should not also have
    /// to be handed the raw service just to read a charset.</para>
    /// </summary>
    internal ConnectionProfile? ActiveProfile => _service.ActiveProfile;

    internal Task OpenAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        Guard();
        return _service.ConnectAsync(profile, cancellationToken);
    }

    internal Task TestAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        Guard();
        return _service.TestConnectionAsync(profile, cancellationToken);
    }

    internal Task<DebugSessionConnection> OpenDebugSessionAsync(
        DebugIsolation isolation, CancellationToken cancellationToken = default)
    {
        Guard();
        return _service.CreateDebugSessionAsync(isolation, cancellationToken);
    }

    internal Task<ImportSessionConnection> OpenImportSessionAsync(CancellationToken cancellationToken = default)
    {
        Guard();
        return _service.CreateImportSessionAsync(cancellationToken);
    }

    /// <summary>
    /// ⛔ Refuses before the driver is touched.
    ///
    /// <para>⚠ It throws rather than returning false because every one of the four openers returns something
    /// the caller then uses; a false would have to be encoded into four different return shapes, and the one
    /// a caller ignored would open the attachment anyway.</para>
    /// </summary>
    private void Guard()
    {
        if (_license is { AllowsConnecting: false })
        {
            throw new LicenseBlockedException(_license.Verdict);
        }
    }
}
