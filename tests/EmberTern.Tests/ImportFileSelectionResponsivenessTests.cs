using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// M3b.1b/c — wybór pliku do importu nie blokuje UX.
///
/// <para>⚠⚠ <b>ZAKRES TEJ KLASY, PODANY WPROST, BO INACZEJ MYLI.</b> Suita <b>nie</b> potrafi udowodnić, że praca
/// zeszła z wątku UI — nie ma tu ani okna, ani pętli Dispatchera, a asercja na czasie byłaby testem, który psuje
/// się z powodów niezwiązanych ze swoim przedmiotem (R16). Ten dowód daje sonda
/// <c>tools/probes/ImportFileOpenProbe</c>, która odkłada zadanie o priorytecie <c>Render</c> przed przypisaniem
/// ścieżki i sprawdza, kiedy się wykonało: <b>17 768 ms → 1 ms</b>.</para>
///
/// <para>⭐ Testy poniżej pinują to, co maszyna <b>umie</b> ocenić sensownie: <b>realne ryzyko przeniesienia
/// odczytu poza wątek</b> (ograniczony podgląd mógł stać się pełnym odczytem) i <b>sygnał dla paska statusu</b>
/// (jeden na całą długość łańcucha, gaszony na każdej ścieżce wyjścia, odporny na wyprzedzenie).</para>
/// </summary>
public class ImportFileSelectionResponsivenessTests : IDisposable
{
    private readonly string _dir;

    public ImportFileSelectionResponsivenessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "embertern-import-resp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static DataImportTabViewModel NewVm()
        => new(new DataImportEnvironment(() => false, () => "—")) { PreviewDebounce = TimeSpan.Zero };

    private static async Task SettleAsync(DataImportTabViewModel vm)
    {
        for (var i = 0; i < 10; i++)
        {
            var pending = vm.PendingRecalculation;
            if (pending is null) return;
            await pending.ConfigureAwait(false);
            if (ReferenceEquals(pending, vm.PendingRecalculation)) return;
        }
    }

    private string WriteCsv(string name, int dataRows)
    {
        var path = Path.Combine(_dir, name);
        var sb = new StringBuilder("KOD;NAZWA\n");
        for (var i = 1; i <= dataRows; i++) sb.Append("K").Append(i).Append(";Pozycja ").Append(i).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    // ── B: przeniesienie odczytu poza wątek nie mogło zamienić ograniczonego podglądu w pełny odczyt ────

    /// <summary>
    /// ⭐ To JEST realne ryzyko zmiany z M3b.1b. Odczyt podglądu przestał być przeplatany z dodawaniem do
    /// kolekcji (`await foreach` + `PreviewRows.Add`) i zbiera najpierw ograniczoną głowę do zwykłej listy.
    /// Gdyby warunek przerwania wypadł z pętli, plik czytałby się CAŁY — objaw byłby dokładnie ten sam, który
    /// ta iteracja naprawia, tylko o jedno ogniwo dalej.
    /// </summary>
    [Fact]
    public async Task Preview_StopsAtItsBound_EvenWhenTheFileIsLonger()
    {
        var vm = NewVm();
        vm.Source.UseFile = true;
        vm.Source.FilePath = WriteCsv("long.csv", DataImportTabViewModel.SourcePreviewRows * 3);
        await SettleAsync(vm);

        Assert.Equal(DataImportTabViewModel.SourcePreviewRows, vm.PreviewRows.Count);
    }

    [Fact]
    public async Task Preview_ShowsEveryRow_WhenTheFileIsShorterThanTheBound()
    {
        var vm = NewVm();
        vm.Source.UseFile = true;
        vm.Source.FilePath = WriteCsv("short.csv", 7);
        await SettleAsync(vm);

        Assert.Equal(7, vm.PreviewRows.Count);
        Assert.Equal(2, vm.PreviewFields.Count);
    }

    // ── C: jeden sygnał „łańcuch pracuje" ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recalculating_IsClearedWhenTheChainSettles()
    {
        var vm = NewVm();
        vm.Source.UseFile = true;
        vm.Source.FilePath = WriteCsv("done.csv", 5);

        await SettleAsync(vm);

        Assert.False(vm.IsRecalculating);
    }

    /// <summary>
    /// ⚠⚠ Pułapka, dla której gaszenie jest warunkowe: wyprzedzony łańcuch kończy się PO starcie następnego
    /// (anulowanie nie jest natychmiastowe), więc bezwarunkowe <c>false</c> w jego <c>finally</c> zgasiłoby
    /// sygnał dla operacji, która właśnie się rozpoczęła. Objaw: przy szybkiej zmianie ustawień pasek statusu
    /// znika, choć praca trwa.
    /// </summary>
    [Fact]
    public async Task Recalculating_SurvivesBeingSuperseded_ByANewerChange()
    {
        var vm = NewVm();
        vm.Source.UseFile = true;
        vm.Source.FilePath = WriteCsv("first.csv", 5);

        // Druga zmiana wyprzedza pierwszą, zanim tamta zdąży się zamknąć.
        vm.Source.FilePath = WriteCsv("second.csv", 5);
        Assert.True(vm.IsRecalculating);

        await SettleAsync(vm);
        Assert.False(vm.IsRecalculating);
    }

    [Fact]
    public async Task Recalculating_IsClearedEvenWhenTheSourceCannotBeRead()
    {
        var vm = NewVm();
        vm.Source.UseFile = true;
        vm.Source.FilePath = Path.Combine(_dir, "nie-ma-takiego-pliku.csv");

        await SettleAsync(vm);

        Assert.False(vm.IsRecalculating);
    }

    // ── C: co pokazuje pasek statusu ────────────────────────────────────────────────────────────────────

    private static MainWindowViewModel NewMain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-import-resp-main-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new MainWindowViewModel(new ConnectionProfileStore(dir), new FirebirdConnectionService());
    }

    private static DataImportTabViewModel AddImport(MainWindowViewModel main)
    {
        var import = NewVm();
        main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateDataImport(main, import, null));
        return import;
    }

    [Fact]
    public void ReadingAFile_IsAnnouncedInTheStatusBar()
    {
        var main = NewMain();
        var import = AddImport(main);

        import.Source.UseFile = true;
        import.IsRecalculating = true;

        Assert.True(main.Progress.IsRunning);
        Assert.Equal(UiStrings.StatusProgressImportReadingFile, main.Progress.Label);
        Assert.True(main.Progress.IsIndeterminate);
        // ⚠ Brak przycisku anulowania jest tu POPRAWNY: łańcuch ma własny CTS, ale użytkownik nie ma dla niego
        // przycisku, a wymyślenie go byłoby dodaniem funkcji pod pozorem podłączenia postępu.
        Assert.False(main.Progress.HasCancel);
    }

    /// <summary>
    /// ⭐ To samo ogniwo obsługuje schowek, więc etykieta „Loading file…" byłaby tam nieprawdą — a kłamiąca
    /// etykieta jest nieodróżnialna od awarii (gotcha #311).
    /// </summary>
    [Fact]
    public void ReadingTheClipboard_DoesNotClaimToBeReadingAFile()
    {
        var main = NewMain();
        var import = AddImport(main);

        import.Source.UseFile = false;
        import.IsRecalculating = true;

        Assert.Equal(UiStrings.StatusProgressImportReadingClipboard, main.Progress.Label);
    }

    /// <summary>Trwający import jest ważniejszy niż przeliczanie konfiguracji — ten sam szczebel, ustalona
    /// kolejność w jego obrębie.</summary>
    [Fact]
    public void ARunningImport_OutranksReadingTheSource()
    {
        var main = NewMain();
        var import = AddImport(main);

        import.Source.UseFile = true;
        import.IsRecalculating = true;
        import.IsRunning = true;

        Assert.Contains("Importing", main.Progress.Label, StringComparison.OrdinalIgnoreCase);
    }
}
