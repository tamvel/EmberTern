using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Sekcja 4 paska statusu — „co się teraz liczy" (product-polish.md §8.4.6, §19.7).
///
/// <para>⭐⭐ To jest INFRASTRUKTURA, i taki jest ratyfikowany podział D4: <b>M3.1f</b> dostarcza tę
/// sekcję plus <b>jedną</b> operację referencyjną (wykonanie zapytania SQL), a <b>M3b</b> podłącza
/// pozostałe źródła postępu. Dlatego model obsługuje <b>oba</b> tryby, choć operacja referencyjna
/// potrafi dziś wypełnić tylko nieokreślony — mierzone: jej <c>IProgress&lt;long&gt;</c> to licznik
/// wierszy, a strumieniowy odczyt nie zna sumy, dopóki nie skończy.</para>
///
/// <para>⚠ Tryb procentowy nie jest budowany „na zapas" wbrew stojącej dyrektywie — jego konsumenci
/// <b>już istnieją</b> i mają własne paski procentowe: <c>BatchResultsDialog</c>
/// (<c>PreparationTotal</c>) i <c>DataImportTabView</c> (<c>ProgressPercent</c>). M3b je tu podepnie.
/// ⛔ Do tego czasu ścieżka procentowa nie ma konsumenta na żywo — nie zakładać, że jest sprawdzona.</para>
///
/// <para>⭐ <b>Anulowanie: ten model NIE ma własnej komendy.</b> Przyjmuje <see cref="ICommand"/>
/// właściciela operacji, więc pasek statusu i toolbar naciskają <b>ten sam obiekt komendy</b> — jego
/// <c>CanExecute</c>, jego zatrzask „anulowanie w toku", jego implementacja. To realizacja słów
/// użytkownika: <i>„dwa zasięgi tej samej komendy, a nie dwie różne implementacje"</i>. ⛔ Nie dodawać
/// tu drugiej komendy Cancel — powstałby drugi właściciel stanu anulowania.</para>
///
/// <para>⚠ Model niesie <b>jedną</b> operację naraz. Dziś nie ma innej możliwości (SQL Editor blokuje
/// równoległe wykonanie przez <c>IsExecuting</c>), a rozstrzygnięcie „co pokazać, gdy biegną dwie"
/// jest decyzją projektową dla M3b, na komplecie źródeł — nie zgadywaniem tutaj.</para>
/// </summary>
public sealed partial class StatusProgressViewModel : ViewModelBase
{
    /// <summary>Czy sekcja jest w ogóle widoczna. Brak operacji to stan domyślny — nie komunikat.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Etykieta po lewej stronie paska (<c>Text.Caption</c>) — np. „Loading… 12 345 rows".</summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>
    /// ⚠ Domyślnie <c>true</c>, i to jest wybór bezpieczny: operacja, która nie zna swojej sumy,
    /// pokazuje animację zamiast paska stojącego na zerze i udającego „0% zrobione".
    /// </summary>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>Postęp 0–100 dla trybu określonego. Bez znaczenia, gdy <see cref="IsIndeterminate"/>.</summary>
    [ObservableProperty]
    private double _percent;

    /// <summary>
    /// Komenda anulowania NALEŻĄCA DO WŁAŚCICIELA operacji, albo <c>null</c>, gdy operacji nie da się
    /// przerwać. Widok wiąże ją wprost, więc stan przycisku wynika z jej własnego <c>CanExecute</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCancel))]
    private ICommand? _cancelCommand;

    /// <summary>Czy w ogóle pokazać przycisk anulowania (§8.4.6 — „tylko gdy operacja jest anulowalna").</summary>
    public bool HasCancel => CancelCommand is not null;

    /// <summary>
    /// Rozpoczyna operację w trybie <b>nieokreślonym</b>. <paramref name="cancelCommand"/> jest
    /// komendą właściciela; <c>null</c> oznacza operację, której nie da się przerwać.
    /// </summary>
    public void Begin(string label, ICommand? cancelCommand = null)
    {
        Label = label;
        IsIndeterminate = true;
        Percent = 0;
        CancelCommand = cancelCommand;
        IsRunning = true;
    }

    /// <summary>Aktualizuje etykietę, zostawiając tryb nieokreślony.</summary>
    public void Report(string label) => Label = label;

    /// <summary>
    /// Aktualizuje etykietę i przełącza na tryb <b>określony</b>. ⚠ Wartość jest przycinana do 0–100:
    /// producent postępu, który policzy 101% albo −1, ma pokazać pełny/pusty pasek, a nie zepsuć układ.
    /// </summary>
    public void Report(string label, double percent)
    {
        Label = label;
        Percent = percent < 0 ? 0 : percent > 100 ? 100 : percent;
        IsIndeterminate = false;
    }

    /// <summary>
    /// Kończy operację i czyści sekcję. ⚠ Zwalnia też referencję do komendy właściciela — inaczej
    /// pasek trzymałby przy życiu VM zakładki zamkniętej w trakcie operacji.
    /// </summary>
    public void End()
    {
        IsRunning = false;
        Label = string.Empty;
        Percent = 0;
        IsIndeterminate = true;
        CancelCommand = null;
    }
}
