// Punkt 4 pakietu po M5 — Performance / Execution plan (advanced): layout (4a) + kolorowanie drzewa (4b).
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- plan
//
// ⭐ TO NIE SĄ KANDYDACI — to render STANU WDROŻONEGO obok stanu sprzed zmiany, w obu motywach.
//
// ⭐⭐ Drzewo po prawej jest ZBUDOWANE PRZEZ PRODUKCJĘ: prawdziwy `PlanParser` na prawdziwym tekście planu
//   → `PlanNodeViewModel` → `PlanTextSegments`. Sonda nie zna żadnej reguły kolorowania; gdyby produkcja
//   pokolorowała plan źle, ten obrazek pokaże dokładnie ten błąd. Kolumna „PRZED" renderuje ten sam model
//   tak, jak robił to widok wcześniej — jednym `TextBlockiem` na `Node.RawText`.
//
// ⚠ Tekst planu poniżej to realny Explain z bazy użytkownika (ze zgłoszenia), nie wymyślony przykład —
//   dzięki temu render odpowiada na pytanie o TEN plan, a nie o plan wygodny dla renderu.
//
// ⛔ ZAKRES KOLOROWANIA jest zamknięty: barwę niosą wyłącznie NAZWA OBIEKTU (kolor rodzaju) i PEŁNY SKAN
//   (cały wiersz, ostrzeżenie). Wariant różnicujący dodatkowo metody dostępu i wycofujący czasowniki
//   Table/Index został zbudowany, obejrzany i ODRZUCONY — dwa neutralne poziomy tekstu dzieli w Dark
//   zaledwie 1,78:1, więc dodatkowe rozróżnienie nie było wiarygodnie widoczne.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.ViewModels;
using EmberTern.Core.Performance;

internal static class Plan
{
    private const string RealExplainPlan = """
        Select Expression
            -> Sort (record length: 860, key length: 8)
                -> Unique Sort (record length: 2412, key length: 1636)
                    -> Filter
                        -> Nested Loop Join (outer)
                            -> Nested Loop Join (inner)
                                -> Filter
                                    -> Table "TECHNOLOGIA" as "T" Access By ID
                                        -> Bitmap Or
                                            -> Bitmap
                                                -> Index "MK_TECHNOLOGIA_STATUS" Range Scan (full match)
                                -> Filter
                                    -> Table "KARTOTEKA" as "PR" Access By ID
                                        -> Bitmap
                                            -> Index "PK_KARTOTEKA" Unique Scan
                            -> Filter
                                -> Table "OZNACZDOK" as "OZN" Access By ID
                                    -> Bitmap
                                        -> Index "MK_OZNACZDOK" Unique Scan
        """;

    /// <summary>Drugi plan — z pełnym skanem, żeby render pokazał, że najważniejszy wiersz NADAL jest
    /// ostrzeżeniem w jednym kawałku i nie zgubił się wśród nowych barw.</summary>
    private const string PlanWithFullScan = """
        Select Expression
            -> Filter
                -> Table "ZAMOWIENIA" as "Z" Full Scan
            -> Table "KONTRAHENCI" as "K" Access By ID
                -> Bitmap
                    -> Index "PK_KONTRAHENCI" Unique Scan
        """;

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            var file = Path.Combine(outDir, $"plan-{variant}.png");
            Program.Render(Build(variant), file, scale: 1.6);
            Console.WriteLine(file);
        }
    }

    private static Control Build(ThemeVariant variant)
    {
        var root = new StackPanel { Spacing = 20, Margin = new Thickness(22) };
        root.Children.Add(Heading($"Execution plan (advanced) — {variant}", 17));

        root.Children.Add(Heading("4b · kolorowanie drzewa — realny plan ze zgłoszenia", 13));
        root.Children.Add(Comparison(RealExplainPlan));

        root.Children.Add(Heading("4b · pełny skan — cały wiersz zostaje ostrzeżeniem", 13));
        root.Children.Add(Comparison(PlanWithFullScan));

        root.Children.Add(Note(
            "Barwę niosą WYŁĄCZNIE nazwy obiektów — te same barwy RODZAJU, których używa Metadata Explorer "
            + "(IconColor_Table / _Index), więc tabela w planie wygląda jak ta sama tabela w drzewie obiektów. "
            + "Reszta wiersza to zwykły tekst, a kwalifikatory i alias są wycofane. Pełny skan maluje CAŁY "
            + "wiersz ostrzeżeniem — świadomie niepodzielony."));
        root.Children.Add(Note(
            "Blok „Raw plan\" pod drzewem pozostaje wiernym monospace bez kolorowania, a Copy kopiuje "
            + "niezmieniony ładunek — kolorowanie dotyczy wyłącznie drzewa."));

        root.Children.Add(Heading("4a · odstęp treści sekcji", 13));
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 26,
            Children =
            {
                Column("PRZED — ExpanderContentPadding = 0", Section(new Thickness(0), new Thickness(0, 6, 0, 4))),
                Column("PO — Pad.Group (10,8)", Section(Token<Thickness>("Pad.Group"), default)),
            },
        });

        if (variant == ThemeVariant.Light)
        {
            // ⚠ Korekta dwóch barw rodzaju jest WARUNKIEM poprawności kolorowania planu w Light, nie osobną
            //   decyzją estetyczną: jako TEKST dawały 4,00:1 i 3,70:1 przy progu 4,5:1.
            root.Children.Add(Heading("Korekta barw rodzaju w Light — warunek progu, zasięg poza planem", 13));
            root.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 24,
                Children =
                {
                    Column("PRZED — 4,00:1 / 3,70:1 (pod progiem 4,5:1)",
                        Swatches([("IconColor_Index", "#558B2F"), ("IconColor_Procedure", "#E65100")])),
                    Column("PO — 4,54:1 / 4,50:1",
                        Swatches([("IconColor_Index", "#4F812C"), ("IconColor_Procedure", "#CE4800")])),
                },
            });
        }

        return root;
    }

    private static Control Comparison(string planText)
    {
        var roots = PlanNodeViewModel.BuildRoots(new PlanParser().Parse(
            new RawPlanCapture(PlanDialect.Explain, planText)));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        row.Children.Add(Column("PRZED — jeden płaski TextBlock", Flat(roots)));
        row.Children.Add(Column("PO — segmenty z PlanNode", Coloured(roots)));
        return row;
    }

    /// <summary>Jak widok renderował wiersz wcześniej: `Node.RawText` w jednym kolorze.</summary>
    private static Control Flat(IReadOnlyList<PlanNodeViewModel> roots)
    {
        var panel = new StackPanel();
        Walk(roots, 0, (node, depth) => panel.Children.Add(new TextBlock
        {
            Text = new string(' ', depth * 4) + node.DisplayText,
            FontFamily = Mono,
            FontSize = 11,
            Foreground = node.IsSequentialScan ? Brush("WarningBrush") : Brush("ForegroundBrush"),
            FontWeight = node.IsSequentialScan ? FontWeight.SemiBold : FontWeight.Normal,
        }));
        return Framed(panel);
    }

    /// <summary>Jak renderuje teraz: przebiegi z `PlanTextSegments`, barwy rozwiązane po kluczu motywu.</summary>
    private static Control Coloured(IReadOnlyList<PlanNodeViewModel> roots)
    {
        var panel = new StackPanel();
        Walk(roots, 0, (node, depth) =>
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = new string(' ', depth * 4), FontFamily = Mono, FontSize = 11 });
            foreach (var segment in node.Segments)
            {
                line.Children.Add(new TextBlock
                {
                    Text = segment.Text,
                    FontFamily = Mono,
                    FontSize = 11,
                    Foreground = Brush(segment.BrushKey),
                    FontWeight = node.IsSequentialScan ? FontWeight.SemiBold : FontWeight.Normal,
                });
            }
            panel.Children.Add(line);
        });
        return Framed(panel);
    }

    /// <summary>Prawdziwy `Expander` z zasobami z produkcji — kolumny różnią się wyłącznie
    /// `ExpanderContentPadding` (i marginesem, który po zmianie stał się zbędny).</summary>
    private static Control Section(Thickness contentPadding, Thickness legacyMargin)
    {
        var expander = new Expander
        {
            Header = "Execution plan (advanced)",
            IsExpanded = true,
            MinHeight = 34,
            Width = 520,
        };
        expander.Resources["ExpanderHeaderPadding"] = new Thickness(10, 0, 0, 0);
        expander.Resources["ExpanderContentPadding"] = contentPadding;

        var body = new StackPanel { Spacing = Token<double>("Space.Md"), Margin = legacyMargin };
        body.Children.Add(Coloured(PlanNodeViewModel.BuildRoots(new PlanParser().Parse(
            new RawPlanCapture(PlanDialect.Explain, PlanWithFullScan)))));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var label = new TextBlock { Text = "Raw plan", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                                    Foreground = Brush("SubtleForegroundBrush") };
        var copy = new Button { Content = "Copy", Classes = { "flat" } };
        Grid.SetColumn(copy, 1);
        header.Children.Add(label);
        header.Children.Add(copy);
        body.Children.Add(header);

        expander.Content = body;
        return expander;
    }

    /// <summary>Próbki barw wpisane WPROST (a nie z zasobów), bo render ma pokazać PRZED obok PO — a wartość
    /// „przed" w produkcie już nie istnieje.</summary>
    private static Control Swatches((string Name, string Hex)[] entries)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var (name, hex) in entries)
        {
            var colour = Color.Parse(hex);
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3),
                                 Background = new SolidColorBrush(colour) },
                    new TextBlock { Text = $"{name}  {hex}", FontFamily = Mono, FontSize = 11,
                                    Foreground = new SolidColorBrush(colour),
                                    VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "TECHNOLOGIA", FontFamily = Mono, FontSize = 11,
                                    Foreground = new SolidColorBrush(colour),
                                    VerticalAlignment = VerticalAlignment.Center },
                },
            });
        }
        return Framed(panel);
    }

    private static void Walk(IReadOnlyList<PlanNodeViewModel> nodes, int depth, Action<PlanNodeViewModel, int> emit)
    {
        foreach (var node in nodes) { emit(node, depth); Walk(node.Children, depth + 1, emit); }
    }

    private static T Token<T>(string key)
        => Application.Current!.FindResource(Application.Current.ActualThemeVariant, key) is T t ? t : default!;

    private static FontFamily Mono => new("Consolas,Cascadia Code,monospace");

    private static Control Framed(Control content)
        => new Border { Background = Brush("BackgroundBrush"), BorderBrush = Brush("BorderBrush"),
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(10, 8), Child = content };

    private static Control Column(string label, Control content)
        => new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = label, FontSize = 10, Foreground = Brush("SubtleForegroundBrush") },
                content,
            },
        };

    private static Control Heading(string text, double size)
        => new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold,
                           Foreground = Brush("ForegroundBrush"), Margin = new Thickness(0, 6, 0, 0) };

    private static Control Note(string text)
        => new TextBlock { Text = text, FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 1100,
                           Foreground = Brush("SubtleForegroundBrush") };

    private static IBrush Brush(string key)
        => Application.Current!.FindResource(Application.Current.ActualThemeVariant, key) as IBrush ?? Brushes.Magenta;
}
