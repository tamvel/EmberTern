using EmberTern.Core.Localization;

namespace EmberTern.Core.Diagnostics;

/// <summary>Two levels only in V1 — no confidence scoring (deferred to V2).</summary>
public enum SessionHealthSeverity
{
    /// <summary>Amber — worth watching.</summary>
    Warning,

    /// <summary>Red — an active health risk.</summary>
    Critical,
}

/// <summary>The V1 detectors. Heavy-user is deliberately NOT a finding kind — it surfaces
/// as a Health-Bar counter + a row marker, to keep the warning surface calm.</summary>
public enum SessionHealthKind
{
    LongRunningTransaction,
    GarbageCollectionRisk,
}

/// <summary>The overall database-session verdict (drives the Health-Bar dot).</summary>
public enum HealthGrade
{
    Healthy,
    Watch,
    AtRisk,
}

/// <summary>Single-stripe risk classification for a session row, highest-priority wins.</summary>
public enum SessionRisk
{
    None = 0,

    /// <summary>Holds a long-running / snapshot transaction (not the GC gatekeeper).</summary>
    LongTransaction = 1,

    /// <summary>Owns the transaction pinning the OAT — blocking garbage collection.</summary>
    GcBlocker = 2,

    // Heavy-load classification is deferred to V2 — it requires an inter-poll activity RATE
    // (snapshot delta), not the cumulative MON$RECORD_STATS total, which misleadingly flags a
    // long-lived idle session as heavy. See the Session Manager "Deferred V2" notes.
}

/// <summary>
/// One health observation, shaped like the Performance <c>Finding</c> (severity + evidence +
/// investigation-oriented text), minus V1-deferred confidence. Pure.
///
/// <para>⭐ <b>Every text member is a <see cref="LocalizableMessage"/>, not a <c>string</c> (decision
/// D‑3).</b> Core cannot know the reader's language, so it names the sentence and hands over the data that
/// belongs in it; the App resolves both at the moment it renders. Resolving here — even once, into a field —
/// would freeze the words in whatever language was current when the analysis ran, which is precisely the
/// failure the live-switching design exists to prevent.</para>
/// </summary>
public sealed record SessionHealthFinding
{
    public required SessionHealthKind Kind { get; init; }

    public required SessionHealthSeverity Severity { get; init; }

    public required long AttachmentId { get; init; }

    /// <summary>The transaction the finding is about, when applicable.</summary>
    public long? TransactionId { get; init; }

    public required LocalizableMessage Title { get; init; }

    public required LocalizableMessage Explanation { get; init; }

    /// <summary>Plain-language consequence — the "why it matters" line. Null when the finding has none.</summary>
    public LocalizableMessage? Impact { get; init; }

    /// <summary>Compact factual evidence rows (isolation, age, gap, …).</summary>
    public IReadOnlyList<LocalizableMessage> Evidence { get; init; } = Array.Empty<LocalizableMessage>();

    /// <summary>Investigation prompts — never imperative.</summary>
    public IReadOnlyList<LocalizableMessage> WhatToCheck { get; init; } = Array.Empty<LocalizableMessage>();
}

/// <summary>Per-session derived health for the grid (risk stripe + counts).</summary>
public sealed record SessionHealthEntry(
    long AttachmentId,
    SessionRisk Risk,
    int ActiveTransactionCount,
    double? OldestTransactionAgeSeconds);

/// <summary>Per-transaction derived health for the grid (GC impact + blocker/long flags).</summary>
public sealed record TransactionHealthEntry(
    long TransactionId,
    long GcImpact,
    bool IsGcBlocker,
    bool IsLong,
    SessionHealthSeverity? Severity);

/// <summary>The one-line verdict for the Health Bar (mirrors the Performance verdict shape).
///
/// <para>⛔ <b><see cref="Headline"/> is deliberately still a <c>string</c> while every other message in this
/// file is a <see cref="LocalizableMessage"/>, and that is a recorded boundary rather than an omission.</b>
/// Its wording is chosen by a COUNT — <c>"1 transaction is blocking…"</c> versus <c>"3 transactions are
/// blocking…"</c> — so migrating it as it stands would put English's two-way singular/plural split into the
/// catalog as if it were universal. A language with more plural categories (Polish has three for this shape)
/// cannot be served by two keys, and Core must not be the layer that decides which category applies. It
/// migrates when the plural mechanism is chosen; ⛔ do not "finish the job" by adding two keys.</para>
/// </summary>
public sealed record SessionHealthVerdict(HealthGrade Grade, string Headline);

/// <summary>Health-Bar counters — the risk ones are clickable filter chips in the UI.</summary>
public sealed record SessionHealthCounters(
    int Sessions,
    int Transactions,
    int LongTransactions,
    int GcRisks,
    long OldestActiveLag);

/// <summary>The full analysis result: verdict + findings + per-session / per-transaction
/// health lookups + counters + the database transaction state. Consumed by the App VMs.</summary>
public sealed record SessionHealthReport
{
    public required SessionHealthVerdict Verdict { get; init; }

    /// <summary>Ordered critical-first.</summary>
    public IReadOnlyList<SessionHealthFinding> Findings { get; init; } = Array.Empty<SessionHealthFinding>();

    /// <summary>Keyed by attachment id.</summary>
    public IReadOnlyDictionary<long, SessionHealthEntry> Sessions { get; init; }
        = new Dictionary<long, SessionHealthEntry>();

    /// <summary>Keyed by transaction id.</summary>
    public IReadOnlyDictionary<long, TransactionHealthEntry> Transactions { get; init; }
        = new Dictionary<long, TransactionHealthEntry>();

    public required SessionHealthCounters Counters { get; init; }

    public required DatabaseTransactionState Database { get; init; }

    /// <summary>Convenience lookups for the VMs.</summary>
    public SessionHealthEntry EntryFor(long attachmentId)
        => Sessions.TryGetValue(attachmentId, out var e)
            ? e
            : new SessionHealthEntry(attachmentId, SessionRisk.None, 0, null);

    public TransactionHealthEntry EntryForTransaction(long transactionId)
        => Transactions.TryGetValue(transactionId, out var e)
            ? e
            : new TransactionHealthEntry(transactionId, 0, false, false, null);
}
