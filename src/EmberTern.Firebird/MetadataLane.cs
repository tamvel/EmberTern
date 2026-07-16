using System.Threading;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// The READ-ONLY metadata attachment (#2): the connection + command lock that every catalog
/// reader (sidebar, DDL preview, completion, security, statistics) runs on.
///
/// <para>It owns NO transaction. Metadata reads use an implicit per-command transaction, so they
/// are never entangled with the user's working transaction and can never block it (or be blocked
/// by it). This is deliberate and is the whole reason the type exists: metadata reading is a
/// read-only concern, and it used to be expressed as a second <see cref="TransactionService"/> —
/// an object that, once the SQL Editor stopped routing DDL onto this lane, could never actually
/// hold a transaction. That zombie abstraction is gone; a lane is now just "which attachment,
/// which lock".</para>
///
/// <para><b>Degraded mode.</b> If the second attachment failed to open,
/// <see cref="FirebirdConnectionService.RequireOpenConnection(ConnectionRole)"/> transparently
/// hands back the DATA connection. That connection may already carry the user's working
/// transaction, and one <c>FbConnection</c> allows only one transaction at a time (gotcha #89) —
/// so in that case, and ONLY in that case, a metadata command must join it. See
/// <see cref="TransactionForCommand"/>.</para>
/// </summary>
public sealed class MetadataLane
{
    private readonly FirebirdConnectionService _connectionService;
    // Only consulted in degraded mode (see TransactionForCommand). Null when the caller has no
    // user transaction at all (tests, standalone readers) — then there is nothing to join either.
    private readonly TransactionService? _userTransaction;

    public MetadataLane(FirebirdConnectionService connectionService, TransactionService? userTransaction = null)
    {
        _connectionService = connectionService;
        _userTransaction = userTransaction;
    }

    /// <summary>The metadata attachment — or the data attachment in degraded mode.</summary>
    public FbConnection RequireOpenConnection()
        => _connectionService.RequireOpenConnection(ConnectionRole.Metadata);

    /// <summary>This lane's command lock (the data lock in degraded mode, so commands on the
    /// shared connection stay serialized). Capture it ONCE per acquire/release pair — never
    /// re-evaluate it at Release (gotchas #98 / #120).</summary>
    public SemaphoreSlim CommandLock
        => _connectionService.GetCommandLock(ConnectionRole.Metadata);

    /// <summary>
    /// The transaction a metadata command must run under: <c>null</c> normally — an implicit
    /// per-command transaction on our own attachment, which is exactly what a read wants.
    /// In degraded mode we are sharing the DATA connection, so we must join whatever working
    /// transaction is open there rather than try to start a second one.
    /// </summary>
    public FbTransaction? TransactionForCommand
        => _connectionService.MetadataIsIndependent ? null : _userTransaction?.ActiveTransaction;
}
