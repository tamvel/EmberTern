using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using EmberTern.Core.Sql;

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
    private readonly string? _columnType;

    public SqlCompletionData(string text, SqlCompletionKind kind, string? description = null, string? columnType = null)
    {
        Text = text;
        Kind = kind;
        _columnType = columnType;
        _description = description ?? DescribeKind(kind);
        // Two-column IBExpert-style display: kind label on the left, name (+ optional
        // ": TYPE" suffix for columns) on the right. Column widths must match across
        // all entries so they line up — fixed-width 90 for the kind column.
        Content = BuildContent(text, kind, columnType);
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
        // Case-preserving insert: read what the user already typed in the
        // completionSegment and shape the inserted text to match (all-lower /
        // all-upper / verbatim). The catalog stores Firebird names uppercase,
        // but lowercase-everywhere is the IBExpert default the user works in.
        var typedPrefix = textArea.Document.GetText(completionSegment);
        var insert = CaseMatcher.Match(typedPrefix, Text);
        textArea.Document.Replace(completionSegment, insert);
    }

    private static Control BuildContent(string text, SqlCompletionKind kind, string? columnType)
    {
        var rightText = kind == SqlCompletionKind.Column && !string.IsNullOrEmpty(columnType)
            ? text + " : " + columnType
            : text;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*"),
        };
        var kindLabel = new TextBlock
        {
            Text = DescribeKind(kind),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6,
        };
        var nameLabel = new TextBlock
        {
            Text = rightText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(kindLabel, 0);
        Grid.SetColumn(nameLabel, 1);
        grid.Children.Add(kindLabel);
        grid.Children.Add(nameLabel);
        return grid;
    }

    private static string DescribeKind(SqlCompletionKind kind) => kind switch
    {
        SqlCompletionKind.Keyword => "Keyword",
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
        SqlCompletionKind.Column => "Field",
        _ => string.Empty,
    };
}
