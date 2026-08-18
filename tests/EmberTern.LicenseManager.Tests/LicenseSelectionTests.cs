using System;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Ticking many licences in the licences view — the selection a batch operates on.
///
/// <para>⭐ The rule under test is stated positively: <b>a tick belongs to the LICENCE, not to the row on
/// screen.</b> Everything below follows from that, and the case that matters most is the one an operator
/// hits by accident — typing one more character into the search box after ticking twenty.</para>
/// </summary>
public sealed class LicenseSelectionTests
{
    [Fact]
    public void TicksSurviveAFilterThatHidesTheRow()
    {
        // ⚠⚠ Refresh() runs on EVERY keystroke in the search box. If the tick were nothing but the
        //    control's selection, one more character after ticking twenty licences would throw all
        //    twenty away without a word.
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 2), ("Globex", 1));

        browser.CheckAllShownCommand.Execute(null);
        Assert.Equal(3, browser.CheckedCount);

        browser.SearchText = "Globex";

        Assert.Single(browser.Results);
        Assert.Equal(3, browser.CheckedCount);
        Assert.Equal(2, browser.CheckedNotShown);
    }

    [Fact]
    public void TheSummarySaysHowManyTickedLicencesTheFiltersAreHiding()
    {
        // ⭐ The consequence of the rule above is STATED rather than hidden. A selection wider than the
        //   list is only safe if the operator can read that it is.
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 2), ("Globex", 1));

        browser.CheckAllShownCommand.Execute(null);
        Assert.Equal("3 licences selected.", browser.CheckSummary);

        browser.SearchText = "Globex";
        Assert.Equal("3 licences selected — 2 of them not shown by the current filters.", browser.CheckSummary);

        browser.ClearChecksCommand.Execute(null);
        Assert.Equal("No licence selected.", browser.CheckSummary);
    }

    [Fact]
    public void TickedRowsComeBackTickedWhenTheFilterWidensAgain()
    {
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 2), ("Globex", 1));

        browser.CheckAllShownCommand.Execute(null);
        browser.SearchText = "Globex";
        browser.SearchText = string.Empty;

        Assert.Equal(3, browser.Results.Count);
        Assert.All(browser.Results, row => Assert.True(row.IsChecked));
        Assert.Equal(0, browser.CheckedNotShown);
    }

    [Fact]
    public void SelectAllShownTicksWhatIsShownAndLeavesTheRestAlone()
    {
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 2), ("Globex", 1));

        browser.SearchText = "Globex";
        browser.CheckAllShownCommand.Execute(null);

        Assert.Equal(1, browser.CheckedCount);
        Assert.Equal(0, browser.CheckedNotShown);
    }

    [Fact]
    public void ClearingTheSelectionDropsTheHiddenTicksToo()
    {
        // ⚠ Otherwise "Clear selection" would leave an invisible remainder, and the next batch would
        //   silently cover licences the operator believed they had let go.
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 2), ("Globex", 1));

        browser.CheckAllShownCommand.Execute(null);
        browser.SearchText = "Globex";
        browser.ClearChecksCommand.Execute(null);

        Assert.Equal(0, browser.CheckedCount);
        Assert.False(browser.HasChecked);
    }

    [Fact]
    public void UntickingAVISIBLERowRemovesItAndOnlyIt()
    {
        // ⭐ A row the filters hide has no checkbox to click, so unticking the one on screen must leave
        //   the other two exactly as they were.
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 2), ("Globex", 1));

        browser.CheckAllShownCommand.Execute(null);
        browser.SearchText = "Globex";

        // The same mutation a click performs: the checkbox writes back through the two-way binding.
        Assert.Single(browser.Results);
        browser.Results[0].IsChecked = false;

        Assert.Equal(2, browser.CheckedCount);
        Assert.Equal(2, browser.CheckedNotShown);
    }

    [Fact]
    public void TickingAROWDoesNotDependOnTheListsOwnSelection()
    {
        // ⭐⭐ THE POINT OF THE CHECKBOX COLUMN. Row selection answers "which licence am I looking at?"
        //    and changes on every click; a tick answers "which licences am I about to change?". If the
        //    two were one mechanism, a stray click on row three would throw away the other nineteen.
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 3));

        browser.CheckAllShownCommand.Execute(null);
        Assert.Equal(3, browser.CheckedCount);

        // An ordinary click somewhere in the list.
        browser.SelectedLicense = browser.Results[1];

        Assert.Equal(3, browser.CheckedCount);
        Assert.All(browser.Results, row => Assert.True(row.IsChecked));
    }

    [Fact]
    public void TheTickedSetIsExposedAsIdentitiesRatherThanAsStoredRows()
    {
        // ⭐⭐ The rows here are a SNAPSHOT taken at tick time. A consumer planning an operation from them
        //    would plan against whatever the register held then — and, worse, could never notice that
        //    anything had changed, because both of its readings would come from the same cache.
        using var manager = new ManagerFixture();
        var browser = Browser(manager, ("ACME", 1));

        browser.CheckAllShownCommand.Execute(null);

        var id = Assert.Single(browser.CheckedIds);
        Assert.Equal(browser.Results[0].Summary.License.LicenseId, id);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static LicenseBrowserViewModel Browser(
        ManagerFixture manager, params (string Customer, int Licences)[] seed)
    {
        foreach (var (name, licences) in seed)
        {
            var customer = manager.SaveCustomer(name);
            for (var i = 0; i < licences; i++)
            {
                manager.SaveLicense(customer);
            }
        }

        return new LicenseBrowserViewModel(manager.Register, () => manager.Now);
    }
}
