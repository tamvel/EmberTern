using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;

namespace EmberTern.App.Completion;

/// <summary>
/// The unified <b>Parameter Helper</b> (design §28 — generalised from the INSERT-only P6 helper): ONE
/// in-window overlay card that shows the parameter list of whatever call/DML site the caret is at —
/// INSERT / UPDATE-OR-INSERT column↔value mapping, EXECUTE PROCEDURE / selectable-proc / function
/// arguments — with the active parameter highlighted and (for routines) its IN/OUT direction. Its
/// single source of truth is <see cref="SignatureHelpEngine"/>; this class is pure presentation +
/// lifetime, shared by both triggers (a double-click on a value, and typing an argument list).
/// <para>
/// Hosted in the editor's <see cref="OverlayLayer"/> — a bare <c>Popup</c> renders invisibly on the
/// desktop despite IsOpen/Visible/Opacity all true (gotcha #209). Lifetime is CONTEXT-driven, not
/// offset-driven (gotcha #210): on every caret move it re-asks the engine and stays open while the
/// caret is still at the SAME site (kind + target), follows the active argument, and closes only when
/// the semantic context changes / Escape / detach.
/// </para>
/// </summary>
internal sealed class ParameterHelper
{
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TextEditor _editor;
    private readonly Func<SemanticModel?> _model;
    // Warms the metadata a signature needs — routine params for a proc/function, columns for an
    // INSERT/UPDATE target — then rebuilds the shared model and returns it, so an uncached callee still
    // shows its parameters after a warm. Null → no warm (the helper only shows what is already cached).
    private readonly Func<string, SignatureKind, Task<SemanticModel?>>? _warmAndRebuild;

    private Control? _card;       // the card currently in the overlay, or null
    private Control? _activeRow;  // the active-parameter row, scrolled into view after layout
    private SignatureKind _kind;  // context identity: what construct the open card is for…
    private string? _target;      // …and its callee/target name (routine or table)
    private int _activeParam;     // the argument index the card currently highlights
    private int _warmGen;         // bumped per trigger so a slow warm from a superseded trigger is dropped
    private bool _detached;

    private ParameterHelper(
        TextEditor editor, Func<SemanticModel?> model, Func<string, SignatureKind, Task<SemanticModel?>>? warmAndRebuild)
    {
        _editor = editor;
        _model = model;
        _warmAndRebuild = warmAndRebuild;
    }

    /// <summary>Attaches the helper to <paramref name="editor"/>. It subscribes to caret moves for its
    /// context-driven lifetime; both triggers (double-click, typing) call <see cref="ShowAt"/>.</summary>
    public static ParameterHelper Attach(
        TextEditor editor, Func<SemanticModel?> model, Func<string, SignatureKind, Task<SemanticModel?>>? warmAndRebuild = null)
    {
        var h = new ParameterHelper(editor, model, warmAndRebuild);
        editor.TextArea.Caret.PositionChanged += h.OnCaretMoved;
        return h;
    }

    /// <summary>True when the card is currently shown.</summary>
    public bool IsOpen => _card is not null;

    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _editor.TextArea.Caret.PositionChanged -= OnCaretMoved;
        Hide();
    }

    /// <summary>Shows the helper for the call/DML site at <paramref name="offset"/> (a double-click on a
    /// value, or a typing trigger). Warms uncached metadata then retries. Returns whether this IS a
    /// parameter site — so a caller (e.g. the double-click) can fall back to another action when it is
    /// not. Read-only; never throws.</summary>
    public bool ShowAt(int offset)
    {
        if (_detached) return false;
        var model = _model();
        if (model is null) { Hide(); return false; }

        var sig = SignatureHelpEngine.GetSignature(model, offset);
        if (sig is not null && sig.Parameters.Count > 0)
        {
            ShowCard(sig);
            return true;
        }

        // A recognised site whose metadata isn't cached yet (a known routine with its params not loaded,
        // or an INSERT/VALUES whose columns aren't cached so the engine returned null) — warm + retry.
        if (_warmAndRebuild is not null)
        {
            var (target, kind) = WarmTargetFor(model, sig, offset);
            if (target is not null)
            {
                int gen = ++_warmGen;
                _ = WarmAndShowAsync(target, kind, offset, gen);
                return true;
            }
        }

        Hide();
        return false;
    }

    // The (target, kind) to warm for a not-yet-showable site: the callee of an empty-param signature,
    // or the INSERT target table when the engine returned null (its columns aren't cached).
    private static (string? Target, SignatureKind Kind) WarmTargetFor(SemanticModel model, SignatureInfo? sig, int offset)
    {
        if (sig is not null && !string.IsNullOrEmpty(sig.Label)) return (sig.Label, sig.Kind);
        var insertTable = SignatureHelpEngine.TryGetInsertTargetTable(model, offset);
        return (insertTable, SignatureKind.Insert);
    }

    private async Task WarmAndShowAsync(string target, SignatureKind kind, int offset, int gen)
    {
        var model = await _warmAndRebuild!(target, kind).ConfigureAwait(true);
        if (_detached || model is null || gen != _warmGen) return;
        var sig = SignatureHelpEngine.GetSignature(model, offset);
        if (sig is not null && sig.Parameters.Count > 0) ShowCard(sig);
    }

    /// <summary>Removes the card from the overlay (Escape / completion-list opening / a lost context).</summary>
    public void Hide()
    {
        if (_card is { } card)
        {
            OverlayLayer.GetOverlayLayer(_editor)?.Children.Remove(card);
            _card = null;
        }
    }

    // Context-driven lifetime (gotcha #210): on any caret move, re-query the engine and keep the card
    // open while the caret is still at the SAME site (kind + target), following the active argument;
    // close when the context genuinely changes (different site, different construct, or none).
    private void OnCaretMoved(object? sender, EventArgs e)
    {
        if (_detached || _card is null) return;
        var model = _model();
        var sig = model is null ? null : SignatureHelpEngine.GetSignature(model, _editor.CaretOffset);
        if (sig is null || sig.Parameters.Count == 0
            || sig.Kind != _kind || !string.Equals(sig.Label, _target, StringComparison.OrdinalIgnoreCase))
        {
            Hide();
            return;
        }
        if (sig.ActiveParameter != _activeParam)
        {
            _activeParam = sig.ActiveParameter;
            var overlay = OverlayLayer.GetOverlayLayer(_editor);
            if (overlay is not null && _card is { } existing)
            {
                SetCard(overlay, sig, Canvas.GetLeft(existing), Canvas.GetTop(existing)); // keep position, follow the arg
            }
        }
    }

    // Anchors a fresh card just below the caret line and remembers the context it is for.
    private void ShowCard(SignatureInfo sig)
    {
        var overlay = OverlayLayer.GetOverlayLayer(_editor);
        if (overlay is null) return;

        _kind = sig.Kind;
        _target = sig.Label;
        _activeParam = sig.ActiveParameter;

        double left = 0, top = 0;
        if (EditorPopups.TryGetCaretRect(_editor, out var rect))
        {
            var p = _editor.TranslatePoint(new Point(rect.X, rect.Bottom), overlay);
            if (p is { } pt) { left = pt.X; top = pt.Y; }
        }
        SetCard(overlay, sig, left, top);
    }

    // Builds the card for sig, positions it at (left, top), and adds it — replacing any existing card.
    // After layout it clamps on-screen and scrolls the highlighted parameter into view.
    private void SetCard(OverlayLayer overlay, SignatureInfo sig, double left, double top)
    {
        if (_card is { } existing) overlay.Children.Remove(existing);
        _activeRow = null;
        var card = BuildCard(sig);
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        overlay.Children.Add(card);
        _card = card;

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_card, card)) return;
            // 18 ≈ one caret line + a gap, so a flipped card clears the line it describes.
            EditorPopups.ClampIntoOverlay(overlay, card, flipOffset: 18);
            _activeRow?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    // A compact themed card: a kind-appropriate heading, then a numbered parameter list (1-based
    // ordinal + "name : type", plus an IN/OUT tag for routine parameters), the active parameter a solid
    // accent pill so it is unmistakable. Scrolls when tall. Theme tokens only (no hardcoded colours).
    private Control BuildCard(SignatureInfo sig)
    {
        var fg = Brush("ForegroundBrush");
        var subtle = Brush("SubtleForegroundBrush");
        var accent = Brush("AccentBrush") ?? fg;
        var onAccent = Brush("OnAccentBrush") ?? Brush("BackgroundBrush");
        bool showDirection = sig.Kind is SignatureKind.Procedure or SignatureKind.Function;

        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock
        {
            Text = HeaderFor(sig),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = subtle,
        });

        var list = new StackPanel { Spacing = 1 };
        for (int i = 0; i < sig.Parameters.Count; i++)
        {
            var p = sig.Parameters[i];
            bool active = i == sig.ActiveParameter;
            var text = string.IsNullOrEmpty(p.Type) ? p.Name : $"{p.Name} : {p.Type}";

            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            inner.Children.Add(new TextBlock
            {
                Text = $"{i + 1}.",
                FontSize = 11,
                MinWidth = 22,
                TextAlignment = TextAlignment.Right,
                Foreground = active ? onAccent : subtle,
            });
            inner.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontFamily = MonoFont,
                FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
                Foreground = active ? onAccent : fg,
            });
            if (showDirection)
            {
                inner.Children.Add(new TextBlock
                {
                    Text = p.Direction == ParameterDirection.Output ? "OUT" : "IN",
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = active ? onAccent : subtle,
                });
            }

            // The active parameter gets a solid accent pill (bold accent TEXT alone was too faint —
            // user feedback). Inactive rows are plain.
            var row = new Border
            {
                Child = inner,
                Background = active ? accent : Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, active ? 2 : 0),
            };
            list.Children.Add(row);
            if (active) _activeRow = row;
        }

        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 320,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        return new Border
        {
            Child = panel,
            Background = Brush("ElevatedPanelBrush") ?? Brush("BackgroundBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            MaxWidth = 460,
            IsHitTestVisible = false, // never intercept the pointer (mirrors the hover / Quick Info cards)
        };
    }

    private static string HeaderFor(SignatureInfo sig) => sig.Kind switch
    {
        SignatureKind.Insert => $"INSERT INTO {sig.Label}",
        SignatureKind.Update => $"UPDATE {sig.Label}",
        SignatureKind.Procedure => $"{sig.Label} (procedure)",
        SignatureKind.Function => $"{sig.Label} (function)",
        _ => sig.Label,
    };

    private IBrush? Brush(string key)
    {
        var theme = _editor.ActualThemeVariant;
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b) return b;
        return null;
    }
}
