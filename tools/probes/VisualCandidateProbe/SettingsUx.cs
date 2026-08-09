// Pakiet UX po M5, punkt 5 — Settings UX. Render OBU MOTYWÓW dla QA użytkownika.
//
// Uruchomienie:  dotnet run --project tools/probes/VisualCandidateProbe -- settings
//
// ⭐⭐ TO NIE JEST MAKIETA — sonda konstruuje PRAWDZIWE okno `SettingsWindow` z prawdziwym
//   `PreferencesService` nad tymczasowym katalogiem i renderuje je w całości. Powód jest wprost lekcją
//   #348 z tego samego pakietu: obrazek zbudowany z atrap wygląda wiarygodnie i odpowiada na INNE
//   pytanie, niż zadano. Tutaj każdy piksel pochodzi z produktu — ze stylów aplikacji, z katalogu
//   `SettingsCatalog`, z ról tokenów i z tych samych szablonów, które zobaczy użytkownik.
//
// ⚠ Dlatego też ta sonda NIE definiuje żadnych kandydatów. W pozostałych modułach kandydat żyje
//   w sondzie i nic się nie wdraża przez samo uruchomienie; tu odwrotnie — sonda pokazuje stan
//   WDROŻONY, więc kolumnę „przed" trzeba wyrenderować PRZED zmianą kodu i zachować jako plik.
//
// ⚠ Renderowane są wszystkie sześć kategorii, bo strony różnią się liczbą kart (General 4, Editor 6)
//   i to właśnie na najgęstszej stronie widać, czy hierarchia powierzchni działa.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Settings;

internal static class SettingsUx
{
    /// <param name="tag">
    /// Przyrostek pliku — „before" przed zmianą, „after" po niej. ⚠ Świadomie parametr, a nie stała:
    /// obie kolumny powstają z TEGO SAMEGO kodu sondy, więc różnica na obrazkach może pochodzić
    /// wyłącznie z produktu.
    /// </param>
    public static void Run(string outDir, string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-settings-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                Application.Current!.RequestedThemeVariant = variant;

                foreach (var category in SettingsCatalog.Categories)
                {
                    // Świeży serwis na kategorię — okno stroi się w konstruktorze, a jeden serwis
                    // współdzielony między sześcioma oknami zapisywałby ten sam plik sześć razy bez powodu.
                    var service = new PreferencesService(new PreferencesStore(dir));
                    var portability = new SettingsPortability(
                        new ApplicationSettingsStore(dir, null), service, "0.5.0-probe");

                    var window = new SettingsWindow(service, portability, category.Id);
                    var file = Path.Combine(outDir, $"settings-{tag}-{variant}-{category.Id}.png");
                    RenderWindow(window, file, scale: 1.5);
                    Console.WriteLine(file);
                }
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* katalog tymczasowy */ }
        }
    }

    /// <summary>
    /// Renderuje CAŁE okno, nie jego treść.
    ///
    /// <para>⚠ To jest różnica merytoryczna wobec <c>Program.Render</c>, który zawija kontrolkę we własne
    /// okno: pytaniem tej iteracji jest hierarchia POWIERZCHNI, a tło okna i jego marginesy są jej częścią.
    /// Render samej treści pokazałby karty bez podłoża, na którym stoją — czyli dokładnie to, o co
    /// pytamy.</para>
    ///
    /// <para>⚠ Okno stoi poza ekranem i nigdy nie jest aktywowane, więc nic nie miga użytkownikowi.</para>
    /// </summary>
    private static void RenderWindow(Window window, string path, double scale)
    {
        window.ShowInTaskbar = false;
        window.Show();
        window.Position = new PixelPoint(-4000, -4000);
        Dispatcher.UIThread.RunJobs();

        // Okno ma jawne Width/Height, więc mierzy się do nich samo; RunJobs po Show wystarcza,
        // żeby szablony się zrealizowały, a wiązania rozwiązały.
        var size = window.ClientSize;
        var bmp = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bmp.Render(window);

        using (var stream = File.Create(path))
        {
            bmp.Save(stream);
        }

        window.Close();
    }
}
