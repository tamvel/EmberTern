using System.Globalization;
using System.Linq;
using System.Resources;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The surfaces whose words are BUILT rather than bound, and what makes a language change reach
/// them.</b>
///
/// <para>Most of this application's text is a binding over a catalog property, and L8.1 measured that such
/// a binding re-reads. Two lists are different in kind: <c>LicenseListItem</c> and
/// <c>ArtifactListItem</c> hold <c>required … { get; init; }</c> strings that a grid binds DIRECTLY, so
/// nothing about switching languages can reach them — the row has to be built again. ⚠ That makes the
/// rebuild part of the localization contract rather than an implementation detail, and it carries a
/// promise of its own: rebuilding must not cost the operator their place.</para>
///
/// <para>⚠⚠ It cannot be measured with one shipped language, so the catalog is swapped for a two-culture
/// one defined here (the instrument L8.1 built for exactly this). ⛔ No pseudo-language ships.</para>
///
/// <para>⚠⚠ <b>The <c>using</c> ORDER in each test is load-bearing, and getting it wrong is how these were
/// first written.</b> A view model stays subscribed to <c>Loc.LanguageChanged</c> for as long as it is
/// reachable, so restoring the catalog in the <c>finally</c> makes it query its register once more — and if
/// the fixture has already been disposed by then, that query hits a closed connection and the exception
/// comes out of the cleanup rather than out of the assertion. ⭐ The fixture is therefore declared FIRST,
/// so it disposes LAST. ⛔ Do not "fix" this by making a refresh tolerate a closed register: in the
/// application the register outlives every view model that reads it, and swallowing the error would hide a
/// real fault the day that stops being true.</para>
/// </summary>
public sealed class LanguageChangeRebuildTests
{
    // ⚠ A real pseudo-locale Windows recognises, so CultureInfo accepts it without a custom culture.
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    /// <summary>A catalog whose answer depends on the culture AND on the key.</summary>
    private sealed class PerKeyCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) =>
            Equals(culture, Pseudo) ? "[[" + name + "]]" : name;
    }

    /// <summary>
    /// ⭐⭐ <b>§53.6 obligation 1.</b> After a language change rebuilds the issuing history, the operator is
    /// still looking at the SAME artifact.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>This is the behavioural half of §53.4's exemption.</b> <c>ArtifactListItem</c> is a record
    /// whose identity DOES contain words (<c>Reason</c>, <c>Ordinal</c>, <c>Standing</c>), which the option
    /// sweep flags as the #394 shape. It was left alone on the user's decision, and the reason it is safe is
    /// not that the words are harmless: it is that <c>ArtifactHistoryViewModel.Load</c> re-selects by
    /// <c>Artifact.ArtifactId</c> rather than by equality. ⛔ Re-select by reference or by record equality
    /// and this goes red — which is the point.</para>
    ///
    /// <para>⭐ It asserts on the ARTIFACT ID, not on the item, precisely because the item is a different
    /// instance carrying different words afterwards.</para>
    /// </remarks>
    [Fact]
    public void TheSelectedArtifactSurvivesALanguageChange()
    {
        using var manager = new ManagerFixture();
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new PerKeyCatalog(), CultureInfo.InvariantCulture);

            var customer = manager.SaveCustomer();
            var licence = manager.SaveLicense(customer, seats: 5);

            var shell = new ShellViewModel(
                manager.Register, manager.Session, manager.Paths, () => manager.Now);
            shell.SelectedCustomer = shell.Customers.First();

            foreach (var reason in new[] { "initial", "renewal", "reissue-lost" })
            {
                manager.Workflow.Issue(manager.Session, licence, manager.Register.GetCustomers()[0], reason);
                manager.Now = manager.Now.AddDays(1);
            }

            shell.SelectedLicense = shell.Licenses.First();

            var chosen = shell.History.Artifacts[^1];   // the oldest, so it is not the default
            shell.History.SelectedArtifact = chosen;

            var chosenId = chosen.Artifact.ArtifactId;
            var standingBefore = chosen.Standing;

            Loc.UseCatalogForVerification(new PerKeyCatalog(), Pseudo);

            // ⭐ The history really was rebuilt — otherwise this test would pass on a surface that ignored
            //   the language change entirely, which is the failure it is here to catch.
            Assert.NotEqual(standingBefore, shell.History.Artifacts.Single(
                a => a.Artifact.ArtifactId == chosenId).Standing);

            Assert.NotNull(shell.History.SelectedArtifact);
            Assert.Equal(chosenId, shell.History.SelectedArtifact!.Artifact.ArtifactId);
            Assert.NotSame(chosen, shell.History.SelectedArtifact);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐ The licences list rebuilds too, and keeps the selected licence and the batch ticks.
    /// </summary>
    /// <remarks>
    /// ⚠ The ticks matter as much as the selection: <c>Refresh</c> is what a language change now calls, and
    /// it is the same path a keystroke in the search box takes — so a rebuild that dropped ticks would
    /// throw away a batch the operator had assembled, silently.
    /// </remarks>
    [Fact]
    public void TheLicencesListRebuildsAndKeepsItsSelectionAndTicks()
    {
        using var manager = new ManagerFixture();
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new PerKeyCatalog(), CultureInfo.InvariantCulture);

            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer, seats: 5);

            var browser = new LicenseBrowserViewModel(manager.Register, () => manager.Now);

            var row = browser.Results.Single();
            browser.SelectedLicense = row;
            row.IsChecked = true;

            var licenceId = row.Summary.License.LicenseId;
            var standingBefore = row.Standing;

            Loc.UseCatalogForVerification(new PerKeyCatalog(), Pseudo);

            var rebuilt = browser.Results.Single();

            Assert.NotSame(row, rebuilt);
            Assert.NotEqual(standingBefore, rebuilt.Standing);

            Assert.NotNull(browser.SelectedLicense);
            Assert.Equal(licenceId, browser.SelectedLicense!.Summary.License.LicenseId);
            Assert.True(rebuilt.IsChecked);
            Assert.Equal(1, browser.CheckedCount);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐ A status column shows a WORD from the catalog, never the persisted value.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ §53.6 obligation 2's guard. Before L8.4 this column was <c>Capitalise(license.Status)</c>, which
    /// renders <c>Active</c> in every language with a green build — the silent kind of defect. ⛔ The
    /// assertion is that the rendered word FOLLOWED the language, because "it looks right in English" is
    /// exactly what the old code also did.
    /// </remarks>
    [Fact]
    public void TheStatusColumnIsALookupAndNotTheStoredValue()
    {
        using var manager = new ManagerFixture();
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new PerKeyCatalog(), CultureInfo.InvariantCulture);

            var customer = manager.SaveCustomer();
            var licence = manager.SaveLicense(customer, seats: 5);

            var browser = new LicenseBrowserViewModel(manager.Register, () => manager.Now);
            Assert.Equal(LicenceStatusText.KeyPrefix + "Active", browser.Results.Single().Status);

            Loc.UseCatalogForVerification(new PerKeyCatalog(), Pseudo);
            Assert.Equal(
                "[[" + LicenceStatusText.KeyPrefix + "Active]]", browser.Results.Single().Status);

            // ⛔⛔ And the PERSISTED value never moved.
            Assert.Equal(LicenseStatuses.Active, manager.Register.GetLicense(licence.LicenseId)!.Status);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }
}
