using System;
using System.IO;
using System.Runtime.CompilerServices;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class MetadataExplorerViewModelTests
{
    [Fact]
    public void ReloadConnections_PopulatesMetadataConnections()
    {
        using var harness = new Harness();
        harness.Store.Upsert(new ConnectionProfile { Name = "A", Host = "h", Port = 3050 });
        harness.Store.Upsert(new ConnectionProfile { Name = "B", Host = "h", Port = 3050 });

        harness.Main.ReloadConnections();

        Assert.Equal(2, harness.Main.Metadata.Connections.Count);
        Assert.Contains(harness.Main.Metadata.Connections, c => c.Profile.Name == "A");
        Assert.Contains(harness.Main.Metadata.Connections, c => c.Profile.Name == "B");
    }

    [Fact]
    public void ReloadConnections_DoesNotAccumulateStaleNodes()
    {
        using var harness = new Harness();
        harness.Store.Upsert(new ConnectionProfile { Name = "A", Host = "h", Port = 3050 });
        harness.Main.ReloadConnections();
        harness.Main.ReloadConnections();
        harness.Main.ReloadConnections();

        // Three calls, one profile → still one node, no doubles.
        Assert.Single(harness.Main.Metadata.Connections);
    }

    [Fact]
    public void Commands_DisabledWhenNoConnectionSelected()
    {
        using var harness = new Harness();

        Assert.Null(harness.Main.Metadata.SelectedConnection);
        Assert.False(harness.Main.Metadata.EditSelectedCommand.CanExecute(null));
        Assert.False(harness.Main.Metadata.CopySelectedCommand.CanExecute(null));
        Assert.False(harness.Main.Metadata.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_EnabledWhenConnectionSelected()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        var node = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.SelectedConnection = node;

        Assert.True(harness.Main.Metadata.EditSelectedCommand.CanExecute(null));
        Assert.True(harness.Main.Metadata.CopySelectedCommand.CanExecute(null));
        Assert.True(harness.Main.Metadata.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void EditSelected_ForwardsToOwnerRequestEdit()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();
        ConnectionProfile? received = null;
        harness.Main.EditRequested += p => received = p;

        harness.Main.Metadata.SelectedConnection = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.EditSelectedCommand.Execute(null);

        Assert.Same(profile, received);
    }

    [Fact]
    public void CopySelected_ForwardsToOwnerCopy()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        harness.Main.Metadata.SelectedConnection = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.CopySelectedCommand.Execute(null);

        Assert.Contains(harness.Store.LoadAll(), p => p.Name == "ERP (Copy)");
    }

    [Fact]
    public void DeleteSelected_RemovesProfileFromStore()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        harness.Main.Metadata.SelectedConnection = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.DeleteSelectedCommand.Execute(null);

        Assert.DoesNotContain(harness.Store.LoadAll(), p => p.Id == profile.Id);
    }

    [Fact]
    public void ApplyEditedProfile_PersistsEditsAndRebuildsTree()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        // Edit the SAME connection (same Id), change name + transaction profile.
        var edited = new ConnectionProfile
        {
            Id = profile.Id,
            Name = "ERP-EDITED",
            Host = "h",
            Port = 3050,
            DataTransactionProfile = TransactionProfile.Snapshot,
        };

        harness.Main.ApplyEditedProfile(edited);

        // Persisted in place (same Id, no duplicate) ...
        var all = harness.Store.LoadAll();
        Assert.Single(all);
        Assert.Equal("ERP-EDITED", all[0].Name);
        Assert.Equal(TransactionProfile.Snapshot, all[0].DataTransactionProfile);

        // ... and the sidebar tree reflects the new name.
        Assert.Single(harness.Main.Metadata.Connections);
        Assert.Equal("ERP-EDITED", harness.Main.Metadata.Connections[0].Profile.Name);
    }

    // ══ M5 / M‑3 klasa A — stan pusty paska bocznego ═══════════════════════════════════════════════

    [Fact]
    public void SidebarEmptyState_ShowsOnlyWhileTheRootIsEmpty()
    {
        using var harness = new Harness();

        // Pierwsze uruchomienie: zero profili ⇒ panel nie ma nic w korzeniu.
        harness.Main.ReloadConnections();
        Assert.True(harness.Main.Metadata.ShowEmptyState);

        harness.Store.Upsert(new ConnectionProfile { Name = "A", Host = "h", Port = 3050 });
        harness.Main.ReloadConnections();
        Assert.False(harness.Main.Metadata.ShowEmptyState);
    }

    /// <summary>
    /// ⭐⭐ ASERCJĄ JEST POWIADOMIENIE, NIE WARTOŚĆ. Odczytana wprost właściwość jest poprawna nawet wtedy,
    /// gdy nic o zmianie nie mówi, a wiązanie odpytuje ją WYŁĄCZNIE po <c>PropertyChanged</c> — więc bez tego
    /// testu podpowiedź „dodaj połączenie" zostałaby na ekranie po podłączeniu pierwszego profilu.
    /// Ten sam błąd złapały testy w M3.3b i M3b.2, za każdym razem dopiero przy podsadzeniu naruszenia.
    /// </summary>
    [Fact]
    public void SidebarEmptyState_AnnouncesItsOwnChange()
    {
        using var harness = new Harness();
        harness.Main.ReloadConnections();

        var announced = 0;
        harness.Main.Metadata.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MetadataExplorerViewModel.ShowEmptyState)) announced++;
        };

        harness.Store.Upsert(new ConnectionProfile { Name = "A", Host = "h", Port = 3050 });
        harness.Main.ReloadConnections();

        Assert.True(announced > 0, "ShowEmptyState zmieniło wartość, ale nie powiadomiło — wiązanie nie odświeży ekranu.");
        Assert.False(harness.Main.Metadata.ShowEmptyState);
    }

    /// <summary>
    /// ⚠ Strażnik ŹRÓDŁOWY, i jest konieczny: poprawna właściwość w ViewModelu nie jest tym samym, co element
    /// na ekranie. Bez wiązania w widoku oba testy wyżej byłyby zielone przy pustym pasku bocznym.
    /// ⛔ Pilnuje też TREŚCI: obie stałe muszą być użyte i musi być przy nich GLIF — bo to glif jest powodem,
    /// dla którego wybrano ten wariant (przycisk „New Connection" nie ma na ekranie podpisu).
    /// </summary>
    [Fact]
    public void SidebarEmptyState_IsActuallyBoundInTheView()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "EmberTern.App", "Views", "MainWindow.axaml"));

        Assert.Contains("IsVisible=\"{Binding ShowEmptyState}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{app:Loc SidebarPlaceholderEmpty}", xaml, StringComparison.Ordinal);
        Assert.Contains("{app:Loc ConnectionsEmptyHint}", xaml, StringComparison.Ordinal);
        Assert.Contains("Icon.Plus", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔⛔ Treść, która CYTUJE ETYKIETĘ NIEISTNIEJĄCĄ NA EKRANIE, jest defektem, nie niedokładnością — i taka
    /// dokładnie stała czekała w `UiStrings` osierocona przez cały czas życia produktu („Click “+ New
    /// Connection” to add one."), przy przycisku, który jest samym glifem. Ten strażnik pilnuje, żeby nie
    /// wróciła: napis, którego użytkownik ma szukać, musi zgadzać się z tym, jak akcja nazywa się naprawdę.
    /// </summary>
    [Fact]
    public void SidebarEmptyState_NeverQuotesALabelTheProductDoesNotShow()
    {
        Assert.DoesNotContain("+ New Connection", UiStrings.ConnectionsEmptyHint, StringComparison.Ordinal);
        Assert.DoesNotContain("+ New Connection", UiStrings.SidebarPlaceholderEmpty, StringComparison.Ordinal);

        // Nazwa, na którą wskazuje podpowiedź, to nazwa z tooltipa tego samego przycisku.
        Assert.Contains(UiStrings.ConnectionNewTooltip, UiStrings.ConnectionsEmptyHint, StringComparison.Ordinal);
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
