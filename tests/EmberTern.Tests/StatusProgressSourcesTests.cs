using System;
using System.IO;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// M3b.1 — sekcja postępu paska statusu ma trzy źródła, więc potrzebuje ARBITRAŻU. Ta klasa pinuje
/// ratyfikowaną drabinkę priorytetów (zapytanie/skrypt &gt; import), to że etykieta NAZYWA operację,
/// oraz dwie własności, których złamanie jest ciche.
///
/// <para>⚠ Asercje idą przez <c>vm.Progress</c> — czyli przez model, który czyta wiązanie w XAML — a nie
/// przez wewnętrzny resolver. Test na prywatnej krotce potwierdzałby wybór, a nie to, że sekcja pokazuje
/// wybraną operację; przy zmianie właściciela to dwie różne rzeczy (patrz
/// <see cref="QueryAfterAScript_DoesNotInheritThePercentage"/>).</para>
///
/// <para>⚠ Klasa NIE konstruuje kontrolek Avalonii, więc należy do partycji głównej, nie do filtra
/// headless (kryterium z handovera §8).</para>
/// </summary>
public class StatusProgressSourcesTests
{
    private static MainWindowViewModel NewVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new MainWindowViewModel(new ConnectionProfileStore(dir), new FirebirdConnectionService());
    }

    private static ScriptExecutorTabViewModel AddScript(MainWindowViewModel vm, FirebirdConnectionService cs)
    {
        var ts = new TransactionService(cs);
        var script = new ScriptExecutorTabViewModel(new FirebirdScriptParser(), new FirebirdScriptExecutor(cs, ts), ts);
        vm.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateScriptExecutor(vm, script, null));
        return script;
    }

    private static DataImportTabViewModel AddImport(MainWindowViewModel vm)
    {
        var import = new DataImportTabViewModel(new DataImportEnvironment(() => true, () => "LAB"))
        {
            PreviewDebounce = TimeSpan.Zero,
        };
        vm.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateDataImport(vm, import, null));
        return import;
    }

    // ── Drabinka priorytetów ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Query_OutranksScriptAndImport()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);
        var import = AddImport(vm);

        script.RunStatementTotal = 10;
        script.IsRunning = true;
        import.IsRunning = true;
        vm.IsExecuting = true;

        Assert.True(vm.Progress.IsRunning);
        Assert.Equal(UiStrings.ExecutingStatus, vm.Progress.Label);
    }

    [Fact]
    public void Script_OutranksImport()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);
        var import = AddImport(vm);

        import.IsRunning = true;
        script.RunStatementTotal = 40;
        script.IsRunning = true;

        Assert.True(vm.Progress.IsRunning);
        Assert.Contains("script", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Importing", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_OwnsTheSection_WhenNothingElseRuns()
    {
        var vm = NewVm();
        var import = AddImport(vm);

        import.ProgressRowsRead = 12_000;
        import.IsRunning = true;

        Assert.True(vm.Progress.IsRunning);
        Assert.Contains("Importing", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12", vm.Progress.Label, StringComparison.Ordinal);
    }

    // ── Ścieżka procentowa — do M3b.1 nie miała ŻADNEGO żywego konsumenta (§19.7.2) ─────────────────

    [Fact]
    public void Script_IsTheFirstLiveConsumerOfThePercentagePath()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);

        script.RunStatementTotal = 40;
        script.IsRunning = true;
        script.SuccessCount = 8;
        script.FailedCount = 2;

        Assert.False(vm.Progress.IsIndeterminate);
        Assert.Equal(25d, vm.Progress.Percent, 3);   // 10 z 40
    }

    [Fact]
    public void Import_StaysIndeterminate_WhenItsRowEstimateIsUnknown()
    {
        var vm = NewVm();
        var import = AddImport(vm);

        // Bez schematu źródła `EstimatedRows` nie istnieje, więc import sam raportuje tryb nieokreślony.
        Assert.True(import.IsProgressIndeterminate);
        import.IsRunning = true;

        Assert.True(vm.Progress.IsRunning);
        Assert.True(vm.Progress.IsIndeterminate);
    }

    /// <summary>
    /// ⭐ Pułapka, dla której applier rozróżnia „zmiana właściciela" od „raport tego samego":
    /// <c>Begin</c> resetuje tryb i procent, <c>Report</c> bez procentu nie rusza ani jednego, ani
    /// drugiego. Bez tego rozróżnienia zapytanie przejmujące sekcję odziedziczyłoby pasek stojący na
    /// procencie TAMTEJ operacji — sekcja kłamałaby o bieżącej chwili.
    ///
    /// <para>⚠⚠ Scenariusz jest PRZEJŚCIEM WŁAŚCICIELA BEZ PRZERWY (skrypt biegnie, użytkownik daje F5),
    /// i to jest jedyny kształt, który tę wadę ujawnia. Pierwsza wersja tego testu gasiła skrypt PRZED
    /// startem zapytania — wtedy sekcja przechodzi przez stan „nic nie trwa", a <c>End()</c> resetuje tryb
    /// sama, więc test przechodził niezależnie od appliera. Podłożone naruszenie (zapal sekcję bez
    /// <c>Begin</c>) przeszło ten wariant bez mrugnięcia; ten wariant je łapie.</para>
    /// </summary>
    [Fact]
    public void QueryTakingOverFromARunningScript_DoesNotInheritThePercentage()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);

        script.RunStatementTotal = 40;
        script.IsRunning = true;
        script.SuccessCount = 20;
        Assert.False(vm.Progress.IsIndeterminate);      // skrypt ustawił tryb procentowy
        Assert.Equal(50d, vm.Progress.Percent, 3);

        // Skrypt NADAL biegnie — zapytanie odbiera mu sekcję drabinką priorytetów, bez przerwy.
        vm.IsExecuting = true;

        Assert.True(vm.Progress.IsRunning);
        Assert.True(script.IsRunning);
        Assert.True(vm.Progress.IsIndeterminate);       // zapytanie sumy nie zna — i tak to mówi
        Assert.Equal(0d, vm.Progress.Percent);
    }

    /// <summary>
    /// Odwrotny kierunek tego samego przejścia: gdy zapytanie się kończy, sekcję przejmuje wciąż
    /// trwający skrypt — i musi wrócić do SWOJEGO trybu procentowego, a nie zostać z nieokreślonym.
    /// </summary>
    [Fact]
    public void WhenTheQueryEnds_ARunningScriptTakesTheSectionBack()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);

        script.RunStatementTotal = 40;
        script.IsRunning = true;
        script.SuccessCount = 20;
        vm.IsExecuting = true;
        Assert.True(vm.Progress.IsIndeterminate);

        vm.IsExecuting = false;

        Assert.True(vm.Progress.IsRunning);             // sekcja NIE gaśnie — skrypt wciąż trwa
        Assert.False(vm.Progress.IsIndeterminate);
        Assert.Equal(50d, vm.Progress.Percent, 3);
        Assert.Contains("script", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);
    }

    // ── Sekcja nie może zostać zapalona po operacji, której nośnik już nie istnieje ─────────────────

    [Fact]
    public void ClosingATabWithARunningOperation_DoesNotLeaveTheBarLit()
    {
        var vm = NewVm();
        var import = AddImport(vm);
        import.IsRunning = true;
        Assert.True(vm.Progress.IsRunning);

        vm.WorkspaceTabs.RemoveAt(vm.WorkspaceTabs.Count - 1);

        Assert.False(vm.Progress.IsRunning);
    }

    [Fact]
    public void ClearingEveryTab_AsDisconnectDoes_DoesNotLeaveTheBarLit()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);
        script.RunStatementTotal = 5;
        script.IsRunning = true;
        Assert.True(vm.Progress.IsRunning);

        // `Clear()` to akcja `Reset` — nie niesie `OldItems`, dlatego odpinanie idzie po własnym zbiorze.
        vm.WorkspaceTabs.Clear();

        Assert.False(vm.Progress.IsRunning);
    }

    // ── „Dwa zasięgi JEDNEJ komendy", rozszerzone na nowe źródła (§19.7.3) ─────────────────────────

    /// <summary>
    /// ⭐ Asercja na TOŻSAMOŚĆ INSTANCJI, nie na „jest jakaś komenda": sekcja postępu i przycisk na
    /// powierzchni operacji muszą naciskać ten sam obiekt, żeby <c>CanExecute</c> i zatrzask
    /// „anulowanie w toku" były jedne. Kopia o identycznym zachowaniu przeszłaby test na równość.
    /// </summary>
    [Fact]
    public void CancelInTheSection_IsTheOwnersOwnCommand_NotACopy()
    {
        using var cs = new FirebirdConnectionService();
        var vm = NewVm();
        var script = AddScript(vm, cs);
        var import = AddImport(vm);

        import.IsRunning = true;
        Assert.Same(import.CancelRunCommand, vm.Progress.CancelCommand);

        script.RunStatementTotal = 3;
        script.IsRunning = true;
        Assert.Same(script.StopCommand, vm.Progress.CancelCommand);

        vm.IsExecuting = true;
        Assert.Same(vm.CancelQueryCommand, vm.Progress.CancelCommand);
    }

    // ── Etykieta nazywa operację ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wymóg użytkownika (2026-08-04): przy trzech źródłach etykieta musi jednoznacznie mówić, co jest
    /// wykonywane. ⚠ Strażnik czyta STAŁE, nie stan uruchomienia — inaczej „uproszczenie" formatu do
    /// samego licznika przeszłoby build i wszystkie pozostałe testy.
    /// </summary>
    [Theory]
    [InlineData("query")]
    [InlineData("script")]
    [InlineData("Importing")]
    public void EveryProgressLabel_NamesItsOperation(string operationWord)
    {
        var labels = new[]
        {
            UiStrings.ExecutingStatus,
            UiStrings.StatusProgressQueryRowsFormat,
            UiStrings.StatusProgressScriptFormat,
            UiStrings.StatusProgressImportFormat,
        };

        Assert.Contains(labels, l => l.Contains(operationWord, StringComparison.OrdinalIgnoreCase));
    }
}
