using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One place that holds unsettled database work, and can settle it.
/// <para>
/// The counterpart of <see cref="IUnsavedWorkSource"/>, which the app has had for editor work since the
/// close-guard seam. The two are deliberately NOT merged: editor work is settled with Save/Discard and
/// transactional work with Commit/Rollback, and the close dialog already asks them as two phases with two
/// verbs. Folding them into one list would take the "keep it" option away from one of them.
/// </para>
/// </summary>
public interface IPendingTransactionalWork
{
    /// <summary>True when something would be lost by stopping now.</summary>
    bool HasWork { get; }

    /// <summary>One line naming what would be lost, in numbers rather than adjectives.</summary>
    string Describe();

    Task CommitAsync();

    Task RollbackAsync();
}

/// <summary>
/// ⭐ The <b>single owner</b> of "does the application hold anything uncommitted, and what is it".
/// <para>
/// It exists because I7.5 gave Data Import its own transaction, which turned a question with exactly one
/// answer (<c>TransactionService.IsActive</c>) into a question with several. The alternative was a growing
/// list of module names inside the close guard — <i>check the console, check the import, check whatever comes
/// next</i> — which spreads knowledge of every module across the shell and guarantees the next module is the
/// one somebody forgets.
/// </para>
/// <para>
/// <b>Not an abstraction built for the future:</b> it ships with two real sources (rule #2), and the app
/// already had this exact shape for the other half of the same question. Had neither been true, two names in
/// one place would have been the right answer.
/// </para>
/// <para>
/// ⚠ <b>The debugger deliberately does not register.</b> Its ratified contract (spec §4.4) is that a debug
/// run's writes are discarded at session end — losing them is the intended outcome, not a surprise, so asking
/// about them would be a question with a foregone answer. The registry could hold it the day that changes.
/// </para>
/// </summary>
public sealed class PendingWorkRegistry
{
    private readonly List<IPendingTransactionalWork> _sources = new();

    public void Register(IPendingTransactionalWork source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!_sources.Contains(source)) _sources.Add(source);
    }

    public void Unregister(IPendingTransactionalWork source) => _sources.Remove(source);

    /// <summary>The one question the shell asks: is there anything uncommitted anywhere?</summary>
    public bool HasWork
    {
        get
        {
            foreach (var source in _sources)
            {
                if (source.HasWork) return true;
            }
            return false;
        }
    }

    /// <summary>What would be lost, one line per source. Only sources that actually hold something speak.</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();
        foreach (var source in _sources)
        {
            if (source.HasWork) lines.Add(source.Describe());
        }
        return lines;
    }

    /// <summary>Commits every source that has work. Used by the disconnect/exit guards, where the user chose
    /// "keep it" about everything at once.</summary>
    public async Task CommitAllAsync()
    {
        foreach (var source in _sources.ToArray())
        {
            if (source.HasWork) await source.CommitAsync().ConfigureAwait(true);
        }
    }

    public async Task RollbackAllAsync()
    {
        foreach (var source in _sources.ToArray())
        {
            if (source.HasWork) await source.RollbackAsync().ConfigureAwait(true);
        }
    }
}

/// <summary>The user's console transaction — F5, the inline data editor, the Script Executor.</summary>
public sealed class ConsoleTransactionWork : IPendingTransactionalWork
{
    private readonly TransactionService _transactions;

    public ConsoleTransactionWork(TransactionService transactions)
        => _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));

    public bool HasWork => _transactions.IsActive;

    public string Describe() => string.Format(
        CultureInfo.CurrentCulture, UiStrings.UnsavedTransactionDataFormat, _transactions.StatementCount);

    public Task CommitAsync() => _transactions.CommitAsync();

    public Task RollbackAsync() => _transactions.RollbackAsync();
}

/// <summary>
/// Data Import's own transaction (I7.5). Registered while an import tab is open and unregistered when it
/// closes, so the shell never has to know a Data Import module exists — only that something has work.
/// </summary>
public sealed class ImportSessionWork : IPendingTransactionalWork
{
    private readonly ImportSessionConnection _session;

    public ImportSessionWork(ImportSessionConnection session)
        => _session = session ?? throw new ArgumentNullException(nameof(session));

    public bool HasWork => _session.IsActive && _session.UncommittedRows > 0;

    public string Describe() => string.Format(
        CultureInfo.CurrentCulture, UiStrings.UnsavedImportRowsFormat, _session.UncommittedRows);

    public Task CommitAsync() => _session.CommitAsync();

    public Task RollbackAsync() => _session.RollbackAsync();
}
