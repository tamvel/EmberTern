using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// What a batch renewal WOULD do — the judgement the preview shows and the execution runs.
///
/// <para>⭐ Pure: the planner takes the previous artifact as a lookup rather than taking a register, so
/// every case here is built rather than seeded. ⛔ Nothing in this file asserts that a method was called;
/// each one asserts the verdict an operator would read off the screen.</para>
/// </summary>
public sealed class BatchRenewalPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTickedLicenceIsExtendedToTheChosenDay()
    {
        var plan = Plan(new DateTime(2029, 1, 31), Issued("lid-1"), Issued("lid-2"));

        Assert.True(plan.CanExecute);
        Assert.Equal(2, plan.Qualifying.Count);

        foreach (var candidate in plan.Candidates)
        {
            // ⭐ The END of the chosen day, through LicenseDay — the same owner the licence form uses.
            //    Midnight would expire a licence at the start of the day it says it is valid until.
            Assert.Equal(new DateTimeOffset(2029, 1, 31, 23, 59, 59, TimeSpan.Zero), candidate.RenewedTerms.ExpiresAt);
            Assert.Equal("2029-01-31", candidate.NewExpiry);
        }
    }

    [Fact]
    public void ALicenceThatWasNeverIssuedIsRecordedAsInitialRatherThanAsARenewal()
    {
        // ⭐⭐ D‑2. `initial` is not a choice and never was — it is what the register PROVES about a
        //     licence with no earlier artifact. Recording such a licence as a "renewal" would be a
        //     permanent lie in an append-only column, which is the defect L5.3 existed to repair.
        var plan = Plan(new DateTime(2029, 1, 31), Issued("lid-1"), NeverIssued("lid-2"));

        Assert.Equal(IssueReasons.Renewal, Candidate(plan, "lid-1").Reason);
        Assert.Equal(IssueReasons.Initial, Candidate(plan, "lid-2").Reason);
        Assert.True(plan.CanExecute);
    }

    [Fact]
    public void ThePlanCountsFirstIssuesSeparatelyFromRenewals()
    {
        // ⭐ D‑2 requires the preview to SAY how many licences would be issued for the first time. An
        //   operator who believes they are renewing twenty existing customers must be told that two of
        //   them are being sent something they have never had.
        var plan = Plan(
            new DateTime(2029, 1, 31),
            Issued("lid-1"), NeverIssued("lid-2"), NeverIssued("lid-3"));

        Assert.Equal(2, plan.FirstIssues);
        Assert.Equal(1, plan.Renewals);
    }

    [Fact]
    public void ALicenceAlreadyValidToTheTargetDateIsBlocked()
    {
        // The operation is called EXTEND. A target that does not extend is the operator's mistake, and
        // it is the one the register can see.
        var plan = Plan(new DateTime(2027, 8, 17), Issued("lid-1", expires: new DateTime(2029, 1, 1)));

        var candidate = Candidate(plan, "lid-1");
        Assert.False(candidate.Qualifies);
        Assert.Contains("would not extend it", candidate.Blocker);
        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void ATargetDateBeforeTheLicencesOwnStartDateIsBlocked()
    {
        // ⭐ A licence that ends before it begins is something the licence FORM already refuses. A batch
        //   that could write one would be a second door into a state the single path forbids.
        var summary = Issued("lid-1", notBefore: new DateTime(2030, 1, 1), expires: new DateTime(2030, 6, 1));
        var plan = Plan(new DateTime(2029, 1, 1), summary);

        var candidate = Candidate(plan, "lid-1");
        Assert.False(candidate.Qualifies);
        Assert.Contains("start date", candidate.Blocker);
    }

    [Fact]
    public void OneBlockedLicenceHoldsTheWholeOperationWithoutHidingTheRest()
    {
        // ⭐⭐ D‑3, both halves. The operation is held — AND the nineteen that were fine are still listed
        //    as qualifying, because the operator has to be able to see what they would lose by removing
        //    the wrong one. ⛔ A plan that silently dropped the blocker would be a partial batch.
        var plan = Plan(
            new DateTime(2028, 1, 1),
            Issued("lid-1"),
            Issued("lid-2", expires: new DateTime(2030, 1, 1)),
            Issued("lid-3"));

        Assert.False(plan.CanExecute);
        Assert.Equal(2, plan.Qualifying.Count);
        Assert.Equal("lid-2", Assert.Single(plan.Blocked).LicenseId);
        Assert.Equal(3, plan.Candidates.Count);
    }

    [Fact]
    public void EveryQualifyingLicenceStoresItsNewTerms()
    {
        // ⚠ TermsChanged is a different question from Reason: it decides whether the batch writes a new
        //   licence ROW. For an extension it is always true, and a false one would leave the register
        //   holding the OLD expiry while the customer holds an artifact carrying the new one.
        var plan = Plan(new DateTime(2029, 1, 31), Issued("lid-1"), NeverIssued("lid-2"));

        Assert.All(plan.Qualifying, candidate => Assert.True(candidate.TermsChanged));
    }

    [Fact]
    public void TheDiffFollowsTheCurrentArtifactPointerAndNotTheNewestRow()
    {
        // ⭐⭐ §39.2 / §45.6. The pointer is the authority on which release the customer is HOLDING. A
        //    plan measured against "the newest row" would judge a renewal against an artifact that was
        //    superseded — and here that is the difference between a blocked operation and a running one.
        var summary = Issued("lid-1", expires: new DateTime(2027, 1, 1));

        // The POINTER says the customer holds an artifact that already runs to 2030.
        var pointer = Artifact("lid-1", signedExpiry: LicenseDay.EndOf(new DateTime(2030, 1, 1)));

        var plan = BatchRenewalPlanner.Plan(
            [summary],
            LicenseDay.EndOf(new DateTime(2030, 1, 1)),
            _ => pointer);

        // ⚠ The ROW expiry (2027) moves, so the "does it extend?" question says yes — and the policy
        //   still refuses, because the artifact the customer holds already expires exactly then.
        var candidate = Candidate(plan, "lid-1");
        Assert.False(candidate.Qualifies);
        Assert.Contains("has not moved", candidate.Blocker);
    }

    [Fact]
    public void AnUnreadablePreviousPayloadDoesNotBlockTheOperation()
    {
        // ⚠⚠ CanCompare == false means UNKNOWN, never UNCHANGED (§45.2). The artifact a support call is
        //    about is precisely the one that will not parse, and refusing to re-issue there would turn a
        //    display problem into an operational one on the day the operator can least afford it.
        var summary = Issued("lid-1");
        var broken = Artifact("lid-1") with { PayloadJson = "{ not json" };

        var plan = BatchRenewalPlanner.Plan(
            [summary], LicenseDay.EndOf(new DateTime(2029, 1, 1)), _ => broken);

        Assert.True(plan.CanExecute);
        Assert.Equal(IssueReasons.Renewal, Candidate(plan, "lid-1").Reason);
    }

    [Fact]
    public void AnEmptySelectionCannotBeExecuted()
    {
        var plan = BatchRenewalPlanner.Plan([], LicenseDay.EndOf(new DateTime(2029, 1, 1)), _ => null);

        Assert.True(plan.IsEmpty);
        Assert.False(plan.CanExecute);
    }

    // ── The semantic-atomicity comparison ───────────────────────────────────────────────────────────

    [Fact]
    public void APlanMatchesItselfAndNotADifferentOperation()
    {
        // ⭐⭐ This is what makes "what the preview showed is what runs" checkable rather than assumed.
        var day = new DateTime(2029, 1, 31);
        var plan = Plan(day, Issued("lid-1"), Issued("lid-2"));

        Assert.True(plan.Matches(Plan(day, Issued("lid-1"), Issued("lid-2"))));

        // A licence removed.
        Assert.False(plan.Matches(Plan(day, Issued("lid-1"))));

        // A licence swapped.
        Assert.False(plan.Matches(Plan(day, Issued("lid-1"), Issued("lid-9"))));

        // The target date moved.
        Assert.False(plan.Matches(Plan(new DateTime(2029, 2, 1), Issued("lid-1"), Issued("lid-2"))));

        // A licence that has since been issued, so its reason would change from initial to renewal.
        Assert.False(Plan(day, NeverIssued("lid-1")).Matches(Plan(day, Issued("lid-1"))));

        // A licence that has since become blocked.
        Assert.False(plan.Matches(
            Plan(day, Issued("lid-1"), Issued("lid-2", expires: new DateTime(2030, 1, 1)))));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static BatchRenewalPlan Plan(DateTime targetDay, params LicenseSummary[] selected)
    {
        var artifacts = new Dictionary<string, IssuedArtifactRecord>(StringComparer.Ordinal);
        foreach (var summary in selected.Where(s => !s.NeverIssued))
        {
            artifacts[summary.License.LicenseId] =
                Artifact(summary.License.LicenseId, summary.License.ExpiresAt);
        }

        return BatchRenewalPlanner.Plan(
            selected,
            LicenseDay.EndOf(targetDay),
            lid => artifacts.TryGetValue(lid, out var found) ? found : null);
    }

    private static BatchRenewalCandidate Candidate(BatchRenewalPlan plan, string licenseId) =>
        plan.Candidates.Single(c => c.LicenseId == licenseId);

    private static LicenseSummary Issued(
        string licenseId, DateTime? notBefore = null, DateTime? expires = null) =>
        Summary(licenseId, notBefore, expires, artifacts: 1);

    private static LicenseSummary NeverIssued(
        string licenseId, DateTime? notBefore = null, DateTime? expires = null) =>
        Summary(licenseId, notBefore, expires, artifacts: 0);

    private static LicenseSummary Summary(
        string licenseId, DateTime? notBefore, DateTime? expires, int artifacts) => new()
        {
            License = new LicenseRecord
            {
                LicenseId = licenseId,
                CustomerId = "c-0001",
                Product = EmberTern.Licensing.LicenseConstants.ProductId,
                Seats = 5,
                NotBefore = LicenseDay.StartOf(notBefore ?? new DateTime(2026, 1, 1)),
                ExpiresAt = LicenseDay.EndOf(expires ?? new DateTime(2027, 1, 1)),
                Status = LicenseStatuses.Active,
            },
            CustomerName = "ACME Sp. z o.o.",
            ArtifactCount = artifacts,
            LastIssuedAt = artifacts == 0 ? null : Now,
            CurrentArtifactId = artifacts == 0 ? null : 1,
        };

    // ⭐ The stored artifact carries a payload written by the PRODUCT's own serialiser, not by a JSON
    //    literal here — so field names and timestamp shape have one definition, and a test cannot pass
    //    against a payload the real parser would reject.
    private static IssuedArtifactRecord Artifact(string licenseId, DateTimeOffset? signedExpiry = null)
    {
        var payload = new EmberTern.Licensing.LicensePayload
        {
            Version = 1,
            KeyId = "R1",
            AlgorithmId = EmberTern.Licensing.SignatureAlgorithmIds.EcdsaP256Sha256,
            LicenseId = licenseId,
            Product = EmberTern.Licensing.LicenseConstants.ProductId,
            Licensee = "ACME Sp. z o.o.",
            Seats = 5,
            IssuedAt = Now,
            NotBefore = LicenseDay.StartOf(new DateTime(2026, 1, 1)),
            ExpiresAt = signedExpiry ?? LicenseDay.EndOf(new DateTime(2027, 1, 1)),
        };

        return new IssuedArtifactRecord
        {
            LicenseId = licenseId,
            KeyId = "R1",
            IssuedAt = Now,
            PayloadJson = System.Text.Encoding.UTF8.GetString(payload.WriteJson()),
            Token = "ETL1.token",
            Reason = IssueReasons.Initial,
        };
    }
}
