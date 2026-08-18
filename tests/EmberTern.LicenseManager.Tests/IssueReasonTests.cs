using System;
using System.Linq;
using System.Reflection;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// L5.3 — the issuing reason is CHOSEN by the operator, and a claim the register can disprove is refused
/// before it becomes a permanent row.
///
/// <para>⭐⭐ <b>What these guard is not a feature but a repair.</b> Until L5.3 the reason was computed as
/// <c>artifacts.Count == 0 ? initial : renewal</c>, in direct contradiction of the contract written on
/// <see cref="IssueRequest.Reason"/> — <i>chosen by the operator, never inferred from a diff</i>. Two of
/// the four vocabulary values were unreachable by any code path, and every re-issue was filed as a
/// renewal whether or not an expiry had ever moved. <c>issued_artifacts</c> is append-only, so each of
/// those rows is wrong forever.</para>
///
/// <para>⭐ <b>The governing rule under test: refuse what can be DISPROVED, allow what cannot be judged.</b>
/// Renewal and terms-change each assert something checkable. "The customer lost their file" asserts
/// something about a person, so it is never refused — only steered (D‑6).</para>
/// </summary>
public sealed class IssueReasonTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly ManagerFixture _manager = new(Now);

    public void Dispose() => _manager.Dispose();

    // ── The vocabulary itself (D-3) ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TheVocabularyIsExactlyFourValuesAndTheyArePersistedVerbatim()
    {
        // ⭐ D-3, pinned as a COUNT rather than as a list of names. `reason` is append-only and stored
        //   verbatim, so a fifth value is a permanent addition to what the register can contain — that is
        //   a decision for the owner, not something a later stage should be able to do by adding a const.
        var values = typeof(IssueReasons)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(4, values.Length);
        Assert.Equal(
            ["initial", "reissue-lost", "renewal", "terms-change"],
            values.OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryOfferedReasonReachesTheOperatorAsWordsAndAsAnExplanation()
    {
        // ⚠ Asserted through the surface the operator actually reads, not against the mapping class: a
        //   perfectly correct mapping that no control consumes is the failure mode rule 12 records, and
        //   a test that calls the mapping directly cannot tell the two apart.
        var shell = NewShell();
        Issue(shell);

        Assert.All(shell.IssueReasonChoices, option =>
        {
            Assert.NotEqual(option.Value, option.Label);
            Assert.NotEmpty(option.Explanation);
        });

        Choose(shell, IssueReasons.ReissueLost);
        Assert.Equal(shell.SelectedIssueReason!.Explanation, shell.IssueReasonExplanation);
    }

    [Fact]
    public void AReasonThisVersionDoesNotRecogniseIsShownVerbatimRatherThanAsUnknown()
    {
        // ⭐ `reason` is append-only and its vocabulary can only grow, so a register written by a later
        //   version must stay readable in an older one — and the raw value is always more informative
        //   than our word for not recognising it.
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, "some-future-reason");

        var shell = new ShellViewModel(_manager.Register, _manager.Session, _manager.Paths, () => _manager.Now);
        shell.SelectedCustomer = shell.Customers.First(c => c.CustomerId == customer.CustomerId);
        shell.SelectedLicense = shell.Licenses.First(l => l.LicenseId == licence.LicenseId);

        Assert.Equal("some-future-reason", shell.History.Artifacts.Single().Reason);
    }

    // ── D-2: the first issue is not a question ──────────────────────────────────────────────────────

    [Fact]
    public void BeforeTheFirstIssueThereIsExactlyOneTruthfulReasonAndItIsNotOffered()
    {
        var shell = NewShell();

        Assert.False(shell.CanChooseIssueReason);
        Assert.Single(shell.IssueReasonChoices);
        Assert.Equal(IssueReasons.Initial, shell.IssueReasonChoices[0].Value);
        Assert.Equal(IssueReasons.Initial, shell.SelectedIssueReason!.Value);
    }

    [Fact]
    public void AFirstIssueIsRecordedAsInitial()
    {
        var shell = NewShell();

        Issue(shell);

        Assert.Equal(IssueReasons.Initial, OnlyArtifact(shell).Reason);
    }

    [Fact]
    public void AfterTheFirstIssueTheOperatorMustChooseAndInitialIsNoLongerOffered()
    {
        var shell = NewShell();
        Issue(shell);

        Assert.True(shell.CanChooseIssueReason);
        Assert.Null(shell.SelectedIssueReason);
        Assert.Equal(
            [IssueReasons.Renewal, IssueReasons.TermsChange, IssueReasons.ReissueLost],
            shell.IssueReasonChoices.Select(c => c.Value).ToArray());
    }

    [Fact]
    public void IssuingASecondTimeWithoutChoosingAReasonIsRefusedAndSignsNothing()
    {
        var shell = NewShell();
        Issue(shell);

        // ⚠ No default is supplied for a real choice. A pre-selected value would reproduce exactly the
        //   inference L5.3 removed, only with a control in front of it.
        Advance();
        Issue(shell);

        Assert.Equal(MessageSeverity.Warning, shell.Message!.Severity);
        Assert.Contains("Choose why", shell.Message.Text, StringComparison.Ordinal);
        Assert.Single(_manager.Register.GetArtifacts(shell.LicenseId));
    }

    [Fact]
    public void NoDefaultIsSuppliedEvenForAReasonThePolicyWouldHappilyAccept()
    {
        // ⭐⭐ MEASURED, NOT ASSUMED. The test above passed under an injected defect that hard-coded
        //     `renewal` — because the POLICY refused it, so the test was proving the policy rather than
        //     the absence of a default. `reissue-lost` is the reason the policy never refuses, so if
        //     anything ever supplies a default this is the assertion that notices (gotcha #378).
        var shell = NewShell();
        Issue(shell);

        Assert.Null(shell.SelectedIssueReason);
        Assert.DoesNotContain(
            IssueReasons.ReissueLost,
            _manager.Register.GetArtifacts(shell.LicenseId).Select(a => a.Reason));

        Advance();
        Issue(shell);

        Assert.Single(_manager.Register.GetArtifacts(shell.LicenseId));
    }

    // ── The measured diff ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenewalIsRefusedWhenTheExpiryHasNotMoved()
    {
        var shell = NewShell();
        Issue(shell);

        Choose(shell, IssueReasons.Renewal);
        Issue(shell);

        Assert.Equal(MessageSeverity.Warning, shell.Message!.Severity);
        Assert.Contains("expiry has not moved", shell.Message.Text, StringComparison.Ordinal);

        // ⭐⭐ The refusal is only worth anything if nothing was signed. A message beside a recorded
        //     artifact would be a warning about something that already happened.
        Assert.Single(_manager.Register.GetArtifacts(shell.LicenseId));
    }

    [Fact]
    public void RenewalIsAllowedOnceTheExpiryHasActuallyMoved()
    {
        var shell = NewShell();
        Issue(shell);

        shell.LicenseExpiresAt = shell.LicenseExpiresAt!.Value.AddYears(1);
        shell.SaveLicenseCommand.Execute(null);

        Choose(shell, IssueReasons.Renewal);
        Advance();
        Issue(shell);

        var artifacts = _manager.Register.GetArtifacts(shell.LicenseId);
        Assert.Equal(2, artifacts.Count);
        Assert.Equal(IssueReasons.Renewal, artifacts[0].Reason);
    }

    [Fact]
    public void TermsChangeIsRefusedWhenNothingButTheExpiryDiffers()
    {
        var shell = NewShell();
        Issue(shell);

        shell.LicenseExpiresAt = shell.LicenseExpiresAt!.Value.AddYears(1);
        shell.SaveLicenseCommand.Execute(null);

        Choose(shell, IssueReasons.TermsChange);
        Issue(shell);

        Assert.Equal(MessageSeverity.Warning, shell.Message!.Severity);
        Assert.Contains("no terms change to record", shell.Message.Text, StringComparison.Ordinal);
        Assert.Single(_manager.Register.GetArtifacts(shell.LicenseId));
    }

    [Fact]
    public void TermsChangeIsAllowedWhenTheSeatCountMoved()
    {
        var shell = NewShell();
        Issue(shell);

        shell.LicenseSeats += 5;
        shell.SaveLicenseCommand.Execute(null);

        Choose(shell, IssueReasons.TermsChange);
        Advance();
        Issue(shell);

        Assert.Equal(IssueReasons.TermsChange, _manager.Register.GetArtifacts(shell.LicenseId)[0].Reason);
    }

    [Fact]
    public void RenamingTheCustomerIsATermsChangeBecauseTheNameIsSigned()
    {
        // ⭐ D6: the licensee is signed into the artifact and displayed inside the customer's EmberTern.
        //   A re-issue after a company is renamed genuinely changes what they hold, even though no date
        //   and no number moved — so the diff has to see it, or "terms change" would be refused on the
        //   one occasion it is most obviously true.
        var shell = NewShell();
        Issue(shell);

        shell.CustomerName = "ACME S.A.";
        shell.SaveCustomerCommand.Execute(null);
        SelectFirstLicence(shell);

        Choose(shell, IssueReasons.TermsChange);
        Advance();
        Issue(shell);

        Assert.Equal(IssueReasons.TermsChange, _manager.Register.GetArtifacts(shell.LicenseId)[0].Reason);
    }

    [Fact]
    public void ASubSecondDifferenceIsNotAChangeBecauseNoArtifactCouldEverShowIt()
    {
        // ⭐⭐ The comparison runs on the SIGNED wire form. The issuer truncates every timestamp to whole
        //     seconds, so two values that differ below that produce byte-identical payloads — calling
        //     them a change would refuse "renewal" for a difference the customer's file cannot contain,
        //     and allow "terms change" for one either.
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var jittered = licence with { ExpiresAt = licence.ExpiresAt.AddMilliseconds(400) };

        var change = IssueChange.Between(
            _manager.Register.GetCurrentArtifact(licence.LicenseId), jittered, customer.Name);

        Assert.True(change.CanCompare);
        Assert.False(change.ExpiryMoved);
        Assert.False(change.AnythingChanged);
    }

    // ── What cannot be judged is never refused ──────────────────────────────────────────────────────

    [Fact]
    public void ReissueLostIsNeverRefusedWhateverTheTermsSay()
    {
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);
        var current = _manager.Register.GetCurrentArtifact(licence.LicenseId);

        var unchanged = IssueChange.Between(current, licence, customer.Name);
        var everythingMoved = IssueChange.Between(
            current, licence with { Seats = 99, ExpiresAt = licence.ExpiresAt.AddYears(3) }, "Someone Else");

        // ⭐ No register can know whether a customer lost a file. A rule that pretended otherwise would be
        //   guessing, which is the habit this whole stage removes.
        Assert.Null(IssueReasonPolicy.Refuse(IssueReasons.ReissueLost, unchanged));
        Assert.Null(IssueReasonPolicy.Refuse(IssueReasons.ReissueLost, everythingMoved));
    }

    [Fact]
    public void AStoredPayloadCannotBeCorruptedInPlaceEvenPastTheApi()
    {
        // ⭐⭐ Worth its own assertion because it is why the test BELOW cannot use the database. The
        //     append-only trigger refuses an UPDATE on `issued_artifacts` even from raw SQL, so "the
        //     stored payload went bad" is not a state this register can be talked into.
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var refused = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            RegisterProbe.Execute(
                _manager.Register,
                "UPDATE issued_artifacts SET payload_json = 'not json at all' " +
                $"WHERE lid = '{licence.LicenseId}';"));

        Assert.Contains("append-only", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadablePreviousPayloadBlocksNothing()
    {
        // ⚠⚠ `CanCompare == false` means UNKNOWN, never UNCHANGED. A payload the parser refuses is exactly
        //    the artifact a support call is about; refusing to re-issue it would turn a display problem
        //    into an operational one, on the day the operator can least afford it.
        // ⚠ Handed to the comparison directly rather than stored first — see the test above for why the
        //   database will not hold this state. The reachable route to it is a payload written by a FUTURE
        //   version whose shape this one cannot parse, which is the same input from here.
        var customer = _manager.SaveCustomer();
        var licence = _manager.SaveLicense(customer);
        var issued = _manager.Workflow
            .Issue(_manager.Session, licence, customer, IssueReasons.Initial).Artifact;

        var unreadable = issued with { PayloadJson = "not json at all" };

        var change = IssueChange.Between(unreadable, licence, customer.Name);

        Assert.True(change.HasPrevious);
        Assert.False(change.CanCompare);
        Assert.Null(IssueReasonPolicy.Refuse(IssueReasons.Renewal, change));
        Assert.Null(IssueReasonPolicy.Refuse(IssueReasons.TermsChange, change));
    }

    // ── The pointer, not the ordering ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheDiffIsMeasuredAgainstThePointerAndNotAgainstTheNewestRow()
    {
        // ⭐⭐ Built the way §44.4 taught, TWICE OVER.
        //
        //     First: the API cannot produce a pointer that is not the newest row, because a re-issue
        //     appends and repoints in ONE transaction — so the state is injected past the API with raw
        //     SQL, exactly as the corruption tests do, or the two implementations could never disagree.
        //
        //     Second, and only found by injecting the defect: an earlier version of this test called
        //     `IssueChange.Between` with an artifact it had fetched ITSELF, so replacing the shell's
        //     `GetCurrentArtifact` with `GetArtifacts()[0]` left it green. The choice of artifact is the
        //     shell's, so the shell is what has to be driven.
        var shell = NewShell();
        Issue(shell);

        Advance();
        shell.LicenseSeats += 7;
        shell.SaveLicenseCommand.Execute(null);
        Choose(shell, IssueReasons.TermsChange);
        Issue(shell);

        var artifacts = _manager.Register.GetArtifacts(shell.LicenseId);
        Assert.Equal(2, artifacts.Count);

        RegisterProbe.Execute(
            _manager.Register,
            $"UPDATE license_current_artifact SET artifact_id = {artifacts[^1].ArtifactId} " +
            $"WHERE lid = '{shell.LicenseId}';");

        // Against the NEWEST row the seats already match, so a terms change would be refused as untrue.
        // Against the POINTER — now the first artifact, signed before the seats moved — they differ, and
        // the operator's claim is correct.
        Advance();
        Choose(shell, IssueReasons.TermsChange);
        Issue(shell);

        Assert.Equal(MessageSeverity.Success, shell.Message!.Severity);
        Assert.Equal(3, _manager.Register.GetArtifacts(shell.LicenseId).Count);
    }

    // ── D-4: the optional note ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnOperatorNoteIsRecordedOnTheAuditLineAndTheGeneratedSummarySurvivesIt()
    {
        var shell = NewShell();
        shell.IssueNote = "Ticket 4471, requested by Kowalski.";
        Issue(shell);

        var note = _manager.Register
            .GetAudit(new AuditQuery { Action = "licence.issued", TargetId = shell.LicenseId })
            .Single()
            .Note;

        Assert.Contains("Ticket 4471", note!, StringComparison.Ordinal);

        // ⭐ Appended, never instead of. The generated summary is what lets the audit answer "on what
        //   terms?" without joining anything.
        Assert.Contains("seat(s), until", note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoteIsOptionalAndAnEmptyOneAddsNothingToTheAuditLine()
    {
        var shell = NewShell();
        shell.IssueNote = "   ";
        Issue(shell);

        var note = _manager.Register
            .GetAudit(new AuditQuery { Action = "licence.issued", TargetId = shell.LicenseId })
            .Single()
            .Note!;

        Assert.EndsWith(".", note, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoteDoesNotSurviveIntoTheNextIssue()
    {
        // ⚠ A remark is about ONE issue. Carrying it forward would attach last week's ticket number to
        //   this week's artifact, in a log written to be trusted.
        var shell = NewShell();
        shell.IssueNote = "Ticket 4471.";
        Issue(shell);

        Assert.Equal(string.Empty, shell.IssueNote);
    }

    // ── The history says the reason in words ────────────────────────────────────────────────────────

    [Fact]
    public void TheHistoryShowsTheReasonInWordsWhileTheRawValueStaysOnTheRecord()
    {
        var shell = NewShell();
        Issue(shell);

        var item = shell.History.Artifacts.Single();

        Assert.Equal("Initial issue", item.Reason);
        Assert.Equal(IssueReasons.Initial, item.Artifact.Reason);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A shell with one customer, one licence, that licence selected, and its terms saved THROUGH THE FORM.
    ///
    /// <para>⚠ The register is filled before the shell is built: the shell reads customers in its
    /// constructor, so a licence saved afterwards is invisible until something reloads.</para>
    ///
    /// <para>⚠⚠ <b>The Save terms is load-bearing, and finding out why was worth the test.</b>
    /// <see cref="ManagerFixture.SaveLicense"/> stores <c>NotBefore = Now</c> — 09:00 — while the form
    /// reads a date picker and stores the whole UTC DAY. So a fixture-built licence differs from its own
    /// form round-trip in the START date as well as the expiry, and a diff taken against it reported a
    /// terms change on a test that had only moved the expiry. That is an artefact of the fixture, not of
    /// the product: an operator's licence is always created through the form. Normalising once here keeps
    /// every diff test measuring the difference it actually set up.</para>
    /// </summary>
    private ShellViewModel NewShell()
    {
        var customer = _manager.SaveCustomer();
        _manager.SaveLicense(customer);

        var shell = new ShellViewModel(_manager.Register, _manager.Session, _manager.Paths, () => _manager.Now);
        SelectFirstLicence(shell);
        shell.SaveLicenseCommand.Execute(null);
        return shell;
    }

    private static void SelectFirstLicence(ShellViewModel shell)
    {
        shell.SelectedCustomer = null;
        shell.SelectedCustomer = shell.Customers.First();
        shell.SelectedLicense = shell.Licenses.First();
    }

    private static void Choose(ShellViewModel shell, string reason) =>
        shell.SelectedIssueReason = shell.IssueReasonChoices.First(c => c.Value == reason);

    // ⚠ The register refuses an artifact whose `iat` does not come after the current one's (§39.3), so a
    //   second issue in the same test second would be rejected for a reason that has nothing to do with
    //   what is under test.
    private void Advance() => _manager.Now = _manager.Now.AddMinutes(1);

    private static void Issue(ShellViewModel shell) =>
        shell.IssueAndSaveCommand.Execute(null);

    private IssuedArtifactRecord OnlyArtifact(ShellViewModel shell) =>
        _manager.Register.GetArtifacts(shell.LicenseId).Single();
}
