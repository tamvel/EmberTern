using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using EmberTern.App.Behaviors;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The placement arithmetic behind <see cref="GrowingDialogBehavior"/> — etap 5b's second QA finding.
///
/// <para>The settings import dialog is <c>SizeToContent</c> and grows once a file has been opened. Avalonia grows
/// such a window <b>downwards from its current position</b>, so a dialog centred on its owner slid its footer, and
/// with it the Import button, under the bottom edge of the screen.</para>
///
/// <para>The doing needs a desktop; the deciding does not, so it lives in a pure static and is asserted here. The
/// cases below are the ones a real screen produces: a dialog that grew past the bottom, one that never left the
/// screen, one taller than the working area at all, and a working area that does not start at the origin (a
/// taskbar, or a second monitor to the left of the primary — where a naive clamp against <c>0,0</c> is wrong in a
/// way nobody notices on a single-monitor developer machine).</para>
/// </summary>
public sealed class GrowingDialogBehaviorTests
{
    // A 1920×1080 screen with a 40px taskbar along the bottom.
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    /// <summary>The reported defect: the dialog grew and its bottom went past the working area. It is pushed up by
    /// exactly the overflow — no more, so the movement is the minimum that solves the problem.</summary>
    [Fact]
    public void ADialogThatGrewPastTheBottom_IsPushedUpByExactlyTheOverflow()
    {
        var grown = new PixelRect(600, 700, 560, 700); // bottom = 1400, i.e. 360 past the working area

        var placed = GrowingDialogBehavior.ClampOnScreen(grown, Work);

        Assert.Equal(600, placed.X);
        Assert.Equal(340, placed.Y);
        Assert.Equal(Work.Bottom, placed.Y + grown.Height);
    }

    /// <summary>⚠ The other half of "nudge, not jump": a dialog that still fits is not moved at all. Re-centring
    /// on every size change would be more disorienting than the defect.</summary>
    [Fact]
    public void ADialogThatStillFits_IsNotMoved()
    {
        var fits = new PixelRect(600, 200, 560, 500);

        Assert.Equal(fits.Position, GrowingDialogBehavior.ClampOnScreen(fits, Work));
    }

    /// <summary>
    /// A window larger than the working area cannot be made to fit, so the question is which half survives. The
    /// top-left does — the header and the start of the content — because a dialog showing its bottom-right is
    /// unreadable, and its title is what tells the user what they are looking at.
    /// </summary>
    [Fact]
    public void AWindowTallerThanTheScreen_KeepsItsTopVisible()
    {
        var huge = new PixelRect(-40, 300, 2000, 1400);

        var placed = GrowingDialogBehavior.ClampOnScreen(huge, Work);

        Assert.Equal(Work.X, placed.X);
        Assert.Equal(Work.Y, placed.Y);
    }

    /// <summary>
    /// ⚠ The working area is not the screen and does not have to start at the origin — a top-docked taskbar, or a
    /// monitor to the left of the primary, gives it a non-zero (and possibly negative) origin. Clamping against
    /// <c>0,0</c> instead of the working area's own edges passes every single-monitor test and puts the dialog
    /// under the taskbar in real use.
    /// </summary>
    [Fact]
    public void TheWorkingAreasOwnOrigin_IsWhatIsClampedAgainst()
    {
        var secondary = new PixelRect(-1920, 48, 1600, 900); // to the LEFT of the primary, taskbar on top
        var above = new PixelRect(-1800, 0, 560, 400);       // started above that working area

        var placed = GrowingDialogBehavior.ClampOnScreen(above, secondary);

        Assert.Equal(-1800, placed.X);
        Assert.Equal(48, placed.Y);
    }

    /// <summary>A dialog pushed off the right edge comes back the same way the bottom does — the rule is written
    /// once for both axes rather than only for the one the defect happened on.</summary>
    [Fact]
    public void TheSameRuleAppliesHorizontally()
    {
        var offRight = new PixelRect(1700, 100, 560, 400);

        var placed = GrowingDialogBehavior.ClampOnScreen(offRight, Work);

        Assert.Equal(Work.Right - 560, placed.X);
        Assert.Equal(100, placed.Y);
    }

    // ── Sufit wysokości (M4.4 / M‑5) ────────────────────────────────────────────────────────────────
    //
    // ⭐⭐ Reguła ratyfikowana 2026-08-09: sufit to MNIEJSZA z dwóch liczb — własnego limitu dialogu
    // i tego, co pozwala ekran — nigdy zamiennik pierwszej. Powód jest zmierzony: ręczny `MaxHeight`
    // wykonuje DWIE prace („nie przekraczaj ekranu" i „nie rośnij ponad wygodny rozmiar nawet na dużym
    // monitorze"), a mechanizm ekranowy zna tylko pierwszą.

    /// <summary>
    /// ⭐ Przypadek, przez który ta reguła w ogóle powstała: `ExecuteProcedureDialog` niesie świadome 720
    /// i na monitorze 1080 ma przy nim ZOSTAĆ. Nadpisanie dałoby 1008, czyli okno o 288 px wyższe, niż
    /// chciał autor — jako skutek uboczny dodania ochrony, której ten dialog na tym ekranie nie potrzebuje.
    /// </summary>
    [Fact]
    public void OnALargeScreen_ADialogKeepsItsOwnDeliberateCap()
    {
        Assert.Equal(720, GrowingDialogBehavior.CeilingFor(currentMax: 720, workingAreaHeight: 1032, scaling: 1.0));
    }

    /// <summary>
    /// ⭐ Druga połowa tej samej reguły — i to jest defekt, dla którego M‑5 istnieje: na ekranie 1366×768
    /// obszar roboczy zostawia 720, więc sufit 696 jest MNIEJSZY od zadeklarowanego 720 i to on wygrywa.
    /// Bez tego procedura o wielu parametrach daje okno wyższe od ekranu i stopka wychodzi poza krawędź.
    /// </summary>
    [Fact]
    public void OnASmallScreen_TheScreenWins()
    {
        Assert.Equal(696, GrowingDialogBehavior.CeilingFor(currentMax: 720, workingAreaHeight: 768 - 48, scaling: 1.0));
    }

    /// <summary>
    /// ⚠ Dialog bez własnego limitu ma <c>MaxHeight</c> równy nieskończoności, więc minimum degeneruje się
    /// do sufitu ekranu. ⭐ To jest dowód, że zmiana jest NO-OP dla obu dotychczasowych konsumentów
    /// (dialogi eksportu i importu ustawień — żaden nie deklaruje własnego limitu).
    /// </summary>
    [Fact]
    public void ADialogWithNoCapOfItsOwn_TakesTheScreenCeiling()
    {
        Assert.Equal(1008, GrowingDialogBehavior.CeilingFor(double.PositiveInfinity, 1032, 1.0));
    }

    /// <summary>
    /// ⚠ Jednostki są tu pułapką (opisaną w samej klasie): obszar roboczy przychodzi w pikselach
    /// FIZYCZNYCH, a <c>MaxHeight</c> jest w DIP-ach. Przy 150 % pominięcie skalowania dałoby sufit
    /// o połowę za wysoki — czyli mechanizm ochronny, który nie chroni.
    /// </summary>
    [Fact]
    public void TheCeilingIsExpressedInDips_NotPhysicalPixels()
    {
        Assert.Equal(664, GrowingDialogBehavior.CeilingFor(double.PositiveInfinity, 1032, 1.5));
    }

    /// <summary>
    /// ⚠ Sufit schodzi tylko w dół: policzony dwa razy jest bezczynny, a nie przeliczony ponownie. To
    /// bezpieczny kierunek i konsekwencja reguły minimum — warto ją mieć przypiętą, bo <c>ApplyCeiling</c>
    /// jest publiczne, a ktoś kiedyś zawoła je drugi raz.
    /// </summary>
    [Fact]
    public void ApplyingTheCeilingTwice_IsANoOp()
    {
        var once = GrowingDialogBehavior.CeilingFor(720, 720, 1.0);
        Assert.Equal(once, GrowingDialogBehavior.CeilingFor(once, 720, 1.0));
    }

    /// <summary>
    /// ⚠ Zdegenerowany ekran (brak informacji o rozmiarze albo skalowaniu) nie może SKASOWAĆ limitu, który
    /// dialog już zadeklarował — inaczej awaria odczytu ekranu odblokowywałaby okno zamiast je chronić.
    /// </summary>
    [Theory]
    [InlineData(0d, 1.0)]
    [InlineData(20d, 1.0)]
    [InlineData(1032d, 0d)]
    public void ADegenerateScreen_LeavesTheDeclaredCapAlone(double workingAreaHeight, double scaling)
    {
        Assert.Equal(720, GrowingDialogBehavior.CeilingFor(720, workingAreaHeight, scaling));
    }

    /// <summary>
    /// ⭐⭐ Okna z <c>SizeToContent</c> — plik → czy dostaje sufit, i DLACZEGO. ⚠ Wartością tej tablicy nie
    /// są nazwy, tylko to, że dopisanie nowego takiego okna zmusza autora do rozstrzygnięcia, po której
    /// stronie granicy stoi (wzorzec <c>DatePresentationTests</c>).
    /// <para>
    /// ⭐ Kryterium jest RZECZYWISTE, nie grupowe: treść musi móc urosnąć PO otwarciu. Zmierzone w M4.4 —
    /// przynależność do „grupy 16" niczego nie dowodzi, bo w większości z nich rozmiar jest ustalony przed
    /// <c>ShowDialog</c> i okno nigdy się nie zmienia.
    /// </para>
    /// </summary>
    private static readonly (string File, bool Ceiling, string Why)[] SizeToContentDialogs =
    {
        ("SettingsImportDialog",      true,  "wybór pliku odsłania listę sekcji — defekt źródłowy, etap 5b"),
        ("SettingsExportDialog",      true,  "grupa hasła pojawia się po wyborze sekcji; dzieli sufit z importem"),
        ("ExecuteProcedureDialog",    true,  "M4.4: własny limit 720 stoi POWYŻEJ obszaru roboczego 1366×768 (696)"),
        ("ExportDialog",              true,  "M4.4: wybór CSV odsłania opcje, baner błędu, podmiana paneli"),
        ("NewConnectionDialog",       true,  "M4.4: komunikat testu połączenia rośnie poza ScrollViewerem"),
        ("RecompileDependentsDialog", false, "M4.4 ZMIERZONE: zero wiązań IsVisible, lista gotowa przed otwarciem"),
        ("AboutWindow",               false, "treść statyczna"),
        ("CheckConstraintDialog",     false, "formularz o stałej liczbie pól"),
        ("ChoiceDialog",              false, "treść i przyciski znane przed otwarciem"),
        ("ConfirmDialog",             false, "treść i przyciski znane przed otwarciem"),
        ("GlobalSearchDialog",        false, "formularz o stałej liczbie pól"),
        ("NewFolderDialog",           false, "jedno pole"),
        ("NewRoleDialog",             false, "jedno pole"),
        ("SubprogramKindDialog",      false, "wybór o stałej liczbie pozycji"),
        ("TextPromptDialog",          false, "jedno pole"),
        ("UserEditDialog",            false, "formularz o stałej liczbie pól"),
    };

    /// <summary>
    /// ⛔⛔ Sufit bez przewijania nie rozwiązuje problemu — PRZYCINA treść. Mówi to o sobie wprost
    /// <see cref="GrowingDialogBehavior"/>: *„the ceiling decides the size, the ScrollViewer decides what
    /// happens to the overflow, and a dialog with the first and not the second would simply be clipped"*.
    /// <para>
    /// ⭐ To nie jest hipotetyczne: w M4.4 <c>ExportDialog</c> był dokładnie w tym stanie i dlatego dostał
    /// najpierw wspólny <c>ScrollViewer</c> wokół obu stanów, a dopiero potem sufit. Ten test pilnuje, żeby
    /// kolejność nie odwróciła się przy następnym dialogu — i żeby nikt nie usunął tego opakowania.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDialogWithTheCeiling_CanScrollItsBody()
    {
        var missing = new List<string>();

        foreach (var (file, ceiling, _) in SizeToContentDialogs)
        {
            if (!ceiling) continue;

            var markup = File.ReadAllText(Path.Combine(ViewsRoot(), file + ".axaml"));
            if (!markup.Contains("<ScrollViewer", StringComparison.Ordinal))
            {
                missing.Add(file);
            }
        }

        Assert.True(missing.Count == 0,
            "Dialog dostaje sufit wysokości, ale nie ma czym przewijać, więc sufit go PRZYTNIE:\n  "
            + string.Join("\n  ", missing)
            + "\n\nNajpierw ScrollViewer wokół ciała okna, dopiero potem GrowingDialogBehavior.Attach.");
    }

    /// <summary>
    /// ⭐⭐ Egzekwuje, że tablica wyżej opisuje RZECZYWISTOŚĆ w obie strony: każde okno z <c>SizeToContent</c>
    /// ma zapisaną decyzję, a każda zapisana decyzja zgadza się z tym, co robi kod.
    /// <para>
    /// ⚠ To jest #340 przełożone na strażnika: w M4.3 dziewiętnaście decyzji „rozstrzygnie brama" żyło
    /// w ŹRÓDLE, podczas gdy rejestr żył w DOKUMENCIE — i zamykany bywał wyłącznie dokument. Tablica, którą
    /// pilnuje test, nie może wypaść między etapami.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySizeToContentDialog_HasARecordedDecisionAboutGrowth()
    {
        var actual = Directory.EnumerateFiles(ViewsRoot(), "*.axaml")
            .Where(f => File.ReadAllText(f).Contains("SizeToContent=", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var recorded = SizeToContentDialogs.Select(d => d.File).ToHashSet(StringComparer.Ordinal);

        var undeclared = actual.Except(recorded).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var stale = recorded.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0,
            "Nowe okno SizeToContent bez zapisanej decyzji o wzroście:\n  " + string.Join("\n  ", undeclared)
            + "\n\nRozstrzygnij POMIAREM, a nie przynależnością do grupy: czy jego treść może urosnąć PO "
            + "otwarciu? Jeżeli tak — ScrollViewer i Attach. Jeżeli nie — wpis z powodem.");

        Assert.True(stale.Count == 0,
            "Wpis opisuje okno, które nie jest już SizeToContent — usuń go:\n  " + string.Join("\n  ", stale));

        // ⚠ Druga połowa: deklaracja musi zgadzać się z kodem. Bez tego wpis „true" przy oknie, które
        // niczego nie wpina, byłby dokumentacją udającą mechanizm.
        foreach (var (file, ceiling, why) in SizeToContentDialogs)
        {
            var codeBehind = Path.Combine(ViewsRoot(), file + ".axaml.cs");
            var attaches = File.Exists(codeBehind)
                && File.ReadAllText(codeBehind).Contains("GrowingDialogBehavior.Attach", StringComparison.Ordinal);

            Assert.True(attaches == ceiling,
                $"{file}: tablica mówi Ceiling={ceiling} („{why}”), a kod {(attaches ? "wpina" : "NIE wpina")} "
                + "GrowingDialogBehavior. Zmień jedno albo drugie — rozjazd oznacza, że jedno z nich kłamie.");
        }
    }

    private static string ViewsRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "EmberTern.App", "Views")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "src", "EmberTern.App", "Views");
    }
}
