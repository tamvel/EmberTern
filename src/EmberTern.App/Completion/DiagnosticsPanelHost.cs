using System;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Language;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage 7 / S4 + S5 — owns one <see cref="DiagnosticsPanelViewModel"/>'s relationship to an SQL editing
/// surface: <b>which SQL document the panel reflects</b>, and <b>where navigation jumps</b>.
///
/// <para><b>Every surface goes through this host</b> — the object editors, which own several SQL editors
/// (Procedure / Function: source · body · cursor · subprogram; Trigger / View: source · body; Package:
/// header · body), and the main SQL Editor, whose single editor makes the rule below collapse onto it. The
/// SQL Editor gains nothing from the rule itself; it uses the host so there is exactly ONE targeting
/// mechanism, and the panel and navigation therefore cannot disagree on any surface.</para>
///
/// <para><b>The rule — "LastFocusedSqlDocument" (explicit Stage 7 design decision).</b> The panel always
/// reflects the SQL document the user was <em>last working in</em>: the last SQL editor to take focus, or —
/// until one does, and after a mode switch — the mode's primary editor (the fallback supplied by the host
/// view). It deliberately does <b>not</b> reuse the views' existing <c>ActiveEditor</c> property, whose first
/// clause requires <c>_focusedEditor.IsEffectivelyVisible</c>: selecting the peer Diagnostics tab hides the
/// editor tab, so that guard would always fail there and collapse the panel onto the mode's primary editor —
/// the Cursors/Subprograms editors could then never appear in it. The guard exists so Alt+F never formats a
/// hidden editor; a read-only list has no such concern. Tracking focus stickily instead also keeps the panel
/// independent of how AvaloniaEdit/TabControl realize hidden tab content.</para>
///
/// <para><b>Scope — the active document only.</b> The panel shows ONE editor's findings, never a merge of
/// several (explicit user decision): a workspace-wide diagnostics list, if it is ever wanted, is a separate
/// feature and must not change this panel's meaning. A finding in a non-active editor is therefore not
/// listed — its squiggle still flags it in place.</para>
///
/// <para>Pure wiring: it owns no diagnostics logic and computes nothing. Each tracked editor gets an
/// ordinary <see cref="DiagnosticsPanelBinder"/>, gated through that binder's existing lazy panel resolver —
/// a binder whose editor is not the active document resolves to <c>null</c> and publishes nothing. So
/// <see cref="Republish"/> can simply ask every binder to publish: exactly one of them will.</para>
///
/// <para><b>S5 — navigation lives here for one reason: this is the class that knows the target.</b>
/// Next/previous and row activation both jump into <see cref="ActiveDocument"/> — the same rule the panel
/// publishes through — so a row and the jump can never disagree. Navigation is a <b>pure consumer</b>: it
/// reads the panel's already-published rows (themselves the language service's cached, version-matched
/// findings) and never parses, rebuilds a <c>SemanticModel</c>, or re-runs the
/// <see cref="DiagnosticsEngine"/>. Hosting <c>F8</c> in <see cref="Track"/> also means every SQL surface
/// gets it from ONE place — the main SQL Editor included, since it now takes a host too (gotcha #219:
/// <see cref="SqlEditorBehavior"/> is not a seam that reaches it).</para>
/// </summary>
internal sealed class DiagnosticsPanelHost
{
    private readonly List<DiagnosticsPanelBinder> _binders = new();
    private readonly Func<DiagnosticsPanelViewModel?> _panel;
    private readonly Func<TextEditor?> _fallbackDocument;
    private readonly Action<TextEditor>? _reveal;
    private TextEditor? _lastFocused;

    /// <param name="panel">Resolves the current VM's panel (lazily — a detail view is reused across
    /// objects, so its VM changes).</param>
    /// <param name="fallbackDocument">The mode's primary editor — what the panel reflects until the user
    /// focuses an editor, and after <see cref="ResetActiveDocument"/>.</param>
    /// <param name="reveal">S5 — brings a jump target on screen before the caret lands in it. In the object
    /// editors the Diagnostics panel is a PEER tab, so reading the list hides the editor: activating a row
    /// there has TWO targets, the caret AND the tab. Null for a surface whose editor is always visible
    /// beside its panel.</param>
    public DiagnosticsPanelHost(
        Func<DiagnosticsPanelViewModel?> panel,
        Func<TextEditor?> fallbackDocument,
        Action<TextEditor>? reveal = null)
    {
        _panel = panel;
        _fallbackDocument = fallbackDocument;
        _reveal = reveal;
    }

    /// <summary>The one true target — the <c>LastFocusedSqlDocument</c> rule (§8.2.1). Both the panel's
    /// contents and S5's jumps route through this, so they cannot disagree.</summary>
    public TextEditor? ActiveDocument => _lastFocused ?? _fallbackDocument();

    /// <summary>Registers one SQL editor and its controller. Ignores a null editor (a surface the view does
    /// not have). Call once per editor, after <see cref="SqlEditorBehavior.Attach"/>.</summary>
    public void Track(TextEditor? editor, SqlCompletionController controller)
    {
        if (editor is null) return;

        _binders.Add(DiagnosticsPanelBinder.Attach(
            editor, controller,
            // The gate: publish only while this editor IS the active document.
            () => ReferenceEquals(ActiveDocument, editor) ? _panel() : null));

        // Focus tracking is the host's own (deliberately separate from the view's `_focusedEditor`, which
        // backs the visibility-guarded ActiveEditor rule that Format/selection need — see the class remarks).
        // Republishing here is what makes switching editors refresh the panel with no text edit.
        editor.GotFocus += (_, _) =>
        {
            if (ReferenceEquals(_lastFocused, editor)) return;
            _lastFocused = editor;
            Republish();
        };

        // S5: F8 / Shift+F8 on every SQL editing surface, wired once. The editor pressing the key IS the
        // active document (it has focus), so this is always scoped to what the panel is showing.
        editor.KeyDown += OnEditorKeyDown;
    }

    /// <summary>Drops the sticky document and republishes — the fallback takes over. Call when the editor
    /// MODE flips (an Easy-mode editor must not stay selected in Source mode, and vice versa) and when the
    /// view is rebound to a different object.</summary>
    public void ResetActiveDocument()
    {
        _lastFocused = null;
        Republish();
    }

    /// <summary>Re-publishes the active document's cached diagnostics into the current panel. Reads only
    /// cached state — no parse, no re-analysis.</summary>
    public void Republish()
    {
        foreach (var binder in _binders) binder.Publish();
    }

    // ── Navigation (S5) ─────────────────────────────────────────────────────────────────────────

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F8) return;
        if (e.KeyModifiers == KeyModifiers.None) Navigate(forward: true);
        else if (e.KeyModifiers == KeyModifiers.Shift) Navigate(forward: false);
        else return;
        e.Handled = true;
    }

    /// <summary>Moves to the next (or previous) diagnostic of the active document, wrapping silently. The
    /// panel's selection follows, so it and the caret always name the same finding. A clean document is a
    /// no-op — never a "no more diagnostics" prompt.</summary>
    public void Navigate(bool forward)
    {
        var editor = ActiveDocument;
        var panel = _panel();
        if (editor is null || panel is null) return;

        // The caret is the anchor, so navigation is monotonic and independent of how the selection got
        // where it is (a click, a previous jump, or nothing at all).
        int index = forward ? panel.IndexAfter(editor.CaretOffset) : panel.IndexBefore(editor.CaretOffset);
        if (index < 0) return;

        panel.SelectedIndex = index;
        JumpTo(editor, panel.Diagnostics[index].Diagnostic);
    }

    /// <summary>Activates a panel row (double-click / Enter): jumps into the active document and leaves the
    /// selection where the user put it.</summary>
    public void ActivateRow(DiagnosticRowViewModel? row)
    {
        var editor = ActiveDocument;
        if (editor is null || row is null) return;
        JumpTo(editor, row.Diagnostic);
    }

    /// <summary>Q5 — the panel as a THIRD trigger for the code-action menu: jump to the row (exactly as
    /// activating it does), then ask the editor to open the menu at the caret that jump just set. It adds
    /// no way to obtain or perform an action; it reuses the one published by
    /// <see cref="SqlEditorBehavior"/>, so all three surfaces run the same flow.</summary>
    public void ShowCodeActionsForRow(DiagnosticRowViewModel? row)
    {
        var editor = ActiveDocument;
        if (editor is null || row is null) return;
        JumpTo(editor, row.Diagnostic);
        SqlEditorBehavior.GetCodeActions(editor)?.Invoke();
    }

    /// <summary>Reveals the target, places the caret on the diagnostic's span and focuses the editor —
    /// the same gesture go-to-definition uses (<see cref="NavigationController"/>), so a jump reads the
    /// same wherever it came from.</summary>
    private void JumpTo(TextEditor editor, Diagnostic diagnostic)
    {
        if (editor.Document is not { } document) return;

        _reveal?.Invoke(editor);

        // Clamp: diagnostics are version-matched to the model, but an activation can land a hair ahead of
        // the next rebuild after an edit — the same guard the squiggle renderer and the panel binder apply.
        int start = Math.Clamp(diagnostic.Start, 0, document.TextLength);
        int length = Math.Clamp(diagnostic.Length, 0, document.TextLength - start);

        // Caret + selection SYNCHRONOUSLY: the caret is the anchor the next F8 reads, and input events
        // outrank Background posts — a queued keypress would otherwise navigate from a stale position and
        // land on the same diagnostic twice.
        editor.CaretOffset = start;
        if (length > 0) editor.Select(start, length);

        // Scrolling + focus, however, need a laid-out TextView: a reveal above may have just switched the
        // tab that hosts this editor, so it has no layout until the next pass.
        Dispatcher.UIThread.Post(() =>
        {
            editor.TextArea.Caret.BringCaretToView();
            editor.Focus();
        }, DispatcherPriority.Background);
    }
}
