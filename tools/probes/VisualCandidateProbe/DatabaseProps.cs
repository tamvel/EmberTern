// Pakiet UX po M5, punkt 6 — Database Properties. Render OBU MOTYWÓW dla QA użytkownika.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- dbprops
//
// ⭐ Renderowane jest PRAWDZIWE okno `DatabasePropertiesDialog` z prawdziwym `DatabasePropertiesViewModel`.
//   Dane podstawia się bez serwera, bo VM przyjmuje czytnik i writer jako DELEGATY — czyli ta sama cecha,
//   która czyni regułę „wysyłamy tylko zmienione" testowalną, pozwala też pokazać okno na obrazku.
//
// ⚠ Renderowane są trzy stany, bo różnią się TYM, co użytkownik ma zrozumieć, a nie tylko wyglądem:
//   1) zwykły  — profil z hasłem, nic nie zmienione (Apply nieaktywny),
//   2) bez hasła — jedyna odmowa znana Z GÓRY; pola wyłączone, powód przy nich,
//   3) częściowy sukces — stan, którego nie da się wywołać na żądanie na żywej bazie, a jest osiągalny,
//      bo Apply nie jest atomowy.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Metadata;

internal static class DatabaseProps
{
    private static DatabaseProperties Sample() => new()
    {
        DatabasePath = @"C:\Dane\C#\Źródła\EmberTern\Lab\EmberTern_Lab.fdb",
        Owner = "SYSDBA",
        EngineVersion = "5.0.3",
        OdsMajor = 13,
        OdsMinor = 1,
        Dialect = 3,
        Charset = "WIN1250",
        CreatedAt = new DateTime(2026, 5, 12, 9, 14, 32),
        PageSize = 8192,
        Pages = 12480,
        PageBuffers = 51200,
        LingerSeconds = null,
        SweepInterval = 20000,
        ForcedWrites = true,
        ReserveSpace = true,
    };

    public static void Run(string outDir)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = variant;

            Render(outDir, $"dbprops-{variant}-normal", Build(canWrite: true), null);
            Render(outDir, $"dbprops-{variant}-nopassword", Build(canWrite: false), null);

            // ⚠⚠ Komunikat MUSI zostać ustawiony PO Show(), nie przed: `Opened` uruchamia `LoadAsync`,
            //   a udane wczytanie czyści baner (bo tak ma być w produkcie — świeży odczyt nie zostawia
            //   starego błędu na ekranie). Pierwsza wersja sondy ustawiała go w konstruktorze i render
            //   wychodził BEZ banera — obrazek wiarygodny, odpowiadający na inne pytanie (#348).
            var partial = Build(canWrite: true);
            Render(outDir, $"dbprops-{variant}-partial", partial, vm =>
            {
                var (text, severity) = DatabasePropertiesViewModel.Describe(Partial());
                vm.Message = text;
                vm.MessageSeverity = severity;
                vm.HasMessage = true;
            });
        }
    }

    private static DatabaseConfigurationResult Partial() => new(
    [
        new DatabaseSettingOutcome(DatabaseSetting.SweepInterval),
        new DatabaseSettingOutcome(
            DatabaseSetting.ForcedWrites,
            "Unable to perform operation. System privilege USE_GFIX_UTILITY is missing",
            "28000",
            [335544788, 335545112]),
    ]);

    private static DatabasePropertiesDialog Build(bool canWrite)
    {
        var properties = Sample();
        var vm = new DatabasePropertiesViewModel(
            "SZKOLENIE_SQL", canWrite,
            _ => Task.FromResult(properties),
            (_, _) => Task.FromResult(new DatabaseConfigurationResult([])));

        return new DatabasePropertiesDialog(vm);
    }

    private static void Render(
        string outDir, string name, Window window, Action<DatabasePropertiesViewModel>? afterLoad)
    {
        window.ShowInTaskbar = false;
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);
        Dispatcher.UIThread.RunJobs();

        if (afterLoad is not null)
        {
            afterLoad((DatabasePropertiesViewModel)window.DataContext!);
            Dispatcher.UIThread.RunJobs();
        }

        var size = window.ClientSize;
        const double scale = 1.5;
        var bmp = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bmp.Render(window);

        var file = Path.Combine(outDir, name + ".png");
        using (var stream = File.Create(file))
        {
            bmp.Save(stream);
        }

        Console.WriteLine(file);
        window.Close();
    }
}
