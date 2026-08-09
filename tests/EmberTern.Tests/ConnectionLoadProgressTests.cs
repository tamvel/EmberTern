using System;
using System.IO;
using System.Linq;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// M3b.2 (§19.34) — ładowanie połączenia w sekcji postępu paska statusu.
///
/// <para>⚠⚠ <b>To jest klasa o ŚCIEŻKACH WYJŚCIA, nie o wyglądzie.</b> Ładowanie połączenia ma trzy fazy
/// w dwóch klasach i <c>MetadataReady</c> — naturalny kandydat na sygnał końca — <b>nie nastąpi</b> przy
/// nieudanym połączeniu, przy rozłączeniu w trakcie, ani gdy prefetch rzuci wyjątek, którego
/// <c>LoadGroupAsync</c> nie łapie. Każda z tych dróg zostawiłaby zapalony pasek na zawsze (§19.7.4),
/// a objaw byłby trwały i niezwiązany z niczym, co użytkownik akurat robi.</para>
///
/// <para>⭐ Testy poniżej nie potrzebują serwera: sterują flagami faz dokładnie tak, jak robią to ich
/// właściciele, i pytają o to, co widzi wiązanie — <c>vm.Progress</c>.</para>
/// </summary>
public class ConnectionLoadProgressTests
{
    private static MainWindowViewModel NewVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-connload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new MainWindowViewModel(new ConnectionProfileStore(dir), new FirebirdConnectionService());
    }

    // ── Faza 1 ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Connecting_IsAnnounced()
    {
        var vm = NewVm();
        vm.IsConnecting = true;

        Assert.True(vm.Progress.IsRunning);
        Assert.Equal(UiStrings.StatusProgressConnecting, vm.Progress.Label);
        Assert.True(vm.Progress.IsIndeterminate);
        // Połączenia nie da się przerwać z paska: nie ma dla tego komendy, a wymyślenie jej byłoby
        // dodaniem funkcji pod pozorem podłączenia postępu.
        Assert.False(vm.Progress.HasCancel);
    }

    // ── Faza 3 ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadingMetadata_ReportsRealPercentage()
    {
        var vm = NewVm();
        vm.IsConnecting = true;

        vm.Metadata.BeginMetadataPrefetch(13);
        vm.Metadata.ReportMetadataPrefetch(4);

        Assert.True(vm.Progress.IsRunning);
        Assert.Contains("metadata", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.Progress.IsIndeterminate);
        Assert.Equal(4 * 100d / 13, vm.Progress.Percent, 3);
    }

    /// <summary>
    /// ⭐ Dwie flagi, jeden szczebel: faza 3 przejmuje sekcję po fazie 1 i etykieta się zmienia, ale pasek
    /// ani na moment nie gaśnie. Pomiędzy nimi leży faza 2, która blokuje wątek UI — gdyby sekcja gasła
    /// na jej czas, po jej zakończeniu zapaliłaby się ponownie, czyli mrugnięcie w środku jednej operacji.
    /// </summary>
    [Fact]
    public void TheSectionStaysLit_AcrossTheHandoverFromConnectingToMetadata()
    {
        var vm = NewVm();
        vm.IsConnecting = true;
        Assert.True(vm.Progress.IsRunning);

        vm.Metadata.BeginMetadataPrefetch(13);
        Assert.True(vm.Progress.IsRunning);
        Assert.Contains("metadata", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);

        vm.Metadata.EndMetadataPrefetch();
        Assert.False(vm.Progress.IsRunning);
    }

    /// <summary>Koniec prefetchu gasi RÓWNIEŻ fazę 1 — to jedyne miejsce domykające odcinek udanego
    /// połączenia, bo prefetch ma swoje <c>finally</c> i nie da się go pominąć.</summary>
    [Fact]
    public void FinishingThePrefetch_AlsoClearsPhaseOne()
    {
        var vm = NewVm();
        vm.IsConnecting = true;
        vm.Metadata.BeginMetadataPrefetch(13);

        vm.Metadata.EndMetadataPrefetch();

        Assert.False(vm.IsConnecting);
        Assert.False(vm.Progress.IsRunning);
    }

    // ── Ścieżki wyjścia, na których faza 3 NIE nastąpi ─────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ Rozłączenie w trakcie ładowania. `ApplyActiveConnectionChange(null)` jest tu jedyną drogą, która
    /// biegnie, a `MetadataReady` nie padnie — bez gaszenia w tym miejscu pasek zostałby zapalony.
    /// </summary>
    [Fact]
    public void DisconnectingMidLoad_DoesNotLeaveTheBarLit()
    {
        var vm = NewVm();
        vm.IsConnecting = true;
        Assert.True(vm.Progress.IsRunning);

        vm.ApplyActiveConnectionChange(null);

        Assert.False(vm.IsConnecting);
        Assert.False(vm.Progress.IsRunning);
    }

    /// <summary>
    /// Prefetch przerwany wyjątkiem: właściciel gasi fazę 3 we własnym <c>finally</c>, co gasi też fazę 1.
    /// Test odtwarza dokładnie to, co robi `finally` — bez tego pasek świeciłby po operacji, która się
    /// wywaliła, a użytkownik nie miałby jak go zgasić.
    /// </summary>
    [Fact]
    public void APrefetchThatFails_StillClearsTheBar()
    {
        var vm = NewVm();
        vm.IsConnecting = true;
        vm.Metadata.BeginMetadataPrefetch(13);
        vm.Metadata.ReportMetadataPrefetch(5);

        // to, co wykonuje `finally` w LoadCategoriesAsync, niezależnie od tego, czy pętla dobiegła końca
        vm.Metadata.EndMetadataPrefetch();

        Assert.False(vm.Progress.IsRunning);
        Assert.False(vm.IsConnecting);
    }

    // ── Drabinka priorytetów ────────────────────────────────────────────────────────────────────────

    /// <summary>Ładowanie połączenia jest najwyżej: dopóki nie skończy, nie działa nic innego.</summary>
    [Fact]
    public void ConnectionLoading_OutranksAQuery()
    {
        var vm = NewVm();
        vm.IsExecuting = true;
        Assert.Equal(UiStrings.ExecutingStatus, vm.Progress.Label);

        vm.IsConnecting = true;

        Assert.Equal(UiStrings.StatusProgressConnecting, vm.Progress.Label);
    }

    [Fact]
    public void WhenTheConnectionHasLoaded_TheQueryTakesTheSectionBack()
    {
        var vm = NewVm();
        vm.IsExecuting = true;
        vm.IsConnecting = true;
        vm.Metadata.BeginMetadataPrefetch(13);
        Assert.Contains("metadata", vm.Progress.Label, StringComparison.OrdinalIgnoreCase);

        vm.Metadata.EndMetadataPrefetch();

        Assert.True(vm.Progress.IsRunning);
        Assert.Equal(UiStrings.ExecutingStatus, vm.Progress.Label);
    }

    /// <summary>
    /// ⚠ Odświeżenie metadanych (F4) świadomie NIE raportuje — decyzja użytkownika 2026-08-04. Strażnik
    /// jest tu, bo `RefreshAsync` wykonuje tę samą pracę i ma własne `try/finally`, więc podłączenie go
    /// „przy okazji" byłoby jedną linią i nikt by nie zauważył, że zakres się rozszerzył.
    /// </summary>
    [Fact]
    public void ManualRefresh_IsDeliberatelyNotWiredToTheStatusBar()
    {
        var source = File.ReadAllText(SourcePath("ViewModels", "MetadataExplorerViewModel.cs"));
        var start = source.IndexOf("public async Task RefreshAsync", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = source.IndexOf("internal async Task LoadGroupAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var body = source[start..end];
        Assert.DoesNotContain("BeginMetadataPrefetch", body, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var all = new[] { dir!.FullName, "src", "EmberTern.App" }.Concat(parts).ToArray();
        return Path.Combine(all);
    }
}
