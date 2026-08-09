using System.Collections.ObjectModel;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Nawigacja ←/→ po spłaszczonym drzewie — reguła ratyfikowana przez użytkownika przy odbiorze M4.2b.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>Testy są CZYSTE — bez sesji headless — i to nie jest szczegół.</b> Reguła żyje
/// w <see cref="SidebarFlatController"/>, czyli w zwykłej klasie bez Avalonii, więc daje się sprawdzić
/// bezpośrednio. Gdyby siedziała w code-behind widoku, jedynym sposobem byłby test headless: droższy,
/// wolniejszy i powiększający kruchą listę klas z gotchy #94/#226/#286. ⭐ To argument ZA trzymaniem
/// decyzji w kontrolerze, a nie w widoku — niezależny od tego, że dzięki temu oba drzewa dzielą jedną
/// implementację.
/// </para>
/// <para>
/// ⚠ Testowany jest <see cref="SidebarFlatController.Navigate"/>, bo to on DECYDUJE. Widok wnosi tylko
/// zaznaczenie i przewinięcie — a testowanie „czy `ListBox` zmienił `SelectedItem`" odpowiadałoby na
/// pytanie o Avalonię, nie o naszą regułę.
/// </para>
/// </remarks>
public class SidebarKeyboardNavigationTests
{
    private static (SidebarFlatController Controller, ObservableCollection<DependencyGroupNode> Roots) Build()
    {
        var roots = new ObservableCollection<DependencyGroupNode>
        {
            new()
            {
                ObjectType = "Table",
                Children =
                [
                    new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "ORDERS", ObjectType = "Table" } },
                    new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "CUSTOMERS", ObjectType = "Table" } },
                ],
            },
            // ⚠⚠ Pusta kategoria stoi w ŚRODKU, nie na końcu, i to jest wymóg testu
            // `Right_OnExpandedButChildlessNode_DoesNotStealTheNextSibling`. Pierwsza wersja fikstury
            // miała ją ostatnią — wtedy „nie ma dokąd pójść" wynikało z KOŃCA LISTY, a nie z reguły,
            // więc test przechodził również z zepsutą implementacją (podsadzenie `>` → `>=` go nie
            // zapaliło). Wykryło to dopiero podsadzenie naruszenia: #315 na własnym strażniku.
            new() { ObjectType = "View" },
            new() { ObjectType = "Domain" },
        };

        var controller = new SidebarFlatController(
            roots,
            childrenSelector: n => n is DependencyGroupNode g ? g.Children.Cast<object>() : null,
            isContainer: n => n is DependencyGroupNode,
            hasChildren: n => n is DependencyGroupNode { Count: > 0 },
            isExpanded: n => n is DependencyGroupNode { IsExpanded: true },
            setExpanded: (n, v) => { if (n is DependencyGroupNode g) g.IsExpanded = v; });

        return (controller, roots);
    }

    [Fact]
    public void Right_OnCollapsedNode_Expands_AndDoesNotMoveSelection()
    {
        var (controller, roots) = Build();
        var row = controller.Rows[0];

        var target = controller.Navigate(row, forward: true);

        Assert.Null(target);
        Assert.True(roots[0].IsExpanded);
    }

    [Fact]
    public void Right_OnExpandedNode_MovesToFirstChild()
    {
        var (controller, roots) = Build();
        roots[0].IsExpanded = true;

        var target = controller.Navigate(controller.Rows[0], forward: true);

        Assert.NotNull(target);
        Assert.Same(roots[0].Children[0], target!.Node);
    }

    [Fact]
    public void Left_OnExpandedNode_Collapses_AndDoesNotMoveSelection()
    {
        var (controller, roots) = Build();
        roots[0].IsExpanded = true;

        var target = controller.Navigate(controller.Rows[0], forward: false);

        Assert.Null(target);
        Assert.False(roots[0].IsExpanded);
    }

    [Fact]
    public void Left_OnLeaf_MovesToParent()
    {
        var (controller, roots) = Build();
        roots[0].IsExpanded = true;

        var leafRow = controller.Rows.First(r => r.Node is DependencyLeafNode);
        var target = controller.Navigate(leafRow, forward: false);

        Assert.NotNull(target);
        Assert.Same(roots[0], target!.Node);
    }

    /// <summary>
    /// ⚠ Zwinięty korzeń nie ma rodzica — <c>←</c> nie może wtedy nigdzie skoczyć i to jest poprawne.
    /// Bez tego przypadku strażnik przepuściłby implementację, która „na wszelki wypadek" skacze na
    /// pierwszy wiersz listy.
    /// </summary>
    [Fact]
    public void Left_OnCollapsedRoot_StaysPut()
    {
        var (controller, _) = Build();
        Assert.Null(controller.Navigate(controller.Rows[0], forward: false));
    }

    /// <summary>
    /// ⚠⚠ Kategoria PUSTA bywa „rozwinięta" — drzewo zależności wypisuje każdą kategorię, również bez
    /// zależności. <c>→</c> nie ma wtedy dokąd pójść, a implementacja licząca pierwsze dziecko z płaskiej
    /// projekcji mogłaby złapać SĄSIADA (następny wiersz o tej samej głębokości). Ten test pilnuje
    /// właśnie tego mylenia „następny wiersz" z „pierwsze dziecko".
    /// </summary>
    [Fact]
    public void Right_OnExpandedButChildlessNode_DoesNotStealTheNextSibling()
    {
        var (controller, roots) = Build();
        roots[1].IsExpanded = true;

        var emptyRow = controller.Rows.First(r => ReferenceEquals(r.Node, roots[1]));
        Assert.Null(controller.Navigate(emptyRow, forward: true));
    }

    /// <summary>
    /// ⭐ Reguła musi być identyczna w obu drzewach, więc jest JEDNA. Ten test pilnuje, że wiersz spoza
    /// projekcji nie wywraca nawigacji — kontroler jest współdzielony, a drzewo zależności wymienia go
    /// przy każdym przeładowaniu zależności, więc osierocony wiersz jest realną możliwością.
    /// </summary>
    [Fact]
    public void Navigate_OnARowOutsideTheProjection_IsIgnored()
    {
        var (controller, _) = Build();
        var orphan = new SidebarRow(new DependencyGroupNode { ObjectType = "Ghost" }, 0, true, false);

        Assert.Null(controller.Navigate(orphan, forward: true));
        Assert.Null(controller.Navigate(orphan, forward: false));
        Assert.Null(controller.Navigate(null, forward: true));
    }
}
