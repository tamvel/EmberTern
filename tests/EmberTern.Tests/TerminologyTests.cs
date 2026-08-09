using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmberTern.App;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Product Polish M5 / M‑4 — strażnik słownika terminologii (ryzyko **R‑8**).
/// Norma: <c>docs/design/terminology.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>Ten strażnik pilnuje SŁOWNIKA, nie stylu.</b> Nie sprawdza, czy tekst jest ładny — sprawdza, czy
/// dla danej operacji użyto czasownika, który słownik jej przypisuje. Powód jest merytoryczny: w EmberTernie
/// <c>Drop</c> i <c>Delete</c> <b>niosą różną informację</b> (czy wykona się DDL <c>DROP</c>), więc pomylenie
/// ich nie jest niezgrabnością, tylko wprowadzeniem użytkownika w błąd co do tego, co się stanie z bazą.
/// </para>
/// <para>
/// ⚠⚠ <b>Dlaczego lista wyjątków jest jawna i dlaczego to jest jej wartość.</b> Nie da się z samego napisu
/// wywnioskować, czy operacja generuje <c>DROP</c> — to fakt o KODZIE, nie o stringu. Strażnik trzyma więc
/// tablicę „ta stała opisuje operację DDL DROP", a jej prawdziwą funkcją jest to, że <b>dopisanie się do niej
/// zmusza autora do zadeklarowania strony granicy</b> (ten sam wzorzec, co lista plików w
/// <c>DatePresentationTests</c>). ⛔ Nie zamieniać jej na heurystykę po nazwie stałej — nazwy kłamią:
/// <c>FieldsContextMenuDrop</c> niosło tekst „Delete field", a <c>GeneratorDeleteConfirmFormat</c> — „Drop
/// generator".
/// </para>
/// </remarks>
public class TerminologyTests
{
    /// <summary>
    /// Stałe opisujące operację, która generuje <c>DROP</c> w bazie — muszą mówić „Drop".
    /// ⚠ Ustalone przez odczytanie KODU wykonującego akcję, nie przez zgadywanie z nazwy.
    /// </summary>
    private static readonly string[] DropOperations =
    [
        // Drzewo metadanych — DROP <dowolny obiekt schematu>.
        "MetadataContextDeleteTable", "MetadataDeleteTableConfirmTitle", "MetadataDeleteTableConfirmYes",
        "MetadataContextDelete", "MetadataDeleteObjectConfirmTitle", "MetadataDeleteObjectConfirmYes",
        "MetadataContextDeleteUser", "MetadataContextDropRole",
        // Edytory obiektów — DROP GENERATOR / EXCEPTION / INDEX / DOMAIN / PACKAGE.
        "GeneratorDeleteTooltip", "GeneratorDeleteConfirmTitle", "GeneratorDeleteConfirmYes",
        "ExceptionDeleteTooltip", "ExceptionDeleteConfirmTitle", "ExceptionDeleteConfirmYes",
        "IndexDeleteTooltip", "IndexDeleteConfirmTitle", "IndexDeleteConfirmYes",
        "DomainDeleteTooltip", "DomainDeleteConfirmTitle", "DomainDeleteConfirmYes",
        "PackageDeleteConfirmTitle", "PackageDeleteConfirmYes",
        // Tabela — ALTER TABLE … DROP <pole> / DROP CONSTRAINT, oraz DROP INDEX z zakładki Indeksy.
        "FieldEditDropTooltip", "FieldEditDropConfirmTitle", "FieldEditDropConfirmYes", "FieldsContextMenuDrop",
        "ConstraintMenuDropPrimaryKey", "ConstraintMenuDropForeignKey", "ConstraintMenuDropCheck",
        "ConstraintMenuDropUnique", "ConstraintDropConfirmTitle", "ConstraintDropConfirmYes",
        "FieldsContextMenuDropForeignKey", "IndexMenuDrop", "IndexDropConfirmTitle", "IndexDropConfirmYes",
        // Security — DROP USER / DROP ROLE.
        "SecurityDeleteUser", "SecurityDeleteUserTitle", "SecurityDropRole", "SecurityDropRoleTitle",
        // Data Import — DROP TABLE utworzonej tabeli. ⚠ Od 2026-08-10 NIE jest to wyjątek, tylko zwykłe
        //   zastosowanie reguły: import naprawdę wykonuje DROP.
        "ImportConfirmDropTableTitle", "ImportConfirmDropTableConfirm",
    ];

    /// <summary>
    /// Stałe opisujące usunięcie, które NIE jest operacją <c>DROP</c> — muszą mówić „Delete".
    /// </summary>
    private static readonly string[] DeleteOperations =
    [
        "ConnectionDelete", "ConnectionDeleteTooltip", "ConnectionDeleteConfirmTitle", "ConnectionDeleteConfirmYes",
        "FolderContextDelete", "FolderDeleteConfirmTitle", "FolderDeleteConfirmYes",
        "QueryDeleteTooltip", "QueryDeleteConfirmTitle", "QueryDeleteConfirmYes", "QueryContextDelete",
        "ImportProfileDeleteTooltip", "ImportProfileDeleteTitle", "ImportProfileDeleteConfirm",
        // Wiersz danych — DELETE FROM, więc „Delete" zgadza się i ze słownikiem, i z SQL-em.
        "DataEditDeleteRowTooltip", "DataEditDeleteRow", "DataEditDeleteConfirmTitle", "DataEditDeleteConfirmYes",
    ];

    /// <summary>
    /// Stałe opisujące usunięcie elementu z EDYTOWANEJ KOLEKCJI (bufor, nie baza) — muszą mówić „Remove".
    /// </summary>
    private static readonly string[] RemoveOperations =
    [
        "FilterRemoveConditionTooltip", "AggregationRemoveLineTooltip",
        "ViewColumnDeleteTooltip", "ProcedureParamDeleteTooltip", "FunctionArgumentDeleteTooltip",
        "CollectionMenuDelete", "CommandTitleCollectionRemove",
    ];

    [Fact]
    public void EveryDropOperationSaysDrop() => AssertVerb(DropOperations, "Drop", ["Delete", "Remove"]);

    [Fact]
    public void EveryPlainDeletionSaysDelete() => AssertVerb(DeleteOperations, "Delete", ["Drop", "Remove"]);

    [Fact]
    public void EveryCollectionRemovalSaysRemove() => AssertVerb(RemoveOperations, "Remove", ["Drop", "Delete"]);

    /// <summary>
    /// ⛔ <c>Run</c> nie jest synonimem <c>Execute</c>. Wolno go użyć tylko tam, gdzie znaczy co innego —
    /// „Run to cursor" — albo jako STAN przebiegu („Running…", „Not run"). Wyjątki są wypisane, więc dopisanie
    /// kolejnego wymaga świadomej decyzji.
    /// </summary>
    [Fact]
    public void RunIsNeverASynonymForExecute()
    {
        string[] allowed =
        [
            "CommandTitleDebuggerRunToCursor", "DebuggerRunToCursorMenu", "DebuggerRunToCursorTooltip",
            "ScriptResultNotRun", "ScriptStatusRunning", "ScriptStatusNothingToRun",
        ];

        // ⚠⚠ WYŁĄCZNIE KRÓTKIE ETYKIETY — i to jest poprawka MOJEGO testu, nie produktu. Pierwsza wersja
        //   skanowała wszystkie stałe i zapaliła się na PROZIE, w której „run" jest zwykłym angielskim
        //   słowem („the run stopped", „this run wasn't measured", „runs in its own transaction"). Reguła
        //   §1.2 dotyczy CZASOWNIKA AKCJI na etykiecie, a nie występowania słowa w zdaniu; test, który tego
        //   nie rozróżnia, raportuje coś innego, niż mówi jego nazwa (#333).
        var offenders = Strings()
            .Where(s => !allowed.Contains(s.Name, StringComparer.Ordinal))
            .Where(s => s.Value.Length <= 24)
            .Where(s => ContainsWord(s.Value, "Run") || ContainsWord(s.Value, "run"))
            .Select(s => $"{s.Name} = [{s.Value}]")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "§1.2 terminology.md — Run nie jest synonimem Execute. Znaleziono:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>⚠ „Rollback" jako NAZWA operacji jest jednym słowem. W zdaniu, jako czasownik frazowy,
    /// „roll back" zostaje — §2.4, i dlatego sprawdzane są wyłącznie krótkie etykiety.</summary>
    [Fact]
    public void RollbackIsOneWordInLabels()
    {
        var offenders = Strings()
            .Where(s => s.Value.Length <= 34 && s.Value.Contains("Roll back", StringComparison.Ordinal))
            .Select(s => $"{s.Name} = \"{s.Value}\"")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "§1 terminology.md — Rollback jest jednym slowem w etykietach. Znaleziono:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⚠⚠ Strażnik NIEAKTUALNYCH WPISÓW — bez niego tablice wyżej cicho gniją. Stała, która zniknęła albo
    /// została przemianowana, zostawiłaby w liście martwą nazwę, a test dalej świeciłby na zielono
    /// i „pilnował" czegoś, czego nie ma (#333).
    /// </summary>
    [Fact]
    public void NoTerminologyEntryIsStale()
    {
        var known = Strings().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var stale = DropOperations.Concat(DeleteOperations).Concat(RemoveOperations)
            .Where(n => !known.Contains(n))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "Wpisy w TerminologyTests nie mają już odpowiadających stałych w UiStrings:\n  "
            + string.Join("\n  ", stale));
    }

    private static void AssertVerb(IReadOnlyList<string> names, string expected, string[] forbidden)
    {
        var byName = Strings().ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var name in names)
        {
            if (!byName.TryGetValue(name, out var value)) continue; // nieaktualność łapie osobny test
            if (ContainsWord(value, expected)) continue;

            var wrong = forbidden.FirstOrDefault(f => ContainsWord(value, f));
            offenders.Add($"{name} = [{value}] -> oczekiwano: {expected}" +
                          (wrong is null ? string.Empty : $", jest: {wrong}"));
        }

        // ⚠ Komunikat celowo bez polskich cudzysłowów: para „ + ASCII " wewnątrz interpolowanego stringa
        //   zamyka literał, a ta pułapka wystąpiła już w M4.3, M4.4 i tutaj (trzeci raz).
        Assert.True(
            offenders.Count == 0,
            $"terminology.md par.1 - ta operacja musi mowic: {expected}\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Dopasowanie CAŁEGO słowa — inaczej „Delete" trafiałoby w „Deleted", a „Run" w „Running".</summary>
    private static bool ContainsWord(string text, string word)
    {
        var i = text.IndexOf(word, StringComparison.Ordinal);
        while (i >= 0)
        {
            var beforeOk = i == 0 || !char.IsLetter(text[i - 1]);
            var after = i + word.Length;
            var afterOk = after >= text.Length || !char.IsLetter(text[after]);
            if (beforeOk && afterOk) return true;
            i = text.IndexOf(word, i + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static IEnumerable<(string Name, string Value)> Strings()
    {
        foreach (var f in typeof(UiStrings).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType != typeof(string)) continue;
            if (f.GetValue(null) is string v) yield return (f.Name, v);
        }
    }
}
