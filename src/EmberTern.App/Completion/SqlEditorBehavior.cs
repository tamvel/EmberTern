using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql;

namespace EmberTern.App.Completion;

/// <summary>
/// Shared wiring that gives any AvaloniaEdit <see cref="TextEditor"/> the same
/// SQL-editor capabilities the main SQL Editor has — autocomplete (object + column
/// suggestions via <see cref="SqlCompletionController"/>) and open-object navigation
/// (double-click + Ctrl+click on an identifier → <see cref="MainWindowViewModel.TryOpenDdlForWord"/>).
/// One implementation, reused by the SQL Editor's surfaces and the Procedure Detail
/// editors — the completion + resolution logic lives in <see cref="SqlCompletionController"/>
/// / <see cref="SqlCompletionContext"/> / the VM, not duplicated here.
/// </summary>
internal static class SqlEditorBehavior
{
    /// <param name="contextTableProvider">For a trigger body editor: returns the
    /// trigger's table so <c>NEW.</c> / <c>OLD.</c> complete that table's columns.
    /// Null for ordinary editors (NEW/OLD have no meaning there).</param>
    public static SqlCompletionController Attach(TextEditor editor, MainWindowViewModel vm, Func<string?>? contextTableProvider = null)
    {
        var completion = new SqlCompletionController(
            editor,
            vm.EnumerateLoadedObjects,
            dotTableResolver: vm.ResolveDotTable,
            cachedColumnsProvider: vm.TryGetCachedColumns,
            ensureColumnsAsync: t => vm.EnsureColumnsAsync(t),
            contextTableProvider: contextTableProvider);

        // Select-an-identifier → box all its occurrences in this editor.
        OccurrenceHighlighter.Attach(editor);

        // Double-click on an identifier → open the object (same as the SQL Editor).
        editor.DoubleTapped += (_, e) =>
        {
            if (TryOpenWord(editor, vm)) e.Handled = true;
        };

        // Ctrl+Click → open the object. Tunneled so it runs before the editor's own
        // pointer handling; only acts when the word resolves to a loaded object.
        editor.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            var props = e.GetCurrentPoint(editor).Properties;
            if (props.IsLeftButtonPressed
                && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                && TryOpenWord(editor, vm))
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        return completion;
    }

    private static bool TryOpenWord(TextEditor editor, MainWindowViewModel vm)
    {
        var text = editor.Text;
        if (string.IsNullOrEmpty(text)) return false;
        var word = SqlCompletionContext.GetWordAt(text, editor.CaretOffset);
        if (string.IsNullOrEmpty(word.Text)) return false;
        return vm.TryOpenDdlForWord(word.Text);
    }
}
