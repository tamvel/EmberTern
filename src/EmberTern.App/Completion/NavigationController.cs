using System.Globalization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.CodeActions;
using EmberTern.Core.Sql.Language.Hover;
using EmberTern.Core.Sql.Language.Matching;
using EmberTern.Core.Sql.Language.Navigation;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;

namespace EmberTern.App.Completion;

/// <summary>
/// The editor's hover + navigation UX layer (Etap 6 / M4, design §9.4 / §10; unified hover per
/// <c>editor-stage7-diagnostics.md</c> §15). Two deliberately separate cues over one
/// <see cref="TextEditor"/>:
/// <list type="bullet">
///   <item><b>Plain hover (no modifier) = INFORMATION.</b> After a short dwell, ONE card explains what
///   is under the pointer: the diagnostic behind a squiggle, the semantic Quick Info for a symbol, or
///   both as sections (<see cref="HoverInfoEngine"/> composes, <see cref="HoverInfoView"/> renders).</item>
///   <item><b>Ctrl = ACTIONABILITY.</b> Ctrl+hover over a <em>navigable</em> identifier underlines it and
///   switches the cursor to a hand; <b>Ctrl+click</b> goes to the definition via
///   <see cref="NavigationEngine"/> (a schema object opens its detail/DDL tab; a local —
///   alias/variable/parameter/cursor/CTE — jumps the caret to its declaration).</item>
/// </list>
///
/// <para><b>Why information is NOT behind Ctrl.</b> The two cues answer different questions, and the
/// split is the frozen §9.4 decision: the semantic colour is the permanent cue, Ctrl is the actionable
/// one. Requiring Ctrl to learn <em>why</em> a span is squiggled would be a contradiction — the
/// actionability cue means "this leads somewhere", and the most common squiggle (an unknown object)
/// leads NOWHERE: it is unresolved, so it has no navigation target at all. Gating information on
/// <see cref="NavigationEngine.TargetAt"/> (which is what this class used to do) therefore showed
/// nothing precisely where <c>ET0001</c> fires. Plain hover answers "what is this / why is it flagged";
/// Ctrl answers "can I go there".</para>
///
/// <para>A thin App glue over the pure Core engines (<see cref="NavigationEngine"/>,
/// <see cref="HoverInfoEngine"/>, <see cref="QuickInfoEngine"/>): it maps a pointer position to a
/// document offset, asks the engines, and paints/opens. It reads the per-editor cached
/// <see cref="SemanticModel"/> and the language service's cached diagnostics (shared with the completion
/// controller, semantic highlighter and squiggle renderer — one background parse per editor); it never
/// parses or analyses on the pointer path. Read-only, so §0 (never lose information) holds by
/// construction.</para>
///
/// <para>The Ctrl affordance is driven by real resolution, not a name search — so the underline appears
/// exactly where Ctrl+Click will navigate. When the model can't resolve (e.g. a body-only Easy-mode
/// editor whose CREATE header isn't in the text), Ctrl+Click falls back to the name-based open
/// (<see cref="_openByName"/>); the affordance stays semantic (no underline there).</para>
/// </summary>
internal sealed class NavigationController
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // Plain hover fires on every pointer move, where Ctrl+hover was self-limiting — the dwell is the
    // whole noise budget. Long enough not to flash while the pointer crosses the text on its way
    // somewhere, short enough to feel like an answer rather than a wait.
    //
    // ⚠⚠ 350 → 250 ms (user report "Quick Info appears too late", 2026-08-07). The number was IN the normal
    // IDE band (VS Code's editor.hover.delay is 300 ms, Visual Studio ≈ 400, Rider ≈ 500) — so the report
    // looked like it contradicted the measurement, and the reason it does not is UpdateHoverInfo below:
    // the dwell RESTARTS on every offset change outside the open card's span. That is deliberate and must
    // stay (it is what stops cards strobing along the pointer's path), but it means the wait is 350 ms after
    // the pointer STOPS, not 350 ms after it reaches the symbol — and a pointer settling onto a name is
    // exactly the slow-moving case. The perceived delay is therefore strictly longer than the constant, which
    // is why a value at the fast end of the band is the right correction rather than a value below it.
    // ⛔ Do not go under ~200 ms: below that the card starts appearing during the pointer's final approach,
    // which is the flashing this budget exists to prevent.
    private static readonly TimeSpan HoverDwell = TimeSpan.FromMilliseconds(250);
    // Gap between the pointer and the card, so the card never sits under the cursor itself.
    private const double HoverGap = 16;

    // Nudge past the flagged symbol, and the bulb's own footprint (icon + 2px padding each side) —
    // needed to keep it inside the view when the symbol sits near the right edge.
    private const double BulbGap = 3;
    private const double BulbIconSize = 14;
    private const double BulbSize = BulbIconSize + 4;

    private readonly TextEditor _editor;
    private readonly Func<SemanticModel?> _model;
    // The language service's CACHED, version-matched diagnostics — an input, never recomputed here (the
    // hover performs no analysis; HoverInfoEngine's signature enforces that).
    private readonly Func<IReadOnlyList<Diagnostic>> _diagnostics;
    // True while the completion list / Parameter Helper / Quick Info popup owns the screen — the hover
    // stays out of their way rather than stacking on them.
    private readonly Func<bool> _isOtherPopupOpen;
    private readonly Func<string, MetadataObjectKind, bool> _openSchemaObject;
    private readonly Func<string, bool> _openByName;
    // Async fetch of a schema object's DDL/source text for Peek Definition (M5), or null → Peek only
    // works for locals (declaration text from the document, no DB).
    private readonly Func<string, MetadataObjectKind, Task<string?>>? _fetchDefinition;
    private readonly UnderlineRenderer _underline;
    private readonly DispatcherTimer _hoverDwell;

    private TextSpan? _activeSpan;   // the currently-underlined identifier, or null when clear
    private Point _lastPointer;
    private bool _pointerInside;
    private bool _detached;

    // The hover card currently in the editor's OverlayLayer, and the span it describes. Hosted in the
    // overlay rather than a bare Popup: that pattern renders invisibly on the desktop despite
    // IsOpen/Visible/Opacity all true (gotcha #209 — it cost the Parameter Helper a debugging session,
    // and plain hover makes this the primary discovery surface, so it is not a bet worth taking).
    private Control? _hoverCard;
    private TextSpan? _hoverSpan;

    // The code-action menu (Ctrl+., Stage Q). OverlayLayer-hosted for the same reason as the hover card.
    private Control? _codeActionMenu;
    private ListBox? _codeActionList;
    // The caret the open menu was built for. Its actions describe THAT position, so the menu is
    // invalidated the moment the caret leaves it (or the text changes underneath).
    private int _codeActionOffset = -1;

    // The code-action light bulb (Q3) — a DISCOVERABILITY surface only. It decides whether to appear by
    // calling the same GetActionsAtCaret the menu does, and clicking it runs the same ShowCodeActions;
    // it owns no way to obtain or perform an action.
    private Control? _bulb;
    private int _bulbAnchor = -1;             // the document offset it is currently pinned to
    private bool _bulbUpdating;               // re-entrancy guard — see UpdateBulb

    // Inline rename popup (M5), created lazily on first F2.
    private Popup? _renamePopup;
    private Border? _renameBorder;
    private TextBox? _renameBox;
    private NavigationRename? _renameActive;
    private string _renameCurrent = string.Empty;

    // Peek definition flyout (M5), created lazily on first Alt+F12.
    private Popup? _peekPopup;
    private int _peekGeneration; // guards a stale async DDL result from a superseded/closed peek

    // Double-click on a value → the unified Parameter Helper (design §28), owned by the completion
    // controller (one parameter-info surface for both double-click and typing). This delegate shows it
    // at the clicked offset and returns whether that offset is a parameter site; null → no helper wired.
    private readonly Func<int, bool>? _showParameterHelper;

    // Data-tip source for a paused debug session (spec §9.4): given a variable/parameter name, its live
    // frame value, or null. Null on every non-debugger surface (the SQL editor is unaffected).
    private readonly Func<string, DebugHoverValue?>? _debugValueLookup;

    private NavigationController(
        TextEditor editor,
        Func<SemanticModel?> model,
        Func<IReadOnlyList<Diagnostic>> diagnostics,
        Func<bool> isOtherPopupOpen,
        Func<string, MetadataObjectKind, bool> openSchemaObject,
        Func<string, bool> openByName,
        Func<string, MetadataObjectKind, Task<string?>>? fetchDefinition,
        Func<int, bool>? showParameterHelper,
        Func<string, DebugHoverValue?>? debugValueLookup)
    {
        _editor = editor;
        _model = model;
        _diagnostics = diagnostics;
        _isOtherPopupOpen = isOtherPopupOpen;
        _openSchemaObject = openSchemaObject;
        _openByName = openByName;
        _fetchDefinition = fetchDefinition;
        _showParameterHelper = showParameterHelper;
        _debugValueLookup = debugValueLookup;
        _underline = new UnderlineRenderer(editor);

        _hoverDwell = new DispatcherTimer { Interval = HoverDwell };
        _hoverDwell.Tick += OnHoverDwellElapsed;
    }

    /// <summary>Attaches hover + navigation to <paramref name="editor"/>.</summary>
    /// <param name="model">The editor's cached semantic model (from the completion controller).</param>
    /// <param name="diagnostics">The editor's CACHED, version-matched diagnostics (from the same
    /// controller). <b>Required, deliberately</b>: an optional-with-default source would let a wiring
    /// seam silently omit it and leave that surface's squiggles unexplained — exactly the S3 failure
    /// (gotcha #219). Required makes a missed seam a compile error instead.</param>
    /// <param name="isOtherPopupOpen">True while the completion list / Parameter Helper / Quick Info popup
    /// is up, so the hover never stacks on them. Required for the same reason.</param>
    /// <param name="openSchemaObject">Opens a resolved schema object (name + kind) — the VM.</param>
    /// <param name="openByName">Name-based open fallback for editors the model can't fully resolve.</param>
    /// <param name="fetchDefinition">Async fetch of a schema object's DDL/source for Peek Definition
    /// (M5); null → Peek shows only local declarations.</param>
    /// <param name="showParameterHelper">Shows the unified Parameter Helper at a double-clicked offset
    /// (returns whether it is a parameter site); null → double-click only does name-based open.</param>
    /// <param name="debugValueLookup">Data-tip source for a paused debug session (spec §9.4): variable name →
    /// its live frame value; null (the default) on non-debugger surfaces.</param>
    public static NavigationController Attach(
        TextEditor editor,
        Func<SemanticModel?> model,
        Func<IReadOnlyList<Diagnostic>> diagnostics,
        Func<bool> isOtherPopupOpen,
        Func<string, MetadataObjectKind, bool> openSchemaObject,
        Func<string, bool> openByName,
        Func<string, MetadataObjectKind, Task<string?>>? fetchDefinition = null,
        Func<int, bool>? showParameterHelper = null,
        Func<string, DebugHoverValue?>? debugValueLookup = null)
    {
        var c = new NavigationController(
            editor, model, diagnostics, isOtherPopupOpen, openSchemaObject, openByName,
            fetchDefinition, showParameterHelper, debugValueLookup);
        editor.TextArea.TextView.BackgroundRenderers.Add(c._underline);

        // Ctrl+Click — tunneled so it runs before AvaloniaEdit's own selection handling, letting us
        // consume the click (no stray word-select) when it navigates.
        editor.AddHandler(InputElement.PointerPressedEvent, c.OnPointerPressed, RoutingStrategies.Tunnel);

        // Hover tracking on the render surface (where the pointer actually moves).
        editor.TextArea.TextView.PointerMoved += c.OnPointerMoved;
        editor.TextArea.TextView.PointerExited += c.OnPointerExited;

        // Ctrl press/release while the editor has focus re-evaluates at the last pointer position
        // (so holding Ctrl over an already-hovered identifier lights it up, and releasing clears it).
        editor.KeyDown += c.OnKeyChanged;
        editor.KeyUp += c.OnKeyChanged;
        // F2 (rename) / Alt+F12 (peek) / Ctrl+. (code actions) — the command keys.
        editor.KeyDown += c.OnCommandKey;
        // Escape-to-dismiss must beat AvaloniaEdit's own Escape handling, so it tunnels.
        editor.AddHandler(InputElement.KeyDownEvent, c.OnTunnelKey, RoutingStrategies.Tunnel);
        // Double-click → INSERT/VALUES column helper (P6) or name-based open. Consolidated here so
        // there is ONE double-click handler (the two duplicated ones in SqlEditorBehavior / MainWindow
        // move here), avoiding an e.Handled ordering dance between two subscribers on one event.
        editor.DoubleTapped += c.OnDoubleTapped;
        // An edit invalidates the card: it describes an offset in a document that just changed.
        editor.TextChanged += c.OnTextChanged;
        // The code-action bulb follows the caret's LINE and must be repositioned when the view scrolls
        // under it.
        editor.TextArea.Caret.PositionChanged += c.OnCaretMovedForBulb;
        editor.TextArea.TextView.ScrollOffsetChanged += c.OnScrollForBulb;
        // The moment the line geometry becomes valid again — the same signal BreakpointMargin repaints
        // on. A bulb whose placement could not be computed yet gets its chance here.
        editor.TextArea.TextView.VisualLinesChanged += c.OnVisualLinesChangedForBulb;
        return c;
    }

    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_underline);
        _editor.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _editor.TextArea.TextView.PointerMoved -= OnPointerMoved;
        _editor.TextArea.TextView.PointerExited -= OnPointerExited;
        _editor.KeyDown -= OnKeyChanged;
        _editor.KeyUp -= OnKeyChanged;
        _editor.KeyDown -= OnCommandKey;
        _editor.RemoveHandler(InputElement.KeyDownEvent, OnTunnelKey);
        _editor.DoubleTapped -= OnDoubleTapped;
        _editor.TextChanged -= OnTextChanged;
        _editor.TextArea.Caret.PositionChanged -= OnCaretMovedForBulb;
        _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollForBulb;
        _editor.TextArea.TextView.VisualLinesChanged -= OnVisualLinesChangedForBulb;
        HideBulb();
        _hoverDwell.Stop();
        _hoverDwell.Tick -= OnHoverDwellElapsed;
        HideHover();
        CloseCodeActionMenu(); // overlay-hosted, so it would otherwise outlive the controller
        _peekGeneration++;
        if (_renamePopup is not null) _renamePopup.IsOpen = false;
        if (_peekPopup is not null) _peekPopup.IsOpen = false;
    }

    // ── Pointer plumbing ─────────────────────────────────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _pointerInside = true;
        _lastPointer = e.GetPosition(_editor);
        UpdateNavigationAffordance(e.KeyModifiers);  // Ctrl → "this leads somewhere"
        UpdateHoverInfo();                           // plain → "what is this / why is it flagged"
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _pointerInside = false;
        Clear();
    }

    private void OnKeyChanged(object? sender, KeyEventArgs e)
    {
        // Ctrl press/release only re-evaluates the AFFORDANCE. The info card deliberately survives it:
        // pressing Ctrl to navigate what you are already reading must not blink the explanation away.
        if (_pointerInside) UpdateNavigationAffordance(e.KeyModifiers);
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        HideHover();
        // The text changed under the open menu, so its actions describe a document that no longer
        // exists — close it rather than let one be picked.
        CloseCodeActionMenu();
        // The diagnostics now describe text that no longer exists, so the bulb would be offering a fix
        // for a problem that may already be gone. Hide it NOW and let the recomputed diagnostics decide
        // whether it comes back (RefreshCodeActionIndicator, called on ModelUpdated).
        HideBulb();
    }

    /// <summary>Clears both cues — the pointer left, or a click/navigation happened.</summary>
    private void Clear()
    {
        ClearNavigationAffordance();
        HideHover();
    }

    // ── Ctrl = actionability (underline + hand cursor) ───────────────────────────────────────────

    private void UpdateNavigationAffordance(KeyModifiers modifiers)
    {
        if (_detached) return;
        bool ctrl = (modifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (!ctrl || !_pointerInside) { ClearNavigationAffordance(); return; }

        int? offset = OffsetAt(_lastPointer);
        var model = _model();
        var target = offset is { } o && model is not null ? NavigationEngine.TargetAt(model, o) : null;
        if (target is null) { ClearNavigationAffordance(); return; }

        var span = target.ReferenceSpan;
        if (_activeSpan is { } a && a.Start == span.Start && a.Length == span.Length)
        {
            return; // same identifier — already lit; don't redundantly invalidate
        }

        _activeSpan = span;
        _underline.Segment = new TextSegment { StartOffset = span.Start, Length = span.Length };
        _editor.TextArea.TextView.InvalidateVisual();
        _editor.TextArea.TextView.Cursor = HandCursor;
    }

    private void ClearNavigationAffordance()
    {
        if (_activeSpan is null) return; // already clear — avoid redundant invalidations on plain moves
        _activeSpan = null;
        _underline.Segment = null;
        _editor.TextArea.TextView.InvalidateVisual();
        _editor.TextArea.TextView.Cursor = null; // fall back to the editor's I-beam
    }

    // ── Plain hover = information (the unified card) ─────────────────────────────────────────────

    private void UpdateHoverInfo()
    {
        if (_detached) return;
        if (!_pointerInside) { HideHover(); return; }

        int? offset = OffsetAt(_lastPointer);
        if (offset is null) { HideHover(); return; }

        // Still inside the region the open card describes ⇒ its content cannot have changed. Leaving it
        // alone is what stops the card flickering as the pointer drifts across one identifier.
        if (_hoverCard is not null && _hoverSpan is { } s && offset >= s.Start && offset <= s.End) return;

        // Moved onto something else: drop the stale card and re-arm the dwell, so crossing the text on
        // the way somewhere never strobes cards along the path.
        HideHover();
        _hoverDwell.Stop();
        _hoverDwell.Start();
    }

    private void OnHoverDwellElapsed(object? sender, EventArgs e)
    {
        _hoverDwell.Stop();
        if (_detached || !_pointerInside) return;
        // Arbitration: the completion list and the Parameter Helper own the screen while they are up.
        if (_isOtherPopupOpen()) return;

        int? offset = OffsetAt(_lastPointer);
        var model = _model();
        if (offset is null || model is null) return;

        // Pure lookup over already-computed results — no parse, no analysis on the pointer path. The debug
        // value lookup (when wired) reads the paused frame's client-side truth, not the server.
        var hover = HoverInfoEngine.GetHover(model, _diagnostics(), offset.Value, _debugValueLookup);
        if (hover is null) return;
        ShowHover(hover);
    }

    private void ShowHover(HoverInfo hover)
    {
        var overlay = OverlayLayer.GetOverlayLayer(_editor);
        if (overlay is null) return;

        HideHover();
        // Only claim a fix exists when one really does — the hint is computed from the SAME
        // GetActionsAtCaret the menu and the bulb use, over the hovered span's own diagnostics, so the
        // three surfaces can never disagree about whether there is anything to offer. Read-only: the
        // card shows the shortcut, it does not become a way to run it (§15.1.1).
        bool hasFixes = !_editor.IsReadOnly && GetActionsAtCaret(hover.Span.Start).Count > 0;
        var card = HoverInfoView.Build(hover, _editor.ActualThemeVariant, hasFixes);
        // Never intercept the pointer: a hit-testable card under the cursor fires PointerExited on the
        // editor and flickers itself shut. It must also never take focus — hovering is not an action.
        card.IsHitTestVisible = false;
        card.Focusable = false;

        var anchor = _editor.TranslatePoint(new Point(_lastPointer.X, _lastPointer.Y + HoverGap), overlay);
        Canvas.SetLeft(card, anchor?.X ?? 0);
        Canvas.SetTop(card, anchor?.Y ?? 0);
        overlay.Children.Add(card);
        _hoverCard = card;
        _hoverSpan = hover.Span;

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_hoverCard, card)) return;
            // Flip above the POINTER (not the caret), clearing the gap we just added below it.
            EditorPopups.ClampIntoOverlay(overlay, card, flipOffset: HoverGap * 2);
        }, DispatcherPriority.Background);
    }

    private void HideHover()
    {
        _hoverDwell.Stop();
        if (_hoverCard is { } card)
        {
            // ⭐⭐ REMOVE FROM THE PANEL THAT ACTUALLY HOLDS IT — the rule HideBulb already states, and this is
            // the site that made the user pay for it (report 2026-08-03: a hover card left on screen that no
            // tab change, no pointer exit and no click could remove — only restarting the application).
            // GetOverlayLayer(_editor) answers "which overlay would this editor use NOW". Once the tab has been
            // switched the editor is detached, so it answers null (or a different window's layer) and Remove
            // silently does nothing — while the card is still parented in the OLD overlay, which belongs to the
            // WINDOW and therefore outlives every tab. Clearing the field then drops the last reference to it.
            (card.Parent as Panel)?.Children.Remove(card);
            _hoverCard = null;
        }
        _hoverSpan = null;
    }

    // ── Ctrl+Click → navigate ──────────────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Any click dismisses the info card — you have started doing something, not reading.
        HideHover();
        // A click back in the editor dismisses the menu too, and hands the keyboard back. LostFocus
        // alone would close it, but only once focus actually moved; doing it here also covers a click
        // that lands on the editor without changing focus.
        CancelCodeActionMenu();

        var props = e.GetCurrentPoint(_editor).Properties;
        if (!props.IsLeftButtonPressed) return;
        if ((e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control) return;

        int? offset = OffsetAt(e.GetPosition(_editor));
        if (offset is null) return;
        if (Navigate(offset.Value))
        {
            e.Handled = true;
            Clear();
        }
    }

    /// <summary>Test seam: runs the Ctrl+Click navigation decision at <paramref name="offset"/>
    /// without synthesising a pointer event (headless probe). Returns whether it navigated.</summary>
    internal bool NavigateForTest(int offset) => Navigate(offset);

    /// <summary>Test seam: runs the safe-rename decision + apply at <paramref name="offset"/> without
    /// the popup UI (headless probe). Enforces the same §0 guards as the interactive path (renameable
    /// local only, valid name, no document/model drift). Returns whether the rename applied.</summary>
    internal bool TryRenameForTest(int offset, string newName)
    {
        var model = _model();
        if (model is null) return false;
        var rename = NavigationEngine.GetLocalRename(model, offset);
        if (rename is null) return false;

        var saved = _editor.CaretOffset;
        _editor.CaretOffset = offset; // IsValidNewName reads the caret for the in-scope collision check
        try
        {
            if (string.Equals(FoldIdentifier(newName), rename.CurrentName, StringComparison.Ordinal)) return false;
            if (!IsValidNewName(newName, rename)) return false;
            return TryApplyRename(rename, newName);
        }
        finally
        {
            _editor.CaretOffset = Math.Min(saved, _editor.Document?.TextLength ?? saved);
        }
    }

    /// <summary>Test seam: the local-reference highlight spans at <paramref name="offset"/> (headless
    /// probe) — empty unless the offset is on a script-local symbol.</summary>
    internal IReadOnlyList<TextSpan> ReferencesForTest(int offset)
        => CaretSymbolReferenceProducer.Compute(_model(), offset);

    private bool Navigate(int offset)
    {
        var model = _model();
        if (model is not null)
        {
            var target = NavigationEngine.TargetAt(model, offset);
            if (target is not null)
            {
                switch (target.Kind)
                {
                    case NavigationTargetKind.SchemaObject when !string.IsNullOrEmpty(target.ObjectName):
                        return _openSchemaObject(target.ObjectName!, MapKind(target.ObjectKind));

                    case NavigationTargetKind.LocalDefinition:
                        if (target.DefinitionSpan is { } def) JumpTo(def);
                        return true; // navigable local — handled even if the declaration span is unknown
                }
            }
        }

        // The model couldn't resolve it (e.g. a body-only Easy-mode editor). Fall back to a
        // name-based open against loaded metadata.
        var word = SqlCompletionContext.GetWordAt(_editor.Text ?? string.Empty, offset).Text;
        return !string.IsNullOrEmpty(word) && _openByName(word);
    }

    private void JumpTo(TextSpan def)
    {
        var len = _editor.Document?.TextLength ?? 0;
        int start = Math.Clamp(def.Start, 0, len);
        int length = Math.Clamp(def.Length, 0, len - start);
        _editor.CaretOffset = start;
        if (length > 0) _editor.Select(start, length);
        _editor.TextArea.Caret.BringCaretToView();
        _editor.Focus();
    }

    // (The caret-symbol reference highlight moved to the unified RelatedElementsRenderer in Stage 8 / M1 —
    // CaretSymbolReferenceProducer. ReferencesForTest above delegates to it so the headless probe is
    // unchanged. This controller keeps only the Ctrl-hover underline + navigation/rename/peek.)

    // ── Safe local rename — F2 (M5, §0 / §10) ────────────────────────────────────────────────────

    private void OnCommandKey(object? sender, KeyEventArgs e)
    {
        if (_detached) return;
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
        {
            if (BeginRename()) e.Handled = true;
        }
        else if (e.Key == Key.F12 && (e.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt)
        {
            if (BeginPeek()) e.Handled = true;
        }
        else if (e.Key == Key.OemPeriod && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            if (ShowCodeActions()) e.Handled = true;
        }
    }

    // Escape must close the menu wherever the keyboard happens to be, and the two places need two
    // subscriptions for one behaviour — not two behaviours. The menu lives in the window's OverlayLayer,
    // which is NOT a descendant of the editor, so a key pressed with the list focused never reaches the
    // editor at all (the list's own handler covers that). With focus still in the editor, a BUBBLE
    // handler is too late: AvaloniaEdit's TextArea marks Escape handled at the source, so it never
    // reaches an ancestor — the same trap as gotcha #224 (Tab), and the reason this is TUNNELLED.
    private void OnTunnelKey(object? sender, KeyEventArgs e)
    {
        if (_detached || _codeActionMenu is null) return;

        // While the menu is open it OWNS these keys — the same bargain the completion list makes. It
        // must tunnel: AvaloniaEdit's TextArea handles Escape and the arrows at the source, so a bubble
        // handler would never see them (the gotcha #224 trap).
        switch (e.Key)
        {
            case Key.Escape:
                CancelCodeActionMenu();
                e.Handled = true;
                break;
            case Key.Down:
                MoveCodeActionSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveCodeActionSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab:
                ApplySelectedCodeAction();
                e.Handled = true;
                break;
        }
    }

    // Wraps around: with two or three actions, stopping at the ends is just a keystroke that does
    // nothing.
    private void MoveCodeActionSelection(int delta)
    {
        if (_codeActionList is not { ItemCount: > 0 } list) return;
        int next = (list.SelectedIndex + delta + list.ItemCount) % list.ItemCount;
        list.SelectedIndex = next;
        list.ScrollIntoView(next);
    }

    // ── Code actions — Ctrl+. (Stage Q / Q2) ─────────────────────────────────────────────────────
    //
    // The light bulb (Q3) is a second TRIGGER for this same method, never a second implementation: both
    // build their list from one GetActionsAtCaret call and apply through one InvokeCodeAction.

    /// <summary>Opens the code-action menu at the caret, if anything is offered there. The public entry
    /// point for a THIRD trigger (the Diagnostics panel, Q5) — it goes through the same ShowCodeActions
    /// as Ctrl+. and the bulb, so a fix can never be reached by a path of its own.</summary>
    public bool TryShowCodeActions() => ShowCodeActions();

    /// <summary>Test seam: the actions offered at <paramref name="offset"/>, without the menu UI.</summary>
    internal IReadOnlyList<CodeAction> CodeActionsForTest(int offset) => GetActionsAtCaret(offset);

    /// <summary>Test seam: offers the actions at the caret and applies the one at
    /// <paramref name="index"/>, exercising the same path the menu does.</summary>
    internal bool InvokeCodeActionForTest(int offset, int index)
    {
        var actions = GetActionsAtCaret(offset);
        return index >= 0 && index < actions.Count && InvokeCodeAction(actions[index]);
    }

    // Every fix offered for every diagnostic covering the caret. The diagnostics are the language
    // service's CACHED list — this performs no analysis, exactly as the hover does not (§4).
    private IReadOnlyList<CodeAction> GetActionsAtCaret(int offset) => GetActionsAtCaret(offset, out _);

    /// <param name="anchorOffset">Where the FIRST offering diagnostic ends — i.e. the end of the flagged
    /// symbol itself. The bulb is placed there rather than at the end of the line: the user is looking at
    /// the problem, not at the right margin. Computed here, in the same pass that finds the actions, so
    /// the indicator and the menu can never describe different spans.</param>
    private IReadOnlyList<CodeAction> GetActionsAtCaret(int offset, out int anchorOffset)
    {
        anchorOffset = -1;
        var model = _model();
        if (model is null) return Array.Empty<CodeAction>();

        List<CodeAction>? actions = null;
        foreach (var d in _diagnostics())
        {
            if (offset < d.Start || offset > d.End) continue;
            foreach (var action in QuickFixEngine.GetFixes(model, d))
            {
                (actions ??= new List<CodeAction>()).Add(action);
                if (anchorOffset < 0) anchorOffset = d.End;
            }
        }
        return (IReadOnlyList<CodeAction>?)actions ?? Array.Empty<CodeAction>();
    }

    // THE single activation point (design §3 / CodeAction's own note): every surface that offers an
    // action ends up here, so the day a non-edit action exists, exactly one method learns about it.
    private bool InvokeCodeAction(CodeAction action)
    {
        if (_editor.IsReadOnly) return false; // never mutate a read-only surface (§0)
        if (!TextEditApplier.TryApply(_editor.Document, action.Edits, _editor.CaretOffset, out int caret))
        {
            return false;
        }
        _editor.CaretOffset = caret;
        _editor.TextArea.Focus();
        return true;
    }

    // Returns false when there is nothing to offer, leaving Ctrl+. unhandled — a shortcut that silently
    // does nothing is better than a menu that says "no actions here".
    private bool ShowCodeActions()
    {
        if (_editor.IsReadOnly) return false;
        var actions = GetActionsAtCaret(_editor.CaretOffset);
        if (actions.Count == 0) return false;

        var overlay = OverlayLayer.GetOverlayLayer(_editor);
        if (overlay is null) return false;

        CloseCodeActionMenu();

        // OverlayLayer, like the hover card — a bare Popup renders invisibly on the desktop despite
        // IsOpen/Visible/Opacity all being true (gotcha #209).
        // Styled as another editor popup, not a window of its own: the chrome below mirrors the
        // completion list's (elevated surface, 1px border) and the row metrics live in the shared
        // `code-action-menu` style so the two cannot drift apart.
        var list = new ListBox
        {
            ItemsSource = actions,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(CodeAction.Title)),
            SelectedIndex = 0,
            MinWidth = 180,
            MaxHeight = 180,
        };
        list.Classes.Add("code-action-menu");
        var card = new Border
        {
            Background = ThemeBrush("SurfaceRaisedBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Child = list,
        };

        // A single click runs the action: the ListBox has already moved the selection onto the pressed
        // item by the time this fires, so "click" and "Enter on the selection" are literally the same
        // operation — one selection model, three ways to reach it.
        list.PointerReleased += (_, e) =>
        {
            if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>() is not null) ApplySelectedCodeAction();
        };

        var anchor = EditorPopups.TryGetCaretRect(_editor, out var caretRect)
            ? _editor.TranslatePoint(new Point(caretRect.X, caretRect.Bottom), overlay)
            : new Point(0, 0);
        Canvas.SetLeft(card, anchor?.X ?? 0);
        Canvas.SetTop(card, anchor?.Y ?? 0);
        overlay.Children.Add(card);

        _codeActionMenu = card;
        _codeActionList = list;
        _codeActionOffset = _editor.CaretOffset;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_codeActionMenu, card)) return;
            EditorPopups.ClampIntoOverlay(overlay, card, flipOffset: caretRect.Height);
        }, DispatcherPriority.Background);

        // Focus deliberately STAYS in the editor, exactly as the completion window works: an
        // overlay-hosted list does not reliably take keyboard focus, and a menu whose arrow keys depend
        // on that is a menu that needs a mouse. OnTunnelKey drives it instead, so the keyboard behaves
        // the same wherever focus happens to be.
        return true;
    }

    // ── The light bulb (Q3) — discoverability ONLY ───────────────────────────────────────────────
    //
    // Ctrl+. ─┐
    //          ├── GetActionsAtCaret() ──► menu ──► InvokeCodeAction()
    // bulb   ─┘
    //
    // The bulb contributes nothing to that flow. It answers one question — "is there at least one
    // action here?" — with the same call the menu uses, and its click is literally ShowCodeActions.

    /// <summary>Re-evaluates the bulb. Called when the diagnostics have been recomputed — the moment a
    /// just-applied fix must stop being offered.</summary>
    public void RefreshCodeActionIndicator()
    {
        if (_detached) return;
        UpdateBulb();
    }

    /// <summary>Test seam: whether the bulb is currently offered (headless probe).</summary>
    internal bool IsCodeActionIndicatorVisible => _bulb is not null;

    /// <summary>Test seam: the armed action's index in the open menu, or -1.</summary>
    internal int CodeActionSelectionForTest => _codeActionList?.SelectedIndex ?? -1;

    /// <summary>Test seam: whether the code-action menu is open. Asserted directly rather than by
    /// counting overlay children — the bulb shares that overlay and moves in and out on its own.</summary>
    internal bool IsCodeActionMenuOpen => _codeActionMenu is not null;


    private void OnCaretMovedForBulb(object? sender, EventArgs e)
    {
        // An open menu describes the caret it was built for. If the caret moved, that context is gone —
        // close rather than let the user pick a fix for a position they have left.
        InvalidateCodeActionMenuIfMoved();
        UpdateBulb();
    }

    // Scrolling does not change WHETHER there are actions, only where the line is — so reposition
    // without re-evaluating.
    private void OnScrollForBulb(object? sender, EventArgs e)
    {
        if (_bulb is not null) PositionBulb(_bulbAnchor);
    }

    // The view's geometry just became valid/changed: place a bulb that could not be placed before, and
    // keep an existing one on its line.
    private void OnVisualLinesChangedForBulb(object? sender, EventArgs e)
    {
        if (_detached) return;
        if (_bulb is not null) { PositionBulb(_bulbAnchor); return; }
        UpdateBulb(); // a placement that failed earlier gets its retry here
    }

    private void UpdateBulb()
    {
        // Adding to / removing from the overlay changes layout, which can raise VisualLinesChanged
        // SYNCHRONOUSLY — and that handler calls back in here. Without this guard the hide/show pair
        // below interleaves with a nested update: both add a control, only the last is remembered, and
        // the first is stranded in the overlay forever (found by the Q3 placement test, which saw two
        // children where one was expected).
        if (_bulbUpdating) return;
        _bulbUpdating = true;
        try
        {
            UpdateBulbCore();
        }
        finally
        {
            _bulbUpdating = false;
        }
    }

    private void UpdateBulbCore()
    {
        if (_detached || _editor.IsReadOnly)
        {
            HideBulb();
            return;
        }

        var doc = _editor.Document;
        int anchor = -1;
        int actionCount = doc is null ? -1 : GetActionsAtCaret(_editor.CaretOffset, out anchor).Count;
        if (doc is null || actionCount == 0 || anchor < 0)
        {
            HideBulb();
            return;
        }

        // Pinned to the flagged SYMBOL. While the caret moves within the same flagged span the anchor is
        // unchanged, so the bulb sits perfectly still — re-showing it in place would read as a flicker.
        if (_bulb is not null && anchor == _bulbAnchor)
        {
            PositionBulb(anchor);
            return;
        }

        HideBulb();
        ShowBulb(anchor);
    }

    // Theme-scoped resources MUST be looked up with the theme variant. Control.FindResource(key) does
    // not supply one, so every brush in ThemeDictionaries comes back UNSET — which is how the bulb ended
    // up with a null Foreground: the control had a size, a position and IsEffectivelyVisible=true, and
    // painted nothing, because SvgIcon strokes its geometry with Foreground. Geometries are NOT
    // theme-scoped, which is why Data resolved and hid the problem. This is the same lookup
    // HoverInfoView uses; nothing in this file may use FindResource for a brush again.
    private IBrush? ThemeBrush(string key)
        => Application.Current?.Resources.TryGetResource(key, _editor.ActualThemeVariant, out var v) == true
           && v is IBrush b ? b : null;

    private void ShowBulb(int anchorOffset)
    {
        var overlay = OverlayLayer.GetOverlayLayer(_editor);
        if (overlay is null) return;

        // A FILLED path, not the stroked SvgIcon family: at this size an outline reads as an empty ring.
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Application.Current?.Resources.TryGetResource("Icon.LightbulbFilled", null, out var g) == true
                ? g as Geometry : null,
            Fill = ThemeBrush("CodeActionBrush"),
            Width = BulbIconSize,
            Height = BulbIconSize,
            Stretch = Stretch.Uniform,
        };
        var button = new Border
        {
            Child = icon,
            Background = Brushes.Transparent, // a hit target, not a painted chip
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(3),
            Cursor = HandCursor,
            Opacity = 1.0,
            [ToolTip.TipProperty] = UiStrings.CodeActionsTooltip,
        };
        // Amber-gold at rest so it reads as "a fix is available" at a glance (its own CodeActionBrush —
        // an offer, not a warning); the accent on hover says "and you can click me".
        button.PointerEntered += (_, _) => icon.Fill = ThemeBrush("AccentIconBrush");
        button.PointerExited += (_, _) => icon.Fill = ThemeBrush("CodeActionBrush");
        // The ONE flow: no separate retrieval, no separate invocation.
        button.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            ShowCodeActions();
        };

        // Position BEFORE committing: if the geometry is not available yet, nothing has been added to
        // the overlay and no state has been touched, so the next VisualLinesChanged simply tries again.
        if (!TryGetSymbolAnchor(anchorOffset, overlay, out var anchor)) return;

        Canvas.SetLeft(button, anchor.X);
        Canvas.SetTop(button, anchor.Y);
        overlay.Children.Add(button);
        _bulb = button;
        _bulbAnchor = anchorOffset;
    }

    private void PositionBulb(int anchorOffset)
    {
        if (_bulb is not { } bulb) return;
        var overlay = OverlayLayer.GetOverlayLayer(_editor);
        if (overlay is null) { HideBulb(); return; }

        // Geometry not ready is NOT the same as "the symbol is gone": keep the bulb and let
        // VisualLinesChanged reposition it once the view settles.
        if (!TryEnsureVisualLines()) return;
        if (!TryGetSymbolAnchor(anchorOffset, overlay, out var anchor))
        {
            HideBulb(); // lines are valid and this offset has no geometry ⇒ it scrolled out of view
            return;
        }

        Canvas.SetLeft(bulb, anchor.X);
        Canvas.SetTop(bulb, anchor.Y);
    }

    // Reading TextView.VisualLines while they are invalid THROWS (EditorPopups' rule, learned from the
    // double-click crash: "never access VisualLines while it's invalid"). A background RENDERER may skip
    // this — its Draw only runs when they are valid by construction, which is why the inline-values idiom
    // this placement was lifted from has no guard. The bulb positions from a timer tick and from
    // ModelUpdated, i.e. OUTSIDE the render pass, so the guarantee does not transfer and the guard is
    // mandatory. False ⇒ not computable right now; try again on the next VisualLinesChanged.
    private bool TryEnsureVisualLines()
    {
        var tv = _editor.TextArea.TextView;
        if (tv.VisualLinesValid) return true;
        try { tv.EnsureVisualLines(); }
        catch (InvalidOperationException)
        {
            return false; // a build is already running mid-Measure
        }
        return tv.VisualLinesValid;
    }

    // The anchor just past the end of the line's text — placement chosen because it provably never covers
    // code and never shifts the document (the left gutter is unavailable: every SQL surface shows line
    // numbers). False ⇒ the line has no geometry, i.e. it is not currently visible.
    // The anchor sits immediately after the flagged symbol — where the user is already looking, rather
    // than out at the right margin (the line-end placement was measurably correct and practically
    // useless: nobody looks there). Overlapping whatever follows the symbol is accepted: the bulb is
    // present only while the caret rests on the problem and goes the moment it moves.
    // False ⇒ this offset has no geometry right now, i.e. it is not visible.
    private bool TryGetSymbolAnchor(int offset, OverlayLayer overlay, out Point anchor)
    {
        anchor = default;
        var doc = _editor.Document;
        if (doc is null || offset < 0 || offset > doc.TextLength) return false;
        if (!TryEnsureVisualLines()) return false;

        var textView = _editor.TextArea.TextView;
        Point top, bottom;
        try
        {
            // Same idiom as EditorPopups.TryGetCaretRect: ask the view where a document position is, then
            // take out the scroll offset to get viewport coordinates.
            var position = new TextViewPosition(doc.GetLocation(offset));
            top = textView.GetVisualPosition(position, VisualYPosition.LineTop) - textView.ScrollOffset;
            bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom) - textView.ScrollOffset;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // Vertically centred on the line so it reads as sitting beside the word, not on top of it; and
        // kept inside the view, so a symbol near the right edge still shows its bulb.
        double x = Math.Min(top.X + BulbGap, Math.Max(0, textView.Bounds.Width - BulbSize));
        double y = top.Y + Math.Max(0, (bottom.Y - top.Y - BulbSize) / 2);
        if (y < -BulbSize || y > textView.Bounds.Height) return false; // scrolled out of view

        var point = textView.TranslatePoint(new Point(x, y), overlay);
        if (point is null) return false;
        anchor = point.Value;
        return true;
    }

    private void HideBulb()
    {
        if (_bulb is { } bulb)
        {
            // Remove from the panel that ACTUALLY holds it, not from whatever GetOverlayLayer resolves
            // to now: clearing the field while the control stayed parented is how one gets stranded in
            // the overlay with nothing left pointing at it.
            (bulb.Parent as Panel)?.Children.Remove(bulb);
            _bulb = null;
        }
        _bulbAnchor = -1;
    }

    private void ApplySelectedCodeAction()
    {
        var action = _codeActionList?.SelectedItem as CodeAction;
        CloseCodeActionMenu();          // close FIRST: applying moves focus back to the editor
        if (action is not null) InvokeCodeAction(action);
    }

    private void CloseCodeActionMenu()
    {
        if (_codeActionMenu is { } card)
        {
            // Same rule as HideBulb / HideHover: the panel that holds it, not the one the editor would resolve
            // to now — otherwise a tab switch strands the menu in the window's overlay for good.
            (card.Parent as Panel)?.Children.Remove(card);
            _codeActionMenu = null;
            _codeActionList = null;
            _codeActionOffset = -1;
        }
    }

    // Dismissal that returns the user to what they were doing: the editor keeps the keyboard, so they
    // can carry straight on typing. Used by Escape and by a click that lands back in the editor — NOT by
    // LostFocus, where focus has deliberately gone somewhere else and stealing it back would be wrong.
    private void CancelCodeActionMenu()
    {
        if (_codeActionMenu is null) return;
        CloseCodeActionMenu();
        _editor.TextArea.Focus();
    }

    // The open menu's actions were computed for one caret position. Once the caret leaves it, the menu is
    // describing a context the user has abandoned. (Applying a stale action could not corrupt anything —
    // TextEditApplier's drift check would refuse it — but offering it at all is the wrong behaviour.)
    private void InvalidateCodeActionMenuIfMoved()
    {
        if (_codeActionMenu is null) return;
        if (_editor.CaretOffset != _codeActionOffset) CloseCodeActionMenu();
    }

    // Opens the inline rename box iff the caret is on a safely-renameable local (the Navigation
    // Engine enforces this — a schema object / column / NEW-OLD / table-by-own-name yields null, so
    // a rename can never touch a database object). Returns false → not a renameable local; the key
    // is left unhandled.
    private bool BeginRename()
    {
        if (_editor.IsReadOnly) return false; // never edit a read-only surface (§0)
        var model = _model();
        if (model is null) return false;
        var rename = NavigationEngine.GetLocalRename(model, _editor.CaretOffset);
        if (rename is null) return false;

        ClosePeek();
        EnsureRenamePopup();
        _renameActive = rename;

        // Prefill from the identifier text as written at the caret (preserves the user's casing).
        var caretSpan = SpanAtCaret(rename, _editor.CaretOffset) ?? rename.Occurrences[0].Span;
        _renameCurrent = SafeGetText(caretSpan);
        _renameBox!.Text = _renameCurrent;
        SetRenameError(false);

        PlaceAtCaret(_renamePopup!);
        _renamePopup!.IsOpen = true;
        _renameBox.Focus();
        _renameBox.SelectAll();
        return true;
    }

    private void EnsureRenamePopup()
    {
        if (_renamePopup is not null) return;
        _renameBox = new TextBox
        {
            MinWidth = 160,
            FontFamily = MonoFont,
            Padding = new Thickness(4, 2),
        };
        _renameBox.KeyDown += OnRenameKey;
        _renameBox.LostFocus += (_, _) => CloseRename();
        _renameBox.TextChanged += (_, _) => SetRenameError(false);
        _renameBorder = new Border
        {
            Child = _renameBox,
            Background = Brush("SurfaceRaisedBrush"),
            BorderBrush = Brush("FocusBorderBrush") ?? Brush("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2),
        };
        _renamePopup = new Popup
        {
            PlacementTarget = _editor,
            IsLightDismissEnabled = true,
            Child = _renameBorder,
        };
        ((ISetLogicalParent)_renamePopup).SetParent(_editor);
    }

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRename(); e.Handled = true; }
    }

    private void CommitRename()
    {
        var rename = _renameActive;
        var box = _renameBox;
        if (rename is null || box is null) { CloseRename(); return; }

        var newName = (box.Text ?? string.Empty).Trim();
        // No change → just close.
        if (string.Equals(FoldIdentifier(newName), rename.CurrentName, StringComparison.Ordinal))
        {
            CloseRename();
            return;
        }
        // User error (invalid name / keyword / collision) → keep the box open, flag it.
        if (!IsValidNewName(newName, rename))
        {
            SetRenameError(true);
            return;
        }
        // §0: apply only if every occurrence still reads as the identifier we resolved; a drift
        // between the model and the document aborts the whole rename (never a partial/wrong edit).
        TryApplyRename(rename, newName);
        CloseRename();
    }

    // Replaces every occurrence with the new name in ONE undo group, last-to-first so offsets stay
    // valid. Verifies each span still reads as the original identifier first — if the document has
    // drifted from the model, it aborts without editing anything (§0). Returns whether it applied.
    // Rename is a CodeAction like any other: a set of edits applied atomically. It owns the decision of
    // WHAT to replace (the binder's exact occurrences of one local symbol) and hands it to the one
    // applier, which owns bounds-checking, drift control, ordering and the undo unit. There is no
    // second mutation path here — the previous hand-rolled verify/sort/BeginUpdate loop is gone
    // (editor-quick-fixes.md §2.2). ExpectedOldText is the text the BINDER saw at each occurrence, not
    // text re-read from the document, so the check compares against the model's belief rather than
    // against itself — and it is per-occurrence, so mixed casing needs no folding rule here.
    private bool TryApplyRename(NavigationRename rename, string newName)
    {
        var edits = new List<TextEdit>(rename.Occurrences.Count);
        foreach (var o in rename.Occurrences)
        {
            edits.Add(new TextEdit(o.Span.Start, o.Span.Length, newName, o.Text));
        }

        if (!TextEditApplier.TryApply(_editor.Document, edits, _editor.CaretOffset, out int caret)) return false;
        _editor.CaretOffset = caret;
        return true;
    }

    // A new name is safe when it is a plain (unquoted) identifier, is not a reserved keyword, and
    // does not collide with another local in scope (which would introduce shadowing/ambiguity).
    private bool IsValidNewName(string name, NavigationRename rename)
    {
        if (!IsPlainIdentifier(name)) return false;
        if (FirebirdSyntax.IsKeyword(name.ToUpperInvariant())) return false;

        var model = _model();
        if (model is not null)
        {
            foreach (var sym in model.SymbolsInScope(_editor.CaretOffset))
            {
                if (ReferenceEquals(sym, rename.Symbol)) continue;
                if (string.Equals(sym.Name, name, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        return true;
    }

    private void SetRenameError(bool error)
    {
        if (_renameBorder is null) return;
        _renameBorder.BorderBrush = error
            ? Brush("ErrorBrush")
            : Brush("FocusBorderBrush") ?? Brush("AccentBrush");
    }

    private void CloseRename()
    {
        _renameActive = null;
        if (_renamePopup is { IsOpen: true } p)
        {
            p.IsOpen = false;
            _editor.Focus();
        }
    }

    // ── Peek definition — Alt+F12 (M5, §10) ──────────────────────────────────────────────────────

    // Shows the definition inline without opening a tab: a local's declaration text (from the
    // document, no DB), or a schema object's DDL/source fetched read-only. Returns false → nothing
    // to peek.
    private bool BeginPeek()
    {
        CloseRename();
        var model = _model();
        int caret = _editor.CaretOffset;
        var target = model is null ? null : NavigationEngine.TargetAt(model, caret);

        if (target is { Kind: NavigationTargetKind.LocalDefinition, DefinitionSpan: { } def })
        {
            ShowPeek(string.Format(CultureInfo.CurrentCulture, UiStrings.PeekHeaderFormat, KindLabel(target.Symbol.Kind), target.Symbol.Name), DeclarationText(def), monospace: true);
            return true;
        }

        if (target is { Kind: NavigationTargetKind.SchemaObject, ObjectName.Length: > 0 })
        {
            StartSchemaPeek(target.ObjectName!, MapKind(target.ObjectKind));
            return true;
        }

        // The model couldn't resolve it (e.g. a body-only editor) — best-effort by name.
        if (_fetchDefinition is not null)
        {
            var word = SqlCompletionContext.GetWordAt(_editor.Text ?? string.Empty, caret).Text;
            if (!string.IsNullOrEmpty(word))
            {
                StartSchemaPeek(word, MetadataObjectKind.Table);
                return true;
            }
        }
        return false;
    }

    private void StartSchemaPeek(string name, MetadataObjectKind kind)
    {
        if (_fetchDefinition is null) return;
        int gen = ++_peekGeneration;
        ShowPeek(name, UiStrings.PeekLoading, monospace: false);
        _ = FetchAndShowPeekAsync(name, kind, gen);
    }

    private async Task FetchAndShowPeekAsync(string name, MetadataObjectKind kind, int gen)
    {
        string? ddl;
        try { ddl = await _fetchDefinition!(name, kind).ConfigureAwait(true); }
        catch { ddl = null; }
        if (_detached || gen != _peekGeneration) return; // superseded or closed while fetching
        if (string.IsNullOrEmpty(ddl)) { ClosePeek(); return; }
        ShowPeek(name, ddl!, monospace: true);
    }

    private void ShowPeek(string header, string body, bool monospace)
    {
        EnsurePeekPopup();
        _peekPopup!.Child = BuildPeekCard(header, body, monospace);
        PlaceAtCaret(_peekPopup);
        _peekPopup.IsOpen = false; // toggle so the placement rect is re-read
        _peekPopup.IsOpen = true;
    }

    private void EnsurePeekPopup()
    {
        if (_peekPopup is not null) return;
        _peekPopup = new Popup { PlacementTarget = _editor, IsLightDismissEnabled = true };
        ((ISetLogicalParent)_peekPopup).SetParent(_editor);
    }

    private Control BuildPeekCard(string header, string body, bool monospace)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = header,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Foreground = Brush("SubtleForegroundBrush"),
        });

        var text = new TextBox
        {
            Text = body,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = monospace ? MonoFont : FontFamily.Default,
            MaxWidth = 640,
            MaxHeight = 320,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        panel.Children.Add(text);

        var border = new Border
        {
            Child = panel,
            Background = Brush("SurfaceRaisedBrush") ?? Brush("BackgroundBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            MaxWidth = 660,
            MaxHeight = 360,
        };
        // Escape closes the peek even when focus is inside the read-only text.
        border.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { ClosePeek(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        return border;
    }

    private void ClosePeek()
    {
        _peekGeneration++;
        if (_peekPopup is { IsOpen: true } p) p.IsOpen = false;
    }

    // The document text of the whole line(s) spanned by a local declaration — the "peek" body.
    private string DeclarationText(TextSpan def)
    {
        var doc = _editor.Document;
        if (doc is null) return string.Empty;
        int start = Math.Clamp(def.Start, 0, doc.TextLength);
        int end = Math.Clamp(def.End, start, doc.TextLength);
        var from = doc.GetLineByOffset(start).Offset;
        var to = doc.GetLineByOffset(end).EndOffset;
        return doc.GetText(from, to - from).Trim();
    }

    // ── Double-click: unified Parameter Helper (design §28) or name-based open ────────────────────

    // One double-click handler for the editor. When the click lands at a parameter site (an INSERT /
    // UPDATE-OR-INSERT value, an EXECUTE PROCEDURE / function argument), show the unified Parameter
    // Helper (owned by the completion controller). Otherwise fall back to the name-based open (open the
    // object whose name was double-clicked — IBExpert compatibility, §10). §0: read-only.
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_detached) return;
        // Derive the offset from the POINTER position, not _editor.CaretOffset: on a real double-tap the
        // caret is not reliably placed at the clicked value yet when this fires. Fall back to the caret
        // only when the pointer isn't over text. Same decision as the test seam otherwise.
        int offset = OffsetAt(e.GetPosition(_editor)) ?? _editor.CaretOffset;
        if (TryHandleDoubleClick(offset)) e.Handled = true;
    }

    /// <summary>The double-click decision at <paramref name="offset"/> (test seam — driven headlessly
    /// without synthesising a pointer gesture). Shows the unified Parameter Helper at a parameter site,
    /// else opens the double-clicked object by name. Returns whether it handled the click.</summary>
    internal bool TryHandleDoubleClick(int offset)
    {
        if (_showParameterHelper is not null && _showParameterHelper(offset)) return true;
        var word = SqlCompletionContext.GetWordAt(_editor.Text ?? string.Empty, offset).Text;
        return !string.IsNullOrEmpty(word) && _openByName(word);
    }

    // ── Shared popup helpers ─────────────────────────────────────────────────────────────────────

    // Anchors a popup just below the caret (shared with the completion controller's Quick Info popup).
    private void PlaceAtCaret(Popup popup) => EditorPopups.PlaceAtCaret(_editor, popup);

    private IBrush? Brush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b) return b;
        return null;
    }

    private string SafeGetText(TextSpan span)
    {
        var doc = _editor.Document;
        if (doc is null || span.Start < 0 || span.End > doc.TextLength) return string.Empty;
        return doc.GetText(span.Start, span.Length);
    }

    private static TextSpan? SpanAtCaret(NavigationRename rename, int caret)
    {
        foreach (var o in rename.Occurrences)
        {
            if (caret >= o.Span.Start && caret <= o.Span.End) return o.Span;
        }
        return null;
    }

    // Mirrors the binder's identifier folding: quoted → literal case (quotes stripped, "" unescaped);
    // unquoted → upper-case (Firebird catalog convention). Used to compare occurrence text to the
    // resolved symbol name in the §0 drift guard.
    private static string FoldIdentifier(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            return raw.Substring(1, raw.Length - 2).Replace("\"\"", "\"");
        }
        return raw.ToUpperInvariant();
    }

    private static bool IsPlainIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (var ch in s)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '$')) return false;
        }
        return true;
    }

    private static string KindLabel(SymbolKind kind) => kind switch
    {
        SymbolKind.Variable => UiStrings.ObjectKindVariable,
        SymbolKind.Parameter => UiStrings.ObjectKindParameter,
        SymbolKind.Cursor => UiStrings.ObjectKindCursor,
        SymbolKind.Cte => UiStrings.ObjectKindCte,
        SymbolKind.TableReference => UiStrings.ObjectKindAlias,
        SymbolKind.RecordAlias => UiStrings.ObjectKindRecord,
        _ => UiStrings.ObjectKindDefinition,
    };

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    // Maps a pointer position (editor coordinates) to a document offset, or null when the point is
    // not over text (empty space / past the last character).
    private int? OffsetAt(Point pointerInEditor)
    {
        var doc = _editor.Document;
        if (doc is null) return null;
        var tvp = _editor.GetPositionFromPoint(pointerInEditor);
        if (tvp is null) return null;
        return doc.GetOffset(tvp.Value.Location);
    }

    private static MetadataObjectKind MapKind(SymbolKind kind) => kind switch
    {
        SymbolKind.Table => MetadataObjectKind.Table,
        SymbolKind.View => MetadataObjectKind.View,
        SymbolKind.SystemTable => MetadataObjectKind.SystemTable,
        SymbolKind.Procedure => MetadataObjectKind.Procedure,
        SymbolKind.Function => MetadataObjectKind.Function,
        SymbolKind.Trigger => MetadataObjectKind.Trigger,
        SymbolKind.Domain => MetadataObjectKind.Domain,
        SymbolKind.Exception => MetadataObjectKind.Exception,
        SymbolKind.Sequence => MetadataObjectKind.Generator,
        SymbolKind.Role => MetadataObjectKind.Role,
        SymbolKind.Package => MetadataObjectKind.Package,
        SymbolKind.Index => MetadataObjectKind.Index,
        // A safe default; the VM prefers the authoritative kind from loaded metadata anyway.
        _ => MetadataObjectKind.Table,
    };

    /// <summary>Draws a 1px accent underline beneath the active (Ctrl-hovered) identifier.</summary>
    private sealed class UnderlineRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        public ISegment? Segment;

        public UnderlineRenderer(TextEditor editor) => _editor = editor;

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var seg = Segment;
            if (seg is null || textView.VisualLines.Count == 0) return;
            var brush = ResolveBrush("AccentBrush");
            if (brush is null) return;
            var pen = new Pen(brush, 1);
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
            {
                double y = r.Bottom - 0.5;
                drawingContext.DrawLine(pen, new Point(r.Left, y), new Point(r.Right, y));
            }
        }

        private IBrush? ResolveBrush(string key)
        {
            var theme = _editor.ActualThemeVariant;
            if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
            {
                return b;
            }
            return null;
        }
    }
}
