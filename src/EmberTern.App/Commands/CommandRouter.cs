using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
    private readonly Func<Control?> _objectTree;

    private CommandRouter(
        Visual root,
        Func<MainWindowViewModel?> viewModel,
        Func<bool> focusSidebarFilter,
        Func<Control?> objectTree)
    {
        _root = root;
        _viewModel = viewModel;
        _focusSidebarFilter = focusSidebarFilter;
        _objectTree = objectTree;
    }

    /// <summary>
    /// Wires the router to <paramref name="root"/>'s bubbling KeyDown.
    /// </summary>
    /// <param name="focusSidebarFilter">
    /// Focuses the Object Explorer's filter box, returning false when it is not available. A view action,
    /// not a command, so the router is handed it rather than reaching into the window.
    /// </param>
    /// <param name="objectTree">
    /// The Object Explorer's list, used only to decide whether <see cref="CommandScope.Tree"/> is live.
    /// Passed in rather than inferred from the focused element's DataContext: the window owns the sidebar,
    /// and a router that guesses which list is "the tree" would answer wrongly the day a second one exists.
    /// </param>
    public static CommandRouter Attach(
        InputElement root,
        Func<MainWindowViewModel?> viewModel,
        Func<bool> focusSidebarFilter,
        Func<Control?> objectTree)
    {
        var router = new CommandRouter(root, viewModel, focusSidebarFilter, objectTree);
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

        var focused = FocusedVisual();
        var editor = EditorSearch.EditorFor(focused);
        var vm = _viewModel();
        var focus = new FocusState(
            Editor: editor,
            InGrid: focused?.FindAncestorOfType<DataGrid>(includeSelf: true) is not null,
            InObjectTree: _objectTree() is { } tree && IsWithin(focused, tree));

        foreach (var descriptor in candidates)
        {
            if (!IsLive(descriptor, focus, vm)) continue;

            // Live, but somebody else owns the keystroke: stop here rather than letting a broader scope
            // answer for it — the owner's claim is the most specific one there is.
            if (descriptor.Dispatch == CommandDispatch.Reserved) return false;

            if (TryDispatch(descriptor, focus.Editor, vm)) return true;
        }

        return false;
    }

    /// <summary>Which focus scopes are live for one key stroke, computed once per stroke.</summary>
    private readonly record struct FocusState(TextEditor? Editor, bool InGrid, bool InObjectTree);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Handle(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    // Does this command's scope apply at this moment?
    private static bool IsLive(CommandDescriptor descriptor, FocusState focus, MainWindowViewModel? vm)
        => descriptor.Scope switch
        {
            CommandScope.Editor => focus.Editor is not null,
            CommandScope.Tree => focus.InObjectTree,
            CommandScope.Grid => focus.InGrid,
            CommandScope.Tab => descriptor.TabKinds is null
                                || (vm?.SelectedWorkspaceTab is { } tab && descriptor.TabKinds.Contains(tab.Kind)),
            _ => true,
        };

    private static bool IsWithin(Visual? candidate, Visual container)
    {
        for (var v = candidate; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, container)) return true;
        }
        return false;
    }

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
