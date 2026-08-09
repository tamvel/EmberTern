// Product Polish M5 / DPI — DIAGNOZA znaleziska QA przy 150 % i 175 %.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- fit
//
// ─── ZGŁOSZENIE ──────────────────────────────────────────────────────────────────────────────────
//
// QA użytkownika (2026-08-10): przy 150 % i 175 % **Activity Monitor i Data Import nie mieszczą się
// w dostępnej przestrzeni, część interfejsu jest poza ekranem i nie ma jak do niej doscrollować**.
// Pozostałe punkty checklisty przeszły.
//
// ─── CO TA SONDA MIERZY ──────────────────────────────────────────────────────────────────────────
//
// Pytanie do rozstrzygnięcia brzmi: czy to defekt SKALOWANIA, czy ograniczenie KONSTRUKCYJNE tych widoków?
// Te dwie odpowiedzi prowadzą w zupełnie inne miejsca, a rozróżnia je JEDNA liczba: **minimalna szerokość,
// jakiej widok żąda w DIP-ach**. Skalowanie DPI nie zmienia bowiem liczby DIP-ów, których żąda kontrolka —
// zmienia liczbę DIP-ów, które MA ekran (1920 px / 1,5 = 1280 DIP). Jeżeli widok żąda więcej niż 1920, to
// nie mieści się także przy 100 % i DPI jedynie ujawnia defekt wcześniej.
//
// ⭐ Mierzone na PRAWDZIWYCH widokach z prawdziwymi ViewModelami (bez bazy) — nie na odtworzonych kopiach
//   pasków. Kopia odpowiadałaby na pytanie o kopię (#345).
//
// ⛔ To jest WYŁĄCZNIE pomiar. Sonda niczego nie naprawia.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Firebird;

internal static class Fit
{
    /// <summary>Typowe ekrany × skale, których dotyczy zgłoszenie. Szerokość/wysokość w pikselach FIZYCZNYCH.</summary>
    private static readonly (int W, int H, string Name)[] Screens =
    [
        (1920, 1080, "1920×1080"),
        (2560, 1440, "2560×1440"),
    ];

    private static readonly double[] Scales = [1.00, 1.25, 1.50, 1.75, 2.00];

    public static void Run(string outDir)
    {
        var report = new StringBuilder();
        report.AppendLine("DPI — DIAGNOZA: minimalna szerokość widoków vs dostępne DIP-y");
        report.AppendLine(new string('=', 100));
        report.AppendLine();
        report.AppendLine("⭐ Skalowanie NIE zmienia liczby DIP-ów, których żąda kontrolka. Zmienia liczbę DIP-ów,");
        report.AppendLine("   które ma ekran: 1920 px / 1,5 = 1280 DIP. Dlatego wystarczy porównać dwie liczby.");
        report.AppendLine();

        var cs = new FirebirdConnectionService();
        try
        {
            var subjects = new (string Name, Control View)[]
            {
                ("Activity Monitor", BuildTrace(cs)),
                ("Data Import", BuildImport()),
                ("Script Executor (odniesienie)", BuildScript(cs)),
            };

            foreach (var (name, view) in subjects)
            {
                Measure(report, name, view);
            }

            report.AppendLine();
            report.AppendLine(new string('=', 100));
            report.AppendLine("DOSTĘPNE DIP-y PRZY SKALI (obszar roboczy = ekran minus pasek zadań ~48 px fiz.)");
            report.AppendLine();
            report.Append("  skala ");
            foreach (var (_, _, sname) in Screens) report.Append($"{sname,22}");
            report.AppendLine();
            foreach (var scale in Scales)
            {
                report.Append($"  {scale,5:P0} ");
                foreach (var (w, h, _) in Screens)
                {
                    report.Append($"{w / scale,10:0} × {(h - 48) / scale,-9:0}");
                }

                report.AppendLine();
            }
        }
        finally
        {
            cs.Dispose();
        }

        var file = Path.Combine(outDir, "m5-dpi-fit.txt");
        File.WriteAllText(file, report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine(file);
    }

    /// <summary>
    /// Realizuje widok przy NIEOGRANICZONEJ szerokości i czyta jego <c>DesiredSize</c> — czyli ile DIP-ów
    /// naprawdę żąda. ⚠ Dodatkowo rozbija wynik na pierwszy poziom dzieci, bo „widok chce 1900" jest
    /// bezużyteczne, dopóki nie wiadomo, KTÓRY pasek tego żąda.
    /// </summary>
    private static void Measure(StringBuilder report, string name, Control view)
    {
        var window = new Window { Content = view, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        report.AppendLine(new string('-', 100));
        report.AppendLine($"### {name}");
        report.AppendLine($"    żądana szerokość widoku: {view.DesiredSize.Width,8:0} DIP" +
                          $"   ·   żądana wysokość: {view.DesiredSize.Height,6:0} DIP");
        report.AppendLine();

        // Najszersze POZIOME panele bez przewijania — to one przycinają, bo `StackPanel` w poziomie
        // mierzy dzieci przy nieskończoności i nie kompresuje się.
        var offenders = view.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(p => p.Orientation == Orientation.Horizontal && p.DesiredSize.Width > 700)
            .Select(p => (Panel: p, Width: p.DesiredSize.Width, Scroll: HasScrollAncestor(p)))
            .OrderByDescending(x => x.Width)
            .Take(6)
            .ToList();

        if (offenders.Count == 0)
        {
            report.AppendLine("    (żaden poziomy StackPanel nie żąda ponad 700 DIP)");
        }
        else
        {
            report.AppendLine("    poziome panele żądające ponad 700 DIP:");
            foreach (var o in offenders)
            {
                report.AppendLine($"      {o.Width,8:0} DIP   dzieci: {o.Panel.Children.Count,3}   " +
                                  $"przewijalny: {(o.Scroll ? "TAK" : "NIE ⛔")}");
            }
        }

        // ── WYSOKOŚĆ: co żąda miejsca w pionie i czego NIE DA SIĘ ścisnąć ────────────────────────────
        // ⚠ Osobne pytanie od szerokości i osobny objaw w QA (przy 175 % znika pasek statusu). Element
        //   z `MinHeight` nie skompresuje się poniżej tej wartości ANI o piksel, więc suma minimów w jednej
        //   kolumnie Grida jest twardą podłogą całego okna — niezależnie od tego, ile ekranu zostało.
        var tallMins = view.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.MinHeight > 40 && !double.IsInfinity(c.MinHeight))
            .Select(c => (Type: c.GetType().Name, Min: c.MinHeight))
            .OrderByDescending(x => x.Min)
            .Take(6)
            .ToList();

        var rootScroll = view is ScrollViewer
                         || view.GetVisualDescendants().OfType<ScrollViewer>().Any(s =>
                             s.GetVisualAncestors().OfType<ScrollViewer>().Any() == false
                             && s.VerticalScrollBarVisibility is Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                                 or Avalonia.Controls.Primitives.ScrollBarVisibility.Visible);

        report.AppendLine($"    zewnętrzne przewijanie PIONOWE całego widoku: {(rootScroll ? "jest" : "BRAK ⛔")}");
        if (tallMins.Count > 0)
        {
            report.AppendLine("    elementy z MinHeight > 40 DIP (nieściśliwe):");
            foreach (var t in tallMins) report.AppendLine($"      {t.Min,6:0} DIP   {t.Type}");
        }

        window.Close();
        report.AppendLine();
    }

    /// <summary>Czy element leży w jakimkolwiek <see cref="ScrollViewer"/> pozwalającym przewinąć w POZIOMIE?</summary>
    private static bool HasScrollAncestor(Visual v)
        => v.GetVisualAncestors().OfType<ScrollViewer>()
            .Any(s => s.HorizontalScrollBarVisibility is Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                or Avalonia.Controls.Primitives.ScrollBarVisibility.Visible);

    private static Control BuildTrace(FirebirdConnectionService cs)
        => new TraceMonitorTabView { DataContext = new TraceMonitorTabViewModel(new FirebirdTraceService(cs)) };

    private static Control BuildImport()
        => new DataImportTabView
        {
            DataContext = new DataImportTabViewModel(new DataImportEnvironment(() => false, () => "test")),
        };

    private static Control BuildScript(FirebirdConnectionService cs)
    {
        var ts = new TransactionService(cs);
        return new ScriptExecutorTabView
        {
            DataContext = new ScriptExecutorTabViewModel(new FirebirdScriptParser(), new FirebirdScriptExecutor(cs, ts), ts),
        };
    }
}
