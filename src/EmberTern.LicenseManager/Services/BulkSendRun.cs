using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.Services;

/// <summary>Where a run currently is.</summary>
public enum BulkSendPhase
{
    /// <summary>Nothing has started yet.</summary>
    Idle = 0,

    /// <summary>Talking to the server about one message.</summary>
    Sending = 1,

    /// <summary>
    /// Pacing before the next message.
    ///
    /// <para>⭐ It is a phase of its own because the progress bar deliberately does NOT move here, and an
    /// unexplained stationary bar reads as a frozen application. The surface says how long is left instead.</para>
    /// </summary>
    Waiting = 2,

    /// <summary>The operator asked to stop, and the attempt in flight is being allowed to finish.</summary>
    Stopping = 3,

    /// <summary>Over.</summary>
    Finished = 4,
}

/// <summary>
/// What is happening right now — one snapshot, replaced as the run advances.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>PROGRESS is its own model, not the plan with counters bolted on and not the result being
/// filled in</b> (§60.2). Three types mean the three questions cannot be confused: what we intend, what is
/// happening, what happened.</para>
///
/// <para>⚠ No Avalonia types (Architecture rule 1). It reaches the interface through
/// <see cref="IProgress{T}"/>, which marshals to whatever context created it.</para>
/// </remarks>
public sealed record BulkSendProgress
{
    /// <summary>Where the run is.</summary>
    public required BulkSendPhase Phase { get; init; }

    /// <summary>
    /// How many messages the run will attempt — ⭐ the progress bar's <c>Maximum</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The count of ATTEMPTED messages, which is neither the ticks nor the plan's size: held licences
    /// never become messages and skipped ones are deliberately not attempted (§60.2).
    /// </remarks>
    public required int Total { get; init; }

    /// <summary>
    /// How many attempts have FINISHED — ⭐ the progress bar's <c>Value</c>.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ It counts finished attempts and nothing else: it does not move while waiting, and it is never
    /// advanced to make a bar look busy. 🔒 The user's requirement, stated twice: the bar is tied to the
    /// real number of completed attempts, and <c>IsIndeterminate</c> is never used.
    /// </remarks>
    public required int Completed { get; init; }

    /// <summary>How many of those succeeded.</summary>
    public required int Sent { get; init; }

    /// <summary>How many failed. ⚠ Under K1 this can only ever reach 1 — see <see cref="BulkSendConclusion"/>.</summary>
    public required int Failed { get; init; }

    /// <summary>Whose licence is being handled, or <see langword="null"/> between messages.</summary>
    public string? CurrentCustomer { get; init; }

    /// <summary>Where the current message is going.</summary>
    public string? CurrentAddress { get; init; }

    /// <summary>Which licence, shortened.</summary>
    public string? CurrentShortId { get; init; }

    /// <summary>How many seconds of pacing are left, while <see cref="Phase"/> is <see cref="BulkSendPhase.Waiting"/>.</summary>
    public int? SecondsToNext { get; init; }
}

/// <summary>What happened to one licence in a run. ⭐ Exactly one of the four applies.</summary>
public enum BulkSendOutcome
{
    /// <summary>
    /// The server accepted the message.
    ///
    /// <para>⛔⛔ It does NOT mean the customer received it. With one recipient per message a provider
    /// commonly accepts mail for an address that does not exist and bounces it later, which this
    /// application cannot see at all (§60.1). ⛔ The word "delivered" is forbidden in every catalog.</para>
    /// </summary>
    Sent = 0,

    /// <summary>The attempt was made and did not succeed. ⭐ The server's own words travel with it.</summary>
    Failed = 1,

    /// <summary>In the run, deliberately not attempted, for a stated reason.</summary>
    Skipped = 2,

    /// <summary>⭐ The run stopped before reaching it. ⚠ Not a failure and not a skip — nothing was tried.</summary>
    NotAttempted = 3,
}

/// <summary>
/// One licence's line in the report.
/// </summary>
/// <remarks>
/// ⚠⚠ It is a <c>record</c> and therefore raises no <c>PropertyChanged</c>. ⛔ A <c>DataTemplate</c> bound
/// straight to one of its properties renders once and then FREEZES in that language (gotcha #401) — the
/// presentation rows are rebuilt from the result, never notified. L10.4 owns that.
/// </remarks>
public sealed record BulkSendAttempt
{
    /// <summary>Whose licence it is.</summary>
    public required string CustomerName { get; init; }

    /// <summary>Where the message went, or would have gone.</summary>
    public required string Address { get; init; }

    /// <summary>The <c>lid</c>.</summary>
    public required string LicenseId { get; init; }

    /// <summary>The <c>lid</c>, shortened for a list.</summary>
    public string ShortId => LicenceIdText.Short(LicenseId);

    /// <summary>Which of the four happened.</summary>
    public required BulkSendOutcome Outcome { get; init; }

    /// <summary>
    /// OUR sentence about it — why it was skipped, or why it was never attempted.
    /// </summary>
    /// <remarks>
    /// ⭐ A key and its arguments, never rendered text: the report is deliberately long-lived on screen and
    /// is the most likely of all surfaces to still be showing when the language changes.
    /// </remarks>
    public LocalizedText? Reason { get; init; }

    /// <summary>
    /// The SERVER's words, verbatim, when it refused.
    /// </summary>
    /// <remarks>
    /// ⛔ Never translated, never interpreted, never rewritten — the same rule the single send path and
    /// EmberTern's connection dialog both carry. A wrong password and a blocked app password differ only in
    /// this text.
    /// </remarks>
    public string? ServerMessage { get; init; }

    /// <summary>When the attempt finished, or <see langword="null"/> when none was made.</summary>
    public DateTimeOffset? At { get; init; }
}

/// <summary>
/// How a run ended.
/// </summary>
/// <remarks>
/// <para>⚠⚠ <b>FOUR values, and §60.7's fifth (<c>CompletedWithErrors</c>) is deliberately absent.</b> Under
/// the ratified <b>K1</b> policy the first failure stops the run, so a run that had a failure ended
/// <see cref="StoppedAfterError"/> — a "completed, with errors" conclusion has no producer, and an enum
/// value nothing can produce is the dead-surface trap in the one place a reader most needs to trust the
/// list (gotcha #233). ⏭ If K2 is ever adopted it comes back WITH its producer, which is the only honest
/// order.</para>
/// </remarks>
public enum BulkSendConclusion
{
    /// <summary>Nothing was attempted, because nothing qualified.</summary>
    NothingToSend = 0,

    /// <summary>Every attempted message was accepted. ⚠ Skipped licences do not spoil this.</summary>
    Completed = 1,

    /// <summary>🔒 K1: a message was refused, so the run stopped and the rest were never attempted.</summary>
    StoppedAfterError = 2,

    /// <summary>The operator stopped it. ⭐ Said in those words, never as a generic failure.</summary>
    StoppedByOperator = 3,
}

/// <summary>
/// What a run actually did — the report, and the authority the card renders from.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>The invariant, and the whole reason this is a structured value rather than a sentence:</b>
/// <c>Planned == Sent + Failed + Skipped + NotAttempted</c>. 🔒 The user's requirement, and it is asserted
/// automatically — it is what makes "we sent 40" impossible to say when 36 succeeded.</para>
///
/// <para>⚠ HELD licences are deliberately NOT in <see cref="Attempts"/>: they never entered the run. They
/// are named in the plan's own <c>Held</c> list, which the preview shows before the operator commits and
/// which the card keeps showing afterwards.</para>
/// </remarks>
public sealed record BulkSendResult
{
    /// <summary>How it ended.</summary>
    public required BulkSendConclusion Conclusion { get; init; }

    /// <summary>The run's own id — what its <c>licence.batch-sent</c> audit line is filed under.</summary>
    public required string RunId { get; init; }

    /// <summary>Every licence that entered the run, in order, exactly once.</summary>
    public required IReadOnlyList<BulkSendAttempt> Attempts { get; init; }

    /// <summary>When it started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When it ended.</summary>
    public required DateTimeOffset FinishedAt { get; init; }

    /// <summary>How long it took. ⭐ From the injected clock, never a <c>Stopwatch</c> — so a test can make it.</summary>
    public TimeSpan Elapsed => FinishedAt - StartedAt;

    /// <summary>How many licences were in the run.</summary>
    public int Planned => Attempts.Count;

    /// <summary>How many messages the server accepted.</summary>
    public int Sent => Count(BulkSendOutcome.Sent);

    /// <summary>How many were refused.</summary>
    public int Failed => Count(BulkSendOutcome.Failed);

    /// <summary>How many were deliberately not attempted.</summary>
    public int Skipped => Count(BulkSendOutcome.Skipped);

    /// <summary>How many the run never reached.</summary>
    public int NotAttempted => Count(BulkSendOutcome.NotAttempted);

    /// <summary>⭐ The licences whose ticks may be dropped — and ONLY those (§60.10, decision L).</summary>
    public IReadOnlyList<string> SentIds =>
        Attempts.Where(a => a.Outcome == BulkSendOutcome.Sent).Select(a => a.LicenseId).ToList();

    private int Count(BulkSendOutcome outcome) => Attempts.Count(a => a.Outcome == outcome);
}

/// <summary>
/// Runs a bulk send: sends what the plan says, at the pace the settings say, and reports what happened.
/// </summary>
/// <remarks>
/// <para>⭐ <b>Three injected seams, each for one reason.</b> <c>delay</c> so the suite does not wait
/// minutes (⛔ no <c>Thread.Sleep</c> anywhere); <c>clock</c> so "how long it took" is testable without a
/// timer; <see cref="IProgress{T}"/> so progress reaches the interface with no Avalonia type in sight.</para>
///
/// <para>⛔ <b>It composes nothing and signs nothing.</b> Every message is handed to it already composed,
/// before the first send — so a licence that cannot be composed stops the run before anything leaves,
/// which is the shape <c>IssuingWorkflow.IssueBatch</c> established (§60.4 step 4).</para>
///
/// <para>⚠ It holds the register for exactly ONE write: the run's own <c>licence.batch-sent</c> line. Every
/// per-licence audit line is <see cref="LicenceDelivery"/>'s, unchanged.</para>
/// </remarks>
public sealed class BulkSendRun
{
    private readonly LicenseRegister _register;
    private readonly LicenceDelivery _delivery;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates the runner.</summary>
    /// <param name="register">⚠ For the run marker only.</param>
    /// <param name="delivery">Sends and writes the per-licence audit line. ⭐ Unchanged from L6.</param>
    /// <param name="delay">
    /// How to wait. ⭐ Injected so a test can pass a no-op: a suite that actually paced 15 seconds per
    /// message would be unusable, and a <c>Thread.Sleep</c> in a test is worse than that.
    /// </param>
    /// <param name="clock">The clock, for the attempt stamps and the elapsed time.</param>
    public BulkSendRun(
        LicenseRegister register,
        LicenceDelivery delivery,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _delay = delay ?? Task.Delay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Sends the plan.
    /// </summary>
    /// <param name="plan">⭐ The plan the operator approved. ⛔ Not re-derived here — the caller re-plans and compares.</param>
    /// <param name="composed">Every sendable licence's message, by <c>lid</c>, composed before this was called.</param>
    /// <param name="sender">The transport.</param>
    /// <param name="note">The operator's remark for the run's audit line, or <see langword="null"/>.</param>
    /// <param name="progress">Where snapshots go. ⚠ Optional so a test can ignore it.</param>
    /// <param name="stopRequested">
    /// ⭐⭐ <b>"Send no more", NOT "abort this one".</b> It is honoured BETWEEN messages and it interrupts the
    /// PACING; it is deliberately never passed to the sender. A cancelled <c>SendMailAsync</c> may already
    /// have delivered the message, and an audit line claiming otherwise would be a lie — which is why
    /// <c>SmtpLicenseEmailSender</c> rethrows cancellation instead of recording it as a refusal.
    /// </param>
    /// <exception cref="ArgumentException">A sendable licence has no composed message — a caller fault.</exception>
    public async Task<BulkSendResult> ExecuteAsync(
        BulkSendPlan plan,
        IReadOnlyDictionary<string, LicenseMessage> composed,
        ILicenseEmailSender sender,
        string? note = null,
        IProgress<BulkSendProgress>? progress = null,
        CancellationToken stopRequested = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(composed);
        ArgumentNullException.ThrowIfNull(sender);

        var startedAt = _clock();
        var runId = NewRunId();

        // ⭐ The run's own work list: every candidate that entered the run, in the plan's order. Held ones
        //   are absent by construction — they never entered.
        var inRun = plan.Candidates.Where(c => c.Qualifies).ToList();
        var total = inRun.Count(c => c.Action == BulkSendPlanned.Send);

        var attempts = new List<BulkSendAttempt>(inRun.Count);
        var sent = 0;
        var failed = 0;
        var stopped = false;
        var stoppedByOperator = false;

        for (var i = 0; i < inRun.Count; i++)
        {
            var candidate = inRun[i];

            if (candidate.Action == BulkSendPlanned.Skip)
            {
                attempts.Add(Skipped(candidate));
                continue;
            }

            if (stopped)
            {
                attempts.Add(NotAttempted(candidate, stoppedByOperator));
                continue;
            }

            if (!composed.TryGetValue(candidate.LicenseId, out var message))
            {
                // ⛔ A caller fault, not a delivery outcome: §60.4 composes EVERYTHING before the first
                //    send precisely so this is unreachable. Throwing beats inventing a failure the server
                //    never produced.
                throw new ArgumentException(
                    $"No composed message for licence {candidate.LicenseId}.", nameof(composed));
            }

            Report(progress, BulkSendPhase.Sending, total, attempts, sent, failed, candidate, null);

            // ⚠ NO token here, deliberately — see the parameter's own remarks.
            var outcome = await _delivery.SendAsync(sender, message).ConfigureAwait(false);
            var at = _clock();

            if (outcome.Sent)
            {
                sent++;
                attempts.Add(new BulkSendAttempt
                {
                    CustomerName = candidate.CustomerName,
                    Address = candidate.Address,
                    LicenseId = candidate.LicenseId,
                    Outcome = BulkSendOutcome.Sent,
                    At = at,
                });
            }
            else
            {
                failed++;
                attempts.Add(new BulkSendAttempt
                {
                    CustomerName = candidate.CustomerName,
                    Address = candidate.Address,
                    LicenseId = candidate.LicenseId,
                    Outcome = BulkSendOutcome.Failed,

                    // ⭐ OUR sentence when the failure is ours — today that means the send timed out, and
                    //   there are no server words to put beside it. ⛔ `ServerMessage` stays null in that
                    //   case rather than quoting an English diagnostic as if the server had said it: the
                    //   column is called "server message" and it must never contain anything else.
                    Reason = outcome.Reason
                        ?? new LocalizedText(ViewModels.StatusCatalog.BulkAttemptFailed),
                    ServerMessage = outcome.Reason is null ? outcome.Error : null,
                    At = at,
                });

                // 🔒 K1 — STOP ON THE FIRST FAILURE. ⛔ No classification of the server's answer: telling
                //    "a bad address" from "we are being throttled" means interpreting a message this
                //    project has already decided never to interpret, and the outcome carries no
                //    structured code to key on. Everything after this is NotAttempted.
                stopped = true;
                continue;
            }

            // ⭐ Pace only when another message is actually coming. The last message waits for nothing, and
            //   a run of one waits not at all.
            if (!stopped && HasAnotherToSend(inRun, i))
            {
                Report(progress, BulkSendPhase.Waiting, total, attempts, sent, failed, null,
                    plan.DelaySeconds);

                try
                {
                    await _delay(TimeSpan.FromSeconds(plan.DelaySeconds), stopRequested)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // ⭐ The operator asked to stop DURING the pacing: nothing was in flight, so the run
                    //   simply ends here.
                    stopped = true;
                    stoppedByOperator = true;
                }
            }

            // ⚠ Checked AFTER the attempt and after the pacing, never in the middle of a send: an attempt
            //   already under way is allowed to finish and to be recorded (🔒 the user's own wording).
            if (!stopped && stopRequested.IsCancellationRequested)
            {
                Report(progress, BulkSendPhase.Stopping, total, attempts, sent, failed, null, null);
                stopped = true;
                stoppedByOperator = true;
            }
        }

        var finishedAt = _clock();

        var result = new BulkSendResult
        {
            Conclusion = Conclude(attempts, stoppedByOperator),
            RunId = runId,
            Attempts = attempts,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
        };

        RecordRun(result, plan, note);
        Report(progress, BulkSendPhase.Finished, total, attempts, sent, failed, null, null);

        return result;
    }

    // ⭐ Four conclusions, and the order of the checks is the order of what matters: the operator's own
    //   decision outranks everything (they know why they stopped), then K1's refusal, then "nothing to do".
    private static BulkSendConclusion Conclude(
        IReadOnlyList<BulkSendAttempt> attempts, bool stoppedByOperator)
    {
        if (stoppedByOperator)
        {
            return BulkSendConclusion.StoppedByOperator;
        }

        if (attempts.Any(a => a.Outcome == BulkSendOutcome.Failed))
        {
            return BulkSendConclusion.StoppedAfterError;
        }

        // ⚠ "Nothing to send" is about ATTEMPTS, not about the run being empty: a run of twenty licences
        //   that were all already sent attempted nothing, and saying "completed" would overstate it.
        return attempts.Any(a => a.Outcome == BulkSendOutcome.Sent)
            ? BulkSendConclusion.Completed
            : BulkSendConclusion.NothingToSend;
    }

    // ⭐ ONE line per run, written after the fact so its counts are true. ⚠ The note is the OPERATOR's and
    //   the sentence around it is English and invariant, like every audit note in this register
    //   (`terminology.md` §4.4) — a history whose language depends on when a row was written stops being
    //   one document.
    private void RecordRun(BulkSendResult result, BulkSendPlan plan, string? note)
    {
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Bulk send {result.Conclusion}: {result.Sent} sent, {result.Failed} failed, " +
            $"{result.Skipped} skipped, {result.NotAttempted} not attempted, " +
            $"{plan.Held.Count} held, via {plan.Host}.");

        _register.Record(
            AuditActions.LicenceBatchSent,
            AuditTargets.Batch,
            result.RunId,
            string.IsNullOrWhiteSpace(note) ? summary : $"{summary} {note.Trim()}");
    }

    private static bool HasAnotherToSend(IReadOnlyList<BulkSendCandidate> inRun, int index)
    {
        for (var i = index + 1; i < inRun.Count; i++)
        {
            if (inRun[i].Action == BulkSendPlanned.Send)
            {
                return true;
            }
        }

        return false;
    }

    private static BulkSendAttempt Skipped(BulkSendCandidate candidate) => new()
    {
        CustomerName = candidate.CustomerName,
        Address = candidate.Address,
        LicenseId = candidate.LicenseId,
        Outcome = BulkSendOutcome.Skipped,
        Reason = candidate.SkipReason,
    };

    private static BulkSendAttempt NotAttempted(BulkSendCandidate candidate, bool byOperator) => new()
    {
        CustomerName = candidate.CustomerName,
        Address = candidate.Address,
        LicenseId = candidate.LicenseId,
        Outcome = BulkSendOutcome.NotAttempted,

        // ⭐ Two different reasons, because they are two different facts for the operator: one says "you
        //   stopped it", the other "it stopped itself". A single sentence would make the report vaguer than
        //   the run.
        Reason = new LocalizedText(
            byOperator
                ? ViewModels.StatusCatalog.BulkNotAttemptedOperatorStopped
                : ViewModels.StatusCatalog.BulkNotAttemptedRunStopped),
    };

    private static void Report(
        IProgress<BulkSendProgress>? progress,
        BulkSendPhase phase,
        int total,
        IReadOnlyList<BulkSendAttempt> attempts,
        int sent,
        int failed,
        BulkSendCandidate? current,
        int? secondsToNext)
    {
        if (progress is null)
        {
            return;
        }

        progress.Report(new BulkSendProgress
        {
            Phase = phase,
            Total = total,

            // ⭐⭐ FINISHED ATTEMPTS, and nothing else. ⛔ Not "how far through the list we are": a skipped
            //    licence advances the loop and must not advance the bar, or the bar would report progress
            //    the run never made.
            Completed = sent + failed,
            Sent = sent,
            Failed = failed,
            CurrentCustomer = current?.CustomerName,
            CurrentAddress = current?.Address,
            CurrentShortId = current?.ShortId,
            SecondsToNext = secondsToNext,
        });
    }

    // ⚠ The same shape as the batch renewal's batch id — 12 hex characters, enough to correlate an audit
    //   line by eye and short enough to read out.
    private static string NewRunId() => Guid.NewGuid().ToString("N")[..12];
}
