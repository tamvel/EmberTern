using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using EmberTern.App.Controls;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>The split of <c>IconGeometries.axaml</c> (2026-08-16) — and the two properties that make it
/// worth having.</b>
///
/// <para>The dictionary used to end with three <c>ControlTheme</c>s bound to <c>controls:SvgIcon</c>,
/// <c>DebuggerIcon</c> and <c>CreateIcon</c>. Those 164 lines were the ONLY type reference in a file that
/// is otherwise 86 pure <see cref="StreamGeometry"/> resources — and they were what made the whole
/// dictionary unlinkable from EmberTern License Manager, which needs the icons and must never acquire
/// this application's controls (<c>docs/design/licensing-system.md</c> §12.1).</para>
///
/// <para>So the type-bound half moved to <c>Themes/IconControlThemes.axaml</c>. Two things must stay true
/// afterwards, and neither is visible in a build:</para>
///
/// <list type="number">
/// <item><b>The geometry file stays pure</b>, or it silently stops being linkable and the License Manager
/// is back to copying icons.</item>
/// <item><b>Every <c>{StaticResource Icon.*}</c> still resolves</b>. ⚠ <c>StaticResource</c> resolves at
/// LOAD time against the dictionaries merged so far, so the split created an ordering dependency that
/// compiles perfectly when reversed and produces icons that are simply absent.</item>
/// </list>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class IconGeometriesSplitTests
{
    /// <summary>
    /// ⚠⚠ <b>A LIVE DEFECT, recorded rather than fixed — and the list must never grow to two without a
    /// decision.</b>
    ///
    /// <para><c>SqlCompletionData.cs</c> asks for <c>Icon.Name</c> for <c>SqlCompletionKind.Column</c> and
    /// for locals; the only occurrence of that key in <c>IconGeometries.axaml</c> is inside the header
    /// COMMENT that shows how to add a geometry. So the key does not exist, and column and local
    /// completion items have been rendering with no icon.</para>
    ///
    /// <para>⭐ Found by this guard on the run that introduced it — not by the split, which did not cause
    /// it. ⛔ It is NOT fixed here: choosing the glyph for a column is a design decision the user reviews,
    /// and the standing directive says a cross-cutting problem found mid-stage goes to the backlog WITH
    /// its measurement rather than into the current stage. Recorded in <c>docs/current-state.md</c>.</para>
    ///
    /// <para>⚠ An exclusion list is exactly the shape <c>CLAUDE.md</c> warns about. One entry with a
    /// reason and a scheduled resolution is a record; a second entry means the rule is wrong and the
    /// right move is to say so rather than to append.</para>
    /// </summary>
    private static readonly HashSet<string> KnownMissing = new(StringComparer.Ordinal) { "Icon.Name" };

    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session (gotchas #94 / #226 / #286).</summary>
    public IconGeometriesSplitTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── The static half: the file stays linkable ────────────────────────────────────────────────────

    [Fact]
    public void TheGeometryDictionaryCarriesNoTypeReference()
    {
        // ⭐ THE property that makes the file linkable. A `controls:` reference, a ControlTheme, a
        //   ControlTemplate or an `xmlns` pointing at this assembly all reintroduce the dependency that
        //   the split removed — and the License Manager would fail to LOAD, not to build.
        //
        // ⚠⚠ COMMENTS ARE STRIPPED FIRST, and that is gotcha #375, not tidiness: this file's own
        //    documentation explains how to write a `<controls:CreateIcon …>`, and the first version of
        //    this guard fired on that sentence. ⭐ The ratified fix is always to strip comments, never to
        //    reword the documentation — a guard that fires on its own rule is a guard that gets
        //    suppressed, and a suppressed guard reads as coverage while providing none.
        var text = StripComments(File.ReadAllText(GeometriesPath()));

        var offenders = new List<string>();

        foreach (var forbidden in new[] { "<ControlTheme", "<ControlTemplate", "controls:", "using:EmberTern" })
        {
            if (text.Contains(forbidden, StringComparison.Ordinal))
            {
                offenders.Add(forbidden);
            }
        }

        Assert.True(offenders.Count == 0,
            "IconGeometries.axaml znów odwołuje się do typów: " + string.Join(", ", offenders) + ".\n"
            + "Ten plik jest LINKOWANY do EmberTern.LicenseManager (patrz jego .csproj), a tamten projekt "
            + "nie ma kontrolek EmberTern.App i mieć ich nie może. Odwołanie do typu sprawia, że słownik "
            + "nie daje się wczytać w drugiej aplikacji — błąd pojawi się przy URUCHOMIENIU, nie przy "
            + "budowaniu.\n"
            + "Miejsce na motyw kontrolki ikonowej to Themes/IconControlThemes.axaml.");
    }

    [Fact]
    public void TheGeometriesAreAllStillThereAndOnlyGeometries()
    {
        var text = File.ReadAllText(GeometriesPath());

        var keyed = Regex.Matches(text, @"<StreamGeometry x:Key=""(Icon\.[A-Za-z0-9]+)""").Count;

        // ⚠ Pinned as "many", not as an exact number: the catalog is expected to grow, and a count nobody
        //   can change without editing a test is a count somebody eventually edits without thinking.
        //   What matters is that the split did not LOSE any — 86 existed before it.
        Assert.True(keyed >= 86,
            $"IconGeometries.axaml ma {keyed} kluczowanych geometrii; przed rozdzieleniem było 86. "
            + "Rozdzielenie miało przenieść wyłącznie trzy ControlTheme.");
    }

    [Fact]
    public void TheThreeControlThemesLandedInTheirOwnFileAndNoGeometryWentWithThem()
    {
        var text = File.ReadAllText(ControlThemesPath());

        Assert.Equal(3, Regex.Matches(text, @"<ControlTheme\s").Count);

        // ⛔ The opposite drift: a geometry drifting INTO the type-bound file is a geometry the License
        //    Manager can no longer see, and nothing else would notice.
        Assert.DoesNotContain(
            "<StreamGeometry x:Key=", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMergeOrderThatMakesTheSplitWorkIsDeclaredInAppAxaml()
    {
        // ⚠⚠ The ordering dependency the split created. `CreateIcon`'s ControlTheme resolves
        //    {StaticResource Icon.Play}, and StaticResource looks only at what is ALREADY merged. Reversed,
        //    this compiles and produces icons that are simply not there.
        var app = File.ReadAllText(Path.Combine(AppFolder(), "App.axaml"));

        var geometries = app.IndexOf("IconGeometries.axaml", StringComparison.Ordinal);
        var iconThemes = app.IndexOf("IconControlThemes.axaml", StringComparison.Ordinal);
        var controlThemes = app.IndexOf("Themes/ControlThemes.axaml", StringComparison.Ordinal);

        Assert.True(geometries >= 0, "App.axaml nie scala już IconGeometries.axaml.");
        Assert.True(iconThemes >= 0, "App.axaml nie scala IconControlThemes.axaml.");
        Assert.True(controlThemes >= 0, "App.axaml nie scala ControlThemes.axaml.");

        Assert.True(geometries < iconThemes,
            "IconControlThemes.axaml jest scalany PRZED IconGeometries.axaml. Motyw CreateIcon sięga po "
            + "{StaticResource Icon.Play}, a StaticResource widzi tylko to, co już scalone — w tej "
            + "kolejności ikony po prostu znikną, bez błędu budowania.");

        Assert.True(geometries < controlThemes,
            "ControlThemes.axaml jest scalany PRZED IconGeometries.axaml — szablon CheckBoxa sięga po "
            + "{StaticResource Icon.Check}.");
    }

    // ── The runtime half: the references actually resolve ───────────────────────────────────────────

    [Fact]
    public Task EveryIconKeyTheApplicationReferencesResolvesToAGeometry() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE test the user asked for after the split, and the only one that answers the real
            //     question. It takes every `Icon.*` key spelled anywhere in the application's markup and
            //     asks the LIVE resource system for it. A key that stopped resolving does not throw and
            //     does not warn — the icon is merely absent.
            var application = Application.Current!;
            var missing = new List<string>();

            foreach (var key in ReferencedIconKeys())
            {
                if (KnownMissing.Contains(key))
                {
                    continue;
                }

                if (!application.TryFindResource(key, application.ActualThemeVariant, out var value) ||
                    value is not Geometry)
                {
                    missing.Add(key);
                }
            }

            Assert.True(missing.Count == 0,
                $"{missing.Count} kluczy Icon.* nie rozwiązuje się w działającej aplikacji: "
                + string.Join(", ", missing.Take(15))
                + ".\nNajbardziej prawdopodobna przyczyna po rozdzieleniu z 2026-08-16: IconGeometries.axaml "
                + "nie jest scalony, jest scalony za późno, albo geometria została usunięta razem z "
                + "przeniesionymi ControlTheme.");
        }, default);

    [Fact]
    public Task TheIconControlThemesStillReachTheirControls() =>
        _session.Dispatch(() =>
        {
            // ⚠ The other half: the geometries can resolve perfectly while the moved ControlThemes fail to
            //   apply, and then every icon in the product is an unstyled, invisible TemplatedControl.
            var application = Application.Current!;
            var icon = new SvgIcon
            {
                Data = (Geometry)application.FindResource(
                    application.ActualThemeVariant, "Icon.Play")!,
            };

            var window = new Window { Content = icon };
            window.Show();
            window.UpdateLayout();

            Assert.NotNull(icon.Data);

            // The ControlTheme is what gives the control a size and a template at all; without it the
            // control realises as nothing.
            Assert.True(icon.Bounds.Width > 0 && icon.Bounds.Height > 0,
                "SvgIcon zrealizował się o zerowym rozmiarze — jego ControlTheme z "
                + "IconControlThemes.axaml nie został zastosowany.");
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every <c>Icon.*</c> key the application actually asks for, from BOTH places it asks from.
    ///
    /// <para>⚠⚠ <b>The C# half was found by an injected defect, not by design.</b> The first version of
    /// this scan read markup only; renaming <c>Icon.Sun</c> out of the dictionary did NOT turn the test
    /// red, because the theme toggle resolves its glyph in <c>ThemeToggleIconConverter</c> — a C# string
    /// literal, invisible to a markup scan. Dozens of keys are referenced that way (completion items,
    /// the editor's context menu, navigation), so a markup-only guard was covering perhaps half of the
    /// real surface while reading as complete.</para>
    /// </summary>
    private static IReadOnlyList<string> ReferencedIconKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(AppFolder(), "*.axaml", SearchOption.AllDirectories))
        {
            // ⚠ Only the geometry file itself is skipped — it DECLARES the keys rather than referencing
            //   them, and IconControlThemes.axaml is very much a consumer worth checking.
            if (string.Equals(Path.GetFileName(file), "IconGeometries.axaml", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(
                         File.ReadAllText(file), @"\{(?:Static|Dynamic)Resource\s+(Icon\.[A-Za-z0-9]+)\}"))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        foreach (var file in Directory.EnumerateFiles(AppFolder(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"""(Icon\.[A-Za-z0-9]+)"""))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        Assert.True(keys.Count > 60,
            $"Znaleziono tylko {keys.Count} odwołań do Icon.* — skanowanie prawie na pewno przestało "
            + "działać, a wtedy ten test przechodzi nie sprawdzając niczego. Musi obejmować OBIE formy: "
            + "`{StaticResource Icon.X}` w markupie i \"Icon.X\" w kodzie.");

        return [.. keys];
    }

    // ⚠ XML comments only — the markup has no other comment form, and a stripper that guessed would
    //   itself become the thing nobody trusts.
    private static string StripComments(string markup) =>
        Regex.Replace(markup, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string GeometriesPath() =>
        Path.Combine(AppFolder(), "Themes", "IconGeometries.axaml");

    private static string ControlThemesPath() =>
        Path.Combine(AppFolder(), "Themes", "IconControlThemes.axaml");

    private static string AppFolder() =>
        Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Nie znaleziono katalogu repozytorium.");
    }
}
