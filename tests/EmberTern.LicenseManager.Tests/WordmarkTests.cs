using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The wordmark in the title strip, and the one new colour role it introduced.</b>
///
/// <para>Three separate claims are guarded, and they fail for three different reasons:</para>
/// <list type="number">
///   <item><b>the mark renders as a mark</b> — three runs, the middle one on the brand brush, asserted on
///   the REALISED control rather than on what the XAML spells;</item>
///   <item><b>the token exists in BOTH theme dictionaries</b> — a token defined in one is a bug, and
///   <c>{DynamicResource}</c> does not throw on a missing key: the property silently keeps its default;</item>
///   <item><b>the token is IDENTITY, never a signal</b> — nothing but the wordmark may paint with it.</item>
/// </list>
///
/// <para>⚠⚠ <b>Every headless test here RETURNS its <c>Task</c></b> (gotchas #374 / #391).</para>
///
/// <para>⛔ Joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture (#94 / #226 /
/// #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class WordmarkTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// The minimum contrast the mark must clear against the surface it sits on.
    /// </summary>
    /// <remarks>
    /// ⭐ 4.5:1 and not 3:1: the large-text exemption starts at 18 pt, or 14 pt bold — the wordmark is
    /// <c>Text.Title</c>, i.e. <b>14 px</b> SemiBold, which is below both. ⚠ So the threshold is the
    /// ordinary body-text one, and that is what makes the Light value the hard half.
    /// </remarks>
    private const double MinimumContrast = 4.5;

    private readonly HeadlessUnitTestSession _session;

    public WordmarkTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── The mark as it is actually realised ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Three runs, and the middle one carries the brand colour.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ Asserted by reading the runs off the realised <c>TextBlock</c>, never by matching the markup: a
    /// <c>{DynamicResource}</c> that resolves to nothing raises no error and leaves <c>Foreground</c> at
    /// its inherited value, so the mark would render in one flat colour and look merely plain. That is the
    /// failure this test exists for, and the markup cannot show it.
    /// </remarks>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheMarkRendersInTwoTones(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var window = Show(manager);
            var mark = WordmarkOf(window);
            var runs = RunsOf(mark);
            Assert.Equal(3, runs.Count);

            var brand = HeadlessTheme.Brush("BrandEmberBrush");
            Assert.NotNull(brand);

            // ⭐ The middle run — and ONLY the middle run — is the brand colour.
            Assert.Equal(brand!.Color, ((ISolidColorBrush)runs[1].Foreground!).Color);
            Assert.NotEqual(brand.Color, Tone(runs[0]));
            Assert.NotEqual(brand.Color, Tone(runs[2]));

            // ⭐ And the descriptor is the quiet one, at the smaller grade.
            Assert.Equal(
                HeadlessTheme.Brush("SubtleForegroundBrush")!.Color,
                ((ISolidColorBrush)runs[2].Foreground!).Color);
            Assert.True(runs[2].FontSize < mark.FontSize);
            Assert.Equal(FontWeight.Normal, runs[2].FontWeight);
        }, default);

    /// <summary>
    /// ⭐ The whole name is still there, in order, exactly as before — the mark is a PRESENTATION change.
    /// </summary>
    /// <remarks>
    /// ⚠ It reads the realised runs and joins them, so a lost space or a swapped run fails here. ⭐ The
    /// expected value is composed from the window's own <c>Title</c>, not typed out: a literal here would
    /// be a fourth copy of the product name.
    /// </remarks>
    [Fact]
    public Task TheMarkStillSpellsTheWholeName() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager);
            var spelled = string.Concat(RunsOf(WordmarkOf(window)).Select(r => r.Text));

            Assert.Equal(window.Title, spelled);
        }, default);

    /// <summary>
    /// ⚠ It must not wrap. The implicit <c>TextBlock</c> style sets <c>Wrap</c>, and a wordmark that wraps
    /// inside a title strip one row high is CLIPPED rather than wrapped.
    /// </summary>
    [Fact]
    public Task TheMarkDoesNotWrap() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            Assert.Equal(TextWrapping.NoWrap, WordmarkOf(Show(manager)).TextWrapping);
        }, default);

    // ── The token ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Defined in BOTH theme dictionaries, and with DIFFERENT values.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The difference is the point, not an accident: one shared value was tried on paper and the Light
    /// half is where it breaks (the dark ember measures 1.72:1 on Light chrome). ⛔ A test pinning them
    /// EQUAL would be a test forbidding the very thing the split exists for.
    /// </remarks>
    [Fact]
    public Task TheBrandColourIsDefinedInBothThemesAndDiffers() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");
            var dark = HeadlessTheme.Brush("BrandEmberBrush");

            HeadlessTheme.UseTheme("Light");
            var light = HeadlessTheme.Brush("BrandEmberBrush");

            Assert.NotNull(dark);
            Assert.NotNull(light);
            Assert.NotEqual(dark!.Color, light!.Color);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The mark is legible on the surface it actually sits on, in both themes — measured, not judged.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Against <c>ChromeStrongBrush</c>, because that is what <c>Border.chrome</c> paints and the
    /// wordmark lives in it. Measuring against the window background would measure a surface the mark is
    /// never on.
    /// </remarks>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheMarkIsLegibleOnTheChromeItSitsOn(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            var brand = HeadlessTheme.Brush("BrandEmberBrush")!.Color;
            var chrome = HeadlessTheme.Brush("ChromeStrongBrush")!.Color;

            var measured = Contrast(brand, chrome);

            Assert.True(
                measured >= MinimumContrast,
                $"The wordmark's brand colour measures {measured:0.00}:1 against the title strip in "
                + $"{theme}, below the {MinimumContrast:0.0}:1 this size requires. ⛔ The repair is a "
                + "different value for THAT theme in Colors.axaml — never one value for both, and never "
                + "a larger font to buy the large-text exemption.");
        }, default);

    /// <summary>
    /// ⛔⛔ <b>The brand colour is IDENTITY and paints nothing else.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ The palette already carries two warm colours that MEAN something — <c>TransactionActiveBrush</c>
    /// ("an open transaction / caution-pause") and <c>WarningBrush</c>. A brand colour that starts
    /// appearing on controls becomes a third signal nobody defined, and the first place that would happen
    /// is somebody reaching for "the orange one". ⭐ So the guard is on the CONSUMER COUNT, in both
    /// applications' style layers and views.
    /// </remarks>
    [Fact]
    public void NothingButTheWordmarkPaintsWithTheBrandColour()
    {
        var consumers = new List<string>();

        foreach (var file in MarkupFiles())
        {
            // ⚠ Comment-stripped: `Colors.axaml`'s own comment explains the role at length, and a raw scan
            //   would read that prose as four more consumers (gotcha #396).
            var markup = Regex.Replace(File.ReadAllText(file), "(?s)<!--.*?-->", string.Empty);

            foreach (Match hit in Regex.Matches(markup, @"BrandEmber(?:Color|Brush)"))
            {
                consumers.Add($"{Relative(file)} → {hit.Value}");
            }
        }

        // ⭐ The arithmetic is spelled out because it is not the number one would guess, and this test
        //    told me so on its first run (expected 5, measured 7):
        //        Colors.axaml   2 × `<Color x:Key="BrandEmberColor">`      — Dark and Light
        //                     + 2 × `<SolidColorBrush x:Key="BrandEmberBrush" … BrandEmberColor>`,
        //                           and each of those lines names BOTH keys, so it matches twice  = 6
        //        MainWindow     1 × the wordmark's own consumption                                = 1
        //    ⛔ A total above 7 means something else started painting with the brand colour; the repair is
        //       to give that thing a role of its own, never to raise this number.
        Assert.Equal(7, consumers.Count);

        Assert.Equal(
            6,
            consumers.Count(c => c.Contains("Colors.axaml", StringComparison.OrdinalIgnoreCase)));

        Assert.Single(
            consumers, c => c.Contains("MainWindow.axaml", StringComparison.OrdinalIgnoreCase));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The real main window over a real register — the only way its title strip exists at all.
    /// </summary>
    /// <remarks>
    /// ⚠ A <c>MainWindow</c> with no <c>ShellViewModel</c> is not a cheaper version of this: the strip's
    /// other children bind to the shell, and a guard that avoided the fixture would be measuring a window
    /// the application never shows.
    /// </remarks>
    private static MainWindow Show(ManagerFixture manager)
    {
        var window = new MainWindow
        {
            DataContext = new ShellViewModel(manager.Register, manager.Session, manager.Paths),
        };

        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static TextBlock WordmarkOf(MainWindow window) =>
        ViewProbe.Named<TextBlock>(window, "Wordmark");

    /// <summary>The mark's runs, in document order.</summary>
    /// <remarks>
    /// ⚠ <see cref="TextBlock.Inlines"/> holds <see cref="Inline"/>, so the cast is the assertion that the
    /// mark is made of runs at all — a single-run mark would fail the count above rather than here.
    /// </remarks>
    private static List<Run> RunsOf(TextBlock block) =>
        block.Inlines!.OfType<Run>().ToList();

    private static Color Tone(Run run) =>
        run.Foreground is ISolidColorBrush brush ? brush.Color : Colors.Transparent;

    // ⭐ WCAG 2.x relative luminance and contrast, written out rather than approximated: the whole value of
    //   this guard is that it computes the same number a reviewer would look up.
    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c) =>
        (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Channel(byte value)
    {
        var v = value / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static IEnumerable<string> MarkupFiles() =>
        new[] { "EmberTern.App", "EmberTern.LicenseManager" }
            .Select(project => Path.Combine(RepositoryRoot, "src", project))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot, file);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "EmberTern.LicenseManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
