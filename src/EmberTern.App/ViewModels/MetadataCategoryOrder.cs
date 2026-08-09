using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// JEDNA kanoniczna kolejność kategorii obiektów — czytają ją wszystkie drzewa aplikacji.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>Powstała w M4.2b, bo dwa drzewa miały dwie własne tablice i użytkownik zobaczył skutek:</b>
/// wspólne kategorie stały w innych miejscach — Trigger, Function, Generator, Domain i Package —
/// wyłącznie dlatego, że każdy mechanizm trzymał swoją listę. Zmierzone przed zmianą:
/// </para>
/// <code>
/// połączenie: Table View Procedure Trigger Function Generator Domain Package Exception Role User Index SystemTable
/// zależności: Domain Table View Procedure Function Package Trigger Exception Generator Index
/// </code>
/// <para>
/// ⭐ Rozwiązaniem NIE jest skopiowanie listy do drugiego miejsca — dwie tablice, które dziś się zgadzają,
/// jutro się rozjadą, i to jest dokładnie ten defekt jeszcze raz. Jest nim JEDNA lista, z której każde drzewo
/// <b>filtruje swoje kategorie</b>: kolejność jest wtedy wspólna z konstrukcji, a nie przez staranność.
/// </para>
/// <para>
/// ⚠ Kolejnością odniesienia jest <b>drzewo połączenia</b> i to nie jest arbitralne: to powierzchnia oglądana
/// przez cały dzień pracy (§0.1 — Persistent UI), już odebrana przez użytkownika. Dzięki temu migracja
/// <b>nie rusza w nim ani jednej pozycji</b> — zmienia się wyłącznie drzewo zależności.
/// </para>
/// <para>
/// ⚠⚠ Kategorie występujące tylko w jednym drzewie zostają na swoim miejscu: <c>Role</c>, <c>User</c>
/// i <c>SystemTable</c> są wyłącznie w drzewie połączenia i nie mogą być zależnością — drzewo zależności
/// filtruje je po prostu ze wspólnej listy. ⛔ Kategoria „UDF" została USUNIĘTA (decyzja użytkownika,
/// 2026-08-08): historyczna, zawsze pusta, a produkt wspiera Firebird 5.
/// </para>
/// </remarks>
public static class MetadataCategoryOrder
{
    /// <summary>
    /// Kanoniczna kolejność wszystkich kategorii. ⛔ Zmiana kolejności tutaj zmienia OBA drzewa naraz —
    /// i o to chodzi. Jeżeli jedno z nich ma pokazywać co innego, to jest decyzja produktowa, nie druga tablica.
    /// </summary>
    public static readonly IReadOnlyList<MetadataObjectKind> All = new[]
    {
        MetadataObjectKind.Table,
        MetadataObjectKind.View,
        MetadataObjectKind.Procedure,
        MetadataObjectKind.Trigger,
        MetadataObjectKind.Function,
        MetadataObjectKind.Generator,
        MetadataObjectKind.Domain,
        MetadataObjectKind.Package,
        MetadataObjectKind.Exception,
        MetadataObjectKind.Role,
        MetadataObjectKind.User,
        MetadataObjectKind.Index,
        MetadataObjectKind.SystemTable,
    };

    /// <summary>
    /// Kanoniczna kolejność zawężona do podanych kategorii — dla drzewa, które nie pokazuje wszystkich.
    /// ⭐ Filtrowanie zamiast drugiej listy: drzewo deklaruje CO pokazuje, nigdy W JAKIEJ KOLEJNOŚCI.
    /// </summary>
    public static IEnumerable<MetadataObjectKind> Only(params MetadataObjectKind[] kinds)
    {
        var wanted = kinds.ToHashSet();
        return All.Where(wanted.Contains);
    }
}
