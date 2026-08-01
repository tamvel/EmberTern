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
            bare.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(bare.Icon);

            bare.Close();
        }, default);
    }
}
