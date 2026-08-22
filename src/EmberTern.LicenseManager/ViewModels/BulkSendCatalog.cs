using System;
using System.Globalization;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// What the bulk-send card says in C# — the sentences its view model composes rather than declares in
/// XAML.
///
/// <para>⭐ Its own catalog rather than a region of <see cref="StatusCatalog"/>, for the reason decision
/// D‑5 gives: this is a CARD's running commentary — a preview, a progress line, a report — while a status
/// message is a report of something that happened, raised at one moment and read at another. ⛔ The one
/// sentence both surfaces say ("{0} of {1} messages were sent") lives in <see cref="StatusCatalog"/> and
/// is read from there by both, exactly as <c>BatchRenewalViewModel.BlockerSummary</c> reads
/// <c>Status.Blocked</c>. Two homes for one sentence is two translations of it.</para>
///
/// <para>⚠⚠ <b>The word "delivered" is forbidden here, in every language</b> (§60.1). With one recipient
/// per message a provider commonly ACCEPTS mail for an address that does not exist and bounces it later,
/// which this application never sees. <see cref="AcceptedNotDelivered"/> says so on the card, permanently.</para>
///
/// <para>⚠ Several members below are flat rather than counted, and that is a decision per SENTENCE: a
/// counted family exists where the grammar actually changes with the number (Polish needs three forms),
/// and a section heading like <i>"Will be sent (3)"</i> reads identically for every count in both
/// languages. ⛔ Declaring a family whose arms are identical buys nothing and gives a translator three
/// places to disagree with themselves.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class BulkSendCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Bulk.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    private static string Say(string member, params object?[] arguments) =>
        Loc.Format(KeyPrefix + member, arguments);

    private static string Counted(string member, long count, params object?[] arguments) =>
        Loc.FormatCount(KeyPrefix + member, count, arguments);

    // ── Preview ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Nothing is ticked yet.</summary>
    public static string TickLicences => Word(nameof(TickLicences));

    /// <summary>How many messages the run would attempt.</summary>
    /// <remarks>
    /// ⭐ Its own sentence rather than a clause of the next two: a sentence can have only ONE plural pivot,
    /// and Polish inflects the message count and the address count differently. ⚠ The join is a space, so
    /// the rendered line is the one §60.8 draws — the shape <c>BatchCatalog</c> already uses for its two.
    /// </remarks>
    public static string WillSend(int messages) => Counted(nameof(WillSend), messages);

    /// <summary>How many distinct addresses those messages go to.</summary>
    public static string ToAddresses(int addresses) => Counted(nameof(ToAddresses), addresses);

    /// <summary>⚠ "At least", never an estimate of when the run finishes — the server conversations are unknown.</summary>
    public static string AtLeast(string duration) => Say(nameof(AtLeast), duration);

    /// <summary>⚠ How many ADDRESSES would receive more than one message. ⛔ Shown, never repaired (§60.0).</summary>
    public static string DuplicateAddresses(int addresses) =>
        Counted(nameof(DuplicateAddresses), addresses);

    /// <summary>How many licences are in the run and deliberately not attempted.</summary>
    public static string SkippedCount(int skipped) => Counted(nameof(SkippedCount), skipped);

    /// <summary>The section naming every licence that never becomes a message.</summary>
    public static string HeldSection(int held) => Say(nameof(HeldSection), held);

    /// <summary>⭐ The section carrying the FULL recipient list (§14.1) — never just a counter.</summary>
    public static string SendableSection(int sendable) => Say(nameof(SendableSection), sendable);

    /// <summary>The pacing, the run limit and the server, in one line.</summary>
    public static string Pacing(string delaySeconds, string maxPerRun, string host) =>
        Say(nameof(Pacing), delaySeconds, maxPerRun, host);

    /// <summary>⭐⭐ Permanently on the card: "sent" is the SERVER's acceptance and nothing more (§60.1).</summary>
    public static string AcceptedNotDelivered => Word(nameof(AcceptedNotDelivered));

    // ── While it runs ───────────────────────────────────────────────────────────────────────────────

    // ── What the FOLDED panel's header reports ──────────────────────────────────────────────────────
    // ⭐ Three answers in the order that matters to somebody who folded the panel: a run in flight
    //   outranks a finished one, and both outrank "this is what would go out".

    /// <summary>A series is going out right now.</summary>
    public static string HeaderSending(int completed, int total) =>
        Say(nameof(HeaderSending), Number(completed), Number(total));

    /// <summary>A series finished and its report is inside.</summary>
    public static string HeaderFinished(int sent, int planned) =>
        Say(nameof(HeaderFinished), Number(sent), Number(planned));

    /// <summary>Nothing has run, but something would go out if it did.</summary>
    public static string HeaderReady(int messages) => Counted(nameof(HeaderReady), messages);

    /// <summary>Which message is in flight, and to whom.</summary>
    /// <remarks>
    /// ⚠ The position is <c>Completed + 1</c>: <c>Completed</c> counts FINISHED attempts, so the message
    /// being sent is not among them (§60.7). ⛔ Do not "fix" that in the snapshot.
    /// </remarks>
    public static string SendingNow(string position, string total, string customer, string address) =>
        Say(nameof(SendingNow), position, total, customer, address);

    /// <summary>⭐ Why the bar is standing still. A stationary bar with no explanation reads as a hung window.</summary>
    public static string WaitingSeconds(string seconds) => Say(nameof(WaitingSeconds), seconds);

    /// <summary>The operator asked to stop and the attempt in flight is being allowed to finish.</summary>
    public static string Stopping => Word(nameof(Stopping));

    /// <summary>How many the server has accepted so far.</summary>
    public static string ProgressSent(int sent) => Counted(nameof(ProgressSent), sent);

    // ── The report ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Nothing qualified, so nothing was attempted.</summary>
    public static string ConclusionNothingToSend => Word(nameof(ConclusionNothingToSend));

    /// <summary>Every attempted message was accepted.</summary>
    public static string ConclusionCompleted => Word(nameof(ConclusionCompleted));

    /// <summary>🔒 K1: a message was refused, so the run stopped.</summary>
    public static string ConclusionStoppedAfterError => Word(nameof(ConclusionStoppedAfterError));

    /// <summary>⭐ The operator stopped it — said in those words, never as a generic failure.</summary>
    public static string ConclusionStoppedByOperator => Word(nameof(ConclusionStoppedByOperator));

    /// <summary>How many attempts the server refused. ⚠ Under K1 this is never more than one.</summary>
    public static string ReportFailed(int failed) => Counted(nameof(ReportFailed), failed);

    /// <summary>How many licences were deliberately not attempted.</summary>
    public static string ReportSkipped(int skipped) => Counted(nameof(ReportSkipped), skipped);

    /// <summary>How many the run never reached.</summary>
    public static string ReportNotAttempted(int notAttempted) =>
        Counted(nameof(ReportNotAttempted), notAttempted);

    /// <summary>How long the whole run took.</summary>
    public static string ElapsedTime(string duration) => Say(nameof(ElapsedTime), duration);

    /// <summary>Opens the per-licence detail.</summary>
    public static string ShowDetails(int attempts) => Say(nameof(ShowDetails), attempts);

    /// <summary>Closes it again.</summary>
    public static string HideDetails(int attempts) => Say(nameof(HideDetails), attempts);

    // ── The four outcomes, as words ─────────────────────────────────────────────────────────────────
    // ⭐ The card shows an ICON per row; these are what the COPIED report says, where there is no icon to
    //   read. ⛔ "Sent" is the server's acceptance — see AcceptedNotDelivered.

    /// <summary>The server accepted it.</summary>
    public static string OutcomeSent => Word(nameof(OutcomeSent));

    /// <summary>The server refused it.</summary>
    public static string OutcomeFailed => Word(nameof(OutcomeFailed));

    /// <summary>In the run, deliberately not attempted.</summary>
    public static string OutcomeSkipped => Word(nameof(OutcomeSkipped));

    /// <summary>The run stopped before reaching it.</summary>
    public static string OutcomeNotAttempted => Word(nameof(OutcomeNotAttempted));

    // ── The copied report's column headings ─────────────────────────────────────────────────────────

    /// <summary>Which of the four happened.</summary>
    public static string ColumnStatus => Word(nameof(ColumnStatus));

    /// <summary>Whose licence it is.</summary>
    public static string ColumnCustomer => Word(nameof(ColumnCustomer));

    /// <summary>Where the message went, or would have gone.</summary>
    public static string ColumnEmail => Word(nameof(ColumnEmail));

    /// <summary>The full <c>lid</c>. ⚠ Never the shortened one — a report is correlated against the register.</summary>
    public static string ColumnLicenceId => Word(nameof(ColumnLicenceId));

    /// <summary>When the attempt finished.</summary>
    public static string ColumnTime => Word(nameof(ColumnTime));

    /// <summary>OUR sentence about the row.</summary>
    public static string ColumnReason => Word(nameof(ColumnReason));

    /// <summary>⛔ The SERVER's words, verbatim — never translated.</summary>
    public static string ColumnServerMessage => Word(nameof(ColumnServerMessage));

    // ── Durations ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Hours and whole minutes.</summary>
    public static string DurationHoursMinutes(string hours, string minutes) =>
        Say(nameof(DurationHoursMinutes), hours, minutes);

    /// <summary>Minutes and whole seconds — the shape §60.8 draws.</summary>
    public static string DurationMinutesSeconds(string minutes, string seconds) =>
        Say(nameof(DurationMinutesSeconds), minutes, seconds);

    /// <summary>Seconds alone.</summary>
    public static string DurationSecondsOnly(string seconds) =>
        Say(nameof(DurationSecondsOnly), seconds);

    /// <summary>
    /// A span in the three shapes an operator reads, and never in more precision than it deserves.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>internal</c> because it merely DISPATCHES to the three members above. The convention
    /// <c>EveryPublicCatalogMember_NamesARealEntry</c> rests on is that a PUBLIC catalog member names a
    /// key, so a member that names none says so through its accessibility.
    /// ⚠ The numbers are handed over already rendered and invariant — they are counts, not quantities a
    /// resource value's format specifier should be able to reach.
    /// </remarks>
    internal static string Duration(TimeSpan span)
    {
        var total = span < TimeSpan.Zero ? TimeSpan.Zero : span;

        var hours = (int)total.TotalHours;

        if (hours > 0)
        {
            return DurationHoursMinutes(Number(hours), Number(total.Minutes));
        }

        return (int)total.TotalMinutes > 0
            ? DurationMinutesSeconds(Number((int)total.TotalMinutes), Number(total.Seconds))
            : DurationSecondsOnly(Number(total.Seconds));
    }

    /// <summary>The word for one of the four outcomes. ⚠ <c>internal</c> — it dispatches, it names no key.</summary>
    internal static string Outcome(BulkSendOutcome outcome) => outcome switch
    {
        BulkSendOutcome.Sent => OutcomeSent,
        BulkSendOutcome.Failed => OutcomeFailed,
        BulkSendOutcome.Skipped => OutcomeSkipped,
        _ => OutcomeNotAttempted,
    };

    /// <summary>The whole sentence for a conclusion. ⚠ <c>internal</c> — it dispatches, it names no key.</summary>
    internal static string Conclusion(BulkSendConclusion conclusion) => conclusion switch
    {
        BulkSendConclusion.NothingToSend => ConclusionNothingToSend,
        BulkSendConclusion.Completed => ConclusionCompleted,
        BulkSendConclusion.StoppedAfterError => ConclusionStoppedAfterError,
        _ => ConclusionStoppedByOperator,
    };

    /// <summary>A count as the invariant digits it is. ⚠ Not words — see <see cref="Duration"/>'s remarks.</summary>
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
