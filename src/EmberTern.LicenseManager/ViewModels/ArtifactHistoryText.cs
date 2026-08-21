using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// ⭐⭐ The ONE owner of the persisted <c>current</c> / <c>superseded</c> vocabulary, as words.
///
/// <para><b>The twin of <see cref="LicenceStatusText"/>, and it was found the same way.</b> §53.6 named
/// the licences list's <c>Capitalise(status)</c>; the issuing history had the identical defect one line
/// long — <c>isCurrent ? "current" : "superseded"</c> printed <see cref="RegisterQueries.Current"/> and
/// <see cref="RegisterQueries.Superseded"/> straight to the screen. Those are PERSISTED values
/// (`terminology.md` §4.4) and must stay exactly as they are; how they read is a separate question, and
/// this is where it is answered.</para>
///
/// <para>⭐⭐ "Superseded" is deliberately a NEUTRAL statement of fact, not a warning and not a deletion.
/// An earlier release was really sent, to a real customer, who may still be running it — which is the whole
/// reason <c>issued_artifacts</c> is append-only. ⛔ Nothing in this application may present it as removed,
/// replaced or invalid, and no translation of it may either.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class ArtifactStandingText
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "ArtifactStanding.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>The artifact <c>license_current_artifact</c> points at.</summary>
    public static string Current => Word(nameof(Current));

    /// <summary>An artifact a later one replaced. ⛔ Not removed, not invalid.</summary>
    public static string Superseded => Word(nameof(Superseded));

    /// <summary>How an artifact's standing reads.</summary>
    /// <remarks>
    /// ⛔ Keyed on the register's own projection, exactly as before — never on comparing dates here. The
    /// pointer is the authority on which artifact is current.
    /// </remarks>
    internal static string Describe(bool isCurrent) => isCurrent ? Current : Superseded;
}

/// <summary>
/// What the issuing history says about itself and about the artifact being looked at.
///
/// <para>⭐ The counted summary is a plural FAMILY because English already had two arms ("1 issue on
/// record" / "N issues on record, all kept") — and note that the two differ by more than the number, which
/// is exactly why the tail could not stay a shared fragment.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class HistoryCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "History.";

    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>⭐ The empty state as a STATED fact — "nothing here" and "never issued" are not the same.</summary>
    public static string NeverIssued => Word(nameof(NeverIssued));

    /// <summary>The whole history in one line, saying the append-only guarantee out loud.</summary>
    public static string Summary(int count) => Loc.FormatCount(KeyPrefix + nameof(Summary), count);

    /// <summary>⭐ The size of the token as the customer receives it, armor included.</summary>
    public static string TokenSizeAsDelivered(string bytes) =>
        Loc.Format(KeyPrefix + nameof(TokenSizeAsDelivered), bytes);

    /// <summary>
    /// ⚠ Stated, not hidden: a payload the parser cannot read is the single most interesting row in the
    /// register, and blanking the fields would present it as an ordinary one.
    /// </summary>
    public static string UnreadablePayload => Word(nameof(UnreadablePayload));
}
