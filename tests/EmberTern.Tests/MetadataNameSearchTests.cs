using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using Xunit;

namespace EmberTern.Tests;

public class MetadataNameSearchTests
{
    private static MetadataSearchQuery Q(string term, bool cs = false, bool ww = false, bool names = true)
        => new(term, MatchNames: names, MatchSource: true, CaseSensitive: cs, WholeWord: ww);

    [Fact]
    public void Match_ReturnsNameHitsWithCount()
    {
        var hits = MetadataNameSearch.Match(
            MetadataObjectKind.Procedure,
            new[] { "NAGL_GET", "POZYCJE", "NAGL_NAGL" },
            Q("nagl"));

        Assert.Equal(2, hits.Count);
        var repeated = hits.Single(h => h.ObjectName == "NAGL_NAGL");
        Assert.Equal(SearchMatchLocation.Name, repeated.Location);
        Assert.Equal(MetadataObjectKind.Procedure, repeated.Kind);
        Assert.Equal(2, repeated.MatchCount); // NAGL appears twice
    }

    [Fact]
    public void Match_CaseSensitive_Respected()
    {
        var insensitive = MetadataNameSearch.Match(MetadataObjectKind.Table, new[] { "NAGL" }, Q("nagl"));
        var sensitive = MetadataNameSearch.Match(MetadataObjectKind.Table, new[] { "NAGL" }, Q("nagl", cs: true));
        Assert.Single(insensitive);
        Assert.Empty(sensitive);
    }

    [Fact]
    public void Match_WholeWord_Respected()
    {
        var names = new[] { "NAGL", "NAGLOWEK" };
        Assert.Equal(2, MetadataNameSearch.Match(MetadataObjectKind.Table, names, Q("nagl")).Count);
        Assert.Single(MetadataNameSearch.Match(MetadataObjectKind.Table, names, Q("nagl", ww: true)));
    }

    [Fact]
    public void Match_TrimsAndSkipsBlankNames()
    {
        var hits = MetadataNameSearch.Match(MetadataObjectKind.Domain, new[] { "  T_NAGL  ", "   ", "" }, Q("nagl"));
        Assert.Single(hits);
        Assert.Equal("T_NAGL", hits[0].ObjectName);
    }

    [Fact]
    public void Match_EmptyTerm_ReturnsNothing()
        => Assert.Empty(MetadataNameSearch.Match(MetadataObjectKind.Table, new[] { "NAGL" }, Q("")));

    [Fact]
    public void MatchAll_SkipsKindsNotInQuery()
    {
        var groups = new (MetadataObjectKind, IReadOnlyList<string>)[]
        {
            (MetadataObjectKind.Procedure, new[] { "NAGL_P" }),
            (MetadataObjectKind.Index, new[] { "NAGL_IX" }), // not a supported kind
        };
        var query = new MetadataSearchQuery("nagl", Kinds: new[] { MetadataObjectKind.Procedure });
        var hits = MetadataNameSearch.MatchAll(groups, query);
        Assert.Single(hits);
        Assert.Equal(MetadataObjectKind.Procedure, hits[0].Kind);
    }

    [Fact]
    public void MatchAll_MatchNamesFalse_ReturnsNothing()
    {
        var groups = new (MetadataObjectKind, IReadOnlyList<string>)[] { (MetadataObjectKind.Table, new[] { "NAGL" }) };
        var query = new MetadataSearchQuery("nagl", MatchNames: false);
        Assert.Empty(MetadataNameSearch.MatchAll(groups, query));
    }

    [Fact]
    public void MatchAll_AcrossKinds_AggregatesHits()
    {
        var groups = new (MetadataObjectKind, IReadOnlyList<string>)[]
        {
            (MetadataObjectKind.Table, new[] { "NAGL", "POZ" }),
            (MetadataObjectKind.Generator, new[] { "GEN_NAGL" }),
            (MetadataObjectKind.Domain, new[] { "T_ID" }),
        };
        var hits = MetadataNameSearch.MatchAll(groups, Q("nagl"));
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Kind == MetadataObjectKind.Table && h.ObjectName == "NAGL");
        Assert.Contains(hits, h => h.Kind == MetadataObjectKind.Generator && h.ObjectName == "GEN_NAGL");
    }

    [Fact]
    public void SupportedKinds_CoverTheAgreedSet_AndExcludeNameOnlyExtras()
    {
        var k = MetadataSearchQuery.SupportedKinds;
        Assert.Contains(MetadataObjectKind.Table, k);
        Assert.Contains(MetadataObjectKind.View, k);
        Assert.Contains(MetadataObjectKind.Procedure, k);
        Assert.Contains(MetadataObjectKind.Trigger, k);
        Assert.Contains(MetadataObjectKind.Function, k);
        Assert.Contains(MetadataObjectKind.Package, k);
        Assert.Contains(MetadataObjectKind.Exception, k);
        Assert.Contains(MetadataObjectKind.Domain, k);
        Assert.Contains(MetadataObjectKind.Generator, k);
        // Not in scope for V1 Global Search.
        Assert.DoesNotContain(MetadataObjectKind.Index, k);
        Assert.DoesNotContain(MetadataObjectKind.Role, k);
        Assert.DoesNotContain(MetadataObjectKind.User, k);
        Assert.DoesNotContain(MetadataObjectKind.SystemTable, k);
    }
}
