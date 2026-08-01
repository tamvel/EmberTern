using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The one fact about the Design Token catalog that cannot be established by reading code or by building:
/// <b>a token declared in <c>Themes/Tokens.axaml</c> / <c>Themes/Typography.axaml</c> actually arrives at a
/// control.</b>
///
/// <para>⭐ Why a build proves nothing here. A <c>{DynamicResource Text.Title.Size}</c> setter compiles whether
/// or not the key exists, whether or not the dictionary is merged, and whether or not the value has a type the
/// target property accepts. An unresolved dynamic resource does not throw — the property silently keeps its
/// inherited default, so the failure looks like "this label is the wrong size on one screen", months later and
/// far from its cause. This is the same gap as gotcha #251 ("added" is not "paints") and the reason
/// <c>BrandingPresentationTests</c> exists.</para>
///
/// <para>⚠ Both layers are asserted, because they resolve through <i>different</i> mechanisms and one working
/// does not imply the other: the scalar layer (<c>x:Double</c>, <c>FontWeight</c>) and the composite layer
/// (<c>Thickness</c>, <c>CornerRadius</c>) — §3.2's two halves.</para>
///
/// <para>⚠ Deliberately the cheapest possible headless test: bare controls inside a bare <see cref="Window"/>,
/// no <c>MainWindow</c> (the documented hang-prone shape). A style setter reaching a control that has no XAML
/// and no code-behind can only have come from the application-level stylesheet — which is the property that has
/// to hold for every control M2b styles next.</para>
///
/// <para>⚠ It joins <see cref="HeadlessCollection"/> and never adds its own class fixture (gotchas
/// #94 / #226 / #286).</para>
///
/// <para>⛔ This test pins <b>that the token arrives</b>, not <b>which number it carries</b>. The values live in
/// the catalog and are expected to move (§4.2.4 — role before value); duplicating them here would create the
/// second copy the whole stage exists to remove. It therefore compares against the dictionary, not against a
/// literal.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DesignTokenApplicationTests
{
    private readonly HeadlessUnitTestSession _session;

    public DesignTokenApplicationTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public async Task TypographyRole_ReachesAStyledTextBlock()
    {
        await _session.Dispatch(() =>
        {
            var header = new TextBlock { Text = "Group", Classes = { "group-header" } };
            var window = new Window { Content = header };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Text.SectionHeader — the role `group-header` carries. Compared against the catalog, so the
            // assertion survives a deliberate change of the value and fails only when the wiring breaks.
            Assert.Equal(Token<double>("Text.SectionHeader.Size"), header.FontSize);
            Assert.Equal(Token<FontWeight>("Text.SectionHeader.Weight"), header.FontWeight);

            // The guard that catches a silently unresolved resource: Avalonia's default TextBlock size is 12,
            // which happens to be a real token value elsewhere — so "it looks plausible" is not evidence. The
            // section-header role is 11, so a failed resolution is visible rather than coincidental.
            Assert.NotEqual(12d, header.FontSize);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task CompositeToken_ReachesAStyledBorder()
    {
        await _session.Dispatch(() =>
        {
            var group = new Border { Classes = { "settings-group" } };
            var window = new Window { Content = group };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The composite layer (§3.2): XAML cannot compose a Thickness from two x:Double, so these are
            // pre-composed resources — a different resolution path from the scalars above.
            Assert.Equal(Token<Thickness>("Border.All"), group.BorderThickness);
            Assert.Equal(Token<Thickness>("Pad.Group"), group.Padding);
            Assert.Equal(Token<CornerRadius>("Radius.Surface"), group.CornerRadius);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Reads a token straight from the application's merged resources — the same lookup a style performs. If
    /// the key is missing this fails loudly here, instead of leaving a control on a silent default.
    /// </summary>
    private static T Token<T>(string key)
    {
        var app = Application.Current;
        Assert.NotNull(app);
        Assert.True(app!.TryFindResource(key, out var value), $"Token `{key}` is not in the application's resources.");
        return Assert.IsType<T>(value);
    }
}
