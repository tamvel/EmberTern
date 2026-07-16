using System;
using System.Collections.Generic;
using AvaloniaEdit;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// Shared wiring that gives any AvaloniaEdit <see cref="TextEditor"/> the same
/// SQL-editor capabilities the main SQL Editor has — autocomplete (object + column
/// suggestions via <see cref="SqlCompletionController"/>) and open-object navigation
/// (double-click + Ctrl+click on an identifier → <see cref="MainWindowViewModel.TryOpenDdlForWord"/>).
/// One implementation, reused by the SQL Editor's surfaces and the Procedure Detail
/// editors — the completion + resolution logic lives in <see cref="SqlCompletionController"/>
/// / <see cref="SqlCompletionContext"/> / the VM, not duplicated here.
/// </summary>
internal static class SqlEditorBehavior
{
    /// <param name="contextTableProvider">For a trigger body editor: returns the
    /// trigger's table so <c>NEW.</c> / <c>OLD.</c> complete that table's columns.
    /// Null for ordinary editors (NEW/OLD have no meaning there).</param>
    /// <param name="ambientSymbols">For an Easy-mode routine BODY editor: the routine's parameters
    /// and DECLAREd variables, which live in the surrounding grids rather than in the body text —
    /// without them the model cannot see them and Ctrl+Space offers no params/locals. Null for the
    /// SQL editor and Source mode, where the text already contains every declaration.</param>
    public static SqlCompletionController Attach(
        TextEditor editor,
        MainWindowViewModel vm,
        Func<string?>? contextTableProvider = null,
        Func<IReadOnlyList<Symbol>>? ambientSymbols = null)
    {
        var completion = new SqlCompletionController(
            editor,
            metadataSnapshot: vm.CreateMetadataSnapshot,
            ensureColumnsAsync: t => vm.EnsureColumnsAsync(t),
            contextTableProvider: contextTableProvider,
            ensureRoutineParamsAsync: t => vm.EnsureRoutineParametersAsync(t),
            // Rebuild the semantic model when a metadata category finishes loading, so objects loaded
            // after the editor opened (views / selectable procedures used in FROM) begin resolving for
            // highlight / Ctrl-nav / Quick Info. Scoped to the editor's visual-tree lifetime inside the
            // controller, so this subscription to the long-lived Metadata singleton is leak-free.
            subscribeMetadataChanged: h => vm.Metadata.ObjectsChanged += h,
            unsubscribeMetadataChanged: h => vm.Metadata.ObjectsChanged -= h,
            // Metadata generation → a deliberate trigger rebuilds the model when a category loaded
            // after this editor opened, so completion/highlight is live without a keystroke.
            metadataGeneration: () => vm.Metadata.ObjectsGeneration,
            // Sprint 1 (point b) + Package 5 (Stage B/C): warm the referenced objects' columns + rich
            // detail so the model is complete for the text without typing "table." — every SQL surface.
            warmReferencedMetadata: (names, ct) => vm.WarmReferencedAsync(names, ct),
            // Package 5 closure: prefetch-complete → definitive rebuild + full warm + publish, scoped to
            // the editor's visual-tree lifetime (leak-free).
            subscribeMetadataReady: h => vm.Metadata.MetadataReady += h,
            unsubscribeMetadataReady: h => vm.Metadata.MetadataReady -= h,
            // Easy-mode routine bodies: seed the model with the params/variables held in the grids.
            ambientSymbols: ambientSymbols);

        // Semantic highlighting (Etap 6): colour identifiers by resolved role, fed by the same
        // cached semantic model the completion controller owns (one background parse per editor).
        SemanticHighlighter.Attach(editor, completion);

        // Navigation (Etap 6 / M4): Ctrl+hover (underline + hand cursor + Quick Info tooltip) and
        // Ctrl+Click go-to-definition, driven by the same cached model. Owns the Ctrl+Click gesture;
        // a name-based open is the fallback for editors the model can't fully resolve (e.g. a
        // body-only Easy-mode trigger editor whose CREATE header isn't in the text).
        NavigationController.Attach(
            editor,
            () => completion.Model,
            (name, kind) => vm.TryOpenSchemaObject(name, kind),
            word => vm.TryOpenDdlForWord(word),
            (name, kind) => vm.FetchObjectDefinitionAsync(name, kind),
            // Double-click on a value → the unified Parameter Helper (owned by the completion controller).
            showParameterHelper: offset => completion.TryShowParameterHelperAt(offset));

        // Stage 7 / S3: diagnostic squiggles — a wavy underline under each Diagnostic the pure-Core
        // DiagnosticsEngine produced, computed on the same background pass the completion controller
        // owns (one parse per editor) and repainted on the shared ModelUpdated cycle. Renderer only —
        // no analysis on the paint path. Attached here so EVERY SQL surface gets it at once.
        SquiggleRenderer.Attach(editor, completion);

        // Select-an-identifier → box all its occurrences in this editor.
        OccurrenceHighlighter.Attach(editor);

        // Find (Ctrl+F) / Replace (Ctrl+H) + right-click menu — one shared installer.
        EditorSearch.Install(editor);

        // Double-click (INSERT/VALUES helper + name-based open) is owned by NavigationController — one
        // handler, no duplicate here (§10 / P6).

        return completion;
    }
}
