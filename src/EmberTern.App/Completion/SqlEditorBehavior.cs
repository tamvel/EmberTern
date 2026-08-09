using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
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
    /// <summary>"Show the code actions at this editor's caret", published by <see cref="Attach"/> so any
    /// surface holding the editor can reach the one code-action flow. Null on an editor that was never
    /// attached (a read-only DDL preview), which is also exactly where actions must not be offered.</summary>
    public static readonly Avalonia.AttachedProperty<Func<bool>?> CodeActionsProperty =
        Avalonia.AvaloniaProperty.RegisterAttached<TextEditor, Func<bool>?>(
            "CodeActions", typeof(SqlEditorBehavior));

    public static void SetCodeActions(TextEditor editor, Func<bool>? value)
        => editor.SetValue(CodeActionsProperty, value);

    public static Func<bool>? GetCodeActions(TextEditor editor)
        => editor.GetValue(CodeActionsProperty);

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
        var navigation = NavigationController.Attach(
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

        // Stage Q / Q5: publish "show code actions here" ON the editor. The Diagnostics panel is hosted
        // by eleven different views, none of which build the NavigationController — an attached property
        // lets the panel reach the ONE flow without threading a delegate through every one of them.
        SetCodeActions(editor, navigation.TryShowCodeActions);

        // Stage Q / Q3: the code-action bulb re-evaluates the moment the diagnostics are recomputed —
        // which is exactly when a just-applied fix must stop being offered. Waiting for its own dwell
        // would leave a stale bulb proposing a repair for a problem that no longer exists.
        completion.ModelUpdated += (_, _) => navigation.RefreshCodeActionIndicator();

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

    /// <summary>
    /// The COMPLETE read-only SQL preview wiring: the semantic accent layer
    /// (<see cref="AttachReadOnlyHighlighting"/>) <b>plus</b> the XSHD lexical palette and the shared
    /// selection brush, re-applied on every theme change.
    ///
    /// <para>⭐ It exists because <see cref="AttachReadOnlyHighlighting"/> wires only HALF of what a preview
    /// needs, and the other half — pick the light/dark XSHD definition, set <c>SelectionBrush</c>, re-run both
    /// on <c>ActualThemeVariantChanged</c> — was copied by hand into <b>twelve</b> views as an identical
    /// ~15-line <c>ApplyEditorTheme</c>. Five more previews were about to become copies 13–17, which is the
    /// point at which the duplication stops being a coincidence and becomes the mechanism.</para>
    ///
    /// <para>⚠ The theme subscription is on the EDITOR, not on the host view, so the wiring needs nothing
    /// from the caller and cannot outlive the control it decorates. That is also why this is one call rather
    /// than a base class: a preview appears inside a tab, a dialog and a panel, which share no host type.</para>
    ///
    /// <para>⚠⚠ SCOPE OF THE SEMANTIC LAYER, MEASURED NOT ASSUMED: <see cref="AttachReadOnlyHighlighting"/>
    /// resolves its metadata from <c>editor.FindAncestorOfType&lt;Window&gt;()?.DataContext as
    /// MainWindowViewModel</c>. In a DIALOG that DataContext is the dialog's own view model, so the model
    /// stays null and only the LEXICAL layer paints. That is a real limit, not a bug to chase here: a dialog
    /// preview shows keywords, types, literals and comments in the app's colours, and the object accents it
    /// cannot resolve are exactly the ones a not-yet-created object would not have anyway.</para>
    /// </summary>
    public static void AttachReadOnlyPreview(TextEditor editor)
    {
        if (editor is null) return;

        AttachReadOnlyHighlighting(editor);
        ApplyPreviewTheme(editor);
        editor.ActualThemeVariantChanged += (_, _) => ApplyPreviewTheme(editor);
    }

    /// <summary>
    /// The whole Live-DDL-preview wiring in one call: <see cref="AttachReadOnlyPreview"/> plus the text push
    /// from the host's <see cref="IDdlPreviewSource"/>, re-bound whenever the host's DataContext changes.
    ///
    /// <para>⚠ The text is PUSHED, never bound: a two-way <c>TextEditor.Text</c> binding is flaky, which is
    /// the gotcha all twelve existing DDL previews already work around by hand. The unchanged-value guard is
    /// not an optimisation — the DDL is recomputed on every keystroke in the form, and re-assigning identical
    /// text resets the caret and any selection while the user is reading it.</para>
    /// </summary>
    public static void AttachDdlPreview(TextEditor editor, StyledElement host)
    {
        if (editor is null || host is null) return;

        AttachReadOnlyPreview(editor);

        IDdlPreviewSource? bound = null;

        void Push()
        {
            var text = bound?.DdlPreview ?? string.Empty;
            if (editor.Text != text) editor.Text = text;
        }

        void OnSourcePropertyChanged(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(IDdlPreviewSource.DdlPreview)) Push();
        }

        void Rebind()
        {
            if (bound is not null) bound.PropertyChanged -= OnSourcePropertyChanged;
            bound = host.DataContext as IDdlPreviewSource;
            if (bound is not null) bound.PropertyChanged += OnSourcePropertyChanged;
            Push();
        }

        host.DataContextChanged += (_, _) => Rebind();
        Rebind();
    }

    /// <summary>The palette half: the XSHD definition for the current theme plus the shared selection brush.
    /// ⛔ Never a hard-coded colour — both come from the app's one catalog, which is what makes a preview
    /// recolour live with the rest of the window.</summary>
    private static void ApplyPreviewTheme(TextEditor editor)
    {
        var theme = editor.ActualThemeVariant;

        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(
            theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName);

        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var resource) == true
            && resource is IBrush brush)
        {
            editor.TextArea.SelectionBrush = brush;
        }
    }
}
