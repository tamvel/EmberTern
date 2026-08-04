using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.App.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ Strażnik instrumentu diagnostycznego drzewa — <b>napisany po awarii, przeciw jej PRZYCZYNIE</b>.
///
/// <para><b>Co się stało.</b> Pierwsza wersja <see cref="TreeDiagnostics"/> składała wiersz logu przez
/// <c>string.Format</c> z wyrównaniem <c>{4,+8:0.0}</c>. Wyrównanie w formacie złożonym przyjmuje
/// <b>wyłącznie liczbę całkowitą</b>, więc <c>+</c> jest błędem składni — <c>FormatException</c> poleciał
/// w górę przez handler <c>PropertyChanged</c> ScrollViewera prosto do Avalonii i <b>zabił proces przy
/// pierwszym rozwinięciu kategorii</b>. Build był zielony i pozostałby zielony, bo format złożony to
/// mini-język interpretowany dopiero w czasie wykonania.</para>
///
/// <para>⚠⚠ <b>Najgorsze było to, CO ta awaria zniszczyła:</b> narzędzie miało złapać cudzy defekt,
/// a stało się jego przyczyną — log użytkownika opisywał wyłącznie błąd instrumentacji. Dlatego nie
/// wystarczy poprawić jednego znaku.</para>
///
/// <para>⭐ Dwie linie obrony, obie tutaj: <b>(1)</b> funkcje składające wiersze są wydzielone i czyste,
/// więc dają się wykonać wrogim wejściem bez pliku i bez flagi; <b>(2)</b> skan źródła pilnuje samej
/// decyzji — w tej klasie nie wolno wrócić do formatu złożonego.</para>
/// </summary>
public class TreeDiagnosticsFormattingTests
{
    /// <summary>
    /// Wrogie wejście do wiersza SCROLL. ⚠ `NaN` i nieskończoności są tu <b>celowo</b>: geometria
    /// przewijania potrafi je przyjąć w trakcie przeliczania układu, a instrument ma wtedy <b>pokazać
    /// dziwną wartość</b>, nie wyłożyć się na niej.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, "Offset/Extent")]
    [InlineData(1234.5, 162090.0, 600.0, -12.5, 0.0, 20, "Offset/Extent")]
    [InlineData(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, "NaN")]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity, 0, 0, 0, -1, "∞")]
    [InlineData(double.MaxValue, double.MinValue, 0, double.Epsilon, 0, int.MaxValue, "skrajne")]
    [InlineData(-1, -1, -1, -1, -1, int.MinValue, null)]
    public void ScrollLine_NeverThrows(
        double offsetY, double extentH, double viewportH, double dOffset, double dExtent,
        int realized, string? source)
    {
        var line = TreeDiagnostics.FormatScrollLine(
            offsetY, extentH, viewportH, dOffset, dExtent, realized, source);

        Assert.False(string.IsNullOrEmpty(line));
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    /// <summary>
    /// ⚠⚠ Nazwa źródła to zwykły tekst, który MOŻE zawierać nawiasy klamrowe. Przy formacie złożonym
    /// byłby to natychmiastowy <c>FormatException</c> — czyli druga postać dokładnie tej samej awarii.
    /// </summary>
    [Theory]
    [InlineData("{0}")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("{4,+8:0.0}")]
    [InlineData("{{nested}}")]
    public void ScrollLine_SurvivesBracesInText(string source)
    {
        var line = TreeDiagnostics.FormatScrollLine(1, 2, 3, 4, 5, 6, source);
        Assert.Contains(source, line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Add", 0, 1, -1, 0, 1)]
    [InlineData("Reset", -1, 0, -1, 0, 0)]
    [InlineData(null, int.MinValue, int.MaxValue, int.MinValue, int.MaxValue, -1)]
    [InlineData("{Add}", 5, 5, 5, 5, 5)]
    public void CollectionLine_NeverThrows(
        string? action, int newIndex, int newCount, int oldIndex, int oldCount, int total)
    {
        var line = TreeDiagnostics.FormatCollectionLine(action, newIndex, newCount, oldIndex, oldCount, total);
        Assert.False(string.IsNullOrEmpty(line));
    }

    /// <summary>
    /// ⭐⭐ Skan źródła — pilnuje DECYZJI, nie pojedynczego znaku.
    ///
    /// <para>⛔ W <c>TreeDiagnostics</c> nie wolno używać formatu złożonego (<c>string.Format</c>,
    /// <c>string.Create(…, $"…")</c>, interpolacji z wyrównaniem <c>{x,-8}</c> lub specyfikatorem
    /// <c>{x:0.0}</c>). Każda z tych form jest parsowana <b>w czasie wykonania</b>, więc literówka
    /// przechodzi build i wybucha u użytkownika — w kodzie, którego jedynym zadaniem jest NIE wywalić
    /// aplikacji.</para>
    ///
    /// <para>⚠ Ten test celowo patrzy tylko na ten jeden plik. Reguła „bez formatów złożonych" nie jest
    /// regułą projektu — jest regułą <b>instrumentu</b>, który biegnie w cudzych callbackach.</para>
    /// </summary>
    [Fact]
    public void TreeDiagnostics_UsesNoRuntimeParsedFormatStrings()
    {
        var path = Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Diagnostics", "TreeDiagnostics.cs");
        Assert.True(File.Exists(path), $"Nie znaleziono {path}");

        var code = File.ReadAllText(path);
        // Komentarze i dokumentacja XML wycięte — one ten wzorzec OPISUJĄ i muszą móc go nazwać.
        code = Regex.Replace(code, @"^\s*///.*$", string.Empty, RegexOptions.Multiline);
        code = Regex.Replace(code, @"//.*$", string.Empty, RegexOptions.Multiline);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        var offenders = new[]
        {
            (Pattern: @"string\.Format\s*\(", What: "string.Format"),
            (Pattern: @"string\.Create\s*\(\s*[A-Za-z]", What: "string.Create z formatem"),
            (Pattern: @"\$""[^""]*\{[A-Za-z_][A-Za-z0-9_.()?\[\]]*\s*[,:]", What: "interpolacja z wyrównaniem lub specyfikatorem"),
        }
        .Where(p => Regex.IsMatch(code, p.Pattern))
        .Select(p => p.What)
        .ToList();

        Assert.True(offenders.Count == 0,
            "TreeDiagnostics wrócił do formatu parsowanego w czasie wykonania: "
            + string.Join(", ", offenders) +
            "\n\nDokładnie ta konstrukcja zabiła proces użytkownika: `{4,+8:0.0}` — wyrównanie przyjmuje "
            + "tylko liczbę całkowitą, więc `+` był błędem składni widocznym dopiero w czasie wykonania. "
            + "Wiersz logu składaj z osobno sformatowanych kawałków (`ToString`) — sklejanie nie ma czego "
            + "sparsować, więc nie ma jak rzucić.");
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
