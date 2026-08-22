using EmberTern.LicenseManager.Data;

using EmberTern.LicenseManager.Localization;

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
[StringCatalog(KeyPrefix)]
internal static class ReasonText
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "Reason.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>The first artifact for a licence.</summary>
    public static string Initial => Word(nameof(Initial));

    /// <summary>The expiry moved.</summary>
    public static string Renewal => Word(nameof(Renewal));

    /// <summary>Something other than the expiry changed.</summary>
    public static string TermsChange => Word(nameof(TermsChange));

    /// <summary>The customer lost a file that is otherwise still correct.</summary>
    public static string ReissueLost => Word(nameof(ReissueLost));

    /// <summary>What <see cref="Initial"/> means.</summary>
    public static string InitialExplained => Word(nameof(InitialExplained));

    /// <summary>What <see cref="Renewal"/> means.</summary>
    public static string RenewalExplained => Word(nameof(RenewalExplained));

    /// <summary>What <see cref="TermsChange"/> means.</summary>
    public static string TermsChangeExplained => Word(nameof(TermsChangeExplained));

    /// <summary>What <see cref="ReissueLost"/> means.</summary>
    public static string ReissueLostExplained => Word(nameof(ReissueLostExplained));

    /// <summary>The short name, used wherever a reason appears in a list or a picker.</summary>
    internal static string Describe(string reason) => reason switch
    {
        IssueReasons.Initial => Initial,
        IssueReasons.Renewal => Renewal,
        IssueReasons.TermsChange => TermsChange,
        IssueReasons.ReissueLost => ReissueLost,
        _ => reason,
    };

    /// <summary>
    /// The one-line explanation shown beside the picker, so the operator chooses on meaning rather than
    /// on a guess at what a two-word label covers.
    /// </summary>
    internal static string Explain(string reason) => reason switch
    {
        IssueReasons.Initial => InitialExplained,
        IssueReasons.Renewal => RenewalExplained,
        IssueReasons.TermsChange => TermsChangeExplained,
        IssueReasons.ReissueLost => ReissueLostExplained,
        _ => string.Empty,
    };
}
