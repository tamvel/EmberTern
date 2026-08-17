using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// Turns a stored issuing reason into the words the operator reads — in ONE place.
///
/// <para>⭐ The same shape, and for the same reason, as <see cref="VerdictText"/>: the history list and the
/// reason picker are two consumers of one mapping, and two switches over one vocabulary is how a ledger
/// and a form end up calling the same fact two different things.</para>
///
/// <para>⚠⚠ <b>It presents the value; it never decides one.</b> <c>issued_artifacts.reason</c> is chosen by
/// the operator and stored verbatim — this class must never grow a comparison that would let a display
/// disagree with what was recorded.</para>
///
/// <para>⭐ An unrecognised value is shown VERBATIM rather than mapped to "unknown". The column is
/// append-only and its vocabulary can only ever grow, so a register written by a later version must stay
/// readable in an older one — and the raw value is always more informative than our word for not knowing it.</para>
/// </summary>
internal static class ReasonText
{
    /// <summary>The short name, used wherever a reason appears in a list or a picker.</summary>
    internal static string Describe(string reason) => reason switch
    {
        IssueReasons.Initial => "Initial issue",
        IssueReasons.Renewal => "Renewal",
        IssueReasons.TermsChange => "Terms change",
        IssueReasons.ReissueLost => "Re-issue — lost file",
        _ => reason,
    };

    /// <summary>
    /// The one-line explanation shown beside the picker, so the operator chooses on meaning rather than
    /// on a guess at what a two-word label covers.
    /// </summary>
    internal static string Explain(string reason) => reason switch
    {
        IssueReasons.Initial =>
            "The first artifact for this licence. Nothing has been sent to the customer yet.",
        IssueReasons.Renewal =>
            "The expiry moved. Press Save terms with the new expiry first — the issue signs the saved terms.",
        IssueReasons.TermsChange =>
            "Something other than the expiry changed: seats, the start date, or the licensee's name.",
        IssueReasons.ReissueLost =>
            "The customer lost their copy of a file that is otherwise still correct.",
        _ => string.Empty,
    };
}
