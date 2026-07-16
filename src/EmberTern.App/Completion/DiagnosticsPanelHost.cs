using System;
using System.Collections.Generic;
using AvaloniaEdit;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage 7 / S4 — hosts one <see cref="DiagnosticsPanelViewModel"/> for an object editor that owns
/// several SQL editors (Procedure / Function: source · body · cursor · subprogram; Trigger / View:
/// source · body; Package: header · body), and decides <b>which one the panel reflects</b>.
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
/// </summary>
internal sealed class DiagnosticsPanelHost
{
    private readonly List<DiagnosticsPanelBinder> _binders = new();
    private readonly Func<DiagnosticsPanelViewModel?> _panel;
    private readonly Func<TextEditor?> _fallbackDocument;
    private TextEditor? _lastFocused;

    /// <param name="panel">Resolves the current VM's panel (lazily — a detail view is reused across
    /// objects, so its VM changes).</param>
    /// <param name="fallbackDocument">The mode's primary editor — what the panel reflects until the user
    /// focuses an editor, and after <see cref="ResetActiveDocument"/>.</param>
    public DiagnosticsPanelHost(Func<DiagnosticsPanelViewModel?> panel, Func<TextEditor?> fallbackDocument)
    {
        _panel = panel;
        _fallbackDocument = fallbackDocument;
    }

    private TextEditor? ActiveDocument => _lastFocused ?? _fallbackDocument();

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
}
