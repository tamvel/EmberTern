using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Completion;

/// <summary>
/// What a <see cref="CompletionItem"/> denotes — a keyword, a schema object (by kind), a column, or
/// a local declaration in scope (alias / variable / parameter / CTE / cursor / NEW-OLD record). One
/// flat discriminator so the App maps it to its display kind with a single switch. Mirrors the
/// <see cref="SymbolKind"/> set plus <see cref="Keyword"/>.
/// </summary>
public enum CompletionItemKind
{
    Keyword,

    // Schema objects
    Table,
    View,
    SystemTable,
    Procedure,
    Function,
    Trigger,
    Domain,
    Exception,
    Sequence,
    Role,
    Package,
    Index,

    /// <summary>A column of a table/view (dot completion, M3).</summary>
    Column,

    // Local declarations in scope
    /// <summary>A FROM/JOIN alias — the <c>k</c> in <c>FROM KONTRAHENT k</c>.</summary>
    TableAlias,
    Variable,
    Parameter,
    Cte,
    Cursor,

    /// <summary>A trigger <c>NEW</c>/<c>OLD</c> record.</summary>
    RecordAlias,

    Unknown,
}

/// <summary>How the completion was triggered — shapes ranking/behavior, not the item set.</summary>
public enum CompletionTrigger
{
    /// <summary>Ctrl+Space — the user explicitly asked; always produce a list.</summary>
    Explicit,

    /// <summary>Auto-trigger while typing an identifier.</summary>
    Identifier,

    /// <summary>Just after a <c>.</c> — a request for the qualifier's columns (M3).</summary>
    Dot,
}

/// <summary>
/// One completion suggestion. <see cref="InsertText"/> is what gets inserted (the App applies
/// case-preservation), <see cref="DisplayText"/> is what the list shows, <see cref="Kind"/> drives
/// the glyph/section, <see cref="SortPriority"/> orders ties (higher = earlier), <see cref="Detail"/>
/// carries an optional extra (a column's type, for the <c>: TYPE</c> suffix), and <see cref="Symbol"/>
/// optionally carries the resolved <see cref="Semantics.Symbol"/> the item denotes — a rich
/// <see cref="Semantics.ColumnSymbol"/> for a dot-completion column (type + domain + nullability + …),
/// so the App can render the domain in the row and the full facts in the detail pane from one source
/// without a second lookup or a duplicated model (Etap 6 P2). <c>null</c> for keyword/object items
/// (the App synthesises those). Pure value — no Avalonia.
/// </summary>
public sealed record CompletionItem(
    string InsertText,
    string DisplayText,
    CompletionItemKind Kind,
    double SortPriority,
    string? Detail = null,
    Semantics.Symbol? Symbol = null);

/// <summary>The result of a completion query — the ordered candidate items. Ordering is by
/// <see cref="CompletionItem.SortPriority"/> desc then name; the App's completion list may re-sort,
/// but the priority breaks ties on equal-prefix matches.
/// <para>
/// When the caret is a <b>dot/qualifier</b> position (<see cref="IsDotContext"/>), the result is
/// scoped to columns: <see cref="DotTargetTable"/> is the table the qualifier resolved to (or null
/// when the qualifier could not be resolved). This lets the App column-warm a resolved-but-uncached
/// target (empty <see cref="Items"/> + non-null <see cref="DotTargetTable"/>) and avoid falling back
/// to keywords in a dot context (§22 / M5).
/// </para>
/// </summary>
public sealed class CompletionResult
{
    /// <summary>An empty non-dot result (no candidates).</summary>
    public static readonly CompletionResult Empty = new(System.Array.Empty<CompletionItem>());

    public CompletionResult(
        IReadOnlyList<CompletionItem> items,
        bool isDotContext = false,
        string? dotTargetTable = null)
    {
        Items = items;
        IsDotContext = isDotContext;
        DotTargetTable = dotTargetTable;
    }

    /// <summary>The candidate items, most-relevant first.</summary>
    public IReadOnlyList<CompletionItem> Items { get; }

    /// <summary>True when there are no candidates.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>True when the caret is a qualifier-dot position — the items are (or should be) the
    /// qualifier's columns, not the baseline list.</summary>
    public bool IsDotContext { get; }

    /// <summary>In a dot context, the table/view whose columns <see cref="Items"/> holds; null when
    /// the qualifier could not be resolved. The App warms this table's columns on an empty result.</summary>
    public string? DotTargetTable { get; }
}
