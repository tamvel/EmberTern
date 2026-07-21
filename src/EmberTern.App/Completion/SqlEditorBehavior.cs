using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// The ONE attach path for an AvaloniaEdit <see cref="TextEditor"/>'s SQL-editor language capabilities —
/// completion (<see cref="SqlCompletionController"/>), semantic highlighting, hover + open-object navigation,
/// diagnostic squiggles, related-elements highlighting, language completion, typing ergonomics, and
/// Find/Replace. Every SQL surface goes through here: the object editors (Procedure / Function / Trigger /
/// View / Package detail + Script Executor) and — since D3 — the main SQL Editor, which calls this once its
/// VM arrives (<c>MainWindow.OnDataContextChanged</c>), rather than hand-wiring a second copy.
/// <para>
/// This is the "intrinsic block" — the capabilities that are identical on every surface (gotcha #219: they
/// used to live in two hand-maintained copies, which is how S3 shipped with no squiggles in the main editor).
/// Genuinely per-host wiring stays with the caller: the Diagnostics panel + F8 nav
/// (<see cref="DiagnosticsPanelHost"/>), Easy-mode ambient refresh (<see cref="AmbientModelRefresh"/>), and
/// the metadata-object drop target (<see cref="EmberTern.App.Sql.SqlSnippetDropTarget"/>).
/// </para>
/// <para>Requires a stable, non-null <see cref="MainWindowViewModel"/> — the completion controller subscribes
/// to that VM's metadata events (leak-free via the editor's visual-tree lifetime) and warms referenced
/// objects itself. The main SQL editor cannot call this in its ctor because the window's VM is set after
/// construction; it waits for the VM ("subscribe once the VM arrives").</para>
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
        Func<IReadOnlyList<Symbol>>? ambientSymbols = null,
        Func<string, EmberTern.Core.Sql.Language.Hover.DebugHoverValue?>? debugValueLookup = null)
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

        // Hover + navigation (Etap 6 / M4 + the unified hover): PLAIN hover → one info card (the
        // diagnostic behind a squiggle and/or the semantic Quick Info); Ctrl+hover → the underline +
        // hand-cursor actionability cue; Ctrl+Click → go-to-definition. All driven by the same cached
        // model + cached diagnostics. Owns the Ctrl+Click gesture; a name-based open is the fallback for
        // editors the model can't fully resolve (e.g. a body-only Easy-mode trigger editor whose CREATE
        // header isn't in the text).
        NavigationController.Attach(
            editor,
            () => completion.Model,
            // The cached, version-matched diagnostics — the same list the squiggles paint from, so the
            // underline and its explanation can never disagree.
            () => completion.Diagnostics,
            () => completion.IsPopupOpen,
            (name, kind) => vm.TryOpenSchemaObject(name, kind),
            word => vm.TryOpenDdlForWord(word),
            (name, kind) => vm.FetchObjectDefinitionAsync(name, kind),
            // Double-click on a value → the unified Parameter Helper (owned by the completion controller).
            showParameterHelper: offset => completion.TryShowParameterHelperAt(offset),
            // Debugger data tips (spec §9.4): the paused frame's value for the variable under the pointer.
            // Null on every non-debugger surface, so the SQL/object editors are unaffected.
            debugValueLookup: debugValueLookup);

        // Stage 7 / S3: diagnostic squiggles — a wavy underline under each Diagnostic the pure-Core
        // DiagnosticsEngine produced, computed on the same background pass the completion controller
        // owns (one parse per editor) and repainted on the shared ModelUpdated cycle. Renderer only —
        // no analysis on the paint path. (D3: this is now the single attach path — the main SQL Editor
        // routes through here too, so there is no second copy to keep in sync.)
        SquiggleRenderer.Attach(editor, completion);

        // Stage 8 / M1: Related Elements Highlighting — ONE renderer for selection-word occurrences, the
        // caret symbol's references, matching brackets, and matching BEGIN/END. Fed by the same cached
        // model (one background parse per editor). Replaced the former occurrence + reference highlighters.
        RelatedElementsRenderer.Attach(editor, completion);

        // Language Completion: finish a daily Firebird construct the developer started typing (if→if () then,
        // gro→group by) via Tab + a passive OverlayLayer hint. Thin, stateless consumer of the pure Core
        // resolver; shares the completion controller only to avoid competing with the list.
        LanguageExpansionController.Attach(editor, completion);

        // Typing Ergonomics: the mechanical editing aids — `begin … end` pairing, delimiter pairing,
        // auto-indent. A separate responsibility from Language Completion (which finishes constructs), and
        // a thin consumer of the pure Core rules.
        TypingErgonomicsController.Attach(editor);

        // Find (Ctrl+F) / Replace (Ctrl+H) + right-click menu — one shared installer.
        EditorSearch.Install(editor);

        // Double-click (INSERT/VALUES helper + name-based open) is owned by NavigationController — one
        // handler, no duplicate here (§10 / P6).

        return completion;
    }

    /// <summary>
    /// Attaches the SEMANTIC highlighting layer ONLY to a <b>read-only SQL preview</b> editor — the DDL
    /// tabs of the object editors and the sidebar DDL preview — so those surfaces colour schema objects
    /// and domains exactly like the main Editor tab (D15.1's "app-wide highlighting"). It deliberately
    /// does NOT wire the interactive machinery (<see cref="SqlCompletionController"/> completion,
    /// squiggles, typing ergonomics) that a read-only preview must not have. The lexical XSHD layer is
    /// applied by each view's own theme code (<c>ApplyEditorTheme</c>); this adds the missing semantic
    /// accent layer, which was the DDL/Editor inconsistency.
    /// <para>The model is rebuilt from the editor's text + the window's <see cref="MainWindowViewModel"/>
    /// metadata snapshot on every text change and whenever a metadata category finishes loading (so
    /// late-loaded objects begin to resolve). The VM is resolved from the visual tree on attach — every
    /// DDL preview calls this with just the editor, no per-view VM plumbing — and the metadata
    /// subscription is released on detach, so it is leak-free.</para>
    /// </summary>
    public static void AttachReadOnlyHighlighting(TextEditor editor)
    {
        if (editor is null) return;

        SemanticModel? model = null;
        MainWindowViewModel? vm = null;

        void Rebuild()
        {
            var text = editor.Text;
            model = string.IsNullOrEmpty(text) || vm is null
                ? null
                : SemanticModel.Build(text, vm.CreateMetadataSnapshot());
            editor.TextArea.TextView.Redraw();
        }

        // Highlight-only: a bare model source (the test/read-only seam), no controller.
        SemanticHighlighter.Attach(editor, () => model);

        void OnObjectsChanged() => Rebuild();

        editor.TextChanged += (_, _) => Rebuild();
        editor.AttachedToVisualTree += (_, _) =>
        {
            vm = editor.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;
            if (vm is not null) vm.Metadata.ObjectsChanged += OnObjectsChanged;
            Rebuild();
        };
        editor.DetachedFromVisualTree += (_, _) =>
        {
            if (vm is not null) vm.Metadata.ObjectsChanged -= OnObjectsChanged;
        };
    }
}
