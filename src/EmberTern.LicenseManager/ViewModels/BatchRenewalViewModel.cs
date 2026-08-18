using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Data;
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
    public string Blocker => Candidate.Blocker ?? string.Empty;

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

    /// <summary>What the last completed batch did, or empty. ⭐ Kept on screen after the message fades.</summary>
    [ObservableProperty]
    private string _lastResult = string.Empty;

    /// <summary>Whether there is a result to show.</summary>
    public bool HasResult => !string.IsNullOrEmpty(LastResult);

    // ⚠ The plan the OPERATOR was shown. The command compares a fresh plan against this one, so the
    //   approval is of a specific operation rather than of a moment.
    private BatchRenewalPlan? _previewed;

    /// <summary>Whether a preview exists to read.</summary>
    public bool HasPreview => _previewed is { IsEmpty: false };

    /// <summary>⭐ <b>D‑3.</b> Whether the operation may run: something is ticked, and nothing is blocked.</summary>
    public bool CanExtend => _previewed is { CanExecute: true };

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
                    ? "Tick licences in the list and choose a target date."
                    : "Tick the licences to extend.";
            }

            var qualifying = plan.Qualifying.Count;
            var licences = qualifying == 1 ? "1 licence" : $"{qualifying} licences";
            var firstIssues = plan.FirstIssues switch
            {
                0 => string.Empty,
                1 => " 1 of them has never been issued and would receive its first artifact.",
                _ => $" {plan.FirstIssues} of them have never been issued and would receive their first artifact.",
            };

            return $"{licences} would be extended to {plan.TargetDay}.{firstIssues}";
        }
    }

    /// <summary>Why the action is unavailable, in one line — never a disabled button with no explanation.</summary>
    public string BlockerSummary
    {
        get
        {
            if (_previewed is not { } plan || plan.Blocked.Count == 0)
            {
                return string.Empty;
            }

            var blocked = plan.Blocked.Count == 1
                ? "1 selected licence cannot be extended to this date"
                : $"{plan.Blocked.Count} selected licences cannot be extended to this date";

            return $"{blocked}, so the whole operation is held. Nothing is issued in part — remove them " +
                   "from the selection, or choose a different target date.";
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
            _report(StatusMessage.Warning(
                "The selection or the target date changed since this preview was built, so nothing was " +
                "issued. The preview below is up to date — check it and run the operation again."));
            return;
        }

        if (!plan.CanExecute)
        {
            Rebuild();
            _report(StatusMessage.Warning(
                plan.IsEmpty
                    ? "Tick at least one licence to extend."
                    : BlockerSummary));
            return;
        }

        if (!TryBuildRequests(plan, out var requests, out var problem))
        {
            _report(StatusMessage.Error(problem));
            return;
        }

        IssueBatchResult result;
        try
        {
            result = _workflow.IssueBatch(_session, requests, Blank(Note));
        }
        catch (RegisterIntegrityException e)
        {
            _report(StatusMessage.Error(
                $"Nothing was issued and the register is unchanged. {e.Message}"));
            return;
        }
        catch (ArgumentException e)
        {
            _report(StatusMessage.Error(
                $"Nothing was issued and the register is unchanged. {e.Message}"));
            return;
        }
        catch (CryptographicException e)
        {
            // The issuer refused to hand out an artifact it could not verify. Phase 1 is pure, so this
            // leaves the register untouched — which is exactly what the operator needs to be told.
            _report(StatusMessage.Error(
                $"Nothing was issued and the register is unchanged. {e.Message}"));
            return;
        }

        Complete(plan, result);
    }

    partial void OnTargetDateChanged(DateTime? value) => Rebuild();

    partial void OnLastResultChanged(string value) => OnPropertyChanged(nameof(HasResult));

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
        BatchRenewalPlan plan, out List<IssueRequest> requests, out string problem)
    {
        requests = new List<IssueRequest>(plan.Candidates.Count);

        foreach (var candidate in plan.Candidates)
        {
            var customer = _register.GetCustomer(candidate.Summary.License.CustomerId);
            if (customer is null)
            {
                problem =
                    $"Licence {candidate.ShortId} refers to customer " +
                    $"{candidate.Summary.License.CustomerId}, which the register does not hold. Nothing " +
                    "was issued.";
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

        problem = string.Empty;
        return true;
    }

    // ⭐ Everything below runs only after the transaction committed, so every sentence here is true.
    private void Complete(BatchRenewalPlan plan, IssueBatchResult result)
    {
        var count = result.Artifacts.Count;
        var licences = count == 1 ? "1 licence" : $"{count} licences";
        var firstIssues = plan.FirstIssues == 0
            ? string.Empty
            : $" {plan.FirstIssues} of them received a first artifact.";

        LastResult =
            $"{licences} extended to {plan.TargetDay}. {count} artifact(s) recorded as batch " +
            $"{result.BatchId}.{firstIssues} " +
            "Nothing was written to disk — export the files from the register when you are ready to send them.";

        _report(StatusMessage.Success(LastResult));

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
