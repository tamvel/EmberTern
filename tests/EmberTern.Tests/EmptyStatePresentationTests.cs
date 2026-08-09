using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Security;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Product Polish M5 / M‑3 — stany puste powierzchni danych (klasy A i B).
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ ZAKRES TEGO PLIKU JEST WYNIKIEM POMIARU, NIE PLANU, i to jest w nim najważniejsze. Audyt §1.1 mówił
/// „empty states w 3 z 48 widoków"; zmierzone — 12 widoków i 5 ViewModeli już je miało, a z trzynastu
/// powierzchni, które inwentaryzacja wskazała jako luki, po sprawdzeniu OSIĄGALNOŚCI stanu zostały cztery.
/// Reszta odpadła z zapisanych powodów, a każdy z nich jest pilnowany niżej, żeby nie wrócił jako „luka".
/// </para>
/// <para>
/// ⛔ ŚWIADOMIE POZA M‑3 (nie przeoczenia — każdy zweryfikowany):
/// <list type="bullet">
/// <item><b>B1 Security → Users</b> — stan NIEOSIĄGALNY: <c>SEC$USERS</c> zawsze zawiera SYSDBA, a błąd
/// odczytu jest łapany i pokazywany banerem (<c>SafeLoadAsync</c> → <c>HasError</c>), więc pusta siatka
/// bez błędu nie występuje.</item>
/// <item><b>B8 View → Fields</b> — stan NIEOSIĄGALNY: widok zawsze ma co najmniej jedną kolumnę.</item>
/// <item><b>B4 Security → Privileges</b> — WYCOFANE, bo proponowana treść była rzeczowo nieprawdziwa:
/// ta siatka wypisuje OBIEKTY kategorii z uprawnieniami jako komórkami, więc pusta znaczy „brak obiektów
/// tej kategorii" albo „filtr nic nie dopasował", nigdy „brak uprawnień".</item>
/// <item><b>B5 Session → Transactions</b> i <b>B7 Table → Indeksy</b> — ODŁOŻONE decyzją użytkownika
/// (licznik już stoi w pasku podsumowania · jedyna akcja to menu kontekstowe).</item>
/// <item><b>klasa C</b> (siatki definicji z „+"), <b>klasa E</b> (siatki danych) i <b>klasa F</b>
/// (17 drzew zależności) — poza zakresem; F dodatkowo jest NAZWANYM WYJĄTKIEM, patrz
/// <see cref="DependencyTree_DeliberatelyHasNoEmptyState"/>.</item>
/// </list>
/// </para>
/// </remarks>
public class EmptyStatePresentationTests
{
    // ══ B2 — Security Manager → Roles ══════════════════════════════════════════════════════════════

    [Fact]
    public void Roles_EmptyStateFollowsTheList()
    {
        var vm = BuildSecurity();
        Assert.True(vm.Roles.ShowEmptyState);

        vm.Roles.Items.Add(new RoleInfo { Name = "R", Owner = "SYSDBA", Description = null });
        Assert.False(vm.Roles.ShowEmptyState);
    }

    /// <summary>⚠ Asercją jest POWIADOMIENIE — wartość bywa poprawna, a ekran nieodświeżony.</summary>
    [Fact]
    public void Roles_EmptyStateAnnouncesItsOwnChange()
    {
        var vm = BuildSecurity();
        var announced = 0;
        vm.Roles.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SecurityRolesPaneViewModel.ShowEmptyState)) announced++;
        };

        vm.Roles.Items.Add(new RoleInfo { Name = "R", Owner = "SYSDBA", Description = null });

        Assert.True(announced > 0, "ShowEmptyState zmieniło wartość, ale nie powiadomiło — wiązanie nie odświeży ekranu.");
    }

    /// <summary>
    /// ⛔⛔ Lista ról NIE MA FILTRA (zmierzone: <c>FilterText</c> istnieje wyłącznie w panelu uprawnień),
    /// więc treść mówiąca o filtrze wskazywałaby element, którego na tym ekranie nie ma — ten sam gatunek
    /// defektu co odrzucony wariant W1 stanu pustego paska bocznego.
    /// ⭐ Nazwa akcji jest SKŁADANA z etykiety przycisku, więc nie może się z nią rozjechać.
    /// </summary>
    [Fact]
    public void Roles_EmptyTextNamesTheButtonAndNeverAFilter()
    {
        Assert.Contains(UiStrings.SecurityAddRole, UiStrings.SecurityRolesEmpty, StringComparison.Ordinal);
        Assert.DoesNotContain("filter", UiStrings.SecurityRolesEmpty, StringComparison.OrdinalIgnoreCase);
    }

    // ══ B3 — Security Manager → Membership ═════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ Treść zależy od KIERUNKU, bo to dwa różne pytania — a jeden komunikat na oba byłby nieprawdziwy
    /// w jednym z nich. Że to dwa pytania, produkt wie niezależnie od M‑3: nagłówek kolumny przełącza się
    /// „Role name" ↔ „Member name", więc test wiąże treść z TYM SAMYM przełącznikiem.
    /// </summary>
    [Fact]
    public void Membership_EmptyTextFollowsTheDirection()
    {
        var vm = BuildSecurity();

        vm.Membership.SelectedDirection = vm.Membership.Directions[0]; // Member of
        Assert.Equal(UiStrings.SecurityMembershipEmptyMemberOf, vm.Membership.EmptyText);
        Assert.Equal(UiStrings.SecurityColRoleName, vm.Membership.RowHeader);

        vm.Membership.SelectedDirection = vm.Membership.Directions[1]; // Members
        Assert.Equal(UiStrings.SecurityMembershipEmptyMembers, vm.Membership.EmptyText);
        Assert.Equal(UiStrings.SecurityColMemberName, vm.Membership.RowHeader);
    }

    /// <summary>
    /// ⚠ Bramka wymaga WYBRANEGO elementu i to jest rozgraniczenie stanów, nie ostrożność: picker
    /// autowybiera pierwszą pozycję, więc pusty picker znaczy „w bazie nie ma ról" — fakt, który komunikuje
    /// już zakładka Roles (B2) i który decyzją użytkownika NIE dostaje tu drugiego komunikatu.
    /// </summary>
    [Fact]
    public void Membership_SaysNothingWhenThereIsNothingToPick()
    {
        var vm = BuildSecurity();

        Assert.Null(vm.Membership.SelectedPicker);
        Assert.False(vm.Membership.ShowEmptyState);
    }

    [Fact]
    public void Membership_EmptyStateShowsForAPickedGranteeWithNoRows()
    {
        var vm = BuildSecurity();
        // ⚠ Picker buduje się z grantee'ów WŁAŚCICIELA, nie z listy użytkowników — pierwsza wersja tego testu
        //   dodawała użytkownika do `Users.Items` i przechodziła obok mechanizmu; złapał to dopiero przebieg.
        vm.Grantees.Add(new GranteeOptionViewModel(new GranteeRef("ALICE", GranteeType.User)));
        vm.Membership.SetMembershipForTest(Array.Empty<MembershipInfo>());

        // Picker zbudowany z grantee'ów właściciela → pierwszy wybrany automatycznie, wierszy brak.
        Assert.NotNull(vm.Membership.SelectedPicker);
        Assert.Empty(vm.Membership.Rows);
        Assert.True(vm.Membership.ShowEmptyState);
    }

    // ══ B6 — Script Executor → wyniki ══════════════════════════════════════════════════════════════

    [Fact]
    public void ScriptResults_BeforeAnyRun_ShowTheRunHint()
    {
        using var cs = new FirebirdConnectionService();
        var vm = BuildScript(cs);

        Assert.True(vm.ShowNoResults);
        Assert.False(vm.ShowNoFilterMatch);
    }

    /// <summary>
    /// ⛔ Oba komunikaty leżą NA tej samej siatce, więc jednoczesna widoczność byłaby nakładającym się
    /// tekstem. Wykluczenie wynika z definicji (<c>!HasResults</c> vs <c>HasResults &amp;&amp; …</c>) i test
    /// pilnuje, żeby przetrwało zmianę którejkolwiek z nich.
    /// </summary>
    [Fact]
    public void ScriptResults_TheTwoEmptyStatesAreMutuallyExclusive()
    {
        using var cs = new FirebirdConnectionService();
        var vm = BuildScript(cs);

        Assert.False(vm.ShowNoResults && vm.ShowNoFilterMatch);

        vm.SelectedFilterIndex = 2; // „Failed" — przebudowuje Rows
        Assert.False(vm.ShowNoResults && vm.ShowNoFilterMatch);
    }

    // ══ Strażniki źródłowe — poprawna właściwość to nie to samo, co element na ekranie ═════════════

    [Fact]
    public void EveryEmptyStateIsActuallyBoundInItsView()
    {
        var security = View("SecurityManagerTabView.axaml");
        Assert.Contains("{Binding Roles.ShowEmptyState}", security, StringComparison.Ordinal);
        Assert.Contains("UiStrings.SecurityRolesEmpty", security, StringComparison.Ordinal);
        Assert.Contains("{Binding Membership.ShowEmptyState}", security, StringComparison.Ordinal);
        Assert.Contains("{Binding Membership.EmptyText}", security, StringComparison.Ordinal);

        var script = View("ScriptExecutorTabView.axaml");
        Assert.Contains("{Binding ShowNoResults}", script, StringComparison.Ordinal);
        Assert.Contains("{Binding ShowNoFilterMatch}", script, StringComparison.Ordinal);
        Assert.Contains("UiStrings.ScriptResultsEmpty", script, StringComparison.Ordinal);
        Assert.Contains("UiStrings.ScriptResultsNoFilterMatch", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔⛔ NAZWANY WYJĄTEK, NIE LUKA — i ten test istnieje po to, żeby kolejny audyt nie zgłosił go jako
    /// braku. Wszystkie 17 drzew „Zależności" świadomie NIE MA stanu pustego: <c>BuildDependencyTree</c>
    /// wypisuje KAŻDĄ kategorię również pustą (parytet z IBExpertem), więc obiekt bez zależności pokazuje
    /// dziesięć wierszy „… (0)" — pustka jest ogłoszona, a ekran nie jest pusty.
    /// ⛔ Decyzja użytkownika (2026-08-10): zostaje bez zmian; nie ukrywamy pustych kategorii i nie dodajemy
    /// komunikatu zbiorczego.
    /// </summary>
    [Fact]
    public void DependencyTree_DeliberatelyHasNoEmptyState()
    {
        var tree = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "EmberTern.App", "Controls", "DependencyTreeView.axaml"));

        // ⚠ Sprawdzane jest ODWOŁANIE DO STAŁEJ, nie samo słowo „Empty": pierwsza wersja szukała podciągu
        //   i zapaliła się na `StringConverters.IsNotNullOrEmpty` — czyli raportowała stan pusty tam, gdzie
        //   stoi zwykły konwerter widoczności. Wykrył to przebieg, nie czytanie.
        Assert.DoesNotContain("UiStrings.", tree, StringComparison.Ordinal);

        // Przesłanka wyjątku: pusta kategoria nadal jest wierszem. Gdyby to się zmieniło, wyjątek traci
        // uzasadnienie i decyzję trzeba podjąć od nowa — dlatego pilnowana jest PRZESŁANKA, nie polityka.
        var builder = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "EmberTern.App", "ViewModels", "TableDetailTabViewModel.cs"));
        Assert.Contains("groups.Add(new DependencyGroupNode", builder, StringComparison.Ordinal);
    }

    private static string View(string fileName)
        => File.ReadAllText(Path.Combine(RepoRoot(), "src", "EmberTern.App", "Views", fileName));

    private static SecurityManagerTabViewModel BuildSecurity()
    {
        var svc = new FirebirdConnectionService();
        return new SecurityManagerTabViewModel(
            new FirebirdSecurityReader(svc), new FirebirdMetadataReader(svc), new FirebirdDdlExecutor(svc), null);
    }

    private static ScriptExecutorTabViewModel BuildScript(FirebirdConnectionService cs)
    {
        var ts = new TransactionService(cs);
        return new ScriptExecutorTabViewModel(new FirebirdScriptParser(), new FirebirdScriptExecutor(cs, ts), ts);
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
