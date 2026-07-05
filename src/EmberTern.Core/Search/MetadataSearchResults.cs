using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Search;

/// <summary>A single result leaf: an object (optionally a nested field) with its
/// total match count and the most-specific location it matched at.</summary>
public sealed record SearchResultLeaf(string ObjectName, string? DetailName, int MatchCount, SearchMatchLocation Location);

/// <summary>Results for one object kind, in display order, with their leaves.</summary>
public sealed record SearchResultGroup(MetadataObjectKind Kind, IReadOnlyList<SearchResultLeaf> Leaves);

/// <summary>
/// Pure grouping of raw <see cref="MetadataSearchHit"/>s into the Search Results tree:
/// merges hits for the same (Kind, ObjectName, DetailName) — an object matched both by
/// name and in its source shows once with the summed count — then groups by kind in the
/// agreed display order, leaves sorted by name (then field).
/// </summary>
public static class MetadataSearchResults
{
    /// <summary>Group order in the Search Results tree.</summary>
    public static readonly IReadOnlyList<MetadataObjectKind> GroupOrder = new[]
    {
        MetadataObjectKind.Procedure,
        MetadataObjectKind.Function,
        MetadataObjectKind.Trigger,
        MetadataObjectKind.View,
        MetadataObjectKind.Package,
        MetadataObjectKind.Table,
        MetadataObjectKind.Domain,
        MetadataObjectKind.Generator,
        MetadataObjectKind.Exception,
    };

    public static IReadOnlyList<SearchResultGroup> Group(IEnumerable<MetadataSearchHit> hits)
    {
        // Merge by (Kind, ObjectName, DetailName): sum counts, keep the most specific
        // location (FieldName/Source/Message beat a plain Name match for display).
        var merged = new Dictionary<(MetadataObjectKind, string, string?), (int Count, SearchMatchLocation Loc)>();
        foreach (var h in hits)
        {
            if (string.IsNullOrEmpty(h.ObjectName)) continue;
            var key = (h.Kind, h.ObjectName, h.DetailName);
            if (merged.TryGetValue(key, out var cur))
                merged[key] = (cur.Count + h.MatchCount, Prefer(cur.Loc, h.Location));
            else
                merged[key] = (h.MatchCount, h.Location);
        }

        var groups = new List<SearchResultGroup>();
        foreach (var kind in GroupOrder)
        {
            var leaves = merged
                .Where(kv => kv.Key.Item1 == kind)
                .Select(kv => new SearchResultLeaf(kv.Key.Item2, kv.Key.Item3, kv.Value.Count, kv.Value.Loc))
                .OrderBy(l => l.ObjectName, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.DetailName ?? string.Empty, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (leaves.Count > 0)
                groups.Add(new SearchResultGroup(kind, leaves));
        }
        return groups;
    }

    // FieldName is most specific (nested), then Source / Message (content), then Name.
    private static SearchMatchLocation Prefer(SearchMatchLocation a, SearchMatchLocation b)
        => Rank(b) > Rank(a) ? b : a;

    private static int Rank(SearchMatchLocation loc) => loc switch
    {
        SearchMatchLocation.FieldName => 3,
        SearchMatchLocation.Source => 2,
        SearchMatchLocation.Message => 2,
        _ => 1, // Name
    };
}
