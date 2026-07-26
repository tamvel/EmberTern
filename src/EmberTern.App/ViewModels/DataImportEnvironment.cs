using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Everything the Data Import surface needs from the world outside it, as delegates.
/// <para>
/// ⭐ <b>Delegates, not services</b> — the reason is rule #1: not one Firebird or Avalonia type may reach a
/// ViewModel, and the surface must stay testable without a database. Every entry here is a question the VM asks
/// and someone else answers.
/// </para>
/// <para>
/// ⭐ <b>One object rather than a growing parameter list.</b> Etap I7 turns the surface from something that reads
/// a file into something that writes to a database, which is six more collaborators. Eleven positional delegates
/// at a call site is a place where two of them get swapped and nothing complains; a named bundle is not.
/// </para>
/// <para>
/// The three constructor arguments are the ones the surface cannot function without and are read <b>live</b>,
/// never snapshotted — the readiness strip must describe the connection and the transaction as they are now, not
/// as they were when the tab opened. Everything else is optional: a surface with no writer simply cannot run,
/// and says so through readiness rather than through a null reference.
/// </para>
/// </summary>
public sealed class DataImportEnvironment
{
    public DataImportEnvironment(
        Func<bool> isConnected,
        Func<bool> hasOpenUserTransaction,
        Func<string> connectionName)
    {
        IsConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
        HasOpenUserTransaction = hasOpenUserTransaction ?? throw new ArgumentNullException(nameof(hasOpenUserTransaction));
        ConnectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
    }

    public Func<bool> IsConnected { get; }

    public Func<bool> HasOpenUserTransaction { get; }

    public Func<string> ConnectionName { get; }

    /// <summary>
    /// The CONNECTION charset name. ⭐ Not a detail: I0 measured that a character the connection charset cannot
    /// represent is written as <c>?</c> with no error at all, <em>even into a UTF8 column</em> — the connection
    /// decides, not the column. This is what <c>ImportCharsetGuard.Strict</c> turns into the throwing encoding
    /// the pipeline validates against (design R1).
    /// </summary>
    public Func<string>? ConnectionCharset { get; init; }

    // ── Metadata lane (read-only, implicit per-command transactions) ────────────────────────────────────

    public Func<CancellationToken, Task<IReadOnlyList<string>>>? ListTablesAsync { get; init; }

    public Func<string, CancellationToken, Task<ImportTarget?>>? ReadTargetAsync { get; init; }

    // ── Data lane (THE user's working transaction) ──────────────────────────────────────────────────────

    /// <summary>Builds the writer for one run. The configuration decides which one — the real writer, wrapped in
    /// the commit-every-N decorator for <see cref="ImportTransactionMode.Batched"/>. The VM never names a writer
    /// type, which is exactly why "Validate" can substitute its own.</summary>
    public Func<ImportConfiguration, IImportWriter>? CreateWriter { get; init; }

    /// <summary>Rows currently in the target, read once when a run is about to start — the number that turns
    /// "empty the table first" into a sentence the user can weigh.</summary>
    public Func<string, CancellationToken, Task<long>>? CountTargetRowsAsync { get; init; }

    /// <summary><c>DELETE FROM</c> in the SAME working transaction, so a Rollback takes it with the import.</summary>
    public Func<string, CancellationToken, Task<long>>? EmptyTargetAsync { get; init; }

    public Func<Task>? CommitAsync { get; init; }

    public Func<Task>? RollbackAsync { get; init; }

    // ── Persistence ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The configuration last used against this connection (§4.8.4). <c>null</c> when there is none —
    /// and a missing store is simply "no last configuration", never an error.</summary>
    public Func<ImportConfiguration?>? LoadLastUsed { get; init; }

    /// <summary>Records the configuration a run was started with. Called by the coordinator only (§4.8.6 — one
    /// owner of persistence), never by a section VM.</summary>
    public Action<ImportConfiguration>? SaveLastUsed { get; init; }

    /// <summary>An environment that knows nothing — the shape a test or a disconnected surface gets.</summary>
    public static DataImportEnvironment Disconnected { get; } =
        new(() => false, () => false, () => string.Empty);
}
