using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Hover;

/// <summary>
/// Composes the ONE hover surface (post-Stage-7 "Unified Hover Information", design
/// <c>editor-stage7-diagnostics.md</c> §15): given a built <see cref="SemanticModel"/>, the
/// <b>already-computed</b> diagnostics for it, and an offset, it returns what the hover should say —
/// the diagnostic explaining a squiggle, the semantic Quick Info for a symbol, or both.
///
/// <para><b>It performs no analysis, and that is enforced by the signature.</b> The diagnostics are an
/// <em>input</em> — the language service's cached, version-matched list — so this engine <em>cannot</em>
/// re-run <see cref="DiagnosticsEngine"/> even by mistake. It is a pure offset lookup over two results
/// that already exist: no parse, no model rebuild, no second analysis pass, no metadata fetch.</para>
///
/// <para><b>Why Core rather than the App:</b> "presentation-layer" means "no new analysis", not "no
/// model". The composition is semantic (which reference is at this offset, which findings cover it),
/// it belongs beside <see cref="QuickInfo.QuickInfoEngine"/>, and here it is zero-Avalonia and
/// headlessly unit-testable — the App is left with pure rendering.</para>
///
/// <para>Read-only, so §0 (never lose information) holds by construction. Error-tolerant: never throws;
/// an offset with nothing to say yields <c>null</c>.</para>
/// </summary>
public static class HoverInfoEngine
{
    /// <summary>
    /// The hover for <paramref name="offset"/>, or <c>null</c> when there is nothing to show.
    /// </summary>
    /// <param name="model">The editor's cached semantic model.</param>
    /// <param name="diagnostics">The cached, version-matched diagnostics of <paramref name="model"/>.
    /// An input, never recomputed — pass <c>DiagnosticsEngine</c>'s existing result.</param>
    /// <param name="offset">The document offset under the pointer.</param>
    public static HoverInfo? GetHover(SemanticModel model, IReadOnlyList<Diagnostic> diagnostics, int offset)
    {
        if (model is null) return null;
        diagnostics ??= Array.Empty<Diagnostic>();

        // The gate is "a resolved symbol OR a diagnostic" — NOT symbol resolution, which is what a
        // symbol-shaped hover would key on. The most common unified case has no Quick Info at all: an
        // unknown object's reference did not resolve, so GetQuickInfo returns null exactly where ET0001
        // fires. Keying on the symbol would blind the hover to precisely the errors it exists to explain.
        var reference = model.ReferenceAt(offset);
        var info = QuickInfo.QuickInfoEngine.GetQuickInfo(model, offset);
        var hits = DiagnosticsAt(diagnostics, offset);

        if (info is null && hits.Count == 0) return null;

        return new HoverInfo(ApplicableSpan(reference, info, hits, offset), hits, info);
    }

    /// <summary>
    /// The diagnostics covering <paramref name="offset"/>, in the engine's own order.
    /// <para>
    /// Hit-testing is <b>inclusive at the span end</b> and mirrors <see cref="SemanticModel.ReferenceAt"/>
    /// exactly (gotcha #198) — reusing the model's offset convention rather than inventing a second one
    /// is what keeps the two sections of a hover agreeing about what "here" means.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Diagnostic> DiagnosticsAt(IReadOnlyList<Diagnostic> all, int offset)
    {
        List<Diagnostic>? hits = null;
        foreach (var d in all)
        {
            if (offset >= d.Start && offset <= d.End) (hits ??= new List<Diagnostic>()).Add(d);
        }
        return hits ?? (IReadOnlyList<Diagnostic>)Array.Empty<Diagnostic>();
    }

    // The narrowest span among the sections actually being shown — the region this content is valid
    // for. Narrowest (not the union) is the correct choice: a statement-wide ET0006 overlapping a
    // 5-char column reference must still re-query when the pointer leaves the column, because the
    // content genuinely differs there. Mirrors ReferenceAt's narrowest-wins tie-break.
    private static TextSpan ApplicableSpan(
        SymbolReference? reference, QuickInfo.QuickInfo? info, IReadOnlyList<Diagnostic> hits, int offset)
    {
        TextSpan? best = null;

        // Only when the symbol actually produced a section: an unresolved reference contributes no
        // content, so its span must not shrink the region this hover claims to describe.
        if (info is not null && reference is not null) best = reference.Span;

        foreach (var d in hits)
        {
            var span = new TextSpan(d.Start, d.Length);
            if (best is null || span.Length < best.Value.Length) best = span;
        }

        // Degenerate fallback: a zero-length finding at the offset still needs a span to sit on.
        return best ?? new TextSpan(offset, 0);
    }
}
