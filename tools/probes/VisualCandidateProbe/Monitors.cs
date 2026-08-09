// Product Polish M4.3 — MATERIAŁ DECYZYJNY: Debugger · Trace · Session Manager · Security Manager · Performance.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- m43
//
// ⛔ Kandydaci są zdefiniowani TUTAJ, nie w produkcie. Uruchomienie sondy nic nie wdraża.
//
// ─── SKĄD SIĘ WZIĘŁY TE PYTANIA ────────────────────────────────────────────────────────────────────
//
// Inwentaryzacja wejściowa M4.3 (regeksy strażników odtworzone 1:1) dała 48 pozycji, ale ⭐⭐ NIE jest to
// sweep literałów: w pięciu plikach etapu stoi **19 komentarzy „Rozstrzyga §13.3"**, które pokrywają
// praktycznie każdą pozostałą tam wartość lokalną. Brama §13.3a nie podjęła ANI JEDNEJ z nich (Z‑1…Z‑6
// dotyczą czego innego), żadna nie dostała numeru K — a blok typografii ogłosił rejestr kolizji
// „zamknięty w całości". ⭐ To ten sam mechanizm, który §19.40.5 opisał przy B2 jako pojedynczą pozycję,
// która „wypadła między etapami"; zmierzone — B2 nie było wyjątkiem, tylko pierwszym napotkanym.
//
// Dlatego M4.3 zaczyna się od RENDERU, a nie od migracji: to są decyzje do odbioru, nie liczby do zamiany.
//
// ⚠⚠ KOLUMNA ×4 JEST CZĘŚCIĄ PYTANIA, NIE OZDOBĄ (§19.38.5 / §19.40.5): przy różnicy 1 px „nie widzę
//    różnicy" i „render nie pokazuje różnicy" wyglądają IDENTYCZNIE. Powiększenie rozstrzyga, którą
//    z tych dwóch rzeczy się właśnie ogląda. ⭐ Decyzja należy jednak do kolumny 1:1 — to ona jest
//    produktem (R16: kryterium odbioru jest ekran).
//
// ⚠ WIERNOŚĆ: tam, gdzie pytanie brzmi „czy to zgadza się z SĄSIADAMI", w renderze stoją PRAWDZIWE
//   kontrolki aplikacji (`TextBox`, `ComboBox`, `Button.flat`) biorące metryki i promień ze stylów
//   produktu. To jest istota Q1 i Q4: `ControlCornerRadius` Fluenta = 3 i jest świadomie NIENADPISANY
//   w `FluentBridge` (bo pokrywa się z `Radius.Surface`), więc każda prawdziwa obramowana kontrolka
//   renderuje się przy 3 — a cztery elementy w Trace i Session tylko ją udają, przy 4.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.Controls;

internal static class Monitors
{
    // Stan faktyczny (zmierzony 2026-08-09) i wartości ról, którym te elementy odmawiają.
    private const double RadiusToday = 4;
    private const double RadiusSurface = 3;

    private const double InlineXToday = 11;   // 3× debugger + 1× Session
    private const double InlineXOther = 12;   // czyszczenie pola Immediate — ten sam element, inna liczba
    private const double IconSm = 12;         // `Size.Icon.Sm`

    private const double EmptyStateToday = 13; // = `Text.Code`, a to nie jest kod
    private const double TextApplication = 12;
    private const double TinyToday = 9;        // katalog nie ma roli o tej wartości
    private const double TextCaption = 10;

    private const double FieldToday = 26;      // nie ma odpowiednika w katalogu
    private const double ControlToolbar = 22;
    private const double Control = 24;
    private const double ControlProminent = 28;

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Program.Render(Q1Radius(), Path.Combine(outDir, $"m43-q1-promien-{variant}.png"), 1.0);
            Program.Render(Q2InlineX(), Path.Combine(outDir, $"m43-q2-inline-x-{variant}.png"), 1.0);
            Program.Render(Q3Text(), Path.Combine(outDir, $"m43-q3-tekst-{variant}.png"), 1.0);
            Program.Render(Q4FieldHeight(), Path.Combine(outDir, $"m43-q4-pole-filtra-{variant}.png"), 1.0);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  Q1 — promień 4 (dziś) vs 3 (`Radius.Surface`) na SIEDMIU wystąpieniach w trzech ekranach.
    //
    //  ⭐ To jest B2 jeszcze raz, ale połowa argumentu jest już ratyfikowana: trzy KARTY dziedziczą
    //    rozstrzygnięcie M4.2 (komentarz `Radius.Surface` zaczyna się od słowa „Karta"). Nowe są cztery
    //    RAMKI KONTROLEK — i tam argument jest inny, mocniejszy: one udają obramowaną kontrolkę,
    //    a każda prawdziwa renderuje się przy 3. Dlatego ostatni wiersz to PRAWDZIWY `TextBox`.
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control Q1Radius()
    {
        var page = NewPage();
        page.Children.Add(Caption("Q1 · promień narożnika — 4 (dziś) vs 3 (rola `Radius.Surface`)"));
        page.Children.Add(Note("Trzy pierwsze wiersze to KARTY (argument B2, już ratyfikowany). Trzy kolejne to RAMKI"
                             + " KONTROLEK — udają obramowaną kontrolkę. Ostatni wiersz jest odniesieniem: prawdziwy"
                             + " `TextBox` z produktu, żeby było widać, przy jakim promieniu renderuje się reszta aplikacji."));

        var one = NewGrid("Auto,Auto,Auto");
        Header(one, string.Empty, "dziś — 4", "`Radius.Surface` — 3");
        Row(one, 1, "karta ostrzeżenia · Session", r => WarningCard(r), true);
        Row(one, 2, "karta błędu · Trace", r => ErrorCard(r), true);
        Row(one, 3, "karta findingu · Performance", r => FindingCard(r), true);
        Row(one, 4, "przełącznik segmentowy · Trace", r => Segmented(r, ["Chronologia", "Transakcje", "Instrukcje"]), true);
        Row(one, 5, "przełącznik segmentowy · Session", r => Segmented(r, ["All", "Long tx", "GC risk"]), true);
        Row(one, 6, "pole filtra · Trace", r => FilterField(r, FieldToday, 200), true);
        Row(one, 7, "⭐ prawdziwy TextBox (odniesienie)", _ => RealTextBox(), false);
        page.Children.Add(one);

        page.Children.Add(Caption("×4 — czy różnica w ogóle istnieje (decyzja należy do kolumny 1:1 wyżej)"));
        var four = NewGrid("Auto,Auto,Auto");
        Header(four, string.Empty, "dziś — 4", "`Radius.Surface` — 3");
        // ⚠ Elementy skrócone WYŁĄCZNIE w tym bloku: przy ×4 pełna szerokość przekracza limit MIERZENIA
        //   w `Program.Render` i obcięłaby ostatnią kolumnę — render wyglądałby poprawnie i odpowiadał
        //   na inne pytanie, niż zadano. Narożnik jest tu jedynym przedmiotem oglądania.
        Row(four, 1, "karta", r => Zoom(MiniCard(r), 4), true);
        Row(four, 2, "przełącznik", r => Zoom(Segmented(r, ["A", "B"]), 4), true);
        Row(four, 3, "pole filtra", r => Zoom(FilterField(r, FieldToday, 40), 4), true);
        page.Children.Add(four);

        return page;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  Q2 — inline ✕. Ten sam element dla użytkownika (✕ czyszczące/usuwające to, w czym stoi)
    //  renderuje się dziś przy 12 w jednym miejscu i przy 11 w czterech. Rola `Size.Icon.Sm` = 12,
    //  a jej własny opis brzmi „ikona inline w tekście 11 px (chip, wiersz siatki)" — czyli dokładnie
    //  te hosty; M4.2 zmigrowało tak sześć ikon tym samym argumentem.
    //
    //  ⚠ Koszt kandydata: cztery ikony ROSNĄ 11 → 12, co idzie pod prąd R18 (przy równej czytelności
    //    wygrywa gęstszy). Wariant odwrotny — „rola na 11" — ruszyłby sześć ikon już odebranych w M4.2,
    //    więc nie jest tu wariantem, tylko cofnięciem cudzej decyzji.
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control Q2InlineX()
    {
        var page = NewPage();
        page.Children.Add(Caption("Q2 · inline ✕ — dziś 12 / 11 / 11 / 11 vs rola `Size.Icon.Sm` = 12 wszędzie"));
        page.Children.Add(Note("Wszystkie cztery to dla użytkownika ten sam element: ✕, które czyści albo usuwa to,"
                             + " w czym stoi. Pytanie brzmi, czy mają być jednej wielkości — i czy tą wielkością jest 12."));

        var one = NewGrid("Auto,Auto,Auto");
        Header(one, string.Empty, "dziś", "`Size.Icon.Sm` — 12");
        Row2(one, 1, "czyszczenie pola Immediate · debugger", ImmediateRow(InlineXOther), ImmediateRow(IconSm), "12 → bez zmiany");
        Row2(one, 2, "usuń watch · wiersz listy", ListRow("v_total", InlineXToday), ListRow("v_total", IconSm), "11 → 12");
        Row2(one, 3, "usuń breakpoint · wiersz siatki", ListRow("linia 42 · warunek IDX = 3", InlineXToday), ListRow("linia 42 · warunek IDX = 3", IconSm), "11 → 12");
        Row2(one, 4, "wyczyść filtr · chip Session", FilterChip(InlineXToday), FilterChip(IconSm), "11 → 12");
        page.Children.Add(one);

        page.Children.Add(Caption("×4 — sama ikona, żeby różnica 1 px była rozstrzygalna"));
        var four = NewGrid("Auto,Auto,Auto");
        Header(four, string.Empty, "11", "12");
        Row2(four, 1, "✕", Zoom(Glyph("Icon.X", InlineXToday, "SubtleForegroundBrush"), 4), Zoom(Glyph("Icon.X", IconSm, "SubtleForegroundBrush"), 4), string.Empty);
        page.Children.Add(four);

        return page;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  Q3 — tekst bez roli. ⭐ Po rozdzieleniu glifów od tekstu (patrz niżej) zostają DWA pytania,
    //  a nie czternaście pozycji.
    //
    //  ⭐⭐ ROZDZIELENIE, KTÓRE ZMIENIŁO KSZTAŁT PYTANIA: grupa „TextBlock 13 px" z §18.0.5/3 opisuje
    //     dwie różne rzeczy pod jedną nazwą. Osiem „Bold 13" w Security Managerze NIE jest tekstem —
    //     są bindowane do `PrivilegeStateGlyphConverter`, który zwraca „✓" / „✓+", wewnątrz przycisku
    //     20×18. To glify strojone do KONTENERA, czyli reguła #10 działająca poprawnie — ta sama,
    //     którą komentarz 140 linii wyżej w TYM SAMYM pliku stosuje do 12 px („ELEMENT UKŁADU…
    //     dobrany do przycisku o `Height=18`"). Dlatego są tu pokazane, ale jako stan do POTWIERDZENIA,
    //     nie jako kandydat do zmiany.
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control Q3Text()
    {
        var page = NewPage();
        page.Children.Add(Caption("Q3 · tekst, który nie ma roli — dwa pytania, nie czternaście pozycji"));

        page.Children.Add(Note("A. Komunikat pustego stanu. ⭐ Session i Trace są konstrukcyjnie IDENTYCZNE (ten sam"
                             + " komentarz, ten sam pędzel, ten sam `Margin=\"0,40,0,0\"`, to samo centrowanie) — jeden"
                             + " element na dwóch ekranach, więc jedno pytanie. Dziś 13, czyli `Text.Code`; a to nie jest kod."));
        var a = NewGrid("Auto,Auto,Auto");
        Header(a, string.Empty, "dziś — 13 (= `Text.Code`)", "`Text.Application` — 12");
        Row2(a, 1, "pusty stan · Session", EmptyState("No sessions to display.", EmptyStateToday), EmptyState("No sessions to display.", TextApplication), string.Empty);
        Row2(a, 2, "pusty stan · Trace", EmptyState("Waiting for trace events…", EmptyStateToday), EmptyState("Waiting for trace events…", TextApplication), string.Empty);
        page.Children.Add(a);

        page.Children.Add(Note("B. Podpisy przy 9 px. Katalog nie ma roli o tej wartości; najbliższa, `Text.Caption`,"
                             + " niesie 10 — i komentarz w debuggerze nazywa tę decyzję wprost («9 → 10»)."));
        var b = NewGrid("Auto,Auto,Auto");
        Header(b, string.Empty, "dziś — 9", "`Text.Caption` — 10");
        Row2(b, 1, "znacznik pochodzenia wartości · debugger", Origin(TinyToday), Origin(TextCaption), string.Empty);
        Row2(b, 2, "skala pod paskiem GC · Session", GapScale(TinyToday), GapScale(TextCaption), string.Empty);
        page.Children.Add(b);

        page.Children.Add(Note("C. ⛔ NIE JEST KANDYDATEM — do potwierdzenia, że zostaje. Osiem «Bold 13» w Security"
                             + " Managerze to GLIFY z konwertera («✓» / «✓+») w przycisku 20×18, a nie tekst; ich rozmiar"
                             + " jest strojony do przycisku (reguła #10). Komentarz w kodzie mówi o nich «a to jest tekst»"
                             + " i to jest jedyna rzecz, którą trzeba tu poprawić."));
        var c = NewGrid("Auto,Auto,Auto");
        Header(c, string.Empty, "glif w przycisku 20×18", "sąsiadujący tekst wiersza");
        Row2(c, 1, "komórka uprawnienia · Security", PrivCell("✓"), Label("SELECT", 11), string.Empty);
        Row2(c, 2, "komórka uprawnienia z opcją grant", PrivCell("✓+"), Label("UPDATE", 11), string.Empty);
        page.Children.Add(c);

        return page;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  Q4 — wysokość pola filtra Trace. Dziś `Height="26"`; katalog ma 22 / 24 / 28 i 26 nie występuje
    //  nigdzie indziej w `src/`. ⚠ Blok typografii (decyzja D) właśnie usunął DRUGIE `MinHeight="26"`
    //  z nagłówków Expandera, przenosząc je na `Size.Control` — to jest ten sam kształt, który został.
    //
    //  ⭐ Pytanie jest o SĄSIADÓW, nie o liczbę, więc każdy wariant stoi w renderze obok prawdziwego
    //    `ComboBox` i `Button.flat` z produktu — dokładnie tak, jak stoi w pasku narzędzi Trace.
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control Q4FieldHeight()
    {
        var page = NewPage();
        page.Children.Add(Caption("Q4 · wysokość pola filtra Trace — dziś 26; katalog zna 22 / 24 / 28"));
        page.Children.Add(Note("Każdy wariant stoi obok PRAWDZIWEGO `ComboBox` i `Button.flat` z produktu, bo pytanie"
                             + " brzmi „czy to zgadza się z paskiem narzędzi\", a nie „która liczba jest ładniejsza\"."));

        var grid = NewGrid("Auto,Auto");
        Header(grid, string.Empty, "pole filtra w otoczeniu paska narzędzi");
        ToolbarRow(grid, 1, "dziś — 26", FieldToday);
        ToolbarRow(grid, 2, "`Size.ControlToolbar` — 22", ControlToolbar);
        ToolbarRow(grid, 3, "`Size.Control` — 24", Control);
        ToolbarRow(grid, 4, "`Size.ControlProminent` — 28", ControlProminent);
        page.Children.Add(grid);

        return page;
    }

    private static void ToolbarRow(Grid grid, int row, string caption, double height)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label(caption, 11), row, 0));

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        bar.Children.Add(FilterField(RadiusToday, height, 180));
        bar.Children.Add(RealComboBox());
        bar.Children.Add(RealButton("Export"));
        grid.Children.Add(At(bar, row, 1));
    }

    // ─── elementy odtworzone z markupu ekranów (struktura + role 1:1) ──────────────────────────────

    private static Control WarningCard(double radius) =>
        Card(radius, "PanelBrush", true, new Thickness(10, 8), stack =>
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            head.Children.Add(Glyph("Icon.AlertTriangle", 14, "WarningIconBrush"));
            var t = Text("Long-running transaction", 11);
            t.FontWeight = FontWeight.SemiBold;
            head.Children.Add(t);
            stack.Children.Add(head);
            stack.Children.Add(Label("Transaction 41 has been open for 12 min.", 11));
        });

    private static Control ErrorCard(double radius) =>
        Card(radius, "RowAlternateBrush", false, new Thickness(8, 6), stack =>
        {
            var t = Text("Dynamic SQL Error — SQL error code = -204", 11);
            t.Bind(TextBlock.ForegroundProperty, Res("ErrorBrush"));
            t.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(t);
        });

    private static Control FindingCard(double radius) =>
        Card(radius, "PanelBrush", true, new Thickness(8, 6), stack =>
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var badge = new Border
            {
                Child = Label("HIGH", 11),
                Padding = new Thickness(4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            badge.Bind(Border.CornerRadiusProperty, Res("Radius.Surface"));
            badge.Bind(Border.BackgroundProperty, Res("SelectionBrush"));
            head.Children.Add(badge);
            var t = Text("Sequential scan on ORDERS", 11);
            t.FontWeight = FontWeight.SemiBold;
            head.Children.Add(t);
            stack.Children.Add(head);
            stack.Children.Add(Label("142 380 records read to return 12 rows.", 11));
        });

    private static Control MiniCard(double radius) =>
        Card(radius, "PanelBrush", true, new Thickness(8, 6), stack => stack.Children.Add(Label("karta", 11)));

    private static Control Card(double radius, string backgroundKey, bool bordered, Thickness padding, Action<StackPanel> fill)
    {
        var body = new StackPanel { Spacing = 4 };
        fill(body);

        var card = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(radius),
            Padding = padding,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(bordered ? 1 : 0),
        };
        card.Bind(Border.BackgroundProperty, Res(backgroundKey));
        if (bordered)
        {
            card.Bind(Border.BorderBrushProperty, Res("BorderBrush"));
        }

        return card;
    }

    // Przełącznik segmentowy: ramka + `ClipToBounds` + poziomy `StackPanel` przycisków `.seg`.
    // ⚠ Styl `Button.seg` jest w produkcie LOKALNY (i zadeklarowany DWA razy — w Session i w Trace,
    //   z różnym paddingiem 8,3 vs 10,3), więc sonda odtwarza go wprost. To jest osobne znalezisko
    //   strukturalne, nie przedmiot tego renderu.
    private static Control Segmented(double radius, string[] labels)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < labels.Length; i++)
        {
            row.Children.Add(SegButton(labels[i], i == 0));
        }

        var frame = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        frame.Bind(Border.BorderBrushProperty, Res("BorderBrush"));
        return frame;
    }

    private static Control SegButton(string label, bool active)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(8, 3),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            FontSize = TextApplication,
            FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
        };
        button.Bind(Button.BackgroundProperty, Res(active ? "SelectionBrush" : "PanelBrush"));
        button.Bind(Button.ForegroundProperty, Res(active ? "ForegroundBrush" : "SubtleForegroundBrush"));
        return button;
    }

    // Pole filtra: ramka + ikona + `TextBox` bez własnej ramki. Udaje jedną kontrolkę i o to chodzi.
    private static Control FilterField(double radius, double height, double textWidth)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(Glyph("Icon.Search", 14, "SubtleForegroundBrush"));
        var text = Text("Filter events…", TextApplication);
        text.Width = textWidth;
        text.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        row.Children.Add(text);

        var frame = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            Height = height,
            Padding = new Thickness(7, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        frame.Bind(Border.BackgroundProperty, Res("BackgroundBrush"));
        frame.Bind(Border.BorderBrushProperty, Res("BorderBrush"));
        return frame;
    }

    // ⭐ ODNIESIENIE — prawdziwe kontrolki produktu. Ich promień i wysokość pochodzą ze stylów
    //   aplikacji, nie z tej sondy; to one odpowiadają na pytanie „przy czym renderuje się reszta".
    private static Control RealTextBox() =>
        new TextBox { Text = "prawdziwy TextBox", Width = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };

    private static Control RealComboBox()
    {
        var combo = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
        combo.Items.Add("Wszystkie zdarzenia");
        combo.SelectedIndex = 0;
        return combo;
    }

    private static Control RealButton(string label)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("flat");
        return button;
    }

    // ─── hosty inline ✕ ───────────────────────────────────────────────────────────────────────────

    private static Control ImmediateRow(double iconSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Label("v_total * 2", 11));
        row.Children.Add(Glyph("Icon.X", iconSize, "SubtleForegroundBrush"));
        var frame = new Border
        {
            Child = row,
            Padding = new Thickness(6, 3),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        frame.Bind(Border.CornerRadiusProperty, Res("Radius.Surface"));
        frame.Bind(Border.BorderBrushProperty, Res("BorderBrush"));
        frame.Bind(Border.BackgroundProperty, Res("BackgroundBrush"));
        return frame;
    }

    private static Control ListRow(string label, double iconSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Label(label, 11));
        row.Children.Add(Glyph("Icon.X", iconSize, "SubtleForegroundBrush"));
        row.Margin = new Thickness(6, 3);
        return row;
    }

    private static Control FilterChip(double iconSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Text("Filtered by session 41", 11));
        row.Children.Add(Glyph("Icon.X", iconSize, "SubtleForegroundBrush"));

        var chip = new Border
        {
            Child = row,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        chip.Bind(Border.CornerRadiusProperty, Res("Radius.Chip"));
        chip.Bind(Border.BackgroundProperty, Res("SelectionBrush"));
        return chip;
    }

    // ─── hosty tekstowe ───────────────────────────────────────────────────────────────────────────

    private static Control EmptyState(string message, double size)
    {
        var text = Text(message, size);
        text.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        text.HorizontalAlignment = HorizontalAlignment.Center;

        var panel = new Border
        {
            Child = text,
            Width = 260,
            Height = 64,
            Margin = new Thickness(8, 5),
        };
        panel.Bind(Border.BackgroundProperty, Res("BackgroundBrush"));
        return panel;
    }

    private static Control Origin(double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 5), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Text("120", TextApplication));
        var origin = Text("odtworzone", size);
        origin.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        row.Children.Add(origin);
        return row;
    }

    private static Control GapScale(double size)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(8, 5), Width = 220 };
        var bar = new Border { Height = 10, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(5) };
        bar.Bind(Border.BackgroundProperty, Res("SelectionBrush"));
        stack.Children.Add(bar);

        var labels = new Grid { Width = 220 };
        var min = Text("0", size);
        min.HorizontalAlignment = HorizontalAlignment.Left;
        min.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        var max = Text("GC risk near 20 000", size);
        max.HorizontalAlignment = HorizontalAlignment.Right;
        max.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        labels.Children.Add(min);
        labels.Children.Add(max);
        stack.Children.Add(labels);
        return stack;
    }

    // Komórka uprawnienia: glif z konwertera w przycisku 20×18 — element UKŁADU, nie tekst.
    private static Control PrivCell(string glyph)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.ForegroundProperty, Res("ConnectedBrush"));

        var button = new Border
        {
            Child = text,
            Width = 20,
            Height = 18,
            Margin = new Thickness(8, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Bind(Border.BorderBrushProperty, Res("ControlOutlineBrush"));
        button.BorderThickness = new Thickness(1);
        return button;
    }

    // ─── wspólne drobiazgi (idiom jak w Radius.cs / Density.cs — jeden w całej sondzie) ────────────

    private static StackPanel NewPage()
    {
        var page = new StackPanel { Spacing = 4, Margin = new Thickness(10) };
        page.Bind(Panel.BackgroundProperty, Res("ChromeStrongBrush"));
        return page;
    }

    private static Control Caption(string text)
    {
        var t = Text(text, 13);
        t.FontWeight = FontWeight.SemiBold;
        t.Margin = new Thickness(4, 10, 4, 2);
        return t;
    }

    private static Control Note(string text)
    {
        var t = Text(text, 11);
        t.TextWrapping = TextWrapping.Wrap;
        t.MaxWidth = 900;
        t.Margin = new Thickness(4, 0, 4, 6);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
        return t;
    }

    private static Grid NewGrid(string columns)
    {
        var grid = new Grid { Margin = new Thickness(4) };
        foreach (var part in columns.Split(','))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse(part)));
        }

        return grid;
    }

    private static void Header(Grid grid, params string[] headers)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < headers.Length; i++)
        {
            var h = Text(headers[i], 11);
            h.FontWeight = FontWeight.SemiBold;
            h.Margin = new Thickness(8, 4);
            h.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
            grid.Children.Add(At(h, 0, i));
        }
    }

    // Wiersz „ten sam element w dwóch promieniach"; `bothColumns=false` dla wiersza odniesienia.
    private static void Row(Grid grid, int row, string caption, Func<double, Control> build, bool bothColumns)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label(caption, 11), row, 0));
        grid.Children.Add(At(Pad(build(RadiusToday)), row, 1));
        if (bothColumns)
        {
            grid.Children.Add(At(Pad(build(RadiusSurface)), row, 2));
        }
    }

    private static void Row2(Grid grid, int row, string caption, Control left, Control right, string delta)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var label = caption + (string.IsNullOrEmpty(delta) ? string.Empty : $"   ({delta})");
        grid.Children.Add(At(Label(label, 11), row, 0));
        grid.Children.Add(At(Pad(left), row, 1));
        grid.Children.Add(At(Pad(right), row, 2));
    }

    private static Control Pad(Control child)
    {
        child.Margin = new Thickness(8, 5);
        return child;
    }

    private static TextBlock Label(string text, double size)
    {
        var t = Text(text, size);
        t.Bind(TextBlock.ForegroundProperty, Res("SubtleForegroundBrush"));
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

    // ⚠ `LayoutTransformControl`, nie `Viewbox` — ten sam powód co w Radius.cs: skalowanie UKŁADU
    //   powiększa geometrię wektorowo, więc różnica 4 vs 3 (albo 11 vs 12) zostaje wierna. `Viewbox`
    //   dopasowałby treść do zadanej szerokości i ZNIÓSŁBY dokładnie tę różnicę, którą render pokazuje.
    private static Control Zoom(Control child, double factor) =>
        new LayoutTransformControl
        {
            Child = child,
            LayoutTransform = new ScaleTransform(factor, factor),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private static Control At(Control child, int row, int col)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, col);
        return child;
    }

    private static DynamicResourceExtension Res(string key) => new(key);
}
