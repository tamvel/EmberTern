using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The settings a bulk send would use, or the sentence explaining why there are none.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It carries the refusal as a VALUE rather than announcing it.</b> The preview is rebuilt on
/// every keystroke in the search box, so a producer that put its own refusal on the message strip would
/// re-raise the same warning dozens of times while the operator typed. ⛔ And two producers — a quiet one
/// for the preview and a loud one for the send — would be two answers to one question, which is how two
/// surfaces start disagreeing about whether e-mail is configured.</para>
/// <para>⚠ <c>PasswordUnavailable</c> is deliberately NOT a refusal, exactly as it is not one on the single
/// send path: the message can still be composed, and an attempt will fail with the SERVER's own words
/// rather than with our guess about them.</para>
/// </remarks>
public sealed record BulkSendSettings
{
    /// <summary>What the run would use, or <see langword="null"/> when it cannot run at all.</summary>
    public SmtpSettings? Settings { get; init; }

    /// <summary>Why there are none — never blank when <see cref="Settings"/> is null.</summary>
    public StatusMessage? Refusal { get; init; }

    /// <summary>Whether a run is possible as far as the configuration is concerned.</summary>
    public bool IsReady => Settings is not null;

    /// <summary>The settings are there.</summary>
    public static BulkSendSettings Ready(SmtpSettings settings) =>
        new() { Settings = settings ?? throw new ArgumentNullException(nameof(settings)) };

    /// <summary>They are not, and this is why.</summary>
    public static BulkSendSettings Refused(StatusMessage refusal) =>
        new() { Refusal = refusal ?? throw new ArgumentNullException(nameof(refusal)) };
}

/// <summary>
/// One line of the bulk-send PREVIEW, already in the words the operator reads.
/// </summary>
/// <remarks>
/// <para>⭐ The presentation layer over <see cref="BulkSendCandidate"/>, exactly as
/// <see cref="BatchRenewalRow"/> is over <c>BatchRenewalCandidate</c>. ⚠ No Avalonia types (Architecture
/// rule 1).</para>
/// <para>⚠⚠ It is a <c>record</c> and raises no <c>PropertyChanged</c>, so a template bound to
/// <see cref="Reason"/> renders once and FREEZES in that language (gotcha #401). The rows are therefore
/// REBUILT on a language change — <see cref="BulkSendViewModel.Rebuild"/> — never notified.</para>
/// </remarks>
public sealed record BulkSendRow
{
    /// <summary>The judgement this row presents.</summary>
    public required BulkSendCandidate Candidate { get; init; }

    /// <summary>Whose licence it is.</summary>
    public string CustomerName => Candidate.CustomerName;

    /// <summary>Where the message would go.</summary>
    public string Address => Candidate.Address;

    /// <summary>The licence id, shortened for a list.</summary>
    public string ShortId => Candidate.ShortId;

    /// <summary>The full <c>lid</c>.</summary>
    public string LicenseId => Candidate.LicenseId;

    /// <summary>
    /// Why this licence is held, or why it is being skipped — ⭐ never blank on a row that has one.
    /// </summary>
    /// <remarks>⚠ Rendered on READ, from the candidate's key and arguments (L8.2's shape).</remarks>
    public string Reason => (Candidate.Hold ?? Candidate.SkipReason)?.ToString() ?? string.Empty;

    /// <summary>Whether there is a reason to show.</summary>
    public bool HasReason => Candidate.Hold is not null || Candidate.SkipReason is not null;

    /// <summary>Builds a row.</summary>
    public static BulkSendRow From(BulkSendCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new BulkSendRow { Candidate = candidate };
    }
}

/// <summary>
/// One line of the REPORT, already in the words the operator reads.
/// </summary>
/// <remarks>
/// ⚠⚠ Same rule as <see cref="BulkSendRow"/>, and it matters more here: the report is the longest-lived
/// text this application puts on screen, so it is the one most likely to still be showing when the
/// language changes. It is rebuilt from <see cref="BulkSendResult"/>, which holds keys and arguments.
/// ⛔ <see cref="ServerMessage"/> is the exception and stays in the server's own words, always.
/// </remarks>
public sealed record BulkSendReportRow
{
    // ⚠ A TIME OF DAY, invariant, matching every other date this application shows (§36.2 / terminology.md
    //   §4.4). ⭐ Recorded in DatePresentationTests.DeliberateIsoDisplayPaths rather than hidden from it.
    private const string TimeFormat = "HH:mm:ss";

    /// <summary>What the run recorded.</summary>
    public required BulkSendAttempt Attempt { get; init; }

    /// <summary>Which of the four happened.</summary>
    public BulkSendOutcome Outcome => Attempt.Outcome;

    /// <summary>Whose licence it is.</summary>
    public string CustomerName => Attempt.CustomerName;

    /// <summary>Where the message went, or would have gone.</summary>
    public string Address => Attempt.Address;

    /// <summary>The licence id, shortened for a list.</summary>
    public string ShortId => Attempt.ShortId;

    /// <summary>The full <c>lid</c> — what a copied report is correlated against.</summary>
    public string LicenseId => Attempt.LicenseId;

    /// <summary>When the attempt finished, or empty when none was made.</summary>
    public string Time =>
        Attempt.At?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>OUR sentence about the row, resolved on read.</summary>
    public string Reason => Attempt.Reason?.ToString() ?? string.Empty;

    /// <summary>Whether there is one.</summary>
    public bool HasReason => Attempt.Reason is not null;

    /// <summary>⛔ The SERVER's words, verbatim. Never translated, never interpreted, never rewritten.</summary>
    public string ServerMessage => Attempt.ServerMessage ?? string.Empty;

    /// <summary>Whether the server said anything.</summary>
    public bool HasServerMessage => !string.IsNullOrWhiteSpace(Attempt.ServerMessage);

    /// <summary>The outcome as a word — what the COPIED report says where the card shows an icon.</summary>
    public string OutcomeLabel => BulkSendCatalog.Outcome(Attempt.Outcome);

    /// <summary>
    /// ⭐ Which of the four icons the row wears, as a RESOURCE KEY.
    /// </summary>
    /// <remarks>
    /// ⚠ A key string, never a <c>Geometry</c> — Architecture rule 1 keeps Avalonia types out of a view
    /// model, and the product's own <c>IconResourceKey</c> established exactly this shape. ⚠ The four
    /// geometries are stroke-drawn (§60.1), which is the card's business, not this one's.
    /// </remarks>
    public string IconKey => Attempt.Outcome switch
    {
        BulkSendOutcome.Sent => "Icon.Check",
        BulkSendOutcome.Failed => "Icon.X",
        BulkSendOutcome.Skipped => "Icon.Minus",
        _ => "Icon.Stop",
    };

    /// <summary>⭐ Which theme token paints it, as a key — same rule as <see cref="IconKey"/>.</summary>
    public string BrushKey => Attempt.Outcome switch
    {
        BulkSendOutcome.Sent => "ConnectedBrush",
        BulkSendOutcome.Failed => "ErrorBrush",
        _ => "SubtleForegroundBrush",
    };

    /// <summary>Builds a row.</summary>
    public static BulkSendReportRow From(BulkSendAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return new BulkSendReportRow { Attempt = attempt };
    }
}

/// <summary>
/// Sending many licences by e-mail, one message at a time, at a pace the operator can interrupt.
///
/// <para>⭐⭐ <b>Three models, never one</b> (§60.2): the PLAN is what we intend, the PROGRESS is what is
/// happening, the RESULT is what happened. This view model owns none of the three — it builds the plan
/// from the browser's ticks, hands it to <see cref="BulkSendRun"/>, and presents what comes back. ⛔ It
/// composes no message of its own accounting: every counter on screen is read off
/// <see cref="BulkSendResult.Attempts"/>, so <i>"we sent 40"</i> cannot be said when 36 succeeded.</para>
///
/// <para>⭐⭐ <b>Semantic atomicity, exactly as <see cref="BatchRenewalViewModel"/> has it.</b> The plan is
/// measured AGAIN when the operator clicks, and the run starts only if the fresh plan is the one they
/// read (<see cref="BulkSendPlan.Matches"/>). ⚠ Here the comparison covers the e-mail settings too, so a
/// host or a pacing changed in another window between the preview and the click is a refusal rather than a
/// surprise.</para>
///
/// <para>⭐⭐ <b>Everything is composed before anything leaves</b> (§60.4 step 4) — the shape
/// <c>IssuingWorkflow.IssueBatch</c> established for signing, moved onto sending. A licence that cannot be
/// composed stops the whole run while nothing has been sent, and it is named.</para>
///
/// <para>⛔ <b>No retry, no jitter, no classification of an SMTP error, no CC, no BCC, no merging of two
/// licences into one message, no run history, no way around the run limit</b> (§60.0). None of these may
/// be added without stopping and putting the decision to the user.</para>
///
/// <para>⚠ No Avalonia types (Architecture rule 1): the confirmation, the clipboard and the progress
/// marshalling all arrive as delegates.</para>
/// </summary>
public sealed partial class BulkSendViewModel : ObservableObject
{
    private readonly LicenseRegister _register;
    private readonly LicenseBrowserViewModel _browser;
    private readonly Func<BulkSendSettings> _settings;
    private readonly Action<StatusMessage> _report;
    private readonly Func<SmtpSettings, ILicenseEmailSender> _senderFactory;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly Func<DateTimeOffset>? _clock;
    private readonly Func<Action<BulkSendProgress>, IProgress<BulkSendProgress>> _progressFactory;
    private readonly Func<IssuedArtifactRecord, CustomerRecord, SmtpSettings, LicenseMessage> _composer;

    private BulkSendPlan? _previewed;
    private StatusMessage? _settingsRefusal;
    private BulkSendResult? _result;
    private StatusMessage? _resultMessage;
    private CancellationTokenSource? _stop;

    /// <summary>Creates the bulk-send surface over the browser's ticked set.</summary>
    /// <param name="register">The register of record — read for the plan, written only by the run.</param>
    /// <param name="browser">⭐ The ONE answer to "which licences is this about?" — its ticks, never a second selection.</param>
    /// <param name="settings">
    /// ⭐ Reads the e-mail settings FRESH from the file, and reports the same refusals the single send path
    /// reports. ⚠ Called only when something is ticked (see <see cref="BuildPlan"/>).
    /// </param>
    /// <param name="report">Where a message goes — the shell owns the one strip this application has.</param>
    /// <param name="senderFactory">⭐ The transport seam, so a test can refuse without a network.</param>
    /// <param name="delay">The pacing seam. ⛔ A suite that actually waited 15 seconds a message is unusable.</param>
    /// <param name="clock">The clock seam, so "how long it took" is a decision a test can make.</param>
    /// <param name="progressFactory">
    /// ⭐⭐ How a snapshot reaches the interface. It defaults to <see cref="Progress{T}"/>, whose whole
    /// purpose here is to marshal onto the thread that constructed it — with no Avalonia type in sight.
    /// ⚠⚠ <c>Progress&lt;T&gt;</c> delivers ASYNCHRONOUSLY, so a report can arrive after the awaited run has
    /// returned; a test that needs the sequence to be deterministic passes a synchronous
    /// <see cref="IProgress{T}"/> here. ⛔ The production default is not weakened to make that easier.
    /// </param>
    /// <param name="composer">
    /// ⭐⭐ How a message is built, defaulting to <see cref="LicenseMessageComposer.Compose"/> — the same
    /// authority the single send path uses.
    /// <para>⚠⚠ <b>It is a seam because the promise it guards is otherwise UNPROVABLE.</b> The planner's
    /// fourth condition asks <see cref="LicenseMessageComposer.Problems"/>, so a licence the composer would
    /// refuse is HELD before it ever reaches composition — and anything that could change that between the
    /// re-plan and the composition also changes the plan, which <see cref="BulkSendPlan.Matches"/> refuses
    /// first. There is therefore no register state that reaches this failure. §60.4's promise —
    /// <i>nothing leaves if any message cannot be built</i> — is still a promise, and a promise nothing can
    /// exercise is one nobody knows is broken. ⛔ <c>LicenseMessageComposer</c> itself is untouched (§60.7).</para>
    /// </param>
    public BulkSendViewModel(
        LicenseRegister register,
        LicenseBrowserViewModel browser,
        Func<BulkSendSettings> settings,
        Action<StatusMessage> report,
        Func<SmtpSettings, ILicenseEmailSender>? senderFactory = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null,
        Func<Action<BulkSendProgress>, IProgress<BulkSendProgress>>? progressFactory = null,
        Func<IssuedArtifactRecord, CustomerRecord, SmtpSettings, LicenseMessage>? composer = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _senderFactory = senderFactory ?? (s => new SmtpLicenseEmailSender(s));
        _delay = delay;
        _clock = clock;
        _progressFactory = progressFactory ?? (post => new Progress<BulkSendProgress>(post));
        _composer = composer ?? LicenseMessageComposer.Compose;

        // ⭐ The preview follows the ticks, always — a preview the operator has to remember to refresh will
        //    eventually describe a different operation from the one about to run.
        _browser.CheckedChanged += (_, _) => Rebuild();

        // ⚠ Weak + static handler: Loc.LanguageChanged is a static event, i.e. a GC root.
        // ⭐ REBUILT rather than notified — both row types are records and raise nothing (#401).
        LanguageChange.SubscribeWeak(this, static card =>
        {
            card.Rebuild();
            card.RebuildReport();
            card.AnnounceResult();
            card.AnnounceProgress();
        });

        Rebuild();
    }

    // ── The preview ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The FULL recipient list (§14.1) — every message that would actually be attempted.</summary>
    public ObservableCollection<BulkSendRow> Sendable { get; } = [];

    /// <summary>Every ticked licence that never becomes a message, each with its reason. ⛔ Named, never dropped.</summary>
    public ObservableCollection<BulkSendRow> Held { get; } = [];

    /// <summary>
    /// Whether licences already sent since their current artifact was issued are left alone.
    /// </summary>
    /// <remarks>
    /// 🔒 Default ON. ⭐ It is only safe as a default because "already sent" is measured against the CURRENT
    /// artifact's <c>iat</c> — without that, every renewal would be skipped because the licence had a
    /// message a year ago (§60.7).
    /// </remarks>
    [ObservableProperty]
    private bool _skipAlreadySent = true;

    /// <summary>⭐ An optional remark stored on the run's own <c>licence.batch-sent</c> audit line.</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>Whether there is a preview to read.</summary>
    public bool HasPreview => _previewed is { IsEmpty: false };

    /// <summary>Whether the run may start.</summary>
    public bool CanSend => !IsSending && _previewed is { CanExecute: true };

    /// <summary>Whether anything is being held back.</summary>
    public bool HasHeld => Held.Count > 0;

    /// <summary>Whether anything would actually be attempted.</summary>
    public bool HasSendable => Sendable.Count > 0;

    /// <summary>
    /// What the run would do, in whole sentences — how many messages, to how many addresses, and the
    /// shortest it can take.
    /// </summary>
    /// <remarks>
    /// ⭐ THREE complete sentences joined by a space, never one sentence with two clauses spliced in: a
    /// counted sentence has exactly one plural pivot, and Polish inflects the message count and the address
    /// count differently. ⚠ When the configuration refuses, this says WHY rather than going blank — a
    /// disabled action with no explanation is the one shape this application does not ship.
    /// </remarks>
    public string PreviewSummary
    {
        get
        {
            if (_settingsRefusal is { } refusal)
            {
                return refusal.Text;
            }

            if (_previewed is not { } plan || plan.IsEmpty)
            {
                return BulkSendCatalog.TickLicences;
            }

            var sendable = plan.Sendable.Count;

            return string.Join(
                ' ',
                BulkSendCatalog.WillSend(sendable),
                BulkSendCatalog.ToAddresses(plan.RecipientCount),
                BulkSendCatalog.AtLeast(BulkSendCatalog.Duration(plan.MinimumDuration)));
        }
    }

    /// <summary>⚠ How many addresses would receive more than one message, or empty. ⛔ Shown, never repaired.</summary>
    public string DuplicateWarning =>
        _previewed is { DuplicateRecipientCount: > 0 } plan
            ? BulkSendCatalog.DuplicateAddresses(plan.DuplicateRecipientCount)
            : string.Empty;

    /// <summary>How many licences are in the run and deliberately not attempted, or empty.</summary>
    public string SkippedSummary =>
        _previewed is { } plan && plan.Skipped.Count > 0
            ? BulkSendCatalog.SkippedCount(plan.Skipped.Count)
            : string.Empty;

    /// <summary>The pacing, the run limit and the server. ⭐ Shown, so "sent" never has to mean "somewhere".</summary>
    public string PacingNote =>
        _previewed is { } plan
            ? BulkSendCatalog.Pacing(
                BulkSendCatalog.Number(plan.DelaySeconds),
                BulkSendCatalog.Number(plan.MaxPerRun),
                plan.Host)
            : string.Empty;

    /// <summary>⭐⭐ Permanent on the card: "sent" is the server's acceptance, not the customer's receipt.</summary>
    public string AcceptanceNote => BulkSendCatalog.AcceptedNotDelivered;

    /// <summary>The section heading over the held licences.</summary>
    public string HeldHeading => BulkSendCatalog.HeldSection(Held.Count);

    /// <summary>The section heading over the full recipient list.</summary>
    public string SendableHeading => BulkSendCatalog.SendableSection(Sendable.Count);

    /// <summary>
    /// ⛔ Why the action is unavailable when the selection is over the run limit — never a dead button.
    /// </summary>
    /// <remarks>
    /// ⚠ It reads <c>Status.BulkOverRunLimit</c>, the same key the strip raises when the command is reached
    /// anyway: the card and the strip must say the same thing, and two keys is how they stop doing so —
    /// the arrangement <c>BatchRenewalViewModel.BlockerSummary</c> already uses.
    /// </remarks>
    public string LimitWarning =>
        _previewed is { ExceedsRunLimit: true } plan
            ? Loc.FormatCount(
                StatusCatalog.BulkOverRunLimit.Value,
                plan.Sendable.Count,
                BulkSendCatalog.Number(plan.MaxPerRun))
            : string.Empty;

    /// <summary>Whether the selection is over the run limit.</summary>
    public bool ExceedsRunLimit => _previewed is { ExceedsRunLimit: true };

    /// <summary>
    /// Rebuilds the preview from the ticked set, the settings and the skip option.
    /// </summary>
    /// <remarks>
    /// ⚠ Called on every tick and every option change, and again by the command before it acts. ⛔ Its
    /// result is never cached across a user action: the whole point of the previewed plan is to be COMPARED
    /// against a fresh reading, not to save one.
    /// </remarks>
    public void Rebuild()
    {
        _previewed = BuildPlan();

        Sendable.Clear();
        Held.Clear();

        if (_previewed is { } plan)
        {
            foreach (var candidate in plan.Sendable)
            {
                Sendable.Add(BulkSendRow.From(candidate));
            }

            foreach (var candidate in plan.Held)
            {
                Held.Add(BulkSendRow.From(candidate));
            }
        }

        Announce();
    }

    partial void OnSkipAlreadySentChanged(bool value) => Rebuild();

    // ── While it runs ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True from the click until the report exists.
    /// </summary>
    /// <remarks>
    /// ⭐ 🔒 Decision M: it also blocks "Extend and issue". The loop awaits, so the interface stays alive —
    /// and every message is already COMPOSED, so a renewal running underneath would send an artifact that
    /// had stopped being the current one.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private bool _isSending;

    /// <summary>True once the operator asked to stop and the attempt in flight is finishing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private bool _isStopping;

    /// <summary>The newest snapshot, or <see langword="null"/> when nothing is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressTotal))]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    [NotifyPropertyChangedFor(nameof(ProgressLine))]
    [NotifyPropertyChangedFor(nameof(ProgressCounts))]
    private BulkSendProgress? _progress;

    /// <summary>Raised whenever <see cref="IsSending"/> changes — what decision M is wired to.</summary>
    public event EventHandler? SendingChanged;

    /// <summary>Whether the run can still be stopped.</summary>
    public bool CanStop => IsSending && !IsStopping;

    /// <summary>⭐ The bar's <c>Maximum</c> — the messages that will be ATTEMPTED, not the ticks.</summary>
    public int ProgressTotal => Progress?.Total ?? 0;

    /// <summary>⭐⭐ The bar's <c>Value</c> — FINISHED attempts. ⛔ It never moves while waiting.</summary>
    public int ProgressValue => Progress?.Completed ?? 0;

    /// <summary>What is happening, in one line.</summary>
    public string ProgressLine
    {
        get
        {
            if (Progress is not { } snapshot)
            {
                return string.Empty;
            }

            return snapshot.Phase switch
            {
                BulkSendPhase.Sending => BulkSendCatalog.SendingNow(
                    BulkSendCatalog.Number(snapshot.Completed + 1),
                    BulkSendCatalog.Number(snapshot.Total),
                    snapshot.CurrentCustomer ?? string.Empty,
                    snapshot.CurrentAddress ?? string.Empty),

                // ⭐ The bar stands still here, honestly, and this is what explains it.
                BulkSendPhase.Waiting => BulkSendCatalog.WaitingSeconds(
                    BulkSendCatalog.Number(snapshot.SecondsToNext ?? 0)),

                BulkSendPhase.Stopping => BulkSendCatalog.Stopping,
                _ => string.Empty,
            };
        }
    }

    /// <summary>How many have been accepted and how many refused, so far.</summary>
    public string ProgressCounts =>
        Progress is { } snapshot
            ? BulkSendCatalog.ProgressSent(snapshot.Sent) + " · " +
              BulkSendCatalog.ReportFailed(snapshot.Failed)
            : string.Empty;

    // ── The report ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every licence that entered the last run, in order, exactly once.</summary>
    public ObservableCollection<BulkSendReportRow> ReportRows { get; } = [];

    /// <summary>Whether the detail is open. ⭐ A toggled <c>bool</c>, ⛔ not an <c>Expander</c> (§60.8).</summary>
    [ObservableProperty]
    private bool _showDetails;

    /// <summary>⭐ There is a report to read, and it stays until the next run starts.</summary>
    public bool HasResult => _result is not null;

    /// <summary>
    /// The headline — how many of the planned messages were sent.
    /// </summary>
    /// <remarks>
    /// ⭐ Stored as a <see cref="StatusMessage"/> and resolved on READ, the shape
    /// <c>BatchRenewalViewModel.LastResult</c> established: the report is the longest-lived text on this
    /// screen, so storing the rendered sentence would freeze it in the language the run happened to
    /// execute in.
    /// </remarks>
    public string ResultHeadline => _resultMessage?.Text ?? string.Empty;

    /// <summary>⭐ Which of the four conclusions — a whole sentence for each, never a shared vague one.</summary>
    public string ResultConclusion =>
        _result is { } result ? BulkSendCatalog.Conclusion(result.Conclusion) : string.Empty;

    /// <summary>
    /// The counts that are not the headline: refused, skipped, never attempted.
    /// </summary>
    /// <remarks>
    /// ⚠ Counted off <see cref="BulkSendResult.Attempts"/>, never accumulated separately — that is what
    /// keeps <c>Planned == Sent + Failed + Skipped + NotAttempted</c> true on the screen as well as in the
    /// model. ⭐ A count of zero is left out rather than shown as "0".
    /// </remarks>
    public string ResultCounts
    {
        get
        {
            if (_result is not { } result)
            {
                return string.Empty;
            }

            var parts = new List<string>(3);

            if (result.Failed > 0)
            {
                parts.Add(BulkSendCatalog.ReportFailed(result.Failed));
            }

            if (result.Skipped > 0)
            {
                parts.Add(BulkSendCatalog.ReportSkipped(result.Skipped));
            }

            if (result.NotAttempted > 0)
            {
                parts.Add(BulkSendCatalog.ReportNotAttempted(result.NotAttempted));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>How long the run took, from the injected clock.</summary>
    public string ResultElapsed =>
        _result is { } result
            ? BulkSendCatalog.ElapsedTime(BulkSendCatalog.Duration(result.Elapsed))
            : string.Empty;

    /// <summary>The detail toggle's own label, carrying how many lines are behind it.</summary>
    public string DetailsLabel =>
        _result is not { } result
            ? string.Empty
            : ShowDetails
                ? BulkSendCatalog.HideDetails(result.Planned)
                : BulkSendCatalog.ShowDetails(result.Planned);

    partial void OnShowDetailsChanged(bool value) => OnPropertyChanged(nameof(DetailsLabel));

    // ── The platform seams ──────────────────────────────────────────────────────────────────────────

    /// <summary>Asks the operator to confirm. Assigned by the view.</summary>
    public Func<ConfirmRequest, Task<bool>>? Confirm { get; set; }

    /// <summary>Puts text on the clipboard. Assigned by the view.</summary>
    public Func<string, Task>? TextCopier { get; set; }

    // ── Actions ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends every message the preview promised, in the order it showed them.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ The six steps of §60.4, in order and none of them optional: re-plan · refuse if the plan
    /// moved · compose EVERYTHING · confirm · run · report.</para>
    /// <para>⛔ <b>With no confirmer wired it REFUSES rather than proceeding</b> — the rule L6.1a's
    /// <c>Forget settings</c> established: an outward-facing act must not lose its guard because a view
    /// forgot to attach one, with every test still green.</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        var approved = _previewed;

        // ⭐⭐ SEMANTIC ATOMICITY. Measured AGAIN here, and the run starts only if this is the operation the
        //    operator read. A tick, an option or an e-mail setting that moved in between produces a
        //    different plan, and running that one would mean the preview described something else.
        var plan = BuildPlan();

        if (plan is null || approved is null || !plan.Matches(approved))
        {
            Rebuild();
            _report(StatusMessage.Warning(StatusCatalog.BulkPreviewOutOfDate));
            return;
        }

        if (!plan.CanExecute)
        {
            Rebuild();
            _report(plan.ExceedsRunLimit
                ? StatusMessage.Counted(
                    StatusCatalog.BulkOverRunLimit,
                    MessageSeverity.Warning,
                    plan.Sendable.Count,
                    BulkSendCatalog.Number(plan.MaxPerRun))
                : StatusMessage.Warning(StatusCatalog.BulkTickAtLeastOneLicence));
            return;
        }

        var settings = _settings();
        if (settings.Settings is not { } smtp)
        {
            Rebuild();
            _report(settings.Refusal ?? StatusMessage.Warning(StatusCatalog.EmailNotConfigured));
            return;
        }

        // ⭐⭐ STEP 4 — everything composed BEFORE the first send, so a licence that cannot be composed
        //    stops the run while nothing has left. ⚠ Before the confirmation on purpose: a dialog that
        //    opens and then says "actually, no" is one the operator has to close to learn nothing.
        if (!TryComposeAll(plan, smtp, out var composed, out var problem))
        {
            _report(problem!);
            return;
        }

        if (Confirm is null)
        {
            _report(StatusMessage.Warning(StatusCatalog.ConfirmationUnavailableNothingSent));
            return;
        }

        var confirmed = await Confirm(new ConfirmRequest(
            ConfirmCatalog.BulkSendTitle,
            ConfirmCatalog.BulkSendMessage,
            ConfirmCatalog.BulkSendAction,
            plan.Host,
            BulkSendCatalog.Duration(plan.MinimumDuration))
        {
            Count = plan.Sendable.Count,
        }).ConfigureAwait(true);

        if (!confirmed)
        {
            // ⭐ Cancel changes nothing and says nothing — a "cancelled" notice reports the absence of an event.
            return;
        }

        await RunAsync(plan, composed, smtp).ConfigureAwait(true);
    }

    /// <summary>
    /// Asks the run to stop.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ "Send no more", NOT "abort this one". The attempt in flight finishes and is recorded; ⛔ the token
    /// never reaches the sender, because a cancelled SMTP conversation may already have delivered the
    /// message and an audit line claiming otherwise would be a lie (§60.6). ⛔ No second "are you sure":
    /// stopping is the safe direction.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsStopping = true;
        _stop?.Cancel();
    }

    /// <summary>
    /// Copies the report as TSV — the summary first, then one line per licence.
    /// </summary>
    /// <remarks>
    /// 🔒 Decision N. ⭐ TSV rather than prose because the operator's next act is a spreadsheet or a ticket,
    /// and because it is the one format that survives a paste anywhere. ⛔ The server's words travel
    /// untranslated, exactly as they do on screen.
    /// </remarks>
    [RelayCommand]
    private async Task CopyReportAsync()
    {
        if (_result is null || TextCopier is null)
        {
            return;
        }

        await TextCopier(BuildReportText()).ConfigureAwait(true);
        _report(StatusMessage.Success(StatusCatalog.BulkReportCopied));
    }

    // ── The run ─────────────────────────────────────────────────────────────────────────────────────

    private async Task RunAsync(
        BulkSendPlan plan, IReadOnlyDictionary<string, LicenseMessage> composed, SmtpSettings smtp)
    {
        // ⚠ The previous report goes the moment a new run starts. Two reports on one card, one of them
        //   stale, is worse than none.
        SetResult(null, null);

        _stop = new CancellationTokenSource();
        IsStopping = false;
        SetSending(true);

        BulkSendResult result;
        try
        {
            var run = new BulkSendRun(_register, new LicenceDelivery(_register), _delay, _clock);

            result = await run.ExecuteAsync(
                plan,
                composed,
                _senderFactory(smtp),
                Blank(Note),
                _progressFactory(snapshot => Progress = snapshot),
                _stop.Token).ConfigureAwait(true);
        }
        finally
        {
            SetSending(false);
            IsStopping = false;
            Progress = null;
            _stop.Dispose();
            _stop = null;
        }

        Complete(result);
    }

    // ⭐ Everything below runs only after the run finished, so every sentence here is true.
    private void Complete(BulkSendResult result)
    {
        SetResult(result, StatusMessage.Counted(
            StatusCatalog.BulkRunSentOfPlanned, SeverityOf(result), result.Sent, result.Planned));

        _report(_resultMessage!);

        // ⭐⭐ 🔒 Decision L — the ticks come off the SENT licences and off nothing else. A failure, a skip
        //    and an untouched licence all stay ticked, so "resume" is: fix the problem, click again, and
        //    only what is left goes out. ⛔ Nobody receives a duplicate.
        // ⚠ Deliberately different from the batch renewal, which clears everything — that operation is
        //   atomic and this one cannot be. That difference IS the rule.
        _browser.Untick(result.SentIds);

        Note = string.Empty;
        Rebuild();
    }

    private static MessageSeverity SeverityOf(BulkSendResult result) => result.Conclusion switch
    {
        BulkSendConclusion.Completed => MessageSeverity.Success,
        BulkSendConclusion.StoppedAfterError => MessageSeverity.Error,
        _ => MessageSeverity.Warning,
    };

    /// <summary>
    /// Composes every message the run will send, and refuses the whole run if one of them cannot be built.
    /// </summary>
    /// <remarks>
    /// ⚠ The planner already asked <see cref="LicenseMessageComposer.Problems"/> for each of these, so this
    /// should never refuse. ⭐ It is written as if it will anyway: the state can move between the plan and
    /// the composition, and the promise being kept — <i>nothing leaves if anything cannot be built</i> — is
    /// worth more than the assumption that it cannot happen.
    /// </remarks>
    private bool TryComposeAll(
        BulkSendPlan plan,
        SmtpSettings settings,
        out IReadOnlyDictionary<string, LicenseMessage> composed,
        out StatusMessage? problem)
    {
        var messages = new Dictionary<string, LicenseMessage>(StringComparer.Ordinal);

        foreach (var candidate in plan.Sendable)
        {
            // ⚠ Both are non-null on a sendable candidate by construction: a licence with no current
            //   artifact or no customer is HELD, and a held candidate never reaches this list.
            var artifact = candidate.CurrentArtifact!;
            var customer = candidate.Customer!;

            try
            {
                messages[candidate.LicenseId] = _composer(artifact, customer, settings);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                // ⭐ The composer's own sentences where it has them — one authority about what can be sent
                //   — and its English diagnostic only where it does not. ⛔ Never our own words in code.
                var problems = LicenseMessageComposer.Problems(artifact, customer, settings);

                composed = messages;
                problem = StatusMessage.Error(
                    StatusCatalog.BulkComposeFailed,
                    candidate.CustomerName,
                    candidate.ShortId,
                    problems.Count > 0 ? new LocalizedSentences(problems) : e.Message);

                return false;
            }
        }

        composed = messages;
        problem = null;
        return true;
    }

    /// <summary>
    /// Reads the register and the settings, and plans what the ticked licences would receive.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>The licences are READ HERE, not taken from the browser's rows.</b> Those rows are a
    /// snapshot taken when a licence was ticked; planning from them would plan against whatever the
    /// register held at that moment — and, worse, would make <see cref="BulkSendPlan.Matches"/> vacuous,
    /// because both readings would come from one cache and could never disagree (#378).</para>
    /// <para>⚠ Neither the register nor the settings file is touched while nothing is ticked, which is the
    /// state the licences view spends almost all of its time in — so typing in the search box costs
    /// nothing until the operator has actually started selecting.</para>
    /// <para>⭐ <c>GetLastSentAt</c> is ONE query for the whole register (§60.7). ⛔ Never a per-licence
    /// audit read: <c>audit_log</c> has no index for it, and <c>GetAudit</c>'s default limit would truncate
    /// the answer in silence.</para>
    /// </remarks>
    private BulkSendPlan? BuildPlan()
    {
        var ticked = _browser.CheckedIds;
        if (ticked.Count == 0)
        {
            _settingsRefusal = null;
            return null;
        }

        var settings = _settings();
        _settingsRefusal = settings.Refusal;

        if (settings.Settings is not { } smtp)
        {
            return null;
        }

        var wanted = new HashSet<string>(ticked, StringComparer.Ordinal);

        // ⭐ In the register's own order — soonest expiry first, then customer name — so the preview reads
        //   the same way twice and two plans are comparable position by position.
        var selected = _register.QueryLicenses()
            .Where(summary => wanted.Contains(summary.License.LicenseId))
            .ToList();

        return BulkSendPlanner.Plan(
            selected,
            _register.GetCurrentArtifact,
            _register.GetCustomer,
            _register.GetLastSentAt(),
            smtp,
            _clock?.Invoke() ?? DateTimeOffset.UtcNow,
            SkipAlreadySent);
    }

    // ── The copied report ───────────────────────────────────────────────────────────────────────────

    /// <summary>The report as TSV — ⭐ the summary first, then the detail, exactly as decision N asks.</summary>
    /// <remarks>
    /// ⚠ <c>internal</c> so the guard can read the produced text rather than the clipboard: what matters is
    /// what the operator pastes, not that a delegate was called.
    /// </remarks>
    internal string BuildReportText()
    {
        if (_result is not { } result)
        {
            return string.Empty;
        }

        var text = new StringBuilder();

        text.AppendLine(ResultHeadline);
        text.AppendLine(ResultConclusion);

        if (ResultCounts.Length > 0)
        {
            text.AppendLine(ResultCounts);
        }

        text.AppendLine(ResultElapsed);
        text.AppendLine();

        text.AppendLine(string.Join(
            '\t',
            BulkSendCatalog.ColumnStatus,
            BulkSendCatalog.ColumnCustomer,
            BulkSendCatalog.ColumnEmail,
            BulkSendCatalog.ColumnLicenceId,
            BulkSendCatalog.ColumnTime,
            BulkSendCatalog.ColumnReason,
            BulkSendCatalog.ColumnServerMessage));

        foreach (var attempt in result.Attempts)
        {
            var row = BulkSendReportRow.From(attempt);

            text.AppendLine(string.Join(
                '\t',
                Cell(row.OutcomeLabel),
                Cell(row.CustomerName),
                Cell(row.Address),

                // ⚠ The FULL id, never the shortened one: a copied report is correlated against the
                //   register, and an ellipsis is a presentation decision (LicenceIdText).
                Cell(row.LicenseId),
                Cell(row.Time),
                Cell(row.Reason),

                // ⛔ The server's words, verbatim — the one cell that is not ours to translate.
                Cell(row.ServerMessage)));
        }

        return text.ToString();
    }

    // ⚠ A tab or a newline inside a value would silently shift every following column, so both become a
    //   space. ⛔ Nothing is truncated and nothing is dropped: a report that quietly loses a server's
    //   sentence is worse than one with a long cell.
    private static string Cell(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // ── Bookkeeping ─────────────────────────────────────────────────────────────────────────────────

    private void SetSending(bool sending)
    {
        IsSending = sending;
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetResult(BulkSendResult? result, StatusMessage? message)
    {
        _result = result;
        _resultMessage = message;
        ShowDetails = false;
        RebuildReport();
        AnnounceResult();
    }

    // ⭐ REBUILT from the result's keys and arguments, never notified: BulkSendAttempt is a record and
    //   raises no PropertyChanged, so a template bound to one of its properties renders once and freezes
    //   in that language (#401).
    private void RebuildReport()
    {
        ReportRows.Clear();

        if (_result is not { } result)
        {
            return;
        }

        foreach (var attempt in result.Attempts)
        {
            ReportRows.Add(BulkSendReportRow.From(attempt));
        }
    }

    private void Announce()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(HasHeld));
        OnPropertyChanged(nameof(HasSendable));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(DuplicateWarning));
        OnPropertyChanged(nameof(SkippedSummary));
        OnPropertyChanged(nameof(PacingNote));
        OnPropertyChanged(nameof(AcceptanceNote));
        OnPropertyChanged(nameof(HeldHeading));
        OnPropertyChanged(nameof(SendableHeading));
        OnPropertyChanged(nameof(LimitWarning));
        OnPropertyChanged(nameof(ExceedsRunLimit));
        SendCommand.NotifyCanExecuteChanged();
    }

    private void AnnounceResult()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultHeadline));
        OnPropertyChanged(nameof(ResultConclusion));
        OnPropertyChanged(nameof(ResultCounts));
        OnPropertyChanged(nameof(ResultElapsed));
        OnPropertyChanged(nameof(DetailsLabel));
    }

    private void AnnounceProgress()
    {
        OnPropertyChanged(nameof(ProgressLine));
        OnPropertyChanged(nameof(ProgressCounts));
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
