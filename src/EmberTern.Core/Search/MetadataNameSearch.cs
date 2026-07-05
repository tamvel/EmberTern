using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Search;

/// <summary>
/// Pure name-search over already-loaded object names (fed from the Explorer's
/// name cache — zero DB round-trips). Produces <see cref="SearchMatchLocation.Name"/>
/// hits with an occurrence count. The DB-only searches (source bodies, table field
/// names, exception messages) live in <c>FirebirdMetadataSearchReader</c>.
/// </summary>
public static class MetadataNameSearch
{
    /// <summary>
    /// Matches <paramref name="names"/> of one <paramref name="kind"/> against the
    /// query. Honors CaseSensitive + WholeWord (client-side, where we control the match).
    /// </summary>
    public static IReadOnlyList<MetadataSearchHit> Match(
        MetadataObjectKind kind, IEnumerable<string> names, MetadataSearchQuery query)
    {
        var hits = new List<MetadataSearchHit>();
        if (string.IsNullOrEmpty(query.Term)) return hits;

        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (!SearchTextMatch.Contains(name, query.Term, query.CaseSensitive, query.WholeWord)) continue;
            int count = SearchTextMatch.CountOccurrences(name, query.Term, query.CaseSensitive, query.WholeWord);
            hits.Add(new MetadataSearchHit(kind, name!, SearchMatchLocation.Name, count == 0 ? 1 : count));
        }
        return hits;
    }

    /// <summary>
    /// Matches names across several (kind, names) groups, skipping kinds the query
    /// doesn't include. Convenience over the Explorer's name cache.
    /// </summary>
    public static IReadOnlyList<MetadataSearchHit> MatchAll(
        IEnumerable<(MetadataObjectKind Kind, IReadOnlyList<string> Names)> groups, MetadataSearchQuery query)
    {
        var hits = new List<MetadataSearchHit>();
        if (!query.MatchNames || string.IsNullOrEmpty(query.Term)) return hits;

        foreach (var (kind, names) in groups)
        {
            if (!query.Includes(kind)) continue;
            hits.AddRange(Match(kind, names, query));
        }
        return hits;
    }
}
