// Product Polish M5 / M‑3 klasa A — materiał decyzyjny dla PUSTEGO PASKA BOCZNEGO.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- empty
//
// ─── CO JEST PRZEDMIOTEM DECYZJI ─────────────────────────────────────────────────────────────────
//
// Pierwsze uruchomienie EmberTerna: zero profili połączeń ⇒ `SidebarRows` jest puste ⇒ pod polem
// filtra nie ma NICZEGO. Zmierzone w `MainWindow.axaml`: pasek boczny to `TextBox` filtra + płaska
// `ListBox` nad `SidebarRows`, i nic poza tym.
//
// ⭐ Akcja „nowe połączenie" istnieje w DOKŁADNIE dwóch miejscach i żadne nie jest widoczne z pustego
//   panelu: przycisk `Button.icon` z glifem `Icon.Plus` w pasku tytułu (MainWindow.axaml:127) oraz
//   pozycja menu kontekstowego FOLDERU — a folderu też nie ma czym otworzyć, bo lista jest pusta.
//   ⛔ Nie ma dla niej `CommandId`, więc nie ma skrótu i `CommandTip.Sentence` nic tu nie doda.
//
// ⚠⚠ ZNALEZISKO, KTÓRE ZMIENIA KSZTAŁT PYTANIA: gotowa (i nieużywana) stała `ConnectionsEmptyHint`
//   brzmi „Click “+ New Connection” to add one." — i CYTUJE ETYKIETĘ, KTÓREJ W PRODUKCIE NIE MA.
//   Przycisk jest sam glifem; jego tooltip brzmi „New Connection" (bez plusa w treści). Użytkownik
//   dostałby więc polecenie znalezienia napisu, który nigdzie nie występuje — kształt gotchy #311
//   (kłamiąca etykieta jest nieodróżnialna od awarii). Wpięcie tej stałej „bo jest gotowa" byłoby
//   wpięciem defektu. Dlatego W1 stoi w renderze OSOBNO, jako stan do odrzucenia, a nie jako kandydat.
//
// ─── KANDYDACI ───────────────────────────────────────────────────────────────────────────────────
//
//   W0  stan dzisiejszy — pusto (odniesienie)
//   W1  istniejąca stała `ConnectionsEmptyHint`, dosłownie          ⚠ cytuje nieistniejącą etykietę
//   W2  ta sama konstrukcja, ale nazwa zgodna z tooltipem
//   W3  najpierw KROK, potem lokalizacja — bez donoszenia o nieobecności
//   W4  jak W3 + PRAWDZIWY glif `Icon.Plus` w treści (dopasowanie kształtu, nie słowa)
//   W5  jak W3 + przycisk akcji w panelu   ⛔ to już nie komunikat, tylko NOWA AFORDANCJA
//
// ⚠ W5 celowo jest w materiale, mimo że wykracza poza „stan pusty": bez niego decyzja „komunikat czy
//   przycisk" zapadłaby na podstawie mojego opisu, a nie obrazu. ⛔ Jego obecność w renderze nie jest
//   rekomendacją.
//
// ⭐ Geometrie ikon są ROZWIĄZYWANE Z ZASOBÓW APLIKACJI po kluczu (`Icon.Plus` itd.), nie przepisane —
//   render pokazuje ten sam kształt, który maluje produkt (#345: mierz to, co maluje, nie kopię).
//
// ⛔ KANDYDACI SĄ ZDEFINIOWANI TUTAJ, NIE W PRODUKCIE. Uruchomienie sondy niczego nie wdraża.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.Controls;

internal static class Empty
{
    /// <summary>Rzeczywista szerokość paska bocznego z `MainWindow.axaml` (ColumnDefinition Width="280").</summary>
    private const double SidebarWidth = 280;

    /// <summary>Wysokość obszaru listy w podglądzie — tyle, żeby było widać, ile pustki wypełnia komunikat.</summary>
    private const double ListHeight = 200;

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;

            var file = Path.Combine(outDir, $"m5e-sidebar-{variant}.png");
            Program.Render(BuildStrip(), file, scale: 2.0);
            Console.WriteLine(file);

            // ⭐ Osobny render tego, CO WESZŁO — wzorzec `z6-SHIPPED-*` z M3.5. „Kandydat wygląda dobrze"
            //   i „tak wygląda produkt" to dwa różne twierdzenia, a tutaj naprawdę się różnią: wdrożenie
            //   bierze ROLE z katalogu (`Size.Icon.Sm` 12 + `Text.Compact.Size` 11), podczas gdy render
            //   kandydata miał dobrane ręcznie 14 + 10. Powód odstępstwa stoi przy elemencie w `MainWindow.axaml`.
            var shipped = Path.Combine(outDir, $"m5e-shipped-{variant}.png");
            Program.Render(BuildShipped(), shipped, scale: 2.0);
            Console.WriteLine(shipped);

            // Pozostałe stany puste M‑3 (B2 · B3 ×2 kierunki · B6 ×2 stany) — do QA wzrokowego treści.
            var rest = Path.Combine(outDir, $"m5e-states-{variant}.png");
            Program.Render(BuildStates(), rest, scale: 2.0);
            Console.WriteLine(rest);
        }
    }

    /// <summary>
    /// Pozostałe stany puste M‑3 na właściwym tle. ⚠ To REKONSTRUKCJA treści i wagi tekstu, nie zrzut
    /// prawdziwych ekranów: siatki Security Managera i Script Executora wymagają ViewModeli z usługami.
    /// Wpięcia pilnują strażniki źródłowe; ten obraz służy wyłącznie ocenie SŁÓW i ich czytelności.
    /// </summary>
    private static Control BuildStates()
    {
        var grid = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Margin = new Thickness(16) };
        grid.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        grid.Children.Add(StateRow("B2 · Security → Roles (baza bez ról)",
            $"No roles yet — use Add role above."));
        grid.Children.Add(StateRow("B3 · Membership — kierunek „Member of\"",
            "This user or role belongs to no roles."));
        grid.Children.Add(StateRow("B3 · Membership — kierunek „Members\"",
            "This role has no members."));
        grid.Children.Add(StateRow("B6 · Script Executor — przed uruchomieniem",
            "Run the script — each statement and its result appear here."));
        grid.Children.Add(StateRow("B6 · Script Executor — filtr ukrył wszystko",
            "No statements match the current filter."));
        return grid;
    }

    private static Control StateRow(string caption, string text)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        var label = new TextBlock { Text = caption, FontSize = 11 };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        stack.Children.Add(label);

        var host = new Border { Width = 640, Height = 64 };
        host.Bind(Border.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));
        host.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        host.BorderThickness = new Thickness(1);

        var message = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        message.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        message.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Application.Size"));
        host.Child = message;

        stack.Children.Add(host);
        return stack;
    }

    private static Control BuildShipped()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(16),
        };
        row.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));
        row.Children.Add(Column("PRZED — dziś", null));
        row.Children.Add(Column("PO — jak weszło (role z katalogu)", ShippedHint()));
        return row;
    }

    /// <summary>
    /// Odwzorowanie WDROŻONEGO elementu: te same klucze zasobów, których używa `MainWindow.axaml`.
    /// ⚠ To rekonstrukcja, nie prawdziwy widok — `MainWindow` nie daje się zbudować w sesji headless
    /// (zmierzony kształt wieszający suite), więc render dowodzi WYGLĄDU ról, a nie wpięcia. Wpięcia
    /// pilnuje strażnik źródłowy `SidebarEmptyState_IsActuallyBoundInTheView`.
    /// </summary>
    private static Control ShippedHint()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Bind(StackPanel.SpacingProperty, new DynamicResourceExtension("Space.Sm"));
        stack.Bind(Layoutable.MarginProperty, new DynamicResourceExtension("Pad.Panel"));

        var title = new TextBlock
        {
            Text = "Add a connection to get started.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        title.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        title.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Application.Size"));
        stack.Children.Add(title);

        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        line.Bind(StackPanel.SpacingProperty, new DynamicResourceExtension("Space.Xs"));

        var icon = new SvgIcon { VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current!.Resources.TryGetResource("Icon.Plus", Application.Current.ActualThemeVariant, out var g)
            && g is Geometry geometry)
        {
            icon.Data = geometry;
        }

        icon.Bind(Layoutable.WidthProperty, new DynamicResourceExtension("Size.Icon.Sm"));
        icon.Bind(Layoutable.HeightProperty, new DynamicResourceExtension("Size.Icon.Sm"));
        icon.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,
            new DynamicResourceExtension("SubtleForegroundBrush"));
        line.Children.Add(icon);

        var caption = new TextBlock
        {
            Text = "New Connection — in the toolbar above",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        caption.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        caption.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Compact.Size"));
        line.Children.Add(caption);

        stack.Children.Add(line);
        return stack;
    }

    private static Control BuildStrip()
    {
        // ⚠⚠ DWA RZĘDY PO TRZY, NIE JEDEN PASEK SZEŚCIU — i to nie jest kwestia gustu. `Program.Render`
        //   pokazuje okno z `SizeToContent`, a okno NIE MOŻE być szersze niż ekran: pierwsza wersja miała
        //   sześć kolumn w linii (1792 px logicznych) i szósty kandydat został po cichu ODCIĘTY, mimo że
        //   limit `Measure` (3000) był z zapasem. Render wyglądał poprawnie i odpowiadał na inne pytanie,
        //   niż zadano — ten sam rodzaj cichej awarii co brakujący słownik zasobów.
        var outer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 20,
            Margin = new Thickness(16),
        };
        outer.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        outer.Children.Add(Row(
            Column("W0 · dziś", null),
            Column("W1 · istniejąca stała ⚠", Hint(
                "No connections yet.\nClick “+ New Connection” to add one.")),
            Column("W2 · nazwa zgodna z tooltipem", Hint(
                "No connections yet.\nUse New Connection (+) in the toolbar above."))));

        outer.Children.Add(Row(
            Column("W3 · najpierw krok", Hint(
                "Add a connection to get started.\nNew Connection (+) is in the toolbar above.")),
            Column("W4 · krok + glif", HintWithGlyph()),
            Column("W5 · krok + przycisk ⛔", HintWithButton())));

        return outer;
    }

    private static Control Row(params Control[] columns)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var c in columns)
        {
            row.Children.Add(c);
        }

        return row;
    }

    /// <summary>
    /// Jedna kolumna = wierny wycinek okna: pasek tytułu z PRAWDZIWYMI przyciskami (żeby było widać, gdzie
    /// jest „+" względem komunikatu, który go wskazuje) nad paskiem bocznym o rzeczywistej szerokości.
    /// </summary>
    private static Control Column(string caption, Control? content)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };

        var label = new TextBlock
        {
            Text = caption,
            FontSize = 11,
            Margin = new Thickness(2, 0, 2, 0),
        };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        stack.Children.Add(label);

        var frame = new StackPanel { Orientation = Orientation.Vertical, Width = SidebarWidth };

        // ── Pasek tytułu (wycinek) — chroma z prawdziwymi `Button.icon`. ──────────────────────────
        var chrome = new Border { Padding = new Thickness(4, 4) };
        chrome.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var key in new[] { "Icon.Menu", "Icon.PanelLeft", "Icon.Plus", "Icon.FolderPlus", "Icon.Pencil" })
        {
            tools.Children.Add(ToolButton(key));
        }

        chrome.Child = tools;
        frame.Children.Add(chrome);

        // ── Pasek boczny: `PanelBrush`, pole filtra, potem obszar listy. ──────────────────────────
        var side = new Border();
        side.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PanelBrush"));

        var inner = new StackPanel { Orientation = Orientation.Vertical };

        var filterHost = new Border { Margin = new Thickness(8, 6, 8, 4) };
        var filter = new TextBox
        {
            Classes = { "search" },
            Watermark = "Filter objects…",
            Padding = new Thickness(6, 4),
        };
        filterHost.Child = filter;
        inner.Children.Add(filterHost);

        var listArea = new Panel { Height = ListHeight };
        if (content is not null)
        {
            listArea.Children.Add(content);
        }

        inner.Children.Add(listArea);
        side.Child = inner;
        frame.Children.Add(side);

        stack.Children.Add(frame);
        return stack;
    }

    private static Control ToolButton(string geometryKey)
    {
        var button = new Button { Classes = { "icon" } };
        var icon = new SvgIcon();
        // ⭐ Geometria z zasobów aplikacji po kluczu — ten sam kształt, który maluje produkt.
        if (Application.Current!.Resources.TryGetResource(geometryKey, Application.Current.ActualThemeVariant, out var g)
            && g is Geometry geometry)
        {
            icon.Data = geometry;
        }

        button.Content = icon;
        return button;
    }

    /// <summary>
    /// Wspólny kształt komunikatu: `SubtleForegroundBrush`, rola tekstu aplikacji, zawijanie, wyśrodkowany
    /// w obszarze listy — czyli dokładnie ten sam język, którym mówią stany puste Session Managera i Trace'a.
    /// </summary>
    private static Control Hint(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0),
        };
        block.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        block.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Application.Size"));
        return block;
    }

    private static Control HintWithGlyph()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0),
        };

        stack.Children.Add(Hint("Add a connection to get started."));

        // Wiersz „glif + nazwa" — użytkownik dopasowuje KSZTAŁT, nie słowo.
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var icon = new SvgIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current!.Resources.TryGetResource("Icon.Plus", Application.Current.ActualThemeVariant, out var g)
            && g is Geometry geometry)
        {
            icon.Data = geometry;
        }

        icon.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,
            new DynamicResourceExtension("SubtleForegroundBrush"));
        line.Children.Add(icon);

        var caption = new TextBlock
        {
            Text = "New Connection — in the toolbar above",
            VerticalAlignment = VerticalAlignment.Center,
        };
        caption.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
        caption.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Caption.Size"));
        line.Children.Add(caption);

        stack.Children.Add(line);
        return stack;
    }

    private static Control HintWithButton()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0),
        };

        stack.Children.Add(Hint("Add a connection to get started."));

        var button = new Button
        {
            Classes = { "primary" },
            Content = "New Connection",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(button);
        return stack;
    }
}
