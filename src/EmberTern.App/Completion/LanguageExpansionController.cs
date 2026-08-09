using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using EmberTern.Core.Sql.Language.Constructs;

namespace EmberTern.App.Completion;

/// <summary>
/// <b>Language Completion</b> (design: <c>docs/design/editor-language-expansion.md</c>) — finishes a daily
/// Firebird construct the developer already started typing (<c>if</c>→<c>if (▌) then</c>,
/// <c>gro</c>→<c>group by ▌</c>). Deliberately a <b>thin, stateless</b> consumer of the Core resolver:
/// every decision comes from <see cref="LanguageConstructResolver.Resolve"/>, which is re-evaluated from
/// the current (text, caret) on each caret move and on Tab — nothing about the armed construct is
/// remembered. The only field is the presentation card in the overlay.
/// <para>Interaction contract: a passive <see cref="OverlayLayer"/> hint (never focus, never hit-tested,
/// below the caret line so it never covers the caret, clamped into the viewport, theme-tokened) shows
/// exactly what Tab will insert; <b>Tab</b> expands the armed construct (else falls through to normal
/// indent); the hint hides the instant nothing is armed, and never competes with the completion list.</para>
/// <para><b>The hint can never lie.</b> <see cref="CurrentEdit"/> returns the very
/// <see cref="ExpansionEdit"/> that Tab applies, and the hint renders that edit's insert text — preview
/// and result are one object, so casing (or any future per-site decision) cannot drift between them.</para>
/// </summary>
internal sealed class LanguageExpansionController
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TextEditor _editor;
    private readonly Func<bool> _isCompletionActive;
    private Border? _card;        // the hint currently in the overlay (presentation only)
    private TextBlock? _label;    // its expansion text, updated in place to avoid flicker
    private string? _shownText;   // what the label currently shows (skip needless rebuilds)

    // The ONE piece of interaction state: the caret offset at which Escape dismissed the hint, cleared as
    // soon as the caret moves. The controller stays stateless about WHAT is armed (that is always re-derived
    // from (text, caret)); this remembers only that the user said "not here", which no pure function of the
    // text can know. Without it, Escape would hide the card while Tab still expanded — a hidden special
    // action, which is precisely what the obviousness principle forbids (design §7).
    private int? _dismissedAt;

    private LanguageExpansionController(TextEditor editor, Func<bool> isCompletionActive)
    {
        _editor = editor;
        _isCompletionActive = isCompletionActive;
    }

    /// <summary>Attaches to an editor. Shares the completion controller only to know when the completion
    /// list is up (so the two never compete). Called from the single shared wiring seam
    /// <see cref="SqlEditorBehavior.Attach"/> (D3 consolidated the former two seams; gotcha #219).</summary>
    public static void Attach(TextEditor editor, SqlCompletionController completion)
    {
        var c = new LanguageExpansionController(editor, () => completion.IsPopupOpen);
        // Every subscription below only says "re-evaluate now" — none of them decides anything. The whole
        // arming decision lives in CurrentEdit, so a trigger can never disagree with the Tab handler.
        // Caret moves cover typing (a keystroke always moves the caret); the rest cover the ways the answer
        // changes WITHOUT a caret move.
        editor.TextArea.Caret.PositionChanged += (_, _) => c.OnCaretMoved();
        // A selection can appear or clear without moving the caret, and it flips arming (Tab belongs to
        // block-indent whenever text is selected).
        editor.TextArea.SelectionChanged += (_, _) => c.UpdateHint();
        // The hint must never float over another control once the editor no longer owns the caret.
        editor.TextArea.LostFocus += (_, _) => c.HideHint();
        editor.TextArea.GotFocus += (_, _) => c.UpdateHint();
        // Tunnel so we preempt AvaloniaEdit's Tab-indent when a construct is armed.
        editor.AddHandler(InputElement.KeyDownEvent, c.OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The edit Tab would apply right now, or null when nothing is armed — the single decision point, so
    /// the hint and Tab can never disagree. Derived fresh from (text, caret) every call; the only thing
    /// remembered is <see cref="_dismissedAt"/>.
    /// </summary>
    private ExpansionEdit? CurrentEdit()
    {
        var doc = _editor.Document;
        if (doc is null) return null;
        // Not ours unless the editor holds the caret — otherwise a hint would hang over whatever the user
        // moved to. A pull-guard, so a missed LostFocus event can't leave a stale hint armed.
        if (!_editor.TextArea.IsKeyboardFocusWithin) return null;
        // A selection means Tab is (block) indent, always — Language Completion must never replace a
        // selection the user is about to indent.
        if (_editor.SelectionLength > 0) return null;
        if (_isCompletionActive()) return null;   // the list owns Tab (accept the item)

        int caret = _editor.CaretOffset;
        if (_dismissedAt == caret) return null;   // Escape said "not here"; Tab is a normal indent again

        var text = doc.Text;
        var match = LanguageConstructResolver.Resolve(text, caret);
        if (match is null) return null;
        return ConstructExpansion.For(text, caret, match);
    }

    private void OnCaretMoved()
    {
        // Moving the caret retires the Escape dismissal: it applied to that one position, not to the editor.
        if (_dismissedAt is { } d && d != _editor.CaretOffset) _dismissedAt = null;
        UpdateHint();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                var edit = CurrentEdit();
                if (edit is not null) { Expand(edit); e.Handled = true; }
                break;
            case Key.Escape:
                // Dismiss only what is actually on screen. If no hint is up, Escape belongs to whoever else
                // wants it (the completion list) and must not arm a dismissal the user never asked for.
                // Never consume it either way — passive dismiss.
                if (_card is not null) { _dismissedAt = _editor.CaretOffset; HideHint(); }
                break;
            case Key.Space when (e.KeyModifiers & KeyModifiers.Control) != 0:
                HideHint();       // Ctrl+Space opens the list; don't let a hint linger under it
                break;
        }
    }

    private void Expand(ExpansionEdit edit)
    {
        var doc = _editor.Document;
        if (doc is null) return;
        if (edit.Start < 0 || edit.Start + edit.Length > doc.TextLength) return; // stale caret guard
        doc.Replace(edit.Start, edit.Length, edit.InsertText);
        _editor.CaretOffset = edit.Start + edit.CaretOffset;
        HideHint();
    }

    // ── Presentation (OverlayLayer card) ──────────────────────────────────────────────────────

    private void UpdateHint()
    {
        var edit = CurrentEdit();
        if (edit is null) { HideHint(); return; }
        // The edit's OWN text — cased exactly as Tab will insert it (IF → "IF () THEN"), never the
        // catalog's lowercase spelling. Preview and result are the same value by construction.
        ShowHint(edit.InsertText);
    }

    private void ShowHint(string expansion)
    {
        var overlay = OverlayLayer.GetOverlayLayer(_editor);
        if (overlay is null) return;

        if (_card is null)
        {
            _label = new TextBlock
            {
                FontFamily = MonoFont,
                // Scale with the editor so the preview reads like the code being typed, at any font size.
                FontSize = Math.Max(11, _editor.FontSize),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("ForegroundBrush"),
            };
            var tab = new TextBlock
            {
                Text = "⇥",   // ⇥ — communicates "press Tab", without words
                FontSize = Math.Max(11, _editor.FontSize),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("AccentBrush") ?? Brush("SubtleForegroundBrush"),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(tab);
            row.Children.Add(_label);
            _card = new Border
            {
                Child = row,
                Background = Brush("SurfaceRaisedBrush") ?? Brush("PanelBrush") ?? Brush("BackgroundBrush"),
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3),
                IsHitTestVisible = false,   // passive: never intercept the pointer
                Opacity = 0.96,
            };
            overlay.Children.Add(_card);
            _shownText = null;
        }

        if (!string.Equals(_shownText, expansion, StringComparison.Ordinal))
        {
            _label!.Text = expansion;
            _shownText = expansion;
        }
        Reposition(overlay, _card!);
    }

    // Anchors the card just below the caret line (so it never covers the caret), then clamps it into the
    // viewport after layout. Falls back to hiding when the caret rect can't be computed.
    private void Reposition(OverlayLayer overlay, Border card)
    {
        if (!EditorPopups.TryGetCaretRect(_editor, out var rect)) { HideHint(); return; }
        var p = _editor.TranslatePoint(new Point(rect.X, rect.Bottom), overlay);
        if (p is not { } pt) { HideHint(); return; }
        Canvas.SetLeft(card, pt.X);
        Canvas.SetTop(card, pt.Y + 2);
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_card, card)) return;
            EditorPopups.ClampIntoOverlay(overlay, card, flipOffset: 18);
        }, DispatcherPriority.Background);
    }

    private void HideHint()
    {
        if (_card is { } card)
        {
            // ⚠ The panel that HOLDS it — see NavigationController.HideHover for the mechanism and the report
            // it came from. Resolving the overlay from a detached editor removes nothing and strands the hint.
            (card.Parent as Panel)?.Children.Remove(card);
            _card = null;
            _label = null;
            _shownText = null;
        }
    }

    private IBrush? Brush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b) return b;
        return null;
    }
}
