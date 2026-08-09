using System;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Chip transakcji w pasku statusu (product-polish.md §8.4.5, §19.5) — CZĘŚĆ, KTÓRA DA SIĘ SPRAWDZIĆ
/// BEZ ZEGARA.
///
/// <para>⭐ Chip składa się z dwóch rzeczy o zupełnie różnej testowalności: z <b>faktu</b> (kiedy
/// transakcja została otwarta) i z <b>odświeżania wyświetlanego tekstu</b>, które napędza
/// <c>DispatcherTimer</c>. Wyzwalacz oparty wyłącznie na timerze jest nieosiągalny dla testu headless
/// (gotcha #251), dlatego formatowanie zostało wydzielone jako funkcja <b>czysta</b> — bierze
/// <see cref="TimeSpan"/>, a nie zegar. Timer tylko woła <c>OnPropertyChanged</c>; cała treść jest tutaj.</para>
///
/// <para>⚠ Test pilnuje ZGRUBNOŚCI, bo to ona jest decyzją projektową: pasek statusu czyta się kątem
/// oka i „02:37.4" wymagałoby czytania. Dokładny czas wykonania niesie <c>ExecutionTimer</c> w toolbarze
/// edytora — inne pytanie, inny właściciel, inna precyzja.</para>
/// </summary>
public sealed class TransactionChipTests
{
    [Theory]
    // Poniżej minuty — sekundy, obcinane w dół (nie zaokrąglane: „59 s" nie może zrobić się „1 min").
    [InlineData(0, "0 s")]
    [InlineData(1, "1 s")]
    [InlineData(59, "59 s")]
    // Od minuty — same minuty.
    [InlineData(60, "1 min")]
    [InlineData(119, "1 min")]
    [InlineData(3599, "59 min")]
    // Od godziny — godziny i minuty.
    [InlineData(3600, "1 h 0 min")]
    [InlineData(3660, "1 h 1 min")]
    [InlineData(7380, "2 h 3 min")]
    public void FormatTransactionDuration_IsCoarseAndReadable(int seconds, string expected)
        => Assert.Equal(expected, MainWindowViewModel.FormatTransactionDuration(TimeSpan.FromSeconds(seconds)));

    /// <summary>
    /// ⚠ Ujemny czas jest osiągalny, a nie teoretyczny: znacznik bierzemy z zegara klienta, więc
    /// zmiana czasu systemowego albo korekta NTP w trakcie otwartej transakcji potrafi cofnąć „teraz"
    /// za moment startu. Chip ma wtedy pokazać zero, a nie „-3 s" ani wyjątek.
    /// </summary>
    [Fact]
    public void FormatTransactionDuration_ClampsNegativeToZero()
        => Assert.Equal("0 s", MainWindowViewModel.FormatTransactionDuration(TimeSpan.FromSeconds(-5)));
}
