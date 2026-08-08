// Product Polish M4 / D-M4-2 — MATERIAŁ DECYZYJNY dla grupy gęstości.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- density
//
// ⛔ Kandydaci są zdefiniowani TUTAJ, nie w produkcie. Uruchomienie sondy nic nie wdraża.
//
// ⭐⭐ DLACZEGO JEDEN PLIK NA CZTERY PYTANIA: D-M4-2 mówi, że `Size.Icon` i K15 to JEDNA decyzja
//    o gęstości wizualnej, podejmowana raz, na całościowym obrazie. Renderowanie ich osobno byłoby
//    dokładnie tym, czego ta decyzja zabrania (R17: zgodność ≠ spójność). Do tej samej decyzji należą
//    wysokości wierszy siatek definicji (oddane do M4 przez sprint stabilizacyjny i sprint gridów)
//    oraz szerokości list w pasku importu — wszystkie cztery zmieniają, ILE TREŚCI widać na ekranie.
//
// ⚠ GRANICA ODZIEDZICZONA (§19.23.9): SONDA LICZY UKŁAD RAZ, więc odpowiada na „jak to WYGLĄDA",
//    nigdy na „czy to się USTALA". Żadne z tych pytań nie dotyczy zbieżności, więc granica nie boli.
//
// ⚠ WIERNOŚĆ: geometrie ikon pobierane są z ZASOBÓW APLIKACJI po kluczu (`Icon.Table` itd.), a nie
//    kopiowane do sondy — gdy produkt zmieni glif, render zmieni się razem z nim (reguła z §19.36.4).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.Controls;

internal static class Density
{
    // ─── POMIAR WEJŚCIOWY, z kodu (pułapka 3: katalog bywa zamiarem, nie opisem) ───────────────────
    //
    // ROZMIAR IKONY — 355 deklaracji ikon w aplikacji, SIEDEM renderowanych rozmiarów:
    //   191 bez rozmiaru  → 16  (domyślna z `ControlTheme` w IconGeometries.axaml, NIE `Size.Icon`)
    //    75 Width="14"        (= wartość `Size.Icon`, ale literałem)
    //    44 Width="15"        (wiersz drzewa — K15)
    //    16 Width="16"        (redundantne z domyślną)
    //    10 Size.Icon.Sm      · 7 literałem "12" · 5× "13" · 4× "11" · 1× "10"
    //     2 Size.Icon         (pasek zakładek — JEDYNY konsument roli)
    //
    // ⭐ Konsekwencja, którą widać dopiero po rozbiciu na powierzchnie: komentarz `Size.Icon` mówi
    //   „toolbar, zakładka, drzewo, wiersz menu", a ZMIERZONE jest 16 / 14 / 15 / 14. Rola opisuje
    //   jedną z czterech powierzchni, które wymienia.
    private const double ToolbarToday = 16;   // 28 ikon bez rozmiaru + 3 jawnie, pasek tytułu
    private const double TabToday = 14;       // `Size.Icon`
    private const double TreeToday = 15;      // literał, 44×
    private const double MenuToday = 14;      // literał w MenuMarkup.cs:76

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;

            Program.Render(ChromeIconMatrix(), Path.Combine(outDir, $"m4-a-ikona-chromy-{variant}.png"), 1.0);
            Program.Render(TreeRowVariants(), Path.Combine(outDir, $"m4-b-wiersz-drzewa-{variant}.png"), 1.0);
            Program.Render(GridRowVariants(), Path.Combine(outDir, $"m4-c-wysokosc-wiersza-{variant}.png"), 1.0);
            Program.Render(ImportBarVariants(), Path.Combine(outDir, $"m4-d-pasek-importu-{variant}.png"), 1.0);
        }

        // ⭐ Liczby do materiału decyzyjnego: ile miejsca zajmuje pasmo list w każdym wariancie. Pytanie D
        //   jest o SZEROKOŚĆ, więc odpowiedzią jest liczba, a obraz tylko ją ilustruje.
        Console.WriteLine();
        Console.WriteLine("pasmo list importu — zmierzona szerokość:");
        Console.WriteLine($"  dziś (170/170/180) : {Width(ImportBar(170, 170, 180)):0} px");
        Console.WriteLine($"  bez podłogi        : {Width(ImportBar(0, 0, 0)):0} px");
        Console.WriteLine($"  wspólna 140        : {Width(ImportBar(140, 140, 140)):0} px");
        Console.WriteLine();
        // ⚠ Naturalna szerokość KAŻDEJ listy z osobna — bez tego „podłoga 140" byłaby liczbą dobraną na oko,
        //   a nie zmierzoną: trzeba wiedzieć, czy podłoga nie obcina najdłuższej pozycji którejś z list.
        Console.WriteLine("naturalna szerokość pojedynczej listy (MinWidth 0):");
        Console.WriteLine($"  Profile     : {Width(SingleCombo("(no profile)", "Klienci z CSV", "Cennik XLSX")):0} px");
        Console.WriteLine($"  Transaction : {Width(SingleCombo("Manual", "Commit on success", "Batched")):0} px");
        Console.WriteLine($"  Errors      : {Width(SingleCombo("Stop at the first", "Skip the row and continue")):0} px");
        Console.WriteLine();
    }

    private static Control SingleCombo(params string[] items) =>
        new ComboBox { ItemsSource = items, SelectedIndex = 0, MinWidth = 0 };

    // Pomiar bez zapisu obrazu — ta sama ścieżka układu co render (kontrolka musi wisieć na TopLevelu,
    // inaczej style do niej nie dojdą i szerokość będzie szerokością gołego Fluenta).
    private static double Width(Control control)
    {
        var window = new Window { Content = control, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        control.Measure(new Size(3000, 2400));
        var width = control.DesiredSize.Width;
        window.Close();
        return width;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  A — ROZMIAR IKONY CHROMY: cztery powierzchnie × cztery rozmiary
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // Pytanie: czy chroma ma mieć JEDNĄ liczbę, a jeśli tak — którą. Kolumna „dziś" pokazuje stan
    // faktyczny każdej powierzchni z osobna (16 / 14 / 15 / 14), kolejne trzy — tę samą powierzchnię
    // sprowadzoną do jednej liczby.
    private static Control ChromeIconMatrix()
    {
        // ⚠ Kolumny `Auto`, nie stałe: przy stałej szerokości pasek narzędzi obcinał się na ostatnim
        //   przycisku, a obcięty render odpowiada na inne pytanie niż zadane (pierwsze podejście, 190 px).
        var grid = NewGrid("Auto,Auto,Auto,Auto,Auto");
        Header(grid, "", "dziś (16/14/15/14)", "wszędzie 14  (Size.Icon)", "wszędzie 15", "wszędzie 16  (dzisiejsza domyślna)");

        AddRow(grid, 1, "pasek narzędzi", s => Toolbar(s), ToolbarToday);
        AddRow(grid, 2, "wiersz zakładki", s => TabRow(s), TabToday);
        AddRow(grid, 3, "wiersz drzewa", s => TreeMini(s), TreeToday);
        AddRow(grid, 4, "wiersz menu", s => MenuMini(s), MenuToday);

        return grid;

        static void AddRow(Grid g, int row, string label, Func<double, Control> build, double today)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            g.Children.Add(At(Label(label, 12), row, 0));
            var i = 1;
            foreach (var size in new[] { today, 14.0, 15.0, 16.0 })
            {
                var host = build(size);
                host.Margin = new Thickness(4, 6);
                g.Children.Add(At(host, row, i++));
            }
        }
    }

    // Pasek narzędzi: cztery przyciski `Button.icon` z prawdziwym stylem produktu, na chromie.
    private static Control Toolbar(double iconSize)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        foreach (var key in new[] { "Icon.Play", "Icon.Hammer", "Icon.Save", "Icon.RefreshCw", "Icon.Search" })
        {
            var button = new Button { Classes = { "icon" }, Content = Glyph(key, iconSize, null) };
            strip.Children.Add(button);
        }

        // ⭐ Ikony „utwórz" stoją w tym samym pasku i dziś renderują się 16 (brak deklaracji rozmiaru),
        //   czyli o 2 px większe niż sąsiedzi z paska EDYTORA. To jest druga połowa pytania A.
        strip.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(4, 6),
            [!Border.BackgroundProperty] = Res("BorderBrush"),
        });
        foreach (var (geometry, colour) in new[] { ("Icon.Table", "IconColor_Table"), ("Icon.View", "IconColor_View") })
        {
            Application.Current!.TryFindResource(geometry, out var data);
            var icon = new CreateIcon { Data = (Geometry?)data, Width = iconSize, Height = iconSize };
            icon.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, Res(colour));
            strip.Children.Add(new Button { Classes = { "icon" }, Content = icon });
        }

        return Chrome(strip, "ChromeStrongBrush");
    }

    private static Control TabRow(double iconSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 0) };
        row.Children.Add(Glyph("Icon.Query", iconSize, "IconColor_Query"));
        row.Children.Add(Text("SELECT_PRACOWNIKOW", 11));
        row.Children.Add(Glyph("Icon.X", 12, "SubtleForegroundBrush"));

        var tab = new Border
        {
            Height = 26,
            Child = new Panel { Children = { row } },
            [!Border.BackgroundProperty] = Res("SurfaceRaisedBrush"),
        };
        row.VerticalAlignment = VerticalAlignment.Center;
        return Chrome(new StackPanel { Orientation = Orientation.Horizontal, Children = { tab } }, "PanelBrush");
    }

    private static Control TreeMini(double iconSize) => Chrome(TreeRows(iconSize, gap: 5, rows: 3), "PanelBrush");

    private static Control MenuMini(double iconSize)
    {
        var panel = new StackPanel();
        foreach (var (key, label, gesture) in new[]
                 {
                     ("Icon.Pencil", "Edit object", "F2"),
                     ("Icon.Hammer", "Compile", "F7"),
                     ("Icon.Trash", "Delete object", "F8"),
                 })
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto"), Height = 22 };
            var glyph = Glyph(key, iconSize, key == "Icon.Trash" ? "DangerIconBrush" : null);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            line.Children.Add(At(glyph, 0, 0));
            line.Children.Add(At(Text(label, 12), 0, 1));
            var g = Text(gesture, 12);
            g.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
            g.Margin = new Thickness(16, 0, 8, 0);
            line.Children.Add(At(g, 0, 2));
            panel.Children.Add(line);
        }

        return Chrome(panel, "SurfaceRaisedBrush");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  B — WIERSZ DRZEWA: ikona + odstęp (kolizja K15)
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // ⚠ Zmierzone: para 15 px + `Spacing` 5 to NIE jest „112 rozproszonych literałów". To JEDNA ROLA —
    //   wiersz drzewa — w 13 plikach: trzy szablony Metadata Explorera plus każdy szablon wiersza drzewa
    //   „Zależności" w edytorach obiektów (te same 18 drzew, które w M4.2b idą na wspólny `TreeListView`).
    //   Zmiana jest więc SPÓJNA, nie rozpraszająca; 39 z 69 `Spacing="5"` stoi dokładnie przy tej ikonie.
    //
    // ⚠ Wysokość wiersza NIE jest tu przedmiotem: `Size.Row.Tree` = 24 jest `MinHeight`, a ikona 15
    //   mieści się w niej tak samo jak 14. Pytanie jest wyłącznie o GĘSTOŚĆ POZIOMĄ i o wagę optyczną.
    private static Control TreeRowVariants()
    {
        var grid = NewGrid("Auto,Auto,Auto,Auto");
        Header(grid, "", "dziś:  ikona 15 · odstęp 5", "role:  Size.Icon 14 · Space.Xs 4", "pośrednio:  14 · Space.Sm 6");

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("skala 1:1  (9 wierszy)", 11), 1, 0));
        grid.Children.Add(At(Chrome(TreeRows(15, 5, 9), "PanelBrush"), 1, 1));
        grid.Children.Add(At(Chrome(TreeRows(14, 4, 9), "PanelBrush"), 1, 2));
        grid.Children.Add(At(Chrome(TreeRows(14, 6, 9), "PanelBrush"), 1, 3));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("powiększenie ×3  (kategoria + liść)", 11), 2, 0));
        grid.Children.Add(At(Zoom(TreeRows(15, 5, 2), 3), 2, 1));
        grid.Children.Add(At(Zoom(TreeRows(14, 4, 2), 3), 2, 2));
        grid.Children.Add(At(Zoom(TreeRows(14, 6, 2), 3), 2, 3));

        return grid;
    }

    // Wierny odpowiednik szablonu z MainWindow.axaml: wcięcie | pole chevronu 20 px | ikona + etykieta.
    private static Control TreeRows(double iconSize, double gap, int rows)
    {
        var list = new StackPanel();
        var content = new (string Icon, string Colour, string Label, int Depth, bool Expandable)[]
        {
            ("Icon.Table", "IconColor_Table", "Tables (2218)", 0, true),
            ("Icon.Table", "IconColor_Table", "KLIENCI", 1, false),
            ("Icon.Table", "IconColor_Table", "ZAMOWIENIA_NAGLOWEK", 1, false),
            ("Icon.View", "IconColor_View", "Views (184)", 0, true),
            ("Icon.Procedure", "IconColor_Procedure", "Procedures (1075)", 0, true),
            ("Icon.Procedure", "IconColor_Procedure", "SP_DODAJ_POZYCJE", 1, false),
            ("Icon.Trigger", "IconColor_Trigger", "Triggers (612)", 0, true),
            ("Icon.Function", "IconColor_Function", "Functions (93)", 0, true),
            ("Icon.Generator", "IconColor_Generator", "Generators (77)", 0, true),
        };

        foreach (var (icon, colour, label, depth, expandable) in content.Take(rows))
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,20,*"),
                MinHeight = 24,          // `Size.Row.Tree`
                Margin = new Thickness(2, 0),
            };

            row.Children.Add(At(new Border { Width = depth * 14 }, 0, 0));

            // Pole trafienia chevronu 20×20 z glifem `Size.Icon.Sm` (12) — stan faktyczny produktu.
            var chevron = new Panel { Width = 20, VerticalAlignment = VerticalAlignment.Center };
            if (expandable) chevron.Children.Add(Glyph("Icon.ChevronDown", 12, "SubtleForegroundBrush"));
            row.Children.Add(At(chevron, 0, 1));

            var body = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = gap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            body.Children.Add(Glyph(icon, iconSize, colour));
            body.Children.Add(Text(label, 11));
            row.Children.Add(At(body, 0, 2));

            list.Children.Add(row);
        }

        return list;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  C — WYSOKOŚĆ WIERSZA SIATKI DEFINICJI
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // ⚠⚠ ZMIERZONE, i to jest najważniejsza liczba tego pytania: WSZYSTKIE siatki definicji mają
    //    `DataGridCell` `Padding="6 2"` (pion 2+2 = 4). Edytor w komórce ma `Size.Control` = 24.
    //    Minimalna wysokość wiersza, w której edytor się mieści, wynosi więc 4 + 24 = 28 — a produkt
    //    używa 30, 32 i 34. ⭐ Trzy liczby na jedno wymaganie; żadna z nich nie wynika z pomiaru.
    //
    //    stan faktyczny:  34  Pola (TableDetail #FieldsGrid) i Nowa tabela
    //                     32  Dane (TableDetail .data-edit)
    //                     30  parametry/zmienne/kursory (Procedure · Function · Trigger)
    //                     28  uprawnienia (Security Manager)   ·  34  Security .checkbox-grid
    //                     22  indeksy/ograniczenia (TableDetail) i kolumny (View)  ← siatki BEZ edytora
    //
    // ⚠ Wiersz 22 nie należy do tego pytania: te siatki są tylko do odczytu, więc podłoga
    //   `Size.Row.Grid` (22) jest dla nich poprawna. Pytanie dotyczy wyłącznie siatek EDYTOWALNYCH.
    private static Control GridRowVariants()
    {
        var grid = NewGrid("Auto,Auto,Auto,Auto,Auto");
        Header(grid, "", "34  (dziś: Pola)", "32  (dziś: Dane)", "30  (dziś: parametry)", "28  (minimum arytm.)");

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("ta sama wysokość okna 200 px", 11), 1, 0));
        var i = 1;
        foreach (var height in new[] { 34.0, 32.0, 30.0, 28.0 })
        {
            grid.Children.Add(At(FieldsGrid(height), 1, i++));
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("edytor w komórce (ComboBox 24)", 11), 2, 0));
        i = 1;
        foreach (var height in new[] { 34.0, 32.0, 30.0, 28.0 })
        {
            grid.Children.Add(At(EditorInCell(height), 2, i++));
        }

        return grid;
    }

    private sealed class FieldRow
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public string Nullability { get; init; } = "";
    }

    private static Control FieldsGrid(double rowHeight)
    {
        var data = new List<FieldRow>
        {
            new() { Name = "ID_KLIENTA", Type = "INTEGER", Nullability = "NOT NULL" },
            new() { Name = "NAZWA", Type = "VARCHAR(120)", Nullability = "NOT NULL" },
            new() { Name = "NIP", Type = "D_NIP", Nullability = "" },
            new() { Name = "DATA_ZALOZENIA", Type = "TIMESTAMP", Nullability = "NOT NULL" },
            new() { Name = "LIMIT_KREDYTU", Type = "NUMERIC(15,2)", Nullability = "" },
            new() { Name = "ID_HANDLOWCA", Type = "INTEGER", Nullability = "" },
            new() { Name = "UWAGI", Type = "BLOB SUB_TYPE TEXT", Nullability = "" },
            new() { Name = "AKTYWNY", Type = "D_TAK_NIE", Nullability = "NOT NULL" },
        };

        var dg = new DataGrid
        {
            ItemsSource = data,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Height = 200,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        };
        // ⚠ Kolumny celowo wąskie: okno sondy ma `SizeToContent`, a system przycina okno do szerokości
        //   EKRANU — pierwsza wersja tej matrycy (4 × 340 px) obcięła ostatni wariant i wyglądała poprawnie.
        dg.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(110) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Avalonia.Data.Binding("Type"), Width = new DataGridLength(115) });
        dg.Columns.Add(new DataGridTextColumn { Header = "Null", Binding = new Avalonia.Data.Binding("Nullability"), Width = new DataGridLength(55) });

        // Dokładnie te dwa settery, którymi produkt ustawia gęstość siatki definicji.
        dg.Styles.Add(new Style(x => x.Is<DataGridRow>())
        {
            Setters = { new Setter(Layoutable.HeightProperty, rowHeight) },
        });
        dg.Styles.Add(new Style(x => x.Is<DataGridCell>())
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(6, 2)) },
        });

        return new Border { Child = dg, Margin = new Thickness(4, 6, 12, 6) };
    }

    // Czy edytor 24 px mieści się w wierszu — pokazane, a nie tylko policzone.
    private static Control EditorInCell(double rowHeight)
    {
        var cell = new Border
        {
            Height = rowHeight,
            Padding = new Thickness(6, 2),
            [!Border.BorderBrushProperty] = Res("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 1),
        };
        var combo = new ComboBox
        {
            ItemsSource = new[] { "D_NIP", "VARCHAR(120)", "INTEGER" },
            SelectedIndex = 0,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 130,
        };
        cell.Child = combo;

        var caption = Label($"wiersz {rowHeight:0} − padding 4 = {rowHeight - 4:0} px na edytor 24", 11);
        return new StackPanel { Margin = new Thickness(4, 6), Spacing = 4, Children = { cell, caption } };
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  D — PASEK KOMEND IMPORTU: trzy podłogi szerokości
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // ⚠ Zmierzone: `MinWidth` 170 (Profile) · 170 (Transaction) · 180 (Errors) = 520 px podłogi w pasmie,
    //   które jest `DockPanel` z `LastChildFill` — a ostatnim dzieckiem jest poziomy `StackPanel`
    //   przycisków, który się NIE ściska, tylko OBCINA (§19.33). Każdy piksel podłogi to piksel zabrany
    //   przyciskom. Pytanie: czy te podłogi są potrzebne, skoro treść list jest krótsza.
    private static Control ImportBarVariants()
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Bind(Panel.BackgroundProperty, Res("PanelBrush"));

        panel.Children.Add(Label("dziś — MinWidth 170 / 170 / 180", 12));
        panel.Children.Add(ImportBar(170, 170, 180));

        panel.Children.Add(Label("bez podłogi — szerokość z treści", 12));
        panel.Children.Add(ImportBar(0, 0, 0));

        panel.Children.Add(Label("podłoga wspólna 140", 12));
        panel.Children.Add(ImportBar(140, 140, 140));

        return panel;
    }

    private static Control ImportBar(double profile, double transaction, double errors)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        Add("Profile", profile, new[] { "(no profile)", "Klienci z CSV", "Cennik XLSX" });
        Add("Transaction", transaction, new[] { "Manual", "Commit on success", "Batched" });
        Add("Errors", errors, new[] { "Stop at the first", "Skip the row and continue" });

        return Chrome(bar, "ChromeStrongBrush");

        void Add(string label, double minWidth, string[] items)
        {
            var caption = Text(label, 11);
            caption.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
            caption.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(caption);
            bar.Children.Add(new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = 0,
                MinWidth = minWidth,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  Pomocnicze
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    private static Grid NewGrid(string columns)
    {
        var grid = new Grid { Margin = new Thickness(16), ColumnDefinitions = new ColumnDefinitions(columns) };
        grid.Bind(Panel.BackgroundProperty, Res("BackgroundBrush"));
        return grid;
    }

    private static void Header(Grid grid, params string[] headers)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headers.Length; c++)
        {
            var h = Label(headers[c], 11);
            h.Margin = new Thickness(4, 0, 4, 10);
            h.TextWrapping = TextWrapping.Wrap;
            grid.Children.Add(At(h, 0, c));
        }
    }

    private static TextBlock Label(string text, double size)
    {
        var t = Text(text, size);
        t.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    private static TextBlock Text(string text, double size) =>
        new() { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };

    private static SvgIcon Glyph(string geometryKey, double size, string? colourKey)
    {
        Application.Current!.TryFindResource(geometryKey, out var data);
        var icon = new SvgIcon
        {
            Data = (Geometry?)data,
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (colourKey is not null)
        {
            icon.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, Res(colourKey));
        }

        return icon;
    }

    private static Control Chrome(Control child, string backgroundKey)
    {
        var border = new Border { Child = child, Padding = new Thickness(4, 2) };
        border.Bind(Border.BackgroundProperty, Res(backgroundKey));
        return border;
    }

    // ⚠ `LayoutTransformControl`, nie `Viewbox`: skalowanie UKŁADU powiększa geometrię wektorowo, więc
    //   różnica 15 vs 14 zostaje wierna. `Viewbox` dopasowałby treść do zadanej szerokości, czyli
    //   ZNIÓSŁBY dokładnie tę różnicę, którą render ma pokazać.
    private static Control Zoom(Control child, double factor) =>
        new LayoutTransformControl
        {
            Child = child,
            LayoutTransform = new ScaleTransform(factor, factor),
            Margin = new Thickness(4, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    private static Control At(Control child, int row, int col)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, col);
        return child;
    }

    private static DynamicResourceExtension Res(string key) => new(key);
}
