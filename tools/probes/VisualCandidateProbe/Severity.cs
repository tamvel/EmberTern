// Product Polish M5 / §10 — materiał decyzyjny dla trzech barw severity.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- severity
//
// ─── POMIAR WEJŚCIOWY (WCAG 2.x, policzony na tokenach z Colors.axaml) ────────────────────────────
//
// `MessageBanner` maluje pasek (3 px), ikonę (14) ORAZ tekst komunikatu (12 px) TYM SAMYM pędzlem
// severity, a jego styl nadaje tło `PanelBrush` obu wariantom (standalone i `.docked`) — więc
// powierzchnia jest jedna, niezależnie od hosta.
//
//   motyw    severity   kontrast na PanelBrush   tekst 12 px (próg 4,5)   pasek+ikona (próg 3,0)
//   DARK     Error          4,26:1               PONIŻEJ o 0,24           OK
//   DARK     Warning        6,91:1               OK                       OK
//   DARK     Success        6,79:1               OK                       OK
//   DARK     Info           5,80:1               OK                       OK
//   LIGHT    Error          4,87:1               OK                       OK
//   LIGHT    Warning        3,12:1               PONIŻEJ o 1,38           OK
//   LIGHT    Success        3,88:1               PONIŻEJ o 0,62           OK
//   LIGHT    Info           5,33:1               OK                       OK
//
// ⭐⭐ ZNALEZISKO, KTÓRE ZMIENIA KSZTAŁT PYTANIA: pasek i ikona przechodzą próg 3:1 we WSZYSTKICH
//   ośmiu kombinacjach. Defekt dotyczy WYŁĄCZNIE tekstu — czyli sygnał severity (pasek + ikona) jest
//   poprawny wszędzie i nie zależy od barwy tekstu.
//
// ⭐⭐ I DRUGIE: to NIE JEST defekt `MessageBanner`. Zmierzone — `ErrorBrush` ma 30 konsumentów,
//   `WarningBrush` 36, `SuccessIconBrush` 25, a każdy z nich maluje tekst w ~8–13 miejscach poza
//   banerem (Script Executor, Batch Results, Data Import, Performance, Debugger). Baner był miejscem,
//   w którym defekt znaleziono, a nie jego zakresem. Poprawka lokalna w banerze zostawiłaby resztę —
//   to jest R7 („nie łatać pojedynczego ekranu, gdy defekt jest app-wide") w czystej postaci.
//
// ⚠⚠ PUŁAPKA, KTÓRĄ TRZEBA ZNAĆ PRZED WYBOREM WARIANTU: §10 opisuje wiersz „Tekst duży (≥ 14 px lub
//   ≥ 12 px SemiBold) → ≥ 3:1" jako „WCAG AA Large". To oznaczenie jest NIEPRAWDZIWE — WCAG 2.1
//   definiuje duży tekst jako 18 pt (24 px) albo 14 pt bold (18,7 px). Najwyższa rola typograficzna
//   EmberTerna to `Text.Display` = 23 px, więc ŻADNA rola się nie kwalifikuje. Wariant „zrób tekst
//   SemiBold i zejdź na próg 3:1" spełnia więc §10 JAK NAPISANE, ale nie spełnia WCAG AA. Wariant C
//   jest tu pokazany właśnie po to, żeby ta różnica była widoczna, a nie ukryta w tabeli.
//
// ⛔ KANDYDACI SĄ ZDEFINIOWANI TUTAJ, NIE W PRODUKCIE. Uruchomienie sondy niczego nie wdraża.
//
// ─── 🔒 DECYZJA ZAMKNIĘTA (2026-08-10): WARIANT B RATYFIKOWANY ────────────────────────────────────
//
// Użytkownik wybrał zmianę trzech wartości tokenów; weszła do `Themes/Colors.axaml`:
//   Light `WarningColor`     #C77800 → #A16100
//   Light `SuccessIconColor` #2E8B4F → #2A7E48
//   Dark  `ErrorColor`       #F44747 → #F55252
//
// ⚠ KOLUMNA „obecnie" W TABELI `Tokens` NIŻEJ TO STAN SPRZED M5 i jest tu zostawiona CELOWO — jako
//   zapis tego, przeciwko czemu zapadła decyzja. Uruchomienie sondy dziś porówna więc stan przedwdrożeniowy
//   z wdrożonym, a nie „obecny z kandydatem". ⛔ Nie „naprawiać" tego przez podmianę wartości na aktualne:
//   wtedy obie kolumny byłyby identyczne i render przestałby cokolwiek pokazywać.
//
// ⭐ Weryfikacja stanu WDROŻONEGO nie należy już do sondy — robi ją strażnik
//   `DesignTokenApplicationTests.SeverityText_*` / `SeveritySignal_*`, który czyta pędzle z żywych
//   zasobów i z elementu, który maluje. Sonda odpowiadała na „jak to WYGLĄDA", test odpowiada na
//   „czy to nadal trzyma próg".

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using EmberTern.App.Controls;

internal static class Severity
{
    /// <summary>
    /// Kandydaci policzeni PRZY PROGU 4,5:1 na `PanelBrush` (trudniejsza z dwóch powierzchni, bo
    /// `BackgroundBrush` daje w każdym przypadku o ~0,3 więcej).
    /// <para>⭐ Metoda: przyciemnianie przez MNOŻENIE kanałów RGB, co zachowuje odcień i nasycenie HSV
    /// DOKŁADNIE — kandydat jest tą samą barwą, tylko ciemniejszą. Dla Dark/Error trzeba w drugą stronę
    /// (mieszanie z bielą), co odcień zachowuje, ale ⚠ obniża nasycenie — koszt zapisany, bo widać go
    /// na renderze.</para>
    /// </summary>
    private static readonly (string Key, string CurrentDark, string CandDark, string CurrentLight, string CandLight)[] Tokens =
    [
        // Error:   Dark 4,26 → 4,53 (rozjaśnienie)   ·   Light 4,87 już OK, kandydat = bez zmiany
        ("ErrorBrush",       "#F44747", "#F55252", "#CC2929", "#CC2929"),
        // Warning: Dark 6,91 już OK                  ·   Light 3,12 → 4,52 (największa zmiana w całym zestawie)
        ("WarningBrush",     "#E8A020", "#E8A020", "#C77800", "#A16100"),
        // Success: Dark 6,79 już OK                  ·   Light 3,88 → 4,57
        ("SuccessIconBrush", "#6DBE7E", "#6DBE7E", "#2E8B4F", "#2A7E48"),
    ];

    private static readonly (MessageSeverity Severity, string Text)[] Samples =
    [
        (MessageSeverity.Error,   "Nie można skompilować procedury SP_RECALC_TOTALS: Token unknown - line 42, column 9."),
        (MessageSeverity.Warning, "Zaimportowano 118 240 wierszy, 12 pominięto z powodu błędów konwersji."),
        (MessageSeverity.Success, "Skrypt wykonany: 47 instrukcji, 47 zatwierdzonych kroków."),
        (MessageSeverity.Info,    "Transakcja robocza jest otwarta od 4 minut."),
    ];

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;
            var dark = variant == ThemeVariant.Dark;

            // ── Obraz 1: PRAWDZIWY MessageBanner, obecnie vs kandydat ──────────────────────────────
            //
            // ⭐ Renderowana jest kontrolka z produktu, nie makieta — dokładnie z powodu, który sonda
            //   zapisała przy `BuildShippedRow`: podgląd może być piękny przy szablonie renderującym
            //   co innego. Tutaj obie połowy to ten sam `MessageBanner` i ten sam `ControlStyles.axaml`.
            var before = Snapshot(BannerStack());
            Override(dark, candidate: true);
            var after = Snapshot(BannerStack());
            Override(dark, candidate: false);

            var bannerFile = Path.Combine(outDir, $"m5c-banner-{variant}.png");
            Program.Render(SideBySide(before, after, "obecnie", "kandydat"), bannerFile, scale: 1.0);
            Console.WriteLine(bannerFile);

            // ── Obraz 2: sam TEKST, trzy warianty obok siebie ──────────────────────────────────────
            //
            // ⚠ To jest właściwy przedmiot decyzji: defekt dotyczy tekstu, nie sygnału. Trzeci wariant
            //   (obecna barwa + SemiBold) jest tu, bo spełnia §10 jak napisane, a NIE spełnia WCAG —
            //   różnica ma być widoczna na obrazie, nie tylko w tabeli.
            var textBefore = Snapshot(TextStack(dark, semiBold: false));
            var textSemi = Snapshot(TextStack(dark, semiBold: true));
            Override(dark, candidate: true);
            var textAfter = Snapshot(TextStack(dark, semiBold: false));
            Override(dark, candidate: false);

            var textFile = Path.Combine(outDir, $"m5c-text-{variant}.png");
            Program.Render(
                SideBySide(textBefore, textAfter, "A · obecnie", "B · kandydat (barwa)", textSemi, "C · obecna barwa + SemiBold"),
                textFile, scale: 1.0);
            Console.WriteLine(textFile);
        }
    }

    /// <summary>
    /// Podmienia pędzle NA POZIOMIE APLIKACJI, bo <c>IconBrushConverter</c> rozwiązuje klucz przez
    /// <c>Application.Current.Resources.TryGetResource</c> — zasób założony na przodku w drzewie
    /// logicznym NIE zostałby znaleziony. ⚠ Wpis bezpośrednio w <c>Resources</c> bije scalone słowniki,
    /// więc podmiana jest pewna; zdejmowana jest zaraz po renderze, żeby drugi przebieg nie odziedziczył
    /// stanu pierwszego.
    /// </summary>
    private static void Override(bool dark, bool candidate)
    {
        var res = Application.Current!.Resources;
        foreach (var (key, curDark, candDark, curLight, candLight) in Tokens)
        {
            if (!candidate)
            {
                res.Remove(key);
                continue;
            }

            var hex = dark ? candDark : candLight;
            var current = dark ? curDark : curLight;
            // Kandydat równy stanowi obecnemu = ta barwa w tym motywie jest już nad progiem; nie
            // podmieniamy jej, żeby render nie sugerował zmiany, której nie ma.
            if (!string.Equals(hex, current, StringComparison.OrdinalIgnoreCase))
            {
                res[key] = new SolidColorBrush(Color.Parse(hex));
            }
        }
    }

    private static Control BannerStack()
    {
        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 10, Width = 620 };
        panel.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        foreach (var (severity, text) in Samples)
        {
            panel.Children.Add(new MessageBanner
            {
                Severity = severity,
                Message = text,
                ShowCopy = true,
                ShowDismiss = true,
            });
        }

        return panel;
    }

    /// <summary>
    /// Ten sam tekst 12 px na OBU powierzchniach, na których severity realnie ląduje: `PanelBrush`
    /// (baner) i `BackgroundBrush` (log Messages w edytorze SQL). Obie są pokazane, bo różnią się
    /// o ~0,3 i decyzja musi trzymać się tej trudniejszej.
    /// </summary>
    private static Control TextStack(bool dark, bool semiBold)
    {
        var outer = new StackPanel { Margin = new Thickness(14), Spacing = 12, Width = 430 };
        outer.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("BackgroundBrush"));

        foreach (var surfaceKey in new[] { "PanelBrush", "BackgroundBrush" })
        {
            var box = new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(3) };
            box.Bind(Border.BackgroundProperty, new DynamicResourceExtension(surfaceKey));
            box.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
            box.BorderThickness = new Thickness(1);

            var rows = new StackPanel { Spacing = 5 };
            var caption = new TextBlock { Text = surfaceKey, FontSize = 10, Margin = new Thickness(0, 0, 0, 3) };
            caption.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SubtleForegroundBrush"));
            rows.Children.Add(caption);

            foreach (var (severity, _) in Samples)
            {
                var key = MessageBanner.BrushKeyFor(severity);
                var line = new TextBlock
                {
                    // ⚠ Konkatenacja zamiast złożonego format stringa — reguła z katalogu gotchy
                    //   (#343 rodzina): narzędzie diagnostyczne nie używa mini-języka parsowanego
                    //   w czasie wykonania. Tu dodatkowo unika pułapki polskiego cudzysłowu.
                    Text = severity + " — komunikat o typowej długości w tym miejscu interfejsu.",
                    FontSize = 12,
                    FontWeight = semiBold ? FontWeight.SemiBold : FontWeight.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                };
                line.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(key));
                rows.Children.Add(line);
            }

            box.Child = rows;
            outer.Children.Add(box);
        }

        return outer;
    }

    // ─── Składanie porównania ─────────────────────────────────────────────────────────────────────
    //
    // ⭐ Połowy renderowane są ODDZIELNIE, bo podmiana zasobu jest globalna i dwa stany nie mogą
    //   istnieć w jednym drzewie naraz. Każda połowa to bitmapa zrobiona pod SWOIM stanem zasobów,
    //   więc zestawienie jest wierne, a nie zrekonstruowane.
    private static Control SideBySide(Bitmap a, Bitmap b, string labelA, string labelB,
                                      Bitmap? c = null, string? labelC = null)
    {
        var grid = new Grid { Margin = new Thickness(12) };
        grid.Bind(Panel.BackgroundProperty, new DynamicResourceExtension("ChromeStrongBrush"));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var items = c is null
            ? new[] { (a, labelA), (b, labelB) }
            : new[] { (a, labelA), (b, labelB), (c, labelC!) };

        for (var i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var head = new TextBlock { Text = items[i].Item2, FontSize = 11, Margin = new Thickness(6, 0, 6, 6) };
            head.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("ForegroundBrush"));
            Grid.SetRow(head, 0);
            Grid.SetColumn(head, i);
            grid.Children.Add(head);

            var img = new Image { Source = items[i].Item1, Margin = new Thickness(6, 0) };
            Grid.SetRow(img, 1);
            Grid.SetColumn(img, i);
            grid.Children.Add(img);
        }

        return grid;
    }

    /// <summary>Rasteryzuje kontrolkę pod AKTUALNYM stanem zasobów aplikacji.</summary>
    private static Bitmap Snapshot(Control root)
    {
        // ⚠ Kontrolka musi wisieć na TopLevelu, inaczej style aplikacji do niej nie dojdą i render
        //   pokaże gołego Fluenta (ta sama uwaga co przy `Program.Render`).
        var window = new Window { Content = root, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        root.Measure(new Size(2000, 2000));
        root.Arrange(new Rect(root.DesiredSize));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var size = root.Bounds.Size;
        var bmp = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)),
            new Vector(96, 96));
        bmp.Render(root);
        window.Close();
        return bmp;
    }
}
