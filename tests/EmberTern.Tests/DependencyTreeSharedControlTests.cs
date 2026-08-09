using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Strażnicy deduplikacji drzewa „Zależności" — wspólna KONTROLKA (Product Polish M4.2b, decyzja D11).
/// </summary>
/// <remarks>
/// ⚠ <b>Osobna klasa od <c>DependencyTreeTests</c> i to jest podział wg ODPOWIEDZIALNOŚCI, nie wg wygody:</b>
/// tamta testuje MODEL drzewa (<c>BuildDependencyTree</c>, <c>MapObjectTypeToKind</c>, <c>RequestOpen</c>) —
/// czyli co drzewo ZAWIERA; ta testuje jego PREZENTACJĘ — czyli że szablon jest jeden. Model był wspólny
/// na długo przed M4.2b; duplikacja siedziała wyłącznie w warstwie widoku.
/// </remarks>
/// <remarks>
/// <para>
/// ⭐⭐ <b>POMIAR, KTÓRY UZASADNIŁ ETAP:</b> dziewięć edytorów obiektów niosło <b>17 drzew zależności</b>,
/// a ich bloki <c>&lt;TreeView&gt;</c> były <b>bajtowo identyczne</b> poza nazwą bindowanej właściwości
/// i jednym <c>Grid.Row</c>; do tego <b>dziewięć identycznych co do bajtu</b> kopii
/// <c>OnDependencyNodeDoubleTapped</c> w code-behind (ten sam MD5 w każdym pliku).
/// </para>
/// <para>
/// ⚠ Te testy czytają ŹRÓDŁO, i to jest świadome: pytanie brzmi „czy szablon jest jeden", a nie „czy wiersz
/// wygląda tak samo" — na to drugie odpowiada ekran (R16). Guard na wyglądzie byłby zielony również wtedy,
/// gdyby ktoś wkleił siedemnastą kopię wyglądającą identycznie, czyli przepuściłby dokładnie ten dług,
/// którego ten etap się pozbył.
/// </para>
/// </remarks>
public class DependencyTreeSharedControlTests
{
    private const string SharedControl = "Controls/DependencyTreeView.axaml";

    /// <summary>
    /// ⭐ Anty-regresja właściwa dla tego etapu: szablon wiersza zależności ma <b>jednego właściciela</b>.
    /// Wkleić z powrotem lokalny <c>DataTemplate</c> na <c>DependencyGroupNode</c>/<c>DependencyLeafNode</c>
    /// jest łatwo — to najkrótsza droga „żeby ten jeden ekran wyglądał inaczej", i dokładnie tak powstaje
    /// siedemnaście kopii.
    /// </summary>
    [Fact]
    public void OnlyTheSharedControl_DeclaresTheDependencyRowTemplate()
    {
        var appRoot = AppRoot();

        var offenders = Directory
            .EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories)
            .Select(f => (Path: Relative(appRoot, f), Text: WithoutComments(File.ReadAllText(f))))
            .Where(x => x.Path != SharedControl)
            .Where(x => Regex.IsMatch(
                x.Text,
                @"<(?:TreeDataTemplate|DataTemplate)\b[^>]*DataType\s*=\s*""(?:vm:)?Dependency(?:Group|Leaf)Node"""))
            .Select(x => x.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Szablon wiersza drzewa zależności jest zadeklarowany poza wspólną kontrolką:\n  "
            + string.Join("\n  ", offenders)
            + $"\n\n⭐ Jedynym właścicielem jest `{SharedControl}`. M4.2b zdjęło stamtąd 17 identycznych kopii "
            + "tego szablonu; wklejenie własnej kopii cofa ten etap i rozjeżdża wiersz między ekranami.\n"
            + "⚠ Jeżeli JEDEN ekran naprawdę potrzebuje innego wiersza, to jest decyzja projektowa "
            + "(inna rola albo inna kontrolka), a nie kopia szablonu.");
    }

    /// <summary>
    /// ⚠ Obsługa dwukliku wróciła do code-behind = wróciła duplikacja. Handler żyje w kontrolce, na
    /// bąbelkowaniu, i obsługuje wszystkie 17 wystąpień naraz.
    /// </summary>
    [Fact]
    public void NoView_ReintroducesItsOwnDependencyDoubleTapHandler()
    {
        var appRoot = AppRoot();

        var offenders = Directory
            .EnumerateFiles(Path.Combine(appRoot, "Views"), "*.cs", SearchOption.AllDirectories)
            // ⚠ Komentarze odcięte celowo: strażnik ma mierzyć KOD, nie prozę. Licznik `FontSize`
            // w `DesignTokenComplianceTests` liczy również wzmiankę w komentarzu i jest to tam
            // udokumentowany kwirk — tutaj byłby po prostu fałszywym trafieniem, bo opisanie tej
            // historii w komentarzu jest dozwolone i pożądane.
            .Where(f => WithoutLineComments(File.ReadAllText(f))
                .Contains("OnDependencyNodeDoubleTapped", StringComparison.Ordinal))
            .Select(f => Relative(appRoot, f))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Widok znów obsługuje dwuklik drzewa zależności we własnym code-behind:\n  "
            + string.Join("\n  ", offenders)
            + "\n\n⭐ To robi `Controls/DependencyTreeView`, jednym handlerem na bąbelkowaniu. M4.2b usunęło "
            + "dziewięć kopii tej metody, wszystkie identyczne co do bajtu (jeden MD5).");
    }

    /// <summary>
    /// ⭐ Szew nawigacji: wspólna kontrolka nie zna typów ViewModeli, więc pyta przez interfejs. ViewModel,
    /// który wystawia drzewo zależności, ale go nie implementuje, kompiluje się i renderuje poprawnie —
    /// <b>i po prostu nie reaguje na dwuklik</b>. To jest defekt cichy, więc musi mieć strażnika.
    /// </summary>
    [Fact]
    public void EveryViewModelExposingADependencyTree_IsADependencyNavigator()
    {
        var appRoot = AppRoot();

        var offenders = Directory
            .EnumerateFiles(Path.Combine(appRoot, "ViewModels"), "*.cs", SearchOption.AllDirectories)
            .Select(f => (Path: Relative(appRoot, f), Text: File.ReadAllText(f)))
            // Deklaracja kolekcji drzewa, a nie jej użycie — `Clear()`/`Add()` w ciele metody nie czyni
            // ViewModelu właścicielem drzewa.
            .Where(x => Regex.IsMatch(
                x.Text,
                @"ObservableCollection<DependencyGroupNode>\s+(?:DependsOnTree|DependedOnByTree|UsedByTree)\b"))
            .Where(x => !x.Text.Contains("IDependencyNavigator", StringComparison.Ordinal))
            .Select(x => x.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "ViewModel wystawia drzewo zależności, ale nie implementuje `IDependencyNavigator`:\n  "
            + string.Join("\n  ", offenders)
            + "\n\n⚠ Skutek jest CICHY: drzewo narysuje się poprawnie, a dwuklik nic nie zrobi. Wspólna "
            + "kontrolka rozwiązuje nawigatora z `DataContext` i bez interfejsu po prostu go nie znajduje.\n"
            + "⭐ Interfejs ma jedną metodę, którą te ViewModele i tak już miały — dopisanie go to jedno słowo.");
    }

    /// <summary>
    /// ⭐ Wysokość wiersza to ratyfikowana decyzja użytkownika (2026-08-08): wiersz drzewa zależności i wiersz
    /// drzewa w Metadata Explorerze to ten sam rodzaj elementu, więc czytają tę samą rolę.
    /// <para>⚠⚠ Reguła musi być ZAWĘŻONA do tej kontrolki. Globalny styl <c>TreeViewItem</c> niesie
    /// <c>MinHeight="0"</c> i obsługuje też <c>PlanRoots</c> (Performance) oraz <c>Groups</c> (Global Search) —
    /// drzewa spoza tego etapu, których użytkownik wprost nie objął zakresem.</para>
    /// </summary>
    [Fact]
    public void TheSharedControl_TakesItsRowHeightFromTheTreeRowRole()
    {
        var text = File.ReadAllText(Path.Combine(AppRoot(), "Controls", "DependencyTreeView.axaml"));

        // ⚠ Selektor zmieniony w M4.2b razem z mechanizmem: drzewo zależności stoi teraz na płaskiej
        //   `ListBox` nad `SidebarFlatController` (ten sam mechanizm co pasek boczny), więc wysokość
        //   niesie `ListBoxItem`, nie `TreeViewItem`. ⭐ Strażnik POSZEDŁ ZA wysokością, zamiast zniknąć —
        //   dokładnie tak, jak nakazywał jego własny komunikat, gdy zapalił się na tej migracji.
        var setter = Regex.Match(
            text,
            @"<Style\s+Selector=""ListBox\.dependency-list\s+ListBoxItem""\s*>[\s\S]*?"
            + @"<Setter\s+Property=""MinHeight""\s+Value=""(?<v>[^""]+)""");

        // Test, który przechodzi, bo NICZEGO nie dopasował, jest gorszy niż brak testu (R16).
        Assert.True(setter.Success,
            "We wspólnej kontrolce nie ma już stylu wysokości wiersza. Jeżeli wysokość przeniosła się "
            + "gdzie indziej, ten strażnik musi pójść za nią — a nie zniknąć.");

        Assert.Equal("{DynamicResource Size.Row.Tree}", setter.Groups["v"].Value);
    }

    /// <summary>
    /// ⛔ Zakres etapu, zapisany jako test: <c>MemberGroups</c> (zakładka Members pakietu), <c>PlanRoots</c>
    /// (Performance) i <c>Groups</c> (Global Search) to INNE drzewa — inne typy węzłów, inne menu, inne
    /// zadania — i użytkownik wprost wyłączył je z M4.2b.
    /// <para>⚠ Ten test pilnuje, żeby „przy okazji" nie wciągnąć ich do wspólnej kontrolki: to nie byłaby
    /// deduplikacja, tylko sprowadzenie trzech różnych rzeczy do jednej dlatego, że są drzewami
    /// (pułapka 17 — reguła OPISUJE to, co już jest dobre).</para>
    /// </summary>
    [Fact]
    public void TheSharedControl_ServesDependencyTreesOnly()
    {
        var appRoot = AppRoot();

        var offenders = Directory
            .EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories)
            .Select(f => (Path: Relative(appRoot, f), Text: WithoutComments(File.ReadAllText(f))))
            .SelectMany(x => Regex
                .Matches(x.Text, @"<controls:DependencyTreeView\b[^>]*ItemsSource=""\{Binding\s+(?<src>\w+)\}""")
                .Select(m => (x.Path, Source: m.Groups["src"].Value)))
            .Where(t => t.Source is not ("DependsOnTree" or "DependedOnByTree" or "UsedByTree"))
            .Select(t => $"{t.Path}: {t.Source}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Wspólna kontrolka drzewa zależności obsługuje kolekcję, która drzewem zależności nie jest:\n  "
            + string.Join("\n  ", offenders)
            + "\n\n⛔ `MemberGroups`, `PlanRoots` i `Groups` są POZA zakresem M4.2b (decyzja użytkownika) — "
            + "mają własne typy węzłów, własne menu i własne zadania.");
    }

    /// <summary>
    /// ⭐ Nawigacja ←/→ ma być IDENTYCZNA w obu drzewach — wymóg użytkownika postawiony wprost przy odbiorze
    /// M4.2b. Reguła jest jedna (<c>SidebarFlatController.Navigate</c>), ale samo jej istnienie nie wystarcza:
    /// drzewo, które się nie wpięło, po prostu nie reaguje na klawisze.
    /// </summary>
    /// <remarks>
    /// ⚠ To jest defekt CICHY — drzewo renderuje się poprawnie, myszą działa wszystko, a klawiatura milczy
    /// tylko w jednym z dwóch miejsc. Żaden test zachowania kontrolera go nie złapie, bo kontroler jest
    /// wtedy w pełni sprawny; brakuje WPIĘCIA, a to widać wyłącznie w źródle widoku.
    /// </remarks>
    [Fact]
    public void BothTrees_WireTheSharedKeyboardNavigation()
    {
        var appRoot = AppRoot();

        var expected = new[]
        {
            "Views/MainWindow.axaml.cs",             // drzewo połączenia
            "Controls/DependencyTreeView.axaml.cs",  // drzewa „Zależności"
        };

        // ⚠ Komentarze odcięte — ten sam powód i ta sama funkcja co przy strażniku dwukliku: zakomentowane
        //   wpięcie NIE jest wpięciem, a strażnik czytający surowy tekst uznałby je za obecne. Wykryło to
        //   podsadzenie naruszenia, nie przegląd kodu.
        var missing = expected
            .Where(rel => !WithoutLineComments(
                    File.ReadAllText(Path.Combine(appRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .Contains("SidebarKeyboardNavigation.Attach", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Drzewo nie wpina wspólnej nawigacji klawiaturą ←/→:\n  " + string.Join("\n  ", missing)
            + "\n\n⚠ Skutek jest CICHY: drzewo wygląda poprawnie i działa myszą, a klawiatura milczy tylko "
            + "w jednym z dwóch miejsc — czyli dokładnie ten rozjazd, którego ten etap miał się pozbyć.\n"
            + "⭐ Reguła żyje w `SidebarFlatController.Navigate`; widok wnosi wyłącznie wpięcie.");
    }

    // ─── pomocnicze ────────────────────────────────────────────────────────────────────────────────
    private static string Relative(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    private static string WithoutComments(string text) =>
        Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);

    private static string WithoutLineComments(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string AppRoot() => Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

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
