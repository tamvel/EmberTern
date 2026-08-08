namespace EmberTern.App.ViewModels;

/// <summary>
/// Otwiera obiekt wskazany w drzewie „Zależności".
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Powstał w M4.2b jako SZEW, nie jako warstwa.</b> Dziewięć edytorów obiektów niosło dziewięć
/// bajtowo identycznych kopii <c>OnDependencyNodeDoubleTapped</c> w code-behind, a każda z nich wołała
/// <c>RequestOpen(leaf)</c> na swoim własnym typie ViewModelu — <b>siedem niezależnych deklaracji tej samej
/// metody, bez wspólnej bazy ani interfejsu</b>. Dopóki wywołanie szło przez code-behind konkretnego widoku,
/// kompilator wiązał je statycznie i brak abstrakcji nic nie kosztował; wspólna kontrolka nie ma tej
/// możliwości, więc potrzebuje JEDNEGO sposobu zadania tego pytania.
/// </para>
/// <para>
/// ⚠ Interfejs jest celowo <b>jednometodowy i bez własnej logiki</b>. Nie jest to „warstwa nawigacji" ani
/// miejsce na przyszłe operacje na zależnościach — reguła #2 architektury (żadnych interfejsów bez dwóch
/// implementacji) jest spełniona z nawiązką (siedem), ale rozszerzanie go o cokolwiek, czego nie woła
/// wspólna kontrolka, złamałoby regułę „nie dodajemy niczego, bo może się przydać".
/// </para>
/// <para>
/// ⭐ Implementacje NIE zmieniły ani jednej linii ciała: wszystkie siedem miało już dokładnie tę sygnaturę,
/// więc migracja to dopisanie nazwy interfejsu. To jest dowód, że abstrakcja opisuje istniejący wzorzec,
/// a nie narzuca nowy.
/// </para>
/// </remarks>
public interface IDependencyNavigator
{
    /// <summary>Otwiera obiekt reprezentowany przez liść drzewa zależności.</summary>
    void RequestOpen(DependencyLeafNode leaf);
}
