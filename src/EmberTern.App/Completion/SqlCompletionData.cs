using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using EmberTern.App.Controls;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Completion;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

// Categories the editor distinguishes for its completion list. Each maps to a
// (kind label, priority) pair — keywords sort below schema objects so typing the
// start of a table name surfaces the table first. Extends the schema-object set
// with the local-scope kinds the CompletionEngine now surfaces (M5): FROM/JOIN
// aliases, PSQL variables/parameters, CTEs, cursors, NEW/OLD records.
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
    Alias,
    Variable,
    Parameter,
    Cte,
    Cursor,
    Record,
}

internal sealed class SqlCompletionData : ICompletionData
{
    /// <summary>Row text size — smaller than the editor default so the list reads light (P2).</summary>
    private const double RowFontSize = 12;

    private readonly string _description;
    private readonly string? _columnType;
    // Lazily builds the rich Quick Info detail pane (Etap 6 / M5) for THIS item when the
    // completion list selects it. Null → the plain string fallback. Built fresh on each
    // access (AvaloniaEdit only reads Description for the selected item), so a new control
    // is never re-parented, and it always matches the current theme.
    private readonly Func<object?>? _detailFactory;

    public SqlCompletionData(
        string text,
        SqlCompletionKind kind,
        string? description = null,
        string? columnType = null,
        string? columnDomain = null,
        Func<object?>? detailFactory = null)
    {
        Text = text;
        Kind = kind;
        _columnType = columnType;
        _description = description ?? DescribeKind(kind);
        _detailFactory = detailFactory;
        // Modern (VS/Rider-style) row: a per-kind icon (reusing the tree/semantic palette) + the
        // name + a subtle ": TYPE : DOMAIN" for columns (P2). The icon conveys the kind, so no
        // fixed-width text kind column is needed — the list reads lighter and more compact.
        Content = BuildContent(text, kind, columnType, columnDomain);
    }

    /// <summary>
    /// Builds a completion entry from a Core <see cref="CompletionItem"/> (M5). The
    /// engine already ordered/ranked the items; the App only maps the kind to a
    /// display label and shows a column's type as the ": TYPE" suffix. Insertion
    /// text is <see cref="CompletionItem.InsertText"/> (the catalog name), which
    /// <see cref="Complete"/> case-shapes to what the user typed.
    /// <para>
    /// <paramref name="detailFactory"/> (Etap 6 / M5) lazily supplies the Quick Info
    /// detail pane for the selected item — the same <c>QuickInfoView</c> the Ctrl-hover
    /// tooltip uses, so the two surfaces read identically.
    /// </para>
    /// </summary>
    public static SqlCompletionData FromItem(CompletionItem item, Func<object?>? detailFactory = null)
    {
        var kind = MapKind(item.Kind);
        // Column rows show ": TYPE : DOMAIN"; the type is the item detail, the domain comes from the
        // rich ColumnSymbol the engine attached (P2). The full facts live in the detail pane.
        string? columnType = null, columnDomain = null;
        if (kind == SqlCompletionKind.Column)
        {
            columnType = item.Detail;
            columnDomain = (item.Symbol as ColumnSymbol)?.Domain;
        }
        return new SqlCompletionData(
            item.InsertText, kind, columnType: columnType, columnDomain: columnDomain, detailFactory: detailFactory);
    }

    /// <summary>Pure mapping from the Core completion kind to the editor's display kind.
    /// No Avalonia — unit-testable on its own.</summary>
    public static SqlCompletionKind MapKind(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Keyword => SqlCompletionKind.Keyword,
        CompletionItemKind.Table => SqlCompletionKind.Table,
        CompletionItemKind.View => SqlCompletionKind.View,
        CompletionItemKind.SystemTable => SqlCompletionKind.Table,
        CompletionItemKind.Procedure => SqlCompletionKind.Procedure,
        CompletionItemKind.Function => SqlCompletionKind.Function,
        CompletionItemKind.Trigger => SqlCompletionKind.Trigger,
        CompletionItemKind.Domain => SqlCompletionKind.Domain,
        CompletionItemKind.Exception => SqlCompletionKind.Exception,
        CompletionItemKind.Sequence => SqlCompletionKind.Generator,
        CompletionItemKind.Role => SqlCompletionKind.Role,
        CompletionItemKind.Package => SqlCompletionKind.Package,
        CompletionItemKind.Index => SqlCompletionKind.Index,
        CompletionItemKind.Column => SqlCompletionKind.Column,
        CompletionItemKind.TableAlias => SqlCompletionKind.Alias,
        CompletionItemKind.Variable => SqlCompletionKind.Variable,
        CompletionItemKind.Parameter => SqlCompletionKind.Parameter,
        CompletionItemKind.Cte => SqlCompletionKind.Cte,
        CompletionItemKind.Cursor => SqlCompletionKind.Cursor,
        CompletionItemKind.RecordAlias => SqlCompletionKind.Record,
        _ => SqlCompletionKind.Keyword,
    };

    public IImage? Image => null;
    public string Text { get; }
    public object Content { get; }
    // The detail pane AvaloniaEdit shows beside the list for the selected item. The rich
    // Quick Info card when a factory is supplied and yields one; otherwise the plain string.
    public object Description => _detailFactory?.Invoke() ?? _description;
    public SqlCompletionKind Kind { get; }
    // Schema objects beat keywords on ties; columns beat tables; in-scope locals
    // beat catalog objects. Higher priority sorts earlier when CompletionList
    // enables priority ordering — AvaloniaEdit's default sort is alphabetical, so
    // the real effect of this number is to break ties on equal-prefix matches.
    public double Priority => Kind switch
    {
        SqlCompletionKind.Column => 4.0,
        SqlCompletionKind.Alias => 3.5,
        SqlCompletionKind.Variable => 3.5,
        SqlCompletionKind.Parameter => 3.5,
        SqlCompletionKind.Cte => 3.5,
        SqlCompletionKind.Cursor => 3.5,
        SqlCompletionKind.Record => 3.5,
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
        // Case-preserving insert: shape the inserted text to match what the user already typed in
        // the completionSegment (all-lower / all-upper / verbatim). The catalog stores Firebird
        // names uppercase, but lowercase-everywhere is the IBExpert default the user works in.
        //
        // After a qualifier dot ("k.") that segment is EMPTY, so there is nothing to copy — the dot
        // used to read as the start of a fresh word and the catalog's UPPERCASE won, dropping
        // ID_KONTRAHENT into an all-lowercase query. So when the prefix carries no letters we fall
        // back to the user's ACTUAL style in this document rather than the preceding character.
        var typedPrefix = textArea.Document.GetText(completionSegment);
        var documentStyle = SqlCaseStyleDetector.Detect(textArea.Document.Text);
        var insert = CaseMatcher.Match(typedPrefix, Text, documentStyle);
        textArea.Document.Replace(completionSegment, insert);
    }

    private static Control BuildContent(string text, SqlCompletionKind kind, string? columnType, string? columnDomain)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Fixed-width icon slot so names align even when a kind has no icon (keywords).
        var iconSlot = new Border { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };
        var (geometryKey, colorKey) = IconFor(kind);
        if (geometryKey is not null && ResolveGeometry(geometryKey) is { } geometry)
        {
            iconSlot.Child = new SvgIcon
            {
                Data = geometry,
                Foreground = ResolveBrush(colorKey),
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }
        row.Children.Add(iconSlot);

        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = RowFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Columns: subtle ": TYPE : DOMAIN" (domain only when the column is domain-typed).
        if (kind == SqlCompletionKind.Column && !string.IsNullOrEmpty(columnType))
        {
            var detail = ": " + columnType;
            if (!string.IsNullOrEmpty(columnDomain)) detail += " : " + columnDomain;
            row.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = RowFontSize,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return row;
    }

    // The per-kind (icon geometry key, colour key). Reuses the metadata-tree / semantic-highlighting
    // palette so an object's completion icon matches its tree icon and its editor colour. Keywords
    // have no icon (the slot stays blank so names still align). Column → the calm column brush;
    // in-scope locals → the low-chroma local brush (same as the semantic layer).
    private static (string? GeometryKey, string ColorKey) IconFor(SqlCompletionKind kind) => kind switch
    {
        SqlCompletionKind.Table => ("Icon.Table", "IconColor_Table"),
        SqlCompletionKind.View => ("Icon.View", "IconColor_View"),
        SqlCompletionKind.Procedure => ("Icon.Procedure", "IconColor_Procedure"),
        SqlCompletionKind.Function => ("Icon.Function", "IconColor_Function"),
        SqlCompletionKind.Trigger => ("Icon.Trigger", "IconColor_Trigger"),
        SqlCompletionKind.Generator => ("Icon.Generator", "IconColor_Generator"),
        SqlCompletionKind.Domain => ("Icon.Domain", "IconColor_Domain"),
        SqlCompletionKind.Exception => ("Icon.Exception", "IconColor_Exception"),
        SqlCompletionKind.Package => ("Icon.Package", "IconColor_Package"),
        SqlCompletionKind.Role => ("Icon.Role", "IconColor_Role"),
        SqlCompletionKind.Index => ("Icon.Index", "IconColor_Index"),
        SqlCompletionKind.Column => ("Icon.Name", "EditorColumnBrush"),
        SqlCompletionKind.Alias or SqlCompletionKind.Variable or SqlCompletionKind.Parameter
            or SqlCompletionKind.Cte or SqlCompletionKind.Cursor or SqlCompletionKind.Record
            => ("Icon.Name", "EditorLocalBrush"),
        _ => (null, "SubtleForegroundBrush"), // Keyword — no icon
    };

    private static Geometry? ResolveGeometry(string key)
        => Application.Current?.Resources.TryGetResource(key, null, out var g) == true && g is Geometry geo
            ? geo
            : null;

    private static IBrush? ResolveBrush(string key)
    {
        var theme = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
        return Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b
            ? b
            : null;
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
        SqlCompletionKind.Alias => "Alias",
        SqlCompletionKind.Variable => "Variable",
        SqlCompletionKind.Parameter => "Parameter",
        SqlCompletionKind.Cte => "CTE",
        SqlCompletionKind.Cursor => "Cursor",
        SqlCompletionKind.Record => "Record",
        _ => string.Empty,
    };
}
