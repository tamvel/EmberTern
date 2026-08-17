using System;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// What actually changed between the artifact a licence currently has and the terms about to be signed.
///
/// <para>⭐⭐ <b>It exists so a recorded reason can be DISPROVED.</b> <c>issued_artifacts.reason</c> is
/// append-only: a wrong value is in the register forever, and "renewal" against terms whose expiry never
/// moved is a permanent lie about what happened. ⛔ The reason itself is still the operator's choice —
/// this measures, it does not infer (<see cref="IssueRequest.Reason"/>).</para>
///
/// <para>⭐ <b>Both sides are normalised through <see cref="LicensePayload.FormatTimestamp"/></b>, which is
/// the same function the issuer's own truncation and the register's storage go through. So the comparison
/// is made on the WIRE form — what would actually be signed — rather than on in-memory
/// <see cref="DateTimeOffset"/>s that can differ by a sub-second nobody could ever observe in an artifact.
/// ⛔ Do not reimplement the truncation here; a second rounding rule is how the two drift.</para>
/// </summary>
public sealed record IssueChange
{
    /// <summary>Whether the licence has ever been issued.</summary>
    public required bool HasPrevious { get; init; }

    /// <summary>
    /// Whether the previous artifact's payload could be read at all.
    ///
    /// <para>⚠⚠ <see langword="false"/> means <b>unknown</b>, never <b>unchanged</b>. A stored payload the
    /// parser refuses is exactly the artifact a support call is about, and blocking a re-issue because the
    /// register cannot read its own history would turn a display problem into an operational one. Every
    /// consumer must treat this as "cannot judge" and allow the operation.</para>
    /// </summary>
    public required bool CanCompare { get; init; }

    /// <summary>Whether the expiry differs from the one that was signed.</summary>
    public required bool ExpiryMoved { get; init; }

    /// <summary>
    /// Whether anything OTHER than the expiry differs: seats, the start date, the licensee's name, the
    /// product, or the reserved maintenance date.
    ///
    /// <para>⭐ The licensee counts. It is signed into the artifact (D6), so re-issuing after a company is
    /// renamed genuinely changes the terms the customer holds, even though no date and no number moved.</para>
    /// </summary>
    public required bool OtherTermsChanged { get; init; }

    /// <summary>Whether anything at all differs.</summary>
    public bool AnythingChanged => ExpiryMoved || OtherTermsChanged;

    /// <summary>Nothing has ever been issued, so there is nothing to compare against.</summary>
    public static IssueChange NeverIssued { get; } = new()
    {
        HasPrevious = false,
        CanCompare = false,
        ExpiryMoved = false,
        OtherTermsChanged = false,
    };

    /// <summary>
    /// Compares the artifact a licence currently has against the terms that would be signed now.
    /// </summary>
    /// <param name="previous">
    /// ⭐ The artifact <c>license_current_artifact</c> points at — never <c>Artifacts[0]</c>. The pointer is
    /// the authority on which release the customer is holding (§39.2), and it is that release the operator
    /// is deciding to supersede.
    /// </param>
    /// <param name="next">The saved terms. ⚠ The SAVED ones — an unsaved form is not what gets signed.</param>
    /// <param name="licensee">The customer's name as it would be signed now, read fresh (D6).</param>
    public static IssueChange Between(
        IssuedArtifactRecord? previous, LicenseRecord next, string licensee)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (previous is null)
        {
            return NeverIssued;
        }

        if (!LicensePayload.TryParse(
                System.Text.Encoding.UTF8.GetBytes(previous.PayloadJson), out var signed, out _))
        {
            return new IssueChange
            {
                HasPrevious = true,
                CanCompare = false,
                ExpiryMoved = false,
                OtherTermsChanged = false,
            };
        }

        var expiryMoved = !SameInstant(signed.ExpiresAt, next.ExpiresAt);

        var otherChanged =
            signed.Seats != next.Seats ||
            !SameInstant(signed.NotBefore, next.NotBefore) ||
            !string.Equals(signed.Licensee, licensee, StringComparison.Ordinal) ||
            !string.Equals(signed.Product, next.Product, StringComparison.Ordinal) ||
            !SameMaintenance(signed.MaintenanceUntil, next.MaintenanceUntil);

        return new IssueChange
        {
            HasPrevious = true,
            CanCompare = true,
            ExpiryMoved = expiryMoved,
            OtherTermsChanged = otherChanged,
        };
    }

    // ⭐ Equality on the SIGNED representation. Two values that render to the same wire timestamp produce
    //    byte-identical payloads at that field, so calling them different would report a change no
    //    artifact could ever show.
    private static bool SameInstant(DateTimeOffset left, DateTimeOffset right) =>
        string.Equals(
            LicensePayload.FormatTimestamp(left),
            LicensePayload.FormatTimestamp(right),
            StringComparison.Ordinal);

    private static bool SameMaintenance(DateTimeOffset? left, DateTimeOffset? right) => (left, right) switch
    {
        (null, null) => true,
        ({ } l, { } r) => SameInstant(l, r),
        _ => false,
    };
}
