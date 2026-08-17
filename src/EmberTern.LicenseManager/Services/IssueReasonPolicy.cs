using System;
using System.Collections.Generic;
using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// Which reason an operator may attach to an issue, and why one is refused.
///
/// <para>⭐⭐ <b>The governing rule: refuse a reason the register can DISPROVE; never refuse one it cannot
/// judge.</b> <c>renewal</c> and <c>terms-change</c> each assert that something specific changed, and
/// <see cref="IssueChange"/> can check both — so a claim contradicted by the artifact the customer is
/// actually holding is stopped before it becomes a permanent row. <c>reissue-lost</c> asserts something
/// about the CUSTOMER, which no register can verify, so it is never refused — only steered (D6).</para>
///
/// <para>⛔ <b>This is policy, not register semantics, and it deliberately does not live in
/// <see cref="LicenseRegister"/>.</b> The register records what it is told, verbatim and append-only;
/// teaching it to parse a previous payload and second-guess a caller would make the one component whose
/// job is "never lose what happened" also the component with an opinion about it.</para>
///
/// <para>⚠ It also does not live in <see cref="IssuingWorkflow"/>: the workflow signs and records, and
/// tests legitimately issue with arbitrary reason text to exercise storage. The judgement belongs where
/// the operator's choice is made.</para>
/// </summary>
public static class IssueReasonPolicy
{
    /// <summary>
    /// The reasons the operator may pick from, in the order they should be offered.
    ///
    /// <para>⭐ <b>D‑2.</b> Before the first issue the answer is not a choice at all: there is exactly one
    /// truthful value, and offering a list would invite an untruth. From the second issue on,
    /// <c>initial</c> is not offered, because it is then false by definition.</para>
    /// </summary>
    public static IReadOnlyList<string> Offer(IssueChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return change.HasPrevious
            ? [IssueReasons.Renewal, IssueReasons.TermsChange, IssueReasons.ReissueLost]
            : [IssueReasons.Initial];
    }

    /// <summary>
    /// The sentence explaining why this reason cannot be recorded, or <see langword="null"/> when it can.
    ///
    /// <para>⚠ Every refusal names the ACTION that resolves it. "Terms change" chosen against unchanged
    /// terms is nearly always an operator who edited the form and has not pressed <b>Save terms</b> — the
    /// issue signs the SAVED record, so the message has to say so or the operator will simply try again.</para>
    /// </summary>
    public static string? Refuse(string reason, IssueChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Choose why this licence is being issued.";
        }

        if (!change.HasPrevious)
        {
            return string.Equals(reason, IssueReasons.Initial, StringComparison.Ordinal)
                ? null
                : "This licence has never been issued, so the first artifact can only be the initial one.";
        }

        switch (reason)
        {
            case IssueReasons.Initial:
                return "This licence has already been issued, so a further artifact cannot be the initial one. " +
                       "Choose a renewal, a terms change, or a re-issue of a lost file.";

            // ⚠ `CanCompare` false means UNKNOWN, not unchanged — an unreadable stored payload must never
            //   block the operator. See IssueChange.CanCompare.
            case IssueReasons.Renewal when change.CanCompare && !change.ExpiryMoved:
                return "The expiry has not moved since the last issue, so this is not a renewal. " +
                       "Change the expiry and press Save terms first, or pick a different reason.";

            case IssueReasons.TermsChange when change.CanCompare && !change.OtherTermsChanged:
                return "Nothing but the expiry differs from the last issue, so there is no terms change to " +
                       "record. Press Save terms if the form still holds unsaved edits, or pick a different " +
                       "reason.";

            // ⭐ Never refused. The register cannot know whether a customer lost a file, and a rule that
            //   pretends otherwise would be guessing — which is the habit this whole stage removes.
            case IssueReasons.ReissueLost:
            case IssueReasons.Renewal:
            case IssueReasons.TermsChange:
                return null;

            default:
                // Unreachable from the UI, which offers only the four. Stated rather than silently allowed:
                // the value is persisted verbatim and append-only (D‑3).
                return $"'{reason}' is not one of the recorded issuing reasons.";
        }
    }
}
