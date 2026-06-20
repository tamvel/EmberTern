using System.Linq;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// Pure filter/display helpers of the SearchableComboBox (the UI behaviour — popup,
// commit-on-click/Enter, keyboard — is exercised by the headless probe / manual smoke).
public class SearchableComboBoxTests
{
    private static DomainSpec[] Domains() => new[]
    {
        new DomainSpec("T_ID", "INTEGER"),
        new DomainSpec("T_KOD", "VARCHAR(20)"),
        new DomainSpec("T_NAZWISKO", "VARCHAR(60)"),
        new DomainSpec("T_KODPOCZ", "VARCHAR(6)"),
    };

    [Fact]
    public void FilterItems_BlankText_ReturnsAll()
    {
        var all = SearchableComboBox.FilterItems(Domains(), nameof(DomainSpec.Name), "");
        Assert.Equal(4, all.Count);
        var ws = SearchableComboBox.FilterItems(Domains(), nameof(DomainSpec.Name), "   ");
        Assert.Equal(4, ws.Count);
    }

    [Fact]
    public void FilterItems_Contains_CaseInsensitive_Substring()
    {
        var r = SearchableComboBox.FilterItems(Domains(), nameof(DomainSpec.Name), "kod")
            .Cast<DomainSpec>().Select(d => d.Name).ToList();
        Assert.Equal(new[] { "T_KOD", "T_KODPOCZ" }, r);
    }

    [Fact]
    public void FilterItems_NoMatch_ReturnsEmpty()
        => Assert.Empty(SearchableComboBox.FilterItems(Domains(), nameof(DomainSpec.Name), "zzz"));

    [Fact]
    public void FilterItems_NullSource_ReturnsEmpty()
        => Assert.Empty(SearchableComboBox.FilterItems(null, nameof(DomainSpec.Name), "x"));

    [Fact]
    public void DisplayText_ReadsNamedProperty()
        => Assert.Equal("T_ID", SearchableComboBox.DisplayText(new DomainSpec("T_ID", "INTEGER"), nameof(DomainSpec.Name)));

    [Fact]
    public void DisplayText_NullPath_FallsBackToToString()
        => Assert.Equal("hello", SearchableComboBox.DisplayText("hello", null));

    [Fact]
    public void DisplayText_NullItem_ReturnsEmpty()
        => Assert.Equal(string.Empty, SearchableComboBox.DisplayText(null, nameof(DomainSpec.Name)));

    [Fact]
    public void FilterItems_StringList_FiltersOnToString()
    {
        var src = new[] { "INTEGER", "VARCHAR", "BIGINT" };
        var r = SearchableComboBox.FilterItems(src, null, "int").Cast<string>().ToList();
        Assert.Equal(new[] { "INTEGER", "BIGINT" }, r);
    }

    [Fact]
    public void ColumnRef_FormatsQualifiedAndTypeOfClause()
    {
        var c = new ColumnRef("ADRES", "MIASTO", "VARCHAR(50)");
        Assert.Equal("ADRES.MIASTO", c.Qualified);
        Assert.Equal("COLUMN ADRES.MIASTO", c.TypeOfClause);
    }
}
