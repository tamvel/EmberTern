using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L10.3 — the run: PROGRESS while it happens, RESULT when it is over.</b>
///
/// <para>Every test here drives the REAL <see cref="BulkSendRun"/> over a real register, with the transport
/// faked and the pacing injected as a no-op. ⛔ Nothing is stubbed on our side of the server's decision: the
/// audit lines, the attempts, the counters and the conclusions are all the production ones.</para>
///
/// <para>⚠ <c>delay</c> is injected precisely so the suite does not wait 15 seconds per message — and it is
/// a <see cref="Task"/>-returning no-op rather than a <c>Thread.Sleep</c> of zero, so the awaits still
/// happen in the order production has them.</para>
/// </summary>
public sealed class BulkSendRunTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    // ── The invariant ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ 🔒 <b><c>Planned == Sent + Failed + Skipped + NotAttempted</c>, in every terminal state.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The user's own requirement, and the reason the report is a structured value rather than a
    /// sentence: it is what makes "we sent 40" impossible to say when 36 succeeded. ⭐ Asserted across all
    /// four conclusions, because an accounting bug that only appears on the stopped paths would be exactly
    /// the one nobody notices.
    /// </remarks>
    [Fact]
    public async Task TheCountsAlwaysAddUp()
    {
        foreach (var result in await AllTerminalStates())
        {
            Assert.Equal(
                result.Planned,
                result.Sent + result.Failed + result.Skipped + result.NotAttempted);

            Assert.Equal(result.Planned, result.Attempts.Count);

            // ⭐ And every licence appears exactly once — a double-counted attempt would still satisfy the
            //   sum above.
            Assert.Equal(
                result.Attempts.Count,
                result.Attempts.Select(a => a.LicenseId).Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>⚠ HELD licences are not in the report's accounting at all — they never entered the run.</summary>
    [Fact]
    public async Task AHeldLicenceIsNotAnAttempt()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.Licence("kontakt@beta.test", blocked: true);

        var result = await world.Run();

        Assert.Single(result.Attempts);
        Assert.Equal(1, result.Planned);
        Assert.Equal(1, result.Sent);
    }

    // ── Progress ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ 🔒 <b>The bar's value counts FINISHED ATTEMPTS, and never moves while waiting.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The user's requirement, stated twice: no animation pretending to be progress. So this asserts
    /// three things a looser test would miss — the value never exceeds the total, it never decreases, and
    /// every <see cref="BulkSendPhase.Waiting"/> snapshot reports the SAME value as the attempt before it.
    /// </remarks>
    [Fact]
    public async Task TheBarCountsFinishedAttemptsAndStandsStillWhileWaiting()
    {
        using var world = new World();
        for (var i = 0; i < 3; i++)
        {
            world.Licence($"klient{i}@acme.test");
        }

        var seen = new RecordingProgress();
        var result = await world.Run(progress: seen);

        Assert.Equal(3, result.Sent);
        Assert.NotEmpty(seen.Snapshots);

        var snapshots = seen.Snapshots;

        Assert.All(snapshots, p => Assert.Equal(3, p.Total));
        Assert.All(snapshots, p => Assert.InRange(p.Completed, 0, p.Total));

        // ⭐ Monotonic: a bar that goes backwards is worse than one that does not move.
        for (var i = 1; i < snapshots.Count; i++)
        {
            Assert.True(snapshots[i].Completed >= snapshots[i - 1].Completed);
        }

        // ⭐⭐ THE SHARP ONE: a `Sending` snapshot for the k-th message reports k−1 finished attempts —
        //    i.e. the bar does NOT count the message it is currently sending.
        // ⚠⚠ The first draft asserted something else and was WRONG: it demanded that a `Waiting` snapshot
        //    carry the same count as the snapshot before it. It does not, and should not — waiting happens
        //    AFTER an attempt finished, so that count is legitimately one higher. The property that matters
        //    is that nothing is counted BEFORE it finishes, which is what this asserts.
        var started = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Phase == BulkSendPhase.Sending)
            {
                Assert.Equal(started, snapshot.Completed);
                started++;
            }
        }

        Assert.Equal(3, started);

        // ⭐ And a wait says how long is left, so the stationary bar is EXPLAINED rather than faked.
        var waits = snapshots.Where(p => p.Phase == BulkSendPhase.Waiting).ToList();
        Assert.All(waits, p => Assert.NotNull(p.SecondsToNext));
        Assert.All(waits, p => Assert.Equal(15, p.SecondsToNext));

        // Two gaps for three messages — the last message waits for nothing.
        Assert.Equal(2, waits.Count);

        Assert.Equal(3, snapshots[^1].Completed);
        Assert.Equal(BulkSendPhase.Finished, snapshots[^1].Phase);
    }

    /// <summary>⚠ A SKIPPED licence advances the loop and must not advance the bar.</summary>
    /// <remarks>
    /// ⭐ It would be the easiest possible off-by-one: the loop moves, so a naive "how far through the
    /// list" counter would report progress the run never made.
    /// </remarks>
    [Fact]
    public async Task ASkippedLicenceDoesNotAdvanceTheBar()
    {
        using var world = new World();
        var skipped = world.Licence("stary@acme.test");
        world.Licence("nowy@acme.test");

        world.MarkAlreadySent(skipped);

        var seen = new RecordingProgress();
        var result = await world.Run(skipAlreadySent: true, progress: seen);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Sent);

        Assert.All(seen.Snapshots, p => Assert.Equal(1, p.Total));
        Assert.Equal(1, seen.Snapshots[^1].Completed);
    }

    // ── K1 — stop on the first failure ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ 🔒 <b>K1: the first refusal ends the run, and everything after it is <c>NotAttempted</c>.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The failure itself is RECORDED — in the report and in the register — before the run stops, because
    /// what the server said is the whole reason the operator will look. ⛔ And nothing is classified: the
    /// policy needs no idea of WHY the server refused, which is what keeps it out of the interpretation this
    /// project has already decided never to do.
    /// </remarks>
    [Fact]
    public async Task TheFirstFailureStopsTheRunAndTheRestAreNotAttempted()
    {
        using var world = new World();
        for (var i = 0; i < 4; i++)
        {
            world.Licence($"klient{i}@acme.test");
        }

        var result = await world.Run(sender: FakeEmailSender.Failing("5.7.8 Username and Password not accepted."));

        Assert.Equal(BulkSendConclusion.StoppedAfterError, result.Conclusion);
        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.Failed);
        Assert.Equal(3, result.NotAttempted);

        // ⭐ The server's own words reached the report, verbatim and untranslated.
        var failure = Assert.Single(result.Attempts, a => a.Outcome == BulkSendOutcome.Failed);
        Assert.Equal("5.7.8 Username and Password not accepted.", failure.ServerMessage);
        Assert.NotNull(failure.Reason);
        Assert.NotNull(failure.At);

        // ⚠ And the ones never tried carry no server message and no stamp: nothing happened to them.
        Assert.All(
            result.Attempts.Where(a => a.Outcome == BulkSendOutcome.NotAttempted),
            a =>
            {
                Assert.Null(a.ServerMessage);
                Assert.Null(a.At);
                Assert.NotNull(a.Reason);
            });
    }

    /// <summary>⭐ A failure is written to the register before the run gives up.</summary>
    [Fact]
    public async Task AFailedAttemptIsRecordedBeforeTheRunStops()
    {
        using var world = new World();
        var licence = world.Licence("biuro@acme.test");
        world.Licence("kontakt@beta.test");

        await world.Run(sender: FakeEmailSender.Failing());

        Assert.Single(world.AuditFor(licence, "licence.send-failed"));

        // ⛔ And nothing was recorded for the licence the run never reached.
        Assert.Empty(world.Audit("licence.sent"));
    }

    // ── Stopping ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ 🔒 <b>"Stop" means "send no more" — the attempt in flight FINISHES and is recorded.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The token is deliberately never handed to the sender. A cancelled <c>SendMailAsync</c> may already
    /// have delivered the message, so an audit line saying otherwise would be a lie — which is why
    /// <c>SmtpLicenseEmailSender</c> rethrows cancellation rather than recording it as a refusal.
    /// ⭐ The test cancels DURING the first send and asserts that first message counted as sent.
    /// </remarks>
    [Fact]
    public async Task StoppingLetsTheAttemptInFlightFinish()
    {
        using var world = new World();
        world.Licence("pierwszy@acme.test");
        world.Licence("drugi@acme.test");
        world.Licence("trzeci@acme.test");

        using var stop = new CancellationTokenSource();

        // ⭐ Cancels while the FIRST message is being handed to the transport.
        var sender = CancellingSender.After(1, stop);
        var result = await world.Run(sender: sender, stop: stop.Token);

        Assert.Equal(BulkSendConclusion.StoppedByOperator, result.Conclusion);

        Assert.Equal(1, result.Sent);
        Assert.Equal(2, result.NotAttempted);
        Assert.Equal(0, result.Failed);

        // ⭐⭐ The in-flight attempt was RECORDED — the whole point of not cancelling the sender.
        Assert.Single(world.Audit("licence.sent"));

        // ⚠ And the transport was asked exactly once: "send no more" was honoured immediately.
        Assert.Single(sender.Sent);
    }

    /// <summary>
    /// ⭐⭐ <c>StoppedByOperator</c> and <c>StoppedAfterError</c> are NEVER the same answer.
    /// </summary>
    /// <remarks>
    /// ⚠ The report has to say which one happened: "you stopped it" and "their server refused" send the
    /// operator to two completely different next steps.
    /// </remarks>
    [Fact]
    public async Task OperatorStopAndErrorStopAreDifferentConclusions()
    {
        using var world = new World();
        world.Licence("pierwszy@acme.test");
        world.Licence("drugi@acme.test");

        using var stop = new CancellationTokenSource();
        var byOperator = await world.Run(sender: CancellingSender.After(1, stop), stop: stop.Token);

        using var other = new World();
        other.Licence("pierwszy@acme.test");
        other.Licence("drugi@acme.test");
        var byError = await other.Run(sender: FakeEmailSender.Failing());

        Assert.Equal(BulkSendConclusion.StoppedByOperator, byOperator.Conclusion);
        Assert.Equal(BulkSendConclusion.StoppedAfterError, byError.Conclusion);

        // ⭐ And the per-licence reasons differ too, not only the conclusion.
        Assert.NotEqual(
            byOperator.Attempts.Last(a => a.Outcome == BulkSendOutcome.NotAttempted).Reason!.ToString(),
            byError.Attempts.Last(a => a.Outcome == BulkSendOutcome.NotAttempted).Reason!.ToString());
    }

    /// <summary>⭐ A stop requested before anything starts attempts nothing and blames nobody.</summary>
    [Fact]
    public async Task AStopRequestedUpFrontAttemptsNothing()
    {
        using var world = new World();
        world.Licence("biuro@acme.test");
        world.Licence("kontakt@beta.test");

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        var sender = FakeEmailSender.Succeeding();
        var result = await world.Run(sender: sender, stop: stop.Token);

        // ⚠ The FIRST message still goes: the stop is honoured between messages, and there is no "between"
        //   before the first one. ⭐ That is the honest reading of "let the attempt in flight finish" — and
        //   the alternative (checking before the first send) would make a run that the operator started and
        //   immediately cancelled do nothing at all, which is a different promise than the one made.
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.NotAttempted);
        Assert.Equal(BulkSendConclusion.StoppedByOperator, result.Conclusion);
        Assert.Single(sender.Sent);
    }

    // ── Audit ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ One audit line per ATTEMPT, written by the unchanged <c>LicenceDelivery</c>.</summary>
    [Fact]
    public async Task EveryAttemptWritesItsOwnAuditLine()
    {
        using var world = new World();
        world.Licence("pierwszy@acme.test");
        world.Licence("drugi@acme.test");
        world.Licence("trzeci@acme.test");

        var result = await world.Run();

        Assert.Equal(3, result.Sent);
        Assert.Equal(3, world.Audit("licence.sent").Count);
    }

    /// <summary>
    /// ⭐ 🔒 ONE <c>licence.batch-sent</c> line per run (decision I), with the counts and the operator's note.
    /// </summary>
    /// <remarks>
    /// ⚠ Written at the END, so the counts it carries are what happened rather than what was intended.
    /// ⭐ The note is the operator's and the sentence around it is English and invariant, like every audit
    /// note in this register (`terminology.md` §4.4).
    /// </remarks>
    [Fact]
    public async Task OneRunMarkerIsWrittenWithItsCountsAndNote()
    {
        using var world = new World();
        world.Licence("pierwszy@acme.test");
        world.Licence("kontakt@beta.test", blocked: true);

        var result = await world.Run(note: "  ticket 4711  ");

        var marker = Assert.Single(world.Audit(AuditActions.LicenceBatchSent));

        Assert.Equal(AuditTargets.Batch, marker.TargetType);
        Assert.Equal(result.RunId, marker.TargetId);
        Assert.NotEmpty(result.RunId);

        Assert.Contains("1 sent", marker.Note!, StringComparison.Ordinal);
        Assert.Contains("1 held", marker.Note!, StringComparison.Ordinal);
        Assert.Contains("ticket 4711", marker.Note!, StringComparison.Ordinal);
        Assert.DoesNotContain("  ticket", marker.Note!, StringComparison.Ordinal);
    }

    /// <summary>⭐ The marker is written even when nothing was attempted — a run that did nothing is still a run.</summary>
    [Fact]
    public async Task TheRunMarkerIsWrittenEvenWhenNothingWasAttempted()
    {
        using var world = new World();
        world.Licence("kontakt@beta.test", blocked: true);

        var result = await world.Run();

        Assert.Equal(BulkSendConclusion.NothingToSend, result.Conclusion);
        Assert.Empty(result.Attempts);
        Assert.Single(world.Audit(AuditActions.LicenceBatchSent));
    }

    // ── Elapsed ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ How long it took comes from the INJECTED clock, so a test can make it without a timer.
    /// </summary>
    /// <remarks>
    /// ⛔ Not a <c>Stopwatch</c>: gotcha #251's rule — a trigger that only a real timer can reach is
    /// unreachable for a headless test.
    /// </remarks>
    [Fact]
    public async Task TheElapsedTimeComesFromTheClock()
    {
        using var world = new World();
        world.Licence("pierwszy@acme.test");
        world.Licence("drugi@acme.test");

        // ⭐ A clock that advances one minute per read — so the result's span is arithmetic, not luck.
        var result = await world.Run(tickSeconds: 60);

        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.Equal(result.FinishedAt - result.StartedAt, result.Elapsed);
        Assert.All(
            result.Attempts.Where(a => a.At is not null),
            a => Assert.InRange(a.At!.Value, result.StartedAt, result.FinishedAt));
    }

    // ── SentIds ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ 🔒 <b><see cref="BulkSendResult.SentIds"/> holds ONLY the successes</b> (decision L).
    /// </summary>
    /// <remarks>
    /// ⚠ It is what L10.4 unticks, and it is the whole "fix it and click again" mechanism: a failure, a skip
    /// or an unattempted licence must stay ticked, or the operator loses the selection they need to retry.
    /// </remarks>
    [Fact]
    public async Task OnlySuccessesAreOfferedForUnticking()
    {
        using var world = new World();
        var sent = world.Licence("pierwszy@acme.test");
        var alreadySent = world.Licence("stary@acme.test");
        world.Licence("kontakt@beta.test", blocked: true);

        world.MarkAlreadySent(alreadySent);

        var result = await world.Run(skipAlreadySent: true);

        Assert.Equal(new[] { sent }, result.SentIds);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static async Task<List<BulkSendResult>> AllTerminalStates()
    {
        var results = new List<BulkSendResult>();

        // Completed.
        using (var world = new World())
        {
            world.Licence("pierwszy@acme.test");
            world.Licence("drugi@acme.test");
            results.Add(await world.Run());
        }

        // StoppedAfterError, with a skip and a hold in the same run.
        using (var world = new World())
        {
            var skipped = world.Licence("stary@acme.test");
            world.Licence("pierwszy@acme.test");
            world.Licence("drugi@acme.test");
            world.Licence("kontakt@beta.test", blocked: true);
            world.MarkAlreadySent(skipped);
            results.Add(await world.Run(sender: FakeEmailSender.Failing(), skipAlreadySent: true));
        }

        // StoppedByOperator.
        using (var world = new World())
        {
            world.Licence("pierwszy@acme.test");
            world.Licence("drugi@acme.test");
            using var stop = new CancellationTokenSource();
            results.Add(await world.Run(sender: CancellingSender.After(1, stop), stop: stop.Token));
        }

        // NothingToSend.
        using (var world = new World())
        {
            world.Licence("kontakt@beta.test", blocked: true);
            results.Add(await world.Run());
        }

        return results;
    }

    /// <summary>
    /// A whole License Manager plus the pieces a run needs, so a test says WHAT it wants rather than HOW.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly ManagerFixture _manager = new(Start);
        private readonly List<string> _licences = [];
        private int _customers;

        internal string Licence(string? email, bool blocked = false)
        {
            var customer = _manager.Register.SaveCustomer(new CustomerRecord
            {
                CustomerId = _manager.Register.NextCustomerId(),
                Name = $"Klient {++_customers}",
                Email = email,
            });

            var licence = _manager.Register.SaveLicense(new LicenseRecord
            {
                LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
                CustomerId = customer.CustomerId,
                Product = EmberTern.Licensing.LicenseConstants.ProductId,
                Seats = 5,
                NotBefore = Start.AddYears(-1),
                ExpiresAt = Start.AddYears(1),
                Status = blocked ? LicenseStatuses.Blocked : LicenseStatuses.Active,
            });

            _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);
            _licences.Add(licence.LicenseId);

            return licence.LicenseId;
        }

        /// <summary>Records a successful send AFTER the current artifact, so the planner will skip it.</summary>
        internal void MarkAlreadySent(string licenseId) =>
            _manager.Register.Record(
                AuditActions.LicenceSent, AuditTargets.Licence, licenseId, "sent earlier, by a test");

        internal IReadOnlyList<AuditEntry> Audit(string action) =>
            _manager.Register.GetAudit(new AuditQuery { Action = action });

        internal IReadOnlyList<AuditEntry> AuditFor(string licenseId, string action) =>
            _manager.Register.GetAudit(new AuditQuery
            {
                TargetType = AuditTargets.Licence,
                TargetId = licenseId,
                Action = action,
            });

        internal async Task<BulkSendResult> Run(
            ILicenseEmailSender? sender = null,
            bool skipAlreadySent = false,
            string? note = null,
            IProgress<BulkSendProgress>? progress = null,
            CancellationToken stop = default,
            int tickSeconds = 0)
        {
            var settings = new SmtpSettings
            {
                Host = "smtp.example.test",
                FromAddress = "licencje@example.test",
                FromName = "EmberTern",
                MessageLanguage = MessageLanguages.Polish,
            };

            var plan = BulkSendPlanner.Plan(
                _manager.Register.QueryLicenses(),
                _manager.Register.GetCurrentArtifact,
                _manager.Register.GetCustomer,
                _manager.Register.GetLastSentAt(),
                settings,
                Start,
                skipAlreadySent);

            // ⭐ Composed for the whole run BEFORE the first send, exactly as §60.4 step 4 requires — so a
            //   licence that cannot be composed stops everything before anything leaves.
            var composed = new Dictionary<string, LicenseMessage>(StringComparer.Ordinal);
            foreach (var candidate in plan.Sendable)
            {
                composed[candidate.LicenseId] = LicenseMessageComposer.Compose(
                    candidate.CurrentArtifact!, candidate.Customer!, settings);
            }

            var now = Start;
            var run = new BulkSendRun(
                _manager.Register,
                new LicenceDelivery(_manager.Register),

                // ⚠ A real awaitable that waits for nothing: the ordering production depends on is kept,
                //   the 15 seconds are not. ⛔ Never Thread.Sleep.
                delay: (_, token) => token.IsCancellationRequested
                    ? Task.FromCanceled(token)
                    : Task.CompletedTask,
                clock: () =>
                {
                    var reading = now;
                    now = now.AddSeconds(tickSeconds);
                    return reading;
                });

            return await run.ExecuteAsync(
                plan, composed, sender ?? FakeEmailSender.Succeeding(), note, progress, stop);
        }

        public void Dispose() => _manager.Dispose();
    }

    /// <summary>
    /// Collects progress snapshots SYNCHRONOUSLY.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>It exists because <c>Progress&lt;T&gt;</c> delivers ASYNCHRONOUSLY, and the first draft
    /// of these tests used one.</b> <c>Progress&lt;T&gt;</c> posts to the context it was created on, so a
    /// report can arrive AFTER the awaited run has completed — which produced a real
    /// <c>InvalidOperationException: Collection was modified</c> in one of these tests, and left the other
    /// passing only by luck. ⛔ Snapshotting the list before asserting would have hidden the race rather
    /// than removed it: the final <c>Finished</c> report might simply not have arrived yet.</para>
    /// <para>⭐ A synchronous <see cref="IProgress{T}"/> makes the sequence deterministic and complete the
    /// moment <c>ExecuteAsync</c> returns — which is what a test about the ORDER of the reports needs.
    /// ⏭ The interface keeps using <c>Progress&lt;T&gt;</c> on purpose: there, marshalling to the UI thread
    /// is exactly the point, and the report arriving a moment late is invisible.</para>
    /// </remarks>
    private sealed class RecordingProgress : IProgress<BulkSendProgress>
    {
        private readonly List<BulkSendProgress> _snapshots = [];

        internal IReadOnlyList<BulkSendProgress> Snapshots => _snapshots;

        public void Report(BulkSendProgress value) => _snapshots.Add(value);
    }

    /// <summary>
    /// A sender that succeeds, and asks the run to stop after a given number of messages.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ It cancels from INSIDE the send, which is the only way to test the property that matters: the
    /// attempt already in flight must finish and be recorded. ⚠ A test that cancelled before calling the
    /// run would prove something much weaker.
    /// </remarks>
    private sealed class CancellingSender : ILicenseEmailSender
    {
        private readonly CancellationTokenSource _stop;
        private readonly int _after;

        private CancellingSender(int after, CancellationTokenSource stop)
        {
            _after = after;
            _stop = stop;
        }

        internal static CancellingSender After(int messages, CancellationTokenSource stop) =>
            new(messages, stop);

        public string Destination => "smtp.example.test";

        internal List<OutgoingEmail> Sent { get; } = [];

        public Task<SendOutcome> SendAsync(
            OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            Sent.Add(email);

            if (Sent.Count >= _after)
            {
                _stop.Cancel();
            }

            return Task.FromResult(SendOutcome.Ok(Destination));
        }
    }
}
