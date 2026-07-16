using System;
using System.Collections.Generic;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Completion;

/// <summary>
/// Bridges an Easy-mode routine editor's out-of-text declarations (parameters / variables held in the
/// surrounding grids) to its editors' semantic models. When the VM raises
/// <see cref="SourceObjectDetailTabViewModel.AmbientSymbolsChanged"/> — a grid add / remove / reorder or a
/// row rename — this asks every ambient-seeded editor to rebuild its model, so diagnostics, completion,
/// highlighting, Quick Info and every other <c>SemanticModel</c> consumer refresh immediately, without the
/// user having to edit the body text first.
/// <para>
/// A detail view owns one instance: it <see cref="Track"/>s the ambient-seeded editors' controllers once
/// (on visual-tree attach) and <see cref="Bind"/>s the current VM (in <c>OnDataContextChanged</c>), so the
/// subscription follows the reused view as it moves to a new object. Kept in the view layer because it
/// wires a VM event to the per-editor completion controllers, exactly like the other editor wiring in
/// <see cref="SqlEditorBehavior"/>.
/// </para>
/// </summary>
internal sealed class AmbientModelRefresh
{
    private readonly List<SqlCompletionController> _controllers = new();
    private SourceObjectDetailTabViewModel? _vm;

    /// <summary>Registers an ambient-seeded editor's controller. Ignores null (an editor absent from the
    /// view). Call once per editor, after <see cref="SqlEditorBehavior.Attach"/>.</summary>
    public void Track(SqlCompletionController? controller)
    {
        if (controller is not null) _controllers.Add(controller);
    }

    /// <summary>Binds the current VM: unsubscribes the previous one and subscribes <paramref name="vm"/>'s
    /// <see cref="SourceObjectDetailTabViewModel.AmbientSymbolsChanged"/>. Idempotent for the same VM.
    /// Safe to call before any controller is tracked (the handler simply has nothing to refresh yet).</summary>
    public void Bind(SourceObjectDetailTabViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null) _vm.AmbientSymbolsChanged -= OnAmbientChanged;
        _vm = vm;
        if (_vm is not null) _vm.AmbientSymbolsChanged += OnAmbientChanged;
    }

    private void OnAmbientChanged(object? sender, EventArgs e)
    {
        foreach (var controller in _controllers) controller.NotifyAmbientSymbolsChanged();
    }
}
