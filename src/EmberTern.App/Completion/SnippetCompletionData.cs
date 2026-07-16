using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Snippets;
using EmberTern.Core.Sql.Language.Snippets;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Completion;

/// <summary>
/// A completion-list item for a keyword live template (Etap 5 / M8). Filtered by its trigger
/// keyword like any other item; on accept it replaces the typed prefix with the expanded snippet and
/// activates <b>Tab-between-stops</b> navigation via an AvaloniaEdit <see cref="Snippet"/> (built
/// from the Core <see cref="SqlSnippet"/>'s placeholder offsets). §0: expansion only inserts text —
/// it never rewrites surrounding code.
/// </summary>
internal sealed class SnippetCompletionData : ICompletionData
{
    private readonly SnippetTemplate _template;

    public SnippetCompletionData(SnippetTemplate template)
    {
        _template = template;
        Text = template.Keyword;
        Content = BuildContent(template.DisplayText);
    }

    public IImage? Image => null;

    /// <summary>The trigger keyword — the completion filter key and the segment replaced on accept.</summary>
    public string Text { get; }

    public object Content { get; }
    public object Description => _template.DisplayText;

    // Just above keywords (1.0) so an "if" template sits near the IF keyword; below schema objects
    // so typing a table prefix still surfaces the table first.
    public double Priority => 1.5;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        // Replace the typed prefix with the expanded snippet, then hand off to AvaloniaEdit's
        // interactive snippet so Tab cycles the replaceable stops.
        textArea.Document.Remove(completionSegment.Offset, completionSegment.Length);
        textArea.Caret.Offset = completionSegment.Offset;
        ToEditorSnippet(_template.Create()).Insert(textArea);
    }

    // Splits the snippet text at its placeholder offsets into literal text elements and replaceable
    // (Tab-stop) elements. Placeholders are taken in source order.
    private static Snippet ToEditorSnippet(SqlSnippet snippet)
    {
        var editorSnippet = new Snippet();
        var text = snippet.Text;
        int pos = 0;
        foreach (var ph in snippet.Placeholders.OrderBy(p => p.Start))
        {
            if (ph.Start > pos)
            {
                editorSnippet.Elements.Add(new SnippetTextElement { Text = text.Substring(pos, ph.Start - pos) });
            }
            editorSnippet.Elements.Add(new SnippetReplaceableTextElement { Text = text.Substring(ph.Start, ph.Length) });
            pos = ph.Start + ph.Length;
        }
        if (pos < text.Length)
        {
            editorSnippet.Elements.Add(new SnippetTextElement { Text = text.Substring(pos) });
        }
        return editorSnippet;
    }

    private static Control BuildContent(string display)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
        var kindLabel = new TextBlock { Text = "Snippet", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
        var nameLabel = new TextBlock { Text = display, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(kindLabel, 0);
        Grid.SetColumn(nameLabel, 1);
        grid.Children.Add(kindLabel);
        grid.Children.Add(nameLabel);
        return grid;
    }
}
