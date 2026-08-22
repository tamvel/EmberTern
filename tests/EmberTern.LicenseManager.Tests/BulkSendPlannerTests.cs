using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L10.2 — the bulk-send PLAN, which is the contract between the preview and the execution.</b>
///
/// <para>Every judgement the planner makes is tested against records this class constructs, because the
/// planner is pure and takes lookups rather than a register (§60.7). ⚠ The two tests that need a REAL
/// artifact — the expiry rule and the composer's verdict — take a <see cref="ManagerFixture"/> and issue
/// one: a signed token is the only thing the payload can honestly be read out of, and faking it would
/// test the fake.</para>
///
/// <para>⛔ Nothing here sends, composes or writes: the planner cannot, and that is the property.</para>
/// </summary>
public sealed class BulkSendPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    // ── §60.3 · the four conditions, one at a time ──────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Condition 1 — <c>blocked</c> is held, and <c>active</c> is the register's OWN definition.
    /// </summary>
    /// <remarks>
    /// ⚠ This is the semantics the licences view's Status filter already reads (<c>LicenseStatuses</c>),
    /// not a status the planner computes. ⛔ And it is genuinely new behaviour for a bulk operation: the
    /// batch RENEWAL has no such filter at all — a blocked licence can be extended today — which is why
    /// §60.3 had to be written from scratch rather than shared.
    /// </remarks>
    [Fact]
    public void ABlockedLicenceIsHeld()
    {
        var plan = PlanOne(Licence(status: LicenseStatuses.Blocked), issued: true);

        var held = Assert.Single(plan.Held);
        Assert.False(held.Qualifies);
        Assert.Empty(plan.Sendable);
        Assert.False(plan.CanExecute);

        Assert.Contains("blocked", held.Hold!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>⭐ Condition 2 — a licence with no current artifact has nothing to send.</summary>
    /// <remarks>
    /// ⭐ This is also what makes <c>superseded</c> unreachable rather than filtered: the plan reads the
    /// POINTER, and the pointer never points at a superseded artifact.
    /// </remarks>
    [Fact]
    public void ALicenceThatWasNeverIssuedIsHeld()
    {
        var plan = PlanOne(Licence(), issued: false);

        var held = Assert.Single(plan.Held);
        Assert.Null(held.CurrentArtifact);
        Assert.Empty(plan.Sendable);
    }

    /// <summary>
    /// ⭐⭐ Condition 3 — the expiry is judged on the ARTIFACT THAT WOULD TRAVEL, not on the licence row.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The test makes the two disagree on purpose: the artifact is signed with an expiry in the past and
    /// the ROW is then moved to a future one without issuing anything. A planner reading the row would
    /// happily send a licence whose attachment is already dead — §14.2's rule is that the words and the
    /// attachment come from the same bytes, and this is where it bites.
    /// </remarks>
    [Fact]
    public void AnExpiredArtifactIsHeld_EvenWhenTheRowSaysOtherwise()
    {
        using var manager = new ManagerFixture(Now);
        var customer = manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");

        // The artifact is signed with an expiry that has already passed.
        var lapsed = manager.Register.SaveLicense(new LicenseRecord
        {
            LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
            CustomerId = customer.CustomerId,
            Product = EmberTern.Licensing.LicenseConstants.ProductId,
            Seats = 5,
            NotBefore = Now.AddYears(-2),
            ExpiresAt = Now.AddDays(-1),
            Status = LicenseStatuses.Active,
        });

        manager.Workflow.Issue(manager.Session, lapsed, customer, IssueReasons.Initial);

        // ⚠ And now the ROW is moved forward, without issuing anything — a legitimate saved-but-not-issued
        //   state, and the one that makes reading the row wrong.
        manager.Register.SaveLicense(lapsed with { ExpiresAt = Now.AddYears(1) });

        var plan = PlanFrom(manager, customer);

        var held = Assert.Single(plan.Held);
        Assert.Contains("2026-08-21", held.Hold!.ToString(), StringComparison.Ordinal);
        Assert.Empty(plan.Sendable);
    }

    /// <summary>
    /// ⭐⭐ <b><c>NotYetValid</c> IS sent</b> — an artifact that takes effect later is an ordinary renewal.
    /// </summary>
    /// <remarks>
    /// 🔒 §60.3, ratified: only an <c>exp</c> already in the past is useless to a customer. ⛔ Excluding a
    /// licence because it has not started yet would make it impossible to send next year's renewal in
    /// advance, which is the normal way a renewal reaches anybody.
    /// </remarks>
    [Fact]
    public void AnArtifactThatHasNotStartedYetIsStillSent()
    {
        using var manager = new ManagerFixture(Now);
        var customer = manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");

        var future = manager.Register.SaveLicense(new LicenseRecord
        {
            LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
            CustomerId = customer.CustomerId,
            Product = EmberTern.Licensing.LicenseConstants.ProductId,
            Seats = 5,
            NotBefore = Now.AddMonths(4),
            ExpiresAt = Now.AddMonths(16),
            Status = LicenseStatuses.Active,
        });

        manager.Workflow.Issue(manager.Session, future, customer, IssueReasons.Initial);

        var plan = PlanFrom(manager, customer);

        Assert.Empty(plan.Held);
        Assert.Single(plan.Sendable);
        Assert.True(plan.CanExecute);
    }

    /// <summary>
    /// ⭐⭐ Condition 4 — the COMPOSER's own verdict, verbatim, so there is one authority about what can be sent.
    /// </summary>
    /// <remarks>
    /// ⚠ The customer has no e-mail address, which is the composer's own first problem. ⛔ The planner does
    /// not restate it: a second opinion is how two surfaces start disagreeing about the same licence.
    /// </remarks>
    [Fact]
    public void ACustomerWithNoAddressIsHeld_InTheComposersOwnWords()
    {
        using var manager = new ManagerFixture(Now);
        var customer = manager.SaveCustomer("Beta S.A.", email: null);
        var licence = manager.SaveLicense(customer);

        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);

        var plan = PlanFrom(manager, customer);
        var held = Assert.Single(plan.Held);

        // ⭐ Asserted against what the composer itself says, not against a literal — so the two cannot drift.
        var artifact = manager.Register.GetCurrentArtifact(licence.LicenseId)!;
        var expected = LicenseMessageComposer.Problems(artifact, customer, Settings());

        Assert.NotEmpty(expected);
        Assert.Equal(expected[0].ToString(), held.Hold!.ToString());
    }

    /// <summary>⚠ A licence naming a customer the register does not have is a REGISTER fault, and it is named.</summary>
    [Fact]
    public void ALicenceWhoseCustomerIsGoneIsHeld()
    {
        var plan = BulkSendPlanner.Plan(
            [Summary(Licence())],
            _ => Artifact(Licence()),
            _ => null,
            new Dictionary<string, DateTimeOffset>(),
            Settings(),
            Now,
            skipAlreadySent: false);

        Assert.Single(plan.Held);
        Assert.Empty(plan.Sendable);
    }

    // ── "Already sent" ──────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ A licence sent since its current artifact was issued is SKIPPED, and stays in the plan.</summary>
    /// <remarks>
    /// ⚠ Skipped, not dropped: §60.2 requires the report to be able to say how many were skipped and why,
    /// and a licence removed from the list cannot be counted.
    /// </remarks>
    [Fact]
    public void ALicenceAlreadySentSinceItsArtifactIsSkipped()
    {
        var licence = Licence();
        var plan = PlanOne(
            licence,
            issued: true,
            issuedAt: Now.AddDays(-10),
            lastSentAt: Now.AddDays(-9),
            skipAlreadySent: true);

        var skipped = Assert.Single(plan.Skipped);

        Assert.True(skipped.Qualifies);
        Assert.Equal(BulkSendPlanned.Skip, skipped.Action);
        Assert.NotNull(skipped.SkipReason);
        Assert.Empty(plan.Sendable);

        // ⭐ It is IN the run's denominator, which is what makes the report able to account for it.
        Assert.Equal(1, plan.Planned);
        Assert.Empty(plan.Held);
    }

    /// <summary>
    /// ⭐⭐ <b>A RENEWAL is not skipped, and this is the assertion that makes the option safe to default to ON.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The licence WAS sent — a year before the current artifact was issued. A rule keyed on "has this
    /// ever been sent" would skip every renewal there is, silently, and the customer would never receive
    /// the licence they just paid for. 🔒 §60.7 states the comparison as <c>lastSent &gt;= iat</c> for
    /// exactly this reason.
    /// </remarks>
    [Fact]
    public void ARenewalIsNotSkipped_EvenThoughTheLicenceWasSentBefore()
    {
        var plan = PlanOne(
            Licence(),
            issued: true,
            issuedAt: Now.AddDays(-1),
            lastSentAt: Now.AddYears(-1),
            skipAlreadySent: true);

        Assert.Empty(plan.Skipped);
        Assert.Single(plan.Sendable);
        Assert.True(plan.CanExecute);
    }

    /// <summary>⚠ Issued and sent in the same instant counts as SENT — that is the single path's ordinary order.</summary>
    [Fact]
    public void SentInTheSameInstantAsTheIssueCountsAsSent()
    {
        var plan = PlanOne(
            Licence(), issued: true, issuedAt: Now, lastSentAt: Now, skipAlreadySent: true);

        Assert.Single(plan.Skipped);
    }

    /// <summary>⭐ With the option off, nothing is skipped however recently it was sent.</summary>
    [Fact]
    public void WithTheOptionOffNothingIsSkipped()
    {
        var plan = PlanOne(
            Licence(), issued: true, issuedAt: Now.AddDays(-2), lastSentAt: Now, skipAlreadySent: false);

        Assert.Empty(plan.Skipped);
        Assert.Single(plan.Sendable);
    }

    // ── Duplicates, pacing, the limit ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Two licences for one address are BOTH sent, and the fact is COUNTED so it can be said out loud.
    /// </summary>
    /// <remarks>
    /// ⛔ Merging them into one message is forbidden (§60.0) and could not work anyway: the attachment has
    /// a fixed file name, so two would collide. ⭐ So the plan reports how many ADDRESSES receive more than
    /// one message — addresses, not extra messages, which is what the sentence it feeds says.
    /// </remarks>
    [Fact]
    public void TwoLicencesForOneAddressAreBothSentAndTheAddressIsCountedOnce()
    {
        var one = Licence();
        var two = Licence();
        var owner = Customer("biuro@acme.test");

        var plan = BulkSendPlanner.Plan(
            [Summary(one), Summary(two)],
            id => Artifact(id == one.LicenseId ? one : two),
            _ => owner,
            new Dictionary<string, DateTimeOffset>(),
            Settings(),
            Now,
            skipAlreadySent: false);

        Assert.Equal(2, plan.Sendable.Count);
        Assert.Equal(1, plan.RecipientCount);
        Assert.Equal(1, plan.DuplicateRecipientCount);
    }

    /// <summary>⭐ Distinct addresses are not counted as duplicates.</summary>
    [Fact]
    public void DistinctAddressesAreNotDuplicates()
    {
        // ⚠ Two DIFFERENT customer ids: the first draft gave both licences `C-1`, so the lookup handed
        //   back one address twice and the test failed on its own scaffolding rather than on the planner.
        var one = Licence(customerId: "C-1");
        var two = Licence(customerId: "C-2");

        var plan = BulkSendPlanner.Plan(
            [Summary(one, "ACME"), Summary(two, "Beta")],
            id => Artifact(id == one.LicenseId ? one : two),
            id => Customer(id == one.CustomerId ? "biuro@acme.test" : "kontakt@beta.test", id),
            new Dictionary<string, DateTimeOffset>(),
            Settings(),
            Now,
            skipAlreadySent: false);

        Assert.Equal(2, plan.RecipientCount);
        Assert.Equal(0, plan.DuplicateRecipientCount);
    }

    /// <summary>
    /// ⭐ The stated duration counts the GAPS, so one message waits for nothing.
    /// </summary>
    /// <remarks>
    /// ⚠ It is a floor, never an estimate: the time each server conversation takes is unknown before it
    /// happens, and §60.9 requires the figure to be presented as "at least".
    /// </remarks>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 15)]
    [InlineData(5, 60)]
    public void TheStatedDurationCountsTheGapsNotTheMessages(int messages, int expectedSeconds)
    {
        var plan = PlanMany(messages);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), plan.MinimumDuration);
    }

    /// <summary>
    /// ⛔ Over the run limit DISABLES the action rather than warning about it, and says so as a flag.
    /// </summary>
    [Fact]
    public void OverTheRunLimitTheRunCannotStart()
    {
        var plan = PlanMany(4, settings: Settings() with { BulkMaxPerRun = 3 });

        Assert.True(plan.ExceedsRunLimit);
        Assert.False(plan.CanExecute);

        // ⚠ And exactly at the limit it CAN — an off-by-one here would be a limit nobody could reach.
        Assert.False(PlanMany(3, settings: Settings() with { BulkMaxPerRun = 3 }).ExceedsRunLimit);
    }

    /// <summary>
    /// ⭐ The limit is measured against the messages that would be ATTEMPTED, not against the ticks.
    /// </summary>
    /// <remarks>
    /// ⚠ Skipping already-sent licences legitimately brings a selection back under the limit — and held
    /// ones never counted towards it in the first place.
    /// </remarks>
    [Fact]
    public void TheLimitCountsAttemptedMessagesNotTicks()
    {
        var sendable = Licence();
        var alreadySent = Licence();
        var blocked = Licence(status: LicenseStatuses.Blocked);

        var plan = BulkSendPlanner.Plan(
            [Summary(sendable), Summary(alreadySent), Summary(blocked)],
            id => Artifact(new LicenseRecord
            {
                LicenseId = id,
                CustomerId = "C-1",
                Product = EmberTern.Licensing.LicenseConstants.ProductId,
                Seats = 5,
                NotBefore = Now.AddYears(-1),
                ExpiresAt = Now.AddYears(1),
                Status = LicenseStatuses.Active,
            }, issuedAt: Now.AddDays(-5)),
            _ => Customer("biuro@acme.test"),
            new Dictionary<string, DateTimeOffset> { [alreadySent.LicenseId] = Now.AddDays(-1) },
            Settings() with { BulkMaxPerRun = 1 },
            Now,
            skipAlreadySent: true);

        Assert.Single(plan.Sendable);
        Assert.Single(plan.Skipped);
        Assert.Single(plan.Held);
        Assert.Equal(2, plan.Planned);

        Assert.False(plan.ExceedsRunLimit);
        Assert.True(plan.CanExecute);
    }

    /// <summary>⭐ An empty selection cannot start a run, and says so without pretending anything is wrong.</summary>
    [Fact]
    public void AnEmptySelectionCannotStartARun()
    {
        var plan = PlanMany(0);

        Assert.True(plan.IsEmpty);
        Assert.False(plan.CanExecute);
        Assert.Equal(0, plan.Planned);
        Assert.Equal(TimeSpan.Zero, plan.MinimumDuration);
    }

    // ── Matches — the semantic-atomicity check ──────────────────────────────────────────────────────

    /// <summary>⭐ The same inputs plan the same run.</summary>
    [Fact]
    public void APlanMatchesAnIdenticalReplan()
    {
        var licences = Licences(3);

        Assert.True(PlanList(licences).Matches(PlanList(licences)));
    }

    /// <summary>⭐⭐ A licence that gained a HOLD between the preview and the click is a different plan.</summary>
    [Fact]
    public void AGainedHoldBreaksTheMatch()
    {
        var licence = Licence();
        var approved = PlanOne(licence, issued: true);
        var fresh = PlanOne(licence with { Status = LicenseStatuses.Blocked }, issued: true);

        Assert.False(approved.Matches(fresh));
        Assert.False(fresh.Matches(approved));
    }

    /// <summary>⭐⭐ A RECIPIENT that changed is a different plan — the same licence to a different address.</summary>
    /// <remarks>
    /// ⚠ This is the case a licence-id comparison alone would miss: nothing about the selection moved, and
    /// the message would go somewhere the operator never saw.
    /// </remarks>
    [Fact]
    public void AChangedRecipientBreaksTheMatch()
    {
        var licence = Licence();

        var approved = PlanOne(licence, issued: true, email: "biuro@acme.test");
        var fresh = PlanOne(licence, issued: true, email: "ktos.inny@acme.test");

        Assert.False(approved.Matches(fresh));
    }

    /// <summary>⭐ A licence that would now be SKIPPED is a different plan.</summary>
    [Fact]
    public void AGainedSkipBreaksTheMatch()
    {
        var licence = Licence();

        var approved = PlanOne(licence, issued: true, issuedAt: Now.AddDays(-3), skipAlreadySent: true);
        var fresh = PlanOne(
            licence, issued: true, issuedAt: Now.AddDays(-3), lastSentAt: Now, skipAlreadySent: true);

        Assert.False(approved.Matches(fresh));
    }

    /// <summary>⭐ Order matters: two plans over the same licences in a different order are not the same run.</summary>
    [Fact]
    public void AChangedOrderBreaksTheMatch()
    {
        var one = Licence();
        var two = Licence();

        var forward = PlanPair(one, two);
        var reversed = PlanPair(two, one);

        Assert.False(forward.Matches(reversed));
    }

    /// <summary>⭐ A licence added or removed is a different plan.</summary>
    [Fact]
    public void AChangedCountBreaksTheMatch()
    {
        var three = Licences(3);

        Assert.False(PlanList(three).Matches(PlanList([.. three, Licence()])));
    }

    /// <summary>
    /// ⭐⭐ The PACING is part of the approval: the operator confirmed a run that takes a stated time.
    /// </summary>
    /// <remarks>
    /// ⚠ A delay changed in the Settings window while the confirmation was on screen would otherwise turn
    /// an approved nine-minute run into a ten-hour one, or into no pacing at all.
    /// </remarks>
    [Fact]
    public void ChangedPacingOrLimitBreaksTheMatch()
    {
        var licences = Licences(3);

        Assert.False(PlanList(licences)
            .Matches(PlanList(licences, Settings() with { BulkDelaySeconds = 30 })));

        Assert.False(PlanList(licences)
            .Matches(PlanList(licences, Settings() with { BulkMaxPerRun = 10 })));
    }

    /// <summary>
    /// ⚠⚠ A hold's WORDS are not part of the identity — only the verdict is.
    /// </summary>
    /// <remarks>
    /// ⭐ The interface language may legitimately change while a confirmation is on screen, and that must
    /// not read as "the plan moved". The sentence is presentation; the verdict it explains is the contract.
    /// ⛔ So `Matches` deliberately compares neither `Hold` nor `SkipReason` text.
    /// </remarks>
    [Fact]
    public void TheReasonTextIsNotPartOfThePlansIdentity()
    {
        var licence = Licence(status: LicenseStatuses.Blocked);

        var approved = PlanOne(licence, issued: true);
        var reworded = approved with
        {
            Candidates =
            [
                approved.Candidates[0] with
                {
                    Hold = new Localization.LocalizedText(
                        ViewModels.StatusCatalog.BulkHoldNeverIssued),
                },
            ],
        };

        Assert.True(approved.Matches(reworded));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static SmtpSettings Settings() => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern",
        MessageLanguage = MessageLanguages.Polish,
    };

    private static LicenseRecord Licence(string? status = null, string customerId = "C-1") => new()
    {
        LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
        CustomerId = customerId,
        Product = EmberTern.Licensing.LicenseConstants.ProductId,
        Seats = 5,
        NotBefore = Now.AddYears(-1),
        ExpiresAt = Now.AddYears(1),
        Status = status ?? LicenseStatuses.Active,
    };

    private static LicenseSummary Summary(LicenseRecord licence, string name = "ACME Sp. z o.o.") => new()
    {
        License = licence,
        CustomerName = name,
        ArtifactCount = 1,
        LastIssuedAt = Now.AddDays(-1),
    };

    private static CustomerRecord Customer(string? email, string id = "C-1") => new()
    {
        CustomerId = id,
        Name = "ACME Sp. z o.o.",
        Email = email,
    };

    /// <summary>
    /// An artifact whose SIGNED payload matches the licence — built through the real issuer.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>The token is genuinely signed, not stubbed, and it has to be:</b> the planner reads the expiry
    /// out of the TOKEN (as the composer does), so a fabricated one would make the expiry rule untestable —
    /// it would fall through to "unreadable" and be held for the wrong reason. ⭐ The key is a throwaway
    /// generated per call; nothing here touches a real keystore.
    /// </remarks>
    private static IssuedArtifactRecord Artifact(LicenseRecord licence, DateTimeOffset? issuedAt = null)
    {
        var at = issuedAt ?? Now.AddDays(-1);
        var token = TestArtifacts.Token(licence, "ACME Sp. z o.o.", at);

        return new IssuedArtifactRecord
        {
            ArtifactId = 1,
            LicenseId = licence.LicenseId,
            KeyId = TestArtifacts.KeyId,
            IssuedAt = at,
            PayloadJson = "{}",
            Token = token,
            Reason = IssueReasons.Initial,
        };
    }

    private static BulkSendPlan PlanOne(
        LicenseRecord licence,
        bool issued,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? lastSentAt = null,
        bool skipAlreadySent = false,
        string? email = "biuro@acme.test") =>
        BulkSendPlanner.Plan(
            [Summary(licence)],
            _ => issued ? Artifact(licence, issuedAt) : null,
            _ => Customer(email),
            lastSentAt is { } sent
                ? new Dictionary<string, DateTimeOffset> { [licence.LicenseId] = sent }
                : new Dictionary<string, DateTimeOffset>(),
            Settings(),
            Now,
            skipAlreadySent);

    private static BulkSendPlan PlanPair(LicenseRecord first, LicenseRecord second) =>
        BulkSendPlanner.Plan(
            [Summary(first), Summary(second)],
            id => Artifact(id == first.LicenseId ? first : second),
            _ => Customer("biuro@acme.test"),
            new Dictionary<string, DateTimeOffset>(),
            Settings(),
            Now,
            skipAlreadySent: false);

    private static List<LicenseRecord> Licences(int count) =>
        Enumerable.Range(0, count).Select(_ => Licence()).ToList();

    /// <summary>Plans over licences the CALLER owns.</summary>
    /// <remarks>
    /// ⚠⚠ Split out from <see cref="PlanMany"/> because two <c>Matches</c> tests failed on their first
    /// run for a scaffolding reason worth remembering: <c>PlanMany(3)</c> called twice minted three NEW
    /// licence ids each time, so the two plans legitimately did not match and the test looked like a
    /// defect in <c>Matches</c>. ⭐ A test about "the same plan twice" has to plan the same THINGS twice.
    /// </remarks>
    private static BulkSendPlan PlanList(
        IReadOnlyList<LicenseRecord> licences, SmtpSettings? settings = null)
    {
        var byId = licences.ToDictionary(l => l.LicenseId, StringComparer.Ordinal);

        return BulkSendPlanner.Plan(
            licences.Select(l => Summary(l)).ToList(),
            id => Artifact(byId[id]),
            _ => Customer("biuro@acme.test"),
            new Dictionary<string, DateTimeOffset>(),
            settings ?? Settings(),
            Now,
            skipAlreadySent: false);
    }

    private static BulkSendPlan PlanMany(int count, SmtpSettings? settings = null) =>
        PlanList(Licences(count), settings);

    private static BulkSendPlan PlanFrom(ManagerFixture manager, CustomerRecord customer) =>
        BulkSendPlanner.Plan(
            manager.Register.QueryLicenses(),
            manager.Register.GetCurrentArtifact,
            _ => customer,
            manager.Register.GetLastSentAt(),
            Settings(),
            Now,
            skipAlreadySent: false);
}
