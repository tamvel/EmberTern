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
/// </summary>
internal sealed class LanguageExpansionController
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TextEditor _editor;
    private readonly Func<bool> _isCompletionActive;
    private Border? _card;        // the hint currently in the overlay (presentation only)
    private TextBlock? _label;    // its expansion text, updated in place to avoid flicker
    private string? _shownText;   // what the label currently shows (skip needless rebuilds)

    private LanguageExpansionController(TextEditor editor, Func<bool> isCompletionActive)
    {
        _editor = editor;
        _isCompletionActive = isCompletionActive;
    }

    /// <summary>Attaches to an editor. Shares the completion controller only to know when the completion
    /// list is up (so the two never compete). Attach in BOTH wiring seams (gotcha #219).</summary>
    public static void Attach(TextEditor editor, SqlCompletionController completion)
    {
        var c = new LanguageExpansionController(editor, () => completion.IsPopupOpen);
        // Stateless: the hint is a pure function of (text, caret), recomputed on every caret move (which
        // also fires after each keystroke). No timers, no async, no cached "armed" state.
        editor.TextArea.Caret.PositionChanged += (_, _) => c.UpdateHint();
        // Tunnel so we preempt AvaloniaEdit's Tab-indent when a construct is armed.
        editor.AddHandler(InputElement.KeyDownEvent, c.OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    // The armed construct, derived fresh — never remembered. Null while the completion list owns the caret.
    private ConstructMatch? CurrentMatch()
    {
        var doc = _editor.Document;
        if (doc is null || _isCompletionActive()) return null;
        return LanguageConstructResolver.Resolve(doc.Text, _editor.CaretOffset);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                var match = CurrentMatch();
                if (match is not null) { Expand(match); e.Handled = true; }
                break;
            case Key.Escape:
                HideHint();       // passive dismiss — do NOT consume (the list / others may use Escape)
                break;
            case Key.Space when (e.KeyModifiers & KeyModifiers.Control) != 0:
                HideHint();       // Ctrl+Space opens the list; don't let a hint linger under it
                break;
        }
    }

    private void Expand(ConstructMatch match)
    {
        var doc = _editor.Document;
        if (doc is null) return;
        var edit = ConstructExpansion.For(doc.Text, _editor.CaretOffset, match);
        if (edit.Start < 0 || edit.Start + edit.Length > doc.TextLength) return; // stale caret guard
        doc.Replace(edit.Start, edit.Length, edit.InsertText);
        _editor.CaretOffset = edit.Start + edit.CaretOffset;
        HideHint();
    }

    // ── Presentation (OverlayLayer card) ──────────────────────────────────────────────────────

    private void UpdateHint()
    {
        var match = CurrentMatch();
        if (match is null) { HideHint(); return; }
        ShowHint(match.Construct.Expansion);
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
                Background = Brush("ElevatedPanelBrush") ?? Brush("PanelBrush") ?? Brush("BackgroundBrush"),
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
            OverlayLayer.GetOverlayLayer(_editor)?.Children.Remove(card);
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
