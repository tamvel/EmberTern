using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L10.4 — the card's view model: the preview, the confirmation, the run, and the report that
/// outlives it.</b>
///
/// <para>Every test here drives the REAL <see cref="BulkSendViewModel"/> over a real register with a real
/// signing key, a real planner and the real <see cref="BulkSendRun"/>. ⛔ The only things substituted are
/// the four seams the production code declares for exactly this purpose: the transport, the pacing, the
/// clock, and — in one test — the composer.</para>
///
/// <para>⚠ The pacing is a <see cref="Task"/>-returning no-op rather than a <c>Thread.Sleep</c> of zero, so
/// the awaits still happen in the order production has them.</para>
/// </summary>
public sealed class BulkSendViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    // ── The preview ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The full recipient list is on screen (§14.1), and the held licences are NAMED beside it.</summary>
    [Fact]
    public void ThePreviewNamesEveryRecipientAndEveryHeldLicence()
    {
        using var world = new World();
        world.Licence("biuro@acme.test", customer: "ACME");
        world.Licence("kontakt@beta.test", customer: "Beta", blocked: true);

        world.TickAll();

        Assert.Single(world.Card.Sendable);
        Assert.Equal("biuro@acme.test", world.Card.Sendable[0].Address);

        Assert.Single(world.Card.Held);

        // ⛔ Never blank on a held row — it is the only thing telling the operator what to do about a
        //    licence they ticked and did not get.
        Assert.NotEmpty(world.Card.Held[0].Reason);
        Assert.True(world.Card.CanSend);
    }

    /// <summary>⛔ Over the run limit the action is UNAVAILABLE, and the card says why (§60.8).</summary>
    [Fact]
    public void OverTheRunLimitTheActionIsRefusedAndExplained()
    {
        using var world = new World(maxPerRun: 1);
        world.Licence("biuro@acme.test");
        world.Licence("kontakt@beta.test");

        world.TickAll();

        Assert.True(world.Card.ExceedsRunLimit);
        Assert.False(world.Card.CanSend);
        Assert.NotEmpty(world.Card.LimitWarning);
    }

    /// <summary>⭐ Nothing is ticked, so neither the register nor the settings file is read at all.</summary>
    [Fact]
    public void WithNothingTickedThereIsNoPreviewAndNoRead()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");

        Assert.False(world.Card.HasPreview);
        Assert.False(world.Card.CanSend);
        Assert.Equal(0, world.SettingsReads);
    }

    // ── The refusals ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>With no confirmer wired the command REFUSES rather than proceeding.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The rule L6.1a's <c>Forget settings</c> established: an outward-facing act must not lose its guard
    /// because a view forgot to attach one, with every test still green. ⭐ Asserted on the TRANSPORT, not
    /// on a flag — what matters is that nothing left.
    /// </remarks>
    [Fact]
    public async Task WithNoConfirmerNothingIsSent()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.TickAll();

        world.Card.Confirm = null;
        await world.Card.SendCommand.ExecuteAsync(null);

        Assert.Empty(world.Sender.Sent);
        Assert.False(world.Card.HasResult);
        Assert.Equal(StatusCatalog.ConfirmationUnavailableNothingSent, world.LastMessage?.Key);
    }

    /// <summary>⭐ Declining the confirmation changes nothing and says nothing.</summary>
    [Fact]
    public async Task DecliningTheConfirmationSendsNothingAndSaysNothing()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.TickAll();

        world.Confirmed = false;
        world.Messages.Clear();

        await world.Card.SendCommand.ExecuteAsync(null);

        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Messages);
        Assert.False(world.Card.HasResult);
    }

    /// <summary>
    /// ⭐⭐ <b>SEMANTIC ATOMICITY: a plan that moved since the preview is REFUSED, not run.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The register is changed behind the card's back — a licence is blocked — which is exactly the state
    /// the card cannot observe. The fresh plan then holds it, <c>Matches</c> says so, and nothing is sent.
    /// ⛔ Running the new plan instead would be the "38 shown, 36 attempted" failure this whole design
    /// exists to make unreachable.
    /// </remarks>
    [Fact]
    public async Task APlanThatChangedSinceThePreviewIsRefused()
    {
        using var world = new World();
        var licence = world.Licence("biuro@acme.test");
        world.Licence("kontakt@beta.test");
        world.TickAll();

        Assert.Equal(2, world.Card.Sendable.Count);

        world.Block(licence);

        await world.Card.SendCommand.ExecuteAsync(null);

        Assert.Empty(world.Sender.Sent);
        Assert.False(world.Card.HasResult);
        Assert.Equal(StatusCatalog.BulkPreviewOutOfDate, world.LastMessage?.Key);

        // ⭐ And the preview is rebuilt, so what the operator now reads IS the current operation.
        Assert.Single(world.Card.Sendable);
        Assert.Single(world.Card.Held);
    }

    /// <summary>
    /// ⭐⭐ <b>§60.4 step 4: one message that cannot be composed means NOTHING leaves.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ Driven through the composer seam, because the planner's fourth condition makes this unreachable
    /// from any register state — see the seam's own remarks. ⭐ The licence is NAMED: a refusal that does
    /// not say which of forty licences is the problem is a refusal the operator cannot act on.
    /// </remarks>
    [Fact]
    public async Task AMessageThatCannotBeComposedStopsEverythingBeforeAnythingLeaves()
    {
        using var world = new World();
        world.Licence("biuro@acme.test", customer: "ACME");
        var second = world.Licence("kontakt@beta.test", customer: "Beta S.A.");
        world.TickAll();

        world.RefuseToCompose(second);

        await world.Card.SendCommand.ExecuteAsync(null);

        // ⛔ Not "one of two went out" — the run never started.
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Audit(AuditActions.LicenceSent));
        Assert.False(world.Card.HasResult);

        Assert.Equal(StatusCatalog.BulkComposeFailed, world.LastMessage?.Key);
        Assert.Contains("Beta S.A.", world.LastMessage!.Text, StringComparison.Ordinal);
    }

    // ── The run ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The ordinary path: everything goes out, the report says so, and it stays on the card.</summary>
    [Fact]
    public async Task AFinishedRunLeavesAReportOnTheCard()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.Licence("kontakt@beta.test");
        world.TickAll();

        await world.Card.SendCommand.ExecuteAsync(null);

        Assert.Equal(2, world.Sender.Sent.Count);
        Assert.True(world.Card.HasResult);
        Assert.Equal(2, world.Card.ReportRows.Count);
        Assert.All(world.Card.ReportRows, row => Assert.Equal(BulkSendOutcome.Sent, row.Outcome));

        Assert.Equal(BulkSendCatalog.ConclusionCompleted, world.Card.ResultConclusion);
        Assert.NotEmpty(world.Card.ResultHeadline);

        // ⛔ Nothing is running any more, and the run may not be stopped.
        Assert.False(world.Card.IsSending);
        Assert.False(world.Card.CanStop);
    }

    /// <summary>
    /// ⭐⭐ 🔒 <b>Decision L: the ticks come off the SENT licences and off nothing else.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ K1 stops the run at the first refusal, so this one run produces all three of the outcomes that
    /// must KEEP their tick — a failure, a skip and an untouched licence — which is the whole point of
    /// asserting them together rather than one per test.
    /// </remarks>
    [Fact]
    public async Task OnlyTheSentLicencesLoseTheirTick()
    {
        using var world = new World();

        // ⚠ The register's order is soonest expiry first, so the years decide who is attempted when.
        var first = world.Licence("first@acme.test", years: 1);
        var second = world.Licence("second@beta.test", years: 2);
        var third = world.Licence("third@gamma.test", years: 3);
        var skipped = world.Licence("skipped@delta.test", years: 4);

        world.MarkAlreadySent(skipped);
        world.TickAll();

        world.Sender.FailFrom(2);
        await world.Card.SendCommand.ExecuteAsync(null);

        var result = Assert.Single(world.Card.ReportRows, r => r.Outcome == BulkSendOutcome.Sent);
        Assert.Equal(first, result.LicenseId);

        // ⭐ Exactly one tick is gone: the one whose message the server accepted.
        Assert.Equal(
            new[] { second, third, skipped }.OrderBy(x => x, StringComparer.Ordinal),
            world.Ticked.OrderBy(x => x, StringComparer.Ordinal));

        // ⭐ And the four outcomes are all represented, so this really did exercise the three that stay.
        Assert.Equal(BulkSendOutcome.Failed, world.Row(second).Outcome);
        Assert.Equal(BulkSendOutcome.NotAttempted, world.Row(third).Outcome);
        Assert.Equal(BulkSendOutcome.Skipped, world.Row(skipped).Outcome);
    }

    /// <summary>⛔ 🔒 Decision M: "Extend and issue" is unavailable while a series is going out.</summary>
    /// <remarks>
    /// ⚠ Read INSIDE the run, from the confirmer — the only moment at which the property is meant to be
    /// true. A test that looked afterwards would pass whatever the code did.
    /// </remarks>
    [Fact]
    public async Task ExtendIsBlockedWhileASeriesIsRunning()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.TickAll();

        var duringRun = new List<bool>();
        world.Sender.Observe(() => duringRun.Add(world.Batch.IsBlockedByBulkSend));

        Assert.False(world.Batch.IsBlockedByBulkSend);

        await world.Card.SendCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true }, duringRun);
        Assert.False(world.Batch.IsBlockedByBulkSend);
    }

    /// <summary>⭐ The four conclusions each get their OWN whole sentence — ⛔ never a shared vague one.</summary>
    [Fact]
    public async Task EveryConclusionHasItsOwnSentence()
    {
        var sentences = new List<string>();

        foreach (var conclusion in Enum.GetValues<BulkSendConclusion>())
        {
            sentences.Add(BulkSendCatalog.Conclusion(conclusion));
        }

        Assert.Equal(4, sentences.Distinct(StringComparer.Ordinal).Count());
        Assert.All(sentences, s => Assert.NotEmpty(s));

        // ⭐ And two of them are reachable through the card, which is what makes the dispatch more than a
        //   switch nobody calls.
        using var stopped = new World();
        stopped.Licence("biuro@acme.test");
        stopped.TickAll();
        stopped.Sender.FailFrom(1);
        await stopped.Card.SendCommand.ExecuteAsync(null);
        Assert.Equal(BulkSendCatalog.ConclusionStoppedAfterError, stopped.Card.ResultConclusion);

        using var nothing = new World();
        var already = nothing.Licence("biuro@acme.test");
        nothing.MarkAlreadySent(already);
        nothing.TickAll();

        // ⚠ Everything ticked is skipped, so the action is unavailable — which is itself the honest
        //   answer, and the reason `NothingToSend` is reached from the RUN rather than from the card here.
        Assert.False(nothing.Card.CanSend);
        Assert.NotEmpty(nothing.Card.SkippedSummary);
    }

    // ── The report ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The report SURVIVES a language change — read off the realised output, not off a promise.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <c>BulkSendAttempt</c> is a record and raises no <c>PropertyChanged</c>, so a row bound to
    /// one of its properties renders once and FREEZES in that language (#401). This asserts the property
    /// that catches that: every one of OUR sentences on the card reads differently after the switch, in the
    /// rows the card is actually showing.</para>
    /// <para>⛔ And the SERVER's words are asserted to be BYTE-IDENTICAL across the switch — they are not
    /// ours to translate, in either direction.</para>
    /// </remarks>
    [Fact]
    public async Task TheReportIsRebuiltWhenTheLanguageChanges()
    {
        // ⚠⚠ <b>The declaration ORDER is load-bearing, and it is the opposite of the obvious one.</b> The
        //    isolate must come FIRST so the card's own subscription is made inside a clean list — declared
        //    the other way round, the isolate would detach the very subscriber this test is about, and it
        //    would pass or fail for reasons having nothing to do with the card. ⭐ It also means the world
        //    disposes BEFORE the subscriber list is restored, so no revived handler reads a closed register.
        using var isolated = Loc.IsolateSubscribersForVerification();
        using var world = new World();

        try
        {
            Loc.Apply(ApplicationLanguages.English);

            world.Licence("biuro@acme.test", years: 1);
            world.Licence("kontakt@beta.test", years: 2);
            world.TickAll();

            world.Sender.FailFrom(2);
            await world.Card.SendCommand.ExecuteAsync(null);

            var englishHeadline = world.Card.ResultHeadline;
            var englishConclusion = world.Card.ResultConclusion;
            var englishReason = world.Row(BulkSendOutcome.Failed).Reason;
            var serverWords = world.Row(BulkSendOutcome.Failed).ServerMessage;

            // ⚠⚠ The row INSTANCE, and this is the assertion that actually measures #401. Every sentence
            //    below is a computed property resolving through Loc, so it reads correctly in C# whether or
            //    not anything was rebuilt — a test that only compared TEXT would be green over a card that
            //    freezes on screen. What a bound template needs is a new item; that is what this pins.
            var englishRow = world.Row(BulkSendOutcome.Failed);

            Assert.NotEmpty(englishHeadline);
            Assert.NotEmpty(englishReason);
            Assert.NotEmpty(serverWords);

            Loc.Apply(ApplicationLanguages.Polish);

            Assert.NotSame(englishRow, world.Row(BulkSendOutcome.Failed));

            Assert.NotEqual(englishHeadline, world.Card.ResultHeadline);
            Assert.NotEqual(englishConclusion, world.Card.ResultConclusion);
            Assert.NotEqual(englishReason, world.Row(BulkSendOutcome.Failed).Reason);

            // ⛔ The one thing that must NOT move.
            Assert.Equal(serverWords, world.Row(BulkSendOutcome.Failed).ServerMessage);

            // ⭐ And the rows themselves are still all there — a rebuild that lost a line would be worse
            //   than one that never happened.
            Assert.Equal(2, world.Card.ReportRows.Count);
        }
        finally
        {
            Loc.Apply(ApplicationLanguages.English);
        }
    }

    /// <summary>
    /// ⭐⭐ 🔒 <b>Decision N: the copied report carries the SUMMARY and the DETAIL.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Asserted on the produced TEXT, not on the fact that a clipboard delegate was called: what matters
    /// is what the operator pastes. ⭐ Including the FULL licence id — a report is correlated against the
    /// register, and the card's shortened id is a presentation decision.
    /// </remarks>
    [Fact]
    public async Task TheCopiedReportCarriesTheSummaryAndTheDetail()
    {
        using var world = new World();
        var first = world.Licence("biuro@acme.test", customer: "ACME Sp. z o.o.", years: 1);
        var second = world.Licence("kontakt@beta.test", customer: "Beta S.A.", years: 2);
        world.TickAll();

        world.Sender.FailFrom(2);
        await world.Card.SendCommand.ExecuteAsync(null);

        var copied = string.Empty;
        world.Card.TextCopier = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await world.Card.CopyReportCommand.ExecuteAsync(null);

        // ── the summary ──
        Assert.Contains(world.Card.ResultHeadline, copied, StringComparison.Ordinal);
        Assert.Contains(BulkSendCatalog.ConclusionStoppedAfterError, copied, StringComparison.Ordinal);
        Assert.Contains(world.Card.ResultElapsed, copied, StringComparison.Ordinal);

        // ── the detail ──
        Assert.Contains(BulkSendCatalog.ColumnServerMessage, copied, StringComparison.Ordinal);
        Assert.Contains(first, copied, StringComparison.Ordinal);
        Assert.Contains(second, copied, StringComparison.Ordinal);
        Assert.Contains("ACME Sp. z o.o.", copied, StringComparison.Ordinal);
        Assert.Contains(FakeEmailSender.RefusalText, copied, StringComparison.Ordinal);

        // ⭐ Every attempt is a line, and every line has the seven columns decision N names.
        var lines = copied.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var rows = lines.Where(l => l.Contains('\t', StringComparison.Ordinal)).ToList();
        Assert.Equal(world.Card.ReportRows.Count + 1, rows.Count);
        Assert.All(rows, row => Assert.Equal(7, row.Split('\t').Length));

        // ⛔ Copying with nothing to copy does nothing at all.
        using var untouched = new World();
        Assert.Empty(untouched.Card.BuildReportText());
    }

    /// <summary>⚠ A tab inside a value would shift every following column, so it cannot survive into a row.</summary>
    [Fact]
    public async Task AServerMessageWithATabCannotShiftTheColumns()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.TickAll();

        world.Sender.FailFrom(1, "550\tno such user\nsecond line");
        await world.Card.SendCommand.ExecuteAsync(null);

        var row = world.Card.BuildReportText()
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Last(l => l.Contains('\t', StringComparison.Ordinal));

        Assert.Equal(7, row.Split('\t').Length);

        // ⛔ Nothing was dropped — only the two characters that would have broken the shape.
        Assert.Contains("no such user", row, StringComparison.Ordinal);
        Assert.Contains("second line", row, StringComparison.Ordinal);
    }

    // ── The world ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A whole licences view: a real register, a real key, real ticks, and the card over them.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly ManagerFixture _manager = new(Start);
        private readonly SmtpSettings _settings;
        private readonly HashSet<string> _refuseToCompose = new(StringComparer.Ordinal);
        private int _customers;

        internal World(int maxPerRun = 50)
        {
            _settings = new SmtpSettings
            {
                Host = "smtp.example.test",
                FromAddress = "licencje@example.test",
                FromName = "EmberTern",
                MessageLanguage = MessageLanguages.Polish,
                BulkDelaySeconds = 15,
                BulkMaxPerRun = maxPerRun,
            };

            Browser = new LicenseBrowserViewModel(_manager.Register, () => Start);

            Batch = new BatchRenewalViewModel(
                _manager.Register, _manager.Workflow, _manager.Session, Browser, Report);

            Card = new BulkSendViewModel(
                _manager.Register,
                Browser,
                ReadSettings,
                Report,
                senderFactory: _ => Sender,

                // ⚠ A real awaitable that waits for nothing: the ordering production depends on is kept,
                //   the 15 seconds are not. ⛔ Never Thread.Sleep.
                delay: (_, token) => token.IsCancellationRequested
                    ? Task.FromCanceled(token)
                    : Task.CompletedTask,
                clock: () => Start,

                // ⚠⚠ A SYNCHRONOUS IProgress, because Progress<T> delivers asynchronously and a snapshot
                //   can arrive after the awaited run returned (§60.7). ⛔ The production default is
                //   Progress<T> and stays that way — the marshalling is the whole point there.
                progressFactory: post => new ImmediateProgress(post),
                composer: Compose)
            {
                Confirm = _ => Task.FromResult(Confirmed),
            };

            // ⭐ The composition root's own wiring for decision M, mirrored here so the block is exercised
            //   the way the application exercises it.
            Card.SendingChanged += (_, _) => Batch.IsBlockedByBulkSend = Card.IsSending;
        }

        internal LicenseBrowserViewModel Browser { get; }

        internal BatchRenewalViewModel Batch { get; }

        internal BulkSendViewModel Card { get; }

        internal ScriptedSender Sender { get; } = new();

        internal List<StatusMessage> Messages { get; } = [];

        internal bool Confirmed { get; set; } = true;

        internal int SettingsReads { get; private set; }

        internal StatusMessage? LastMessage => Messages.Count == 0 ? null : Messages[^1];

        internal IReadOnlyCollection<string> Ticked => Browser.CheckedIds;

        internal string Licence(
            string? email, string customer = "Klient", bool blocked = false, int years = 1)
        {
            var record = _manager.Register.SaveCustomer(new CustomerRecord
            {
                CustomerId = _manager.Register.NextCustomerId(),
                Name = $"{customer} {++_customers}",
                Email = email,
            });

            var licence = _manager.Register.SaveLicense(new LicenseRecord
            {
                LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
                CustomerId = record.CustomerId,
                Product = EmberTern.Licensing.LicenseConstants.ProductId,
                Seats = 5,
                NotBefore = Start.AddYears(-1),
                ExpiresAt = Start.AddYears(years),
                Status = LicenseStatuses.Active,
            });

            // ⚠ Issued FIRST and blocked afterwards, on purpose: a licence that was never issued is held
            //   for a different reason (§60.3 condition 2), and a test meaning to exercise "blocked" would
            //   otherwise be passing on the wrong condition.
            _manager.Workflow.Issue(_manager.Session, licence, record, IssueReasons.Initial);

            if (blocked)
            {
                Block(licence.LicenseId);
            }

            Browser.Refresh();

            return licence.LicenseId;
        }

        /// <summary>Blocks a licence behind the card's back — a change it has no way to observe.</summary>
        internal void Block(string licenseId)
        {
            var licence = _manager.Register.GetLicense(licenseId)!;
            _manager.Register.SaveLicense(licence with { Status = LicenseStatuses.Blocked });
        }

        /// <summary>Records a send AFTER the current artifact, so the planner will skip the licence.</summary>
        internal void MarkAlreadySent(string licenseId) =>
            _manager.Register.Record(
                AuditActions.LicenceSent, AuditTargets.Licence, licenseId, "sent earlier, by a test");

        internal void RefuseToCompose(string licenseId) => _refuseToCompose.Add(licenseId);

        internal void TickAll() => Browser.CheckAllShownCommand.Execute(null);

        internal IReadOnlyList<AuditEntry> Audit(string action) =>
            _manager.Register.GetAudit(new AuditQuery { Action = action });

        internal BulkSendReportRow Row(string licenseId) =>
            Card.ReportRows.Single(r => string.Equals(r.LicenseId, licenseId, StringComparison.Ordinal));

        internal BulkSendReportRow Row(BulkSendOutcome outcome) =>
            Card.ReportRows.Single(r => r.Outcome == outcome);

        public void Dispose() => _manager.Dispose();

        private BulkSendSettings ReadSettings()
        {
            SettingsReads++;
            return BulkSendSettings.Ready(_settings);
        }

        private void Report(StatusMessage message) => Messages.Add(message);

        private LicenseMessage Compose(
            IssuedArtifactRecord artifact, CustomerRecord customer, SmtpSettings settings) =>
            _refuseToCompose.Contains(artifact.LicenseId)
                ? throw new InvalidOperationException("This licence cannot be sent yet: test refusal.")
                : LicenseMessageComposer.Compose(artifact, customer, settings);
    }

    /// <summary>
    /// A sender that succeeds until a given message, then refuses with the server's own words.
    /// </summary>
    /// <remarks>
    /// ⭐ It is the ONE thing faked on our side of the server's decision. Everything else — the plan, the
    /// composed message, the audit lines, the report — is production code.
    /// </remarks>
    private sealed class ScriptedSender : ILicenseEmailSender
    {
        private int _failFrom = int.MaxValue;
        private string _error = FakeEmailSender.RefusalText;
        private Action? _observe;

        public string Destination => "smtp.example.test";

        internal List<OutgoingEmail> Sent { get; } = [];

        internal void FailFrom(int message, string? error = null)
        {
            _failFrom = message;
            _error = error ?? FakeEmailSender.RefusalText;
        }

        /// <summary>Runs while a message is in flight — the only moment "a run is happening" is true.</summary>
        internal void Observe(Action watch) => _observe = watch;

        public Task<SendOutcome> SendAsync(
            OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(email);

            Sent.Add(email);
            _observe?.Invoke();

            return Task.FromResult(Sent.Count >= _failFrom
                ? SendOutcome.Failed(_error)
                : SendOutcome.Ok(Destination));
        }
    }

    /// <summary>
    /// Delivers a snapshot on the calling thread, at the moment it is reported.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <c>Progress&lt;T&gt;</c> posts to the context it was created on, so a report can arrive AFTER the
    /// awaited run has completed (§60.7). ⭐ Production keeps <c>Progress&lt;T&gt;</c> — marshalling onto the
    /// interface thread is exactly what it is for — and a test that needs a deterministic sequence passes
    /// this instead. ⛔ The production contract is not weakened to simplify a test.
    /// </remarks>
    private sealed class ImmediateProgress(Action<BulkSendProgress> post) : IProgress<BulkSendProgress>
    {
        public void Report(BulkSendProgress value) => post(value);
    }
}
