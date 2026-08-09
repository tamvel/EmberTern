// QA dla pakietu 1–3 (2026-08-09): ikona zakładki Security · stany `Button.primary` · podgląd Live DDL.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- qa123
//
// ⭐ W ODRÓŻNIENIU OD POZOSTAŁYCH MODUŁÓW TEJ SONDY, TO NIE SĄ KANDYDACI — to render STANU WDROŻONEGO
//   obok stanu sprzed zmiany. Kolumna „przed" jest odtworzona wartościami lokalnymi z POMIARU (headless,
//   oba motywy, cztery stany); kolumna „po" bierze wygląd z prawdziwych stylów aplikacji, więc pokazuje
//   to, co realnie zobaczy użytkownik.
//
// ⚠ Granica, którą trzeba znać czytając obrazek: `Button.primary` w stanie najechania/wciśnięcia nie da się
//   wymusić w renderze (pseudoklasy ustawia tylko sama kontrolka), więc OBIE kolumny tego wiersza są
//   odtworzone wartościami lokalnymi. Liczby pod nimi pochodzą z pomiaru na prawdziwej kontrolce, nie z tego
//   renderu — render pokazuje BARWY, pomiar dowodzi, że produkt je przyjmuje.
//
// ⚠ Podgląd DDL jest za to PRAWDZIWY: `TextEditor` z tą samą definicją XSHD, którą wpina
//   `SqlEditorBehavior.AttachReadOnlyPreview`. W dialogach warstwa SEMANTYCZNA jest bezczynna (jej metadane
//   płyną z `MainWindowViewModel`, a DataContextem okna dialogowego jest jego własny VM), więc render
//   pokazuje dokładnie tyle koloru, ile dostanie dialog — ani mniej, ani więcej.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Controls;

internal static class Qa123
{
    private const string SampleDdl =
        "CREATE TABLE ZAMOWIENIA (\n" +
        "    ID          INTEGER NOT NULL,\n" +
        "    NR_DOKUMENTU VARCHAR(32) NOT NULL,\n" +
        "    DATA_WYST   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,\n" +
        "    KWOTA       NUMERIC(15,2),  /* brutto */\n" +
        "    STATUS      D_STATUS,\n" +
        "    CONSTRAINT PK_ZAMOWIENIA PRIMARY KEY (ID)\n" +
        ");";

    public static void Run(string outDir)
    {
        // ⚠⚠ ZNALEZIONE PRZEZ PIERWSZY RENDER, nie przez czytanie kodu: definicje XSHD rejestruje `App`
        //   (`RegisterFirebirdSyntax`), a sonda uruchamia `ProbeApp` — więc `GetDefinition` zwracało null
        //   i „PO" wyglądało DOKŁADNIE tak samo jak „PRZED". To ta sama cicha awaria co brakujący słownik
        //   zasobów (§19.23.7), tylko o warstwę dalej: brak REJESTRACJI nie zawodzi, po cichu zabiera kolor,
        //   a obrazek wygląda wiarygodnie i odpowiada na inne pytanie, niż zadano.
        RegisterFirebirdSyntax();

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            var file = Path.Combine(outDir, $"qa123-{variant}.png");
            Program.Render(Build(variant), file, scale: 1.6);
            Console.WriteLine(file);
        }
    }

    private static Control Build(ThemeVariant variant)
    {
        var root = new StackPanel { Spacing = 22, Margin = new Thickness(22) };

        root.Children.Add(Heading($"Pakiet 1–3 — {variant}", 17));

        // ─── 1. Ikona zakładki Security Manager ───────────────────────────────────────────────────
        root.Children.Add(Heading("1 · Ikona zakładki Security Manager", 13));
        root.Children.Add(Row(
            ("PRZED  (IconColor_Role)", TabChip("Icon.Role", Brush("IconColor_Role"), "Security Manager")),
            ("PO  (AccentBrush)", TabChip("Icon.Role", Brush("AccentBrush"), "Security Manager")),
            ("dla porównania: Data Import", TabChip("Icon.Import", Brush("AccentBrush"), "Data Import"))));
        root.Children.Add(Note("Glif zostaje zależny od kontekstu (User/Role) — zmienia się wyłącznie barwa."));

        // ─── 2. Button.primary — stany ────────────────────────────────────────────────────────────
        root.Children.Add(Heading("2 · Button.primary — tekst w stanach (treść = zwykły string)", 13));

        var accent = Color("AccentColor");
        var accentMuted = Color("AccentMutedColor");
        var accentDisabled = Color("AccentDisabledColor");
        var fluentFg = Color("ForegroundColor");              // to, co malował ContentPresenter
        var onAccent = Color("OnAccentColor");
        var onAccentDisabled = Color("OnAccentDisabledColor");
        var fluentDisabledFg = Color("SubtleForegroundColor");

        root.Children.Add(Row(
            ("spoczynek (bez zmian)", FakeButton("Zapisz", accent, onAccent)),
            ("PRZED · najechanie", FakeButton("Zapisz", accentMuted, fluentFg)),
            ("PO · najechanie", FakeButton("Zapisz", accentMuted, onAccent))));
        root.Children.Add(Row(
            ("PRZED · wciśnięcie", FakeButton("Zapisz", accentMuted, fluentFg)),
            ("PO · wciśnięcie", FakeButton("Zapisz", accentMuted, onAccent)),
            ("", new Border())));
        root.Children.Add(Row(
            ("PRZED · nieaktywny", FakeButton("Zapisz", accentDisabled, fluentDisabledFg)),
            ("PO · nieaktywny", FakeButton("Zapisz", accentDisabled, onAccentDisabled)),
            ("PO · z jawnym TextBlock", RealPrimary("Zapisz"))));
        root.Children.Add(Note(
            "Zmierzone na prawdziwej kontrolce: Light/najechanie 2,04:1 → 8,33:1. Dark przechodził próg "
            + "PRZYPADKIEM (5,62:1), bo ForegroundColor jest tam prawie biały — mechanizm był zepsuty w obu motywach."));

        // ─── 3. Live DDL ──────────────────────────────────────────────────────────────────────────
        root.Children.Add(Heading("3 · Podgląd Live DDL", 13));
        root.Children.Add(Row(
            ("PRZED  (read-only TextBox)", PlainPreview()),
            ("PO  (wspólna powierzchnia SQL)", HighlightedPreview(variant))));
        root.Children.Add(Note(
            "Warstwa leksykalna (XSHD) — dokładnie tyle koloru, ile dostaną cztery dialogi. W New Table, "
            + "który żyje w MainWindow, dochodzi jeszcze warstwa semantyczna (akcenty obiektów)."));

        return root;
    }

    /// <summary>Ta sama rejestracja, którą robi `App.RegisterFirebirdSyntax` — powtórzona, bo sonda nie
    /// uruchamia `App`. ⛔ Nie „uprościć" jej przez usunięcie: bez niej render pokazuje czysty tekst.</summary>
    private static void RegisterFirebirdSyntax()
    {
        foreach (var (name, uri) in new[]
                 {
                     ("Firebird SQL", "avares://EmberTern/Assets/FirebirdSql.xshd"),
                     ("Firebird SQL Light", "avares://EmberTern/Assets/FirebirdSql.Light.xshd"),
                 })
        {
            if (HighlightingManager.Instance.GetDefinition(name) is not null) continue;

            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(uri));
            using var reader = System.Xml.XmlReader.Create(stream);
            var definition = AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(name, new[] { ".sql" }, definition);
        }
    }

    // ─── elementy ────────────────────────────────────────────────────────────────────────────────

    private static Control TabChip(string geometryKey, IBrush iconBrush, string title)
        => new Border
        {
            Background = Brush("ChromeStrongBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SvgIcon { Data = Geometry(geometryKey), Width = 14, Height = 14, Foreground = iconBrush,
                                  VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = title, FontSize = 12, Foreground = Brush("ForegroundBrush"),
                                    VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

    /// <summary>Przycisk odtworzony wartościami lokalnymi — jedyny sposób pokazania stanu, którego render
    /// nie umie wymusić. ⚠ To jest REKONSTRUKCJA barw, nie dowód; dowodem jest pomiar headless.</summary>
    private static Control FakeButton(string label, Color fill, Color text)
        => new Border
        {
            Background = new SolidColorBrush(fill),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12, 5),
            MinWidth = 100,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(text),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };

    private static Control RealPrimary(string label)
        => new Button { Classes = { "primary" }, Content = new TextBlock { Text = label } };

    private static Control PlainPreview()
        => new Border
        {
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Width = 330,
            Child = new TextBox
            {
                Text = SampleDdl,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
                FontSize = 11,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6),
                Background = Brush("BackgroundBrush"),
                Foreground = Brush("ForegroundBrush"),
            },
        };

    private static Control HighlightedPreview(ThemeVariant variant)
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            FontSize = 11,
            Padding = new Thickness(8, 6),
            Background = Brush("BackgroundBrush"),
            Foreground = Brush("ForegroundBrush"),
            Width = 330,
            Height = 150,
            Document = new AvaloniaEdit.Document.TextDocument(SampleDdl),
        };

        // Ta sama definicja, którą wpina AttachReadOnlyPreview dla bieżącego motywu.
        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(
            variant == ThemeVariant.Light ? "Firebird SQL Light" : "Firebird SQL");

        return new Border
        {
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = editor,
        };
    }

    // ─── układ ───────────────────────────────────────────────────────────────────────────────────

    private static Control Row(params (string Label, Control Content)[] cells)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var (label, content) in cells)
        {
            row.Children.Add(new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 10, Foreground = Brush("SubtleForegroundBrush") },
                    content,
                },
            });
        }
        return row;
    }

    private static Control Heading(string text, double size)
        => new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold,
                           Foreground = Brush("ForegroundBrush"), Margin = new Thickness(0, 6, 0, 0) };

    private static Control Note(string text)
        => new TextBlock { Text = text, FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 760,
                           Foreground = Brush("SubtleForegroundBrush") };

    private static IBrush Brush(string key)
        => Application.Current!.FindResource(Application.Current.ActualThemeVariant, key) as IBrush
           ?? Brushes.Magenta;

    private static Color Color(string key)
        => Application.Current!.FindResource(Application.Current.ActualThemeVariant, key) is Color c
            ? c
            : Colors.Magenta;

    private static Avalonia.Media.Geometry? Geometry(string key)
        => Application.Current!.FindResource(Application.Current.ActualThemeVariant, key) as Avalonia.Media.Geometry;
}
