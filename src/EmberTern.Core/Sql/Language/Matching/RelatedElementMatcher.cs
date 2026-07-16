using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Matching;

/// <summary>
/// The inputs a related-element producer needs: the current document text, the caret offset, the
/// current selection text (or null), and the cached <see cref="SemanticModel"/> (or null when the
/// editor has no language model — e.g. the read-only DDL-preview editor). Pure value type — no
/// Avalonia, no document handle — so producers are unit-testable offline.
/// </summary>
public readonly record struct MatchContext(string Text, int Caret, string? Selection, SemanticModel? Model);

/// <summary>
/// One source of "related elements" to highlight together. Stage 8 / M1 turns the editor's several
/// ad-hoc occurrence/reference highlighters into interchangeable producers feeding ONE renderer: alias
/// occurrences, matching brackets, and matching <c>BEGIN/END</c> are all just producers, and a future
/// structural pair (CASE/END, LOOP, …) is one more producer — never another renderer.
/// <para>A producer adds the spans it wants highlighted for the given context to <paramref name="into"/>
/// and adds nothing when it has no match. It must never throw on incomplete/invalid input (§0).</para>
/// </summary>
public interface IRelatedElementProducer
{
    void Collect(MatchContext ctx, ICollection<TextSpan> into);
}

/// <summary>
/// Runs a set of <see cref="IRelatedElementProducer"/>s over a <see cref="MatchContext"/> and returns
/// the merged, de-duplicated set of spans to highlight. The single computation seam behind the App's
/// <c>RelatedElementsRenderer</c> — pure and testable, so bracket / BEGIN-END matching is proven without
/// a window.
/// </summary>
public sealed class RelatedElementMatcher
{
    private readonly IReadOnlyList<IRelatedElementProducer> _producers;

    public RelatedElementMatcher(IReadOnlyList<IRelatedElementProducer> producers)
        => _producers = producers ?? throw new ArgumentNullException(nameof(producers));

    /// <summary>The default M1 producer set: selection-word occurrences, the semantic caret-symbol
    /// references, caret-adjacent bracket pairs, and caret-adjacent <c>BEGIN/END</c> pairs.</summary>
    public static RelatedElementMatcher CreateDefault() => new(new IRelatedElementProducer[]
    {
        new SelectionOccurrenceProducer(),
        new CaretSymbolReferenceProducer(),
        new BracketMatchProducer(),
        new BlockMatchProducer(),
    });

    /// <summary>The spans to highlight for <paramref name="ctx"/>, de-duplicated (a span produced by
    /// two producers — e.g. a selected alias that is also the caret symbol — is drawn once).</summary>
    public IReadOnlyList<TextSpan> Match(MatchContext ctx)
    {
        var set = new HashSet<TextSpan>();
        foreach (var producer in _producers)
        {
            producer.Collect(ctx, set);
        }
        if (set.Count == 0) return Array.Empty<TextSpan>();
        var result = new TextSpan[set.Count];
        set.CopyTo(result);
        return result;
    }
}
