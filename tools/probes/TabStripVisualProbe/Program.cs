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
            foreach (var rowHeight in new[] { 30d, 26d })
            {
                Application.Current!.RequestedThemeVariant = variant;
                Application.Current!.Resources["Size.Row.Tab"] = rowHeight;

                var strip = BuildStrip();
                var file = System.IO.Path.Combine(outDir, $"tabstrip-{variant}-{rowHeight:0}px.png");
                Render(strip, file);
                Console.WriteLine($"{file}");
            }
        }

        Console.WriteLine("OK");
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
        var icon = new EmberTern.App.Controls.SvgIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current!.TryFindResource(iconKey, out var geometry) && geometry is Geometry g)
            icon.Data = g;
        icon.Bind(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(colorKey));

        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("Text.Compact.Size"));
        if (active)
        {
            label.FontWeight = FontWeight.SemiBold;
            label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("ForegroundBrush"));
        }

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(label);

        var activate = new Button
        {
            Classes = { "flat" },
            Padding = new Thickness(8, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = content,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        row.Children.Add(activate);

        if (closable)
        {
            var x = new EmberTern.App.Controls.SvgIcon { Width = 12, Height = 12 };
            if (Application.Current!.TryFindResource("Icon.X", out var xg) && xg is Geometry xgeom)
                x.Data = xgeom;

            row.Children.Add(new Button
            {
                Classes = { "icon" },
                Padding = new Thickness(4, 2),
                Margin = new Thickness(0, 0, 3, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = x,
            });
        }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        var indicator = new Border { Classes = { "tab-indicator" }, Background = Brushes.Transparent };
        indicator.Bind(Layoutable.HeightProperty, new DynamicResourceExtension("Size.TabIndicator"));
        if (active)
            indicator.Bind(Border.BackgroundProperty, new DynamicResourceExtension("AccentBrush"));

        Grid.SetRow(indicator, 0);
        Grid.SetRow(row, 1);
        grid.Children.Add(indicator);
        grid.Children.Add(row);

        var tab = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
        tab.Bind(Layoutable.MinHeightProperty, new DynamicResourceExtension("Size.Row.Tab"));
        tab.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        tab.Bind(Border.BackgroundProperty, new DynamicResourceExtension(active ? "BackgroundBrush" : "PanelBrush"));
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
                 })
        {
            Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null) { Source = new Uri(source) });
        }

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://EmberTern/Themes/ControlStyles.axaml") });
    }
}
