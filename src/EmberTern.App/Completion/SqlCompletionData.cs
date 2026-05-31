using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace EmberTern.App.Completion;

// Categories the editor distinguishes for its completion list. Each maps to a
// (single-char glyph, priority) pair — keywords sort below schema objects so
// typing the start of a table name surfaces the table first.
public enum SqlCompletionKind
{
    Keyword,
    Table,
    View,
    Procedure,
    Function,
    Trigger,
    Generator,
    Domain,
    Exception,
    Package,
    Role,
    Index,
    Column,
}

internal sealed class SqlCompletionData : ICompletionData
{
    private readonly string _description;

    public SqlCompletionData(string text, SqlCompletionKind kind, string? description = null)
    {
        Text = text;
        Kind = kind;
        // Sub-content stack would let us render a glyph + label; for now keep the
        // label plain. CompletionList honours Content; Description shows in the
        // tooltip pane next to the list.
        Content = text;
        _description = description ?? DescribeKind(kind);
    }

    public IImage? Image => null;
    public string Text { get; }
    public object Content { get; }
    public object Description => _description;
    public SqlCompletionKind Kind { get; }
    // Schema objects beat keywords on ties; columns beat tables (Step 2). Higher
    // priority sorts earlier in the list when CompletionList enables priority
    // ordering — AvaloniaEdit's default sort is alphabetical, so the real
    // effect of this number is to break ties on equal-prefix matches.
    public double Priority => Kind switch
    {
        SqlCompletionKind.Column => 4.0,
        SqlCompletionKind.Table => 3.0,
        SqlCompletionKind.View => 3.0,
        SqlCompletionKind.Procedure => 3.0,
        SqlCompletionKind.Function => 2.5,
        SqlCompletionKind.Trigger => 2.0,
        SqlCompletionKind.Generator => 2.0,
        SqlCompletionKind.Domain => 2.0,
        SqlCompletionKind.Exception => 2.0,
        SqlCompletionKind.Package => 2.0,
        SqlCompletionKind.Role => 2.0,
        SqlCompletionKind.Index => 2.0,
        SqlCompletionKind.Keyword => 1.0,
        _ => 0.0,
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }

    private static string DescribeKind(SqlCompletionKind kind) => kind switch
    {
        SqlCompletionKind.Keyword => "SQL keyword",
        SqlCompletionKind.Table => "Table",
        SqlCompletionKind.View => "View",
        SqlCompletionKind.Procedure => "Procedure",
        SqlCompletionKind.Function => "Function",
        SqlCompletionKind.Trigger => "Trigger",
        SqlCompletionKind.Generator => "Generator",
        SqlCompletionKind.Domain => "Domain",
        SqlCompletionKind.Exception => "Exception",
        SqlCompletionKind.Package => "Package",
        SqlCompletionKind.Role => "Role",
        SqlCompletionKind.Index => "Index",
        SqlCompletionKind.Column => "Column",
        _ => string.Empty,
    };
}
