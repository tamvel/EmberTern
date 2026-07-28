using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Commands;

/// <summary>
/// The single keyboard dispatcher for every gesture declared in <see cref="CommandCatalog"/>. It replaces
/// the window's <c>KeyBindings</c> block and its hand-written key handler, so a gesture is bound in exactly
/// one place and its meaning follows from the declared scope rather than from which handler happened to run.
///
/// <para>⚠ <b>It listens on the BUBBLE phase, deliberately.</b> A control that owns a keystroke — the
/// editor's typing mechanics, a text box, the debugger's stepping surface — sees it first and marks it
/// handled, exactly as the window's <c>KeyBindings</c> behaved. The router is the last resort, never the
/// first. (The old handler was on the TUNNEL phase, which is why it needed a focus probe to hand Ctrl+F
/// back to the editor.)</para>
///
/// <para><b>Resolution.</b> For a pressed gesture the catalog returns every command claiming it, most
/// specific scope first. The router takes the first one that is <i>live</i> (its scope applies right now)
/// and can actually run. A live command whose dispatch is <see cref="CommandDispatch.Reserved"/> stops the
/// search without handling the key: its owner has the claim. A live but unavailable routed command — no
/// command on this tab kind, or <c>CanExecute</c> false — falls through to the next, less specific
/// candidate. If nothing resolves the key is left alone.</para>
/// </summary>
internal sealed class CommandRouter
{
    private readonly Visual _root;
    private readonly Func<MainWindowViewModel?> _viewModel;
    private readonly Func<bool> _focusSidebarFilter;

    private CommandRouter(Visual root, Func<MainWindowViewModel?> viewModel, Func<bool> focusSidebarFilter)
    {
        _root = root;
        _viewModel = viewModel;
        _focusSidebarFilter = focusSidebarFilter;
    }

    /// <summary>
    /// Wires the router to <paramref name="root"/>'s bubbling KeyDown.
    /// </summary>
    /// <param name="focusSidebarFilter">
    /// Focuses the Object Explorer's filter box, returning false when it is not available. A view action,
    /// not a command, so the router is handed it rather than reaching into the window.
    /// </param>
    public static CommandRouter Attach(
        InputElement root,
        Func<MainWindowViewModel?> viewModel,
        Func<bool> focusSidebarFilter)
    {
        var router = new CommandRouter(root, viewModel, focusSidebarFilter);
        root.AddHandler(InputElement.KeyDownEvent, router.OnKeyDown, RoutingStrategies.Bubble);
        return router;
    }

    /// <summary>
    /// Resolves and invokes the command for one key stroke. Returns true when a command ran — exposed so a
    /// test can drive the real resolution without an input backend.
    /// </summary>
    public bool Handle(Key key, KeyModifiers modifiers)
    {
        var candidates = CommandCatalog.Match(key, modifiers);
        if (candidates.Count == 0) return false;

        var editor = EditorSearch.EditorFor(FocusedVisual());
        var vm = _viewModel();

        foreach (var descriptor in candidates)
        {
            if (!IsLive(descriptor, editor, vm)) continue;

            // Live, but somebody else owns the keystroke: stop here rather than letting a broader scope
            // answer for it — the owner's claim is the most specific one there is.
            if (descriptor.Dispatch == CommandDispatch.Reserved) return false;

            if (TryDispatch(descriptor, editor, vm)) return true;
        }

        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Handle(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    // Does this command's scope apply at this moment?
    private static bool IsLive(CommandDescriptor descriptor, TextEditor? editor, MainWindowViewModel? vm)
        => descriptor.Scope switch
        {
            CommandScope.Editor => editor is not null,
            CommandScope.Tab => descriptor.TabKinds is null
                                || (vm?.SelectedWorkspaceTab is { } tab && descriptor.TabKinds.Contains(tab.Kind)),
            _ => true,
        };

    private bool TryDispatch(CommandDescriptor descriptor, TextEditor? editor, MainWindowViewModel? vm)
        => descriptor.Id switch
        {
            // View actions — they act on a control, so there is no view-model command to resolve.
            CommandId.EditorFind => editor is not null && EditorSearch.OpenFind(editor),
            CommandId.EditorReplace => editor is not null && EditorSearch.OpenReplace(editor),
            CommandId.FocusSidebarFilter => _focusSidebarFilter(),

            // Everything else is a view-model command, resolved by the scope's owner.
            _ => TryExecute(descriptor.Scope == CommandScope.Tab
                ? vm?.SelectedWorkspaceTab?.ResolveCommand(descriptor.Id)
                : vm?.ResolveCommand(descriptor.Id)),
        };

    private static bool TryExecute(ICommand? command)
    {
        if (command is null || !command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

    private Visual? FocusedVisual()
        => TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() as Visual;
}
