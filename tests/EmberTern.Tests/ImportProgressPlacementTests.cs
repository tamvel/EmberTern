using System;
using System.IO;
using System.Linq;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// §19.33 — postęp trwającego importu jest pokazywany w JEDNYM miejscu, a pasek poleceń nie rośnie w chwili
/// startu importu.
///
/// <para>⚠⚠ <b>DLACZEGO TE ASERCJE CZYTAJĄ XAML, A NIE EKRAN.</b> Przedmiotem jest DOPASOWANIE paska, a to
/// ocenia się okiem (R16) — test na szerokości byłby zielony przy brzydkim ekranie. Maszyna ma tu jednak coś
/// sensownego do powiedzenia o dwóch rzeczach: <b>ile razy ten sam fakt jest wiązany</b> (wymóg użytkownika:
/// *„nie dublujemy informacji"*) i <b>czy pasek poleceń znów zaczął dokować elementy pojawiające się dopiero
/// w trakcie importu</b> (mechanizm, który spowodował przycięcie).</para>
///
/// <para>⭐ Mechanizm wart zapamiętania, bo powrót do niego wygląda niewinnie: band B to <c>DockPanel</c>
/// z <c>LastChildFill</c>. Element dokowany z prawej bierze swój rozmiar PIERWSZY, a przyciski są ostatnim
/// dzieckiem — poziomy <c>StackPanel</c>, który <b>się nie zwęża, tylko PRZYCINA</b>. Dołożenie tam czegoś
/// widocznego tylko w trakcie przebiegu ukrywa przyciski dokładnie wtedy, gdy użytkownik patrzy.</para>
/// </summary>
public class ImportProgressPlacementTests
{
    private static string ViewSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "EmberTern.App", "Views", "DataImportTabView.axaml");
        Assert.True(File.Exists(path), $"nie znaleziono {path}");
        return File.ReadAllText(path);
    }

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }
        return n;
    }

    [Theory]
    [InlineData("{Binding ProgressText}")]
    [InlineData("{Binding ProgressPercent}")]
    [InlineData("{Binding IsProgressIndeterminate}")]
    public void EachProgressFact_IsBoundExactlyOnce(string binding)
    {
        // Wymóg użytkownika po QA M3b.1: pasek statusu już mówi, że import trwa, więc powierzchnia importu
        // nie pokazuje tego drugi raz. Dwa wiązania tego samego faktu = dwa miejsca do rozjechania się.
        Assert.Equal(1, Count(ViewSource(), binding));
    }

    /// <summary>
    /// ⛔ Pasek poleceń (band B) nie może zawierać elementu dokowanego z prawej, który pojawia się dopiero
    /// w trakcie przebiegu. Czas trwania jest wyjątkiem i jest nim świadomie: monospace, ~60 px, i pasek
    /// statusu go nie niesie.
    /// </summary>
    [Fact]
    public void TheCommandBar_DocksNothingThatAppearsOnlyWhileRunning_ExceptTheElapsedTimer()
    {
        var source = ViewSource();

        // Band B kończy się na swoim </DockPanel>; pierwszy w pliku należy właśnie do niego.
        var start = source.IndexOf("<DockPanel LastChildFill=\"True\">", StringComparison.Ordinal);
        Assert.True(start > 0, "nie znaleziono paska poleceń (band B)");
        var end = source.IndexOf("</DockPanel>", start, StringComparison.Ordinal);
        Assert.True(end > start, "nie znaleziono końca paska poleceń");
        var band = source[start..end];

        Assert.DoesNotContain("<ProgressBar", band, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding ProgressText}", band, StringComparison.Ordinal);

        // Jedyny dokowany z prawej element to licznik czasu — jeden, nie dwa.
        Assert.Equal(1, Count(band, "DockPanel.Dock=\"Right\""));
        Assert.Contains("{Binding Timer.ElapsedDisplay}", band, StringComparison.Ordinal);
    }

    /// <summary>
    /// Postęp mieszka w dolnym panelu i jest widoczny niezależnie od wybranej zakładki — czyli jako nakładka
    /// (<c>ZIndex</c>) obok chevronu zwijania, nie w treści którejkolwiek zakładki. Schowany w Raporcie
    /// pokazywałby się tylko temu, kto akurat tam patrzy, a Raport w trakcie przebiegu jest jeszcze pusty.
    /// </summary>
    [Fact]
    public void Progress_LivesInTheBottomPanelOverlay_NotInsideATab()
    {
        var source = ViewSource();

        var progressAt = source.IndexOf("{Binding ProgressText}", StringComparison.Ordinal);
        var tabsAt = source.IndexOf("<TabControl x:Name=\"BottomTabs\"", StringComparison.Ordinal);
        Assert.True(progressAt > 0 && tabsAt > 0);

        // Nakładka jest rodzeństwem TabControl-a i stoi PRZED nim, więc nie jest w treści żadnej zakładki.
        Assert.True(progressAt < tabsAt,
            "postęp trafił do wnętrza zakładki — byłby widoczny tylko przy tej jednej wybranej");

        // I jest nakładką, a nie własnym wierszem: wiersz przesuwałby zakładki w dół w chwili startu importu.
        var overlay = source[..tabsAt];
        var lastZIndex = overlay.LastIndexOf("ZIndex=\"1\"", StringComparison.Ordinal);
        Assert.True(lastZIndex > 0 && lastZIndex < tabsAt, "postęp w dolnym panelu nie jest nakładką");
    }
}
