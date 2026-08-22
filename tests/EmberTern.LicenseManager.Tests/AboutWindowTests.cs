using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EmberTern.LicenseManager;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The About window, and the single-source-of-identity rule it rests on.</b>
///
/// <para>Two independent things are guarded here, and the second is the one that goes stale silently:
/// that the window renders what it claims to, and that <b>no version number is written in code</b>.
/// EmberTern's own <c>AppInfoTests</c> enforces the second for <c>src/EmberTern.App</c> and its sweep is
/// scoped to that folder, so before L9 nothing watched this project at all.</para>
///
/// <para>⚠⚠ <b>Every headless test here RETURNS its <c>Task</c></b> (gotchas #374 / #391): the
/// expression-bodied <c>void</c> form compiles while discarding the <c>Task</c>, xUnit never awaits it,
/// and no assertion in the body can fail the test — five such tests once shipped in this very assembly.
/// ⚠ A lambda that <c>await</c>s must also RETURN a value, so it binds to
/// <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;)</c> rather than to <c>Action</c> (which would be
/// <c>async void</c>).</para>
///
/// <para>⛔ This class joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture —
/// an <c>IClassFixture</c> silently creates a SECOND headless session in the process (gotchas #94 / #226 /
/// #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class AboutWindowTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private readonly HeadlessUnitTestSession _session;

    public AboutWindowTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── Identity comes from the build, and from nowhere else ─────────────────────────────────────────

    /// <summary>
    /// ⭐ Every value the window shows agrees with the one <c>PropertyGroup</c> that declares it.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>Product</c> is read from THIS project's csproj rather than from <c>Directory.Build.props</c>,
    /// because it is the one identity value the project overrides — and the override is the whole point:
    /// before it, the executable claimed its product was "EmberTern".
    /// </remarks>
    [Fact]
    public void EveryIdentityValueComesFromTheBuild()
    {
        Assert.Equal(SharedProperty("Version"), ManagerInfo.Version);
        Assert.Equal(SharedProperty("Company"), ManagerInfo.Author);
        Assert.Equal(SharedProperty("Copyright"), ManagerInfo.Copyright);
        Assert.Equal(ManagerProperty("Product"), ManagerInfo.Product);

        Assert.NotNull(ManagerInfo.ReleaseDate);
        Assert.Equal(
            SharedProperty("ReleaseDate"),
            ManagerInfo.ReleaseDate!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        // ⚠ The whole reason <IncludeSourceRevisionInInformationalVersion> is off AND ManagerInfo truncates
        //   at '+': without either, the SDK's source-revision hash would be on screen.
        Assert.DoesNotContain("+", ManagerInfo.Version, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>The product NAMES ITSELF, not the product it administers.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ Asserted as a relationship rather than against a literal: what matters is that the two
    /// applications' <c>&lt;Product&gt;</c> values are DIFFERENT and that this one is the manager's. A test
    /// spelling the name would be a third copy of it.
    /// </remarks>
    [Fact]
    public void TheManagerDoesNotClaimToBeTheProduct()
    {
        var shared = SharedProperty("Product");
        var mine = ManagerProperty("Product");

        Assert.NotEqual(shared, mine);
        Assert.StartsWith(shared, mine, StringComparison.Ordinal);
        Assert.Equal(mine, ManagerInfo.Product);
    }

    /// <summary>
    /// ⛔ <b>The current version's text appears nowhere under this project.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ A hand-typed copy would not merely duplicate the build — it would go stale on the next release
    /// with a green build, which is gotcha #284's exact shape. ⭐ Comments are swept too: a comment quoting
    /// today's number teaches the next reader something that stops being true.
    /// </remarks>
    [Fact]
    public void NoVersionNumberIsHardCodedInTheManager()
    {
        var version = ManagerInfo.Version;
        Assert.NotEmpty(version);

        var offenders = ManagerSourceFiles()
            .Where(f => File.ReadAllText(f).Contains(version, StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The version must live only in Directory.Build.props, but its text also appears in: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// ⭐⭐ <b>The test above is not enough, and the gap is worth understanding rather than patching.</b>
    /// It searches for TODAY's version, so a literal left over from an EARLIER one sails straight past —
    /// which is not hypothetical: the product's status bar carried <c>Text="EmberTern 0.1.0"</c>, stale for
    /// who knows how long, and the user found it by seeing two surfaces disagree on screen.
    ///
    /// <para>So this one looks for the SHAPE of a version, in the two places one can reach a reader.</para>
    /// </summary>
    [Fact]
    public void NoVersionShapedLiteralCanReachTheScreen()
    {
        var inXaml = new Regex(@"(?:Text|Content|Header|ToolTip\.Tip)=""[^""]*\d+\.\d+\.\d+[^""]*""");
        var inCode = new Regex(@"""[^""\n]*(?<![§.\d])\d+\.\d+\.\d+(?![\d.])[^""\n]*""");

        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in ManagerSourceFiles())
        {
            bool xaml = file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);

            foreach (var (line, number) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
            {
                var trimmed = line.TrimStart();

                // ⚠ Comment lines are excluded in C# because prose legitimately names a literal that was
                //   REMOVED — a historical fact that cannot go stale. Today's number is still banned from
                //   comments, by the test above.
                if (!xaml && (trimmed.StartsWith("//", StringComparison.Ordinal)
                              || trimmed.StartsWith("*", StringComparison.Ordinal)
                              || trimmed.StartsWith("/*", StringComparison.Ordinal)))
                {
                    continue;
                }

                var hit = (xaml ? inXaml : inCode).Match(line);
                if (hit.Success)
                {
                    offenders.Add($"{Relative(file)}:{number} → {hit.Value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A version-shaped literal on a surface that can be displayed is a second source of truth "
            + "waiting to go stale; read it from ManagerInfo instead. Found: "
            + string.Join(", ", offenders));
    }

    // ── The window as it is actually realised ────────────────────────────────────────────────────────

    /// <summary>⭐ It builds, and it is themed, in BOTH themes — rule 7 for every new window.</summary>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheWindowBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);
            var window = Show();

            Assert.NotNull(window.Content);
            Assert.Equal(
                HeadlessTheme.Brush("BackgroundBrush")!.Color,
                ((ISolidColorBrush)window.Background!).Color);
            Assert.Equal(
                HeadlessTheme.Brush("ForegroundBrush")!.Color,
                ((ISolidColorBrush)window.Foreground!).Color);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The four identity lines are on screen with the values the build declared</b> — asserted on the
    /// REALISED text, not on what the XAML spells.
    /// </summary>
    /// <remarks>
    /// ⚠ Reading the rendered <c>Text</c> is what catches a binding that resolves to nothing: a broken
    /// <c>{Binding}</c> raises no error and leaves the block empty, and a stale <c>x:DataType</c> makes a
    /// template stop matching and renders a type NAME instead (gotcha #370).
    /// </remarks>
    [Fact]
    public Task TheIdentityLinesShowWhatTheBuildDeclared() =>
        _session.Dispatch(() =>
        {
            var window = Show();

            Assert.Equal(ManagerInfo.Product, ViewProbe.Named<TextBlock>(window, "ProductName").Text);
            Assert.Contains(
                ManagerInfo.Version,
                ViewProbe.Named<TextBlock>(window, "VersionLine").Text!,
                StringComparison.Ordinal);
            Assert.Contains(
                ManagerInfo.Author,
                ViewProbe.Named<TextBlock>(window, "AuthorLine").Text!,
                StringComparison.Ordinal);
            Assert.Equal(ManagerInfo.Copyright, ViewProbe.Named<TextBlock>(window, "CopyrightLine").Text);

            var released = ViewProbe.Named<TextBlock>(window, "ReleasedLine");
            Assert.True(released.IsVisible);
            Assert.Contains(
                ManagerInfo.ReleaseDate!.Value.ToString(
                    "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                released.Text!,
                StringComparison.Ordinal);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The mark actually LOADS.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ This is the one assertion the window could not do without. The logo lives in
    /// <c>EmberTern.App</c> and is reachable here only because the csproj LINKS it as an
    /// <c>AvaloniaResource</c>; an <c>avares://</c> path that does not resolve makes the <c>Image</c> render
    /// NOTHING — no exception, no binding error, and a window whose subject is simply absent. So the test
    /// asserts a decoded bitmap with real dimensions, never merely that a <c>Source</c> was assigned.
    /// </remarks>
    [Fact]
    public Task TheBrandMarkResolvesAndDecodes() =>
        _session.Dispatch(() =>
        {
            var window = Show();

            var image = window.GetVisualDescendants().OfType<Image>().Single();
            var bitmap = Assert.IsAssignableFrom<Bitmap>(image.Source);

            Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0);

            // ⚠ Square, because it is downscaled to a square slot: a non-square master would letterbox and
            //   look mis-sized rather than broken. BRANDING.md's pipeline produces a square canvas.
            Assert.Equal(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }, default);

    /// <summary>
    /// ⭐ Closing works from the keyboard both ways — the pairing every dialog here carries.
    /// </summary>
    [Fact]
    public Task TheCloseButtonAnswersBothEnterAndEscape() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var close = ViewProbe.Named<Button>(window, "CloseButton");

            Assert.True(close.IsDefault);
            Assert.True(close.IsCancel);
        }, default);

    // ── The menu row that reaches it ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The row is ordinarily available — not "unblocked".</b>
    /// </summary>
    /// <remarks>
    /// ⚠ It stood <c>IsEnabled="False"</c> from L6.1a as a deliberate placeholder, so the assertion that
    /// matters is that the disabling is GONE from the markup and a handler is attached. ⛔ Asserting only
    /// <c>IsEnabled</c> at run time would pass on a row wired to nothing.
    /// </remarks>
    [Fact]
    public void TheAboutRowIsEnabledAndWired()
    {
        var markup = MarkupOf(
            Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager", "Views", "MainWindow.axaml"));

        var row = Regex.Match(markup, @"<MenuItem Name=""AppMenuAbout""(?:[^>]|\n)*?/>");
        Assert.True(row.Success, "The About row is no longer in the application menu.");

        Assert.DoesNotContain("IsEnabled=\"False\"", row.Value, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAppMenuAboutClick\"", row.Value, StringComparison.Ordinal);

        // ⛔ The stale explanation goes with the placeholder — the L8.5 rule for
        //    `ApplicationLanguageUnavailable`: a sentence saying a feature does not exist YET is worse than
        //    none once it does. It must be gone from the CATALOGS too, not just from the row.
        // ⚠⚠ Read from COMMENT-STRIPPED markup and matched as a resx ATTRIBUTE, not as a bare substring.
        //    Both halves are gotcha #396, and this test met it on its first run: the markup's own comment
        //    explains that the tooltip was removed, and a raw scan read that prose as the violation.
        Assert.DoesNotContain("Main.NotAvailableYet", markup, StringComparison.Ordinal);

        foreach (var catalog in new[] { "Strings.resx", "Strings.pl.resx" })
        {
            var text = File.ReadAllText(Path.Combine(
                RepositoryRoot, "src", "EmberTern.LicenseManager", "Localization", catalog));
            Assert.DoesNotContain("name=\"Main.NotAvailableYet\"", text, StringComparison.Ordinal);
        }
    }

    // ── Language ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The window follows a language change while it is open.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ Every line here is composed in C#, so it resolves correctly on READ and is never re-read unless
    /// something says so. Without <c>AboutViewModel</c>'s weak subscription the window would keep rendering
    /// the old language while everything around it changed — no binding error, no exception. That is the
    /// shape L8.4 found four times over, and L8.6's two defects were the same thing one layer out.
    ///
    /// <para>⭐ It asserts the RENDERED text of the realised window, and it asserts a real DIFFERENCE
    /// between the two languages — an assertion that only checked "not empty" would pass on frozen text.</para>
    /// </remarks>
    [Fact]
    public Task TheOpenWindowFollowsALanguageChange() =>
        _session.Dispatch(() =>
        {
            using var isolated = Loc.IsolateSubscribersForVerification();

            try
            {
                Loc.Apply(ApplicationLanguages.English);

                var window = Show();
                var version = ViewProbe.Named<TextBlock>(window, "VersionLine");
                var close = ViewProbe.Named<Button>(window, "CloseButton");

                var englishVersion = version.Text;
                var englishClose = close.Content as string;

                Loc.Apply(ApplicationLanguages.Polish);
                window.UpdateLayout();

                Assert.NotEqual(englishVersion, version.Text);
                Assert.NotEqual(englishClose, close.Content as string);

                // ⭐ And the VALUE travelled unchanged through both: the words moved, the version did not.
                Assert.Contains(ManagerInfo.Version, version.Text!, StringComparison.Ordinal);

                return true;
            }
            finally
            {
                Loc.Apply(ApplicationLanguages.Default);
            }
        }, default);

    /// <summary>
    /// ⭐ Not one of the window's five wordings falls through to its own key.
    /// </summary>
    /// <remarks>
    /// ⚠ A missing entry resolves to the KEY, which renders perfectly and looks like a label
    /// (<c>"About.Version"</c>) — the failure mode `StatusMessageContractTests` records. Checked in BOTH
    /// languages, because a key present in English and absent in Polish is the common half of it.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public void NoWordingFallsThroughToItsKey(string language)
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.Apply(language);
            var about = new AboutViewModel();

            foreach (var rendered in new[]
                     {
                         about.Title, about.VersionText, about.ReleasedText,
                         about.AuthorText, about.CloseText,
                     })
            {
                Assert.NotEmpty(rendered);
                Assert.DoesNotContain(AboutCatalog.KeyPrefix, rendered, StringComparison.Ordinal);
            }
        }
        finally
        {
            Loc.Apply(ApplicationLanguages.Default);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static AboutWindow Show()
    {
        var window = new AboutWindow { DataContext = new AboutViewModel() };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static string SharedProperty(string name) =>
        Property(Path.Combine(RepositoryRoot, "Directory.Build.props"), name);

    private static string ManagerProperty(string name) =>
        Property(
            Path.Combine(
                RepositoryRoot, "src", "EmberTern.LicenseManager", "EmberTern.LicenseManager.csproj"),
            name);

    /// <summary>An MSBuild property's value, read out of the project file that declares it.</summary>
    /// <remarks>
    /// ⚠⚠ <b>It reads COMMENT-STRIPPED markup, and that is not tidiness — this helper failed on its first
    /// run without it.</b> The comment beside the override in <c>EmberTern.LicenseManager.csproj</c>
    /// explains the change by quoting the value it replaced (<c>&lt;Product&gt;EmberTern&lt;/Product&gt;</c>),
    /// and the regex matched the PROSE before the declaration. ⭐ Same shape as gotcha #396, which the
    /// repository already carries twice: a rule stated in a comment is not the rule being read.
    /// </remarks>
    private static string Property(string file, string name)
    {
        var match = Regex.Match(
            MarkupOf(file), $@"<{Regex.Escape(name)}>([^<]*)</{Regex.Escape(name)}>");

        Assert.True(match.Success, $"<{name}> is not declared in {Path.GetFileName(file)}");
        return match.Groups[1].Value;
    }

    /// <summary>
    /// A markup file without its comments — the shape <c>XamlLocalizationTests.CodeOf</c> already uses.
    /// </summary>
    /// <remarks>
    /// ⚠ Non-greedy and single-line, so nested-looking prose does not swallow real markup after it. ⛔ Do
    /// not "improve" this into a full XML parse: these guards must also read a file that does not parse.
    /// </remarks>
    private static string MarkupOf(string file) =>
        Regex.Replace(File.ReadAllText(file), "(?s)<!--.*?-->", string.Empty);

    private static System.Collections.Generic.IEnumerable<string> ManagerSourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager"), "*.*",
                SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase));

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
