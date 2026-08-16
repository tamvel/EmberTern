using System;
using System.Collections.Generic;
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
/// One licence's place in an issuing operation: whose it is, on what terms, and why.
/// </summary>
public sealed record IssueRequest
{
    /// <summary>
    /// ⭐ The terms to SIGN. For a renewal these are the new terms, expiry already moved — the artifact
    /// and the register row must never be built from two different readings of what was agreed.
    /// </summary>
    public required LicenseRecord License { get; init; }

    /// <summary>Whose licence it is. ⭐ The name is read fresh and signed into the artifact (D6).</summary>
    public required CustomerRecord Customer { get; init; }

    /// <summary>One of <see cref="IssueReasons"/>. ⛔ Chosen by the operator, never inferred from a diff.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// <see langword="true"/> when this operation changed the terms and they must be stored alongside the
    /// artifact — a renewal. <see langword="false"/> for a re-issue of terms that are already recorded, so
    /// the history does not gain a line claiming a change that did not happen.
    /// </summary>
    public bool TermsChanged { get; init; }
}

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

        var (record, issued) = Sign(session, license, customer, reason);

        var artifact = _register.AppendArtifact(
            record,
            note: $"Licensed to {customer.Name}, {license.Seats} seat(s), until {license.ExpiresAt:yyyy-MM-dd}.");

        return new IssueResult(artifact, issued);
    }

    /// <summary>
    /// ⭐⭐ Issues a whole operation — one licence or twenty — so that a failure anywhere leaves either
    /// everything recorded or nothing at all.
    ///
    /// <para><b>Phase 1, here: sign everything.</b> Signing is a pure function of key, terms and clock. It
    /// writes no file and no row, so if the tenth signature throws, the first nine are values in memory
    /// that nobody has seen and nothing refers to — the register is exactly as it was, and the correct
    /// response is to try again.</para>
    ///
    /// <para><b>Phase 2, in the register:</b> every term change, artifact, current-artifact pointer and
    /// history line commits as ONE transaction, or none of it does.</para>
    ///
    /// <para><b>Phase 3, afterwards and elsewhere:</b> <see cref="SaveArtifact"/> writes files, from the
    /// STORED token. ⭐ That ordering is the whole guarantee — the only route by which an artifact can
    /// reach a customer starts at a committed row, so "signed but unrecorded" is unreachable rather than
    /// merely unlikely.</para>
    /// </summary>
    /// <param name="session">The unlocked key.</param>
    /// <param name="requests">What to issue. ⚠ Each licence may appear at most once.</param>
    /// <param name="note">A remark stored on the batch's own history line.</param>
    public IssueBatchResult IssueBatch(
        SigningSession session, IReadOnlyList<IssueRequest> requests, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            throw new ArgumentException("A batch must contain at least one licence.", nameof(requests));
        }

        // ── Phase 1 — sign. Pure: no file, no row, nothing anyone can hold if this throws. ──────────
        var units = new List<LicenseIssueUnit>(requests.Count);
        foreach (var request in requests)
        {
            var (record, _) = Sign(session, request.License, request.Customer, request.Reason);

            units.Add(new LicenseIssueUnit
            {
                Artifact = record,
                UpdatedTerms = request.TermsChanged ? request.License : null,
            });
        }

        // ── Phase 2 — record, atomically. ───────────────────────────────────────────────────────────
        return _register.ApplyIssueBatch(units, note);
    }

    // ⭐ The ONE place a licence becomes a signature, used by both the single issue and every unit of a
    //    batch — so the two can never disagree about what gets signed.
    private (IssuedArtifactRecord Record, IssuedLicense Issued) Sign(
        SigningSession session, LicenseRecord license, CustomerRecord customer, string reason)
    {
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

        var record = new IssuedArtifactRecord
        {
            LicenseId = issued.Payload.LicenseId,
            KeyId = issued.Payload.KeyId,
            IssuedAt = issued.Payload.IssuedAt,
            PayloadJson = System.Text.Encoding.UTF8.GetString(issued.PayloadJson),
            Token = issued.Token,
            Reason = reason,
        };

        return (record, issued);
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
