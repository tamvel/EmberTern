using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Completion;

/// <summary>
/// The single filtering + ranking authority for interactive completion — a <b>prediction engine, not a
/// search engine</b>. Given the candidate items (already kind-ranked by <see cref="CompletionEngine"/>)
/// and the prefix the user is typing, it returns the final list the UI displays verbatim. Pure Core,
/// so the philosophy is one place and unit-testable; the App performs no filtering of its own and
/// AvaloniaEdit's built-in substring filter is switched off.
/// <para>Rules (identical for every kind — tables, views, procedures, functions, columns, variables,
/// parameters, aliases, keywords, snippets):</para>
/// <list type="number">
///   <item>Empty prefix (Ctrl+Space with no partial word) → all in-scope items, in their existing rank.</item>
///   <item>Non-empty prefix → keep <b>only</b> items whose text <b>StartsWith</b> the prefix
///   (case-insensitive). <b>Never</b> substring/Contains — that is Global Search, a different workflow.</item>
///   <item>Exact (case-insensitive) matches float to the very top, ahead of longer StartsWith matches.</item>
///   <item>Zero StartsWith matches → empty list (the App closes the popup). No Contains fallback.</item>
/// </list>
/// Within each tier the incoming order is preserved (stable), so the engine's
/// <see cref="CompletionItem.SortPriority"/> + name ordering still breaks ties.
/// </summary>
public static class CompletionMatcher
{
    /// <summary>Filters and ranks <paramref name="items"/> for <paramref name="prefix"/> per the rules
    /// above. Returns <paramref name="items"/> unchanged for an empty prefix; a possibly-empty list
    /// otherwise. Matches on <see cref="CompletionItem.InsertText"/> (the identifier that gets typed).</summary>
    public static IReadOnlyList<CompletionItem> Filter(IReadOnlyList<CompletionItem> items, string? prefix)
    {
        if (items is null || items.Count == 0) return System.Array.Empty<CompletionItem>();
        if (string.IsNullOrEmpty(prefix)) return items;

        List<CompletionItem>? exact = null;
        List<CompletionItem>? startsWith = null;

        foreach (var item in items)
        {
            var text = item.InsertText;
            if (string.IsNullOrEmpty(text)) continue;
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            // StartsWith + equal length == case-insensitive equality → the exact tier.
            if (text.Length == prefix!.Length) (exact ??= new List<CompletionItem>()).Add(item);
            else (startsWith ??= new List<CompletionItem>()).Add(item);
        }

        if (exact is null && startsWith is null) return System.Array.Empty<CompletionItem>();

        var result = new List<CompletionItem>((exact?.Count ?? 0) + (startsWith?.Count ?? 0));
        if (exact is not null) result.AddRange(exact);
        if (startsWith is not null) result.AddRange(startsWith);
        return result;
    }
}
