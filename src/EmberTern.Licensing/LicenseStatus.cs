namespace EmberTern.Licensing;

/// <summary>
/// What the host application should do about the licence it has. The eight states of
/// <c>docs/design/licensing-system.md</c> §7.
///
/// <para>⭐ Only <see cref="Valid"/> and <see cref="Grace"/> mean "the product is fully usable". ⚠ And
/// <see cref="Expired"/> does NOT mean "refuse to start": per ratified decision O3 it blocks new database
/// connections only, leaving the editor, files, exports and settings usable — Architecture rule 11 says a
/// licensing state must never be able to trap or destroy work the user already has open.</para>
/// </summary>
public enum LicenseStatus
{
    /// <summary>
    /// No licence present. ⚠ Produced by the host application, not by <see cref="LicenseVerifier"/>, for
    /// the reason given on <see cref="LicenseFailure.FileMissing"/>.
    /// </summary>
    Unlicensed = 0,

    /// <summary>Present but not acceptable. <see cref="LicenseVerdict.Failure"/> says why.</summary>
    Invalid,

    /// <summary>Verified, but its <c>nbf</c> is in the future.</summary>
    NotYetValid,

    /// <summary>Verified and current.</summary>
    Valid,

    /// <summary>
    /// Past <c>exp</c>, inside <see cref="LicenseConstants.DefaultGracePeriod"/>. ⭐ Fully usable, with a
    /// persistent warning — see the constant for why this is a correctness requirement.
    /// </summary>
    Grace,

    /// <summary>Past <c>exp</c> and past the grace period.</summary>
    Expired,

    /// <summary>
    /// Verified and current, but this build was released after the licence's <c>maint</c> date, so this
    /// version is not covered. ⚠ Nothing issues <c>maint</c> in V1 — see §3 and §13.5 for why the gate is
    /// nonetheless built now: it is the one thing that cannot be retrofitted onto clients already in the
    /// field.
    /// </summary>
    VersionNotCovered,
}
