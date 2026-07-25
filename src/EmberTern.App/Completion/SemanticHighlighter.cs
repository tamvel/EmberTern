using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using EmberTern.Core.Sql.Language.Highlighting;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// The semantic-highlight "accent" layer (Etap 6 / M3, design §9.2) — colours identifier
/// occurrences by their <em>resolved role</em> on top of the lexical (XSHD) base coat. A
/// <see cref="DocumentColorizingTransformer"/> fed by the per-editor
/// <see cref="EditorLanguageService"/>'s cached <see cref="SemanticModel"/> (shared via the
/// <see cref="SqlCompletionController"/>, so there is one background parse per editor). The
/// classification is pure Core (<see cref="SemanticHighlightClassifier"/>); this class is thin glue:
/// map the class → a theme brush and paint.
/// <para>
/// Navigable schema objects reuse the metadata tree's per-kind <c>IconColor_*</c> palette (editor
/// colour == tree icon → teaches "coloured object = navigable"); local names (aliases / PSQL
/// variables / parameters / cursors / CTEs) get a distinct low-chroma <c>EditorLocalBrush</c>, and
/// trigger context variables (NEW/OLD/INSERTING/UPDATING/DELETING) get the context-variable colour
/// (<c>EditorContextVariableBrush</c>). Columns are deliberately left uncoloured (they fall back to the default
/// foreground) so the object accent stays dominant — see <see cref="ResolveBrush"/>. Read-only
/// paint — §0 holds by construction.
/// Lexical keywords/strings/numbers are never touched (the classifier only colours resolved
/// identifiers, which the XSHD layer leaves at the default foreground), so the two layers are disjoint.
/// </para>
/// </summary>
internal sealed class SemanticHighlighter : DocumentColorizingTransformer
{
    private readonly TextEditor _editor;
    private readonly Func<SemanticModel?> _model;

    private SemanticHighlighter(TextEditor editor, Func<SemanticModel?> model)
    {
        _editor = editor;
        _model = model;
    }

    /// <summary>Attaches semantic highlighting to <paramref name="editor"/>, driven by
    /// <paramref name="controller"/>'s cached model. Repaints whenever the model is rebuilt.</summary>
    public static void Attach(TextEditor editor, SqlCompletionController controller)
    {
        Attach(editor, () => controller.Model);
        controller.ModelUpdated += (_, _) => editor.TextArea.TextView.Redraw();
    }

    /// <summary>Attaches with an explicit model source (test seam — the production overload wires it
    /// to the completion controller's cached model + a repaint on model change).</summary>
    internal static SemanticHighlighter Attach(TextEditor editor, Func<SemanticModel?> model)
    {
        var hl = new SemanticHighlighter(editor, model);
        editor.TextArea.TextView.LineTransformers.Add(hl);
        return hl;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var model = _model();
        if (model is null) return;

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        // Collect the references intersecting this line, then apply lowest priority first so a
        // stronger class (object/column) wins over a weaker one (local) on an exact overlap — a
        // table referenced by its own name records both a schema-object occurrence and an implicit
        // table-reference occurrence at the same span, and it should read as the object.
        List<Hit>? hits = null;
        foreach (var reference in model.References)
        {
            var span = reference.Span;
            if (span.Length == 0 || span.End <= lineStart || span.Start >= lineEnd) continue;

            var brush = ResolveBrush(SemanticHighlightClassifier.Classify(reference), out int priority);
            if (brush is null) continue;

            int start = Math.Max(span.Start, lineStart);
            int end = Math.Min(span.End, lineEnd);
            if (start >= end) continue;

            (hits ??= new List<Hit>()).Add(new Hit(start, end, brush, priority));
        }

        if (hits is null) return;
        hits.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));
        foreach (var hit in hits)
        {
            var brush = hit.Brush;
            ChangeLinePart(hit.Start, hit.End, el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private readonly record struct Hit(int Start, int End, IBrush Brush, int Priority);

    /// <summary>Test seam: the foreground brush the highlighter paints at <paramref name="offset"/> —
    /// the highest-priority class among every reference covering it. At a bare-name FROM (<c>FROM
    /// myview</c>) two references overlap (the schema object + the implicit table reference); the object
    /// class (priority 2) must beat the "local" class (priority 0), so the name reads as an object, not
    /// a plain identifier. Null when nothing is painted there. Mirrors <see cref="ColorizeLine"/>'s
    /// winner (which paints highest-priority last).</summary>
    internal IBrush? PaintedBrushAt(int offset)
    {
        var model = _model();
        if (model is null) return null;
        IBrush? winner = null;
        int best = int.MinValue;
        foreach (var reference in model.References)
        {
            var span = reference.Span;
            if (offset < span.Start || offset >= span.End) continue;
            var brush = ResolveBrush(SemanticHighlightClassifier.Classify(reference), out int priority);
            if (brush is null) continue;
            if (priority >= best) { best = priority; winner = brush; }
        }
        return winner;
    }

    private IBrush? ResolveBrush(SemanticHighlight h, out int priority)
    {
        string? key;
        switch (h.Class)
        {
            case SemanticHighlightClass.Local:
                key = "EditorLocalBrush";
                priority = 0;
                break;
            case SemanticHighlightClass.ContextVariable:
                // Trigger context variables (NEW/OLD/INSERTING/UPDATING/DELETING) — coloured like the
                // language's other context variables (the Function/context-constant palette) so they
                // read as core trigger constructs, not plain locals.
                key = "EditorContextVariableBrush";
                priority = 1;
                break;
            case SemanticHighlightClass.Column:
                // Columns are intentionally NOT semantically coloured in the editor (user preference,
                // 2026-07-13): across a wide SELECT list, coloured columns compete with the object
                // accent and hurt readability — most visibly in Light theme. The accent layer
                // emphasises navigable OBJECTS; columns fall back to the default foreground / lexical
                // layer. Core still classifies them as Column, so the Quick Info card header colour
                // and any other consumer of the classifier are unaffected — only this in-editor paint
                // opts out.
                priority = 0;
                return null;
            case SemanticHighlightClass.SchemaObject:
                // D15.1: a domain used as a data type reads like a SQL type (the shared teal), not the
                // per-kind object colour — a domain IS a type in a declaration. Every other object keeps
                // the tree-icon palette (coloured object == navigable). A dedicated domain accent is a
                // deferred follow-up (§3.4); this maps the resolved domain reference to the type brush.
                key = h.ObjectKind == SymbolKind.Domain
                    ? "EditorDataTypeBrush"
                    : EditorSemanticColors.ObjectBrushKey(h.ObjectKind);
                priority = 2;
                break;
            default:
                priority = 0;
                return null;
        }
        return key is null ? null : ResolveThemeBrush(key);
    }

    private IBrush? ResolveThemeBrush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
        {
            return b;
        }
        return null;
    }
}
