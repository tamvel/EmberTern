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
    public DataImportEnvironment(Func<bool> isConnected, Func<string> connectionName)
    {
        IsConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
        ConnectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
    }

    public Func<bool> IsConnected { get; }

    public Func<string> ConnectionName { get; }

    // ⚠ There is deliberately no "has the console got a transaction open" here any more. Since I7.5 the module
    // owns its own transaction, so the SQL Editor's state is none of its business — asking would invite
    // somebody to act on it again.

    /// <summary>
    /// The CONNECTION charset name. ⭐ Not a detail: I0 measured that a character the connection charset cannot
    /// represent is written as <c>?</c> with no error at all, <em>even into a UTF8 column</em> — the connection
    /// decides, not the column. This is what <c>ImportCharsetGuard.Strict</c> turns into the throwing encoding
    /// the pipeline validates against (design R1).
    /// </summary>
    public Func<string>? ConnectionCharset { get; init; }

    /// <summary>
    /// The clipboard's current text. App owns Avalonia's clipboard, Core receives a plain string — which is
    /// exactly why the clipboard is not a second parser (§1.5).
    /// <para>
    /// ⭐ It lives <b>here</b>, in the environment, rather than as an event wired after construction like the file
    /// picker, and the difference is not cosmetic: the clipboard is a source the <b>recalculation chain</b> reads,
    /// so it has to be answerable from the constructor's first chain run — a surface opened on a clipboard
    /// configuration reads the clipboard before the user touches anything. The file picker is only ever driven by
    /// a user command, so an event is still the right shape for it.
    /// </para>
    /// </summary>
    public Func<Task<string?>>? ReadClipboardAsync { get; init; }

    // ── Metadata lane (read-only, implicit per-command transactions) ────────────────────────────────────

    public Func<CancellationToken, Task<IReadOnlyList<string>>>? ListTablesAsync { get; init; }

    public Func<string, CancellationToken, Task<ImportTarget?>>? ReadTargetAsync { get; init; }

    // ── Ddl lane (autonomous, auto-committed, WAIT / Developer Mode) ─────────────────────────────────────

    /// <summary>
    /// ⭐ Runs the <c>CREATE TABLE</c> for a new target — on the <b>Ddl</b> lane, autonomously, and committed
    /// before the first row.
    /// <para>
    /// That is not a preference, it is gotcha #213: a Firebird transaction cannot use an object whose DDL it
    /// has not committed, so creating the table inside the import's own transaction would make every
    /// <c>INSERT</c> fail with "table unknown". The consequence — <b>a rollback of the import cannot remove
    /// the table</b> — is stated in the Target section, in the readiness strip and in the report (§0.5).
    /// </para>
    /// </summary>
    public Func<string, CancellationToken, Task>? CreateTableAsync { get; init; }

    /// <summary>Drops a table this run created, offered when the import then failed. Also on the Ddl lane, and
    /// only ever after the import's own transaction has been rolled back — the rows have to be gone before the
    /// table can be.</summary>
    public Func<string, CancellationToken, Task>? DropTableAsync { get; init; }

    /// <summary>
    /// ⭐ Reports that this run CREATED a table, so the rest of the application can reflect it — today the
    /// metadata tree, which inserts one leaf in place.
    /// <para>
    /// It is a statement of fact ("this table now exists"), not a command ("refresh yourself"): the module knows
    /// exactly what it changed, and telling the tree to re-read all thirteen categories to rediscover one table
    /// is what makes a create cost over a second of frozen UI. The name, not a Firebird or metadata type,
    /// because a ViewModel may name neither (rule #1).
    /// </para>
    /// </summary>
    public Action<string>? TableCreated { get; init; }

    /// <summary>The counterpart: a table this run created has been dropped again after a failed import. Without
    /// it the tree would keep offering an object that no longer exists.</summary>
    public Action<string>? TableDropped { get; init; }

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

    // ── Named profiles (etap I11) ───────────────────────────────────────────────────────────────────────
    //
    // ⭐ Delegates, like every other collaborator, and for the same two reasons: the surface stays testable with
    // no settings file behind it, and the CONNECTION IDENTITY a profile is scoped by never reaches the VM. Which
    // database this is, is not a decision about how to read a file (§4.8.2) — so the surface asks "my profiles"
    // and someone else knows which ones those are.

    /// <summary>The named profiles usable on this connection, ordered by name. A profile too new for this build
    /// is included and reports itself unreadable — hiding it would look like a deletion (§4.8.3).</summary>
    public Func<IReadOnlyList<ImportProfile>>? ListProfiles { get; init; }

    /// <summary>Stores the configuration under a name, replacing a same-named profile. The surface asks about the
    /// overwrite first; this just performs it.</summary>
    public Func<string, ImportConfiguration, ImportProfile>? SaveProfile { get; init; }

    /// <summary>Renames by id. <c>false</c> when the name is already taken — never silently resolved.</summary>
    public Func<string, string, bool>? RenameProfile { get; init; }

    /// <summary>Deletes by id. Destructive, so the surface confirms first.</summary>
    public Func<string, bool>? DeleteProfile { get; init; }

    /// <summary>An environment that knows nothing — the shape a test or a disconnected surface gets.</summary>
    public static DataImportEnvironment Disconnected { get; } =
        new(() => false, () => string.Empty);
}
