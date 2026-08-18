using System;
using System.IO;
using System.Linq;
using EmberTern.LicenseManager.Data;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// What a snapshot of the register is, and what it deliberately is not.
///
/// <para>⭐ D‑3 chose CONTENT fidelity over byte fidelity. A choice like that stays safe only while both
/// halves of it are visible: that the content survives, and that the bytes are not claimed to.</para>
/// </summary>
public sealed class RegisterSnapshotTests
{
    [Fact]
    public void ASnapshotCarriesEveryRowOfEveryTable()
    {
        using var fixture = new ManagerFixture();
        Seed(fixture);

        var expected = fixture.Register.DumpContent();

        Assert.Equal(expected, WithSnapshot(fixture, register => register.DumpContent()));
    }

    [Fact]
    public void ASnapshotCarriesTheCurrentArtifactPointerAndTheWholeHistory()
    {
        using var fixture = new ManagerFixture();
        var (_, license) = Seed(fixture);

        var artifacts = fixture.Register.GetArtifacts(license.LicenseId);
        var current = fixture.Register.GetCurrentArtifact(license.LicenseId);
        var audit = fixture.Register.GetAudit();

        Assert.True(artifacts.Count >= 2, "the fixture must issue more than once for this to prove anything");
        Assert.NotNull(current);
        Assert.NotEmpty(audit);

        var actual = WithSnapshot(fixture, register => (
            Artifacts: register.GetArtifacts(license.LicenseId)
                .Select(a => (a.ArtifactId, a.Token, a.Reason, a.Status)).ToList(),
            // ⭐ The pointer must name the SAME artifact_id, not merely "an artifact". A renumbered
            //    AUTOINCREMENT column would still produce a self-consistent register while detaching
            //    every history line from the identity it was recorded under.
            Current: register.GetCurrentArtifact(license.LicenseId)!.ArtifactId,
            Audit: register.GetAudit().Select(a => (a.AuditId, a.Action, a.TargetId, a.Note)).ToList(),
            Problems: register.CheckIntegrity()));

        Assert.Equal(
            artifacts.Select(a => (a.ArtifactId, a.Token, a.Reason, a.Status)).ToList(), actual.Artifacts);
        Assert.Equal(current!.ArtifactId, actual.Current);
        Assert.Equal(
            audit.Select(a => (a.AuditId, a.Action, a.TargetId, a.Note)).ToList(), actual.Audit);
        Assert.Empty(actual.Problems);
    }

    /// <summary>
    /// ⚠⚠ <b>The measurement that settles D‑3, and it is stronger than the argument written for it.</b>
    /// The plan said <c>File.Copy</c> "can catch the register mid-transaction". On Windows it does not get
    /// that far: while the register is open, the operating system refuses to hand out a read handle at
    /// all. So <c>VACUUM INTO</c> is not the safer of two routes — it is the only route that does not
    /// first close the register out from under the running application.
    /// </summary>
    [Fact]
    public void TheLiveRegisterFileCannotEvenBeReadWhileTheRegisterIsOpen()
    {
        using var fixture = new ManagerFixture();
        Seed(fixture);

        Assert.Throws<IOException>(() => File.ReadAllBytes(fixture.Paths.Register));

        // ⭐ …and the snapshot succeeds against exactly that state.
        Assert.NotEmpty(fixture.Register.CreateSnapshot());
    }

    /// <summary>
    /// ⚠⚠ <b>Disposing a register RELEASES ITS FILE, and that took a fix.</b> <c>Microsoft.Data.Sqlite</c>
    /// pools connections by path, so <c>Close</c> + <c>Dispose</c> alone leave the handle open and the
    /// next <c>File.Move</c> fails against a file the caller believes it closed. The restore path is built
    /// entirely on being able to move a staged register, so this is load-bearing rather than tidy.
    /// </summary>
    [Fact]
    public void ClosingARegisterReleasesItsFileSoItCanBeMoved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "etlm-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "licenses.db");

        try
        {
            using (var register = LicenseRegister.Open(path))
            {
                register.SaveCustomer(new CustomerRecord { CustomerId = "c-0001", Name = "ACME" });
            }

            var moved = Path.Combine(folder, "moved.db");
            File.Move(path, moved);
            Assert.True(File.Exists(moved));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheDumpNoticesAValueChangedUnderneathIt()
    {
        using var fixture = new ManagerFixture();
        var (customer, _) = Seed(fixture);

        var before = fixture.Register.DumpContent();
        fixture.Register.SaveCustomer(customer with { Notes = "changed" });

        Assert.NotEqual(before, fixture.Register.DumpContent());
    }

    /// <summary>
    /// ⭐ The oracle reaches the three tables a hand-written projection is likeliest to forget — the
    /// current-artifact pointer, the append-only history, and <c>sqlite_sequence</c>, which carries the
    /// AUTOINCREMENT high-water mark that stops a restored register re-spending an <c>artifact_id</c>.
    /// </summary>
    [Fact]
    public void TheDumpCoversThePointerTheHistoryAndTheAutoincrementHighWaterMark()
    {
        using var fixture = new ManagerFixture();
        Seed(fixture);

        var dump = fixture.Register.DumpContent();

        Assert.Contains(dump, l => l.StartsWith("sqlite_sequence", StringComparison.Ordinal));
        Assert.Contains(dump, l => l.StartsWith("license_current_artifact", StringComparison.Ordinal));
        Assert.Contains(dump, l => l.StartsWith("audit_log", StringComparison.Ordinal));
        Assert.Contains(dump, l => l.StartsWith("issued_artifacts", StringComparison.Ordinal));
        Assert.Contains(dump, l => l.StartsWith("customers", StringComparison.Ordinal));
        Assert.Contains(dump, l => l.StartsWith("licenses", StringComparison.Ordinal));
        Assert.Contains(dump, l => l.StartsWith("schema_meta", StringComparison.Ordinal));
    }

    /// <summary>⭐ The dump keeps NULL and the empty string apart — they mean different things here.</summary>
    [Fact]
    public void TheDumpDistinguishesNullFromAnEmptyString()
    {
        using var fixture = new ManagerFixture();

        fixture.Register.SaveCustomer(new CustomerRecord
        {
            CustomerId = "c-0001",
            Name = "ACME",
            Notes = null,
        });
        var withNull = fixture.Register.DumpContent()
            .Single(l => l.StartsWith("customers", StringComparison.Ordinal));

        fixture.Register.SaveCustomer(new CustomerRecord
        {
            CustomerId = "c-0001",
            Name = "ACME",
            Notes = string.Empty,
        });
        var withEmpty = fixture.Register.DumpContent()
            .Single(l => l.StartsWith("customers", StringComparison.Ordinal));

        Assert.NotEqual(withNull, withEmpty);
    }

    internal static (CustomerRecord Customer, LicenseRecord License) Seed(ManagerFixture fixture)
    {
        var customer = fixture.SaveCustomer("Żółw Sp. z o.o.");
        var license = fixture.SaveLicense(customer);

        fixture.Workflow.Issue(fixture.Session, license, customer, IssueReasons.Initial);
        fixture.Now = fixture.Now.AddDays(1);
        fixture.Workflow.Issue(
            fixture.Session,
            license with { ExpiresAt = license.ExpiresAt.AddYears(1) },
            customer,
            IssueReasons.Renewal);

        return (customer, license);
    }

    /// <summary>Materialises a snapshot, reads something off it, and leaves nothing behind.</summary>
    internal static T WithSnapshot<T>(ManagerFixture fixture, Func<LicenseRegister, T> read) =>
        WithSnapshotBytes(fixture.Register.CreateSnapshot(), read);

    internal static T WithSnapshotBytes<T>(byte[] snapshot, Func<LicenseRegister, T> read)
    {
        var folder = Path.Combine(Path.GetTempPath(), "etlm-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "licenses.db");
        File.WriteAllBytes(path, snapshot);

        try
        {
            using var register = LicenseRegister.Open(path);
            return read(register);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
