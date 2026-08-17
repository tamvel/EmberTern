namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One selectable issuing reason: the value that gets PERSISTED, and the words the operator picks by.
///
/// <para>⭐⭐ The two are separate fields on purpose. <c>issued_artifacts.reason</c> is append-only and its
/// vocabulary is fixed at four (D‑3), so the stored value must never be derived from a label — a reworded
/// caption would otherwise silently start writing a fifth value into a column nothing can correct.</para>
/// </summary>
/// <param name="Value">The persisted constant, one of <see cref="Data.IssueReasons"/>.</param>
/// <param name="Label">The short name shown in the picker.</param>
/// <param name="Explanation">The one-line meaning, shown for whichever option is selected.</param>
public sealed record IssueReasonOption(string Value, string Label, string Explanation);
