using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;
using static EmberTern.LicenseManager.Tests.ViewProbe;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L10.5 — the bulk-send card, realised.</b>
///
/// <para>Everything L10.4 proved is a claim about a view model, and a view model would pass all of it just
/// as happily over a card that was never bound. These drive the CONTROLS: the bar's own
/// <c>Minimum</c> / <c>Maximum</c> / <c>Value</c>, the four row icons as geometries, the four strokes as
/// brushes off the theme, and the report still standing after the run.</para>
///
/// <para>⚠⚠ <b>EVERY TEST HERE RETURNS ITS <c>Task</c>, AND THAT IS LOAD-BEARING.</b>
/// <c>HeadlessUnitTestSession.Dispatch</c> returns a <c>Task</c>; written as <c>public void X() =&gt;
/// _session.Dispatch(…)</c> it compiles, the <c>Task</c> is DISCARDED, xUnit never awaits it, and no
/// assertion inside the lambda can fail the test (gotcha #374). ⚠⚠ And <c>Dispatch(async () =&gt; …)</c> is
/// the same defect one level deeper — there is no <c>Func&lt;Task&gt;</c> overload, so an async lambda with
/// no return binds to <c>Action</c>, i.e. <c>async void</c>, and everything after the first <c>await</c>
/// detaches (#391). ⭐ The lambdas below that need to await return a value, so they bind to
/// <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, …)</c>.</para>
///
/// <para>⛔ This class joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture:
/// one headless session per PROCESS (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class BulkSendViewTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public BulkSendViewTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── The card at rest ────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The card builds in BOTH themes, and starts in the one state it can honestly be in.</summary>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheCardBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, licences: 2);

            var card = Named<Border>(window, "BulkSendCard");
            Assert.True(card.IsEffectivelyVisible);
            Assert.True(card.Bounds.Height > 0);

            // ⚠ Nothing is ticked, so nothing may be running, nothing may be held, and there is no report.
            Assert.False(Named<Border>(window, "BulkProgressBox").IsEffectivelyVisible);
            Assert.False(Named<Border>(window, "BulkHeldBox").IsEffectivelyVisible);
            Assert.False(Named<Border>(window, "BulkReportBox").IsEffectivelyVisible);
            Assert.False(Named<ListBox>(window, "BulkSendable").IsEffectivelyVisible);
            Assert.False(Named<Button>(window, "BulkSendAction").IsEffectivelyEnabled);

            // ⭐⭐ Permanent, in every state: "sent" means the server accepted it (§60.1).
            Assert.NotEmpty(Named<TextBlock>(window, "BulkAcceptanceNote").Text ?? string.Empty);

            // ⭐ 🔒 Default ON — and it is only safe because "already sent" is measured against the current
            //   artifact's `iat`, so a renewal is never skipped for a message sent a year ago.
            Assert.True(Named<CheckBox>(window, "BulkSkipAlreadySent").IsChecked);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>§14.1: the FULL recipient list is on screen before anything is sent.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Asserted on the REALISED rows and only on what is actually showing — an invisible
    /// <c>TextBlock</c> is still in the tree with its text bound, which is how a list guard passes over a
    /// list nobody can read (#378).
    /// </remarks>
    [Fact]
    public Task TickingShowsEveryRecipientByName() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 3);

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();

            var list = Named<ListBox>(window, "BulkSendable");
            Assert.True(list.IsEffectivelyVisible);

            // ⭐⭐ EVERY recipient is IN the list — that is §14.1's requirement, and it is a claim about the
            //    control's items rather than about how many of them fit on screen at once. ⚠ The card is
            //    bounded (see `LicenceOperations`), so the rest are one scroll away rather than absent;
            //    asserting the realised count instead would be asserting a window size.
            Assert.Equal(3, list.ItemCount);
            Assert.Equal(3, shell.BulkSend.Sendable.Count);

            // ⭐ And what IS realised is really bound: the rows show the address the run would write to,
            //   not an empty template or a type name (#370).
            var shown = Realised(list);
            Assert.NotEmpty(shown);
            Assert.All(shown, text => Assert.Contains("@acme.test", text, StringComparison.Ordinal));

            Assert.True(Named<Button>(window, "BulkSendAction").IsEffectivelyEnabled);
        }, default);

    /// <summary>⛔ A held licence is NAMED on the card, with its reason — never silently dropped.</summary>
    [Fact]
    public Task AHeldLicenceIsNamedWithItsReason() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 1);

            var licence = manager.Register.QueryLicenses()[0].License;
            manager.Register.SaveLicense(licence with { Status = LicenseStatuses.Blocked });
            shell.Browser.Refresh();

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(Named<Border>(window, "BulkHeldBox").IsEffectivelyVisible);

            var shown = Realised(Named<ListBox>(window, "BulkHeld"));
            Assert.NotEmpty(shown);
            Assert.Contains(shown, text => text.Length > 0);

            // ⛔ And nothing may be sent, because nothing qualifies.
            Assert.False(Named<Button>(window, "BulkSendAction").IsEffectivelyEnabled);
        }, default);

    // ── The bar ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ 🔒 <b>The bar is DETERMINATE, always — <c>IsIndeterminate</c> is never used.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The user's requirement, stated twice. An animation that moves while nothing is happening is a
    /// progress bar that lies, and this card exists because a bulk send must not overstate itself.
    /// ⭐ Asserted on the REALISED control, because the claim is about what the operator sees — and it
    /// checks the markup too, so the property cannot be set from anywhere else in this application.
    /// </remarks>
    [Fact]
    public Task TheBarIsNeverIndeterminate() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 2);

            var bar = Named<ProgressBar>(window, "BulkProgressBar");
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(0, bar.Minimum);

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();
            Assert.False(bar.IsIndeterminate);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The bar's <c>Maximum</c> is the number of messages that will be ATTEMPTED.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ Not the ticks and not the plan's size: a held licence never becomes a message and a skipped
    /// one is deliberately not attempted. ⭐ What the RUN puts in a snapshot is L10.3's claim and
    /// <c>BulkSendRunTests</c> owns it; what this owns is that the bar is wired to that snapshot and to
    /// nothing else.</para>
    /// <para>⚠⚠ <b>Driven by setting the snapshot rather than by watching a real run, and that is a
    /// measurement rather than a shortcut:</b> production reports through <c>Progress&lt;T&gt;</c>, which
    /// delivers ASYNCHRONOUSLY (§60.7) — it posts to the interface thread, and inside a headless dispatch
    /// that post is queued behind the very lambda trying to read it. A guard written the other way came
    /// back <c>0</c> every time. ⛔ The production contract is not weakened to make this easier; the test
    /// hands the card exactly the value the run would.</para>
    /// </remarks>
    [Fact]
    public Task TheBarIsWiredToTheRunsOwnCounts() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 3);

            var bar = Named<ProgressBar>(window, "BulkProgressBar");
            var box = Named<Border>(window, "BulkProgressBox");

            Assert.False(box.IsEffectivelyVisible);

            shell.BulkSend.IsSending = true;
            shell.BulkSend.Progress = new BulkSendProgress
            {
                Phase = BulkSendPhase.Sending,
                Total = 38,
                Completed = 11,
                Sent = 11,
                Failed = 0,
                CurrentCustomer = "Delta Sp. z o.o.",
                CurrentAddress = "biuro@delta.test",
            };

            window.UpdateLayout();

            Assert.True(box.IsEffectivelyVisible);
            Assert.Equal(0, bar.Minimum);
            Assert.Equal(38, bar.Maximum);
            Assert.Equal(11, bar.Value);
            Assert.False(bar.IsIndeterminate);

            // ⭐ And the line beside it names who is being written to — the bar alone would not.
            var line = Named<TextBlock>(window, "BulkProgressLine").Text ?? string.Empty;
            Assert.Contains("biuro@delta.test", line, StringComparison.Ordinal);

            // ⭐⭐ THE PACING DOES NOT MOVE THE BAR. A `Waiting` snapshot carries the same finished count,
            //    so the bar stands still — honestly — and the line explains why, instead of an animation
            //    pretending otherwise.
            shell.BulkSend.Progress = shell.BulkSend.Progress with
            {
                Phase = BulkSendPhase.Waiting,
                CurrentCustomer = null,
                CurrentAddress = null,
                SecondsToNext = 15,
            };

            window.UpdateLayout();

            Assert.Equal(11, bar.Value);
            Assert.False(bar.IsIndeterminate);
            Assert.NotEqual(line, Named<TextBlock>(window, "BulkProgressLine").Text);
        }, default);

    // ── The report ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The report STAYS on the card after the run, with an icon and a colour per outcome.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ The colours are read as the BRUSHES the theme resolved, off the realised
    /// <see cref="Path"/> — ⛔ not off a class name in the markup, which would prove that a word was
    /// spelled rather than that anything is painted. A stroke that came back neutral would mean the four
    /// rules in <c>LicenseManagerStyles.axaml</c> lost to the general <c>Viewbox.icon</c> rule above
    /// them.</para>
    /// <para>⭐ K1 gives one run three of the four outcomes, which is why they are asserted together.</para>
    /// </remarks>
    [Fact]
    public Task TheReportStaysOnTheCardAndWearsItsOutcomes() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 3, new ScriptedSender().FailFrom(2));

            Send(shell, window);

            var box = Named<Border>(window, "BulkReportBox");
            Assert.True(box.IsEffectivelyVisible);
            Assert.NotEmpty(Named<TextBlock>(window, "BulkResultHeadline").Text ?? string.Empty);
            Assert.NotEmpty(Named<TextBlock>(window, "BulkResultConclusion").Text ?? string.Empty);
            Assert.NotEmpty(Named<TextBlock>(window, "BulkResultElapsed").Text ?? string.Empty);

            // ⚠ The detail starts CLOSED — a report that opened forty rows by itself would push the card
            //   past every screen it has to fit on.
            var report = Named<ListBox>(window, "BulkReport");
            Assert.False(report.IsEffectivelyVisible);

            Named<Button>(window, "BulkToggleDetails").Command!.Execute(null);
            window.UpdateLayout();
            Assert.True(report.IsEffectivelyVisible);

            // ⭐ Every attempt is a row, whether or not it fits on screen at once.
            Assert.Equal(3, report.ItemCount);

            // ⚠⚠ SCROLLED INTO VIEW one at a time. The card is bounded (see `LicenceOperations`), so a
            //    sweep of realised containers sees whichever row happens to fit — and a colour test that
            //    only ever examined one row would report "all distinct" over a card painting them alike.
            var strokes = new System.Collections.Generic.List<(Geometry Data, Color Stroke)>();

            foreach (var row in shell.BulkSend.ReportRows)
            {
                report.ScrollIntoView(row);
                window.UpdateLayout();

                var glyph = report.GetRealizedContainers()
                    .Where(c => ReferenceEquals(c.DataContext, row))
                    .SelectMany(c => c.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>())
                    .Single(p => p.IsEffectivelyVisible);

                Assert.NotNull(glyph.Data);
                var brush = Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Stroke);
                strokes.Add((glyph.Data!, brush.Color));
            }

            Assert.Equal(3, strokes.Count);

            // ⭐⭐ THREE DISTINCT GLYPHS and THREE DISTINCT COLOURS for sent · failed · not-attempted —
            //    so a row's outcome is readable without reading it.
            // ⚠ Compared by IDENTITY, not by ToString: Avalonia's `StreamGeometry` does not override it, so
            //   every geometry in the application renders as the same type name — a check written that way
            //   is vacuous in the direction that matters. The geometries come from one resource dictionary,
            //   so one key means one instance.
            Assert.Equal(3, strokes.Select(s => s.Data).Distinct().Count());
            Assert.Equal(3, strokes.Select(s => s.Stroke).Distinct().Count());

            // ⛔ And none of them is the neutral document stroke, which is what the general icon rule
            //    would have left them all if the four severity rules had not won.
            var neutral = (Named<TextBlock>(window, "BulkResultHeadline").Foreground as ISolidColorBrush)?.Color;
            Assert.All(strokes, s => Assert.NotEqual(neutral, s.Stroke));
        }, default);

    /// <summary>⛔ 🔒 Decision M: "Extend and issue" is unavailable while a series is going out.</summary>
    /// <remarks>
    /// ⚠ Read INSIDE the run, from the transport — the only moment the property is meant to be true. A
    /// test that looked afterwards would pass whatever the code did.
    /// </remarks>
    [Fact]
    public Task ExtendIsUnavailableWhileASeriesIsRunning() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var duringRun = true;
            Button? extend = null;

            var sender = new ScriptedSender().Observe(
                () => duringRun = extend!.IsEffectivelyEnabled);

            var (shell, window) = Show(manager, licences: 2, sender);
            extend = Named<Button>(window, "ExtendSelected");

            // ⚠ A date, so the batch card's own CanExecute has nothing else to object to — otherwise this
            //   would pass because the button was disabled for an unrelated reason.
            shell.BatchRenewal.TargetDate = manager.Now.AddYears(2).UtcDateTime.Date;
            window.UpdateLayout();

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();
            Assert.True(extend.IsEffectivelyEnabled);

            Assert.True(
            shell.BulkSend.CanSend,
            "The card refuses to send. The preview says: " + shell.BulkSend.PreviewSummary);

        shell.BulkSend.SendCommand.Execute(null);
            window.UpdateLayout();
            AssertRan(shell);

            Assert.False(duringRun);

            // ⚠ ⛔ NOT asserted afterwards, and that is decision L rather than an oversight: every licence
            //   was sent, so every tick came off, so the batch card has nothing left to extend. The button
            //   being unavailable afterwards is two rules working, not one failing.
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ticks everything shown and runs a real bulk send through the card.
    /// </summary>
    /// <remarks>
    /// <para>⭐ The command is the production one and so is everything it drives — the plan, the
    /// composition, the run, the audit lines and the report. Only the server's answer is scripted, and only
    /// the pacing is removed.</para>
    ///
    /// <para>⚠⚠ <b>It does NOT await, and that is not laziness — awaiting here DEADLOCKS.</b> Measured: a
    /// <c>Dispatch(async …)</c> that awaited <c>SendCommand.ExecuteAsync</c> hung the suite until it was
    /// killed. The command's continuations are <c>ConfigureAwait(true)</c>, i.e. they post back to the
    /// Avalonia dispatcher — and the dispatcher is the thread the lambda is running on, so it cannot pump
    /// them while it is blocked on the result. ⛔ Do not "fix" the production code for this: posting a
    /// view model's continuations back to the interface thread is exactly right, and the deadlock is a
    /// property of the harness.</para>
    ///
    /// <para>⭐ It works because every seam in this run completes SYNCHRONOUSLY — a scripted sender, a
    /// pacing that returns <c>Task.CompletedTask</c>, a confirmer that returns <c>Task.FromResult</c> — so
    /// an <c>await</c> on an already-completed task continues inline and the whole command finishes before
    /// <c>Execute</c> returns. ⚠ <see cref="AssertRan"/> is what stops that being an assumption.</para>
    /// </remarks>
    private static void Send(ShellViewModel shell, MainWindow window)
    {
        shell.Browser.CheckAllShownCommand.Execute(null);
        window.UpdateLayout();

        shell.BulkSend.SendCommand.Execute(null);
        window.UpdateLayout();

        AssertRan(shell);
    }

    /// <summary>
    /// ⛔ The run really finished before <c>Execute</c> returned — ⚠ the assumption <see cref="Send"/> rests on.
    /// </summary>
    /// <remarks>
    /// ⭐ Stated as its own assertion rather than left implicit: the day a seam stops completing
    /// synchronously, every guard in this file would otherwise start measuring a card mid-run and would
    /// fail somewhere far from the cause.
    /// </remarks>
    private static void AssertRan(ShellViewModel shell)
    {
        // ⭐⭐ THE FAULT IS SURFACED, not swallowed. `AsyncRelayCommand.Execute` does not await, so an
        //    exception inside the command lands on its task and is never seen — the card simply ends with
        //    no report, and a guard would report "no report" while the real answer sat one property away.
        if (shell.BulkSend.SendCommand.ExecutionTask is { IsFaulted: true } faulted)
        {
            Assert.Fail("The send faulted: " + faulted.Exception);
        }

        Assert.False(shell.BulkSend.IsSending, "The run had not finished when Execute returned.");

        Assert.True(
            shell.BulkSend.HasResult,
            "The run produced no report. The strip says: " + (shell.Message?.Text ?? "(nothing)"));
    }

    /// <summary>The text a list actually SHOWS — realised containers, and only the visible parts of them.</summary>
    /// <remarks>
    /// ⚠⚠ The visibility filter is the point: an invisible <c>TextBlock</c> is still in the tree with its
    /// <c>Text</c> bound, so a sweep without it reads a sentence nobody on the other side of the screen
    /// can read (#378).
    /// </remarks>
    private static System.Collections.Generic.List<string> Realised(ListBox list) =>
        list.GetRealizedContainers()
            .Select(c => string.Join(
                ' ',
                c.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.IsEffectivelyVisible && t.Bounds.Height > 0)
                    .Select(t => t.Text ?? string.Empty)))
            .ToList();

    private static (ShellViewModel Shell, MainWindow Window) Show(
        ManagerFixture manager, int licences, ScriptedSender? sender = null)
    {
        sender ??= new ScriptedSender();

        // ⭐⭐ A CONFIGURED mailbox, written to the fixture's own folder. ⚠ Without it the card refuses
        //    before it plans anything — "e-mail is not configured" is a real refusal and the right one, but
        //    a guard about the card's three STATES would then be measuring the refusal instead, and would
        //    pass over a card that could never send at all.
        SmtpSettingsStore.At(manager.Paths).Save(new SmtpSettings
        {
            Host = "smtp.example.test",
            FromAddress = "licencje@example.test",
            FromName = "EmberTern",
            MessageLanguage = MessageLanguages.Polish,
        });

        var customer = manager.Register.SaveCustomer(new CustomerRecord
        {
            CustomerId = manager.Register.NextCustomerId(),
            Name = "ACME Sp. z o.o.",
            Email = "biuro@acme.test",
        });

        for (var i = 0; i < licences; i++)
        {
            var licence = manager.Register.SaveLicense(new LicenseRecord
            {
                LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
                CustomerId = customer.CustomerId,
                Product = EmberTern.Licensing.LicenseConstants.ProductId,
                Seats = 5,
                NotBefore = manager.Now.AddYears(-1),
                ExpiresAt = manager.Now.AddYears(i + 1),
                Status = LicenseStatuses.Active,
            });

            manager.Workflow.Issue(manager.Session, licence, customer, IssueReasons.Initial);
        }

        var shell = new ShellViewModel(
            manager.Register, manager.Session, manager.Paths, () => manager.Now,

            // ⭐ The two seams a card guard needs: a transport that answers without a server, and no
            //   pacing. ⛔ Never Thread.Sleep — the delay is a real awaitable that waits for nothing, so
            //   the awaits still happen in the order production has them.
            bulkSenderFactory: _ => sender,
            bulkDelay: (_, token) => token.IsCancellationRequested
                ? Task.FromCanceled(token)
                : Task.CompletedTask);

        var window = new MainWindow { DataContext = shell };

        window.Show();
        shell.ShowLicensesCommand.Execute(null);
        window.UpdateLayout();

        // ⚠⚠ AFTER the window has taken the context, and the order is load-bearing. `MainWindow` wires the
        //    REAL confirmation dialog on `DataContextChanged`, so a stub set before this line is
        //    overwritten — and the send then opens a modal that a headless test cannot answer. Measured:
        //    the command returned having done nothing, with an empty strip, because a declined
        //    confirmation says nothing by design. ⭐ That the window overwrites it is CORRECT: the view
        //    owns that seam, and a guard has to work with that rather than around it.
        shell.BulkSend.Confirm = _ => Task.FromResult(true);

        return (shell, window);
    }
}
