namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One selectable issuing reason.
///
/// <para>⭐⭐ <b>Its identity is the PERSISTED value, and nothing else.</b>
/// <c>issued_artifacts.reason</c> is append-only and its vocabulary is fixed at four (D‑3), so the stored
/// value must never be derived from a label — a reworded caption would otherwise silently start writing a
/// fifth value into a column nothing can correct.</para>
///
/// <para>⭐⭐ <b>The words are therefore not members.</b> A <c>record</c> compares by every positional
/// member and <c>ComboBox.SelectedItem</c> matches by equality, so a label held here would put the
/// current language into the option's identity: rebuild the list in another language and the operator's
/// selection equals nothing in it, blanking a picker that gates a signature. ⛔ Do not add a word as a
/// member here.</para>
///
/// <para>⭐ Both words come from <see cref="ReasonText"/> — the class that already exists to be the ONE
/// place a stored reason becomes words, and which the history list also reads. Until this record dropped
/// them, <c>ShellViewModel</c> called <c>ReasonText.Describe</c> / <c>.Explain</c> and copied the results
/// in here, so the picker held a snapshot of a mapping that has an owner.</para>
/// </summary>
/// <param name="Value">The persisted constant, one of <see cref="Data.IssueReasons"/>.</param>
public sealed record IssueReasonOption(string Value)
{
    /// <summary>The short name shown in the picker.</summary>
    public string Label => ReasonText.Describe(Value);

    /// <summary>The one-line meaning, shown for whichever option is selected.</summary>
    public string Explanation => ReasonText.Explain(Value);
}
