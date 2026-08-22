using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Services;

/// <summary>What a planned licence would have done to it when the run executes.</summary>
public enum BulkSendPlanned
{
    /// <summary>A message will be composed for it and an attempt made.</summary>
    Send = 0,

    /// <summary>
    /// It stays in the plan and is deliberately not attempted, for a stated reason.
    ///
    /// <para>⭐ Today the only reason is "already sent since the current artifact was issued". ⚠ A skipped
    /// licence is NOT dropped from the plan: the report has to be able to say how many were skipped and
    /// why, and a licence quietly removed from a list cannot be counted.</para>
    /// </summary>
    Skip = 1,
}

/// <summary>
/// One ticked licence and what the run would do with it.
/// </summary>
/// <remarks>
/// <para>⭐ The presentation layer over this is <c>BulkSendRow</c> (L10.4); everything decided here is a
/// judgement rather than a layout choice, which is what makes it worth a test. ⚠ No Avalonia types
/// (Architecture rule 1).</para>
///
/// <para>⭐⭐ <b>Exactly one of three states, and they are not interchangeable:</b> <see cref="Hold"/> set
/// means it never becomes a message at all; <see cref="Action"/> = <see cref="BulkSendPlanned.Skip"/> means
/// it is in the run and deliberately untouched; otherwise it is one of the messages that will be attempted.
/// §60.2's five counts are built from exactly this distinction.</para>
/// </remarks>
public sealed record BulkSendCandidate
{
    /// <summary>What the register answered about this licence.</summary>
    public required LicenseSummary Summary { get; init; }

    /// <summary>
    /// The artifact <c>license_current_artifact</c> points at, or <see langword="null"/> when the licence
    /// has never been issued.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ The POINTER, never <c>Artifacts[0]</c> — the same authority <c>InspectLatest</c> and the single
    /// send path read (§39.2). ⛔ This is also why "superseded" needs no rule of its own: the pointer does
    /// not point at a superseded artifact, so the state is unreachable rather than filtered.
    /// </remarks>
    public IssuedArtifactRecord? CurrentArtifact { get; init; }

    /// <summary>Whose licence it is, as the register holds them, or <see langword="null"/> when the licence names a customer that is not there.</summary>
    public CustomerRecord? Customer { get; init; }

    /// <summary>
    /// Why this licence cannot take part at all, or <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// ⭐ Never blank on a held row: it is the only thing telling the operator what to do about the licence
    /// they ticked and did not get. ⛔ And never silently dropped — §14.1's rule that survives into §60.4.
    /// </remarks>
    public LocalizedText? Hold { get; init; }

    /// <summary>What the run would do with it. ⚠ Only meaningful when <see cref="Qualifies"/>.</summary>
    public BulkSendPlanned Action { get; init; }

    /// <summary>Why it is being skipped — non-null exactly when <see cref="Action"/> is <see cref="BulkSendPlanned.Skip"/>.</summary>
    public LocalizedText? SkipReason { get; init; }

    /// <summary>When this licence was last successfully sent, or <see langword="null"/>.</summary>
    /// <remarks>⚠ "Sent" means a server accepted it. ⛔ Never "the customer received it" — see §60.1.</remarks>
    public DateTimeOffset? LastSentAt { get; init; }

    /// <summary>⭐ The name signed into the artifact, which is what the message addresses.</summary>
    public string CustomerName => Summary.CustomerName;

    /// <summary>Where the message would go, or empty when the customer has no address.</summary>
    public string Address => Customer?.Email?.Trim() ?? string.Empty;

    /// <summary>The <c>lid</c>.</summary>
    public string LicenseId => Summary.License.LicenseId;

    /// <summary>The licence id, shortened for a list.</summary>
    public string ShortId => LicenceIdText.Short(LicenseId);

    /// <summary>Whether this licence is in the run at all.</summary>
    public bool Qualifies => Hold is null;

    /// <summary>Whether a message will actually be attempted for it.</summary>
    public bool WillBeSent => Qualifies && Action == BulkSendPlanned.Send;
}

/// <summary>
/// A whole bulk send, as it would happen — and the authority on what the operator was shown.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>The plan is the contract between the preview and the execution</b>, exactly as
/// <c>BatchRenewalPlan</c> is for a batch renewal: the execution re-plans from the same inputs and REFUSES
/// if the fresh plan is not the one that was on screen (<see cref="Matches"/>). "38 shown, 36 attempted" is
/// therefore unreachable rather than unlikely.</para>
///
/// <para>⚠⚠ <b>ONE deliberate divergence from the batch renewal's D‑3, and it has a reason:</b> there, a
/// single blocked licence disables the whole operation, because the renewal is ONE register transaction and
/// a partial one would be inconsistent. A send is inherently non-atomic — N conversations with an outside
/// server — and one customer without an e-mail address must not stop the other forty. So held licences are
/// EXCLUDED and NAMED rather than blocking; the half of D‑3 that survives is that nothing is dropped
/// silently (§60.4).</para>
/// </remarks>
public sealed record BulkSendPlan
{
    /// <summary>Every ticked licence, in the register's own order.</summary>
    /// <remarks>
    /// ⭐ The order is the register's — soonest expiry first, then customer name — so the preview reads the
    /// same way twice and two plans are comparable position by position.
    /// </remarks>
    public required IReadOnlyList<BulkSendCandidate> Candidates { get; init; }

    /// <summary>Where the messages would go out through. ⭐ Shown, so "sent" never has to mean "somewhere".</summary>
    public required string Host { get; init; }

    /// <summary>Seconds between two messages.</summary>
    public required int DelaySeconds { get; init; }

    /// <summary>The most messages this run may attempt.</summary>
    public required int MaxPerRun { get; init; }

    /// <summary>Whether the operator asked to skip licences already sent since their current artifact was issued.</summary>
    public required bool SkipAlreadySent { get; init; }

    /// <summary>The ones that never become a message, each with its reason. ⛔ Named, never dropped.</summary>
    public IReadOnlyList<BulkSendCandidate> Held =>
        Candidates.Where(c => !c.Qualifies).ToList();

    /// <summary>The ones in the run that are deliberately not attempted, each with its reason.</summary>
    public IReadOnlyList<BulkSendCandidate> Skipped =>
        Candidates.Where(c => c.Qualifies && c.Action == BulkSendPlanned.Skip).ToList();

    /// <summary>⭐ The messages that will actually be attempted — the progress bar's denominator.</summary>
    public IReadOnlyList<BulkSendCandidate> Sendable =>
        Candidates.Where(c => c.WillBeSent).ToList();

    /// <summary>How many licences are in the run at all — <see cref="Sendable"/> plus <see cref="Skipped"/>.</summary>
    /// <remarks>⭐ The RAPORT's denominator, and deliberately a different number from the progress bar's.</remarks>
    public int Planned => Candidates.Count(c => c.Qualifies);

    /// <summary>How many distinct addresses the attempted messages go to.</summary>
    public int RecipientCount => DistinctAddresses().Count;

    /// <summary>
    /// How many ADDRESSES would receive more than one message.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>It counts addresses, not extra messages</b> — two licences for one customer make this 1, not 2,
    /// because the sentence it feeds says <i>"N addresses will receive more than one message"</i>.
    /// ⭐ It is SHOWN rather than repaired: ⛔ merging two licences into one message is forbidden by §60.0,
    /// and it could not work anyway — the attachment has a fixed file name, so two would collide.
    /// </remarks>
    public int DuplicateRecipientCount =>
        DistinctAddresses().Count(group => group.Value > 1);

    /// <summary>
    /// The shortest this run can take: the waiting alone.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>"At least", and the wording matters as much as the arithmetic.</b> It counts the GAPS —
    /// <c>(attempted − 1) × delay</c>, so one message waits for nothing — and it cannot include the time
    /// each server conversation takes, because that is unknown before it happens. ⛔ Never presented as an
    /// estimate of when the run will finish.
    /// </remarks>
    public TimeSpan MinimumDuration =>
        TimeSpan.FromSeconds(Math.Max(0, Sendable.Count - 1) * (long)DelaySeconds);

    /// <summary>⛔ Whether the selection is over the configured run limit, which DISABLES the action.</summary>
    /// <remarks>
    /// ⚠ Measured against the messages that would actually be attempted, not against the ticks: skipping
    /// twenty already-sent licences legitimately brings a selection back under the limit.
    /// </remarks>
    public bool ExceedsRunLimit => Sendable.Count > MaxPerRun;

    /// <summary>Nothing is ticked, so there is nothing to preview.</summary>
    public bool IsEmpty => Candidates.Count == 0;

    /// <summary>Whether the run may start: something would be attempted, and the limit is not exceeded.</summary>
    public bool CanExecute => Sendable.Count > 0 && !ExceedsRunLimit;

    /// <summary>
    /// Whether another plan would do exactly the same thing to exactly the same licences.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ The semantic-atomicity check. It compares the pacing, the limit, the licences, their order,
    /// their VERDICTS and their ADDRESSES — so a plan that gained a hold, lost a licence, changed one
    /// licence's recipient or would now skip something is NOT the plan the operator approved, and the run
    /// stops rather than executing a near-miss.</para>
    /// <para>⚠ It deliberately does NOT compare the reason TEXT. A hold's sentence is presentation and can
    /// legitimately differ between two readings — the interface language may have changed while the
    /// confirmation was on screen — while the verdict it explains is the thing that must not have moved.</para>
    /// </remarks>
    public bool Matches(BulkSendPlan other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (DelaySeconds != other.DelaySeconds ||
            MaxPerRun != other.MaxPerRun ||
            SkipAlreadySent != other.SkipAlreadySent ||
            !string.Equals(Host, other.Host, StringComparison.Ordinal) ||
            Candidates.Count != other.Candidates.Count)
        {
            return false;
        }

        for (var i = 0; i < Candidates.Count; i++)
        {
            var mine = Candidates[i];
            var theirs = other.Candidates[i];

            if (!string.Equals(mine.LicenseId, theirs.LicenseId, StringComparison.Ordinal) ||
                !string.Equals(mine.Address, theirs.Address, StringComparison.Ordinal) ||
                mine.Qualifies != theirs.Qualifies ||
                mine.Action != theirs.Action)
            {
                return false;
            }
        }

        return true;
    }

    // ⚠ Addresses of the ATTEMPTED messages only: a held licence has no recipient, and a skipped one is not
    //   receiving anything. Ordinal, because an address is an identifier here rather than prose.
    private Dictionary<string, int> DistinctAddresses()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in Candidates)
        {
            if (!candidate.WillBeSent)
            {
                continue;
            }

            counts[candidate.Address] = counts.TryGetValue(candidate.Address, out var seen) ? seen + 1 : 1;
        }

        return counts;
    }
}

/// <summary>
/// Turns a selection into the bulk send that would follow from it.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>Pure, and it takes what it needs as LOOKUPS rather than taking the register.</b> That keeps
/// every judgement here testable against records a test constructs, and keeps the planner from acquiring
/// the ability to write anything or to send anything. ⚠ No IO, no signing, no socket, no Avalonia type.</para>
///
/// <para>⛔ <b>It does not compose a single message.</b> Composition happens once, for the whole run, after
/// the operator clicks and before the first send (§60.4 step 4) — because the preview is rebuilt on every
/// keystroke in the search box, and composing hundreds of messages per typed character would mean parsing
/// hundreds of tokens and filling hundreds of templates. ⭐ What it DOES do is ask
/// <see cref="LicenseMessageComposer.Problems"/>, which is the same authority the single send path uses, so
/// a licence that cannot be composed is held HERE rather than failing later.</para>
/// </remarks>
public static class BulkSendPlanner
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Plans the run.
    /// </summary>
    /// <param name="selected">The ticked licences, in the order they should be shown.</param>
    /// <param name="currentArtifact">⭐ The artifact <c>license_current_artifact</c> points at, per licence.</param>
    /// <param name="customer">Whose licence it is, per customer id.</param>
    /// <param name="lastSentAt">
    /// ⭐ The newest successful send per licence — <c>LicenseRegister.GetLastSentAt</c>, read ONCE for the
    /// whole register (§60.7). ⛔ Never a lookup that queries per licence.
    /// </param>
    /// <param name="settings">The sender, the pacing and the run limit.</param>
    /// <param name="now">The clock, so "expired" is a decision a test can make.</param>
    /// <param name="skipAlreadySent">Whether to skip licences already sent since their current artifact was issued.</param>
    public static BulkSendPlan Plan(
        IReadOnlyList<LicenseSummary> selected,
        Func<string, IssuedArtifactRecord?> currentArtifact,
        Func<string, CustomerRecord?> customer,
        IReadOnlyDictionary<string, DateTimeOffset> lastSentAt,
        SmtpSettings settings,
        DateTimeOffset now,
        bool skipAlreadySent)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(currentArtifact);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(lastSentAt);
        ArgumentNullException.ThrowIfNull(settings);

        var candidates = new List<BulkSendCandidate>(selected.Count);

        foreach (var summary in selected)
        {
            candidates.Add(Judge(summary, currentArtifact, customer, lastSentAt, settings, now, skipAlreadySent));
        }

        return new BulkSendPlan
        {
            Candidates = candidates,
            Host = settings.Host,
            DelaySeconds = settings.BulkDelaySeconds,
            MaxPerRun = settings.BulkMaxPerRun,
            SkipAlreadySent = skipAlreadySent,
        };
    }

    private static BulkSendCandidate Judge(
        LicenseSummary summary,
        Func<string, IssuedArtifactRecord?> currentArtifact,
        Func<string, CustomerRecord?> customer,
        IReadOnlyDictionary<string, DateTimeOffset> lastSentAt,
        SmtpSettings settings,
        DateTimeOffset now,
        bool skipAlreadySent)
    {
        var licence = summary.License;
        var artifact = currentArtifact(licence.LicenseId);
        var owner = customer(licence.CustomerId);

        var candidate = new BulkSendCandidate
        {
            Summary = summary,
            CurrentArtifact = artifact,
            Customer = owner,
            LastSentAt = lastSentAt.TryGetValue(licence.LicenseId, out var sent) ? sent : null,
        };

        if (FindHold(candidate, settings, now) is { } hold)
        {
            return candidate with { Hold = hold };
        }

        // ⭐⭐ COMPARED AGAINST THE CURRENT ARTIFACT'S `iat`, NEVER AGAINST "has it ever been sent".
        //    Without this every RENEWAL would be skipped, because the licence had a message a year ago —
        //    which is the one way a default-on skip could do harm. §60.7 states it as the condition that
        //    makes the option safe to default to ON.
        // ⚠ `>=` and not `>`: issuing and sending in the same second is the ordinary case for the single
        //   path (issue, then send), and it must count as sent.
        if (skipAlreadySent &&
            candidate.LastSentAt is { } lastSent &&
            artifact is { } current &&
            lastSent >= current.IssuedAt)
        {
            return candidate with
            {
                Action = BulkSendPlanned.Skip,
                SkipReason = new LocalizedText(
                    StatusCatalog.BulkSkipAlreadySent,
                    lastSent.ToString(DateFormat, CultureInfo.InvariantCulture)),
            };
        }

        return candidate;
    }

    // ⭐ §60.3's four conditions, in the order an operator would fix them — plus the register fault the
    //   spec's fourth condition cannot evaluate on its own (a licence naming a customer that is not there).
    // ⛔ Every ineligible candidate gets exactly ONE reason: the first that applies.
    private static LocalizedText? FindHold(
        BulkSendCandidate candidate, SmtpSettings settings, DateTimeOffset now)
    {
        var licence = candidate.Summary.License;

        // 1 · The register's own bookkeeping. ⭐ This IS the existing definition of "active" — the same
        //     value the licences view's Status filter reads. ⛔ Not a status this planner computes.
        if (!string.Equals(licence.Status, LicenseStatuses.Active, StringComparison.Ordinal))
        {
            return new LocalizedText(StatusCatalog.BulkHoldLicenceBlocked);
        }

        // 2 · Nothing to send. ⭐ Also what makes "superseded" unreachable rather than filtered: the
        //     pointer never points at a superseded artifact.
        if (candidate.CurrentArtifact is not { } artifact)
        {
            return new LocalizedText(StatusCatalog.BulkHoldNeverIssued);
        }

        // ⚠ A licence whose customer is gone is a REGISTER fault, not an operator's mistake — and it has to
        //   be named here, because condition 4 cannot ask the composer anything without a customer.
        if (candidate.Customer is not { } owner)
        {
            return new LocalizedText(
                StatusCatalog.LicenceRefersToUnknownCustomer, candidate.ShortId, licence.CustomerId);
        }

        // 3 · ⭐⭐ THE EXPIRY IS JUDGED ON THE ARTIFACT THAT WOULD TRAVEL, not on the licence row. §14.2's
        //     rule: the words and the attachment come from the same bytes. A row's `ExpiresAt` may have been
        //     moved and never issued, and a message built from it would describe a licence nobody holds.
        // ⛔ `NotYetValid` is NOT excluded (§60.3): sending a renewal that takes effect on 1 January is an
        //    ordinary operation. Only an `exp` already in the past is useless to a customer.
        // ⚠ An unreadable token falls THROUGH to condition 4, which names it — this cannot judge a payload
        //   it cannot read, and inventing a second "unreadable" sentence here would be a second owner.
        if (ReadPayload(artifact) is { } payload && payload.ExpiresAt <= now)
        {
            return new LocalizedText(
                StatusCatalog.BulkHoldArtifactExpired,
                payload.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        // 4 · ⭐⭐ THE COMPOSER'S OWN VERDICT, verbatim — no customer address, an address that is not one, an
        //     unreadable artifact, no sender address. ONE authority about what can be sent, shared with the
        //     single send path; ⛔ a second opinion here is how two surfaces start disagreeing about the
        //     same licence.
        var problems = LicenseMessageComposer.Problems(artifact, owner, settings);

        return problems.Count == 0
            ? null
            : new LocalizedText(StatusCatalog.Verbatim, new LocalizedSentences(problems));
    }

    // ⭐ Read out of the TOKEN rather than the record's PayloadJson column, exactly as the composer does:
    //   the two hold the same bytes today, and reading the token is what keeps that true by construction.
    private static LicensePayload? ReadPayload(IssuedArtifactRecord artifact) =>
        LicenseEnvelope.TryParse(artifact.Token, out var envelope, out _) &&
        LicensePayload.TryParse(envelope.PayloadJson, out var payload, out _)
            ? payload
            : null;
}
