using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The atomicity of an issuing operation — the one technical problem L5.0 exists to solve.</b>
///
/// <para>A batch that extends twenty customers must not be able to leave ten of them extended, and must
/// never leave a signed artifact that the register does not know about. Those are two different failure
/// modes with two different defences, and both are asserted here:</para>
///
/// <list type="bullet">
/// <item><b>Half a batch</b> is prevented by one SQLite transaction around every row the operation
/// writes — proved by injecting a fault into the middle of a batch and finding the register untouched.</item>
/// <item><b>A signed artifact with no record</b> is prevented by ORDER: signing is a pure function that
/// happens first and produces only values in memory, so a failure there is invisible; delivery happens
/// last and reads a stored token. Proved by making a signature fail mid-batch and finding nothing
/// written.</item>
/// </list>
/// </summary>
public sealed class IssueBatchTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly LicenseRegister _register =
        LicenseRegister.OpenInMemory(() => Now, actor: "tester");

    // ── Atomicity ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryLicenceInABatchIsRecordedTogether()
    {
        SeedThree();

        var result = _register.ApplyIssueBatch(
            [Unit("lid-1"), Unit("lid-2"), Unit("lid-3")], "Extended to 2028-08-15.");

        Assert.Equal(3, result.Artifacts.Count);
        Assert.All(result.Artifacts, a => Assert.True(a.ArtifactId > 0));
        Assert.All(result.Artifacts, a => Assert.Equal(ArtifactStatuses.Current, a.Status));

        Assert.Single(_register.GetArtifacts("lid-1"));
        Assert.Single(_register.GetArtifacts("lid-2"));
        Assert.Single(_register.GetArtifacts("lid-3"));
    }

    [Fact]
    public void AFaultInTheMiddleOfABatchLeavesTheRegisterExactlyAsItWas()
    {
        // ⭐⭐ THE TEST THIS STAGE WAS BUILT FOR. The third unit names a licence that does not exist, so
        //     its INSERT trips the foreign key — the closest stand-in for "the disk went away at row 11".
        //     What matters is not that it throws, but that units one and two are GONE afterwards.
        SeedThree();

        var extended = ExtendedTerms("lid-1", Now.AddYears(3));

        var thrown = Assert.ThrowsAny<SqliteException>(() => _register.ApplyIssueBatch(
        [
            new LicenseIssueUnit { Artifact = Artifact("lid-1"), UpdatedTerms = extended },
            new LicenseIssueUnit { Artifact = Artifact("lid-2"), UpdatedTerms = ExtendedTerms("lid-2", Now.AddYears(3)) },
            new LicenseIssueUnit { Artifact = Artifact("ghost") },
        ]));

        Assert.Contains("FOREIGN KEY", thrown.Message, StringComparison.OrdinalIgnoreCase);

        // No artifact anywhere.
        Assert.Empty(_register.GetArtifacts("lid-1"));
        Assert.Empty(_register.GetArtifacts("lid-2"));
        Assert.Null(_register.GetCurrentArtifact("lid-1"));

        // ⭐ And no term change either — the extension is rolled back with the artifacts it was paired
        //    with, so a customer is never left extended without the file that proves it.
        Assert.Equal(Now.AddYears(1), _register.GetLicense("lid-1")!.ExpiresAt);
        Assert.Equal(Now.AddYears(1), _register.GetLicense("lid-2")!.ExpiresAt);
    }

    [Fact]
    public void AFailedBatchWritesNoHistoryAtAll()
    {
        SeedThree();
        var before = _register.GetAudit(new AuditQuery { Limit = 1000 }).Count;

        Assert.ThrowsAny<SqliteException>(() => _register.ApplyIssueBatch(
            [Unit("lid-1"), Unit("ghost")]));

        // ⚠ A history that records an operation which did not happen is worse than no history: it is the
        //    register asserting something false about what was sent.
        Assert.Equal(before, _register.GetAudit(new AuditQuery { Limit = 1000 }).Count);
        Assert.Empty(_register.GetAudit(new AuditQuery { Action = "licence.batch-issued" }));
    }

    [Fact]
    public void ABatchOfTwentyGoesThroughAsOneOperation()
    {
        // §32's L5 exit criterion: "extend 20 licences in one operation".
        for (var i = 1; i <= 20; i++)
        {
            Seed($"c-{i:0000}", $"Customer {i}", $"lid-{i}");
        }

        var units = Enumerable.Range(1, 20)
            .Select(i => new LicenseIssueUnit
            {
                Artifact = Artifact($"lid-{i}"),
                UpdatedTerms = ExtendedTerms($"lid-{i}", Now.AddYears(2)),
            })
            .ToArray();

        var result = _register.ApplyIssueBatch(units, "Group extension to 2028-08-15.");

        Assert.Equal(20, result.Artifacts.Count);
        Assert.Equal(20, _register.QueryLicenses(new LicenseQuery { NeverIssued = false }).Count);
        Assert.All(
            _register.QueryLicenses(),
            row => Assert.Equal(Now.AddYears(2), row.License.ExpiresAt));
        Assert.Empty(_register.CheckIntegrity());
    }

    // ── The history reads as one decision ───────────────────────────────────────────────────────────

    [Fact]
    public void ABatchIsOneActInTheHistoryAndItsPartsSayWhichAct()
    {
        SeedThree();

        var result = _register.ApplyIssueBatch(
            [Unit("lid-1"), Unit("lid-2")], "Renewals for August.");

        var batchLine = Assert.Single(_register.GetAudit(new AuditQuery { TargetType = "batch" }));
        Assert.Equal("licence.batch-issued", batchLine.Action);
        Assert.Equal(result.BatchId, batchLine.TargetId);
        Assert.Equal("Renewals for August.", batchLine.Note);
        Assert.Contains("lid-1", batchLine.AfterJson!, StringComparison.Ordinal);

        // ⭐ Without the correlation the history shows unrelated changes at one timestamp and nothing that
        //    explains them as a decision somebody took.
        var issued = _register.GetAudit(new AuditQuery { Action = "licence.issued" });
        Assert.Equal(2, issued.Count);
        Assert.All(issued, e => Assert.Contains(result.BatchId, e.Note!, StringComparison.Ordinal));
    }

    [Fact]
    public void ARenewalStoresItsNewTermsAndAPlainReIssueDoesNot()
    {
        SeedThree();

        _register.ApplyIssueBatch(
        [
            new LicenseIssueUnit { Artifact = Artifact("lid-1"), UpdatedTerms = ExtendedTerms("lid-1", Now.AddYears(5)) },
            new LicenseIssueUnit { Artifact = Artifact("lid-2") },
        ]);

        Assert.Equal(Now.AddYears(5), _register.GetLicense("lid-1")!.ExpiresAt);
        Assert.Equal(Now.AddYears(1), _register.GetLicense("lid-2")!.ExpiresAt);

        // ⚠ The re-issue must not claim a change that did not happen.
        var updates = _register.GetAudit(new AuditQuery { Action = "licence.updated" });
        Assert.Equal("lid-1", Assert.Single(updates).TargetId);
    }

    // ── Refusals, before anything is opened ─────────────────────────────────────────────────────────

    [Fact]
    public void TheSameLicenceCannotAppearTwiceInOneBatch()
    {
        // ⭐ Both artifacts would carry the same iat, so the second could never replace the first in the
        //    field (§16.4) — the operator would have shipped a file every client declines.
        SeedThree();

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => _register.ApplyIssueBatch([Unit("lid-1"), Unit("lid-2"), Unit("lid-1")]));

        Assert.Contains("lid-1", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_register.GetArtifacts("lid-1"));
    }

    [Fact]
    public void AUnitCannotPairOneLicencesTermsWithAnothersArtifact()
    {
        SeedThree();

        Assert.Throws<RegisterIntegrityException>(() => _register.ApplyIssueBatch(
        [
            new LicenseIssueUnit { Artifact = Artifact("lid-1"), UpdatedTerms = ExtendedTerms("lid-2", Now.AddYears(3)) },
        ]));

        Assert.Equal(Now.AddYears(1), _register.GetLicense("lid-2")!.ExpiresAt);
    }

    [Fact]
    public void AnEmptyBatchIsRefused()
    {
        Assert.Throws<ArgumentException>(() => _register.ApplyIssueBatch([]));
    }

    [Fact]
    public void ABatchCannotSlipInAnArtifactThatIsNotFresher()
    {
        SeedThree();
        _register.AppendArtifact(Artifact("lid-1", Now.AddMinutes(5)));

        Assert.Throws<RegisterIntegrityException>(
            () => _register.ApplyIssueBatch([Unit("lid-2"), Unit("lid-1")]));

        // The whole batch, including the licence that was perfectly fine.
        Assert.Empty(_register.GetArtifacts("lid-2"));
        Assert.Single(_register.GetArtifacts("lid-1"));
    }

    // ── Phase 1: signing writes nothing ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFailedSIGNATUREInTheMiddleOfABatchRecordsNothingAtAll()
    {
        // ⭐⭐ The other half of the guarantee, and the reason the design signs everything BEFORE opening a
        //     transaction. A signature that throws must leave nothing behind — no row, no file, no
        //     half-signed set — so the honest response is simply to try again.
        using var manager = new ManagerFixture(Now);

        var customer = manager.SaveCustomer("ACME");
        var first = manager.SaveLicense(customer);
        var broken = manager.SaveLicense(customer);
        var third = manager.SaveLicense(customer);

        var workflow = new IssuingWorkflow(manager.Register, () => Now);

        var thrown = Assert.Throws<ArgumentException>(() => workflow.IssueBatch(
            manager.Session,
            [
                new IssueRequest { License = first, Customer = customer, Reason = IssueReasons.Renewal },
                // ⚠ Seats = 0 is refused by the issuer itself — a fault in the MIDDLE of phase 1.
                new IssueRequest { License = broken with { Seats = 0 }, Customer = customer, Reason = IssueReasons.Renewal },
                new IssueRequest { License = third, Customer = customer, Reason = IssueReasons.Renewal },
            ]));

        Assert.Contains("seat", thrown.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(manager.Register.GetArtifacts(first.LicenseId));
        Assert.Empty(manager.Register.GetArtifacts(third.LicenseId));
        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "licence.issued" }));
        Assert.Empty(manager.Register.GetAudit(new AuditQuery { Action = "licence.batch-issued" }));
    }

    [Fact]
    public void EveryArtifactABatchProducesIsOneTheClientAccepts()
    {
        // ⭐ End to end through the real key: the batch's stored tokens are verified by
        //    EmberTern.Licensing — the assembly the customer runs — not by a recomputation of our own.
        using var manager = new ManagerFixture(Now);

        var customer = manager.SaveCustomer("ACME Sp. z o.o.");
        var licences = Enumerable.Range(0, 3).Select(_ => manager.SaveLicense(customer)).ToArray();
        var workflow = new IssuingWorkflow(manager.Register, () => Now);

        var result = workflow.IssueBatch(
            manager.Session,
            licences.Select(l => new IssueRequest
            {
                License = l with { ExpiresAt = Now.AddYears(3) },
                Customer = customer,
                Reason = IssueReasons.Renewal,
                TermsChanged = true,
            }).ToArray(),
            "Group extension.");

        Assert.Equal(3, result.Artifacts.Count);

        foreach (var artifact in result.Artifacts)
        {
            var stored = manager.Register.GetCurrentArtifact(artifact.LicenseId)!;
            var verdict = workflow.Inspect(manager.Session, stored);

            Assert.Equal(LicenseStatus.Valid, verdict.Status);
            Assert.Equal("ACME Sp. z o.o.", verdict.Payload!.Licensee);
            Assert.Equal(Now.AddYears(3), verdict.Payload.ExpiresAt);
        }

        Assert.Empty(manager.Register.CheckIntegrity());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private void SeedThree()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");
        Seed("c-0003", "Gamma", "lid-3");
    }

    private void Seed(string customerId, string name, string licenseId)
    {
        _register.SaveCustomer(new CustomerRecord { CustomerId = customerId, Name = name });
        _register.SaveLicense(new LicenseRecord
        {
            LicenseId = licenseId,
            CustomerId = customerId,
            Product = "EmberTern",
            Seats = 1,
            NotBefore = Now,
            ExpiresAt = Now.AddYears(1),
            Status = LicenseStatuses.Active,
        });
    }

    private LicenseRecord ExtendedTerms(string licenseId, DateTimeOffset expiresAt) =>
        _register.GetLicense(licenseId)! with { ExpiresAt = expiresAt };

    private static LicenseIssueUnit Unit(string licenseId) => new() { Artifact = Artifact(licenseId) };

    private static IssuedArtifactRecord Artifact(string licenseId, DateTimeOffset? issuedAt = null) => new()
    {
        LicenseId = licenseId,
        KeyId = "R1",
        IssuedAt = issuedAt ?? Now,
        PayloadJson = """{"lv":1}""",
        Token = $"ETL1.{licenseId}.signature",
        Reason = IssueReasons.Renewal,
    };

    public void Dispose() => _register.Dispose();
}
