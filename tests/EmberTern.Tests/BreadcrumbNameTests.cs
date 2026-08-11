using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using EmberTern.App.Controls;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>A breadcrumb segment must render a database object's name EXACTLY.</b>
///
/// <para>⚠⚠ The debugger showed <c>XXX_ZESTAWIENIE</c> as <c>XXXZESTAWIENIE</c> — a name that does not exist,
/// in the one panel whose job is to say which routine you are standing in. The cause is not in the debugger
/// and not in any name formatting: a <c>ContentPresenter</c> given STRING content builds an
/// <see cref="AccessText"/>, and <c>AccessText</c> reads <c>_</c> as the access-key marker — it does not draw
/// it, it underlines the following letter instead. Firebird identifiers are full of underscores, so this hits
/// the majority of real ERP names.</para>
///
/// <para>⭐ The assertion is against the MECHANISM, not the pixels: with the access-key reading off, the
/// presenter realizes a plain <see cref="TextBlock"/>. Reading <c>.Text</c> could not have caught this — the
/// underscore survives in the property and is dropped only when drawn, which is exactly why the defect
/// reached a user rather than a test. The width comparison is the second half, and it is the one that speaks
/// about what is actually painted.</para>
///
/// <para>⚠ Constructs Avalonia controls, so this class joins the headless collection and never takes its own
/// class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BreadcrumbNameTests
{
    private readonly HeadlessUnitTestSession _session;

    public BreadcrumbNameTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    private const string Name = "XXX_ZESTAWIENIE";

    private static (Button Crumb, ContentPresenter Presenter) RealizeCrumb(string text)
    {
        var bar = new BreadcrumbBar { ItemsSource = new[] { text } };
        var window = new Window { Width = 800, Height = 200, Content = bar };
        window.Show();

        // A layout pass is what realizes the item containers and applies the templates.
        window.Measure(new Avalonia.Size(800, 200));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 200));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var crumb = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("crumb"));
        crumb.ApplyTemplate();
        var presenter = crumb.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(p => p.Name == "PART_ContentPresenter");
        presenter.UpdateChild();

        return (crumb, presenter);
    }

    /// <summary>
    /// The whole claim: the underscore is content, not markup.
    /// </summary>
    [Fact]
    public async Task ACrumb_RendersAnUnderscoreInAnObjectName_RatherThanEatingItAsAnAccessKey()
    {
        await _session.Dispatch(() =>
        {
            var (_, presenter) = RealizeCrumb(Name);

            Assert.False(
                presenter.RecognizesAccessKey,
                "The crumb's presenter still reads '_' as an access-key marker, so every Firebird name "
                + "containing one is rendered with the underscore missing.");

            var child = presenter.Child;
            Assert.IsNotType<AccessText>(child);
            var block = Assert.IsType<TextBlock>(child);
            Assert.Equal(Name, block.Text);
        }, default);
    }

    /// <summary>
    /// ⭐ The half that measures the PAINTED result rather than the tree: a name whose underscore is consumed
    /// occupies less width than the same name with it drawn. Without this, a future change that swaps the
    /// presenter for something else that still swallows the character would keep the test above honest-looking
    /// and the screen wrong.
    /// </summary>
    [Fact]
    public async Task ACrumbsWidth_AccountsForTheUnderscore()
    {
        await _session.Dispatch(() =>
        {
            var (withUnderscore, _) = RealizeCrumb(Name);
            var (without, _) = RealizeCrumb(Name.Replace("_", string.Empty));

            Assert.True(
                withUnderscore.Bounds.Width > without.Bounds.Width,
                $"'{Name}' rendered no wider than the same name without its underscore "
                + $"({withUnderscore.Bounds.Width} vs {without.Bounds.Width}) — the character is not being drawn.");
        }, default);
    }
}
