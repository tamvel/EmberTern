using System;

namespace EmberTern.Licensing;

/// <summary>
/// The single answer the licensing subsystem produces.
///
/// <para>⭐ <b>The client verifies; it never decides.</b> No other code in EmberTern or the License
/// Manager asks "is the licence OK?" — it reads this verdict. That is what keeps the question to one
/// owner, and it is why <see cref="LicenseVerifier.Verify"/> is the only way to make one.</para>
/// </summary>
public sealed record LicenseVerdict
{
    private LicenseVerdict(
        LicenseStatus status, LicenseFailure failure, LicensePayload? payload, string? detail)
    {
        Status = status;
        Failure = failure;
        Payload = payload;
        Detail = detail;
    }

    /// <summary>What the host should do. See <see cref="LicenseStatus"/>.</summary>
    public LicenseStatus Status { get; }

    /// <summary>
    /// Why, when <see cref="Status"/> is <see cref="LicenseStatus.Invalid"/> or
    /// <see cref="LicenseStatus.Unlicensed"/>. <see cref="LicenseFailure.None"/> otherwise.
    /// </summary>
    public LicenseFailure Failure { get; }

    /// <summary>
    /// The payload — ⭐ present only when the signature verified. A refused artifact never hands its
    /// contents on: showing the licensee name off an unverified file would be showing the user whatever
    /// the file's author chose to write there.
    /// </summary>
    public LicensePayload? Payload { get; }

    /// <summary>
    /// A technical token for <c>[Copy details]</c> — an unknown <c>kid</c>, an offending field name.
    /// ⛔ Never rendered as prose and never localized; it exists so a support mail can carry a fact.
    /// </summary>
    public string? Detail { get; }

    /// <summary>True when the product is fully usable.</summary>
    public bool IsUsable => Status is LicenseStatus.Valid or LicenseStatus.Grace;

    /// <summary>No licence artifact was found. Produced by the host, which is what can see a filesystem.</summary>
    public static LicenseVerdict Unlicensed { get; } =
        new(LicenseStatus.Unlicensed, LicenseFailure.FileMissing, null, null);

    internal static LicenseVerdict Invalid(LicenseFailure failure, string? detail = null) =>
        new(LicenseStatus.Invalid, failure, null, detail);

    internal static LicenseVerdict Accepted(LicenseStatus status, LicensePayload payload) =>
        new(status, LicenseFailure.None, payload, null);
}

/// <summary>
/// Everything a verification depends on, passed in rather than reached for — which is what makes
/// <see cref="LicenseVerifier"/> a pure function and every one of its states testable without a clock, a
/// filesystem or a build.
/// </summary>
/// <param name="Keys">The keys to accept signatures from.</param>
/// <param name="Now">
/// The current instant. ⚠ The <i>host</i> applies the clock-rollback high-water mark (§16.3) and passes
/// the effective value here; this assembly has no store to keep a high-water mark in.
/// </param>
/// <param name="ExpectedProduct">Normally <see cref="LicenseConstants.ProductId"/>.</param>
/// <param name="MaxSupportedVersion">Normally <see cref="LicenseConstants.MaxSupportedPayloadVersion"/>.</param>
/// <param name="GracePeriod">Normally <see cref="LicenseConstants.DefaultGracePeriod"/>.</param>
/// <param name="BuildReleaseDate">
/// This build's release date, for the <c>maint</c> gate. <see langword="null"/> disables the gate — which
/// is what the License Manager passes, since it inspects licences rather than being licensed by them.
/// </param>
public sealed record LicenseVerificationContext(
    TrustedKeyTable Keys,
    DateTimeOffset Now,
    string ExpectedProduct,
    int MaxSupportedVersion,
    TimeSpan GracePeriod,
    DateTimeOffset? BuildReleaseDate)
{
    /// <summary>The context EmberTern uses: the production keys and the shipped constants.</summary>
    public static LicenseVerificationContext ForProduct(
        DateTimeOffset now, DateTimeOffset? buildReleaseDate) =>
        new(TrustedKeys.Production,
            now,
            LicenseConstants.ProductId,
            LicenseConstants.MaxSupportedPayloadVersion,
            LicenseConstants.DefaultGracePeriod,
            buildReleaseDate);
}
