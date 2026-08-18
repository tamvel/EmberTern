using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// One licence's place in a batch renewal: what would happen to it, and — when nothing may — why not.
///
/// <para>⚠ No Avalonia types (Architecture rule 1). Everything the preview shows is a string or a
/// <see langword="bool"/> decided here, because "already valid until 2029-01-01" is a judgement about a
/// date rather than a layout decision, and a judgement is worth a test.</para>
/// </summary>
public sealed record BatchRenewalCandidate
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>What the register answered about this licence.</summary>
    public required LicenseSummary Summary { get; init; }

    /// <summary>The terms that would be SIGNED — the licence's own, with the target expiry.</summary>
    public required LicenseRecord RenewedTerms { get; init; }

    /// <summary>Whose licence it is. ⭐ The name that gets signed into the artifact (D6).</summary>
    public string CustomerName => Summary.CustomerName;

    /// <summary>The <c>lid</c>.</summary>
    public string LicenseId => Summary.License.LicenseId;

    /// <summary>The licence id, shortened for a list.</summary>
    public string ShortId =>
        LicenseId.Length > 12 ? LicenseId[..12] + "…" : LicenseId;

    /// <summary>What the licence runs to today, before the operation.</summary>
    public string CurrentExpiry =>
        Summary.License.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>What it would run to afterwards.</summary>
    public string NewExpiry =>
        RenewedTerms.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The reason that would be recorded — one of <see cref="IssueReasons"/>.
    ///
    /// <para>⭐ <b>D‑1 / D‑2.</b> <c>renewal</c> is the operator's single choice for the whole operation;
    /// <c>initial</c> is not a choice at all and never was — it is what the register PROVES about a
    /// licence that has no earlier artifact, and <see cref="IssueReasonPolicy.Offer"/> has always
    /// answered exactly that. ⛔ This is not the inference L5.3 removed: that one guessed <c>renewal</c>
    /// from a row count, this one reads a fact the policy computes anyway.</para>
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>⭐ Whether this licence has never been issued, so the batch would be its FIRST artifact.</summary>
    public bool IsFirstIssue => !string.Equals(Reason, IssueReasons.Renewal, StringComparison.Ordinal);

    /// <summary>
    /// Whether the licence ROW's terms actually move, i.e. whether the batch stores new terms alongside
    /// the artifact. ⚠ A different question from <see cref="Reason"/>: this one decides
    /// <see cref="LicenseIssueUnit.UpdatedTerms"/>, and a <see langword="false"/> here keeps the history
    /// from gaining a <c>licence.updated</c> line claiming a change that did not happen.
    /// </summary>
    public required bool TermsChanged { get; init; }

    /// <summary>
    /// Why this licence cannot take part, or <see langword="null"/> when it can.
    ///
    /// <para>⭐ <b>D‑3.</b> A single blocker stops the WHOLE operation — there is no partial batch. The
    /// sentence therefore has to name what the operator must do, because it is now the only thing
    /// standing between them and twenty licences.</para>
    /// </summary>
    public string? Blocker { get; init; }

    /// <summary>Whether this licence would be extended.</summary>
    public bool Qualifies => Blocker is null;
}

/// <summary>
/// A whole batch renewal, as it would happen — and the authority on what the operator was shown.
///
/// <para>⭐⭐ <b>The plan is the contract between the preview and the execution.</b> The user's
/// requirement is semantic as well as technical atomicity: <i>what the preview shows as qualifying must
/// be exactly what runs</i>. So the execution does not re-derive a list of its own — it re-plans from the
/// same inputs and REFUSES if the fresh plan is not the one that was on screen (see
/// <c>BatchRenewalViewModel</c>). "20 selected, 19 done" is therefore unreachable rather than
/// unlikely.</para>
/// </summary>
public sealed record BatchRenewalPlan
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The instant every licence in the batch would run to. ⭐ Via <see cref="LicenseDay"/>.</summary>
    public required DateTimeOffset TargetExpiry { get; init; }

    /// <summary>Every selected licence, in the order the operator sees them.</summary>
    public required IReadOnlyList<BatchRenewalCandidate> Candidates { get; init; }

    /// <summary>The target date as the operator chose it.</summary>
    public string TargetDay => TargetExpiry.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>The ones that would be extended.</summary>
    public IReadOnlyList<BatchRenewalCandidate> Qualifying =>
        Candidates.Where(c => c.Qualifies).ToList();

    /// <summary>The ones standing in the way. ⭐ Never hidden, never silently dropped (D‑3).</summary>
    public IReadOnlyList<BatchRenewalCandidate> Blocked =>
        Candidates.Where(c => !c.Qualifies).ToList();

    /// <summary>⭐ How many licences would receive their FIRST artifact — D‑2 requires this to be shown.</summary>
    public int FirstIssues => Candidates.Count(c => c.Qualifies && c.IsFirstIssue);

    /// <summary>How many would be renewals of something already delivered.</summary>
    public int Renewals => Candidates.Count(c => c.Qualifies && !c.IsFirstIssue);

    /// <summary>Nothing is selected, so there is nothing to preview.</summary>
    public bool IsEmpty => Candidates.Count == 0;

    /// <summary>
    /// ⭐ <b>D‑3.</b> Whether the operation may run at all: something is selected, and NOTHING is blocked.
    /// One blocker disables the action for every licence, including the nineteen that were fine.
    /// </summary>
    public bool CanExecute => !IsEmpty && Blocked.Count == 0;

    /// <summary>
    /// The licence ids this plan covers, in order — the identity a re-plan is compared against.
    /// </summary>
    public IReadOnlyList<string> LicenseIds => Candidates.Select(c => c.LicenseId).ToList();

    /// <summary>
    /// Whether another plan would do exactly the same thing to exactly the same licences.
    ///
    /// <para>⭐⭐ This is the semantic-atomicity check. It compares the target, the licences, their
    /// order, their reasons and their verdicts — so a plan that gained a blocker, lost a licence, or
    /// changed one licence's reason between the preview and the button press is NOT the plan the
    /// operator approved, and the operation stops rather than running a near-miss.</para>
    /// </summary>
    public bool Matches(BatchRenewalPlan other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (TargetExpiry != other.TargetExpiry || Candidates.Count != other.Candidates.Count)
        {
            return false;
        }

        for (var i = 0; i < Candidates.Count; i++)
        {
            var mine = Candidates[i];
            var theirs = other.Candidates[i];

            if (!string.Equals(mine.LicenseId, theirs.LicenseId, StringComparison.Ordinal) ||
                !string.Equals(mine.Reason, theirs.Reason, StringComparison.Ordinal) ||
                mine.TermsChanged != theirs.TermsChanged ||
                mine.Qualifies != theirs.Qualifies)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Turns a selection and a target date into the operation that would follow from them.
///
/// <para>⭐ <b>Pure, and it takes the previous artifact as a LOOKUP rather than taking the register.</b>
/// That keeps every judgement here testable against artifacts a test constructs, and it keeps the
/// planner from acquiring the ability to write anything.</para>
///
/// <para>⛔ <b>It does not relax <see cref="IssueReasonPolicy"/> and does not restate it.</b> The policy
/// is called once per licence, unmodified, exactly as the single issuing path calls it — a batch asks the
/// same question twenty times, it does not ask an easier one. Everything this file adds on top is about
/// the word EXTEND, which is the operation's own name and not a licensing rule.</para>
/// </summary>
public static class BatchRenewalPlanner
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Plans the operation.
    /// </summary>
    /// <param name="selected">The licences the operator ticked, in the order they should be shown.</param>
    /// <param name="targetExpiry">
    /// The instant every licence would run to. ⚠ Already through <see cref="LicenseDay.EndOf"/> — the
    /// planner does not do calendar arithmetic, so it cannot disagree with the licence form about what a
    /// day means.
    /// </param>
    /// <param name="currentArtifact">
    /// ⭐ The artifact <c>license_current_artifact</c> points at, per licence — never the newest row
    /// (§39.2). The pointer is the authority on what the customer is actually holding.
    /// </param>
    public static BatchRenewalPlan Plan(
        IReadOnlyList<LicenseSummary> selected,
        DateTimeOffset targetExpiry,
        Func<string, IssuedArtifactRecord?> currentArtifact)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(currentArtifact);

        var candidates = new List<BatchRenewalCandidate>(selected.Count);

        foreach (var summary in selected)
        {
            candidates.Add(Judge(summary, targetExpiry, currentArtifact));
        }

        return new BatchRenewalPlan { TargetExpiry = targetExpiry, Candidates = candidates };
    }

    private static BatchRenewalCandidate Judge(
        LicenseSummary summary,
        DateTimeOffset targetExpiry,
        Func<string, IssuedArtifactRecord?> currentArtifact)
    {
        var licence = summary.License;
        var renewed = licence with { ExpiresAt = targetExpiry };

        // ⭐ The register's own comparison, against the POINTER, on the SIGNED wire form — the same one
        //    the single re-issue path measures. ⛔ Not re-implemented here.
        var change = IssueChange.Between(currentArtifact(licence.LicenseId), renewed, summary.CustomerName);

        // ⭐ D‑1 / D‑2: `initial` where the register proves there is no earlier artifact, `renewal`
        //    everywhere else. This mirrors IssueReasonPolicy.Offer, which answers exactly this question.
        var reason = change.HasPrevious ? IssueReasons.Renewal : IssueReasons.Initial;

        return new BatchRenewalCandidate
        {
            Summary = summary,
            RenewedTerms = renewed,
            Reason = reason,

            // ⚠ About the licence ROW, not about the artifact: does this operation store new terms?
            TermsChanged = renewed.ExpiresAt != licence.ExpiresAt,
            Blocker = FindBlocker(licence, targetExpiry, reason, change),
        };
    }

    private static string? FindBlocker(
        LicenseRecord licence,
        DateTimeOffset targetExpiry,
        string reason,
        IssueChange change)
    {
        // ⭐ Checked FIRST because it is a fault in the terms themselves, not in the issuing story: a
        //    licence that ends before it begins is something the licence FORM already refuses, and a
        //    batch that could write one would be a second door into a state the single path forbids.
        if (targetExpiry <= licence.NotBefore)
        {
            return "The target date is not after this licence's start date " +
                   $"({licence.NotBefore.ToString(DateFormat, CultureInfo.InvariantCulture)}). " +
                   "Choose a later target date, or remove this licence from the selection.";
        }

        // ⭐⭐ THE OPERATION IS CALLED EXTEND, AND THIS IS WHERE THAT WORD IS ENFORCED.
        //
        //    ⛔ It is deliberately NOT a licensing rule and not a weakening of anything: nothing in the
        //    register forbids moving an expiry backwards, and the single path lets an operator do it with
        //    their eyes on one licence. What it forbids is doing it to twenty at once under a button that
        //    says "extend" — silently SHORTENING a customer's licence is the batch mistake with no undo,
        //    because the previous artifact stays valid in the field while the register now disagrees
        //    with it.
        if (targetExpiry <= licence.ExpiresAt)
        {
            return "Already valid until " +
                   $"{licence.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture)}, " +
                   "so the target date would not extend it. Choose a later target date, or remove this " +
                   "licence from the selection.";
        }

        // ⭐ The UNCHANGED policy, once per licence. This is what catches the case where the licence row
        //    was moved backwards from what was signed and the target merely restores it: the row would
        //    move, but the artifact the customer holds already expires then, so it is not a renewal.
        //    ⚠ `CanCompare == false` means UNKNOWN, and the policy already treats it as "cannot judge".
        return IssueReasonPolicy.Refuse(reason, change);
    }
}
