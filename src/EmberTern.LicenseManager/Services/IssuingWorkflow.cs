using System;
using System.IO;
using EmberTern.Licensing;
using EmberTern.Licensing.Issuing;
using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.Services;

/// <summary>What an issue produced, ready to be saved or e-mailed.</summary>
/// <param name="Artifact">The register row, with its identity.</param>
/// <param name="Issued">The signed licence in every form.</param>
public sealed record IssueResult(IssuedArtifactRecord Artifact, IssuedLicense Issued);

/// <summary>
/// Sign, record, and hand back — in that order, and never in another.
///
/// <para>⭐ <b>The register row is written BEFORE the file reaches the customer, and the two are separate
/// steps on purpose.</b> Issuing is the act that matters; writing a copy to disk is a convenience the
/// operator may repeat, may skip, or may do badly. If saving the file were part of issuing, a failed
/// Save-As dialog would leave a licence signed and unrecorded — the one state from which the register can
/// no longer answer "what did we send this customer?".</para>
/// </summary>
public sealed class IssuingWorkflow
{
    private readonly LicenseRegister _register;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates the workflow over a register.</summary>
    public IssuingWorkflow(LicenseRegister register, Func<DateTimeOffset>? clock = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Signs a licence for the terms recorded against <paramref name="license"/>, records the artifact,
    /// and returns it.
    /// </summary>
    public IssueResult Issue(
        SigningSession session, LicenseRecord license, CustomerRecord customer, string reason)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(customer);

        var issued = session.Issuer.Issue(
            new LicenseTerms
            {
                // ⭐ The LICENSEE is the customer's name, taken fresh from the register at the moment of
                //    signing. It is not copied into the licence row, because then a corrected company
                //    name would leave old rows disagreeing with the artifacts that were actually sent.
                Licensee = customer.Name,
                Seats = license.Seats,
                NotBefore = license.NotBefore,
                ExpiresAt = license.ExpiresAt,
                MaintenanceUntil = license.MaintenanceUntil,
                Product = license.Product,
                LicenseId = license.LicenseId,
            },
            _clock());

        var artifact = _register.AppendArtifact(
            new IssuedArtifactRecord
            {
                LicenseId = issued.Payload.LicenseId,
                KeyId = issued.Payload.KeyId,
                IssuedAt = issued.Payload.IssuedAt,
                PayloadJson = System.Text.Encoding.UTF8.GetString(issued.PayloadJson),
                Token = issued.Token,
                Reason = reason,
            },
            note: $"Licensed to {customer.Name}, {license.Seats} seat(s), until {license.ExpiresAt:yyyy-MM-dd}.");

        return new IssueResult(artifact, issued);
    }

    /// <summary>
    /// Writes an artifact to disk as <c>EmberTern.etlic</c>.
    ///
    /// <para>⭐ Written from the STORED token, not from a fresh signature — which is what makes "the
    /// customer lost their file" a five-second re-export rather than a re-issue with a new <c>iat</c>
    /// that EmberTern would then treat as a replacement (§16.4).</para>
    /// </summary>
    public void SaveArtifact(IssuedArtifactRecord artifact, string path)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // UTF-8 with NO BOM — the project's rule for every generated text file (gotcha #178). The armored
        // token is pure ASCII, so this is about the bytes we do NOT add.
        File.WriteAllText(path, LicenseArmor.Wrap(artifact.Token), new System.Text.UTF8Encoding(false));

        _register.Record("licence.exported", "licence", artifact.LicenseId, Path.GetFileName(path));
    }

    /// <summary>
    /// ⭐ Re-verifies a stored artifact through the real client verifier.
    ///
    /// <para>Used by the register view to show, for any artifact ever issued, exactly what EmberTern would
    /// say about it today. It answers a support question — <i>"is the file I sent them still good?"</i> —
    /// with the product's own opinion rather than with a recomputation of our own.</para>
    /// </summary>
    public LicenseVerdict Inspect(SigningSession session, IssuedArtifactRecord artifact) =>
        LicenseVerifier.Verify(
            artifact.Token,
            new LicenseVerificationContext(
                session.TrustedKeys,
                _clock(),
                LicenseConstants.ProductId,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                BuildReleaseDate: null));
}
