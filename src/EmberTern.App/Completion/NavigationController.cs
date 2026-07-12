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
using EmberTern.Core.Sql.Language.Navigation;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;

namespace EmberTern.App.Completion;

/// <summary>
/// The navigation UX layer (Etap 6 / M4, design §9.4 / §10). Gives an AvaloniaEdit
/// <see cref="TextEditor"/> the modern "Ctrl to navigate" affordance:
/// <list type="bullet">
///   <item><b>Ctrl + hover</b> over a navigable identifier → underline it, switch the cursor to a
///   hand, and pop a Quick Info tooltip (kind + facts + members) — so the user instantly sees the
///   element leads somewhere and can check it without opening its definition.</item>
///   <item><b>Ctrl + click</b> → go to definition via the <see cref="NavigationEngine"/> (a schema
///   object opens its detail/DDL tab; a local — alias/variable/parameter/cursor/CTE — jumps the
///   caret to its declaration in the editor).</item>
/// </list>
/// A thin App glue over the pure Core engines (<see cref="NavigationEngine"/>,
/// <see cref="QuickInfoEngine"/>): it maps a pointer position to a document offset, asks the engines,
/// and paints/opens. It reads the per-editor cached <see cref="SemanticModel"/> (shared with the
/// completion controller + semantic highlighter — one background parse per editor); it never
/// re-parses on the pointer path. Read-only, so §0 (never lose information) holds by construction.
/// <para>
/// The affordance is driven by real resolution, not a name search — so the underline appears exactly
/// where Ctrl+Click will navigate. When the model can't resolve (e.g. a body-only Easy-mode editor
/// whose CREATE header isn't in the text), Ctrl+Click falls back to the name-based open
/// (<see cref="_openByName"/>); the hover affordance stays semantic (no underline there).
/// </para>
/// </summary>
internal sealed class NavigationController
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly TextEditor _editor;
    private readonly Func<SemanticModel?> _model;
    private readonly Func<string, MetadataObjectKind, bool> _openSchemaObject;
    private readonly Func<string, bool> _openByName;
    // Async fetch of a schema object's DDL/source text for Peek Definition (M5), or null → Peek only
    // works for locals (declaration text from the document, no DB).
    private readonly Func<string, MetadataObjectKind, Task<string?>>? _fetchDefinition;
    private readonly UnderlineRenderer _underline;
    private readonly ReferenceHighlightRenderer _references;
    private readonly Popup _tooltip;

    private TextSpan? _activeSpan;   // the currently-underlined identifier, or null when clear
    private Point _lastPointer;
    private bool _pointerInside;
    private bool _detached;

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

    private NavigationController(
        TextEditor editor,
        Func<SemanticModel?> model,
        Func<string, MetadataObjectKind, bool> openSchemaObject,
        Func<string, bool> openByName,
        Func<string, MetadataObjectKind, Task<string?>>? fetchDefinition,
        Func<int, bool>? showParameterHelper)
    {
        _editor = editor;
        _model = model;
        _openSchemaObject = openSchemaObject;
        _openByName = openByName;
        _fetchDefinition = fetchDefinition;
        _showParameterHelper = showParameterHelper;
        _underline = new UnderlineRenderer(editor);
        _references = new ReferenceHighlightRenderer(editor, LocalReferenceSpans);

        // A self-managed, focus-neutral hover tooltip. Hit-test-invisible content so it never steals
        // the pointer (which would fire PointerExited on the editor and flicker the popup shut).
        _tooltip = new Popup
        {
            PlacementTarget = editor,
            Placement = PlacementMode.Pointer,
            IsLightDismissEnabled = false,
            VerticalOffset = 16,
        };
        ((ISetLogicalParent)_tooltip).SetParent(editor);
    }

    /// <summary>Attaches navigation to <paramref name="editor"/>.</summary>
    /// <param name="model">The editor's cached semantic model (from the completion controller).</param>
    /// <param name="openSchemaObject">Opens a resolved schema object (name + kind) — the VM.</param>
    /// <param name="openByName">Name-based open fallback for editors the model can't fully resolve.</param>
    /// <param name="fetchDefinition">Async fetch of a schema object's DDL/source for Peek Definition
    /// (M5); null → Peek shows only local declarations.</param>
    /// <param name="showParameterHelper">Shows the unified Parameter Helper at a double-clicked offset
    /// (returns whether it is a parameter site); null → double-click only does name-based open.</param>
    public static NavigationController Attach(
        TextEditor editor,
        Func<SemanticModel?> model,
        Func<string, MetadataObjectKind, bool> openSchemaObject,
        Func<string, bool> openByName,
        Func<string, MetadataObjectKind, Task<string?>>? fetchDefinition = null,
        Func<int, bool>? showParameterHelper = null)
    {
        var c = new NavigationController(editor, model, openSchemaObject, openByName, fetchDefinition, showParameterHelper);
        editor.TextArea.TextView.BackgroundRenderers.Add(c._underline);
        editor.TextArea.TextView.BackgroundRenderers.Add(c._references);

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
        // F2 (rename) / Alt+F12 (peek) — the M5 commands.
        editor.KeyDown += c.OnCommandKey;
        // Repaint the local-reference highlight as the caret moves onto/off a local symbol.
        editor.TextArea.Caret.PositionChanged += c.OnCaretMoved;
        // Double-click → INSERT/VALUES column helper (P6) or name-based open. Consolidated here so
        // there is ONE double-click handler (the two duplicated ones in SqlEditorBehavior / MainWindow
        // move here), avoiding an e.Handled ordering dance between two subscribers on one event.
        editor.DoubleTapped += c.OnDoubleTapped;
        return c;
    }

    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_underline);
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_references);
        _editor.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _editor.TextArea.TextView.PointerMoved -= OnPointerMoved;
        _editor.TextArea.TextView.PointerExited -= OnPointerExited;
        _editor.KeyDown -= OnKeyChanged;
        _editor.KeyUp -= OnKeyChanged;
        _editor.KeyDown -= OnCommandKey;
        _editor.TextArea.Caret.PositionChanged -= OnCaretMoved;
        _editor.DoubleTapped -= OnDoubleTapped;
        _tooltip.IsOpen = false;
        _peekGeneration++;
        if (_renamePopup is not null) _renamePopup.IsOpen = false;
        if (_peekPopup is not null) _peekPopup.IsOpen = false;
    }

    // ── Hover affordance ─────────────────────────────────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _pointerInside = true;
        _lastPointer = e.GetPosition(_editor);
        UpdateHover(e.KeyModifiers);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _pointerInside = false;
        Clear();
    }

    private void OnKeyChanged(object? sender, KeyEventArgs e)
    {
        if (_pointerInside) UpdateHover(e.KeyModifiers);
    }

    private void UpdateHover(KeyModifiers modifiers)
    {
        if (_detached) return;
        bool ctrl = (modifiers & KeyModifiers.Control) == KeyModifiers.Control;
        if (!ctrl || !_pointerInside) { Clear(); return; }

        int? offset = OffsetAt(_lastPointer);
        var model = _model();
        var target = offset is { } o && model is not null ? NavigationEngine.TargetAt(model, o) : null;
        if (target is null) { Clear(); return; }

        var span = target.ReferenceSpan;
        if (_activeSpan is { } a && a.Start == span.Start && a.Length == span.Length)
        {
            return; // same identifier — already lit; don't rebuild/reposition the tooltip
        }

        _activeSpan = span;
        _underline.Segment = new TextSegment { StartOffset = span.Start, Length = span.Length };
        _editor.TextArea.TextView.InvalidateVisual();
        _editor.TextArea.TextView.Cursor = HandCursor;
        ShowTooltip(QuickInfoEngine.GetQuickInfo(model!, offset!.Value));
    }

    private void Clear()
    {
        if (_activeSpan is null) return; // already clear — avoid redundant invalidations on plain moves
        _activeSpan = null;
        _underline.Segment = null;
        _editor.TextArea.TextView.InvalidateVisual();
        _editor.TextArea.TextView.Cursor = null; // fall back to the editor's I-beam
        _tooltip.IsOpen = false;
    }

    private void ShowTooltip(QuickInfo? info)
    {
        if (info is null) { _tooltip.IsOpen = false; return; }
        var card = QuickInfoView.Build(info, _editor.ActualThemeVariant);
        card.IsHitTestVisible = false; // never intercept the pointer
        // Toggle off→on so PlacementMode.Pointer re-reads the current pointer position.
        _tooltip.IsOpen = false;
        _tooltip.Child = card;
        _tooltip.IsOpen = true;
    }

    // ── Ctrl+Click → navigate ──────────────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
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
        => ComputeLocalReferenceSpans(_model(), offset);

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

    // ── Local find references — box every occurrence of the LOCAL under the caret (M5) ───────────

    private int _refCaret = -1;
    private SemanticModel? _refModel;
    private IReadOnlyList<TextSpan> _refSpans = Array.Empty<TextSpan>();

    private void OnCaretMoved(object? sender, EventArgs e)
    {
        if (_detached) return;
        _editor.TextArea.TextView.InvalidateVisual(); // re-run the reference-highlight renderer
        // (The Parameter Helper's own context-driven lifetime is handled by its own caret subscription.)
    }

    // Cached by (caret, model) so a scroll/repaint is cheap; recomputed only when the caret moves or
    // the model rebuilds. Schema objects and columns are deliberately excluded (highlighting every
    // occurrence of a table/column would be noise) — only script-local names light up.
    private IReadOnlyList<TextSpan> LocalReferenceSpans()
    {
        var model = _model();
        int caret = _editor.CaretOffset;
        if (caret == _refCaret && ReferenceEquals(model, _refModel)) return _refSpans;
        _refCaret = caret;
        _refModel = model;
        _refSpans = ComputeLocalReferenceSpans(model, caret);
        return _refSpans;
    }

    private static IReadOnlyList<TextSpan> ComputeLocalReferenceSpans(SemanticModel? model, int caret)
    {
        if (model is null) return Array.Empty<TextSpan>();
        var symbol = model.ReferenceAt(caret)?.Symbol;
        if (symbol is null || !IsLocalHighlightSymbol(symbol)) return Array.Empty<TextSpan>();
        return NavigationEngine.LocalReferences(model, caret);
    }

    private static bool IsLocalHighlightSymbol(Symbol symbol) => symbol
        is TableReferenceSymbol or VariableSymbol or ParameterSymbol
        or CursorSymbol or CteSymbol or RecordAliasSymbol;

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
        var caretSpan = SpanAtCaret(rename, _editor.CaretOffset) ?? rename.Occurrences[0];
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
            Background = Brush("ElevatedPanelBrush"),
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
    private bool TryApplyRename(NavigationRename rename, string newName)
    {
        var doc = _editor.Document;
        if (doc is null) return false;

        foreach (var s in rename.Occurrences)
        {
            if (s.Start < 0 || s.End > doc.TextLength) return false;
            if (!string.Equals(FoldIdentifier(doc.GetText(s.Start, s.Length)), rename.CurrentName, StringComparison.Ordinal))
            {
                return false; // drift → abort, never corrupt
            }
        }

        var spans = new List<TextSpan>(rename.Occurrences);
        spans.Sort(static (a, b) => b.Start.CompareTo(a.Start));
        doc.BeginUpdate();
        try
        {
            foreach (var s in spans) doc.Replace(s.Start, s.Length, newName);
        }
        finally
        {
            doc.EndUpdate();
        }
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
            ShowPeek($"{KindLabel(target.Symbol.Kind)} — {target.Symbol.Name}", DeclarationText(def), monospace: true);
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
        ShowPeek(name, "Loading…", monospace: false);
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
            Background = Brush("ElevatedPanelBrush") ?? Brush("BackgroundBrush"),
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
        foreach (var s in rename.Occurrences)
        {
            if (caret >= s.Start && caret <= s.End) return s;
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
        SymbolKind.Variable => "Variable",
        SymbolKind.Parameter => "Parameter",
        SymbolKind.Cursor => "Cursor",
        SymbolKind.Cte => "Common table expression",
        SymbolKind.TableReference => "Alias",
        SymbolKind.RecordAlias => "Record",
        _ => "Definition",
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

    /// <summary>Boxes every occurrence of the LOCAL symbol under the caret — find local references
    /// (M5). Reuses the calm <c>OccurrenceHighlightBrush</c> so it reads consistently with the
    /// select-a-word occurrence boxes. Fill-only (no outline) to stay subtle. A lone occurrence is
    /// not boxed. Read-only paint — §0 holds by construction.</summary>
    private sealed class ReferenceHighlightRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private readonly Func<IReadOnlyList<TextSpan>> _spans;

        public ReferenceHighlightRenderer(TextEditor editor, Func<IReadOnlyList<TextSpan>> spans)
        {
            _editor = editor;
            _spans = spans;
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var spans = _spans();
            if (spans.Count < 2 || textView.VisualLines.Count == 0) return;

            var fill = ResolveBrush("OccurrenceHighlightBrush");
            if (fill is null) return;

            foreach (var span in spans)
            {
                if (span.Length == 0) continue;
                var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
                builder.AddSegment(textView, new TextSegment { StartOffset = span.Start, Length = span.Length });
                var geo = builder.CreateGeometry();
                if (geo is not null) drawingContext.DrawGeometry(fill, null, geo);
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
