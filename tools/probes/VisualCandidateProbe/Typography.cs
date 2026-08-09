// Product Polish M4 — MATERIAŁ DECYZYJNY dla bloku typografii (rejestr K1–K11 + K2).
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- typo
//
// ⛔ Kandydaci są zdefiniowani TUTAJ, nie w produkcie. Uruchomienie sondy nic nie wdraża.
//
// ⭐⭐ DLACZEGO CAŁY BLOK NARAZ: §18.R sam to zauważył jeszcze w M2c — „wzorzec K1/K2/K3/K6 to jedno
//    pytanie zadane cztery razy: ile mierzy pasek narzędzi i ile mierzy nagłówek sekcji". Rozstrzyganie
//    tych pozycji po jednej znaczyłoby zmienić typografię kilka razy, ani razu nie oglądając jej jako
//    całości (R17) — dokładnie ten sam błąd, przed którym D‑M4‑2 uchronił blok gęstości.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.Controls;

internal static class Typography
{
    // ─── POMIAR WEJŚCIOWY, z kodu ─────────────────────────────────────────────────────────────────
    //
    // ⭐⭐ ZNALEZISKO, KTÓRE ODWRACA PYTANIE O NAGŁÓWEK SEKCJI: kanoniczny `TextBlock.group-header`
    //    niesie `Text.SectionHeader` = 11 SemiBold i w KAŻDYM z 19 użyć stoi bezpośrednio NAD
    //    `TextBlock.field-label`, który niesie `Text.Application` = **12** Regular. Czyli nagłówek jest
    //    o stopień MNIEJSZY od tekstu, który nazywa, a „mocniejszy" jest wyłącznie wagą — choć komentarz
    //    roli twierdzi wprost, że jest mocniejszy, bo „nazywa temat, a nie wartość".
    //
    // ⭐ I dlatego pięć widoków niezależnie od siebie odmówiło tej roli i zostało przy 12 SemiBold
    //    (K3 ×3, K6 ×2, K8 — dwa style po 6 użyć). To nie jest pięć przeoczeń, tylko pięciu autorów
    //    rozstrzygających TĘ SAMĄ sytuację tak samo. Populacje są niemal równe: 19 nagłówków przy 11
    //    i 17 przy 12, w IDENTYCZNYM kontekście.
    private const double SectionHeaderRole = 11;   // Text.SectionHeader.Size
    private const double FieldLabelRole = 12;      // Text.Application.Size (TextBlock.field-label)

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;

            Program.Render(SectionHeaders(), Path.Combine(outDir, $"m4t-a-naglowek-sekcji-{variant}.png"), 1.0);
            Program.Render(ToolbarText(), Path.Combine(outDir, $"m4t-b-pasek-narzedzi-{variant}.png"), 1.0);
            Program.Render(Thirteen(), Path.Combine(outDir, $"m4t-c-trzynastka-{variant}.png"), 1.0);
            Program.Render(SmallMetrics(), Path.Combine(outDir, $"m4t-d-metryki-{variant}.png"), 1.0);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  T‑A — NAGŁÓWEK SEKCJI (K3 · K6 · K8) i jego relacja do podpisu pola
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control SectionHeaders()
    {
        var grid = NewGrid("Auto,Auto,Auto,Auto");
        Header(grid,
            "",
            "dziś A — rola\n11 SemiBold nad 12",
            "dziś B — pięć widoków\n12 SemiBold nad 12",
            "kandydat\n12 SemiBold + podpis 11");

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("grupa ustawień\n(nagłówek + podpis + wartość)", 11), 1, 0));
        grid.Children.Add(At(Group(SectionHeaderRole, FieldLabelRole), 1, 1));
        grid.Children.Add(At(Group(12, FieldLabelRole), 1, 2));
        grid.Children.Add(At(Group(12, 11), 1, 3));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("panel szczegółów\n(nagłówek + wiersze danych)", 11), 2, 0));
        grid.Children.Add(At(DetailPanel(SectionHeaderRole), 2, 1));
        grid.Children.Add(At(DetailPanel(12), 2, 2));
        grid.Children.Add(At(DetailPanel(12), 2, 3));

        return grid;
    }

    // Grupa jak w Settings Center: nagłówek grupy, pod nim podpis pola, pod nim kontrolka.
    private static Control Group(double headerSize, double labelSize)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 6), Width = 260 };

        panel.Children.Add(new TextBlock
        {
            Text = "Formatowanie SQL",
            FontSize = headerSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Wielkość liter słów kluczowych",
            FontSize = labelSize,
            Margin = new Thickness(0, 0, 0, 2),
        });
        var box = new ComboBox { ItemsSource = new[] { "małe litery", "WIELKIE LITERY" }, SelectedIndex = 0, MinWidth = 180 };
        panel.Children.Add(box);

        panel.Children.Add(new TextBlock
        {
            Text = "Zakładki",
            FontSize = headerSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 2),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Maksymalna liczba wierszy",
            FontSize = labelSize,
        });

        return Surface(panel, "PanelBrush");
    }

    // Panel szczegółów jak w Session Managerze / Trace: nagłówek sekcji nad wierszami klucz–wartość.
    private static Control DetailPanel(double headerSize)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 6), Width = 260 };
        panel.Children.Add(new TextBlock
        {
            Text = "Transakcja",
            FontSize = headerSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var (key, value) in new[]
                 {
                     ("Izolacja", "Read Committed"),
                     ("Czas trwania", "00:04:17"),
                     ("Zapytania", "1 284"),
                 })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), Margin = new Thickness(0, 0, 0, 2) };
            row.Children.Add(At(Subtle(key, FieldLabelRole), 0, 0));
            row.Children.Add(At(new TextBlock { Text = value, FontSize = FieldLabelRole }, 0, 1));
            panel.Children.Add(row);
        }

        return Surface(panel, "PanelBrush");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  T‑B — TEKST PASKA NARZĘDZI (K1 · K2)
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // ⚠ Stan zmierzony: `Text.Toolbar` (12) ma **ZERO konsumentów**. Pasek poleceń debuggera stoi na
    //   `Text.Compact` (11, 12 przycisków), pasmo Data Import na `Text.Application` (12), pasek edytora
    //   SQL i Script Executor na `Text.Compact` (11). Czyli rola, która powstała po to, żeby ujednolicić
    //   paski narzędzi, nie opisuje żadnego z nich — a dwa paski mają dwie różne odpowiedzi.
    private static Control ToolbarText()
    {
        var grid = NewGrid("Auto,Auto,Auto");
        Header(grid, "", "11  (dziś: debugger, edytor SQL, Script Executor)", "12  (dziś: Data Import; wartość Text.Toolbar)");

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("pasek poleceń debuggera", 11), 1, 0));
        grid.Children.Add(At(DebuggerBar(11), 1, 1));
        grid.Children.Add(At(DebuggerBar(12), 1, 2));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("pasmo poleceń importu", 11), 2, 0));
        grid.Children.Add(At(ImportBand(11), 2, 1));
        grid.Children.Add(At(ImportBand(12), 2, 2));

        return grid;
    }

    private static Control DebuggerBar(double size)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 6) };
        foreach (var (icon, label) in new[]
                 {
                     ("Icon.Play", "Continue"), ("Icon.StepInto", "Step Into"), ("Icon.StepOver", "Step Over"),
                     ("Icon.StepOut", "Step Out"), ("Icon.RunToSuspend", "SUSPEND"), ("Icon.Stop", "Stop"),
                 })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            content.Children.Add(Glyph(icon, 16));
            content.Children.Add(new TextBlock { Text = label, FontSize = size, VerticalAlignment = VerticalAlignment.Center });
            bar.Children.Add(new Button { Classes = { "flat" }, Content = content, FontSize = size });
        }

        return Surface(bar, "ChromeStrongBrush");
    }

    private static Control ImportBand(double size)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 6), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(Subtle("Profile", size));
        bar.Children.Add(new ComboBox { ItemsSource = new[] { "(no profile)" }, SelectedIndex = 0, MinWidth = 140, FontSize = size });
        bar.Children.Add(Subtle("Transaction", size));
        bar.Children.Add(new ComboBox { ItemsSource = new[] { "Manual" }, SelectedIndex = 0, FontSize = size });
        bar.Children.Add(new Button { Classes = { "primary" }, Content = "Import", FontSize = size });

        return Surface(bar, "ChromeStrongBrush");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  T‑C — 13 px BEZ ROLI (K4 · K9)
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // ⚠⚠ K9 WSKAZUJE W REJESTRZE ZŁY ELEMENT — zmierzone: `TabItem.bottom-tab` i `.sub-tab` przeszły
    //    już na `Text.Compact` (11). Trzynastka została na BAZOWYM stylu `TabItem`, który obsługuje
    //    dokładnie **10 zakładek**: osiem w `AddFieldDialog` i dwie w `NewTableTabView`. To jest trzecia
    //    populacja „zakładek" w produkcie — po pasku dokumentów i po dolnym panelu (§18.R sam ostrzegał,
    //    że rejestr indeksuje po NAZWIE, a „zakładka" jest nośnikiem różnych rzeczy).
    private static Control Thirteen()
    {
        var grid = NewGrid("Auto,Auto,Auto,Auto");
        Header(grid, "", "dziś 13", "12  (Text.Application)", "11  (Text.Compact — jak pozostałe zakładki)");

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("zakładka dialogu\n(AddFieldDialog, NewTable)", 11), 1, 0));
        grid.Children.Add(At(DialogTabs(13), 1, 1));
        grid.Children.Add(At(DialogTabs(12), 1, 2));
        grid.Children.Add(At(DialogTabs(11), 1, 3));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("wiodąca linia planu\n(PlanLead, Performance)", 11), 2, 0));
        grid.Children.Add(At(PlanLead(13), 2, 1));
        grid.Children.Add(At(PlanLead(12), 2, 2));
        grid.Children.Add(At(PlanLead(11), 2, 3));

        return grid;
    }

    private static Control DialogTabs(double size)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(4, 6) };
        var i = 0;
        foreach (var label in new[] { "Domain", "Basic type", "Default", "Check" })
        {
            var selected = i++ == 0;
            var border = new Border
            {
                Padding = new Thickness(10, 4),
                Child = new TextBlock { Text = label, FontSize = size, VerticalAlignment = VerticalAlignment.Center },
            };
            if (selected) border.Bind(Border.BackgroundProperty, Res("SurfaceRaisedBrush"));
            strip.Children.Add(border);
        }

        return Surface(strip, "PanelBrush");
    }

    private static Control PlanLead(double size)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 6), Width = 300, Spacing = 3 };
        panel.Children.Add(new TextBlock
        {
            Text = "PLAN (JOIN (KLIENCI NATURAL) (ZAMOWIENIA INDEX (IDX_ZAM_KLIENT)))",
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(Subtle("Odczyty: 2 218 · zapisy: 0 · czas 41 ms", FieldLabelRole));
        return Surface(panel, "PanelBrush");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  T‑D — DROBNE METRYKI (K7 · K10 · K11)
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control SmallMetrics()
    {
        // ⚠⚠ KOLUMNY ×3 SĄ TU CZĘŚCIĄ PYTANIA, NIE OZDOBĄ. Przy 1:1 wszystkie trzy pary wyglądają
        //    identycznie, a to jest twierdzenie, które trzeba UDOWODNIĆ, a nie założyć: „nie widać różnicy"
        //    i „render nie pokazuje różnicy" wyglądają tak samo. Powiększenie rozstrzyga, czy różnica
        //    istnieje i jest tylko podprogowa, czy nie istnieje wcale.
        var grid = NewGrid("Auto,Auto,Auto,Auto,Auto");
        Header(grid, "", "dziś", "rola", "dziś ×3", "rola ×3");

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("K7 · nagłówek Expandera\n26 vs Size.Control 24", 11), 1, 0));
        grid.Children.Add(At(ExpanderHeader(26), 1, 1));
        grid.Children.Add(At(ExpanderHeader(24), 1, 2));
        grid.Children.Add(At(Zoom(ExpanderHeader(26, narrow: true), 3), 1, 3));
        grid.Children.Add(At(Zoom(ExpanderHeader(24, narrow: true), 3), 1, 4));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("K10 · kształt zakładki\npromień 4 vs Radius.Surface 3", 11), 2, 0));
        grid.Children.Add(At(BottomTab(4), 2, 1));
        grid.Children.Add(At(BottomTab(3), 2, 2));
        grid.Children.Add(At(Zoom(BottomTab(4, single: true), 3), 2, 3));
        grid.Children.Add(At(Zoom(BottomTab(3, single: true), 3), 2, 4));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("K11 · chip transakcji\nodstęp 5 vs Space.Sm 6", 11), 3, 0));
        grid.Children.Add(At(TransactionChip(5), 3, 1));
        grid.Children.Add(At(TransactionChip(6), 3, 2));
        grid.Children.Add(At(Zoom(TransactionChip(5, short_: true), 3), 3, 3));
        grid.Children.Add(At(Zoom(TransactionChip(6, short_: true), 3), 3, 4));

        return grid;
    }

    private static Control ExpanderHeader(double minHeight, bool narrow = false)
    {
        var expander = new Expander
        {
            Header = new TextBlock { Text = narrow ? "Metryki" : "Wykonanie — metryki", FontSize = FieldLabelRole },
            MinHeight = minHeight,
            Padding = new Thickness(0),
            IsExpanded = !narrow,
            Width = narrow ? 120 : 280,
            Content = new TextBlock { Text = "2 218 odczytów · 41 ms", FontSize = FieldLabelRole, Margin = new Thickness(8, 4) },
        };
        return new Border { Child = expander, Margin = new Thickness(4, 6) };
    }

    private static Control BottomTab(double radius, bool single = false)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4, 6) };
        var i = 0;
        foreach (var label in single ? new[] { "Results" } : new[] { "Results", "Messages", "Diagnostics" })
        {
            var selected = i++ == 0;
            var tab = new Border
            {
                CornerRadius = new CornerRadius(radius, radius, 0, 0),
                Padding = new Thickness(8, 4),
                Child = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
            };
            tab.Bind(Border.BackgroundProperty, Res(selected ? "SurfaceRaisedBrush" : "PanelBrush"));
            strip.Children.Add(tab);
        }

        return Surface(strip, "ChromeStrongBrush");
    }

    private static Control TransactionChip(double gap, bool short_ = false)
    {
        var chip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = gap,
            Margin = new Thickness(8, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        dot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, Res("TransactionActiveBrush"));
        chip.Children.Add(dot);
        chip.Children.Add(new TextBlock
        {
            Text = short_ ? "Transakcja" : "Transakcja · 00:04:17",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return Surface(chip, "ChromeStrongBrush");
    }

    // ⚠ `LayoutTransformControl`, nie `Viewbox`: skalowanie UKŁADU powiększa wektorowo, więc różnica
    //   1 px zostaje wierna. `Viewbox` dopasowałby treść do zadanej szerokości i ZNIÓSŁ dokładnie tę
    //   różnicę, o którą pytamy (ta sama pułapka co w bloku gęstości).
    private static Control Zoom(Control child, double factor) =>
        new LayoutTransformControl
        {
            Child = child,
            LayoutTransform = new ScaleTransform(factor, factor),
            Margin = new Thickness(6, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

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
            h.Margin = new Thickness(6, 0, 6, 10);
            h.VerticalAlignment = VerticalAlignment.Bottom;
            grid.Children.Add(At(h, 0, c));
        }
    }

    private static TextBlock Label(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        return t;
    }

    private static TextBlock Subtle(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        return t;
    }

    private static SvgIcon Glyph(string key, double size)
    {
        Application.Current!.TryFindResource(key, out var data);
        return new SvgIcon
        {
            Data = (Geometry?)data,
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static Control Surface(Control child, string backgroundKey)
    {
        var border = new Border { Child = child, Margin = new Thickness(4, 6) };
        border.Bind(Border.BackgroundProperty, Res(backgroundKey));
        return border;
    }

    private static Control At(Control child, int row, int col)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, col);
        return child;
    }

    private static DynamicResourceExtension Res(string key) => new(key);
}
