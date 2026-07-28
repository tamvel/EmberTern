using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using EmberTern.App.Commands;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using EmberTern.Core.Sql;

namespace EmberTern.App.Completion;

/// <summary>
/// Gives any AvaloniaEdit <see cref="TextEditor"/> Find / Replace + a standard
/// right-click menu, reusing the editor's own built-in <see cref="SearchPanel"/>.
/// No custom find/replace engine — SearchPanel already does next/previous,
/// highlight-all, match-case, whole-word and regex.
///
/// Installed in one place per editor: <see cref="SqlEditorBehavior.Attach"/> for the
/// rich SQL/PSQL editors, and directly for the SQL Editor + read-only DDL preview.
///
/// <para>⚠ <b>ONE panel per editor — do not call <c>SearchPanel.Install</c> here.</b>
/// <see cref="TextEditor"/> creates and installs its own panel in its constructor
/// (measured: a bare editor already reports one <c>SearchInputHandler</c> and a non-null
/// <see cref="TextEditor.SearchPanel"/>). This method used to call
/// <c>SearchPanel.Install(editor)</c> on top of that, which registered a SECOND handler and
/// returned a DIFFERENT panel instance — so Ctrl+F drove the built-in one while this class's
/// Find / Replace drove the other. Everything here goes through
/// <see cref="TextEditor.SearchPanel"/>, so there is only one panel to be in one state.</para>
///
/// <para>The Ctrl+F / Ctrl+H gestures are NOT wired here: they are declared in
/// <c>Commands.CommandCatalog</c> at <c>CommandScope.Editor</c> and dispatched by
/// <c>Commands.CommandRouter</c>, which is what makes the editor's Find outrank the window's
/// "focus the sidebar filter" by a declared scope instead of a hand-written focus probe.</para>
/// </summary>
internal static class EditorSearch
{
    /// <summary>
    /// Installs the right-click menu and adopts the editor's own search panel.
    /// Edit actions (Cut/Paste/Replace/Comment/Format) are gated dynamically on the
    /// editor's runtime <see cref="TextEditor.IsReadOnly"/> — which is data-bound and
    /// toggles (e.g. with <c>IsNew</c>) — so a read-only source editor can't be mutated
    /// via the menu's programmatic Document.Replace.
    /// </summary>
    public static void Install(TextEditor editor)
    {
        editor.ContextMenu = BuildContextMenu(editor);
    }

    /// <summary>
    /// The <see cref="TextEditor"/> that <paramref name="v"/> is, or sits inside — else null.
    /// This is how <c>Commands.CommandRouter</c> decides whether the caret is in an editor, i.e.
    /// whether <c>CommandScope.Editor</c> is live for the gesture being resolved.
    /// </summary>
    public static TextEditor? EditorFor(Visual? v)
        => v?.FindAncestorOfType<TextEditor>(includeSelf: true);

    /// <summary>Opens Find on the editor's own panel. Returns false when the editor has no panel.</summary>
    public static bool OpenFind(TextEditor editor)
    {
        if (editor.SearchPanel is not { } panel) return false;
        panel.IsReplaceMode = false;
        SeedFromSelection(panel, editor);
        panel.Open();
        return true;
    }

    /// <summary>
    /// Opens Replace on the editor's own panel. Refused on a read-only editor — Replace would
    /// offer to mutate a document the surface guarantees is not editable (a DDL preview).
    /// </summary>
    public static bool OpenReplace(TextEditor editor)
    {
        if (editor.IsReadOnly || editor.SearchPanel is not { } panel) return false;
        panel.IsReplaceMode = true;
        SeedFromSelection(panel, editor);
        panel.Open();
        return true;
    }

    // Prefill the search box with a single-line selection (VS/IBExpert convenience).
    private static void SeedFromSelection(SearchPanel panel, TextEditor editor)
    {
        var sel = editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && sel.IndexOf('\n') < 0)
            panel.SearchPattern = sel;
    }

    private static ContextMenu BuildContextMenu(TextEditor editor)
    {
        // Icons and gestures come from the same two places every XAML menu uses — the geometry dictionary
        // and CommandCatalog — because this menu being built in C# is an implementation detail, not a
        // licence to look different. The platform edit gestures are the well-known ones and are declared
        // nowhere in the catalog (they belong to the OS/editor, not to EmberTern), so they are named here;
        // Find / Replace / Format are catalog commands and read their key from it.
        var undo = Item(UiStrings.EditorMenuUndo, () => editor.Undo(), "Icon.Undo", new KeyGesture(Key.Z, KeyModifiers.Control));
        var redo = Item(UiStrings.EditorMenuRedo, () => editor.Redo(), "Icon.Redo", new KeyGesture(Key.Y, KeyModifiers.Control));
        var cut = Item(UiStrings.EditorMenuCut, () => editor.Cut(), "Icon.Cut", new KeyGesture(Key.X, KeyModifiers.Control));
        var copy = Item(UiStrings.EditorMenuCopy, () => editor.Copy(), "Icon.Copy", new KeyGesture(Key.C, KeyModifiers.Control));
        var paste = Item(UiStrings.EditorMenuPaste, () => editor.Paste(), "Icon.Paste", new KeyGesture(Key.V, KeyModifiers.Control));
        var selectAll = Item(UiStrings.EditorMenuSelectAll, () => editor.SelectAll(), gesture: new KeyGesture(Key.A, KeyModifiers.Control));
        var find = Item(UiStrings.EditorMenuFind, () => OpenFind(editor), "Icon.Search", Gesture(CommandId.EditorFind));
        var replace = Item(UiStrings.EditorMenuReplace, () => OpenReplace(editor), gesture: Gesture(CommandId.EditorReplace));
        var comment = Item(UiStrings.EditorMenuComment, () => ApplyComment(editor, LineCommentMode.Comment), "Icon.Comment");
        var uncomment = Item(UiStrings.EditorMenuUncomment, () => ApplyComment(editor, LineCommentMode.Uncomment), "Icon.Uncomment");
        var format = Item(UiStrings.EditorMenuFormat, () => FormatEditor(editor), "Icon.Braces", Gesture(CommandId.FormatSql));

        var menu = new ContextMenu();
        foreach (var i in new object[]
        {
            undo, redo, new Separator(),
            cut, copy, paste, selectAll, new Separator(),
            find, replace, new Separator(),
            comment, uncomment, format,
        })
        {
            menu.Items.Add(i);
        }

        // Refresh enabled-state each time it opens (Can* + runtime read-only gating).
        menu.Opening += (_, _) =>
        {
            bool editable = !editor.IsReadOnly;
            undo.IsEnabled = editor.CanUndo;
            redo.IsEnabled = editor.CanRedo;
            copy.IsEnabled = editor.CanCopy;
            cut.IsEnabled = editable && editor.CanCut;
            paste.IsEnabled = editable && editor.CanPaste;
            replace.IsEnabled = editable;
            comment.IsEnabled = editable;
            uncomment.IsEnabled = editable;
            format.IsEnabled = editable;
        };
        return menu;
    }

    private static KeyGesture? Gesture(CommandId id) => new CommandGestureExtension(id).ProvideValue();

    private static MenuItem Item(string header, Action action, string? iconKey = null, KeyGesture? gesture = null)
    {
        var mi = new MenuItem
        {
            Header = header,
            // Reuses the ONE icon path — the same extension the XAML menus use, so this menu cannot end up
            // with icons of a different size or colour policy. A null key simply leaves the icon column
            // empty, which still reserves its width and keeps the labels aligned.
            Icon = iconKey is null ? null : new MenuIconExtension(iconKey).ProvideValue(),
            InputGesture = gesture,
        };
        mi.Click += (_, _) => action();
        return mi;
    }

    private static void FormatEditor(TextEditor editor)
    {
        if (editor.IsReadOnly) return;
        if (editor.SelectionLength > 0)
        {
            var start = editor.SelectionStart;
            var formatted = SqlFormatter.Format(editor.SelectedText);
            editor.Document.Replace(start, editor.SelectionLength, formatted);
        }
        else
        {
            var formatted = SqlFormatter.Format(editor.Text);
            editor.Document.Replace(0, editor.Document.TextLength, formatted);
        }
    }

    private static void ApplyComment(TextEditor editor, LineCommentMode mode)
    {
        if (editor.IsReadOnly) return;
        var r = SqlLineComment.Apply(editor.Text, editor.SelectionStart, editor.SelectionLength, mode);
        editor.Document.Replace(0, editor.Document.TextLength, r.Text);
        var start = Math.Clamp(r.SelectionStart, 0, editor.Document.TextLength);
        editor.SelectionStart = start;
        editor.SelectionLength = Math.Clamp(r.SelectionLength, 0, editor.Document.TextLength - start);
    }
}
