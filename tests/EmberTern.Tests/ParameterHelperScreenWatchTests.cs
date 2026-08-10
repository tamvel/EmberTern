using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using IoPath = System.IO.Path;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ The Parameter Helper card must not outlive its editor's visibility. Reported 2026-08-10: after a
/// double-click opened the card, switching to another workspace tab left it on screen, floating over the
/// new tab's content.
/// <para>
/// ⚠⚠ <b>This suite pins the PREMISES the fix rests on, not a policy</b> (gotcha #322). Every obvious
/// signal for "my editor left the screen" turned out to be unavailable here, and the fix's shape is a
/// direct consequence of which ones survived measurement — so if a framework update moves any of them,
/// the next author should be told by a red test rather than by a returning bug report.
/// </para>
/// <para>
/// ⛔ The end-to-end behaviour cannot be driven here: <c>ParameterHelper</c> needs a real
/// <c>AvaloniaEdit.TextEditor</c>, whose static keymap initialisation throws in a headless session
/// (#226 — measured again while writing this). The stand-in below is a plain <see cref="Rectangle"/>,
/// which is legitimate because every fact under test belongs to Avalonia's visibility/overlay plumbing at
/// <see cref="Control"/> level, not to the editor. The card actually disappearing on a tab switch is the
/// user's visual QA.
/// </para>
/// <para>⚠ Joins <see cref="HeadlessCollection"/>; never its own class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ParameterHelperScreenWatchTests
{
    private readonly HeadlessUnitTestSession _session;
    public ParameterHelperScreenWatchTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    // Two co-existing "tab" views gated on IsVisible — how MainWindow really hosts its workspace tabs
    // (they are siblings in one visual tree, not a TabControl that swaps content).
    private sealed class TwoTabs
    {
        public Control EditorA { get; init; } = null!;
        public Border HostA { get; init; } = null!;
        public Border HostB { get; init; } = null!;

        public static TwoTabs Build()
        {
            var editorA = new Rectangle { Width = 100, Height = 20, Focusable = true };
            var editorB = new Rectangle { Width = 100, Height = 20, Focusable = true };
            var hostA = new Border { Child = editorA, IsVisible = true };
            var hostB = new Border { Child = editorB, IsVisible = false };
            var window = new Window
            {
                Width = 400,
                Height = 300,
                Content = new StackPanel { Children = { hostA, hostB } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return new TwoTabs { EditorA = editorA, HostA = hostA, HostB = hostB };
        }

        public void SwitchToB()
        {
            HostA.IsVisible = false;
            HostB.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public async Task EveryTab_SharesOneOverlayLayer_SoACardReallyFloatsOverTheNextTab()
    {
        await _session.Dispatch(() =>
        {
            var t = TwoTabs.Build();
            var editorB = (Control)t.HostB.Child!;

            var overlayA = OverlayLayer.GetOverlayLayer(t.EditorA);
            var overlayB = OverlayLayer.GetOverlayLayer(editorB);

            Assert.NotNull(overlayA);
            // ⭐ THE mechanism: one overlay for the whole window, so a card parked while tab A was active is
            // physically over tab B afterwards. This is why the card cannot simply be left to be clipped.
            Assert.Same(overlayA, overlayB);

            var card = new Border { Width = 50, Height = 10 };
            overlayA!.Children.Add(card);
            t.SwitchToB();

            Assert.Contains(card, overlayA.Children);
            Assert.True(card.IsEffectivelyVisible, "the stranded card is genuinely still on screen");
        }, default);
    }

    [Fact]
    public async Task ATabSwitch_NeverDetachesTheEditor_SoADetachHookWouldNeverRun()
    {
        await _session.Dispatch(() =>
        {
            var t = TwoTabs.Build();
            int detached = 0;
            t.EditorA.DetachedFromVisualTree += (_, _) => detached++;
            Dispatcher.UIThread.RunJobs();

            t.SwitchToB();

            // ⛔ The signal one reaches for first, and it is silent — a dismissal hooked to it would never
            // run. ParameterHelper.Hide()'s own comment asserted the opposite ("after a tab switch the
            // editor is detached, GetOverlayLayer answers null") as established fact until this was measured.
            Assert.Equal(0, detached);
            // …and the editor is still in the tree, so the overlay still resolves: nothing about the card
            // becomes unreachable. It is simply never asked to go.
            Assert.NotNull(OverlayLayer.GetOverlayLayer(t.EditorA));
        }, default);
    }

    // ⚠⚠ EffectiveViewportChanged is deliberately NOT asserted here, and the reason is worth recording:
    // two runs of the same scenario disagreed (0× while probing, 1× in the first version of this test),
    // because its FIRST delivery after subscribing is asynchronous and can land inside the switch's
    // dispatcher pump. So "it does not fire on a tab switch" is not a fact this harness can establish, the
    // fix does not use it, and an assertion on it would be flaky rather than protective. ⭐ Tuning it until
    // it passed would have turned an unstable measurement into a claim.

    [Fact]
    public async Task ATabSwitch_DoesFire_LayoutUpdated_AndTakesFocusOffTheHiddenEditor()
    {
        await _session.Dispatch(() =>
        {
            var t = TwoTabs.Build();
            t.EditorA.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(t.EditorA.IsFocused, "the fixture must start with the editor focused");

            int layout = 0, lostFocus = 0;
            t.EditorA.LayoutUpdated += (_, _) => layout++;
            t.EditorA.LostFocus += (_, _) => lostFocus++;

            t.SwitchToB();

            // ⭐ The two triggers the fix uses. LostFocus matters because it is raised BY the visibility
            // change itself — hiding an ancestor takes focus off the element — so the signal does not depend
            // on the tab strip being focusable.
            Assert.True(layout > 0, "LayoutUpdated is one of the two triggers the dismissal hangs on");
            Assert.True(lostFocus > 0, "hiding an ancestor must take focus off the element");
            Assert.False(t.EditorA.IsFocused);
        }, default);
    }

    [Fact]
    public async Task IsEffectivelyVisible_IsTheInvariant_AndFlipsOnATabSwitch()
    {
        await _session.Dispatch(() =>
        {
            var t = TwoTabs.Build();
            Assert.True(t.EditorA.IsEffectivelyVisible);

            t.SwitchToB();

            Assert.False(t.EditorA.IsEffectivelyVisible);
        }, default);
    }

    [Fact]
    public void Visual_HasNoObservableIsEffectivelyVisibleProperty_InThisAvaloniaVersion()
    {
        // ⛔ The reason the fix asks on a trigger instead of subscribing to the property: there is no
        // property to subscribe to. Avalonia 12.1.1 computes IsEffectivelyVisible without exposing an
        // AvaloniaProperty for it. If a future version adds one, this test goes red — and that is the
        // moment to replace both triggers with a single subscription.
        var property = typeof(Visual).GetField("IsEffectivelyVisibleProperty");
        Assert.Null(property);
    }

    // ── The wiring, read from the source ─────────────────────────────────────────────────────────
    //
    // ⚠ A source guard, because the behavioural path needs a TextEditor (see the class doc). It is worth
    // having anyway: it is the only check that the two triggers route through the INVARIANT rather than
    // hiding unconditionally, which is what keeps focus moving inside a still-visible tab harmless.

    private static string HelperSource()
    {
        var path = IoPath.Combine(RepoRoot(), "src", "EmberTern.App", "Completion", "ParameterHelper.cs");
        Assert.True(File.Exists(path), $"ParameterHelper.cs not found at {path}");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(IoPath.Combine(dir, "EmberTern.slnx")))
            dir = IoPath.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void TheHelper_WatchesBothTriggers_AndDecidesOnTheInvariant()
    {
        var src = HelperSource();
        Assert.Contains("_editor.LayoutUpdated += OnEditorLayoutUpdated", src);
        Assert.Contains("_editor.TextArea.LostFocus += OnEditorLostFocus", src);
        Assert.Contains("_editor.LayoutUpdated -= OnEditorLayoutUpdated", src);
        Assert.Contains("_editor.TextArea.LostFocus -= OnEditorLostFocus", src);
        // ONE decision, and it is the invariant — not "hide on focus loss", which would also close the card
        // when focus merely moves to another control on the same visible tab.
        Assert.Contains("if (_card is not null && !_editor.IsEffectivelyVisible) Hide();", src);
    }

    [Fact]
    public void TheHelper_RefusesToShowACardOnAnEditorThatIsNotOnScreen()
    {
        // ⚠ The way IN needs the same invariant: ShowAt can resume from an async metadata warm long after
        // the user switched tabs, and the overlay it would add to is the new tab's overlay too.
        var src = HelperSource();
        Assert.Contains("if (!_editor.IsEffectivelyVisible) return;", src);
    }
}
