using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L10.1 — the two bulk-sending settings, and the one aggregate query the bulk preview will read.</b>
///
/// <para>Nothing here builds a window: L10.1's only surface is a section on the Settings page, and what is
/// worth guarding is the CONTRACT underneath it — that the values survive a round trip, that an out-of-range
/// value is refused rather than repaired, that a v2 file still reads, and that
/// <see cref="LicenseRegister.GetLastSentAt"/> answers correctly and in ONE query however long the history
/// is.</para>
///
/// <para>⚠ These tests take a <see cref="ManagerFixture"/> each rather than sharing one: every one of them
/// writes to a register or a settings file, and a shared fixture would make the audit assertions depend on
/// execution order.</para>
/// </summary>
public sealed class BulkSendSettingsTests : IDisposable
{
    private readonly string _folder;

    public BulkSendSettingsTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "etlm-bulk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    // ── The two values, stored and read back ────────────────────────────────────────────────────────

    /// <summary>⭐ The defaults are the ratified ones (§60.9), read from the constants rather than typed.</summary>
    [Fact]
    public void TheDefaultsAreTheRatifiedOnes()
    {
        Assert.Equal(15, SmtpSettings.DefaultBulkDelaySeconds);
        Assert.Equal(50, SmtpSettings.DefaultBulkMaxPerRun);

        // ⭐ And an unconfigured settings record already carries them, so nothing has to remember to.
        Assert.Equal(SmtpSettings.DefaultBulkDelaySeconds, SmtpSettings.Empty.BulkDelaySeconds);
        Assert.Equal(SmtpSettings.DefaultBulkMaxPerRun, SmtpSettings.Empty.BulkMaxPerRun);
    }

    /// <summary>⭐ A round trip through the real store keeps both values.</summary>
    [Fact]
    public void BothValuesSurviveASaveAndLoad()
    {
        var store = Store();
        store.Save(Usable() with { BulkDelaySeconds = 42, BulkMaxPerRun = 7 });

        var load = store.Load();

        Assert.Equal(SmtpSettingsState.Loaded, load.State);
        Assert.Equal(42, load.Settings.BulkDelaySeconds);
        Assert.Equal(7, load.Settings.BulkMaxPerRun);
    }

    /// <summary>
    /// ⭐⭐ <b>v2 → v3 compatibility, proved against a file this build did NOT write.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The file is hand-built with `version: 2` and no bulk keys — which is exactly what every settings
    /// file written before L10.1 looks like. ⛔ Saving a v3 file and re-reading it would prove nothing
    /// about the older shape, because both halves would come from the same build.
    /// ⭐ The claim is threefold: the state is `Loaded` (not `Unreadable`), every v2 field survives, and
    /// the two new ones take their defaults — no migration step and nothing an operator has to do.
    /// </remarks>
    [Fact]
    public void AVersion2FileReadsCleanlyAndTakesTheNewDefaults()
    {
        var path = Path.Combine(_folder, "smtp.dat");
        File.WriteAllText(
            path,
            """
            {
              "version": 2,
              "host": "smtp.legacy.test",
              "port": 2525,
              "security": "StartTls",
              "fromAddress": "licencje@legacy.test",
              "fromName": "Legacy",
              "username": "legacy",
              "messageLanguage": "en"
            }
            """);

        var load = new SmtpSettingsStore(path).Load();

        Assert.Equal(SmtpSettingsState.Loaded, load.State);
        Assert.Equal("smtp.legacy.test", load.Settings.Host);
        Assert.Equal(2525, load.Settings.Port);
        Assert.Equal("licencje@legacy.test", load.Settings.FromAddress);
        Assert.Equal("en", load.Settings.MessageLanguage);

        Assert.Equal(SmtpSettings.DefaultBulkDelaySeconds, load.Settings.BulkDelaySeconds);
        Assert.Equal(SmtpSettings.DefaultBulkMaxPerRun, load.Settings.BulkMaxPerRun);

        // ⚠ And the file is still valid as a whole — a v2 file must not become unsaveable.
        Assert.Empty(load.Settings.Validate());
    }

    /// <summary>⭐ Both new keys reach the file, under the names the wire shape declares.</summary>
    /// <remarks>
    /// ⚠ The VERSION is not asserted here: <c>SmtpSettingsStoreTests</c> owns the store's version
    /// contract — the relationship in <c>ThisBuildWritesItsOwnVersionIntoTheFile</c> and the tripwire in
    /// <c>TheContainerVersionIsWhatThisStageSet</c>. ⛔ A third opinion about the version number is
    /// exactly the second owner that turns a deliberate bump into three red tests.
    /// </remarks>
    [Fact]
    public void BothNewKeysAreWrittenIntoTheFile()
    {
        var store = Store();
        store.Save(Usable());

        using var document = JsonDocument.Parse(File.ReadAllText(store.FilePath));

        Assert.Equal(
            SmtpSettings.DefaultBulkDelaySeconds,
            document.RootElement.GetProperty("bulkDelaySeconds").GetInt32());
        Assert.Equal(
            SmtpSettings.DefaultBulkMaxPerRun,
            document.RootElement.GetProperty("bulkMaxPerRun").GetInt32());
    }

    // ── Validation: refused, never repaired ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Out of range is a REFUSAL, and the value the operator typed is kept.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ This is the window's established behaviour and it is asserted rather than assumed, because §60.7
    /// described it as CLAMPING — which measurement did not support: `PortText` parses and reports, it
    /// does not repair. ⛔ One numeric field that silently rewrites what was typed while another refuses
    /// would be two behaviours in one form.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(601)]
    public void ADelayOutsideItsRangeIsRefusedAndNotRepaired(int delay)
    {
        var settings = Usable() with { BulkDelaySeconds = delay };

        Assert.Single(settings.Validate());
        Assert.Equal(delay, settings.BulkDelaySeconds);
    }

    /// <summary>⭐ The same for the run limit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(501)]
    public void ARunLimitOutsideItsRangeIsRefusedAndNotRepaired(int limit)
    {
        var settings = Usable() with { BulkMaxPerRun = limit };

        Assert.Single(settings.Validate());
        Assert.Equal(limit, settings.BulkMaxPerRun);
    }

    /// <summary>⭐ Both bounds are inclusive — an off-by-one at either end is a real defect.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(600, 500)]
    [InlineData(15, 50)]
    public void TheBoundsThemselvesAreAccepted(int delay, int limit)
    {
        Assert.Empty((Usable() with { BulkDelaySeconds = delay, BulkMaxPerRun = limit }).Validate());
    }

    /// <summary>
    /// ⚠ Checked even with NO server configured — deliberately unlike the port, which is only meaningful
    /// once a host exists. A nonsense value stored while the operator was still filling the form must not
    /// become the value a later bulk run reads.
    /// </summary>
    [Fact]
    public void TheRangesAreCheckedEvenWithNoServer()
    {
        var problems = (SmtpSettings.Empty with { BulkDelaySeconds = 9999 }).Validate();

        // ⚠ Two: the missing sender address, and the delay. The point is that the delay is among them.
        Assert.Contains(problems, p => p.ToString().Contains("9999", StringComparison.Ordinal));
    }

    /// <summary>⭐ Both sentences resolve in both languages — a bound travels as an ARGUMENT, never baked in.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public void BothRefusalsAreWordedInBothLanguages(string language)
    {
        using var isolated = Localization.Loc.IsolateSubscribersForVerification();

        try
        {
            Localization.Loc.Apply(language);

            var problems = (Usable() with { BulkDelaySeconds = 0, BulkMaxPerRun = 0 }).Validate();
            Assert.Equal(2, problems.Count);

            var rendered = problems.Select(p => p.ToString()).ToArray();

            // ⚠ No sentence falls through to its own key — a missing entry renders as "Status.Smtp…",
            //   which looks like a label and is the failure mode StatusMessageContractTests records.
            Assert.All(rendered, r => Assert.DoesNotContain(
                StatusCatalog.KeyPrefix, r, StringComparison.Ordinal));

            // ⭐⭐ Each sentence carries ITS OWN bounds, because they were passed as ARGUMENTS — so a
            //    bound that moves changes both messages with no translation work. ⚠ Asserted per message
            //    rather than over the pair: the first draft of this test looked for "600" in BOTH and went
            //    red on the run-limit sentence, which correctly carries 500.
            Assert.Single(rendered, r =>
                r.Contains("600", StringComparison.Ordinal) &&
                r.Contains("0", StringComparison.Ordinal));

            Assert.Single(rendered, r => r.Contains("500", StringComparison.Ordinal));
        }
        finally
        {
            Localization.Loc.Apply(Settings.ApplicationLanguages.Default);
        }
    }

    // ── The settings form ───────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The form carries both values in and out, through the same two paths every field uses.</summary>
    [Fact]
    public void TheFormRoundTripsBothValues()
    {
        var store = Store();
        store.Save(Usable() with { BulkDelaySeconds = 99, BulkMaxPerRun = 3 });

        var model = new SettingsViewModel(store);

        Assert.Equal(99, model.BulkDelaySeconds);
        Assert.Equal(3, model.BulkMaxPerRun);
        Assert.Equal("99", model.BulkDelayText);
        Assert.Equal("3", model.BulkMaxPerRunText);

        model.BulkDelayText = "30";
        model.BulkMaxPerRunText = "12";

        Assert.Equal(30, model.Current().BulkDelaySeconds);
        Assert.Equal(12, model.Current().BulkMaxPerRun);
    }

    /// <summary>
    /// ⚠ Text that is not a number leaves the value alone rather than zeroing it — the same contract
    /// `PortText` has. ⛔ A half-typed field must not become `0` on the way to `Save`.
    /// </summary>
    [Fact]
    public void TypingSomethingThatIsNotANumberDoesNotZeroTheValue()
    {
        var model = new SettingsViewModel(Store());
        var before = model.BulkDelaySeconds;

        model.BulkDelayText = "abc";

        Assert.Equal(before, model.BulkDelaySeconds);
    }

    // ── GetLastSentAt ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The register sees what <c>LicenceDelivery</c> actually wrote — proved BEHAVIOURALLY.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The action name now has two owners: <c>LicenceDelivery</c> writes it and
    /// <see cref="AuditActions.LicenceSent"/> names it for the reader. ⛔ Comparing the constant against a
    /// literal would only prove that two strings match. This performs a real delivery and asserts the
    /// register sees it, which is the property that actually has to hold — and it would fail if either
    /// side were renamed.
    /// </remarks>
    [Fact]
    public async Task ARealDeliveryIsWhatTheRegisterReadsBack()
    {
        using var manager = new ManagerFixture();
        var (message, licence) = Issue(manager);

        Assert.Empty(manager.Register.GetLastSentAt());

        await new LicenceDelivery(manager.Register)
            .SendAsync(FakeEmailSender.Succeeding(), message);

        var sent = manager.Register.GetLastSentAt();

        Assert.Single(sent);
        Assert.True(sent.ContainsKey(licence.LicenseId));
        Assert.Equal(manager.Now, sent[licence.LicenseId]);
    }

    /// <summary>
    /// ⭐⭐ <b>A FAILED send is not a send, and neither is an <c>.eml</c> export.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Both write an audit line, and neither line means a customer was reached: `licence.send-failed`
    /// says the opposite, and `licence.exported` says a file exists. ⛔ A "skip already sent" decision built
    /// on either would silently skip a licence nobody received.
    /// </remarks>
    [Fact]
    public async Task NeitherAFailedSendNorAnExportCountsAsSent()
    {
        using var manager = new ManagerFixture();
        var (message, _) = Issue(manager);
        var delivery = new LicenceDelivery(manager.Register);

        await delivery.SendAsync(FakeEmailSender.Failing(), message);
        await delivery.ExportAsync(
            new EmlFileEmailSender(Path.Combine(_folder, "message.eml")), message);

        Assert.Empty(manager.Register.GetLastSentAt());
    }

    /// <summary>
    /// ⭐⭐ <b>The NEWEST send wins, and it is found by timestamp rather than by row order.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ This is what pins the assumption `MAX(at)` rests on: the stored format is fixed-width UTC
    /// (<c>yyyy-MM-dd'T'HH:mm:ss'Z'</c>), so a TEXT maximum IS the newest timestamp. The test writes the
    /// LATER send FIRST, so a query that answered "the last row" instead of "the greatest timestamp" would
    /// return the earlier date and go red.
    /// </remarks>
    [Fact]
    public async Task TheNewestSendWins_EvenWhenItIsNotTheNewestRow()
    {
        using var manager = new ManagerFixture();
        var (message, licence) = Issue(manager);
        var delivery = new LicenceDelivery(manager.Register);

        var later = manager.Now.AddDays(10);
        manager.Now = later;
        await delivery.SendAsync(FakeEmailSender.Succeeding(), message);

        manager.Now = later.AddDays(-5);
        await delivery.SendAsync(FakeEmailSender.Succeeding(), message);

        Assert.Equal(later, manager.Register.GetLastSentAt()[licence.LicenseId]);
    }

    /// <summary>
    /// ⭐⭐ <b>A history longer than <see cref="AuditQuery.Limit"/> is NOT truncated.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ This is the specific defect the aggregate query exists to make unreachable. `GetAudit` defaults
    /// to 200 rows, newest first, so an implementation built on it would answer confidently and WRONGLY on
    /// any register with a longer history — with no error and nothing to notice. The register below carries
    /// **260** send lines, and the send this asserts on is the OLDEST of them, i.e. the one a 200-row
    /// window would have dropped.
    /// ⭐ It also proves the shape of the answer: one entry per licence, not one per audit line.
    /// </remarks>
    [Fact]
    public async Task AHistoryLongerThanTheAuditQueryLimit_IsNotTruncated()
    {
        using var manager = new ManagerFixture();
        var (message, licence) = Issue(manager);
        var delivery = new LicenceDelivery(manager.Register);
        var sender = FakeEmailSender.Succeeding();

        // ⭐ The FIRST send is the oldest line and the one at risk: everything after it pushes it out of
        //   any newest-N window.
        var oldest = manager.Now;
        await delivery.SendAsync(sender, message);

        const int Lines = 260;
        for (var i = 1; i < Lines; i++)
        {
            manager.Now = oldest.AddSeconds(-i);
            await delivery.SendAsync(sender, message);
        }

        Assert.True(Lines > new AuditQuery().Limit, "The register must hold more lines than GetAudit returns.");

        var sent = manager.Register.GetLastSentAt();

        Assert.Single(sent);
        Assert.Equal(oldest, sent[licence.LicenseId]);

        // ⚠ And the demonstration that the naive implementation would have been wrong: GetAudit's own
        //   default window genuinely does not reach the answer.
        var window = manager.Register.GetAudit(new AuditQuery
        {
            TargetType = AuditTargets.Licence,
            TargetId = licence.LicenseId,
            Action = AuditActions.LicenceSent,
        });

        Assert.Equal(new AuditQuery().Limit, window.Count);
        Assert.DoesNotContain(window, entry => entry.At == oldest);
    }

    /// <summary>
    /// ⭐⭐ <b>THE MEASUREMENT §60.7 requires: ~500 licences, and the answer costs ONE query.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ The claim being measured is <b>the number of statements</b>, not a wall-clock figure: the
    /// rejected design was one full <c>audit_log</c> scan per selected licence per keystroke, and what makes
    /// this safe is that the count does not grow with the selection at all. `audit_log` carries no index on
    /// <c>(target_type, target_id, action)</c>, so a per-licence shape would have been 500 scans.</para>
    /// <para>⭐ Counted by asking SQLite itself, through the register's own connection, rather than by
    /// timing: a timing threshold on a developer machine is a flaky test, and it would not distinguish "one
    /// query" from "500 fast ones".</para>
    /// </remarks>
    [Fact]
    public async Task TheWholeAnswerCostsOneQuery_MeasuredOn500Licences()
    {
        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");
        var delivery = new LicenceDelivery(manager.Register);
        var sender = FakeEmailSender.Succeeding();

        const int Licences = 500;
        var expected = new string[Licences];

        for (var i = 0; i < Licences; i++)
        {
            var licence = manager.SaveLicense(customer);
            expected[i] = licence.LicenseId;

            var artifact = manager.Workflow
                .Issue(manager.Session, licence, customer, IssueReasons.Initial).Artifact;

            await delivery.SendAsync(
                sender, LicenseMessageComposer.Compose(artifact, customer, Usable()));
        }

        var before = manager.Register.StatementsExecuted;
        var sent = manager.Register.GetLastSentAt();
        var cost = manager.Register.StatementsExecuted - before;

        Assert.Equal(Licences, sent.Count);
        Assert.All(expected, id => Assert.True(sent.ContainsKey(id)));

        Assert.Equal(1, cost);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private SmtpSettingsStore Store() => new(Path.Combine(_folder, "smtp.dat"));

    /// <summary>Settings with nothing wrong with them, so a test can break exactly one thing.</summary>
    private static SmtpSettings Usable() => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern",
        MessageLanguage = MessageLanguages.Polish,
    };

    private static (LicenseMessage Message, LicenseRecord Licence) Issue(ManagerFixture manager)
    {
        var customer = manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");
        var licence = manager.SaveLicense(customer);

        manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
        var current = manager.Register.GetCurrentArtifact(licence.LicenseId)!;

        return (LicenseMessageComposer.Compose(current, customer, Usable()), licence);
    }
}
