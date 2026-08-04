using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ Menu kontekstowe paska bocznego są WSPÓŁDZIELONE — jedna instancja na rodzaj węzła, nigdy jedna
/// na wiersz (M3.4b, <c>product-polish.md</c> §19.28).
///
/// <para><b>Przed czym to chroni.</b> Menu inline w <c>DataTemplate</c> wygląda niewinnie i jest wygodne
/// w edycji, a kosztuje <b>~74% czasu przewijania</b> najgęstszego widoku aplikacji: szablon dostaje każdy
/// zrealizowany wiersz, więc menu z 22 pozycjami było tworzone i wyrzucane 1 640 razy na jedno przewinięcie
/// 5 000 wierszy (pomiar: <c>SharedContextMenuFeasibilityProbe</c>).</para>
///
/// <para>⚠⚠ <b>Regresja byłaby CICHA.</b> Dopisanie menu inline do szablonu wiersza kompiluje się, działa
/// poprawnie funkcjonalnie i nie zapala żadnego innego testu — widać ją dopiero jako szarpanie przy
/// przewijaniu u użytkownika z dużą bazą. Dokładnie ten kształt defektu, dla którego w tym projekcie
/// istnieją strażniki źródła (#284).</para>
///
/// <para>⛔ Gdy kiedyś któryś wiersz paska bocznego BĘDZIE potrzebował własnego menu per instancja —
/// ten test ma <b>upaść</b>, a odpowiedzią jest wyjątek z zapisanym powodem, nie rozluźnienie warunku.</para>
/// </summary>
public class SidebarMenuInstancingTests
{
    /// <summary>
    /// Szablony wierszy paska bocznego nie deklarują menu inline. Zakres jest celowo wąski — trzy szablony
    /// wierszy listy `SidebarList`, bo tylko one są mnożone przez wirtualizację.
    /// </summary>
    [Fact]
    public void SidebarRowTemplates_DeclareNoInlineContextMenu()
    {
        var text = MainWindowSource();

        string[] rowTemplates =
        {
            "vm:FolderNodeViewModel",
            "vm:ConnectionNodeViewModel",
            "vm:MetadataNodeViewModel",
        };

        var offenders = rowTemplates
            .Select(t => (Type: t, Body: TemplateBody(text, t)))
            .Where(x => x.Body is not null && x.Body.Contains("<ContextMenu", StringComparison.Ordinal))
            .Select(x => x.Type)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Szablon wiersza paska bocznego deklaruje menu INLINE: " + string.Join(", ", offenders) +
            "\n\nKażdy zrealizowany wiersz zbuduje wtedy własną kopię menu — zmierzone ~74% czasu "
            + "przewijania i 440 żywych MenuItem zamiast 22 (product-polish.md §19.28). Menu należy do "
            + "`ListBox.Resources` z `x:Key` i `x:DataType`, a szablon odwołuje się do niego przez "
            + "`ContextMenu=\"{StaticResource …}\"`.");
    }

    /// <summary>
    /// Każde odwołanie <c>ContextMenu="{StaticResource X}"</c> ma zadeklarowany zasób <c>X</c>.
    /// ⚠ To NIE jest ceremonia: nierozwiązany <c>StaticResource</c> rzuca dopiero w chwili realizacji
    /// wiersza — czyli po połączeniu z bazą i rozwinięciu kategorii, a nie przy starcie. Smoke tego nie
    /// złapie, bo pusty pasek boczny nie realizuje ani jednego wiersza metadanych.
    /// </summary>
    [Fact]
    public void EverySharedMenuReference_HasItsResource()
    {
        var text = MainWindowSource();

        var referenced = Regex.Matches(text, @"ContextMenu=""\{StaticResource\s+(?<key>[A-Za-z0-9_.]+)\}""")
            .Select(m => m.Groups["key"].Value)
            .Distinct()
            .ToList();

        Assert.True(referenced.Count >= 3,
            $"Znaleziono tylko {referenced.Count} odwołań do współdzielonego menu — wzorzec przestał "
            + "pasować albo menu wróciły do szablonów. Skan, który po cichu nic nie znajduje, „przechodzi” "
            + "zawsze.");

        var declared = Regex.Matches(text, @"<ContextMenu\s+x:Key=""(?<key>[A-Za-z0-9_.]+)""")
            .Select(m => m.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = referenced.Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0,
            "Odwołanie do nieistniejącego zasobu menu: " + string.Join(", ", missing) +
            "\n\nNierozwiązany StaticResource rzuci dopiero przy realizacji wiersza — po połączeniu "
            + "i rozwinięciu kategorii. Build i smoke przechodzą.");
    }

    /// <summary>
    /// ⭐ Każde współdzielone menu deklaruje <c>x:DataType</c>.
    ///
    /// <para>⚠⚠ W <c>DataTemplate</c> typ kontekstu brał się z jego <c>DataType</c> — niejawnie i za darmo.
    /// W zasobach tego rodzica nie ma. Przy bindingach KOMPILOWANYCH brak <c>x:DataType</c> jest błędem
    /// budowania (~30 × AVLN2000, zmierzone przy tej zmianie), ale przy refleksyjnych byłby CISZĄ: puste
    /// menu przy prawym kliknięciu i zielony build. Ten test pilnuje, żeby kontrakt został jawny nawet
    /// wtedy, gdy kompilator akurat nie zmusza.</para>
    /// </summary>
    [Fact]
    public void EverySharedMenu_DeclaresItsDataType()
    {
        var text = MainWindowSource();

        var withoutType = Regex.Matches(text, @"<ContextMenu\s+x:Key=""(?<key>[A-Za-z0-9_.]+)""(?<rest>[^>]*)>")
            .Where(m => !m.Groups["rest"].Value.Contains("x:DataType", StringComparison.Ordinal))
            .Select(m => m.Groups["key"].Value)
            .ToList();

        Assert.True(withoutType.Count == 0,
            "Współdzielone menu bez `x:DataType`: " + string.Join(", ", withoutType) +
            "\n\nMenu w zasobach nie ma rodzica, z którego wziąłby typ kontekstu. Bez jawnej deklaracji "
            + "pierwszy błędnie wpisany binding przestaje być błędem kompilacji.");
    }

    // Ciało szablonu danego typu: od jego otwarcia do początku następnego szablonu (albo końca pliku).
    private static string? TemplateBody(string text, string dataType)
    {
        var open = text.IndexOf($"<DataTemplate DataType=\"{dataType}\">", StringComparison.Ordinal);
        if (open < 0) return null;

        var next = text.IndexOf("<DataTemplate DataType=", open + 10, StringComparison.Ordinal);
        return next < 0 ? text[open..] : text[open..next];
    }

    /// <summary>
    /// ⛔⛔ Lista paska bocznego MUSI mieć <c>AutoScrollToSelectedItem="False"</c> — to jest naprawa
    /// przyczyny defektu „drzewo samo przewija się w dół i zawiesza aplikację"
    /// (<c>metadata-refresh-analysis.md</c> §11).
    ///
    /// <para><b>Dlaczego to potrzebuje strażnika.</b> Ta właściwość wygląda jak coś, co ktoś kiedyś
    /// „posprząta" — jest domyślnie <c>true</c>, jej usunięcie <b>nie psuje żadnego innego testu</b>,
    /// nie rusza wyglądu i nie zmienia niczego w codziennej pracy. Defekt wraca dopiero u użytkownika
    /// z bardzo dużą bazą, który ma coś zaznaczone i rozwinie dużą kategorię — czyli w warunkach,
    /// których nie odtwarza ani smoke, ani żaden pomiar syntetyczny.</para>
    ///
    /// <para>⚠⚠ Dwa moje wcześniejsze pomiary tego zjawiska <b>nie zobaczyły</b>, i to nie przez błąd
    /// pomiaru: w żadnym <b>nic nie było zaznaczone</b>, więc automatyczne przewijanie nie miało czego
    /// gonić. Zmienna decydująca o całym zjawisku nie występowała w eksperymencie. Ten test jest jedyną
    /// rzeczą w repozytorium, która o niej pamięta.</para>
    ///
    /// <para>⛔ Gdyby kiedyś okazało się, że nawigacja klawiaturą tego potrzebuje — odpowiedzią jest
    /// rozwiązanie <b>dla nawigacji klawiaturą</b>, nie powrót globalnego auto-scrolla (decyzja
    /// użytkownika, 2026-08-04).</para>
    /// </summary>
    [Fact]
    public void SidebarList_DisablesAvaloniaAutoScrollToSelectedItem()
    {
        var text = MainWindowSource();

        var element = Regex.Match(
            text, @"<ListBox\b(?<attrs>[^>]*?x:Name=""SidebarList""[^>]*)>", RegexOptions.Singleline);

        Assert.True(element.Success,
            "Nie znaleziono elementu ListBox o nazwie SidebarList. Jeśli pasek boczny został "
            + "przebudowany, ten strażnik musi pójść za nim — a nie zniknąć.");

        var attrs = element.Groups["attrs"].Value;

        Assert.True(
            Regex.IsMatch(attrs, @"AutoScrollToSelectedItem\s*=\s*""False""", RegexOptions.IgnoreCase),
            "SidebarList nie wyłącza `AutoScrollToSelectedItem`.\n\n"
            + "Domyślne `true` powoduje, że po rozwinięciu dużej kategorii Avalonia próbuje przewinąć do "
            + "zaznaczonego wiersza, który leży poza oknem realizacji. VirtualizingStackPanel nie potrafi "
            + "skoczyć do nierealizowanego indeksu, więc pełznie po JEDNYM wierszu (24 px) na cykl "
            + "Dispatchera — zagładzając priorytet tła, przez co aplikacja przestaje reagować na "
            + "kliknięcia. Zmierzone na żywym logu użytkownika: metadata-refresh-analysis.md §11.\n\n"
            + "Pozycja menu 'Pokaż w Metadata Explorer' ma WŁASNE, jawne ScrollIntoView "
            + "(OnRevealSidebarRow) — drugi, automatyczny mechanizm jest tu zbędny.");
    }

    private static string MainWindowSource()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), $"MainWindow.axaml nie istnieje pod {path}");
        // Komentarze wycięte — opis wzorca nie może uchodzić za jego wystąpienie.
        return Regex.Replace(File.ReadAllText(path), "<!--.*?-->", " ", RegexOptions.Singleline);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
