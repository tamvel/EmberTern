using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;
using EmberTern.Core.Sql.Language.Ergonomics;

namespace EmberTern.App.Completion;

/// <summary>
/// <b>Typing Ergonomics</b> — structural auto-indent (design §3.2), plugged into AvaloniaEdit's own
/// indentation seam so Enter keeps its ordinary behaviour and only its leading whitespace becomes smart.
/// A thin App shell: the rule is the pure Core <see cref="AutoIndent"/>, which indents at the formatter's
/// unit so typing never fights Alt+F.
/// <para>Overrides only <see cref="DefaultIndentationStrategy.IndentLine"/> — which copies the previous
/// line's indentation and so has no idea a <c>begin</c> opened a level or an <c>end</c> closed one.</para>
/// <para><b><see cref="DefaultIndentationStrategy.IndentLines"/> is deliberately NOT overridden</b>
/// (inherited unchanged). Re-indent-selection is a lightweight editing command, not a formatter: applying
/// this structural rule across a selection would flatten the formatter's parenthesis/column alignment,
/// i.e. it would slowly become a second, worse formatter. Formatter-quality indentation is what Alt+F is
/// for. Inheriting the base is how "leave it alone" stays true even if the base's behaviour changes.</para>
/// </summary>
internal sealed class SqlIndentationStrategy : DefaultIndentationStrategy
{
    public override void IndentLine(TextDocument document, DocumentLine line)
    {
        if (document is null || line is null) return;
        var indent = AutoIndent.ForLine(document.Text, line.Offset);
        var existing = TextUtilities.GetLeadingWhitespace(document, line);
        // Replace the line's existing indentation rather than prepending, so re-indenting is idempotent.
        if (existing.Length == indent.Length && document.GetText(existing) == indent) return;
        document.Replace(existing.Offset, existing.Length, indent);
    }
}
