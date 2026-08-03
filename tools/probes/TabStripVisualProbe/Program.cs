// Product Polish M3.1a — sonda wizualna paska zakładek. Szczegóły i powód: .csproj.
//
// Uruchomienie:  dotnet run --project tools/probes/TabStripVisualProbe

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

internal static class Program
{
    // Realistyczny zestaw: nazwy z prefiksami (§8.1 — część różnicująca jest na KOŃCU), różne rodzaje
    // obiektów, jedna zakładka aktywna, jedna bez przycisku zamykania.
    private static readonly (string Name, string IconKey, string ColorKey, bool Active, bool Closable)[] Tabs =
    [
        ("ORDERS",                   "Icon.Table",     "IconColor_Table",     false, true),
        ("SP_ADD_ORDER",             "Icon.Procedure", "IconColor_Procedure", true,  true),
        ("XXX_GG_WYSTCECHKART_AU99", "Icon.Trigger",   "IconColor_Trigger",   false, true),
        ("V_ORDER_SUMMARY",          "Icon.View",      "IconColor_View",      false, true),
        ("FN_ADD_TAX",               "Icon.Function",  "IconColor_Function",  false, true),
    ];

    /// <summary>
    /// Przełącznik dowodowy §19.2: czy wskaźnik aktywnej zakładki niesie LOKALNE `Background="Transparent"`.
    /// Wartość lokalna bije setter stylu, więc przy `true` akcent nie ma szans się namalować.
    /// </summary>
    private static bool LocalTransparentOnIndicator =
        Environment.GetEnvironmentVariable("PROBE_LOCAL_TRANSPARENT") == "1";

    public static void Main()
    {
        var app = AppBuilder.Configure<ProbeApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .SetupWithoutStarting();

        var outDir = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
        Directory.CreateDirectory(outDir);

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Application.Current!.Resources["Size.Row.Tab"] = 26d;

            var strip = BuildStrip();
            var file = System.IO.Path.Combine(outDir, $"tabstrip-{variant}-26px{(LocalTransparentOnIndicator ? "-LOCAL" : "-FIXED")}.png");
            Render(strip, file);
            Console.WriteLine($"{file}");

            // ── M3.3b — dwa tryby paska (§8.2) ────────────────────────────────────────────────────────
            // ⭐ To jest pytanie, które §19.1.5 PRZEWIDZIAŁO przy zakładaniu tej sondy („wróci w M3.3").
            //   Pasek zakładek jest pusty bez połączenia z bazą, a sesja headless nie renderuje, więc bez
            //   sondy oba tryby byłyby nieoglądalne aż do ręcznego QA na żywej bazie.
            // ⚠ WIERNIE JAK W PRODUKCIE: tryb robią wyłącznie kierunki przewijania `ScrollViewera`, a
            //   `MaxHeight` jest iloczynem roli i preferencji — dokładnie tak liczy je `ApplyTabStripMode`.
            foreach (var (mode, maxRows) in new[] { ("MultiRow", 3), ("SingleRow", 0) })
            {
                var wide = BuildModeStrip(multiRow: mode == "MultiRow", maxRows: maxRows);
                var modeFile = System.IO.Path.Combine(outDir, $"tabstrip-{variant}-{mode}.png");
                Render(wide, modeFile);
                Console.WriteLine($"{modeFile}");
            }
        }

        Console.WriteLine("OK");
    }

    // Pasek z liczbą zakładek, która NIE MIEŚCI SIĘ w jednym wierszu — bo dopiero wtedy oba tryby zaczynają
    // się różnić i dopiero wtedy jest co oglądać.
    private static readonly (string Name, string IconKey, string ColorKey, bool Active, bool Closable)[] ManyTabs =
    [
        ("ORDERS",                   "Icon.Table",     "IconColor_Table",     false, true),
        ("SP_ADD_ORDER",             "Icon.Procedure", "IconColor_Procedure", true,  true),
        ("XXX_GG_WYSTCECHKART_AU99", "Icon.Trigger",   "IconColor_Trigger",   false, true),
        ("XXX_GG_WYSTCECHKART_BU99", "Icon.Trigger",   "IconColor_Trigger",   false, true),
        ("V_ORDER_SUMMARY",          "Icon.View",      "IconColor_View",      false, true),
        ("FN_ADD_TAX",               "Icon.Function",  "IconColor_Function",  false, true),
        ("CUSTOMERS",                "Icon.Table",     "IconColor_Table",     false, true),
        ("SP_RECALC_TOTALS",         "Icon.Procedure", "IconColor_Procedure", false, true),
        ("V_STOCK_LEVELS",           "Icon.View",      "IconColor_View",      false, true),
        ("FN_FULL_LABEL",            "Icon.Function",  "IconColor_Function",  false, true),
        ("INVOICE_LINES",            "Icon.Table",     "IconColor_Table",     false, true),
        ("SP_DBG_SUMMARY",           "Icon.Procedure", "IconColor_Procedure", false, true),
    ];

    private static Control BuildModeStrip(bool multiRow, int maxRows)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, iconKey, colorKey, active, closable) in ManyTabs)
            wrap.Children.Add(BuildTab(name, iconKey, colorKey, active, closable));

        var items = new ItemsControl { ItemsSource = null };
        var scroll = new ScrollViewer { Content = wrap };

        if (multiRow)
        {
            // Szerokość ograniczona ⇒ WrapPanel zawija ⇒ wiele wierszy; pionowo przewija się SAM PASEK.
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.MaxHeight = (double)Application.Current!.Resources["Size.Row.Tab"]! * maxRows;
        }
        else
        {
            // Szerokość nieskończona ⇒ WrapPanel nie zawija nigdy ⇒ jeden wiersz.
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(scroll, 0);
        grid.Children.Add(scroll);

        if (!multiRow)
        {
            // Przycisk przepełnienia z licznikiem NIEWIDOCZNYCH zakładek. W sondzie liczba jest wpisana
            // (sonda nie mierzy viewportu), w produkcie liczy ją `UpdateTabOverflow` z rzeczywistego układu.
            var overflow = new EmberTern.App.Controls.SearchableComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0),
                MinWidth = 0,
                SelectionBoxText = "5",
            };
            Grid.SetColumn(overflow, 1);
            grid.Children.Add(overflow);
        }

        var strip = new Border { Classes = { "tab-strip" }, Child = grid };
        strip.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PanelBrush"));
        strip.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        strip.BorderThickness = new Thickness(0, 0, 0, 1);

        var body = new Border { Height = 26 };
        body.Bind(Border.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        var root = new StackPanel { Orientation = Orientation.Vertical, Width = 620 };
        root.Children.Add(strip);
        root.Children.Add(body);
        return root;
    }

    private static Control BuildStrip()
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach (var (name, iconKey, colorKey, active, closable) in Tabs)
            wrap.Children.Add(BuildTab(name, iconKey, colorKey, active, closable));

        // Border.tab-strip — kontener; reguła w ControlStyles.axaml zdejmuje geometrię akcji z `Button.flat`.
        var strip = new Border { Classes = { "tab-strip" }, Child = wrap };
        strip.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PanelBrush"));
        strip.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        strip.BorderThickness = new Thickness(0, 0, 0, 1);

        // Odrobina obszaru roboczego pod paskiem — bez niej krawędź dolna nie ma się od czego odciąć.
        var body = new Border { Height = 26 };
        body.Bind(Border.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        var root = new StackPanel { Orientation = Orientation.Vertical, Width = 760 };
        root.Children.Add(strip);
        root.Children.Add(body);
        return root;
    }

    private static Control BuildTab(string name, string iconKey, string colorKey, bool active, bool closable)
    {
        // ⚠⚠ M3.3a — SONDA ODTWARZA MECHANIZM, NIE WYNIK. Wszystko, co w produkcie pochodzi ze stylu,
        //    musi tu pochodzić ze stylu; wszystko, co jest rolą, musi być rolą. Sonda, która wiąże wynik
        //    bezpośrednio, potwierdza stan, którego może nie być — to jest dokładnie ten błąd, który
        //    kosztował §19.2 (pułapka 12) i który po tej iteracji byłby jeszcze łatwiejszy do popełnienia,
        //    bo tła kafelka i wagi etykiety nie ustawia już nic w widoku.
        var icon = new EmberTern.App.Controls.SvgIcon { VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(Layoutable.WidthProperty, new DynamicResourceExtension("Size.Icon"));
        icon.Bind(Layoutable.HeightProperty, new DynamicResourceExtension("Size.Icon"));
        if (Application.Current!.TryFindResource(iconKey, out var geometry) && geometry is Geometry g)
            icon.Data = g;
        icon.Bind(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(colorKey));

        // ⛔ Waga i kontrast etykiety aktywnej NIE są tu ustawiane — niesie je styl
        //    `Border.workspace-tab.active-tab TextBlock`.
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Compact.Size"));

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Bind(StackPanel.SpacingProperty, new DynamicResourceExtension("Space.Sm"));
        content.Children.Add(icon);
        content.Children.Add(label);

        // ⛔ Bez `Background` i bez `BorderThickness` — pierwsze daje `Button.flat`, drugie reguła
        //    kontenera `Border.tab-strip Button.flat`. Wiernie jak w szablonie.
        var activate = new Button
        {
            Classes = { "flat" },
            Padding = new Thickness(8, 4),
            Content = content,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        row.Children.Add(activate);

        if (closable)
        {
            var x = new EmberTern.App.Controls.SvgIcon();
            x.Bind(Layoutable.WidthProperty, new DynamicResourceExtension("Size.Icon.Sm"));
            x.Bind(Layoutable.HeightProperty, new DynamicResourceExtension("Size.Icon.Sm"));
            if (Application.Current!.TryFindResource("Icon.X", out var xg) && xg is Geometry xgeom)
                x.Data = xgeom;

            // ⚠ `BorderThickness` ZOSTAJE — w szablonie też zostaje, bo `Button.icon` go nie ustawia
            //   (w odróżnieniu od `Button.flat`). `Background` zniknęło po obu stronach.
            row.Children.Add(new Button
            {
                Classes = { "icon" },
                Padding = new Thickness(4, 2),
                Margin = new Thickness(0, 0, 3, 0),
                BorderThickness = new Thickness(0),
                Content = x,
            });
        }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        // ⚠⚠ WIERNIE JAK W XAML: wskaźnik NIE dostaje tła bezpośrednio. Klasę `active-tab` niesie RODZIC,
        //    a akcent maluje styl instancyjny (poniżej). Pierwsza wersja tej sondy wiązała tło wprost dla
        //    zakładki aktywnej i przez to MIERZYŁA INNY MECHANIZM niż produkt — obraz wychodził poprawny,
        //    mimo że w aplikacji wskaźnik się nie malował (§19.2, pułapka 12).
        var indicator = new Border { Classes = { "tab-indicator" } };
        if (LocalTransparentOnIndicator)
            indicator.Background = Brushes.Transparent;
        indicator.Bind(Layoutable.HeightProperty, new DynamicResourceExtension("Size.TabIndicator"));

        Grid.SetRow(indicator, 0);
        Grid.SetRow(row, 1);
        grid.Children.Add(indicator);
        grid.Children.Add(row);

        // ⛔⛔ ŻADNEGO `Background` — i to jest po M3.3a najważniejsza linia tej sondy. Kafelek bierze tło
        //    ze stylu `Border.workspace-tab` (spoczynek) albo `.workspace-tab.active-tab` (aktywna), więc
        //    związanie go tutaj wprost — jak robiła poprzednia wersja — sprawiłoby, że obraz jest poprawny
        //    NIEZALEŻNIE od tego, czy styl w produkcie działa. Iteracja zmierzyła, że przy wartości lokalnej
        //    właśnie NIE działa, więc to nie jest ryzyko teoretyczne.
        var tab = new Border
        {
            Classes = { "workspace-tab" },
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = grid,
        };
        tab.Bind(Layoutable.MinHeightProperty, new DynamicResourceExtension("Size.Row.Tab"));
        tab.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));

        // ⛔ Bez stylu instancyjnego — po M3.3a KOMPLET reguł zakładki aktywnej (tło kafelka, etykieta,
        //    wskaźnik) mieszka w `ControlStyles.axaml`, czyli w stylach aplikacji, które ta sonda ładuje.
        //    Wystarczy klasa stanu; resztę robi produkt.
        if (active)
            tab.Classes.Add("active-tab");

        return tab;
    }

    private static void Render(Control root, string path)
    {
        // ⚠ Kontrolka musi wisieć na TopLevelu, inaczej style aplikacji do niej nie dojdą i render pokaże
        //   gołego Fluenta. Okno nie jest pokazywane — wystarczy, że istnieje jako korzeń drzewa.
        var window = new Window { Content = root, ShowInTaskbar = false };
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);

        root.Measure(new Size(1000, 400));
        root.Arrange(new Rect(root.DesiredSize));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var size = root.Bounds.Size;
        var scale = 2.0; // 2x — żeby 4 px różnicy dało się ocenić na ekranie
        var bmp = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bmp.Render(root);
        using var stream = File.Create(path);
        bmp.Save(stream);
        window.Close();
    }
}

/// <summary>Minimalna aplikacja ładująca DOKŁADNIE te same słowniki i style co `App.axaml`.</summary>
internal sealed class ProbeApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;

        foreach (var source in new[]
                 {
                     "avares://EmberTern/Themes/Tokens.axaml",
                     "avares://EmberTern/Themes/Typography.axaml",
                     "avares://EmberTern/Themes/Colors.axaml",
                     "avares://EmberTern/Themes/FluentBridge.axaml",
                     "avares://EmberTern/Themes/IconGeometries.axaml",
                     "avares://EmberTern/Themes/ControlThemes.axaml",
                     // ⚠ M3.3b — DOŁOŻONE PO POMIARZE, NIE Z OSTROŻNOŚCI. Przycisk przepełnienia jest
                     //   `SearchableComboBox`em, a bez tego słownika kontrolka nie ma ControlTheme, więc
                     //   nie ma szablonu i renderuje się jako NIC. Render wychodził „poprawny" — po prostu
                     //   bez przycisku — czyli sonda znów pokazywałaby stan, którego nie ma (§19.2).
                     //   ⭐ Reguła: sonda musi ładować te same słowniki co `App.axaml`; brakujący słownik
                     //   nie zawodzi, tylko po cichu usuwa element z obrazu.
                     "avares://EmberTern/Themes/SearchableComboBox.axaml",
                     "avares://EmberTern/Themes/PickerTemplates.axaml",
                 })
        {
            Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null) { Source = new Uri(source) });
        }

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://EmberTern/Themes/ControlStyles.axaml") });
    }
}
