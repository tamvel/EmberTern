using CommunityToolkit.Mvvm.Input;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Sekcja postępu paska statusu (product-polish.md §8.4.6, §19.7) — maszyna stanów, czyli ta część,
/// która da się sprawdzić bez Avalonii.
///
/// <para>⭐ <see cref="StatusProgressViewModel"/> jest INFRASTRUKTURĄ dla M3b (ratyfikowany podział D4),
/// więc jej kontrakt zostanie użyty przez producentów postępu, których jeszcze nie ma. Test opisuje ten
/// kontrakt teraz, kiedy jest jeden konsument, a nie za pięć — bo wtedy będzie już utrwalony.</para>
///
/// <para>⚠ Tryb procentowy nie ma dziś konsumenta na żywo: operacja referencyjna (wykonanie zapytania
/// SQL) potrafi tylko tryb nieokreślony, bo jej <c>IProgress&lt;long&gt;</c> to licznik wierszy bez sumy.
/// Konsumenci istnieją i są znani (Batch, Data Import — oba mają dziś własne paski procentowe), ale
/// podłącza ich M3b. Do tego czasu <b>to jest jedyne miejsce, które ścieżkę procentową w ogóle
/// wykonuje.</b></para>
/// </summary>
public sealed class StatusProgressTests
{
    private static RelayCommand Noop() => new(() => { });

    [Fact]
    public void FreshInstance_IsIdleAndShowsNothing()
    {
        var p = new StatusProgressViewModel();

        Assert.False(p.IsRunning);
        Assert.Equal(string.Empty, p.Label);
        Assert.False(p.HasCancel);
        // ⚠ Nieokreślony jest bezpiecznym domyślnym: operacja, która nie zna sumy, ma animować,
        // a nie stać na zerze i sugerować „0% zrobione".
        Assert.True(p.IsIndeterminate);
    }

    [Fact]
    public void Begin_StartsIndeterminate_AndAdoptsTheOwnersCancelCommand()
    {
        var p = new StatusProgressViewModel();
        var cancel = Noop();

        p.Begin("Executing query…", cancel);

        Assert.True(p.IsRunning);
        Assert.Equal("Executing query…", p.Label);
        Assert.True(p.IsIndeterminate);
        Assert.True(p.HasCancel);
        // ⭐ TEN SAM obiekt komendy, nie opakowanie: pasek statusu i toolbar mają naciskać jedną komendę,
        // żeby jej `CanExecute` i zatrzask „anulowanie w toku" gasiły oba przyciski naraz.
        Assert.Same(cancel, p.CancelCommand);
    }

    [Fact]
    public void Begin_WithoutCancel_ShowsNoCancelAffordance()
    {
        var p = new StatusProgressViewModel();

        p.Begin("Compiling…");

        Assert.True(p.IsRunning);
        Assert.False(p.HasCancel);   // §8.4.6 — przycisk tylko wtedy, gdy operacja jest anulowalna
    }

    [Fact]
    public void Report_WithoutPercent_KeepsIndeterminate()
    {
        var p = new StatusProgressViewModel();
        p.Begin("Executing query…", Noop());

        p.Report("Loading… 12 345 rows");

        Assert.Equal("Loading… 12 345 rows", p.Label);
        Assert.True(p.IsIndeterminate);
    }

    [Fact]
    public void Report_WithPercent_SwitchesToDeterminate()
    {
        var p = new StatusProgressViewModel();
        p.Begin("Importing…");

        p.Report("Importing… 40%", 40);

        Assert.False(p.IsIndeterminate);
        Assert.Equal(40, p.Percent);
    }

    /// <summary>
    /// ⚠ Przycinanie nie jest asekuracją teoretyczną: producent postępu liczy procent z dwóch liczb,
    /// z których jedna bywa oszacowana. Pasek ma wtedy pokazać pełny albo pusty, a nie wyjechać poza
    /// swoje 120 px i przesunąć chipy stanu obok.
    /// </summary>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void Report_ClampsPercentIntoRange(double reported, double expected)
    {
        var p = new StatusProgressViewModel();
        p.Begin("…");

        p.Report("…", reported);

        Assert.Equal(expected, p.Percent);
    }

    /// <summary>
    /// ⚠⚠ Zwolnienie komendy jest wymogiem POPRAWNOŚCI, nie higieny — to ten sam kształt, co odpinanie
    /// subskrypcji railu w M3.1c (§19.4.2): pasek statusu żyje tak długo jak okno, więc trzymana komenda
    /// utrzymywałaby przy życiu VM zakładki zamkniętej w trakcie operacji. Dodatkowo osierocony przycisk
    /// Cancel wskazywałby na operację, której już nie ma.
    /// </summary>
    [Fact]
    public void End_ClearsEverything_IncludingTheCancelCommand()
    {
        var p = new StatusProgressViewModel();
        p.Begin("Executing query…", Noop());
        p.Report("Importing… 40%", 40);

        p.End();

        Assert.False(p.IsRunning);
        Assert.Equal(string.Empty, p.Label);
        Assert.Equal(0, p.Percent);
        Assert.True(p.IsIndeterminate);   // wraca do bezpiecznego domyślnego
        Assert.Null(p.CancelCommand);
        Assert.False(p.HasCancel);
    }

    /// <summary>
    /// Druga operacja po pierwszej zaczyna od czystego stanu — w szczególności <b>nie dziedziczy</b>
    /// trybu określonego po poprzedniej. Bez tego zapytanie SQL uruchomione po imporcie pokazywałoby
    /// pasek stojący na wartości z tamtej operacji.
    /// </summary>
    [Fact]
    public void Begin_AfterADeterminateRun_ResetsToIndeterminate()
    {
        var p = new StatusProgressViewModel();
        p.Begin("Importing…");
        p.Report("Importing… 90%", 90);
        p.End();

        p.Begin("Executing query…", Noop());

        Assert.True(p.IsIndeterminate);
        Assert.Equal(0, p.Percent);
    }
}
