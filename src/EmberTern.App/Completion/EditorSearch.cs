using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using EmberTern.Core.Sql;

namespace EmberTern.App.Completion;

/// <summary>
/// Gives any AvaloniaEdit <see cref="TextEditor"/> Find / Replace + a standard
/// right-click menu, reusing AvaloniaEdit's built-in <see cref="SearchPanel"/>
/// (Find via Ctrl+F is auto-wired by <see cref="SearchPanel.Install"/>; we add
/// Ctrl+H for Replace). No custom find/replace engine — SearchPanel already does
/// next/previous, highlight-all, match-case, whole-word and regex.
///
/// Installed in one place per editor: <see cref="SqlEditorBehavior.Attach"/> for the
/// rich SQL/PSQL editors, and directly for the SQL Editor + read-only DDL preview.
/// </summary>
internal static class EditorSearch
{
    /// <summary>
    /// Installs Find (Ctrl+F, auto-wired) + Replace (Ctrl+H) + the right-click menu.
    /// Edit actions (Cut/Paste/Replace/Comment/Format) are gated dynamically on the
    /// editor's runtime <see cref="TextEditor.IsReadOnly"/> — which is data-bound and
    /// toggles (e.g. with <c>IsNew</c>) — so a read-only source editor can't be mutated
    /// via the menu's programmatic Document.Replace.
    /// </summary>
    public static SearchPanel Install(TextEditor editor)
    {
        var panel = SearchPanel.Install(editor); // wires Ctrl+F to open Find

        // Ctrl+H → Replace (AvaloniaEdit only auto-wires Ctrl+F). Bubbles up from the
        // focused inner text view to the editor.
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!editor.IsReadOnly
                && e.Key == Key.H
                && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                OpenReplace(panel, editor);
                e.Handled = true;
            }
        });

        editor.ContextMenu = BuildContextMenu(editor, panel);
        return panel;
    }

    /// <summary>True when <paramref name="v"/> is (or sits inside) a
    /// <see cref="TextEditor"/> — used by the window's Ctrl+F router to leave the
    /// shortcut for the editor's own SearchPanel instead of the sidebar filter.</summary>
    public static bool IsInsideEditor(Visual? v)
        => v is not null && v.FindAncestorOfType<TextEditor>(includeSelf: true) is not null;

    public static void OpenFind(SearchPanel panel, TextEditor editor)
    {
        panel.IsReplaceMode = false;
        SeedFromSelection(panel, editor);
        panel.Open();
    }

    public static void OpenReplace(SearchPanel panel, TextEditor editor)
    {
        panel.IsReplaceMode = true;
        SeedFromSelection(panel, editor);
        panel.Open();
    }

    // Prefill the search box with a single-line selection (VS/IBExpert convenience).
    private static void SeedFromSelection(SearchPanel panel, TextEditor editor)
    {
        var sel = editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && sel.IndexOf('\n') < 0)
            panel.SearchPattern = sel;
    }

    private static ContextMenu BuildContextMenu(TextEditor editor, SearchPanel panel)
    {
        var undo = Item(UiStrings.EditorMenuUndo, () => editor.Undo());
        var redo = Item(UiStrings.EditorMenuRedo, () => editor.Redo());
        var cut = Item(UiStrings.EditorMenuCut, () => editor.Cut());
        var copy = Item(UiStrings.EditorMenuCopy, () => editor.Copy());
        var paste = Item(UiStrings.EditorMenuPaste, () => editor.Paste());
        var selectAll = Item(UiStrings.EditorMenuSelectAll, () => editor.SelectAll());
        var find = Item(UiStrings.EditorMenuFind, () => OpenFind(panel, editor));
        var replace = Item(UiStrings.EditorMenuReplace, () => OpenReplace(panel, editor));
        var comment = Item(UiStrings.EditorMenuComment, () => ApplyComment(editor, LineCommentMode.Comment));
        var uncomment = Item(UiStrings.EditorMenuUncomment, () => ApplyComment(editor, LineCommentMode.Uncomment));
        var format = Item(UiStrings.EditorMenuFormat, () => FormatEditor(editor));

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

    private static MenuItem Item(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
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
