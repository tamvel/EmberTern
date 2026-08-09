// Product Polish M4.2 / B2 — MATERIAŁ DECYZYJNY: promień karty aktywności w bliźniakach.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- radius
//
// ⛔ Kandydaci są zdefiniowani TUTAJ, nie w produkcie. Uruchomienie sondy nic nie wdraża.
//
// ─── SKĄD SIĘ WZIĘŁO TO PYTANIE ────────────────────────────────────────────────────────────────────
//
// `ProcedureDetailTabView` i `FunctionDetailTabView` rysują kartę aktywności tabeli z `CornerRadius="4"`,
// podczas gdy rola `Radius.Surface` niesie **3**. M2c iteracja 4 (§18.4) zostawiła wartość lokalną
// z powodem i oddała decyzję „karta: 3 czy 4" przeglądowi **§13.3**.
//
// ⚠⚠ I TU JEST PRAWDZIWE ZNALEZISKO: **§13.3a nigdy tego nie rozstrzygnęło.** Brama wyprodukowała
//    sześć znalezisk (Z‑1…Z‑6) i żadne z nich nie dotyczy promienia karty. Pozycja nie dostała też
//    numeru K, więc nie weszła do rejestru kolizji, który blok typografii zamknął „w całości".
//    Wypadła między jednym etapem a drugim — nie jako decyzja, tylko jako brak decyzji.
//
// ⭐ Dlatego to pytanie jest zadawane RENDEREM, a nie liczbą: przy różnicy 1 px odpowiedź „4 jest
//   lepsze od 3" nie ma sensu w oderwaniu od tego, czy karta zgadza się z SĄSIADAMI na tym samym
//   ekranie. Render pokazuje więc kartę razem z powierzchnią na `Radius.Surface` i chipem na
//   `Radius.Chip`, bo to jest realne otoczenie decyzji.
//
// ⚠⚠ DLACZEGO KOLUMNA ×4 JEST CZĘŚCIĄ PYTANIA, A NIE OZDOBĄ (§19.38.5): przy 1:1 różnica 3 vs 4 jest
//    podprogowa, a wtedy „nie widzę różnicy" i „render nie pokazuje różnicy" wyglądają IDENTYCZNIE.
//    Powiększenie rozstrzyga, którą z tych dwóch rzeczy się właśnie ogląda. Decyzja należy jednak
//    do kolumny 1:1 — to ona jest produktem (R16: kryterium odbioru jest ekran).
//
// ⚠ WIERNOŚĆ: karta jest odtworzona z markupu bliźniaków co do struktury i ról (tło `BackgroundBrush`,
//   padding 8,6, `Size.Icon` + `Text.SectionHeader` w nagłówku, `Size.Icon.Sm` + `Text.Compact`
//   w wierszach zmian), a geometrie i pędzle pobierane są z ZASOBÓW APLIKACJI po kluczu.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.Controls;

internal static class Radius
{
    // Stan faktyczny bliźniaków (§18.4.5) i wartość roli, której karta odmówiła.
    private const double CardToday = 4;
    private const double SurfaceRole = 3;

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Program.Render(CardVariants(), Path.Combine(outDir, $"m4r-b2-promien-karty-{variant}.png"), 1.0);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  B2 — karta aktywności: 4 (dziś) vs 3 (`Radius.Surface`), w otoczeniu, w dwóch skalach
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    private static Control CardVariants()
    {
        var grid = NewGrid("Auto,Auto,Auto");
        grid.Bind(Panel.BackgroundProperty, Res("ChromeStrongBrush"));

        Header(grid, string.Empty, "dziś — 4", "rola `Radius.Surface` — 3");

        // Wiersz 1: 1:1 — to jest kolumna, w której zapada decyzja.
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("1:1  (produkt)", 11), 1, 0));
        grid.Children.Add(At(Surroundings(CardToday), 1, 1));
        grid.Children.Add(At(Surroundings(SurfaceRole), 1, 2));

        // Wiersz 2: ×4 — rozstrzyga, czy 1:1 nie pokazuje różnicy, czy różnicy nie widać.
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(At(Label("×4  (czy różnica istnieje)", 11), 2, 0));
        grid.Children.Add(At(Zoom(Card(CardToday), 4), 2, 1));
        grid.Children.Add(At(Zoom(Card(SurfaceRole), 4), 2, 2));

        return grid;
    }

    // Karta NIE stoi sama — obok niej żyją dwa inne promienie z katalogu. Pytanie „3 czy 4" jest
    // pytaniem o zgodność z NIMI, a nie o urodę pojedynczego narożnika.
    private static Control Surroundings(double cardRadius)
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(8, 6, 12, 8) };
        stack.Children.Add(Card(cardRadius));

        var reference = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        reference.Children.Add(SurfaceSample("kontener · Radius.Surface (3)", SurfaceRole));
        reference.Children.Add(Chip("chip · Radius.Chip (4)", 4));
        stack.Children.Add(reference);

        return stack;
    }

    private static Control Card(double radius)
    {
        var body = new StackPanel { Spacing = 5 };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(Glyph("Icon.Table", 14, "AccentBrush"));
        var title = Text("ORDERS", 12);
        title.FontWeight = FontWeight.SemiBold;
        head.Children.Add(title);
        body.Children.Add(head);

        body.Children.Add(ChangeRow("Icon.Plus", "SuccessIconBrush", "12", "inserted"));
        body.Children.Add(ChangeRow("Icon.Pencil", "WarningIconBrush", "3", "updated"));
        body.Children.Add(ChangeRow("Icon.Trash", "DangerIconBrush", "1", "deleted"));

        var card = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(8, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        card.Bind(Border.BackgroundProperty, Res("BackgroundBrush"));
        return card;
    }

    private static Control ChangeRow(string geometryKey, string colourKey, string count, string verb)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Glyph(geometryKey, 12, colourKey));

        var n = Text(count, 11);
        n.FontWeight = FontWeight.SemiBold;
        n.Bind(TextBlock.ForegroundProperty, Res(colourKey));
        row.Children.Add(n);

        row.Children.Add(Label(verb, 11));
        return row;
    }

    private static Control SurfaceSample(string caption, double radius)
    {
        var border = new Border
        {
            Child = Label(caption, 11),
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(8, 6),
            BorderThickness = new Thickness(1),
        };
        border.Bind(Border.BackgroundProperty, Res("PanelBrush"));
        border.Bind(Border.BorderBrushProperty, Res("BorderBrush"));
        return border;
    }

    private static Control Chip(string caption, double radius)
    {
        var text = Text(caption, 11);
        text.Bind(TextBlock.ForegroundProperty, Res("OnAccentBrush"));

        var chip = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Bind(Border.BackgroundProperty, Res("AccentBrush"));
        return chip;
    }

    // ─── wspólne drobiazgi (kształt jak w Density.cs — jeden idiom w całej sondzie) ────────────────
    private static Grid NewGrid(string columns)
    {
        var grid = new Grid { Margin = new Thickness(10) };
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

    // ⚠ `LayoutTransformControl`, nie `Viewbox` — ten sam powód co w Density.cs: skalowanie UKŁADU
    //   powiększa geometrię wektorowo, więc różnica 4 vs 3 zostaje wierna. `Viewbox` dopasowałby
    //   treść do zadanej szerokości i ZNIÓSŁBY dokładnie tę różnicę, którą render ma pokazać.
    private static Control Zoom(Control child, double factor) =>
        new LayoutTransformControl
        {
            Child = child,
            LayoutTransform = new ScaleTransform(factor, factor),
            Margin = new Thickness(8, 6),
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
