using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Search;

/// <summary>Where a search term was found — drives the leaf label / chip in the
/// Search Results tree and how the object opens.</summary>
public enum SearchMatchLocation
{
    /// <summary>In the object's own name (e.g. a procedure named …TERM…).</summary>
    Name,
    /// <summary>In the object's source body (procedure/function/trigger/view/package).</summary>
    Source,
    /// <summary>In an exception's message text (RDB$MESSAGE).</summary>
    Message,
    /// <summary>In a table column name — <see cref="MetadataSearchHit.DetailName"/>
    /// carries the field; <see cref="MetadataSearchHit.ObjectName"/> the table.</summary>
    FieldName,
}

/// <summary>
/// One search result: an object (optionally a nested field) whose name / source /
/// message contains the search term, with a per-object occurrence count.
/// Pure DTO (record) — additive-extensible without breaking the positional ctor.
/// </summary>
public sealed record MetadataSearchHit(
    MetadataObjectKind Kind,
    string ObjectName,
    SearchMatchLocation Location,
    int MatchCount,
    string? DetailName = null);

/// <summary>
/// A metadata search request: the term plus what to search (names / source),
/// matching options, and which object kinds to include.
/// </summary>
public sealed record MetadataSearchQuery(
    string Term,
    bool MatchNames = true,
    bool MatchSource = true,
    bool CaseSensitive = false,
    bool WholeWord = false,
    IReadOnlyList<MetadataObjectKind>? Kinds = null)
{
    /// <summary>Object kinds EmberTern's Global Search covers. Source-searchable
    /// kinds (proc/func/trigger/view/package) plus exception (name+message) and
    /// name-only kinds (table+fields, domain, generator).</summary>
    public static readonly IReadOnlyList<MetadataObjectKind> SupportedKinds = new[]
    {
        MetadataObjectKind.Table,
        MetadataObjectKind.View,
        MetadataObjectKind.Procedure,
        MetadataObjectKind.Trigger,
        MetadataObjectKind.Function,
        MetadataObjectKind.Package,
        MetadataObjectKind.Exception,
        MetadataObjectKind.Domain,
        MetadataObjectKind.Generator,
    };

    /// <summary>The requested kinds, defaulting to <see cref="SupportedKinds"/>.</summary>
    public IReadOnlyList<MetadataObjectKind> EffectiveKinds => Kinds ?? SupportedKinds;

    public bool Includes(MetadataObjectKind kind) => EffectiveKinds.Contains(kind);
}
