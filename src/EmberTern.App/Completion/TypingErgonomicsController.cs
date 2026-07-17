using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using EmberTern.Core.Sql.Language.Ergonomics;

namespace EmberTern.App.Completion;

/// <summary>
/// <b>Typing Ergonomics</b> (design: <c>docs/design/editor-language-expansion.md</c> §3) — the editor's
/// mechanical editing aids: the <c>begin … end</c> keyword pair, delimiter pairing, and auto-indent. It
/// removes typing mechanics; it never authors code (Rule 0).
/// <para>Like <see cref="LanguageExpansionController"/> this is a thin, <b>stateless</b> consumer of a
/// pure Core decision — every edit is re-derived from (text, caret) on the keystroke. Nothing is
/// remembered, nothing is timed.</para>
/// <para><b>Enter stays an ordinary indented newline.</b> When a block opens, the caret lands exactly
/// where a plain Enter + auto-indent would have put it — the closer simply appears on the line below. So
/// Enter never jumps the caret by grammar and carries no hidden meaning (§1).</para>
/// </summary>
internal sealed class TypingErgonomicsController
{
    private readonly TextEditor _editor;

    private TypingErgonomicsController(TextEditor editor) => _editor = editor;

    /// <summary>Attaches to an editor. Called from the single shared wiring seam
    /// <see cref="SqlEditorBehavior.Attach"/> (D3 consolidated the former two seams; gotcha #219).</summary>
    public static void Attach(TextEditor editor)
    {
        var c = new TypingErgonomicsController(editor);
        // Tunnel: Enter and Backspace are built-in editing keys, so a bubbling handler would run after
        // AvaloniaEdit had already done them — the same race Tab loses (gotcha #224).
        editor.AddHandler(InputElement.KeyDownEvent, c.OnPreviewKeyDown, RoutingStrategies.Tunnel);
        // TextEntering fires BEFORE the character reaches the document, which is the only place a pair can
        // replace the plain insertion.
        editor.TextArea.TextEntering += c.OnTextEntering;
        // Structural auto-indent through AvaloniaEdit's own seam: Enter still inserts the newline itself,
        // and only its leading whitespace becomes smart. (The default strategy copies the previous line's
        // indent, which cannot see that `begin` opened a level or `end` closed one.)
        editor.TextArea.IndentationStrategy = new SqlIndentationStrategy();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter) TryOpenBlock(e);
        else if (e.Key == Key.Back) TryRemoveEmptyPair(e);
    }

    // Typing an opener pairs it; typing a closer that is already there steps over it. Anything else:
    // don't handle, and the character is inserted normally.
    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        var doc = _editor.Document;
        if (doc is null) return;
        if (_editor.SelectionLength > 0) return;   // typing REPLACES a selection — ordinary editing
        if (e.Text is not { Length: 1 }) return;   // paste / IME composition is not a keystroke

        var edit = DelimiterPairing.OnCharacterTyped(doc.Text, _editor.CaretOffset, e.Text[0]);
        if (edit is null) return;
        Apply(doc, edit);
        e.Handled = true;
    }

    // Backspace between an empty pair removes both — the pair was made by one keystroke, so it dies by one.
    private void TryRemoveEmptyPair(KeyEventArgs e)
    {
        var doc = _editor.Document;
        if (doc is null || _editor.SelectionLength > 0) return;

        var edit = DelimiterPairing.OnBackspace(doc.Text, _editor.CaretOffset);
        if (edit is null) return;
        Apply(doc, edit);
        e.Handled = true;
    }

    // Opens `begin … end` when Enter completes an unclosed opener. Anything else: don't handle, and the
    // editor's own Enter (plus the indentation strategy) runs untouched.
    private void TryOpenBlock(KeyEventArgs e)
    {
        var doc = _editor.Document;
        if (doc is null) return;
        // A selection means Enter REPLACES it — ordinary editing, never ours.
        if (_editor.SelectionLength > 0) return;

        // The block's indent is the FORMATTER's, decided in Core — not the editor's tab settings — so the
        // generated block already matches what Alt+F would produce. The newline is the one thing only the
        // App knows.
        var edit = KeywordPairing.OnNewLine(doc.Text, _editor.CaretOffset, NewLineAtCaret(doc));
        if (edit is null) return;
        Apply(doc, edit);
        e.Handled = true;
    }

    private void Apply(TextDocument doc, PairEdit edit)
    {
        if (edit.Start < 0 || edit.Start + edit.Length > doc.TextLength) return; // stale caret guard
        doc.Replace(edit.Start, edit.Length, edit.InsertText);
        _editor.CaretOffset = edit.Start + edit.CaretOffset;
    }

    private string NewLineAtCaret(TextDocument doc)
        => TextUtilities.GetNewLineFromDocument(doc, doc.GetLineByOffset(_editor.CaretOffset).LineNumber);
}
