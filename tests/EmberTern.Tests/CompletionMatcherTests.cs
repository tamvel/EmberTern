using System.Linq;
using EmberTern.Core.Sql.Language.Completion;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The single completion filtering/ranking authority (Completion milestone) — prefix-first
/// "prediction, not search": empty prefix → all; non-empty → StartsWith only (never Contains) with
/// exact floated to the top; zero StartsWith → empty. Pure Core, pinned directly.
/// </summary>
public class CompletionMatcherTests
{
    private static CompletionItem Item(string name, CompletionItemKind kind = CompletionItemKind.Table, double pri = 3.0)
        => new(name, name, kind, pri);

    private static string[] Names(System.Collections.Generic.IReadOnlyList<CompletionItem> items)
        => items.Select(i => i.InsertText).ToArray();

    [Fact]
    public void EmptyPrefix_ReturnsAll_Unchanged()
    {
        var items = new[] { Item("STATUS"), Item("KONTRAHENT"), Item("ZAMOWIENIA") };
        Assert.Same(items, CompletionMatcher.Filter(items, ""));
        Assert.Same(items, CompletionMatcher.Filter(items, null));
    }

    [Fact]
    public void Prefix_KeepsOnlyStartsWith_NeverContains()
    {
        var items = new[]
        {
            Item("STATUS"), Item("STATUS_ID"), Item("STATUS_NAME"),
            Item("NR_STATUS"), Item("OLD_STATUS"), Item("DATASTATUS"),
        };
        var result = Names(CompletionMatcher.Filter(items, "sta"));
        Assert.Equal(new[] { "STATUS", "STATUS_ID", "STATUS_NAME" }, result);
        Assert.DoesNotContain("NR_STATUS", result);
        Assert.DoesNotContain("OLD_STATUS", result);
        Assert.DoesNotContain("DATASTATUS", result);
    }

    [Fact]
    public void Prefix_IsCaseInsensitive()
    {
        var items = new[] { Item("ZAMOWIENIA"), Item("ZAM_POZ"), Item("KONTRAHENT") };
        var result = Names(CompletionMatcher.Filter(items, "zam"));
        Assert.Equal(new[] { "ZAMOWIENIA", "ZAM_POZ" }, result);
    }

    [Fact]
    public void ExactMatch_FloatsToTop_EvenBelowStartsWithByRank()
    {
        // "ID" exact should beat "ID_KONTRAHENT"/"IDX" even though they share the prefix.
        var items = new[] { Item("ID_KONTRAHENT"), Item("IDX"), Item("ID") };
        var result = Names(CompletionMatcher.Filter(items, "id"));
        Assert.Equal("ID", result[0]);
        Assert.Contains("ID_KONTRAHENT", result);
        Assert.Contains("IDX", result);
    }

    [Fact]
    public void ExactMatch_FloatsAboveHigherRankedStartsWith()
    {
        // A low-priority keyword that EXACTLY matches beats a high-priority table that only starts with.
        var items = new[]
        {
            Item("SELECT_LOG", CompletionItemKind.Table, pri: 3.0),
            Item("SELECT", CompletionItemKind.Keyword, pri: 1.0),
        };
        var result = Names(CompletionMatcher.Filter(items, "select"));
        Assert.Equal(new[] { "SELECT", "SELECT_LOG" }, result);
    }

    [Fact]
    public void ZeroStartsWith_ReturnsEmpty()
    {
        var items = new[] { Item("KONTRAHENT"), Item("ZAMOWIENIA") };
        Assert.Empty(CompletionMatcher.Filter(items, "xyz"));
    }

    [Fact]
    public void WithinTier_IncomingOrderIsPreserved()
    {
        // The engine's rank ordering (passed in) is kept stable within the StartsWith tier.
        var items = new[] { Item("STA_A"), Item("STA_B"), Item("STA_C") };
        Assert.Equal(new[] { "STA_A", "STA_B", "STA_C" }, Names(CompletionMatcher.Filter(items, "sta")));
    }

    [Fact]
    public void EmptyItems_ReturnsEmpty()
        => Assert.Empty(CompletionMatcher.Filter(System.Array.Empty<CompletionItem>(), "x"));
}
