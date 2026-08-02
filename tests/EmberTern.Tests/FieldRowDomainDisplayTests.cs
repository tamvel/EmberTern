using System.Collections.ObjectModel;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Wiersz pola oparty na DOMENIE pokazuje jej typ bazowy w kolumnie Type — w procedurach, funkcjach
/// i triggerach naraz, bo wszystkie trzy dzielą <see cref="ProcedureFieldRowBase"/>
/// (product-polish.md §19.8).
///
/// <para>⚠ Zgłoszone jako defekt lokalny dla Variables procedury. Zmierzone: <b>nie jest lokalny</b> —
/// dotyczy każdego wiersza opartego na domenie, więc także parametrów wejściowych/wyjściowych,
/// argumentów funkcji i zmiennych triggera. W zgłoszonej procedurze parametry po prostu nie były
/// oparte na domenie.</para>
///
/// <para>⭐⭐ Przyczyna to KOLEJNOŚĆ, nie brak funkcji. Mechanizm istniał w dwóch miejscach
/// (<c>OnDomainNameChanged</c> i subskrypcja <c>AvailableDomains.CollectionChanged</c>), ale
/// <c>LoadType</c> ustawia <c>DomainName</c> pod <c>_suppressCompose</c>, więc pierwsze wyjście
/// następowało przed synchronizacją — a drugie ratowało sytuację TYLKO wtedy, gdy lista domen
/// dojeżdżała PO zbudowaniu wierszy. Przy połączeniu, w którym domeny były już wczytane, nic nie
/// odpalało synchronizacji i kolumna Type zostawała pusta na zawsze.</para>
/// </summary>
public sealed class FieldRowDomainDisplayTests
{
    private sealed class Owner : IFieldRowOwner
    {
        public ObservableCollection<DomainSpec> AvailableDomains { get; } = [];
        public IReadOnlyList<string> BasicTypes { get; } = ["INTEGER", "VARCHAR", "NUMERIC"];
        public ObservableCollection<string> AvailableTables { get; } = [];
        public IColumnsLoader? ColumnsLoader => null;
    }

    private static Owner OwnerWithDomainsAlreadyLoaded()
    {
        var o = new Owner();
        o.AvailableDomains.Add(new DomainSpec("T_ID", "INTEGER"));
        o.AvailableDomains.Add(new DomainSpec("T_KODPOCZ", "VARCHAR(6)"));
        return o;
    }

    /// <summary>
    /// Przypadek ze zgłoszenia: domeny są JUŻ wczytane, gdy powstaje wiersz. To ta kolejność, w której
    /// subskrypcja <c>CollectionChanged</c> nigdy nie zadziała, bo kolekcja się już nie zmieni.
    /// </summary>
    [Fact]
    public void Variable_OnADomain_ShowsTheDomainsBaseType_WhenDomainsWereLoadedFirst()
    {
        var owner = OwnerWithDomainsAlreadyLoaded();

        var row = ProcedureVariableRowViewModel.From(
            new ProcedureVariable { Name = "ID_NAGLCO", TypeText = "T_ID" }, owner);

        Assert.Equal("T_ID", row.DomainName);   // domena zostaje w swojej kolumnie
        Assert.Equal("INTEGER", row.BaseType);  // …a Type pokazuje jej typ bazowy
    }

    /// <summary>
    /// ⚠⚠ REGUŁA #11. Synchronizacja jest WYŁĄCZNIE informacyjna: kanonicznym typem pozostaje nazwa
    /// domeny, więc <c>TypeText</c> — to, z czego powstaje DDL — nie może się zmienić na typ bazowy.
    /// Gdyby się zmienił, kompilacja podmieniłaby domenę na jej rozwinięcie i cicho zerwała powiązanie
    /// zmiennej z domeną.
    /// </summary>
    [Fact]
    public void Variable_OnADomain_KeepsTheDomainAsTheCanonicalType()
    {
        var owner = OwnerWithDomainsAlreadyLoaded();

        var row = ProcedureVariableRowViewModel.From(
            new ProcedureVariable { Name = "ID_NAGLCO", TypeText = "T_ID" }, owner);

        Assert.Equal("T_ID", row.TypeText);
        Assert.Equal("T_ID", row.ToVariable().TypeText);
    }

    /// <summary>Rozmiar i skala też pochodzą z domeny — kolumny Size/Scale przestają być puste.</summary>
    [Fact]
    public void Variable_OnASizedDomain_ShowsItsSize()
    {
        var owner = OwnerWithDomainsAlreadyLoaded();

        var row = ProcedureVariableRowViewModel.From(
            new ProcedureVariable { Name = "KOD", TypeText = "T_KODPOCZ" }, owner);

        Assert.Equal("VARCHAR", row.BaseType);
        Assert.Equal(6, row.Size);
        Assert.Equal("T_KODPOCZ", row.TypeText);
    }

    /// <summary>
    /// Ta sama klasa bazowa obsługuje parametry, więc naprawa u źródła obejmuje je automatycznie.
    /// Test istnieje, żeby zapisać ZASIĘG: gdyby ktoś naprawił to tylko dla Variables, ten upadnie.
    /// </summary>
    [Fact]
    public void Parameter_OnADomain_GetsTheSameTreatment()
    {
        var owner = OwnerWithDomainsAlreadyLoaded();

        var row = ProcedureParamRowViewModel.From(
            new ProcedureParameter { Name = "P", TypeText = "T_ID" }, owner);

        Assert.Equal("INTEGER", row.BaseType);
        Assert.Equal("T_ID", row.ToParameter().TypeText);
    }

    /// <summary>Zwykły typ (bez domeny) działa jak dotąd — kolumna Domain zostaje pusta.</summary>
    [Fact]
    public void PlainType_IsUnaffected()
    {
        var owner = OwnerWithDomainsAlreadyLoaded();

        var row = ProcedureVariableRowViewModel.From(
            new ProcedureVariable { Name = "N", TypeText = "NUMERIC(18,4)" }, owner);

        Assert.Null(row.DomainName);
        Assert.Equal("NUMERIC", row.BaseType);
        Assert.Equal(18, row.Size);
        Assert.Equal(4, row.Scale);
    }

    /// <summary>
    /// Nieznana nazwa (domena spoza listy albo literówka) nie może zniknąć ani zostać podmieniona —
    /// wiersz zachowuje ją jako typ kanoniczny, a Type zostaje pusty, bo nie ma czego rozwiązać.
    /// </summary>
    [Fact]
    public void UnknownDomain_IsPreservedVerbatim()
    {
        var owner = OwnerWithDomainsAlreadyLoaded();

        var row = ProcedureVariableRowViewModel.From(
            new ProcedureVariable { Name = "X", TypeText = "T_NIEZNANA" }, owner);

        Assert.Equal("T_NIEZNANA", row.DomainName);
        Assert.Null(row.BaseType);
        Assert.Equal("T_NIEZNANA", row.ToVariable().TypeText);
    }
}
