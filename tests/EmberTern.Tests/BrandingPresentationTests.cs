using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// One fact, and it is the one that cannot be established by reading the code: the application icon
/// declared by the <c>Window</c> style in <c>Themes/ControlStyles.axaml</c> actually reaches a window.
///
/// <para>⭐ A style setter for <c>Icon</c> compiles whether or not <c>Window.Icon</c> is a styled property
/// and whether or not the converter can read an avares URI — a build proves neither. Only opening a window
/// and reading back the value the framework settled on does. "Added" is not "paints" (gotcha #251); the
/// same gap exists between "styled" and "applied".</para>
///
/// <para>⚠ Deliberately the CHEAPEST possible headless test: a bare <see cref="Window"/> and nothing else.
/// It constructs no <c>MainWindow</c> — that is what the notoriously hang-prone
/// <c>ConnectionExpandBindingProbe</c> does, and this sprint is not the place to pay that cost. The bare
/// window is also the stronger assertion: an icon reaching a window that has no XAML and no code-behind can
/// only have come from the application-level style, which is exactly the property that must hold when a
/// future window is added and nobody remembers to set one.</para>
///
/// <para>⚠ It joins <see cref="HeadlessCollection"/> and never adds its own class fixture — xunit creates an
/// <c>IClassFixture</c> once per test CLASS, and a second <c>HeadlessUnitTestSession</c> in one process is
/// what gotchas #94 / #226 / #286 forbid.</para>
///
/// <para>⛔⛔ RUNS IN THE <b>ISOLATED</b> TEST PARTITION, beside <c>ConnectionExpandBindingProbe</c> — not in the
/// grouped headless partition. Measured 2026-08-05 during the Avalonia 12.1.1 update: in the grouped run this
/// test failed roughly 1 in 3 with <i>"The calling thread cannot access this object because a different thread
/// owns it"</i>, and <b>alone it is green 6/6</b>. ⭐ The reason it is the one class that needs isolating is
/// specific, not superstition: it is the only headless test that opens a real platform <c>Window</c>
/// (<c>Show()</c>), so it is the only one whose object lives in the <c>HeadlessWindowingPlatform</c> that the
/// other classes' sessions keep touching. ⚠ Two things were tried first and are recorded so nobody retries
/// them: dropping <c>Show()</c> (the style then never applies — <c>Icon</c> reads null, so the test would pass
/// while proving nothing) and hardening the window lifecycle with try/finally + a pump (still 1 in 6).
/// ⛔ Do not move it back into the grouped filter, and do not weaken the assertion to "the setter exists in
/// the file" — that is the one thing this test exists to disprove as sufficient.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BrandingPresentationTests
{
    private readonly HeadlessUnitTestSession _session;

    public BrandingPresentationTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public async System.Threading.Tasks.Task EveryWindow_TakesTheApplicationIcon_FromTheOneStyle()
    {
        await _session.Dispatch(() =>
        {
            var bare = new Window();
            try
            {
                // Show() is REQUIRED and not ceremony: measured 2026-08-05 — with ApplyTemplate() alone the
                // style never reaches the window and Icon reads null, so dropping it would turn this into a
                // test that passes without proving anything.
                bare.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(bare.Icon);
            }
            finally
            {
                // Exception-safe teardown: without the finally, a failed assertion leaves the window open in a
                // session seven other classes keep using, and the pump makes Close() finish inside THIS
                // dispatch instead of after it returns.
                // ⚠ Measured 2026-08-05, and stated because an inert guard reads to the next author as a real
                // safety net: this hardening did NOT fix the flake it was written for — the run still failed
                // 1 in 6. What fixed it was moving this class into the ISOLATED test partition (green 6/6
                // alone, and the class doc says why). Keep the finally; do not credit it with more than it did.
                bare.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, default);
    }
}
