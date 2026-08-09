using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Product Polish M3.5 / Z-6 — the contract that makes the toolbar's "create …" icons share ONE geometry
/// per object kind with the metadata tree.
///
/// <para>⭐⭐ What is actually being defended. Before M3.5 the toolbar carried nine
/// <c>Icon.&lt;Kind&gt;Plus</c> geometries that were hand-composed <b>copies</b> of the plain
/// <c>Icon.&lt;Kind&gt;</c> ones, each with its glyph squeezed to ~11 of 24 units to make room for a plus
/// beside it. Two defects in one: the glyph rendered at ~62 % of the size its own counterpart has in the
/// tree, and the copy could drift from the original silently. <see cref="EmberTern.App.Controls.CreateIcon"/>
/// removed both by referencing the plain geometry and drawing the badge itself.</para>
///
/// <para>⚠ Why a SOURCE guard and not a rendering one. The regression this class exists to catch is
/// "someone re-adds a <c>*Plus</c> geometry" or "someone puts a written-out path into a
/// <c>CreateIcon</c>" — both of which <b>render perfectly</b>. There is no visual symptom to assert
/// against: a hand-copied path looks right on the day it is written and wrong only after the original
/// changes, months later. The mechanism is the only observable, so the source is the only place to
/// assert it. Same reasoning as gotcha #284 (a guard must key on the value's SOURCE, not its contents).</para>
///
/// <para>⚠ This class constructs no Avalonia controls, so it belongs to the MAIN test partition, not the
/// headless filter (handover §8's criterion). The companion rendering assertions — that the ControlTheme is
/// reachable and both badge brushes resolve in both themes — live in
/// <see cref="DesignTokenApplicationTests"/>, which is already in that filter.</para>
/// </summary>
public class CreateIconContractTests
{
    private static readonly string[] Kinds =
        ["Table", "View", "Procedure", "Trigger", "Function", "Generator", "Domain", "Package", "Exception"];

    /// <summary>
    /// ⛔ The nine composed "…Plus" geometries must stay deleted. Re-adding one is the exact regression
    /// M3.5 removed, and it is invisible: the icon renders, just small again.
    /// ⚠ <c>Icon.Plus</c> (the bare plus, used on its own) and <c>Icon.FolderPlus</c> (genuine Lucide,
    /// full-size glyph with the plus INSIDE the folder body — the model M3.5 followed) are legitimate and
    /// deliberately allowed.
    /// </summary>
    [Fact]
    public void NoComposedPlusGeometryComesBack()
    {
        var geometries = File.ReadAllText(GeometriesPath());

        var offenders = Kinds
            .Where(k => geometries.Contains($"x:Key=\"Icon.{k}Plus\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Wróciły skomponowane geometrie `Icon.<Rodzaj>Plus`: " + string.Join(", ", offenders) + ".\n"
            + "Każda z nich jest ręczną KOPIĄ `Icon.<Rodzaj>` ze glifem ściśniętym do ~11 z 24 jednostek — "
            + "czyli ~62 % rozmiaru, jaki ten sam glif ma w drzewie metadanych, plus możliwość cichego "
            + "rozjazdu z oryginałem.\n"
            + "Akcja „utwórz …\" to `<controls:CreateIcon Data=\"{StaticResource Icon.<Rodzaj>}\" ... />` — "
            + "zero nowej geometrii. Dozwolone są wyłącznie Icon.Plus i Icon.FolderPlus.");
    }

    /// <summary>
    /// Every <c>CreateIcon</c> in every view must take its glyph from a <b>plain</b> geometry by reference.
    /// A written-out path, or a "…Plus" key, re-creates the copy this control exists to remove.
    /// </summary>
    [Fact]
    public void EveryCreateIconReferencesAPlainGeometry()
    {
        foreach (var (file, text) in Views())
        {
            foreach (var match in Regex.Matches(text, @"<controls:CreateIcon\s+([^>]*?)/>", RegexOptions.Singleline).Cast<Match>())
            {
                var attrs = match.Groups[1].Value;
                var data = Regex.Match(attrs, @"Data\s*=\s*""\{StaticResource\s+(Icon\.[A-Za-z]+)\}""");

                Assert.True(data.Success,
                    $"{Path.GetFileName(file)}: `CreateIcon` bez `Data=\"{{StaticResource Icon.<Rodzaj>}}\"`.\n"
                    + "Geometria MUSI być referencją do wpisu w IconGeometries.axaml — wpisana ścieżka "
                    + "przywraca ręczną kopię, czyli dokładnie defekt, który M3.5 usunęło.\n"
                    + "Atrybuty: " + attrs.Trim());

                var key = data.Groups[1].Value;
                Assert.False(key.EndsWith("Plus", StringComparison.Ordinal),
                    $"{Path.GetFileName(file)}: `CreateIcon` używa `{key}`. "
                    + "Badge rysuje kontrolka — glif ma być wariantem PLAIN.");
            }
        }
    }

    /// <summary>
    /// All nine toolbar create buttons go through <c>CreateIcon</c>. ⚠ Pinned as a COUNT because the
    /// plausible regression is one button quietly reverted to <c>SvgIcon</c> — eight correct icons and one
    /// small one is exactly the kind of inconsistency nobody notices in a screenshot.
    /// </summary>
    [Fact]
    public void AllNineCreateActionsUseTheComposite()
    {
        var toolbar = Views().Single(v => Path.GetFileName(v.File) == "MainWindow.axaml").Text;

        var count = Regex.Matches(toolbar, @"<controls:CreateIcon\s").Count;
        Assert.True(count == Kinds.Length,
            $"Oczekiwano {Kinds.Length} `CreateIcon` w pasku narzędzi, jest {count}. "
            + "Prawdopodobna przyczyna: jeden przycisk wrócił na `SvgIcon` — wtedy osiem ikon ma "
            + "pełnowymiarowy glif, a jedna jest mała, i tego się nie zauważa na zrzucie.");

        foreach (var kind in Kinds)
        {
            Assert.Contains($"{{StaticResource Icon.{kind}}}", toolbar, StringComparison.Ordinal);
        }
    }

    private static (string File, string Text)[] Views()
        => Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Views"), "*.axaml", SearchOption.AllDirectories)
            .Select(f => (File: f, Text: File.ReadAllText(f)))
            .ToArray();

    private static string GeometriesPath()
        => Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Themes", "IconGeometries.axaml");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
