using System;
using System.IO;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.Licensing.Issuing;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Mints REAL signed licence tokens for tests that need to read a payload back out of one.
///
/// <para>⭐⭐ <b>It exists because a fabricated token cannot be read.</b> <c>BulkSendPlanner</c> judges an
/// artifact's expiry by parsing the TOKEN — the same bytes the composer reads, which is what keeps the
/// message and the attachment from disagreeing (§14.2). A stubbed token therefore does not merely weaken
/// such a test, it INVERTS it: the payload fails to parse, the candidate is held as "unreadable", and the
/// test passes for entirely the wrong reason.</para>
///
/// <para>⭐ Signing is PURE — <c>session.Issuer.Issue(terms, clock)</c> touches no register and writes
/// nothing — so a caller needs a key and nothing else. That is why this is a token mint rather than a
/// second <see cref="ManagerFixture"/>: tests whose subject is the planner keep passing explicit lookups
/// and never acquire a register they do not read.</para>
///
/// <para>⚠ <b>ONE key ceremony for the whole test process</b>, created on first use. A ceremony per call
/// was measured expensive enough to matter (it dominates L10.1's 500-licence measurement), and nothing
/// here depends on keys differing between tests.</para>
///
/// <para>⚠ Its keystore lives under <c>%TEMP%\etlm-tests</c> and is deliberately NOT deleted: the folder
/// is the same one <see cref="ManagerFixture"/> uses and is already a recorded backlog item. ⛔ Do not add
/// a finalizer or a process-exit hook for it — a test helper that tries to tidy the filesystem at shutdown
/// is a source of flaky runs, and the leftover is one small folder.</para>
/// </summary>
internal static class TestArtifacts
{
    private const string Passphrase = "six generated words kept on paper too";

    private static readonly Lazy<SigningSession> Session = new(CreateSession);

    /// <summary>The <c>kid</c> every token this class mints carries.</summary>
    internal static string KeyId => Session.Value.KeyId;

    /// <summary>
    /// A genuinely signed <c>ETL1.…</c> token whose payload matches <paramref name="licence"/>.
    /// </summary>
    /// <param name="licence">The terms to sign. ⭐ Its expiry is what a reader parses back out.</param>
    /// <param name="licensee">The name signed in — what the message would address the customer as.</param>
    /// <param name="issuedAt">The <c>iat</c>. ⚠ Explicit, because "already sent since the artifact was issued" is a comparison against exactly this.</param>
    internal static string Token(LicenseRecord licence, string licensee, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(licence);

        return Session.Value.Issuer.Issue(
            new LicenseTerms
            {
                Licensee = licensee,
                Seats = licence.Seats,
                NotBefore = licence.NotBefore,
                ExpiresAt = licence.ExpiresAt,
                MaintenanceUntil = licence.MaintenanceUntil,
                Product = licence.Product,
                LicenseId = licence.LicenseId,
            },
            issuedAt).Token;
    }

    private static SigningSession CreateSession()
    {
        var folder = Path.Combine(
            Path.GetTempPath(), "etlm-tests", "mint-" + Guid.NewGuid().ToString("N"));

        var paths = new ManagerPaths(folder);
        paths.EnsureFolder();

        return SigningSession.Create(paths, "R1", Passphrase, DateTimeOffset.UnixEpoch);
    }
}
