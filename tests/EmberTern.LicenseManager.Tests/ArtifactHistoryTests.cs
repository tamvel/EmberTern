using System;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// L5.2 — the issuing history, as the view model answers it.
///
/// <para>⭐ These are pure view-model tests: no window, no dispatcher. The history's job is to state facts
/// about artifacts, and every one of those facts is a string or a flag — so the layout half lives in
/// <see cref="ArtifactHistoryPresentationTests"/> and this file is free to run fast and cover the
/// interesting states.</para>
/// </summary>
public sealed class ArtifactHistoryTests
{
    [Fact]
    public void EveryIssueEverMadeIsStillListed()
    {
        // ⭐⭐ THE CENTRAL PROMISE OF THE PANEL. `issued_artifacts` is append-only, and the operator's
        //    question is whether re-issuing overwrote what was sent before. Three issues, three rows.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        Assert.Equal(3, shell.History.Artifacts.Count);
        Assert.True(shell.History.HasArtifacts);
        Assert.False(shell.History.IsEmpty);
    }

    [Fact]
    public void TheListRunsNewestFirstSoTheLatestIssueIsNotBuriedUnderTheOldOnes()
    {
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        var ordered = shell.History.Artifacts.Select(a => a.Artifact.ArtifactId).ToList();
        Assert.Equal(ordered.OrderByDescending(id => id).ToList(), ordered);
    }

    [Fact]
    public void ExactlyOneIssueIsMarkedCurrentAndItIsTheOneTheRegistersPointerNames()
    {
        // ⛔ Read from `license_current_artifact` through the register's projection — NOT decided here by
        //    taking the newest row. Those agree today; the pointer is the authority (§39.2), and a view
        //    that recomputed it would disagree with the `artifact_status` view the recovery path promises.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        var current = Assert.Single(shell.History.Artifacts, a => a.IsCurrent);
        Assert.Equal(
            manager.Register.GetCurrentArtifact(licence.LicenseId)!.ArtifactId,
            current.Artifact.ArtifactId);
    }

    [Fact]
    public void TheCurrentMarkFollowsThePointerEvenWhenItIsNotTheNEWESTArtifact()
    {
        // ⚠⚠ THIS TEST EXISTS BECAUSE THE OBVIOUS ONE DOES NOT WORK. Replacing the register's projection
        //    with "the newest row wins" was injected and the whole suite stayed GREEN — in ordinary
        //    operation a re-issue always appends and moves the pointer together, so the two answers
        //    coincide and every scenario a test can build through the API cannot tell them apart.
        //
        // ⭐ So the divergence is INJECTED past the API, exactly as §39.4's corruption tests are: the
        //    pointer is repointed at the OLDEST artifact while three remain on record. A view that
        //    recomputes "current" from the ordering now disagrees with `license_current_artifact` — and
        //    therefore with the `artifact_status` view that §29's recovery path promises to any SQL tool
        //    that opens the file without this application.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);
        IssueThreeTimes(manager, licence);

        var all = manager.Register.GetArtifacts(licence.LicenseId);
        var oldest = all[^1].ArtifactId;
        ExecuteRaw(
            manager,
            $"UPDATE license_current_artifact SET artifact_id = {oldest} WHERE lid = '{licence.LicenseId}';");

        shell.SelectedLicense = shell.Licenses.First();

        var current = Assert.Single(shell.History.Artifacts, a => a.IsCurrent);
        Assert.Equal(oldest, current.Artifact.ArtifactId);
        Assert.NotEqual(all[0].ArtifactId, current.Artifact.ArtifactId);
    }

    /// <summary>
    /// Reaches past the register's own API.
    ///
    /// <para>⚠ Deliberate, and the same technique §39.4 records for the integrity checks: a state that
    /// only this application's own writer can produce is a state that proves the writer, not the reader.
    /// ⛔ <c>license_current_artifact</c> is the one artifact-related table that is NOT append-only — it
    /// is rewritten on every re-issue by design — so this is an unusual value, not a forbidden write.
    /// </para>
    /// </summary>
    private static void ExecuteRaw(ManagerFixture manager, string sql)
    {
        var field = typeof(LicenseRegister).GetField(
            "_connection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)field!.GetValue(manager.Register)!;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void AnEarlierIssueSaysSupersededAndNeverSaysDeletedReplacedOrInvalid()
    {
        // ⭐⭐ The wording is the guarantee, in words. An earlier artifact was really delivered and may
        //    still be running at the customer; presenting it as removed would contradict the append-only
        //    rule the register is built on.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        foreach (var earlier in shell.History.Artifacts.Where(a => !a.IsCurrent))
        {
            Assert.Equal("superseded", earlier.Standing);
            foreach (var forbidden in new[] { "deleted", "removed", "replaced", "invalid", "overwritten" })
            {
                Assert.DoesNotContain(forbidden, earlier.Standing, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheSummarySaysOutLoudThatNothingWasOverwritten()
    {
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        Assert.Contains("3 issues on record", shell.History.Summary, StringComparison.Ordinal);
        Assert.Contains("never overwritten or deleted", shell.History.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ALicenceThatWasNeverIssuedSaysSoRatherThanShowingAnEmptyList()
    {
        // ⚠ "Nothing here" and "never sent" look identical when a panel is simply blank, and only one of
        //   them is information.
        using var manager = new ManagerFixture();
        var (shell, _) = WithLicense(manager);
        shell.SelectedLicense = shell.Licenses.First();

        Assert.Empty(shell.History.Artifacts);
        Assert.True(shell.History.IsEmpty);
        Assert.Contains("Never issued", shell.History.Summary, StringComparison.Ordinal);
    }

    // ── The detail of one artifact ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectingAnIssueShowsTheTermsThatWereSIGNEDIntoIt()
    {
        // ⭐⭐ From the STORED payload through `LicensePayload.TryParse` — the client's own parser — not
        //    from the licence row. The two differ the moment terms are changed, and the artifact's own
        //    account of itself is the one a support call needs.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager, seats: 7);

        manager.Workflow.Issue(manager.Session, licence, manager.Register.GetCustomers()[0], "initial");
        shell.SelectedLicense = shell.Licenses.First();
        shell.History.SelectedArtifact = shell.History.Artifacts[0];

        Assert.True(shell.History.HasSelection);
        Assert.Equal("ACME Sp. z o.o.", shell.History.Licensee);
        Assert.Equal("7 seats", shell.History.Seats);
        Assert.Contains("→", shell.History.Validity, StringComparison.Ordinal);
        Assert.Contains(LicenseConstants.ProductId, shell.History.Product, StringComparison.Ordinal);
        Assert.Contains(manager.Session.KeyId, shell.History.SignedWith, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVerdictComesFromTheRealVerifierAndNotFromArithmeticOnTheDates()
    {
        // ⭐⭐ Pinned against the verifier ITSELF rather than against an expected sentence: if the product
        //    changes what it thinks of this artifact, this panel has to change with it. A hard-coded
        //    string here would let the two drift and would still be green.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        var issued = manager.Workflow.Issue(
            manager.Session, licence, manager.Register.GetCustomers()[0], "initial");

        shell.SelectedLicense = shell.Licenses.First();
        shell.History.SelectedArtifact = shell.History.Artifacts[0];

        var fromTheVerifier = manager.Workflow.Inspect(manager.Session, issued.Artifact);
        Assert.Contains(
            fromTheVerifier.Status == LicenseStatus.Valid ? "would accept it" : "would",
            shell.History.Verdict,
            StringComparison.Ordinal);
        Assert.Equal(VerdictSentence(fromTheVerifier), shell.History.Verdict);
    }

    [Fact]
    public void TheDetailShowsTheStoredTokenVerbatimAndItsDeliveredSize()
    {
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);

        var issued = manager.Workflow.Issue(
            manager.Session, licence, manager.Register.GetCustomers()[0], "initial");

        shell.SelectedLicense = shell.Licenses.First();
        shell.History.SelectedArtifact = shell.History.Artifacts[0];

        Assert.Equal(issued.Artifact.Token, shell.History.Token);
        Assert.Equal(issued.Artifact.PayloadJson, shell.History.PayloadJson);

        // ⭐ The size the CUSTOMER receives — the armored token as SaveArtifact writes it, not the raw
        //   token's character count.
        var delivered = System.Text.Encoding.UTF8.GetByteCount(LicenseArmor.Wrap(issued.Artifact.Token));
        Assert.Contains(
            delivered.ToString(System.Globalization.CultureInfo.InvariantCulture),
            shell.History.TokenSize,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingLicenceClearsTheDetailSoNoArtifactIsShownAgainstTheWrongLicence()
    {
        // ⚠ The failure this prevents is silent and plausible: the panel keeps rendering the previous
        //   licence's artifact beside the newly selected licence's terms.
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        var first = manager.SaveLicense(customer);
        manager.SaveLicense(customer);
        manager.Workflow.Issue(manager.Session, first, customer, "initial");

        var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now)
        {
            SelectedCustomer = null,
        };
        shell.SelectedCustomer = shell.Customers.First();

        shell.SelectedLicense = shell.Licenses.Single(l => l.LicenseId == first.LicenseId);
        shell.History.SelectedArtifact = shell.History.Artifacts[0];
        Assert.True(shell.History.HasSelection);

        shell.SelectedLicense = shell.Licenses.Single(l => l.LicenseId != first.LicenseId);

        Assert.False(shell.History.HasSelection);
        Assert.Empty(shell.History.Artifacts);
        Assert.Empty(shell.History.Token);
    }

    [Fact]
    public void ReloadingKeepsTheOperatorLookingAtTheIssueTheyHadOpen()
    {
        // ⭐ The list is rebuilt from the register on every load, so the item is a different INSTANCE even
        //   when it is the same row. Matching on identity is what stops a re-export from closing the
        //   detail the operator was reading (§40.3 point 4, learned on the licences list).
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);
        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        var oldest = shell.History.Artifacts[^1];
        shell.History.SelectedArtifact = oldest;

        shell.History.Reload();

        Assert.NotNull(shell.History.SelectedArtifact);
        Assert.Equal(oldest.Artifact.ArtifactId, shell.History.SelectedArtifact!.Artifact.ArtifactId);
        Assert.NotSame(oldest, shell.History.SelectedArtifact);
    }

    // ── Inspect and export, over the history ─────────────────────────────────────────────────────────

    [Fact]
    public void InspectLatestOpensTheArtifactItIsTalkingAbout()
    {
        // ⭐⭐ The message strip and the detail panel must never describe two different releases. Before
        //    L5.2 the command produced a sentence and left the panel showing whatever was open.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);
        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        shell.History.SelectedArtifact = shell.History.Artifacts[^1];   // the oldest
        shell.InspectLatestCommand.Execute(null);

        Assert.True(shell.History.SelectedArtifact!.IsCurrent);
        Assert.Equal(shell.History.Verdict, shell.Message!.Text);
    }

    [Fact]
    public void InspectLatestStillExplainsALicenceThatWasNeverIssued()
    {
        // ⛔ The behaviour P1-c leaned on, unchanged by the rewrite.
        using var manager = new ManagerFixture();
        var (shell, _) = WithLicense(manager);
        shell.SelectedLicense = shell.Licenses.First();

        shell.InspectLatestCommand.Execute(null);

        Assert.Equal(MessageSeverity.Warning, shell.Message!.Severity);
        Assert.Equal("This licence has never been issued.", shell.Message.Text);
    }

    [Fact]
    public async Task ExportingTheSELECTEDIssueWritesThatIssueAndNotTheNewestOne()
    {
        // ⭐⭐ THE POINT OF THE COMMAND. "Export latest…" already covers the common case; this exists for
        //    "send them the one from March", and getting the newest instead would be a wrong file sent to
        //    a customer with no error anywhere.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);
        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();

        var oldest = shell.History.Artifacts[^1];
        shell.History.SelectedArtifact = oldest;

        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"etlm-{Guid.NewGuid():N}.etlic");
        shell.SaveFilePicker = _ => Task.FromResult<string?>(path);

        await shell.ExportSelectedArtifactCommand.ExecuteAsync(null);

        try
        {
            // ⚠ Compared against the ARMORED form, which is what reaches the customer: `LicenseArmor.Wrap`
            //   breaks the token into 64-character lines, so the bare token is not a contiguous substring
            //   of the file. Asserting on the raw token looks right and fails for a reason that has
            //   nothing to do with which artifact was written.
            var written = System.IO.File.ReadAllText(path);
            Assert.Equal(LicenseArmor.Wrap(oldest.Artifact.Token), written);
            Assert.NotEqual(LicenseArmor.Wrap(shell.History.Artifacts[0].Artifact.Token), written);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportingWithNothingSelectedAsksForASelectionRatherThanGuessing()
    {
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);
        IssueThreeTimes(manager, licence);
        shell.SelectedLicense = shell.Licenses.First();
        shell.History.SelectedArtifact = null;

        var asked = false;
        shell.SaveFilePicker = _ => { asked = true; return Task.FromResult<string?>(null); };

        await shell.ExportSelectedArtifactCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Equal(MessageSeverity.Warning, shell.Message!.Severity);
        Assert.Contains("Select an issue", shell.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportingAStoredIssueIsRecordedInTheAuditLogByTheSameWriterAsEveryOtherExport()
    {
        // ⛔ No second save path and no second audit action — `IssuingWorkflow.SaveArtifact` records
        //    `licence.exported`, and this command must go through it rather than writing the file itself.
        using var manager = new ManagerFixture();
        var (shell, licence) = WithLicense(manager);
        var issued = manager.Workflow.Issue(
            manager.Session, licence, manager.Register.GetCustomers()[0], "initial");

        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"etlm-{Guid.NewGuid():N}.etlic");

        try
        {
            manager.Workflow.SaveArtifact(issued.Artifact, path);

            var history = manager.Register.GetAudit(
                new AuditQuery { TargetType = "licence", TargetId = licence.LicenseId });
            Assert.Contains(history, e => e.Action == "licence.exported");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void TheHistoryOffersNoWayToDeleteOrEditAnIssue()
    {
        // ⛔⛔ Append-only is enforced at the database (§39.2). A command here would be an invitation to a
        //    stack trace, and this asserts the SURFACE agrees with the schema rather than trusting that
        //    nobody adds one. Keyed on the generated command properties, so a new one cannot slip in.
        var commands = typeof(ArtifactHistoryViewModel).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.EndsWith("Command", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(commands, n =>
            n.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Update", StringComparison.OrdinalIgnoreCase));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static string VerdictSentence(LicenseVerdict verdict) => verdict.Status switch
    {
        LicenseStatus.Valid =>
            "EmberTern would accept it: valid until " +
            $"{verdict.Payload!.ExpiresAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"licensed to {verdict.Payload.Licensee}.",
        LicenseStatus.Grace =>
            "EmberTern would accept it, but it is past its expiry and inside the grace period.",
        LicenseStatus.Expired => "EmberTern would report it as expired.",
        LicenseStatus.NotYetValid => "EmberTern would report it as not yet valid.",
        _ => $"EmberTern would refuse it ({verdict.Failure}).",
    };

    private static (ShellViewModel Shell, LicenseRecord License) WithLicense(
        ManagerFixture manager, int seats = 5)
    {
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer, seats);

        var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
        shell.SelectedCustomer = shell.Customers.First();
        return (shell, licence);
    }

    /// <summary>
    /// Three issues, each stamped later than the last.
    ///
    /// <para>⚠ The clock has to MOVE: the register refuses an artifact whose <c>iat</c> does not come
    /// after the current one's (§39.3), because EmberTern would decline to install it as a replacement.
    /// </para>
    /// </summary>
    private static void IssueThreeTimes(ManagerFixture manager, LicenseRecord licence)
    {
        var customer = manager.Register.GetCustomers()[0];
        foreach (var reason in new[] { "initial", "renewal", "reissue-lost" })
        {
            manager.Workflow.Issue(manager.Session, licence, customer, reason);
            manager.Now = manager.Now.AddDays(1);
        }
    }
}
