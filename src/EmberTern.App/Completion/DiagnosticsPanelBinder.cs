using System;
using System.Collections.Generic;
using AvaloniaEdit;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Completion;

/// <summary>
/// Stage 7 / S4 — feeds the <see cref="DiagnosticsPanelViewModel"/> from an editor's <b>cached</b>
/// diagnostics. The last link of the one-way chain
/// <c>Parser → AST → SemanticModel → DiagnosticsEngine → EditorLanguageService → panel</c>.
/// <para>
/// Publishes on the shared <see cref="SqlCompletionController.ModelUpdated"/> cycle — the same signal the
/// semantic highlighter and the squiggle renderer ride — so it triggers no parse, no model rebuild and no
/// second analysis; it only reads the list the language service already computed on its background pass.
/// Every refresh requirement falls out of that one subscription: a text edit and a metadata bump rebuild
/// the model, and an Easy-mode ambient-symbol change routes through
/// <see cref="SqlCompletionController.NotifyAmbientSymbolsChanged"/> into the very same rebuild.
/// </para>
/// <para>
/// It lives in the view layer because mapping a <c>Diagnostic</c>'s absolute offset to a line/column needs
/// the AvaloniaEdit document — exactly the reason the other editor wiring
/// (<see cref="AmbientModelRefresh"/>, <see cref="SqlEditorBehavior"/>) sits here too. The VM stays free of
/// Avalonia types.
/// </para>
/// </summary>
internal sealed class DiagnosticsPanelBinder
{
    private readonly TextEditor _editor;
    private readonly SqlCompletionController _controller;
    private readonly Func<DiagnosticsPanelViewModel?> _panel;

    private DiagnosticsPanelBinder(
        TextEditor editor, SqlCompletionController controller, Func<DiagnosticsPanelViewModel?> panel)
    {
        _editor = editor;
        _controller = controller;
        _panel = panel;
    }

    /// <summary>Binds <paramref name="controller"/>'s diagnostics to the panel <paramref name="panel"/>
    /// resolves. The panel is resolved lazily on each publish because the window's VM attaches after the
    /// editor is wired. Returns the binder so the host can <see cref="Publish"/> on a VM swap.</summary>
    public static DiagnosticsPanelBinder Attach(
        TextEditor editor, SqlCompletionController controller, Func<DiagnosticsPanelViewModel?> panel)
    {
        var binder = new DiagnosticsPanelBinder(editor, controller, panel);
        controller.ModelUpdated += (_, _) => binder.Publish();
        return binder;
    }

    /// <summary>Projects the cached diagnostics into the panel. Safe before a VM (or a model) exists.</summary>
    public void Publish()
    {
        var panel = _panel();
        if (panel is null) return;

        var diagnostics = _controller.Diagnostics;
        var document = _editor.Document;
        var rows = new List<DiagnosticRowViewModel>(diagnostics.Count);

        foreach (var d in diagnostics)
        {
            int line = 1, column = 1;
            if (document is not null)
            {
                // Clamp: diagnostics are version-matched to the model, but a publish can land a hair ahead
                // of the next rebuild after an edit — never ask the document about an offset past its end
                // (same guard the squiggle renderer applies on the paint path).
                var location = document.GetLocation(Math.Clamp(d.Start, 0, document.TextLength));
                line = location.Line;
                column = location.Column;
            }
            rows.Add(new DiagnosticRowViewModel(d, line, column));
        }

        // Engine order, verbatim — the panel never sorts (design §8.2).
        panel.Update(rows);
    }
}
