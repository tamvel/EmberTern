using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One line of the batch preview, already in the words the operator reads.
///
/// <para>⭐ The presentation layer over <see cref="BatchRenewalCandidate"/>, exactly as
/// <c>LicenseListItem</c> is over <c>LicenseSummary</c>. It exists so the reason reaches the screen
/// through <see cref="ReasonText"/> — the ONE mapping the history and the single-issue picker already
/// use — rather than through a second switch that could call the same fact something else.</para>
///
/// <para>⚠ No Avalonia types (Architecture rule 1).</para>
/// </summary>
public sealed record BatchRenewalRow
{
    /// <summary>The judgement this row presents.</summary>
    public required BatchRenewalCandidate Candidate { get; init; }

    /// <summary>Whose licence it is.</summary>
    public string CustomerName => Candidate.CustomerName;

    /// <summary>The licence id, shortened for a list.</summary>
    public string ShortId => Candidate.ShortId;

    /// <summary>What would happen to the expiry, both ends of it.</summary>
    public string Change => $"{Candidate.CurrentExpiry} → {Candidate.NewExpiry}";

    /// <summary>The reason that would be RECORDED, in words — through the one shared mapping.</summary>
    public string ReasonLabel => ReasonText.Describe(Candidate.Reason);

    /// <summary>⭐ Whether this licence would receive its first artifact ever (D‑2).</summary>
    public bool IsFirstIssue => Candidate.IsFirstIssue;

    /// <summary>Whether this licence would be extended.</summary>
    public bool Qualifies => Candidate.Qualifies;

    /// <summary>⭐ Why not, when not — never blank on a blocked row (D‑3).</summary>
    /// <remarks>
    /// ⚠ Rendered on read (L8.2): the candidate holds the KEY, and this projects it to the words the row
    /// shows. ⏭ The preview grid is rebuilt on every tick, so it needs no notification of its own.
    /// </remarks>
    public string Blocker => Candidate.Blocker?.ToString() ?? string.Empty;

    /// <summary>Whether there is a blocker to show.</summary>
    public bool IsBlocked => !Candidate.Qualifies;

    /// <summary>Builds a row.</summary>
    public static BatchRenewalRow From(BatchRenewalCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new BatchRenewalRow { Candidate = candidate };
    }
}

/// <summary>
/// Extending many licences to one date, as one act.
///
/// <para>⭐⭐ <b>The first surface in the License Manager that changes more than one licence, and it is
/// built so that "twenty selected, nineteen done" is unreachable rather than unlikely.</b> Three
/// separate properties carry that:</para>
/// <list type="number">
///   <item><b>Technical atomicity</b> — <c>IssuingWorkflow.IssueBatch</c> signs everything before
///   recording anything, and <c>LicenseRegister.ApplyIssueBatch</c> commits terms, artifacts, pointers,
///   history and the batch line in ONE transaction (§39.1). ⛔ This view model adds no second write
///   path; it prepares requests and hands them over.</item>
///   <item><b>Semantic atomicity</b> — the plan the operator READ is the plan that runs. The command
///   re-plans and refuses if the fresh plan differs in any way from the previewed one, rather than
///   quietly running the new one.</item>
///   <item><b>No partial batch</b> — D‑3: one blocked licence disables the action for all of them.</item>
/// </list>
///
/// <para>⭐ <b>The reason is stated, not picked, and that is D‑1 rather than a shortcut.</b> The operation
/// is an extension, so <c>renewal</c> is the only truthful value for a licence that has one before it,
/// and <c>initial</c> is what the register PROVES about one that does not. Offering a list of four here
/// would be the L5.3 trap in reverse: inviting an untruth where there is only one true answer.</para>
///
/// <para>⛔ <b>It writes no <c>.etlic</c> file (D‑4).</b> The batch ends at a committed register. Export
/// stays the separate, existing action, from the STORED token.</para>
/// </summary>
public sealed partial class BatchRenewalViewModel : ObservableObject
{
    private readonly LicenseRegister _register;
    private readonly IssuingWorkflow _workflow;
    private readonly SigningSession _session;
    private readonly LicenseBrowserViewModel _browser;
    private readonly Action<StatusMessage> _report;

    /// <summary>Creates the batch surface over the browser's ticked set.</summary>
    public BatchRenewalViewModel(
        LicenseRegister register,
        IssuingWorkflow workflow,
        SigningSession session,
        LicenseBrowserViewModel browser,
        Action<StatusMessage> report)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _report = report ?? throw new ArgumentNullException(nameof(report));

        // ⭐ The preview follows the ticks, always. A preview the operator has to remember to refresh is
        //    a preview that will eventually describe a different operation from the one about to run.
        _browser.CheckedChanged += (_, _) => Rebuild();

        // ⚠ Weak + static handler: this view model outlives nothing in particular, and Loc.LanguageChanged
        //   is a static event. See LanguageChange.SubscribeWeak.
        // ⭐ Rebuild rather than notify: `BatchRenewalRow.ReasonLabel` is a computed property on a row
        //   the grid binds directly, and a row raises nothing — so the rows have to be built again.
        //   `Rebuild` also notifies PreviewSummary, which is where the counted sentences live.
        LanguageChange.SubscribeWeak(this, static batch =>
        {
            batch.Rebuild();
            batch.OnPropertyChanged(nameof(LastResult));
            batch.OnPropertyChanged(nameof(BlockerSummary));
        });
    }

    /// <summary>Every ticked licence and what would happen to it — including the ones standing in the way.</summary>
    public ObservableCollection<BatchRenewalRow> Rows { get; } = [];

    /// <summary>
    /// The date every ticked licence would run to.
    ///
    /// <para>⚠ Read as a UTC calendar day running to the END of it, through <see cref="LicenseDay"/> —
    /// the same owner the licence form uses, so the two surfaces cannot differ by a day.</para>
    /// </summary>
    [ObservableProperty]
    private DateTime? _targetDate;

    /// <summary>
    /// ⭐ <b>D‑4.</b> An optional remark stored on the batch's own audit line — a ticket number, who asked,
    /// why now. Same mechanism as the single issue's note, on the one history line a batch already writes.
    /// </summary>
    [ObservableProperty]
    private string _note = string.Empty;

    // ⚠ The OUTCOME, not its rendering (L8.2). Storing the sentence here would freeze the panel in the
    //   language the batch happened to run in — and this text is deliberately long-lived on screen, so it
    //   is the most likely of all of them to still be showing when the language changes.
    private StatusMessage? _lastResultMessage;

    /// <summary>What the last completed batch did, or empty. ⭐ Kept on screen after the message fades.</summary>
    public string LastResult => _lastResultMessage?.Text ?? string.Empty;

    /// <summary>Whether there is a result to show.</summary>
    public bool HasResult => _lastResultMessage is not null;

    // ⚠ The plan the OPERATOR was shown. The command compares a fresh plan against this one, so the
    //   approval is of a specific operation rather than of a moment.
    private BatchRenewalPlan? _previewed;

    /// <summary>Whether a preview exists to read.</summary>
    public bool HasPreview => _previewed is { IsEmpty: false };

    /// <summary>⭐ <b>D‑3.</b> Whether the operation may run: something is ticked, and nothing is blocked.</summary>
    /// <remarks>
    /// ⚠ Since L10.4 it also answers <see langword="false"/> while a bulk send is running — 🔒 decision M,
    /// see <see cref="IsBlockedByBulkSend"/>.
    /// </remarks>
    public bool CanExtend => !IsBlockedByBulkSend && _previewed is { CanExecute: true };

    /// <summary>
    /// ⛔ 🔒 <b>Decision M (§60.11 risk 1).</b> Set while a bulk send is in flight.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The send loop <c>await</c>s, so the interface stays alive throughout — and every one of its
    /// messages was COMPOSED before the first one left (§60.4 step 4). Extending a licence underneath a
    /// running series would therefore mint a new artifact while a message describing the OLD one is still
    /// queued to go out: the customer would receive a licence that had already been superseded, minutes
    /// after it was. ⛔ The answer is to make the two operations mutually exclusive, not to re-compose
    /// mid-run.
    /// </remarks>
    public bool IsBlockedByBulkSend
    {
        get => _isBlockedByBulkSend;
        set
        {
            if (_isBlockedByBulkSend == value)
            {
                return;
            }

            _isBlockedByBulkSend = value;
            OnPropertyChanged();
            Announce();
        }
    }

    private bool _isBlockedByBulkSend;

    /// <summary>Whether anything at all is standing in the way.</summary>
    public bool HasBlockers => _previewed is { } plan && plan.Blocked.Count > 0;

    /// <summary>
    /// What the operation would do, in one line — the count, the date, and how many are first issues.
    ///
    /// <para>⭐ <b>D‑2 requires the first-issue count to be visible</b>: a licence that has never been
    /// issued is not being renewed, and an operator who thinks they are extending twenty existing
    /// customers should be told that three of them are being sent something for the first time.</para>
    /// </summary>
    public string PreviewSummary
    {
        get
        {
            if (_previewed is not { } plan || plan.IsEmpty)
            {
                return TargetDate is null
                    ? BatchCatalog.TickAndChooseDate
                    : BatchCatalog.TickLicences;
            }

            // ⭐ TWO whole sentences, joined here. The first-issue notice used to be a fragment carrying
            //   its own leading space — legible in English and unassignable to a translator. The space is
            //   the JOIN's, so the rendered line is unchanged.
            var extended = BatchCatalog.WouldBeExtended(plan.Qualifying.Count, plan.TargetDay);

            return plan.FirstIssues == 0
                ? extended
                : extended + " " + BatchCatalog.FirstIssues(plan.FirstIssues);
        }
    }

    /// <summary>Why the action is unavailable, in one line — never a disabled button with no explanation.</summary>
    /// <remarks>
    /// ⚠⚠ <b>Each count carries its WHOLE sentence, not a fragment</b> (L8.2). The singular and plural
    /// readings used to be built by concatenating a clause onto a shared tail, which would have handed the
    /// translator half a sentence as an argument — forbidden, because word order is the translator's
    /// decision and Polish does not put the clause where English does.
    /// ⭐ L8.5 collapsed the two keys into ONE plural family, which is what Polish needs — the pair was
    /// only ever a consequence of L8.2 being forbidden to change a single English character.
    /// </remarks>
    public string BlockerSummary
    {
        get
        {
            if (_previewed is not { } plan || plan.Blocked.Count == 0)
            {
                return string.Empty;
            }

            return Loc.FormatCount(StatusCatalog.Blocked.Value, plan.Blocked.Count);
        }
    }

    /// <summary>
    /// Rebuilds the preview from the ticked set and the target date.
    ///
    /// <para>⚠ Called on every tick and every date change, and again by the command before it acts. ⛔ Its
    /// result is never cached across a user action — the whole point of <see cref="_previewed"/> is to be
    /// COMPARED against a fresh reading, not to save one.</para>
    /// </summary>
    public void Rebuild()
    {
        _previewed = BuildPlan();

        Rows.Clear();
        if (_previewed is { } plan)
        {
            foreach (var candidate in plan.Candidates)
            {
                Rows.Add(BatchRenewalRow.From(candidate));
            }
        }

        Announce();
    }

    /// <summary>
    /// Extends every ticked licence to the target date, as one act.
    ///
    /// <para>⭐⭐ The order is the one §39.1 established and this stage does not touch: prepare, sign
    /// everything, commit everything once. A failure anywhere before the commit leaves the register
    /// exactly as it was, and nothing anybody can hold has been produced.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExtend))]
    private void Extend()
    {
        var approved = _previewed;

        // ⭐⭐ SEMANTIC ATOMICITY. The plan is measured again HERE, and the operation runs only if it is
        //    the same operation the operator read. A selection or a date that moved in between produces a
        //    different plan, and running that one would mean the preview described something else — the
        //    exact "20 shown, 19 done" shape this stage exists to make unreachable.
        var plan = BuildPlan();

        if (plan is null || approved is null || !plan.Matches(approved))
        {
            Rebuild();
            _report(StatusMessage.Warning(StatusCatalog.PreviewOutOfDate));
            return;
        }

        if (!plan.CanExecute)
        {
            Rebuild();

            // ⚠ The blocked branch reuses BlockerSummary's already-resolved sentence rather than a key of
            //   its own: the strip and the panel beneath it must say the same thing, and two keys is how
            //   they stop doing so.
            _report(plan.IsEmpty
                ? StatusMessage.Warning(StatusCatalog.TickAtLeastOneLicence)
                : BlockedMessage());
            return;
        }

        if (!TryBuildRequests(plan, out var requests, out var problem))
        {
            _report(problem!);
            return;
        }

        IssueBatchResult result;
        try
        {
            result = _workflow.IssueBatch(_session, requests, Blank(Note));
        }
        catch (RegisterIntegrityException e)
        {
            // ⭐ Two of OUR sentences, both resolved at read time: the outer one says nothing was issued,
            //   and the register's own refusal is nested as an argument (LocalizedText's mechanism).
            //   ⛔ Not `e.Message` — that is the English diagnostic half.
            _report(StatusMessage.Error(
                StatusCatalog.NothingIssuedRegisterUnchanged,
                new LocalizedText(e.Key, [.. e.Arguments])));
            return;
        }
        catch (ArgumentException e)
        {
            _report(StatusMessage.Error(StatusCatalog.NothingIssuedRegisterUnchanged, e.Message));
            return;
        }
        catch (CryptographicException e)
        {
            // The issuer refused to hand out an artifact it could not verify. Phase 1 is pure, so this
            // leaves the register untouched — which is exactly what the operator needs to be told.
            _report(StatusMessage.Error(StatusCatalog.NothingIssuedRegisterUnchanged, e.Message));
            return;
        }

        Complete(plan, result);
    }

    partial void OnTargetDateChanged(DateTime? value) => Rebuild();

    private void SetLastResult(StatusMessage? message)
    {
        _lastResultMessage = message;
        OnPropertyChanged(nameof(LastResult));
        OnPropertyChanged(nameof(HasResult));
    }

    /// <summary>
    /// Reads the register and plans what the ticked licences would become.
    ///
    /// <para>⭐⭐ <b>The terms are READ HERE, not taken from the browser's rows.</b> Those rows are a
    /// snapshot taken when a licence was ticked; planning from them would mean planning against whatever
    /// the register held at that moment, and — worse — it would make
    /// <see cref="BatchRenewalPlan.Matches"/> vacuous, because both readings would come from the same
    /// cache and could never disagree. ⚠ This was found by trying to prove that guard with an injected
    /// defect and failing to make it go red (#378).</para>
    ///
    /// <para>⚠ The register read is skipped entirely while nothing is ticked, which is the state the
    /// licences view spends almost all of its time in — so typing in the search box costs no extra
    /// query until the operator has actually started selecting.</para>
    /// </summary>
    private BatchRenewalPlan? BuildPlan()
    {
        if (TargetDate is not { } day)
        {
            return null;
        }

        var ticked = _browser.CheckedIds;
        if (ticked.Count == 0)
        {
            return new BatchRenewalPlan { TargetExpiry = LicenseDay.EndOf(day), Candidates = [] };
        }

        var wanted = new HashSet<string>(ticked, StringComparer.Ordinal);

        // ⭐ In the register's own order — soonest expiry first, then customer name — so the preview reads
        //   the same way twice and two plans are comparable position by position.
        var selected = _register.QueryLicenses()
            .Where(summary => wanted.Contains(summary.License.LicenseId))
            .ToList();

        // ⭐ The POINTER per licence (§39.2), never the newest row.
        return BatchRenewalPlanner.Plan(selected, LicenseDay.EndOf(day), _register.GetCurrentArtifact);
    }

    // ⚠ Built for the WHOLE plan before anything is signed. A customer that cannot be read is a register
    //   fault, and finding it here means it costs nothing — no signature, no row, nothing to undo.
    private bool TryBuildRequests(
        BatchRenewalPlan plan, out List<IssueRequest> requests, out StatusMessage? problem)
    {
        requests = new List<IssueRequest>(plan.Candidates.Count);

        foreach (var candidate in plan.Candidates)
        {
            var customer = _register.GetCustomer(candidate.Summary.License.CustomerId);
            if (customer is null)
            {
                // ⚠ A MESSAGE, not a rendered sentence: the caller puts it on the strip, where it may sit
                //   across a language change.
                problem = StatusMessage.Error(
                    StatusCatalog.LicenceRefersToUnknownCustomer,
                    candidate.ShortId,
                    candidate.Summary.License.CustomerId);
                return false;
            }

            requests.Add(new IssueRequest
            {
                // ⭐ The RENEWED terms — the ones that would be signed, expiry already moved. The artifact
                //    and the stored row must never come from two different readings of what was agreed.
                License = candidate.RenewedTerms,
                Customer = customer,
                Reason = candidate.Reason,
                TermsChanged = candidate.TermsChanged,
            });
        }

        problem = null;
        return true;
    }

    /// <summary>The blocked-plan warning, as the message the strip stores.</summary>
    private StatusMessage BlockedMessage() =>
        StatusMessage.Counted(
            StatusCatalog.Blocked,
            MessageSeverity.Warning,
            _previewed is { } plan ? plan.Blocked.Count : 1);

    // ⭐ Everything below runs only after the transaction committed, so every sentence here is true.
    private void Complete(BatchRenewalPlan plan, IssueBatchResult result)
    {
        var count = result.Artifacts.Count;

        // ⚠⚠ WHOLE sentences, never one assembled from clauses (L8.2). ⭐ L8.5 folded the singular/plural
        //    pairs into two counted FAMILIES, which is what Polish needs and English could not have while
        //    L8.2 was forbidden to change a character. The first-issue variant stays a SEPARATE family
        //    rather than an appended clause — the clause is what rule 12 forbids handing to a translator.
        // ⚠ The count is always {0} (Loc.FormatCount puts it there), so it is not repeated below.
        var message = plan.FirstIssues == 0
            ? StatusMessage.Counted(
                StatusCatalog.BatchCompleted, MessageSeverity.Success,
                count, plan.TargetDay, count, result.BatchId)
            : StatusMessage.Counted(
                StatusCatalog.BatchCompletedWithFirstIssues, MessageSeverity.Success,
                count, plan.TargetDay, count, result.BatchId, plan.FirstIssues);

        SetLastResult(message);
        _report(message);

        Note = string.Empty;

        // ⭐ The ticks are dropped only once the operation is recorded. Clearing them earlier would leave
        //    an operator whose batch failed with no selection to retry from.
        _browser.ClearChecksCommand.Execute(null);
        _browser.Refresh();

        // ⚠ Clearing the date rebuilds the preview on its own — the operation is finished, so the card
        //   goes back to asking for the next one rather than still describing the last.
        TargetDate = null;
    }

    private void Announce()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanExtend));
        OnPropertyChanged(nameof(HasBlockers));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(BlockerSummary));
        ExtendCommand.NotifyCanExecuteChanged();
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
