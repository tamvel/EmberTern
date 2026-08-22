using System;
using System.IO;
using EmberTern.Core.Settings;
using EmberTern.Licensing;

namespace EmberTern.App.Licensing;

/// <summary>What an <see cref="LicenseService.Install"/> attempt did, beyond the verdict itself.</summary>
internal enum LicenseInstallOutcome
{
    /// <summary>The file is now the active licence.</summary>
    Installed,

    /// <summary>⛔ Refused: it verified, but it is not newer than the licence already installed (§16.4).</summary>
    NotNewer,

    /// <summary>⛔ Refused: it verified, but it carries a DIFFERENT licence id and the user has not confirmed.</summary>
    DifferentLicenseNeedsConfirmation,

    /// <summary>⛔ Refused: the verdict itself is unusable. Read the verdict for what to say.</summary>
    Rejected,

    /// <summary>⛔ The verification passed but the file could not be written or read back.</summary>
    NotStored,
}

/// <summary>The result of offering a licence to <see cref="LicenseService.Install"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Verdict">⭐ For a successful install this is the verdict of the file READ BACK FROM DISK.</param>
internal sealed record LicenseInstallResult(LicenseInstallOutcome Outcome, LicenseVerdict Verdict);

/// <summary>
/// ⭐⭐ <b>The application's licence state — resolved once at startup, and the ONE thing the rest of the
/// app asks.</b>
///
/// <para>⛔ <b>There is no network code here, and none anywhere beneath it.</b> V1 is entirely offline
/// (decision D1): no backend, no activation server, no check-in, no telemetry, not even an optional one.
/// <c>EmberTern.Licensing</c> has zero package references and <c>LicensingMakesNoNetworkCallsTests</c>
/// asserts its type closure contains nothing from <c>System.Net.*</c> — which is the machine-checkable
/// form of that decision, and will still be true when nobody remembers this conversation.</para>
///
/// <para>⛔ <b>Nothing here identifies the machine.</b> No InstallationId, no hardware fingerprint, no
/// counters (decision D3). A licence copied to a second machine works, and V1 says so honestly rather
/// than pretending otherwise — technical seat enforcement is V2's job and needs a backend to exist first.
/// <c>seats</c> is carried, displayed, and enforced by nothing (D2).</para>
///
/// <para>⭐ <b>The trusted-key table is a constructor parameter defaulting to
/// <see cref="TrustedKeys.Production"/>.</b> The application always uses the production table; tests
/// supply their own so the whole chain — issue, store, resolve, verify — can be proven end to end.
/// ⭐ Since the real key ceremony (2026-08-22, L7.3/L7.4) <c>TrustedKeys.Production</c> carries <c>R1</c>, so
/// a licence issued by the License Manager verifies here for real. ⚠ Before that it was empty by design and
/// every real licence resolved to <c>Invalid / UnknownKey</c>, which is why <c>Valid</c> and <c>Grace</c>
/// were proven by tests rather than by hand for five stages.</para>
/// </summary>
internal sealed class LicenseService
{
    /// <summary>⭐ The clock-rollback tolerance (§16.3). Time zones, DST, VM suspends and dead CMOS batteries are all normal.</summary>
    internal static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(48);

    private readonly LicenseLocation _location;
    private readonly ApplicationSettingsStore _settings;
    private readonly TrustedKeyTable _trustedKeys;
    private readonly Func<DateTimeOffset> _systemClock;

    /// <summary>Creates the service.</summary>
    /// <param name="location">Where to look for the licence file.</param>
    /// <param name="settings">Where the clock high-water mark lives.</param>
    /// <param name="trustedKeys">⚠ Defaults to the shipped production table; tests pass their own.</param>
    /// <param name="systemClock">⚠ Tests pass their own; production reads the machine clock.</param>
    internal LicenseService(
        LicenseLocation location,
        ApplicationSettingsStore settings,
        TrustedKeyTable? trustedKeys = null,
        Func<DateTimeOffset>? systemClock = null)
    {
        _location = location ?? throw new ArgumentNullException(nameof(location));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _trustedKeys = trustedKeys ?? TrustedKeys.Production;
        _systemClock = systemClock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The current verdict. <see cref="Refresh"/> computes it; until then it is <c>Unlicensed</c>.</summary>
    internal LicenseVerdict Verdict { get; private set; } =
        LicenseVerdict.Unlicensed;

    /// <summary>Which file the verdict came from, or <see langword="null"/> when there is none. Support asks this first.</summary>
    internal string? SourcePath { get; private set; }

    /// <summary>⭐ Where activation writes — the per-user file, and the only one EmberTern ever writes (§8).</summary>
    internal string InstallPath => _location.UserPath;

    /// <summary>
    /// ⭐ True when the system clock is more than <see cref="ClockTolerance"/> BEHIND the high-water mark.
    /// ⛔ Advisory only — it never blocks anything.
    /// </summary>
    internal bool ClockLooksRolledBack { get; private set; }

    /// <summary>
    /// ⭐⭐ Whether a NEW database connection may be opened.
    ///
    /// <para>Only <c>Expired</c> denies it (§7), and even then the application opens: the editor, files,
    /// exports and settings all keep working, so no state can prevent saving or exporting work that is
    /// already open. ⛔ In a <c>Debug</c> build nothing is ever denied — the gate is off by configuration
    /// (§16.5), and this property is where that shows.</para>
    ///
    /// <para>⚠ The DOMAIN is "opens a new attachment", not "the button the user pressed" — Connect, Test
    /// connection, a debug session and an import session are all the same act. Ratified with the user
    /// 2026-08-15: there is deliberately no exception for Test connection.</para>
    /// </summary>
    internal bool AllowsNewDatabaseConnections =>
        !LicensingPolicy.GateEnabled || Verdict.Status != LicenseStatus.Expired;

    /// <summary>
    /// ⭐ Whether the application should refuse to be used at all.
    ///
    /// <para><c>Unlicensed</c>, <c>Invalid</c>, <c>NotYetValid</c> and <c>VersionNotCovered</c> gate;
    /// <c>Expired</c> does not (it only stops new connections), and <c>Valid</c>/<c>Grace</c> are full
    /// function. ⛔ Always false in a <c>Debug</c> build.</para>
    /// </summary>
    internal bool IsBlocked => LicensingPolicy.GateEnabled && Verdict.Status is
        LicenseStatus.Unlicensed or LicenseStatus.Invalid or
        LicenseStatus.NotYetValid or LicenseStatus.VersionNotCovered;

    /// <summary>
    /// ⭐⭐ <b>The ONE question <see cref="LicensedConnections"/> asks before opening an attachment.</b>
    ///
    /// <para>It is the conjunction of the two rules design §7 states separately, and the conjunction is
    /// read off that table rather than invented: <c>Expired</c> denies new connections while the rest of the
    /// application keeps working, and the four <see cref="IsBlocked"/> states are <i>gated</i> — a stronger
    /// condition that necessarily includes not opening databases. Stated once here so a caller cannot
    /// satisfy one half and miss the other.</para>
    ///
    /// <para>⛔ Always true in a <c>Debug</c> build — both halves fold away with the gate (§16.5).</para>
    /// </summary>
    internal bool AllowsConnecting => AllowsNewDatabaseConnections && !IsBlocked;

    /// <summary>
    /// ⭐ True when a valid licence is within <see cref="LicenseConstants.ExpiryWarningWindow"/> of expiry.
    /// The banner this drives is dismissible, and it is the customer's entire annual contact with licensing.
    /// </summary>
    internal bool IsExpiringSoon =>
        Verdict.Status == LicenseStatus.Valid &&
        Verdict.Payload is { } payload &&
        payload.ExpiresAt - EffectiveNow() <= LicenseConstants.ExpiryWarningWindow;

    /// <summary>
    /// Resolves the licence file and verifies it, then records the clock high-water mark.
    ///
    /// <para>⚠ Never throws. A licence problem is a verdict, and startup must survive every one of them —
    /// including a file locked by a backup agent, which reads as "no licence" rather than as a crash.</para>
    /// </summary>
    internal LicenseVerdict Refresh()
    {
        var systemNow = _systemClock();
        var highWater = ReadHighWater();

        ClockLooksRolledBack = highWater is { } mark && systemNow < mark - ClockTolerance;

        SourcePath = _location.ResolveExisting();
        Verdict = SourcePath is null
            ? LicenseVerdict.Unlicensed
            : VerifyText(LicenseStore.TryRead(SourcePath), Max(systemNow, highWater));

        return Verdict;
    }

    /// <summary>
    /// ⭐ Records the high-water mark. Called on exit, so a session that ran through midnight is recorded.
    /// </summary>
    /// <returns>False when the write did not reach the file — the caller may log it; it is not fatal.</returns>
    internal bool RecordClock()
    {
        var systemNow = _systemClock();

        // ⭐ Through `Update`, which takes the cross-process lock and reads UNDER it. ⛔ Not
        //    `Load()` → mutate → `Save()`: that shape turned a transient read failure into DEFAULTS and
        //    wrote them, measured at 89 default-writes and 0 of 5 connection profiles surviving.
        return _settings.Update(settings =>
        {
            var current = settings.UserSettings.LicenseClockHighWater;
            if (current is null || systemNow > current)
            {
                settings.UserSettings.LicenseClockHighWater = systemNow;
            }
        });
    }

    /// <summary>
    /// Verifies an offered licence and, if it may replace the current one, stores it and re-verifies from
    /// disk.
    ///
    /// <para>⭐ <b>The freshness rule (§16.4).</b> An offered licence is installed only when it verifies,
    /// matches the product, and either there is no local licence, or it carries the same <c>lid</c> with a
    /// NEWER <c>iat</c>, or the caller has confirmed replacing a different <c>lid</c>. That makes renewal
    /// idempotent and turns an accidental re-import of last year's file into a no-op instead of a
    /// downgrade — while still allowing a machine to be legitimately moved to another licence.</para>
    /// </summary>
    /// <param name="text">The licence as the user supplied it — a pasted artifact or a file's contents.</param>
    /// <param name="confirmedDifferentLicense">
    /// True when the user has explicitly agreed to replace a licence with a DIFFERENT <c>lid</c>.
    /// </param>
    internal LicenseInstallResult Install(string? text, bool confirmedDifferentLicense = false)
    {
        var effectiveNow = EffectiveNow();
        var offered = VerifyText(text, effectiveNow);

        // ⚠ `NotYetValid` and `Expired` are ACCEPTED for storage: both are authentic licences, and refusing
        //    to store a post-dated renewal would make renewing early impossible. Only an unusable artifact
        //    is rejected outright.
        if (offered.Payload is null || offered.Status is LicenseStatus.Invalid or LicenseStatus.Unlicensed)
        {
            return new LicenseInstallResult(LicenseInstallOutcome.Rejected, offered);
        }

        if (Verdict.Payload is { } current)
        {
            if (!string.Equals(offered.Payload.LicenseId, current.LicenseId, StringComparison.Ordinal))
            {
                if (!confirmedDifferentLicense)
                {
                    return new LicenseInstallResult(
                        LicenseInstallOutcome.DifferentLicenseNeedsConfirmation, offered);
                }
            }
            else if (offered.Payload.IssuedAt <= current.IssuedAt)
            {
                return new LicenseInstallResult(LicenseInstallOutcome.NotNewer, offered);
            }
        }

        LicenseVerdict stored;
        try
        {
            _location.EnsureUserFolder();

            // ⭐ The verdict that comes back is the one for the file ON DISK — `Install` cannot be called
            //    in a way that skips the re-read.
            stored = LicenseStore.Install(_location.UserPath, text!, candidate => VerifyText(candidate, effectiveNow));
        }
        catch (IOException)
        {
            return new LicenseInstallResult(LicenseInstallOutcome.NotStored, offered);
        }
        catch (UnauthorizedAccessException)
        {
            return new LicenseInstallResult(LicenseInstallOutcome.NotStored, offered);
        }

        if (stored.Payload is null || stored.Status is LicenseStatus.Invalid or LicenseStatus.Unlicensed)
        {
            // The write half-succeeded, or something rewrote the file underneath us. The user finds out
            // now, with their copy of the file still to hand.
            return new LicenseInstallResult(LicenseInstallOutcome.NotStored, stored);
        }

        Verdict = stored;
        SourcePath = _location.UserPath;
        return new LicenseInstallResult(LicenseInstallOutcome.Installed, stored);
    }

    /// <summary>
    /// ⭐ The one verification entry point, so the startup path and the activation path cannot diverge
    /// (design §5: *"the full §4 verification chain, identical code"*).
    /// </summary>
    private LicenseVerdict VerifyText(string? text, DateTimeOffset effectiveNow) =>
        LicenseVerifier.Verify(
            text,
            new LicenseVerificationContext(
                _trustedKeys,
                effectiveNow,
                LicenseConstants.ProductId,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                // ⭐ `maint` is compared against the BUILD's release date, so a perpetual-fallback licence
                //    covers the versions published while maintenance ran. AppInfo reads it off the assembly,
                //    which `Directory.Build.props` is the single source of.
                // ⚠ `AppInfo.ReleaseDate` is a `DateOnly` — a release is a DAY, not an instant. It is
                //    widened here at UTC midnight, so a licence whose `maint` falls on the release date
                //    covers that build rather than missing it by the hours nobody recorded.
                BuildReleaseDate()));

    /// <summary>The build's release date as an instant, or <see langword="null"/> when the assembly carries none.</summary>
    private static DateTimeOffset? BuildReleaseDate() =>
        AppInfo.ReleaseDate is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

    /// <summary>⭐ <c>max(systemNow, highWater)</c> — moving the clock back cannot revive an expired licence.</summary>
    private DateTimeOffset EffectiveNow() => Max(_systemClock(), ReadHighWater());

    private static DateTimeOffset Max(DateTimeOffset systemNow, DateTimeOffset? highWater) =>
        highWater is { } mark && mark > systemNow ? mark : systemNow;

    /// <summary>
    /// ⚠ A failed settings read answers <see langword="null"/> — no high-water mark — rather than a
    /// default. The guard is then merely absent for that session, which is the safe direction: the
    /// alternative would be inventing an instant and enforcing it.
    /// </summary>
    private DateTimeOffset? ReadHighWater() => _settings.Load()?.UserSettings.LicenseClockHighWater;
}
