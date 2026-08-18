using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>The split of the DataGrid standard into <c>Themes/DataGridStyles.axaml</c> (2026-08-18) — and
/// the properties that make it worth having.</b>
///
/// <para>The grid standard used to live inside <c>ControlStyles.axaml</c>. That file cannot be linked into
/// EmberTern.LicenseManager: it binds to <c>EmberTern.App.Controls</c>, to AvaloniaEdit and to
/// <c>avares://EmberTern/...</c>, none of which that application has or may acquire (its <c>.csproj</c>
/// records the full list). The grid standard binds to nothing but theme tokens — so splitting it out is
/// what lets the License Manager's licence list BE this grid instead of resembling it.</para>
///
/// <para>⚠ Same move, same reason and same shape of guard as <see cref="IconGeometriesSplitTests"/>. Three
/// things must stay true afterwards and none of them is visible in a build:</para>
///
/// <list type="number">
/// <item><b>The file stays portable</b> — one type reference and the License Manager fails to LOAD, not
/// to build.</item>
/// <item><b>The cascade position is unchanged</b> — every selector in it repins something
/// <c>Avalonia.Controls.DataGrid/Themes/Fluent.xaml</c> paints, so it must resolve after that theme.</item>
/// <item><b>The standard actually reaches a realised grid</b> — a style file that is no longer included
/// produces no error at all, only Fluent's own much taller rows.</item>
/// </list>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DataGridStylesSplitTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session (gotchas #94 / #226 / #286).</summary>
    public DataGridStylesSplitTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── The static half: the file stays linkable ────────────────────────────────────────────────────

    [Fact]
    public void TheGridStandardCarriesNoTypeReference()
    {
        // ⭐ THE property that makes the file linkable. A `controls:` reference, an `avares://EmberTern/`
        //   URI or an xmlns into this assembly all reintroduce exactly the dependency the split removed.
        //
        // ⚠⚠ Comments stripped first — gotcha #375. The file's own header explains WHY a `controls:`
        //    reference is forbidden, and a guard that fires on the documentation of its own rule is a
        //    guard that gets suppressed.
        var text = StripComments(File.ReadAllText(GridStylesPath()));

        var offenders = new List<string>();

        foreach (var forbidden in new[] { "controls:", "using:EmberTern", "avares://EmberTern", "aecc:" })
        {
            if (text.Contains(forbidden, StringComparison.Ordinal))
            {
                offenders.Add(forbidden);
            }
        }

        Assert.True(offenders.Count == 0,
            "DataGridStyles.axaml znów odwołuje się do typów lub zasobów tej aplikacji: "
            + string.Join(", ", offenders) + ".\n"
            + "Ten plik jest LINKOWANY do EmberTern.LicenseManager (patrz jego .csproj), a tamten projekt "
            + "nie ma kontrolek EmberTern.App i mieć ich nie może. Odwołanie do typu sprawia, że styl nie "
            + "daje się wczytać w drugiej aplikacji — błąd pojawi się przy URUCHOMIENIU, nie budowaniu.\n"
            + "Miejsce na styl siatki związany z tą aplikacją to ControlStyles.axaml.");
    }

    [Fact]
    public void TheEditingStylesStayedBehindAndTheStandardCameAcross()
    {
        // ⛔ Drift in both directions. An EmberTern-only editing style landing here makes the file
        //    unportable again; a piece of the STANDARD drifting back into ControlStyles.axaml makes the
        //    two applications' grids differ, which is the whole thing this split exists to prevent.
        var standard = StripComments(File.ReadAllText(GridStylesPath()));
        var controlStyles = StripComments(File.ReadAllText(ControlStylesPath()));

        foreach (var owned in new[] { "DataGridRow", "DataGridColumnHeader", "nth-child(2n)" })
        {
            Assert.True(standard.Contains(owned, StringComparison.Ordinal),
                $"DataGridStyles.axaml nie zawiera już `{owned}` — standard siatki się rozpadł.");
            Assert.False(controlStyles.Contains(owned, StringComparison.Ordinal),
                $"`{owned}` wrócił do ControlStyles.axaml. Standard siatki ma JEDNO źródło, wspólne dla "
                + "obu aplikacji — kopia zdryfuje przy pierwszej zmianie metryki.");
        }

        // The editable-grid styles are EmberTern's own and must not travel.
        foreach (var stays in new[] { "DataGridCell TextBox", "DataGrid.field-grid", "DataGridCell Button" })
        {
            Assert.True(controlStyles.Contains(stays, StringComparison.Ordinal),
                $"`{stays}` zniknął z ControlStyles.axaml.");
            Assert.False(standard.Contains(stays, StringComparison.Ordinal),
                $"`{stays}` trafił do DataGridStyles.axaml. To styl EDYCJI siatki EmberTerna; License "
                + "Manager ma listę tylko do odczytu i nie ma tych klas.");
        }
    }

    [Fact]
    public void TheCascadePositionThatMakesTheStandardWinIsDeclared()
    {
        // ⚠⚠ Every selector in the split file repins something Fluent's DataGrid theme paints. Included
        //    BEFORE that theme it compiles, loads, and is silently overwritten — Fluent's tall rows come
        //    back with a green build.
        var app = File.ReadAllText(Path.Combine(AppFolder(), "App.axaml"));
        var controlStyles = File.ReadAllText(ControlStylesPath());

        var fluentGrid = app.IndexOf("Avalonia.Controls.DataGrid/Themes/Fluent.xaml", StringComparison.Ordinal);
        var ourStyles = app.IndexOf("Themes/ControlStyles.axaml", StringComparison.Ordinal);

        Assert.True(fluentGrid >= 0, "App.axaml nie dołącza już motywu Fluent dla DataGrida.");
        Assert.True(ourStyles > fluentGrid,
            "ControlStyles.axaml jest dołączany PRZED motywem Fluent DataGrida — standard siatki zostanie "
            + "nadpisany bez żadnego błędu.");

        Assert.Contains(
            "avares://EmberTern/Themes/DataGridStyles.axaml", controlStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLicenseManagerLinksTheFileRatherThanCopyingIt()
    {
        // ⭐ Jedno źródło standardu siatki dla dwóch aplikacji. Kopia zdryfuje przy pierwszej zmianie
        //   wysokości wiersza, a dryf będzie niewidoczny, dopóki ktoś nie postawi obu okien obok siebie.
        var managerFolder = Path.Combine(RepositoryRoot(), "src", "EmberTern.LicenseManager");
        var project = File.ReadAllText(Path.Combine(managerFolder, "EmberTern.LicenseManager.csproj"));

        Assert.Contains(
            @"..\EmberTern.App\Themes\DataGridStyles.axaml", project, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(managerFolder, "Themes", "DataGridStyles.axaml")),
            "DataGridStyles.axaml został SKOPIOWANY do License Managera. Ma zostać zlinkowany.");
    }

    // ── The runtime half: the standard reaches a realised grid ──────────────────────────────────────

    [Fact]
    public Task ARealisedGridTakesItsRowAndHeaderHeightFromTheStandard() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ The only assertion that survives the file being dropped from the cascade entirely.
            //     A missing StyleInclude raises nothing — the grid simply wears Fluent's own metrics.
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                ItemsSource = new[] { new Row("a"), new Row("b") },
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Avalonia.Data.Binding(nameof(Row.Name)),
            });

            var window = new Window { Content = grid, Width = 400, Height = 300 };
            window.Show();
            window.UpdateLayout();

            var rows = grid.GetVisualDescendants().OfType<DataGridRow>().ToList();
            Assert.NotEmpty(rows);

            // ⚠⚠ THE CELL IS WHAT CARRIES THE ROW'S HEIGHT HERE, AND THAT IS MEASURED RATHER THAN
            //    PREFERRED. `DataGridStyles.axaml` declares `DataGridRow.MinHeight = Size.Row.Grid`, and
            //    that setter has NEVER taken effect: Avalonia's DataGrid writes `MinHeight = 0` onto every
            //    row it generates as a LOCAL VALUE, and a local value outranks a style setter (CLAUDE.md
            //    UI rule §16, third route). Probed on 2026-08-18 — priority `LocalValue`, value 0.
            //    ⛔ Not repaired here: the row floor is inert in EmberTern today, the rows are compact
            //    anyway, and changing it would move every grid in the product. Recorded as a finding.
            //    ⭐ So the claim below is the one that is actually true and actually load-bearing: the
            //    CELL wears the standard, which is what keeps the row off Fluent's much taller default.
            var cell = grid.GetVisualDescendants().OfType<DataGridCell>().First();
            Assert.Equal(new Avalonia.Thickness(8, 3), cell.Padding);
            Assert.Equal(11d, cell.FontSize);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, cell.VerticalContentAlignment);
            Assert.All(rows, row => Assert.True(row.Bounds.Height <= 24d,
                $"Wiersz siatki ma {row.Bounds.Height} px. Standard z DataGridStyles.axaml nie dociera do "
                + "siatki — najprawdopodobniej plik wypadł z kaskady albo trafił przed motyw Fluenta."));

            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First();
            Assert.Equal(24d, header.MinHeight);
            Assert.IsAssignableFrom<ISolidColorBrush>(header.Background);
        }, default);

    private sealed record Row(string Name);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static string StripComments(string markup) =>
        Regex.Replace(markup, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string GridStylesPath() =>
        Path.Combine(AppFolder(), "Themes", "DataGridStyles.axaml");

    private static string ControlStylesPath() =>
        Path.Combine(AppFolder(), "Themes", "ControlStyles.axaml");

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
