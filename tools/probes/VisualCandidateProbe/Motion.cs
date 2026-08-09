// Product Polish M5 / §9 — POMIAR RUCHU. Co naprawdę animuje się w aplikacji.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- motion
//
// ─── PO CO TO ISTNIEJE ───────────────────────────────────────────────────────────────────────────
//
// Licznik na źródłach mówi, że `EmberTern.App` ma ZERO `Transitions`, zero `<Animation>` i zero
// `Storyboard`. ⚠⚠ To jednak odpowiada wyłącznie na pytanie „czego MY nie napisaliśmy", a §9 zakazuje
// przejść na właściwościach UKŁADU niezależnie od tego, kto je wniósł — i sam §9 pisze, że „Fluent wnosi
// własne przejścia". Szablonów Fluenta nie da się przeczytać z pakietu (są skompilowane), więc jedyny
// uczciwy pomiar to odczyt z ELEMENTU, KTÓRY MALUJE: zrealizować kontrolki i przejść ich drzewo wizualne
// razem z wnętrzem szablonów (#345 — mierz to, co maluje, nie deklarację).
//
// ⚠ Kontrolki muszą być ZREALIZOWANE (Measure + Arrange w prawdziwym oknie), bo `Transitions` ustawia
//   `ControlTheme`, a ten stosuje się dopiero przy aplikowaniu szablonu. Odczyt z niezrealizowanej
//   instancji zwróciłby null i „udowodnił" brak przejść.
//
// ⚠ Mierzone w OBU MOTYWACH, bo `ControlTheme` rozwiązuje się przez wariant motywu — gdyby któryś wariant
//   niósł inny zestaw, pomiar w jednym byłby odpowiedzią na inne pytanie.
//
// ⛔ To jest WYŁĄCZNIE pomiar. Sonda niczego nie zmienia w produkcie.

using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

internal static class Motion
{
    /// <summary>
    /// Właściwości zakazane przez §9 — „każde przejście na właściwości wpływającej na układ".
    /// ⚠ Lista jest SZERSZA niż cytat z §9 (`Width`/`Height`/`Margin`/`Padding`), bo zakaz jest sformułowany
    /// przez SKUTEK, a nie przez nazwy: `MinWidth`, `MaxHeight`, `BorderThickness` czy `FontSize` też
    /// przesuwają sąsiadów. Reguła #11 z CLAUDE.md — formułuj pozytywnie to, czym rzecz JEST; tutaj rzeczą
    /// jest „wpływa na desired size".
    /// </summary>
    private static readonly string[] LayoutAffecting =
    [
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "Margin", "Padding", "BorderThickness", "FontSize", "Spacing",
    ];

    public static void Run(string outDir)
    {
        var report = new StringBuilder();
        report.AppendLine("§9 — POMIAR PRZEJŚĆ NA ZREALIZOWANYCH KONTROLKACH");
        report.AppendLine(new string('=', 96));

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            report.AppendLine();
            report.AppendLine($"── MOTYW: {variant} ──────────────────────────────────────────────────────");

            var root = BuildSpecimens();
            var window = new Window
            {
                Content = root,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            window.Show();
            window.Position = new PixelPoint(-4000, -4000);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            root.Measure(new Size(1600, 3000));
            root.Arrange(new Rect(root.DesiredSize));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // ⚠⚠ POWIERZCHNIE W POPUPACH NIE LEŻĄ W DRZEWIE OKNA — lista `ComboBox`a, podmenu i tooltip
            //    dostają własny `PopupRoot`. Pominięcie ich dałoby wynik „zmierzone wszystko", a naprawdę
            //    ominęłoby dokładnie te powierzchnie, na których frameworki najczęściej animują pojawianie.
            //    Otwieramy więc, co da się otworzyć, i przechodzimy również korzenie popupów.
            var extraRoots = new List<Visual>();
            // ⚠ `.ToList()` PRZED otwieraniem: `Walk` jest leniwe, a otwarcie popupu MUTUJE drzewo w trakcie
            //   enumeracji — pierwszy przebieg wywrócił się na „Collection was modified".
            foreach (var combo in Walk(window).OfType<ComboBox>().ToList())
            {
                combo.IsDropDownOpen = true;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            foreach (var menuItem in Walk(window).OfType<MenuItem>().ToList())
            {
                menuItem.Open();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            foreach (var popup in Walk(window).OfType<Avalonia.Controls.Primitives.Popup>().ToList())
            {
                // ⚠ `Popup.Host` nie jest publiczne w Avalonii 12.1.1 — odczyt refleksją, jak przy `ITransition.Property`.
                if (Read(popup, "Host") is Visual host) extraRoots.Add(host);
            }

            var found = 0;
            var violations = 0;
            var all = Walk(window).Concat(extraRoots.SelectMany(Walk)).ToList();
            report.AppendLine($"  (przejrzano {all.Count} elementów, w tym {extraRoots.Count} korzeni popupów)");
            foreach (var visual in all)
            {
                if (visual is not Animatable animatable) continue;
                var transitions = animatable.Transitions;
                if (transitions is null || transitions.Count == 0) continue;

                found++;
                var owner = Describe(visual);
                foreach (var t in transitions)
                {
                    // ⚠ `ITransition.Property` NIE jest publiczne w Avalonii 12.1.1, a `Transition<T>` jest
                    //   generyczne po typie animowanej wartości, więc rzutowanie wymagałoby znajomości typu
                    //   z góry. Odczyt refleksją jest tu właściwym narzędziem: to sonda pomiarowa, a pytanie
                    //   brzmi „co framework naprawdę animuje", nie „co da się statycznie wyrazić".
                    var prop = Read(t, "Property") is AvaloniaProperty p ? p.Name : "(?)";
                    var kind = t.GetType().Name;
                    var duration = Read(t, "Duration") is TimeSpan d ? d : TimeSpan.Zero;
                    var easing = Read(t, "Easing")?.GetType().Name ?? "(?)";
                    var bad = Array.IndexOf(LayoutAffecting, prop) >= 0;
                    if (bad) violations++;
                    report.AppendLine(
                        $"  {(bad ? "⛔" : "  ")} {owner,-44} {prop,-20} {kind,-26} {duration.TotalMilliseconds,5:0} ms  {easing}");
                }
            }

            report.AppendLine();
            report.AppendLine($"  elementów z przejściami: {found}   ·   przejść na właściwości UKŁADU: {violations}");

            // ⭐⭐ Drugie pytanie, bez którego pierwszy wynik jest nie do zinterpretowania: czy przejście
            //    `RenderTransform` na przycisku COKOLWIEK animuje? Przejście bez settera, który zmienia
            //    wartość, jest BEZCZYNNE — a bezczynne przejście nie jest przedmiotem §9, tylko szumem
            //    w pomiarze. Sprawdzane przez wymuszenie pseudoklasy `:pressed` i odczyt wartości.
            report.AppendLine();
            report.AppendLine("  Czy `RenderTransform` przycisku faktycznie się zmienia w stanie wciśniętym?");
            foreach (var variantName in new[] { "", "icon", "flat", "primary", "caption", "seg" })
            {
                var probe = new Button { Content = "X" };
                if (variantName.Length > 0) probe.Classes.Add(variantName);
                var host = new Window { Content = probe, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight };
                host.Show();
                host.Position = new PixelPoint(-4000, -4000);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var rest = Matrix(probe);
                ((Avalonia.Controls.IPseudoClasses)probe.Classes).Set(":pressed", true);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                probe.Measure(new Size(400, 200));
                probe.Arrange(new Rect(probe.DesiredSize));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var pressed = Matrix(probe);

                report.AppendLine($"    Button{(variantName.Length > 0 ? "." + variantName : " (goły)"),-14} spoczynek={rest,-46} wciśnięty={pressed}");
                host.Close();
            }

            window.Close();
        }

        var file = Path.Combine(outDir, "m5-motion.txt");
        File.WriteAllText(file, report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine(file);
    }

    /// <summary>⚠ `ToString()` na `TransformOperations` zwraca samą nazwę typu — pierwsza wersja pomiaru
    /// „pokazała", że stan spoczynku i wciśnięcia są identyczne, bo porównywała dwa razy tę samą etykietę.
    /// Wartość niesie dopiero macierz.</summary>
    private static string Matrix(Control c)
        => c.RenderTransform is Avalonia.Media.Transformation.TransformOperations ops
            ? ops.Value.ToString()
            : c.RenderTransform?.ToString() ?? "(null)";

    private static object? Read(object target, string propertyName)
        => target.GetType().GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)?.GetValue(target);

    private static string Describe(Visual v)
    {
        var name = v.GetType().Name;
        if (v is StyledElement { Name: { } n } && n.Length > 0) name += $"#{n}";
        // Nazwa najbliższego przodka-kontrolki, żeby wiadomo było, CO to jest w produkcie.
        var host = v.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Tag is string);
        if (host?.Tag is string tag) name = $"{tag} → {name}";
        return name;
    }

    private static IEnumerable<Visual> Walk(Visual root)
    {
        yield return root;
        foreach (var child in root.GetVisualChildren())
        {
            foreach (var d in Walk(child)) yield return d;
        }
    }

    /// <summary>
    /// Po jednej sztuce każdej kontrolki, której aplikacja NAPRAWDĘ używa — plus warianty przycisku
    /// z `ControlStyles.axaml`, bo to one nadpisują chromę Fluenta.
    /// ⚠ `Tag` niesie nazwę okazu, żeby raport mówił „Button.icon → Border", a nie samo „Border".
    /// </summary>
    private static Control BuildSpecimens()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Margin = new Thickness(8) };

        void Add(string tag, Control c)
        {
            c.Tag = tag;
            panel.Children.Add(c);
        }

        foreach (var variant in new[] { "icon", "flat", "primary", "caption", "seg" })
        {
            Add($"Button.{variant}", new Button { Classes = { variant }, Content = "X" });
        }

        Add("Button (bez klasy)", new Button { Content = "X" });
        Add("ToggleButton.icon", new ToggleButton { Classes = { "icon" }, Content = "X" });
        Add("TextBox", new TextBox { Text = "x" });
        Add("TextBox.search", new TextBox { Classes = { "search" }, Text = "x" });
        Add("CheckBox", new CheckBox { Content = "x" });
        Add("RadioButton", new RadioButton { Content = "x" });
        Add("ComboBox", new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 });
        Add("Slider", new Slider { Minimum = 0, Maximum = 10, Value = 5, Width = 120 });
        Add("ProgressBar", new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, Width = 120 });
        Add("Expander", new Expander { Header = "h", Content = new TextBlock { Text = "c" }, IsExpanded = true });
        Add("ScrollViewer", new ScrollViewer
        {
            Height = 40,
            Content = new StackPanel { Children = { new TextBlock { Text = "a" }, new TextBlock { Text = "b" } } },
        });
        Add("ListBox", new ListBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0, Height = 50 });
        Add("TreeView", new TreeView { ItemsSource = new[] { "a", "b" }, Height = 50 });
        Add("TabControl", new TabControl
        {
            Items =
            {
                new TabItem { Header = "t1", Content = new TextBlock { Text = "x" } },
                new TabItem { Classes = { "bottom-tab" }, Header = "t2", Content = new TextBlock { Text = "y" } },
            },
        });
        Add("Menu/MenuItem", new Menu
        {
            Items = { new MenuItem { Header = "m", Items = { new MenuItem { Header = "sub" } } } },
        });
        Add("GridSplitter", new GridSplitter { Width = 4, Height = 20 });
        Add("NumericUpDown", new NumericUpDown { Value = 1, Width = 120 });
        Add("DataGrid", new DataGrid
        {
            Height = 60,
            Width = 240,
            ItemsSource = new[] { new { A = 1 }, new { A = 2 } },
        });

        return panel;
    }
}
