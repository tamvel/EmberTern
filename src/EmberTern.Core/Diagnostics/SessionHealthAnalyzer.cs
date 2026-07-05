using System.Globalization;

namespace EmberTern.Core.Diagnostics;

/// <summary>Thresholds for the V1 detectors. A couple of thresholds — NOT a scoring system
/// (that's deferred to V2). Overridable so tests are deterministic.</summary>
public sealed record SessionHealthOptions
{
    /// <summary>A transaction older than this, if snapshot or the OAT holder, is "long".</summary>
    public TimeSpan LongTransactionWarn { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>A long transaction older than this escalates to Critical.</summary>
    public TimeSpan LongTransactionCritical { get; init; } = TimeSpan.FromMinutes(10);

    // Heavy-user thresholds are gone in V1 — that classification needs an inter-poll rate
    // (deferred to V2). Only the long-transaction + GC-gap thresholds remain.

    /// <summary>An OAT lag at/above this is itself enough to flag a GC risk / escalate it.</summary>
    public long LargeGapThreshold { get; init; } = 10_000;

    public static SessionHealthOptions Default { get; } = new();
}

/// <summary>
/// Turns the raw MON$ snapshot (sessions + transactions + database state) into a diagnosis:
/// a verdict, ranked findings, per-session / per-transaction health, and Health-Bar counters.
/// Pure and deterministic — the whole point of the module lives here, testable with no DB.
///
/// Measured-first + scale-before-alarm: transaction age alone is never a finding; it must be
/// a snapshot OR the OAT gatekeeper, and impact is expressed as the concrete GC gap count.
/// EmberTern's own attachments (<see cref="SessionInfo.IsSelf"/>) are excluded from findings,
/// heavy ranking, and counters — we never warn about our own tool. The database-wide gap is
/// still honest (it includes everyone) because it comes from <c>MON$DATABASE</c>.
/// </summary>
public static class SessionHealthAnalyzer
{
    public static SessionHealthReport Analyze(
        IReadOnlyList<SessionInfo> sessions,
        IReadOnlyList<TransactionInfo> transactions,
        DatabaseTransactionState database,
        DateTime referenceTime,
        SessionHealthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(database);
        options ??= SessionHealthOptions.Default;

        var selfIds = sessions.Where(s => s.IsSelf).Select(s => s.AttachmentId).ToHashSet();
        var visibleSessions = sessions.Where(s => !s.IsSelf).ToList();
        var visibleTransactions = transactions.Where(t => !selfIds.Contains(t.AttachmentId)).ToList();

        // --- Per-transaction derivations -------------------------------------------------
        var oatHolder = FindOatHolder(visibleTransactions, database);
        var gcSet = new HashSet<long>();
        var longSet = new HashSet<long>();
        var txSeverity = new Dictionary<long, SessionHealthSeverity>();
        var findings = new List<SessionHealthFinding>();

        foreach (var tx in visibleTransactions)
        {
            var age = AgeSeconds(tx.StartedAt, referenceTime);
            var isOat = oatHolder is not null && tx.TransactionId == oatHolder.TransactionId;
            var isLong = age is { } a
                         && a >= options.LongTransactionWarn.TotalSeconds
                         && (tx.IsSnapshot || isOat);
            if (isLong)
            {
                longSet.Add(tx.TransactionId);
            }
        }

        // GC risk: the OAT holder, gated so normal churn doesn't nag — it must be a snapshot,
        // OR old, OR the lag must be materially large.
        if (oatHolder is not null)
        {
            var holderAge = AgeSeconds(oatHolder.StartedAt, referenceTime) ?? 0;
            var lag = database.OldestActiveLag;
            // Scale before alarm: being the OAT is not itself a problem — it must have aged or
            // the lag must be materially large. Snapshot isolation escalates SEVERITY, not the trigger.
            var meaningful = holderAge >= options.LongTransactionWarn.TotalSeconds
                             || lag >= options.LargeGapThreshold;
            if (meaningful)
            {
                var severity = (oatHolder.IsSnapshot
                                || holderAge >= options.LongTransactionCritical.TotalSeconds
                                || lag >= options.LargeGapThreshold)
                    ? SessionHealthSeverity.Critical
                    : SessionHealthSeverity.Warning;
                gcSet.Add(oatHolder.TransactionId);
                txSeverity[oatHolder.TransactionId] = severity;
                findings.Add(BuildGcFinding(oatHolder, database, referenceTime, severity));
            }
        }

        // Long-running transactions that are NOT the GC gatekeeper (that one already got a
        // richer GC card that subsumes "long").
        foreach (var txId in longSet)
        {
            if (gcSet.Contains(txId))
            {
                continue;
            }

            var tx = visibleTransactions.First(t => t.TransactionId == txId);
            var age = AgeSeconds(tx.StartedAt, referenceTime) ?? 0;
            var severity = age >= options.LongTransactionCritical.TotalSeconds
                ? SessionHealthSeverity.Critical
                : SessionHealthSeverity.Warning;
            txSeverity[txId] = severity;
            findings.Add(BuildLongTransactionFinding(tx, age, severity));
        }

        // --- Per-session entries ---------------------------------------------------------
        var sessionEntries = new Dictionary<long, SessionHealthEntry>();
        foreach (var s in sessions)
        {
            var own = transactions.Where(t => t.AttachmentId == s.AttachmentId).ToList();
            var activeCount = own.Count(t => t.IsActive);
            double? oldestAge = own
                .Select(t => AgeSeconds(t.StartedAt, referenceTime))
                .Where(a => a is not null)
                .Select(a => a!.Value)
                .DefaultIfEmpty(double.NaN)
                .Max();
            if (double.IsNaN(oldestAge.Value))
            {
                oldestAge = null;
            }

            var risk = SessionRisk.None;
            if (!s.IsSelf)
            {
                if (own.Any(t => gcSet.Contains(t.TransactionId)))
                {
                    risk = SessionRisk.GcBlocker;
                }
                else if (own.Any(t => longSet.Contains(t.TransactionId)))
                {
                    risk = SessionRisk.LongTransaction;
                }
            }

            sessionEntries[s.AttachmentId] = new SessionHealthEntry(
                s.AttachmentId, risk, activeCount, oldestAge);
        }

        // --- Per-transaction entries -----------------------------------------------------
        var txEntries = new Dictionary<long, TransactionHealthEntry>();
        foreach (var tx in transactions)
        {
            var impact = database.NextTransaction > 0
                ? Math.Max(0, database.NextTransaction - tx.TransactionId)
                : 0;
            txEntries[tx.TransactionId] = new TransactionHealthEntry(
                tx.TransactionId,
                impact,
                gcSet.Contains(tx.TransactionId),
                longSet.Contains(tx.TransactionId),
                txSeverity.TryGetValue(tx.TransactionId, out var sev) ? sev : null);
        }

        // --- Ordering, counters, verdict -------------------------------------------------
        findings = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Kind)
            .ThenBy(f => f.AttachmentId)
            .ToList();

        var counters = new SessionHealthCounters(
            Sessions: visibleSessions.Count,
            Transactions: visibleTransactions.Count,
            LongTransactions: longSet.Count,
            GcRisks: gcSet.Count,
            OldestActiveLag: database.OldestActiveLag);

        var verdict = BuildVerdict(findings, gcSet.Count, longSet.Count);

        return new SessionHealthReport
        {
            Verdict = verdict,
            Findings = findings,
            Sessions = sessionEntries,
            Transactions = txEntries,
            Counters = counters,
            Database = database,
        };
    }

    /// <summary>The visible transaction holding the database OAT position, if any.</summary>
    internal static TransactionInfo? FindOatHolder(
        IReadOnlyList<TransactionInfo> transactions, DatabaseTransactionState database)
    {
        if (database.OldestActive <= 0)
        {
            return null;
        }

        // ONLY the exact holder. If no visible transaction matches the OAT, the gatekeeper is a
        // self/hidden attachment — we must not blame an innocent visible transaction for it (the
        // lag counter stays honest from MON$DATABASE regardless).
        return transactions.FirstOrDefault(t => t.IsActive && t.TransactionId == database.OldestActive);
    }

    internal static double? AgeSeconds(DateTime? startedAt, DateTime referenceTime)
        => startedAt is { } start ? Math.Max(0, (referenceTime - start).TotalSeconds) : null;

    private static SessionHealthFinding BuildGcFinding(
        TransactionInfo holder, DatabaseTransactionState db, DateTime now, SessionHealthSeverity severity)
    {
        var age = AgeSeconds(holder.StartedAt, now);
        var lag = db.OldestActiveLag;
        return new SessionHealthFinding
        {
            Kind = SessionHealthKind.GarbageCollectionRisk,
            Severity = severity,
            AttachmentId = holder.AttachmentId,
            TransactionId = holder.TransactionId,
            Title = "Garbage collection is blocked",
            Explanation = holder.IsSnapshot
                ? "A snapshot transaction is the oldest active transaction in the database."
                : "This transaction is the oldest active transaction in the database.",
            Impact = string.Format(
                CultureInfo.InvariantCulture,
                "Firebird cannot garbage-collect record versions from ~{0:N0} later transactions while it stays open — expect growing page reads and file size.",
                lag),
            Evidence = new[]
            {
                $"Tx {holder.TransactionId} · {IsolationLabel(holder)}",
                age is { } a ? $"Age {FormatAge(a)}" : "Age unknown",
                $"OAT lag {lag:N0} · OST {db.OldestSnapshot:N0} · Next {db.NextTransaction:N0}",
            },
            WhatToCheck = new[]
            {
                "Is this a reporting/BI connection that could run read-committed?",
                "Was a UI screen left open on a snapshot mid-edit?",
            },
        };
    }

    private static SessionHealthFinding BuildLongTransactionFinding(
        TransactionInfo tx, double ageSeconds, SessionHealthSeverity severity)
        => new()
        {
            Kind = SessionHealthKind.LongRunningTransaction,
            Severity = severity,
            AttachmentId = tx.AttachmentId,
            TransactionId = tx.TransactionId,
            Title = "Long-running transaction",
            Explanation = tx.IsSnapshot
                ? "A snapshot transaction has stayed open for a long time."
                : "A transaction has stayed open for a long time.",
            Impact = "While it lives it holds a stable view — contributing to record-version retention and delaying garbage collection.",
            Evidence = new[]
            {
                $"Tx {tx.TransactionId} · {IsolationLabel(tx)}",
                $"Age {FormatAge(ageSeconds)}",
            },
            WhatToCheck = new[]
            {
                "Is the owning session idle with the transaction left open?",
                "Can it commit or switch to read-committed?",
            },
        };

    private static SessionHealthVerdict BuildVerdict(
        IReadOnlyList<SessionHealthFinding> findings, int gcCount, int longCount)
    {
        var grade = findings.Any(f => f.Severity == SessionHealthSeverity.Critical)
            ? HealthGrade.AtRisk
            : findings.Count > 0
                ? HealthGrade.Watch
                : HealthGrade.Healthy;

        string headline;
        if (gcCount > 0)
        {
            headline = gcCount == 1
                ? "1 transaction is blocking garbage collection."
                : $"{gcCount} transactions are blocking garbage collection.";
        }
        else if (longCount > 0)
        {
            headline = longCount == 1
                ? "1 long-running transaction detected."
                : $"{longCount} long-running transactions detected.";
        }
        else
        {
            headline = "All sessions healthy.";
        }

        return new SessionHealthVerdict(grade, headline);
    }

    private static string IsolationLabel(TransactionInfo tx)
        => string.IsNullOrWhiteSpace(tx.IsolationMode)
            ? (tx.IsSnapshot ? "Snapshot" : "Read Committed")
            : tx.IsolationMode;

    private static string FormatAge(double seconds) => DiagnosticsFormat.Age(seconds);
}
