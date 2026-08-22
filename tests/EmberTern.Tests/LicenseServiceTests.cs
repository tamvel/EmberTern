using System;
using System.IO;
using System.Text;
using EmberTern.App.Licensing;
using EmberTern.Core.Settings;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// L4a — the licence mechanism: resolution, verification, the clock guard, the freshness rule and what the
/// verdict permits.
///
/// <para>⭐ Every licence here is really signed (see <see cref="LicenseFixtures"/>) and verified through the
/// real <c>LicenseVerifier</c> — the same code the shipped application runs. ⚠ The trusted-key table is the
/// fixture's OWN, never the shipped one — that is exactly why <c>LicenseService</c> takes one as a
/// parameter, and it is what keeps these tests independent of the production key.</para>
///
/// <para>⛔ Nothing in this file, or beneath it, touches a network. V1 is offline by decision D1, and
/// <c>LicensingMakesNoNetworkCallsTests</c> asserts the machine-checkable form of that.</para>
/// </summary>
public sealed class LicenseServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "etlic-tests", Guid.NewGuid().ToString("N"));

    private readonly LicenseFixtures _fixtures = new();

    private string UserDirectory => Path.Combine(_root, "user");

    private string MachineDirectory => Path.Combine(_root, "machine");

    private LicenseLocation Location => new(UserDirectory, MachineDirectory);

    private ApplicationSettingsStore Settings => new(Path.Combine(_root, "settings"));

    private LicenseService Service(DateTimeOffset? now = null, TrustedKeyTable? keys = null) =>
        new(Location, Settings, keys ?? _fixtures.TrustedKeys, () => now ?? Now);

    public LicenseServiceTests()
    {
        Directory.CreateDirectory(UserDirectory);
        Directory.CreateDirectory(MachineDirectory);
        Directory.CreateDirectory(Path.Combine(_root, "settings"));
    }

    // ── Resolution ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoLicenceFileAnywhereReadsAsUnlicensed()
    {
        var verdict = Service().Refresh();

        Assert.Equal(LicenseStatus.Unlicensed, verdict.Status);
        Assert.Null(Service().SourcePath);
    }

    [Fact]
    public void ThePerUserFileShadowsThePerMachineFile()
    {
        // ⭐ Design §8: first match wins, and the per-user file always shadows the machine one. The machine
        //   path exists because we sometimes install the licence at the customer's site ourselves (D8).
        Write(MachineDirectory, _fixtures.Valid(Now, "lid-machine"));
        Write(UserDirectory, _fixtures.Valid(Now, "lid-user"));

        var service = Service();
        var verdict = service.Refresh();

        Assert.Equal("lid-user", verdict.Payload!.LicenseId);
        Assert.Equal(Path.Combine(UserDirectory, LicenseConstants.StoredFileName), service.SourcePath);
    }

    [Fact]
    public void ThePerMachineFileIsUsedWhenThereIsNoPerUserOne()
    {
        Write(MachineDirectory, _fixtures.Valid(Now, "lid-machine"));

        var service = Service();
        var verdict = service.Refresh();

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal("lid-machine", verdict.Payload!.LicenseId);
        Assert.Equal(Path.Combine(MachineDirectory, LicenseConstants.StoredFileName), service.SourcePath);
    }

    // ── States ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AValidLicenceIsValidAndNamesItsLicensee()
    {
        Write(UserDirectory, _fixtures.Valid(Now));

        var verdict = Service().Refresh();

        Assert.Equal(LicenseStatus.Valid, verdict.Status);
        Assert.Equal(LicenseFixtures.Licensee, verdict.Payload!.Licensee);
    }

    [Fact]
    public void AnExpiryInsideFourteenDaysIsGraceAndStillFullyUsable()
    {
        // ⭐ The grace period is a correctness requirement, not generosity: renewal in V1 is a human
        //   process, and an expiry that bricks the tool at midnight turns a purchase-order delay into a
        //   work stoppage.
        Write(UserDirectory, _fixtures.Issue(Now.AddYears(-1), Now.AddYears(-1), Now.AddDays(-3)));

        var service = Service();

        Assert.Equal(LicenseStatus.Grace, service.Refresh().Status);
        Assert.True(service.AllowsNewDatabaseConnections);
        Assert.False(service.IsBlocked);
    }

    [Fact]
    public void PastTheGracePeriodTheLicenceIsExpired()
    {
        Write(UserDirectory, _fixtures.Issue(Now.AddYears(-1), Now.AddYears(-1), Now.AddDays(-20)));

        Assert.Equal(LicenseStatus.Expired, Service().Refresh().Status);
    }

    [Fact]
    public void ALicenceThatHasNotStartedYetIsNotYetValid()
    {
        Write(UserDirectory, _fixtures.Issue(Now, Now.AddDays(10), Now.AddYears(1)));

        Assert.Equal(LicenseStatus.NotYetValid, Service().Refresh().Status);
    }

    [Fact]
    public void ALicenceForAnotherProductIsRefused()
    {
        Write(UserDirectory, _fixtures.Issue(Now, Now, Now.AddYears(1), product: "SomethingElse"));

        var verdict = Service().Refresh();

        Assert.Equal(LicenseStatus.Invalid, verdict.Status);
        Assert.Equal(LicenseFailure.WrongProduct, verdict.Failure);
    }

    [Fact]
    public void ALicenceSignedByAKeyThisBuildDoesNotKnowIsRefused()
    {
        // ⚠ Until the 2026-08-22 ceremony this was ALSO the state of every real licence, in every
        //   configuration, because TrustedKeys.Production was empty. It no longer is — so this test is now
        //   only about the rule it names: an unknown key is refused, never guessed at.
        Write(UserDirectory, _fixtures.Valid(Now));

        var verdict = Service(keys: LicenseFixtures.Foreign).Refresh();

        Assert.Equal(LicenseStatus.Invalid, verdict.Status);
        Assert.Equal(LicenseFailure.SignatureInvalid, verdict.Failure);
    }

    [Fact]
    public void AnAlteredLicenceIsRefused()
    {
        // ⚠⚠ The tampering goes through `LicenseFixtures.Tamper`, which edits the ENCODED payload. The first
        //    version of this test did a text `Replace` of the licensee's name — which appears nowhere in the
        //    file, because the payload is base64url. It mutated nothing and passed while proving nothing.
        var altered = LicenseFixtures.Tamper(_fixtures.Valid(Now));
        Write(UserDirectory, altered);

        var verdict = Service().Refresh();

        Assert.Equal(LicenseStatus.Invalid, verdict.Status);
        Assert.NotEqual(LicenseFailure.None, verdict.Failure);
    }

    [Fact]
    public void AFileThatIsNotALicenceIsRefusedWithoutBeingTouched()
    {
        // ⛔ Design §4: a malformed file is NOT deleted and NOT moved. It is the customer's file, and the
        //   support answer is often "send us exactly what you have".
        var path = Path.Combine(UserDirectory, LicenseConstants.StoredFileName);
        File.WriteAllText(path, "this is a holiday photo");

        Assert.Equal(LicenseStatus.Invalid, Service().Refresh().Status);
        Assert.True(File.Exists(path));
        Assert.Equal("this is a holiday photo", File.ReadAllText(path));
    }

    [Fact]
    public void AValidLicenceCloseToExpiryIsFlaggedButNotDegraded()
    {
        Write(UserDirectory, _fixtures.Issue(Now.AddYears(-1), Now.AddYears(-1), Now.AddDays(10)));

        var service = Service();
        service.Refresh();

        Assert.Equal(LicenseStatus.Valid, service.Verdict.Status);
        Assert.True(service.IsExpiringSoon);
        Assert.True(service.AllowsNewDatabaseConnections);
    }

    // ── What the verdict permits ────────────────────────────────────────────────────────────────────

    [Fact]
    public void OnlyExpiredStopsNewDatabaseConnections()
    {
        // ⭐ Ratified with the user 2026-08-15: the DOMAIN is "opens a new attachment" — Connect, Test
        //   connection, a debug session and an import session are the same act, with no exception for Test.
        // ⚠ In a Debug build the gate is off entirely, so the assertion is written against the policy
        //   rather than against a hard-coded expectation.
        Write(UserDirectory, _fixtures.Issue(Now.AddYears(-1), Now.AddYears(-1), Now.AddDays(-20)));

        var service = Service();
        service.Refresh();

        Assert.Equal(LicenseStatus.Expired, service.Verdict.Status);
        Assert.Equal(!LicensingPolicy.GateEnabled, service.AllowsNewDatabaseConnections);

        // ⭐ …and Expired never blocks the application itself: work, files, exports and settings stay
        //   available, because no state may prevent saving what is already open (Architecture rule 11).
        Assert.False(service.IsBlocked);
    }

    [Fact]
    public void AnUnlicensedInstallationIsGatedButOnlyInARelease()
    {
        var service = Service();
        service.Refresh();

        Assert.Equal(LicensingPolicy.GateEnabled, service.IsBlocked);
    }

    // ── The clock guard (§16.3) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MovingTheClockBackCannotReviveAnExpiredLicence()
    {
        // ⭐⭐ In V1 the expiry date is the ENTIRE enforcement mechanism, so an unguarded clock would make
        //    it a no-op: set the machine back a year and the licence lives again.
        Write(UserDirectory, _fixtures.Issue(Now.AddYears(-1), Now.AddYears(-1), Now.AddDays(-20)));

        Assert.True(Service().RecordClock());

        var rolledBack = Service(Now.AddYears(-1));
        var verdict = rolledBack.Refresh();

        Assert.Equal(LicenseStatus.Expired, verdict.Status);
        Assert.True(rolledBack.ClockLooksRolledBack);
    }

    [Fact]
    public void ASmallClockCorrectionIsNotTreatedAsARollback()
    {
        // ⚠ 48 h of tolerance, because time zones, DST, VM suspends, dead CMOS batteries and travelling
        //   laptops are all normal — and the flag must not cry wolf.
        Service().RecordClock();

        var corrected = Service(Now.AddHours(-47));
        corrected.Refresh();

        Assert.False(corrected.ClockLooksRolledBack);
    }

    [Fact]
    public void TheHighWaterMarkOnlyEverMovesForward()
    {
        Service().RecordClock();
        Service(Now.AddDays(-30)).RecordClock();

        Assert.Equal(Now, Settings.Load()!.UserSettings.LicenseClockHighWater);
    }

    [Fact]
    public void TheRollbackFlagWarnsAndNeverBlocks()
    {
        // ⛔ A user legitimately fixing a badly wrong clock must not be locked out of their tool.
        Write(UserDirectory, _fixtures.Valid(Now));
        Service().RecordClock();

        var rolledBack = Service(Now.AddYears(-1));
        rolledBack.Refresh();

        Assert.True(rolledBack.ClockLooksRolledBack);
        Assert.False(rolledBack.IsBlocked);
        Assert.True(rolledBack.AllowsNewDatabaseConnections);
    }

    // ── Installing and the freshness rule (§16.4) ───────────────────────────────────────────────────

    [Fact]
    public void InstallingWritesTheFileAndReportsTheVerdictReadBackFromDisk()
    {
        var service = Service();
        service.Refresh();

        var result = service.Install(_fixtures.Valid(Now));

        Assert.Equal(LicenseInstallOutcome.Installed, result.Outcome);
        Assert.Equal(LicenseStatus.Valid, result.Verdict.Status);

        // ⭐ The proof that the verdict came from the FILE: a fresh service, reading only the disk, agrees.
        Assert.Equal(LicenseStatus.Valid, Service().Refresh().Status);
    }

    [Fact]
    public void TheStoredFileIsUtf8WithoutAByteOrderMark()
    {
        var service = Service();
        service.Refresh();
        service.Install(_fixtures.Valid(Now));

        var bytes = File.ReadAllBytes(Path.Combine(UserDirectory, LicenseConstants.StoredFileName));

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.StartsWith(LicenseArmor.BeginMarker, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void ARenewalOfTheSameLicenceIsAccepted()
    {
        var service = Service();
        service.Install(_fixtures.Issue(Now, Now, Now.AddDays(30), "lid-1"));

        var renewal = service.Install(_fixtures.Issue(Now.AddDays(1), Now, Now.AddYears(1), "lid-1"));

        Assert.Equal(LicenseInstallOutcome.Installed, renewal.Outcome);
        Assert.Equal(Now.AddYears(1), Service().Refresh().Payload!.ExpiresAt);
    }

    [Fact]
    public void ReImportingAnOlderFileForTheSameLicenceIsANoOpRatherThanADowngrade()
    {
        // ⭐ This is what makes renewal idempotent, and it is the reason the rule keys on `iat`: an
        //   accidental re-import of last year's e-mail must not take the customer's expiry backwards.
        var service = Service();
        service.Install(_fixtures.Issue(Now.AddDays(1), Now, Now.AddYears(1), "lid-1"));

        var stale = service.Install(_fixtures.Issue(Now, Now, Now.AddDays(30), "lid-1"));

        Assert.Equal(LicenseInstallOutcome.NotNewer, stale.Outcome);
        Assert.Equal(Now.AddYears(1), Service().Refresh().Payload!.ExpiresAt);
    }

    [Fact]
    public void ADifferentLicenceNeedsAnExplicitConfirmation()
    {
        // ⭐ Moving a machine to a different licence is legitimate, so this is a confirmation rather than a
        //   refusal — but it must be a decision, not an accident.
        var service = Service();
        service.Install(_fixtures.Issue(Now, Now, Now.AddYears(1), "lid-1"));

        var other = _fixtures.Issue(Now, Now, Now.AddYears(1), "lid-2");

        Assert.Equal(
            LicenseInstallOutcome.DifferentLicenseNeedsConfirmation,
            service.Install(other).Outcome);
        Assert.Equal("lid-1", Service().Refresh().Payload!.LicenseId);

        Assert.Equal(
            LicenseInstallOutcome.Installed,
            service.Install(other, confirmedDifferentLicense: true).Outcome);
        Assert.Equal("lid-2", Service().Refresh().Payload!.LicenseId);
    }

    [Fact]
    public void AnUnusableArtifactIsRejectedAndLeavesTheInstalledLicenceAlone()
    {
        var service = Service();
        service.Install(_fixtures.Valid(Now, "lid-1"));

        Assert.Equal(LicenseInstallOutcome.Rejected, service.Install("not a licence").Outcome);
        Assert.Equal("lid-1", Service().Refresh().Payload!.LicenseId);
    }

    [Fact]
    public void APostDatedRenewalCanStillBeInstalled()
    {
        // ⚠ `NotYetValid` is an AUTHENTIC licence. Refusing to store one would make renewing early
        //    impossible, which is precisely when a well-organised customer renews.
        var service = Service();
        service.Refresh();

        var result = service.Install(_fixtures.Issue(Now, Now.AddDays(30), Now.AddYears(1)));

        Assert.Equal(LicenseInstallOutcome.Installed, result.Outcome);
        Assert.Equal(LicenseStatus.NotYetValid, result.Verdict.Status);
    }

    private static void Write(string directory, string licence) =>
        File.WriteAllText(
            Path.Combine(directory, LicenseConstants.StoredFileName), licence, new UTF8Encoding(false));

    public void Dispose()
    {
        _fixtures.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}
