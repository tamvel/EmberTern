// Product Polish M5 / L‑1 — materiał decyzyjny dla pierścienia focus.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- focus
//
// ─── POMIAR WEJŚCIOWY (headless, `Focus(NavigationMethod)` + odczyt z ContentPresentera) ──────────
//
//   wariant   pseudoklasy przy Tab                        pseudoklasy przy Pointer      ramka w stanie focus
//   icon      :focus-within, :focus, :focus-visible       :focus-within, :focus         #007FD4  gr. 1
//   flat      :focus-within, :focus, :focus-visible       :focus-within, :focus         #007FD4  gr. 1
//   primary   :focus-within, :focus, :focus-visible       :focus-within, :focus         #2D6BBF  gr. 1   ← to jest jego
//                                                                                                  WŁASNA ramka akcentu,
//                                                                                                  focus jej nie zmienia
//   caption   :focus-within, :focus, :focus-visible       :focus-within, :focus         Transparent gr. 0
//
// ⭐⭐ TRZY WNIOSKI, KTÓRE ZMIENIAJĄ KSZTAŁT L‑1 (audyt opisywał to jako „primary/caption nie mają :focus"):
//
//   1. `:focus` ZAPALA SIĘ TAKŻE OD MYSZY. Zmierzone — po `Focus(NavigationMethod.Pointer)` klasa `:focus`
//      jest obecna, a `:focus-visible` nie. Czyli `Button.icon` i `Button.flat` pokazują niebieski
//      pierścień PO KLIKNIĘCIU MYSZĄ i zostaje on, aż fokus odejdzie.
//      ⚠ A `CheckBox` i `RadioButton` w `ControlThemes.axaml` używają `:focus-visible`, czyli reagują
//      WYŁĄCZNIE na klawiaturę. Aplikacja ma więc DWA różne zachowania focusu, zależnie od kontrolki —
//      i to jest defekt głębszy niż „brakuje selektora w dwóch wariantach".
//
//   2. ⛔ NAIWNA POPRAWKA DLA `primary` DAŁABY PIERŚCIEŃ NIEWIDOCZNY. `FocusBorderBrush` na tle akcentu
//      to **1,26:1 w Dark i 1,17:1 w Light** (próg 3:1 dla znaczącego elementu nietekstowego). Skopiowanie
//      settera z `Button.flat` wygląda jak naprawa i nie naprawia nic.
//
//   3. ⛔ NAIWNA POPRAWKA DLA `caption` BYŁABY MARTWA. Ten wariant ma `BorderThickness=0` (świadomy reset —
//      to przyciski paska tytułu), więc setter `BorderBrush` **nie namaluje niczego**. Styl bezczynny czyta
//      się dla następnej osoby jak działające zabezpieczenie (ta sama pułapka co settings-center §15.7).
//
// ⭐ KANDYDACI POLICZENI PRZY PROGU 3:1, każdy z tokenu, który TA kontrolka już zna:
//     primary → `OnAccentBrush` (biel), czyli barwa jej własnego tekstu    → 5,29:1 na akcencie
//     caption → `FocusBorderBrush` jako TŁO, czyli język, którym ten wariant już sygnalizuje hover
//               → 3,27:1 (Dark) / 3,76:1 (Light) wobec paska tytułu
//
// ⚠ Kandydaci są tu składani WARTOŚCIAMI LOKALNYMI na instancji — a wartość lokalna bije setter stylu,
//   więc podgląd jest wiernym odwzorowaniem tego, co dałby setter w `ControlStyles.axaml` (ta sama technika,
//   którą sonda stosuje dla `CheckBox` w Z‑2).
//
// ⛔ KANDYDACI SĄ ZDEFINIOWANI TUTAJ, NIE W PRODUKCIE. Uruchomienie sondy niczego nie wdraża.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

internal static class Focus
{
    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;

            var file = Path.Combine(outDir, $"m5f-focus-{variant}.png");
            Program.Render(BuildTable(), file, scale: 2.0);
            Console.WriteLine(file);
        }
    }

    private static Control BuildTable()
    {
        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("120,150,150,150"),
            RowSpacing = 12,
        };
        // Pasek tytułu jest właściwym tłem dla `caption`, a dla pozostałych i tak jest to chroma.
        grid.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));

        var headers = new[] { "", "spoczynek", "focus DZIŚ", "focus KANDYDAT" };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headers.Length; c++)
        {
            var h = new TextBlock { Text = headers[c], FontSize = 11, Margin = new Thickness(4, 0, 4, 4) };
            h.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            Grid.SetRow(h, 0);
            Grid.SetColumn(h, c);
            grid.Children.Add(h);
        }

        var variants = new[] { "icon", "flat", "primary", "caption" };
        for (var i = 0; i < variants.Length; i++)
        {
            var name = variants[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock { Text = "Button." + name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(Place(label, row, 0));
            grid.Children.Add(Place(Host(Make(name, focused: false, candidate: false)), row, 1));
            grid.Children.Add(Place(Host(Make(name, focused: true, candidate: false)), row, 2));
            grid.Children.Add(Place(Host(Make(name, focused: true, candidate: true)), row, 3));
        }

        return grid;
    }

    /// <summary>
    /// ⚠ Każdy przycisk mieszka we WŁASNYM oknie, bo focus jest właściwością okna — dwa przyciski w jednym
    /// drzewie nie mogą być zafokusowane naraz, a render pokazałby wtedy stan, którego nie zamawiano.
    /// Okno jest niepokazywane w wyniku; służy wyłącznie za nośnik fokusu, a do siatki trafia sam przycisk.
    /// </summary>
    private static Control Host(Button button)
    {
        var holder = new Border { Padding = new Thickness(6) };
        holder.Child = button;
        return holder;
    }

    private static Button Make(string variant, bool focused, bool candidate)
    {
        var button = new Button
        {
            Classes = { variant },
            Content = variant == "icon" || variant == "caption" ? "✕" : "Wykonaj",
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        if (focused && candidate)
        {
            // ⭐ Kandydat składany z tokenu, który TEN wariant już zna — nie z nowej barwy.
            switch (variant)
            {
                case "primary":
                    // Biały pierścień = barwa własnego tekstu przycisku; 5,29:1 na akcencie.
                    button.BorderBrush = Resolve("OnAccentBrush");
                    button.BorderThickness = new Thickness(2);
                    break;
                case "caption":
                    // Ten wariant sygnalizuje stan TŁEM (tak działa jego hover), a nie ramką —
                    // bo `BorderThickness` jest u niego świadomym resetem do zera.
                    button.Background = Resolve("FocusBorderBrush");
                    // ⚠⚠ DWA SETTERY, NIE JEDEN — i to wyszło dopiero z renderu. Samo tło zostawia glif
                    //   w `ForegroundBrush`, co na niebieskim daje **2,84:1 w Dark** (pod progiem 3:1).
                    //   `OnAccentBrush` podnosi to do 4,21 (Dark) / 4,53 (Light). Kandydat „tylko tło"
                    //   został odrzucony pomiarem, zanim trafił do materiału decyzyjnego.
                    button.Foreground = Resolve("OnAccentBrush");
                    break;
                default:
                    // icon / flat — kandydat jest tożsamy ze stanem dzisiejszym; różnica dotyczy
                    // WYZWALACZA (`:focus` vs `:focus-visible`), a tego nie widać na statycznym renderze.
                    button.BorderBrush = Resolve("FocusBorderBrush");
                    break;
            }
        }
        else if (focused)
        {
            // Stan „dziś": tylko `icon` i `flat` mają setter; `primary` i `caption` nie reagują na focus.
            if (variant is "icon" or "flat")
            {
                button.BorderBrush = Resolve("FocusBorderBrush");
            }
        }

        return button;
    }

    private static IBrush? Resolve(string key) =>
        Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var v)
            ? v as IBrush
            : null;

    private static Control Place(Control child, int row, int col)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, col);
        return child;
    }
}
