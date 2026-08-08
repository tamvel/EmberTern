// Product Polish M3.5 — sonda kandydatów wizualnych. Powód i granice: .csproj.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe
//
// Renderuje DWIE decyzje M3.5, każdą obok stanu obecnego, w obu motywach:
//   Z-6  ikony „Create …" — glif pełnowymiarowy + plus jako badge (dziś glif jest ściśnięty do ~11/24)
//   Z-2  CheckBox w stanie niezaznaczonym — krawędź nad progiem 3:1 (dziś 1,60:1 Dark / 1,35:1 Light)
//
// ⛔ Kandydaci są zdefiniowani TUTAJ, nie w produkcie. Uruchomienie sondy nic nie wdraża.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using EmberTern.App.Controls;

internal static class Program
{
    // ─── Z-6: ikony „Create …" ────────────────────────────────────────────────────────────────────────
    //
    // POMIAR WEJŚCIOWY (§13.3): wszystkie dziewięć geometrii `*Plus` ma identyczny segment plusa
    // `M18.5 15 V22 M15 18.5 H22` — czyli plus zajmuje CAŁY prawy dolny kwadrant siatki 24×24 i NIE
    // nachodzi na glif. Glif został za to ściśnięty do ~11×11 tam, gdzie jego własny odpowiednik bez
    // plusa ma 18×18. To ~62 % rozmiaru liniowego i ~40 % powierzchni, przy IDENTYCZNYM pudełku
    // (`SvgIcon` renderuje w stałym Viewboxie 24×24) — czyli gotcha #288 na opak: ink box zaczął
    // dyktować rozmiar optyczny, zamiast go diagnozować.
    //
    // ⭐ WZÓR: `Icon.FolderPlus` (IconGeometries.axaml:87) to prawdziwe Lucide i JEST pełnowymiarowy —
    //   korpus 2..22, plus `M12 10v6 M9 13h6` WEWNĄTRZ korpusu, jedna barwa, zero knockoutu. Dziesiąta
    //   ikona tej rodziny już robi to, o co chodzi; pozostałe dziewięć to ręczne kompresje.
    //
    // ⚠⚠ DLACZEGO NIE OVERLAY BADGE Z KNOCKOUTEM (i dlaczego poprzednia próba była trudna): knockout
    //   wymaga wypełnienia w barwie tła POD ikoną, a to tło nie jest stałe — w spoczynku
    //   `ChromeStrongBrush`, po najechaniu `IconHoverBrush` (ControlStyles.axaml:693), w stanie
    //   wyłączonym całość przez `Opacity 0.4`. Knockout w barwie chromy pokazałby łatę złego koloru
    //   w momencie wejścia kursora. Do tego jedna `StreamGeometry` ma jeden pędzel i żadnego knockoutu.
    //   ⭐ Rozwiązanie: plus ląduje tam, gdzie glif NIE MA TUSZU — wtedy separacja jest w geometrii,
    //   a nie w drugim kolorze. 8 z 9 glifów ma puste miejsce w prawym dolnym narożniku (wewnątrz albo
    //   poza konturem); tylko `Table` ma zajęte całe wnętrze i wymaga skrócenia jednej kreski.
    //
    // ⚠ Rozstawy zgodne z gotchą #287: rozpiętości ramion plusa są wielokrotnościami 1,5.
    private static readonly (string Kind, string ColorKey, string Current, string Candidate, string Note)[] Icons =
    [
        ("Table", "IconColor_Table",
            "M5 3 H12 A2 2 0 0 1 14 5 V12 A2 2 0 0 1 12 14 H5 A2 2 0 0 1 3 12 V5 A2 2 0 0 1 5 3 Z M3 8.5 H14 M8.5 3 V14 M18.5 15 V22 M15 18.5 H22",
            // Pudełko pełnowymiarowe (3..21). Pionowa kreska siatki skrócona V21 → V15, żeby zwolnić
            // DOLNĄ PRAWĄ komórkę; oba poziomy zostają, więc siatka nadal się czyta. Plus w tej komórce.
            "M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 Z M12 3 V15 M3 9 H21 M3 15 H21 M16.5 15.75 V20.25 M14.25 18 H18.75",
            "jedyny glif z zajętym wnętrzem — skrócona kreska pionowa"),

        ("View", "IconColor_View",
            "M2 9.5 C4.5 4 11.5 4 14 9.5 C11.5 15 4.5 15 2 9.5 Z M5.4 9.5 A2.6 2.6 0 1 0 10.6 9.5 A2.6 2.6 0 1 0 5.4 9.5 Z M18.5 15 V22 M15 18.5 H22",
            // Oko nietknięte (pełne Lucide). Plus w PUSTYM narożniku poza konturem — oko jest płaskie,
            // więc przy x≥17 jego dolna krawędź jest wysoko nad plusem.
            "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9 12 a3 3 0 1 0 6 0 a3 3 0 1 0-6 0 M19.5 17 V22 M17 19.5 H22",
            "glif nietknięty, plus poza konturem"),

        ("Procedure", "IconColor_Procedure",
            "M5 3 H12 A2 2 0 0 1 14 5 V12 A2 2 0 0 1 12 14 H5 A2 2 0 0 1 3 12 V5 A2 2 0 0 1 5 3 Z M6 6.5 l2 2 -2 2 M9.5 10.5 h3 M18.5 15 V22 M15 18.5 H22",
            // Wnętrze pudełka jest prawie puste (chevron x7..9, kreska y13) — plus wchodzi bez kolizji.
            "M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 Z M7 11 l2-2-2-2 M11 13 h4 M17 14.5 V19.5 M14.5 17 H19.5",
            "glif nietknięty, plus w pustym wnętrzu"),

        ("Trigger", "IconColor_Trigger",
            "M9 2 L4 11 H8 L7 16 L13 7 H9 Z M18.5 15 V22 M15 18.5 H22",
            // Błyskawica biegnie po antyprzekątnej, więc prawy dolny narożnik jest poza konturem.
            "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z M19.5 17 V22 M17 19.5 H22",
            "glif nietknięty, plus poza konturem"),

        ("Function", "IconColor_Function",
            "M5 3 H12 A2 2 0 0 1 14 5 V12 A2 2 0 0 1 12 14 H5 A2 2 0 0 1 3 12 V5 A2 2 0 0 1 5 3 Z M10 5.4 c-1.5 0 -2.1 0.9 -2.1 2.3 V11.4 M6.6 8.3 H10.4 M18.5 15 V22 M15 18.5 H22",
            // ƒ zajmuje x9..15; plus przesunięty w prawo (lewa krawędź 15,75) żeby zostawić odstęp.
            "M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 Z M9 17c2 0 2.8-1 2.8-2.8V10c0-2 1-3.3 3.2-3 M9 11.2h5.7 M18 15 V19.5 M15.75 17.25 H20.25",
            "glif nietknięty, plus w pustym wnętrzu"),

        ("Generator", "IconColor_Generator",
            "M2 6 H13 M2 10 H13 M7 2 L6 14 M11 2 L10 14 M18.5 15 V22 M15 18.5 H22",
            // Kreskowanie kończy się na x=20/y=15; prawy dolny narożnik pusty.
            "M4 9 H20 M4 15 H20 M10 3 L8 21 M16 3 L14 21 M19.5 17 V22 M17 19.5 H22",
            "glif nietknięty, plus poza kreskowaniem"),

        ("Domain", "IconColor_Domain",
            "M8 2 L14 8 L8 14 L2 8 Z M18.5 15 V22 M15 18.5 H22",
            // Romb ma całkowicie puste wnętrze — plus wchodzi do środka, w dolną prawą część
            // (końce ramion sumują się do 31,25 przy krawędzi x+y=34, więc z zapasem).
            "M12 2 L22 12 L12 22 L2 12 Z M14 12.75 V17.25 M11.75 15 H16.25",
            "glif nietknięty, plus we wnętrzu rombu"),

        ("Package", "IconColor_Package",
            "M2 5 L8 2 L14 5 L8 8 Z M2 5 V11 L8 14 M14 5 V11 L8 14 M8 8 V14 M18.5 15 V22 M15 18.5 H22",
            // Plus w dolnej prawej ścianie sześcianu; prawa krawędź 17,5 mieści się pod skosem (18,46).
            "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z M12 22V12 M3.29 7 L12 12 L20.71 7 M7.5 4.27 l9 5.15 M15.5 13.5 V17.5 M13.5 15.5 H17.5",
            "glif nietknięty, plus w dolnej ścianie"),

        ("Exception", "IconColor_Exception",
            "M8 2 L14 13 H2 Z M8 6 V9 M8 11 h.01 M18.5 15 V22 M15 18.5 H22",
            // Wykrzyknik stoi na x=12; plus po jego prawej, wewnątrz trójkąta (prawa krawędź 18 < 21).
            "M21.73 18 l-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9 v4 M12 17 h.01 M16 15.5 V19.5 M14 17.5 H18",
            "glif nietknięty, plus we wnętrzu trójkąta"),
    ];

    // ─── Z-2: krawędź niezaznaczonego CheckBoxa ───────────────────────────────────────────────────────
    //
    // POMIAR WEJŚCIOWY (§13.3): krawędź `NormalRectangle` bierze `BorderBrush` (#3F3F46 / #D8DBE0), co
    // daje 1,60:1 w Dark i 1,35:1 w Light — daleko pod progiem 3:1 z §10. Zaznaczony jest jaskrawo
    // niebieski, więc kontrolka krzyczy, gdy jest włączona, i praktycznie nie istnieje, gdy nie jest.
    // To ta sama rodzina co gotcha #308 (pola filtra 2,55:1), a tam ratyfikowana poprawka celowała
    // W PRÓG, nie wyżej.
    //
    // Dwa kandydaci, bo to decyzja odbioru, nie arytmetyki:
    //   „progowy"  — policzony na ~3,1:1 (Dark #6A6A70, Light #90939A): minimum, które spełnia §10
    //   „subtelny" — istniejący `SubtleForegroundBrush` (6,31:1 Dark / 5,71:1 Light): zero nowych barw
    private static readonly (string Label, Color Dark, Color Light)[] CheckBoxCandidates =
    [
        ("obecnie  BorderBrush",        Color.Parse("#3F3F46"), Color.Parse("#D8DBE0")),
        ("progowy  ~3,1:1",            Color.Parse("#6A6A70"), Color.Parse("#90939A")),
        ("subtelny SubtleForeground",  Color.Parse("#9AA0A6"), Color.Parse("#5F6570")),
    ];

    public static void Main(string[] args)
    {
        AppBuilder.Configure<ProbeApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .SetupWithoutStarting();

        var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
        Directory.CreateDirectory(outDir);

        // ⚠ M3.5 jest ZAMKNIĘTE i odebrane, więc jego rendery nie odtwarzają się przy każdym uruchomieniu —
        //   trzeba o nie poprosić jawnie (`-- m35`). Domyślnie sonda renderuje aktualne pytanie: M4 / gęstość.
        if (args.Length > 0 && args[0] == "m35")
        {
            RenderStage35(outDir);
            Console.WriteLine("OK");
            return;
        }

        Density.Run(outDir);
        foreach (var file in Directory.GetFiles(outDir, "m4-*.png").OrderBy(f => f))
        {
            Console.WriteLine(file);
        }

        Console.WriteLine("OK");
    }

    private static void RenderStage35(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;

            var icons = BuildIconTable();
            var iconFile = Path.Combine(outDir, $"z6-create-icons-{variant}.png");
            Render(icons, iconFile, scale: 1.0);
            Console.WriteLine(iconFile);

            var badges = BuildBadgeTable();
            var badgeFile = Path.Combine(outDir, $"z6-badge-variants-{variant}.png");
            Render(badges, badgeFile, scale: 1.0);
            Console.WriteLine(badgeFile);

            var props = BuildProportionTable();
            var propFile = Path.Combine(outDir, $"z6-badge-proportions-{variant}.png");
            Render(props, propFile, scale: 1.0);
            Console.WriteLine(propFile);

            var shipped = BuildShippedRow();
            var shippedFile = Path.Combine(outDir, $"z6-SHIPPED-{variant}.png");
            Render(shipped, shippedFile, scale: 1.0);
            Console.WriteLine(shippedFile);

            var boxes = BuildCheckBoxTable();
            var boxFile = Path.Combine(outDir, $"z2-checkbox-{variant}.png");
            Render(boxes, boxFile, scale: 3.0);
            Console.WriteLine(boxFile);
        }
    }

    // Wiersz na rodzaj: nazwa | obecnie 16px | kandydat 16px | obecnie ×5 | kandydat ×5 | nota.
    // ⭐ Dwa rozmiary naraz, bo to DWA różne pytania: 16 px odpowiada „czy w pasku narzędzi widać glif",
    //   a powiększenie „czy plus jest czytelny i czy geometria nie ma kolizji".
    private static Control BuildIconTable()
    {
        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("110,80,80,140,140,*"),
            Background = Brush.Parse("#00000000"),
        };
        grid.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));

        var headers = new[] { "", "obecnie 16", "kandydat 16", "obecnie ×5", "kandydat ×5", "" };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headers.Length; c++)
        {
            var h = new TextBlock { Text = headers[c], FontSize = 11, Margin = new Thickness(4, 0, 4, 8) };
            h.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            Grid.SetRow(h, 0);
            Grid.SetColumn(h, c);
            grid.Children.Add(h);
        }

        for (var i = 0; i < Icons.Length; i++)
        {
            var (kind, colorKey, current, candidate, note) = Icons[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            grid.Children.Add(Cell(new TextBlock { Text = kind, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, row, 0));
            grid.Children.Add(Cell(Icon(current, colorKey, 16), row, 1));
            grid.Children.Add(Cell(Icon(candidate, colorKey, 16), row, 2));
            grid.Children.Add(Cell(Icon(current, colorKey, 80), row, 3));
            grid.Children.Add(Cell(Icon(candidate, colorKey, 80), row, 4));

            var n = new TextBlock { Text = note, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            n.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            grid.Children.Add(Cell(n, row, 5));
        }

        return grid;
    }

    // ─── Z-6, runda 2: BADGE, a nie drugi glif ────────────────────────────────────────────────────────
    //
    // ⭐⭐ POWÓD TEJ RUNDY — obserwacja użytkownika (2026-08-04) i jej techniczne potwierdzenie:
    //   „przestań traktować plus jako część ikony, zacznij traktować go jak niewielki badge akcji".
    //   `SvgIcon` to JEDEN `Path`, `Stroke = Foreground`, `StrokeThickness = 2` — jeden pędzel, brak
    //   wypełnienia, jedna grubość dla CAŁEJ ścieżki. Badge jest z definicji znakiem MNIEJSZYM
    //   i GĘŚCIEJSZYM, a `SvgIcon` umie tylko „mniejszy i równie grubo" — dlatego runda 1 dała kleks
    //   przy 16 px. To ograniczenie STRUKTURALNE, nie kwestia doboru kształtu.
    //
    // ⭐ Precedens w repozytorium: `DebuggerIcon` — dwa kolory + wypełniona kropka, więc „nie może być
    //   pojedynczą pociągniętą geometrią, stąd dedykowany composite". Tam kropka jest SOLIDNA i po prostu
    //   NACHODZI na trójkąt — bez knockoutu, więc bez zależności od barwy tła (a zatem bez problemu
    //   z `IconHoverBrush`). Ten sam zabieg działa dla badge'a „utwórz".
    //
    // ⭐⭐ I drugi zysk, ważniejszy od wyglądu: `Icon.TablePlus` jest RĘCZNĄ KOPIĄ `Icon.Table`.
    //   Composite bierze geometrię PLAIN przez referencję, więc dziewięć kopii znika, a pasek narzędzi
    //   pokazuje ten sam glif co drzewo — dokładnie ten błąd i dokładnie to lekarstwo, które
    //   `DebuggerIcon` już raz opisał („⛔ nie zamieniać referencji na wpisaną ścieżkę").
    //
    // ⚠ Podglądy poniżej składa SONDA (dwa `Path` w `Canvas`), NIE nowa kontrolka w produkcie —
    //   kontrolka powstaje dopiero po akceptacji wariantu.
    private static readonly (string Kind, string ColorKey, string Plain, string Current)[] BadgeKinds =
    [
        ("Table", "IconColor_Table",
            "M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 Z M12 3 V21 M3 9 H21 M3 15 H21",
            "M5 3 H12 A2 2 0 0 1 14 5 V12 A2 2 0 0 1 12 14 H5 A2 2 0 0 1 3 12 V5 A2 2 0 0 1 5 3 Z M3 8.5 H14 M8.5 3 V14 M18.5 15 V22 M15 18.5 H22"),
        ("View", "IconColor_View",
            "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9 12 a3 3 0 1 0 6 0 a3 3 0 1 0-6 0",
            "M2 9.5 C4.5 4 11.5 4 14 9.5 C11.5 15 4.5 15 2 9.5 Z M5.4 9.5 A2.6 2.6 0 1 0 10.6 9.5 A2.6 2.6 0 1 0 5.4 9.5 Z M18.5 15 V22 M15 18.5 H22"),
        ("Function", "IconColor_Function",
            "M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 Z M9 17c2 0 2.8-1 2.8-2.8V10c0-2 1-3.3 3.2-3 M9 11.2h5.7",
            "M5 3 H12 A2 2 0 0 1 14 5 V12 A2 2 0 0 1 12 14 H5 A2 2 0 0 1 3 12 V5 A2 2 0 0 1 5 3 Z M10 5.4 c-1.5 0 -2.1 0.9 -2.1 2.3 V11.4 M6.6 8.3 H10.4 M18.5 15 V22 M15 18.5 H22"),
        ("Generator", "IconColor_Generator",
            "M4 9 H20 M4 15 H20 M10 3 L8 21 M16 3 L14 21",
            "M2 6 H13 M2 10 H13 M7 2 L6 14 M11 2 L10 14 M18.5 15 V22 M15 18.5 H22"),
    ];

    private enum Badge { None, ChipNeutral, ChipAccent, ThickPlus }

    private static Control BuildBadgeTable()
    {
        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("100,56,56,56,56,110,110,110,110"),
        };
        grid.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));

        var headers = new[] { "", "dziś", "chip\nneutr.", "chip\nakcent", "grubszy\nplus", "dziś ×4", "chip neutr. ×4", "chip akcent ×4", "grubszy plus ×4" };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headers.Length; c++)
        {
            var h = new TextBlock { Text = headers[c], FontSize = 10, Margin = new Thickness(2, 0, 2, 8) };
            h.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            grid.Children.Add(Cell(h, 0, c));
        }

        for (var i = 0; i < BadgeKinds.Length; i++)
        {
            var (kind, colorKey, plain, current) = BadgeKinds[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            grid.Children.Add(Cell(new TextBlock { Text = kind, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, row, 0));

            grid.Children.Add(Cell(Icon(current, colorKey, 16), row, 1));
            grid.Children.Add(Cell(Composed(plain, colorKey, Badge.ChipNeutral, 16), row, 2));
            grid.Children.Add(Cell(Composed(plain, colorKey, Badge.ChipAccent, 16), row, 3));
            grid.Children.Add(Cell(Composed(plain, colorKey, Badge.ThickPlus, 16), row, 4));

            grid.Children.Add(Cell(Icon(current, colorKey, 96), row, 5));
            grid.Children.Add(Cell(Composed(plain, colorKey, Badge.ChipNeutral, 96), row, 6));
            grid.Children.Add(Cell(Composed(plain, colorKey, Badge.ChipAccent, 96), row, 7));
            grid.Children.Add(Cell(Composed(plain, colorKey, Badge.ThickPlus, 96), row, 8));
        }

        return grid;
    }

    /// <summary>
    /// Składa glif PLAIN (pełne 18 j.) z badge'em, tak jak zrobiłaby to kontrolka composite —
    /// `Canvas 24×24` w `Viewbox Uniform`, dokładnie jak ControlTheme `SvgIcon`/`DebuggerIcon`.
    /// </summary>
    // ⭐ Runda 3 — PROPORCJE badge'a (użytkownik oddał tę decyzję pomiarowi po wyborze kierunku).
    //   Pytanie jest jedno: czy minimalne zmniejszenie średnicy albo odsunięcie od krawędzi odsłoni
    //   zauważalnie więcej glifu, nie tracąc czytelności plusa przy 16 px. Testowane na DWÓCH najgęstszych
    //   glifach, bo tam zasłonięcie boli najbardziej: `Table` (siatka) i `Package` (trzy ściany).
    private static readonly (string Label, double Diameter, double Inset)[] Proportions =
    [
        ("Ø11 przy krawędzi", 11.0, 0.0),
        ("Ø10 odsunięty 0,5", 10.0, 0.5),
        ("Ø9,5 odsunięty 1",   9.5, 1.0),
    ];

    private static Control BuildProportionTable()
    {
        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("150,60,60,130,130"),
        };
        grid.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));

        var headers = new[] { "", "Table 16", "Package 16", "Table ×6", "Package ×6" };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headers.Length; c++)
        {
            var h = new TextBlock { Text = headers[c], FontSize = 10, Margin = new Thickness(2, 0, 2, 8) };
            h.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            grid.Children.Add(Cell(h, 0, c));
        }

        var table = BadgeKinds[0];
        var package = ("Package", "IconColor_Package",
            "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z M12 22V12 M3.29 7 L12 12 L20.71 7 M7.5 4.27 l9 5.15");

        for (var i = 0; i < Proportions.Length; i++)
        {
            var (label, d, inset) = Proportions[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var lbl = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            lbl.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            grid.Children.Add(Cell(lbl, row, 0));

            grid.Children.Add(Cell(Composed(table.Plain, table.ColorKey, Badge.ChipAccent, 16, d, inset), row, 1));
            grid.Children.Add(Cell(Composed(package.Item3, package.Item2, Badge.ChipAccent, 16, d, inset), row, 2));
            grid.Children.Add(Cell(Composed(table.Plain, table.ColorKey, Badge.ChipAccent, 110, d, inset), row, 3));
            grid.Children.Add(Cell(Composed(package.Item3, package.Item2, Badge.ChipAccent, 110, d, inset), row, 4));
        }

        return grid;
    }

    private static Control Composed(string plainPath, string colorKey, Badge badge, double size,
                                    double diameter = 11.0, double inset = 0.0)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };

        var glyph = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(plainPath),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension(colorKey));
        canvas.Children.Add(glyph);

        // ⭐ Badge SOLIDNY nachodzi na glif i go zasłania — więc separacja nie wymaga knockoutu, a zatem
        //   nie zależy od barwy tła pod ikoną (`ChromeStrongBrush` w spoczynku, `IconHoverBrush` po
        //   najechaniu). To dokładnie ten sam zabieg, którym kropka breakpointu siedzi na trójkącie.
        if (badge is Badge.ChipNeutral or Badge.ChipAccent)
        {
            var left = 24 - diameter - inset;
            var centre = left + diameter / 2;
            var arm = diameter * 0.26; // ramię plusa proporcjonalne do dysku, nie stała

            var disc = new Avalonia.Controls.Shapes.Ellipse { Width = diameter, Height = diameter };
            Canvas.SetLeft(disc, left);
            Canvas.SetTop(disc, left);
            disc.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new DynamicResourceExtension(
                badge == Badge.ChipNeutral ? "ForegroundBrush" : "AccentBrush"));
            canvas.Children.Add(disc);

            // Plus NA dysku — kontrastuje wyłącznie z dyskiem, nie z tłem strony, więc jest pewny
            // w obu motywach i niezależny od dziewięciu barw rodzajów.
            var plus = new Avalonia.Controls.Shapes.Path
            {
                // ⚠ Konkatenacja, NIE złożony format string — reguła z katalogu gotchy: narzędzie
                //   diagnostyczne nie używa mini-języka parsowanego w czasie wykonania.
                Data = Geometry.Parse(
                    "M" + N(centre) + " " + N(centre - arm) + " V" + N(centre + arm) +
                    " M" + N(centre - arm) + " " + N(centre) + " H" + N(centre + arm)),
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
            };
            plus.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension(
                badge == Badge.ChipNeutral ? "BackgroundBrush" : "OnAccentBrush"));
            canvas.Children.Add(plus);
        }
        else if (badge == Badge.ThickPlus)
        {
            // Wariant bez dysku: plus w barwie rodzaju, ale WŁASNĄ, grubszą kreską (3,5 vs 2) — to
            // właśnie to, czego jedna `StreamGeometry` nie potrafi wyrazić.
            var plus = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M19 15.5 V22.5 M15.5 19 H22.5"),
                StrokeThickness = 3.5,
                StrokeLineCap = PenLineCap.Round,
            };
            plus.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension(colorKey));
            canvas.Children.Add(plus);
        }

        return new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            Margin = new Thickness(4, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private static Control BuildCheckBoxTable()
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
        panel.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        var dark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;

        foreach (var (label, darkColor, lightColor) in CheckBoxCandidates)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // ⚠ Wartość LOKALNA na instancji bije setter `ControlTheme` — więc podmiana krawędzi tutaj
            //   jest wierną zapowiedzią tego, co dałby setter w `ControlThemes.axaml`.
            var unchecked_ = new CheckBox
            {
                Content = "niezaznaczony",
                FontSize = 12,
                BorderBrush = new SolidColorBrush(dark ? darkColor : lightColor),
            };
            var checked_ = new CheckBox { Content = "zaznaczony", FontSize = 12, IsChecked = true };

            var lbl = new TextBlock { Text = label, FontSize = 11, Width = 200, VerticalAlignment = VerticalAlignment.Center };
            lbl.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));

            line.Children.Add(lbl);
            line.Children.Add(unchecked_);
            line.Children.Add(checked_);
            panel.Children.Add(line);
        }

        return panel;
    }

    /// <summary>
    /// ⭐⭐ WERYFIKACJA STANU WYSYŁANEGO — używa PRAWDZIWEJ kontrolki <c>CreateIcon</c> i jej
    /// <c>ControlTheme</c> z `IconGeometries.axaml`, a nie ręcznej kompozycji z podglądów wyżej.
    /// ⚠ To nie jest ta sama asercja: podglądy dowodzą, że wariant DOBRZE WYGLĄDA, a ten render dowodzi,
    /// że tak wygląda TO, CO WESZŁO DO PRODUKTU. Podglądy mogłyby być piękne przy szablonie, który
    /// renderuje coś innego — dokładnie ta pułapka złapała M3.3b (brakujący słownik nie zawodzi, tylko
    /// po cichu usuwa element z obrazu).
    /// </summary>
    private static Control BuildShippedRow()
    {
        var outer = new StackPanel { Margin = new Thickness(16), Spacing = 14 };
        outer.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));

        foreach (var size in new double[] { 16, 88 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = size > 40 ? 10 : 6 };
            foreach (var (kind, colorKey, _, _, _) in Icons)
            {
                // ⭐ Geometria pobrana z ZASOBÓW APLIKACJI po kluczu — dokładnie tak, jak robi to
                //   `MainWindow.axaml` (`{StaticResource Icon.Table}`). Żadnej kopii ścieżki w sondzie:
                //   gdyby produkt zmienił glif, ten render zmieni się razem z nim.
                Application.Current!.TryFindResource("Icon." + kind, out var geometry);
                var icon = new CreateIcon
                {
                    Data = (Geometry?)geometry,
                    Width = size,
                    Height = size,
                };
                icon.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,
                    new DynamicResourceExtension(colorKey));
                row.Children.Add(icon);
            }
            outer.Children.Add(row);
        }

        return outer;
    }

    private static string N(double v) =>
        v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static Control Cell(Control child, int row, int col)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, col);
        return child;
    }

    private static Control Icon(string pathData, string colorKey, double size)
    {
        var icon = new SvgIcon
        {
            Data = Geometry.Parse(pathData),
            Width = size,
            Height = size,
            Margin = new Thickness(4, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, new DynamicResourceExtension(colorKey));
        return icon;
    }

    internal static void Render(Control root, string path, double scale)
    {
        // ⚠ Kontrolka musi wisieć na TopLevelu, inaczej style aplikacji do niej nie dojdą i render pokaże
        //   gołego Fluenta. Okno nie jest pokazywane — wystarczy, że istnieje jako korzeń drzewa.
        // ⚠ ZMIERZONE, nie założone: okno układa swoją treść do WŁASNEGO rozmiaru i nadpisuje ręczne
        //   Measure/Arrange — pierwszy render przyciął 9 wierszy do 5, bo domyślne okno było niższe niż
        //   treść. `SizeToContent` oddaje decyzję o rozmiarze treści, a nie oknu.
        var window = new Window
        {
            Content = root,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
        };
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // ⚠ Podniesione w M4 z 1400×2000: matryce gęstości mają cztery kolumny wariantów obok siebie,
        //   a zbyt ciasny limit MIERZENIA obcina ostatnią kolumnę — render wygląda wtedy poprawnie
        //   i odpowiada na inne pytanie, niż zadano.
        root.Measure(new Size(3000, 2400));
        root.Arrange(new Rect(root.DesiredSize));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var size = root.Bounds.Size;
        var bmp = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bmp.Render(root);
        using var stream = File.Create(path);
        bmp.Save(stream);
        window.Close();
    }
}

internal sealed class ProbeApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;

        // ⚠ Reguła, za którą M3.3b zapłaciło (§19.23.7): sonda musi ładować TE SAME słowniki co
        //   `App.axaml`. Brakujący słownik nie zawodzi — po cichu usuwa element z obrazu.
        foreach (var source in new[]
                 {
                     "avares://EmberTern/Themes/Tokens.axaml",
                     "avares://EmberTern/Themes/Typography.axaml",
                     "avares://EmberTern/Themes/Colors.axaml",
                     "avares://EmberTern/Themes/FluentBridge.axaml",
                     "avares://EmberTern/Themes/IconGeometries.axaml",
                     "avares://EmberTern/Themes/ControlThemes.axaml",
                     "avares://EmberTern/Themes/SearchableComboBox.axaml",
                     "avares://EmberTern/Themes/PickerTemplates.axaml",
                 })
        {
            Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null) { Source = new Uri(source) });
        }

        Styles.Add(new FluentTheme());
        // ⚠ DOPISANE W M4: bez motywu `DataGrid` siatka nie ma szablonu i renderuje się jako NIC — a to
        //   jest dokładnie ta cicha awaria, którą opisuje reguła wyżej. Kolejność jak w `App.axaml`:
        //   Fluent → DataGrid → nasze style, żeby `ControlStyles.axaml` mogło nadpisać oba.
        //   `AvaloniaEdit` świadomie pominięty — żaden render tej sondy nie zawiera edytora tekstu.
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://EmberTern/Themes/ControlStyles.axaml") });
    }
}
