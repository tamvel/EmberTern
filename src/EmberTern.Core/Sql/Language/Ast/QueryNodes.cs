using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

// The concrete query nodes — Etap 6.9 milestones B2 (clause skeleton) + B3 (recursive query model). They
// model the STRUCTURE of a SELECT query — its clauses (SELECT/FROM/WHERE/GROUP BY/HAVING/ORDER BY), the
// FROM list with its join structure, set operations, and every nested query (a derived table's body, an
// EXISTS / scalar subquery, a CTE body, a WITH main query) — so the binder, formatter, diagnostics,
// folding, breadcrumbs and the debugger read the shape from the tree instead of each re-scanning tokens.
//
// Depth = STRUCTURAL depth (design principle #2). A clause's INTERIOR (a projection item's expression, a
// WHERE predicate, an ORDER BY term, a join's ON condition) stays in the owning node's Tokens — ordinary
// arithmetic/boolean expressions are opaque token fragments by design. Only STRUCTURALLY MEANINGFUL
// constructs become nodes: a query, its clauses, its FROM/join structure, a set operation, a WITH/CTE, a
// derived table, an EXISTS, a scalar subquery. Because a query can nest a query (a subquery holds a real
// QueryNode), B3 makes the model FULLY RECURSIVE: a QueryNode may contain QueryNodes to any depth, with no
// special-casing and no separate machinery.
//
// §0: every node is a structural overlay on the lossless token stream (the byte-for-byte round-trip comes
// from SqlScript.Tokens, never from these overlays), and every node's span is TokenSpan of the exact range
// it covers — so child spans always nest inside their parent and appear in source order (pinned by
// StructuralAstDifferentialTests). Anything the producer cannot cleanly model becomes a RawQuery (the
// query-level §0 valve) or is left unmodeled, never lost.

/// <summary>A set-operation operator joining two queries.</summary>
public enum SetOperator
{
    /// <summary><c>UNION</c>.</summary>
    Union,
    /// <summary><c>INTERSECT</c>.</summary>
    Intersect,
    /// <summary><c>EXCEPT</c>.</summary>
    Except,
}

/// <summary>The kind of a <see cref="JoinedTable"/> join.</summary>
public enum JoinKind
{
    /// <summary><c>[INNER] JOIN</c>.</summary>
    Inner,
    /// <summary><c>LEFT [OUTER] JOIN</c>.</summary>
    Left,
    /// <summary><c>RIGHT [OUTER] JOIN</c>.</summary>
    Right,
    /// <summary><c>FULL [OUTER] JOIN</c>.</summary>
    Full,
    /// <summary><c>CROSS JOIN</c>.</summary>
    Cross,
    /// <summary><c>NATURAL [kind] JOIN</c>.</summary>
    Natural,
}

// ── Clauses ────────────────────────────────────────────────────────────────────────────────────

/// <summary>The abstract base of a query clause — one <c>SELECT</c> / <c>FROM</c> / <c>WHERE</c> /
/// <c>GROUP BY</c> / <c>HAVING</c> / <c>ORDER BY</c> segment of a <see cref="SelectQuery"/>. It carries
/// its <see cref="Tokens"/> slice (leading keyword included) so a consumer reads the clause's content
/// without re-scanning; the clause interior is an ordinary/expression fragment kept in those tokens
/// (structural-depth boundary). Its <see cref="Children"/> are the structurally-meaningful nested nodes
/// it contains — the embedded subquery expressions (<see cref="ExistsExpression"/> /
/// <see cref="ScalarSubquery"/>) for a predicate/projection clause, or the <see cref="FromItem"/>s for a
/// <see cref="FromClause"/>.</summary>
public abstract class QueryClause : SqlNode
{
    private readonly IReadOnlyList<SqlNode> _children;

    private protected QueryClause(
        int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? children)
        : base(start, length)
    {
        Tokens = tokens;
        _children = children ?? Array.Empty<SqlNode>();
    }

    /// <summary>The clause's significant tokens (its leading keyword through its last token).</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>The <c>SELECT [FIRST n] [SKIP n] [DISTINCT|ALL] &lt;projection&gt;</c> clause. Projection
/// items stay in <see cref="QueryClause.Tokens"/> (ordinary expressions are token fragments); a scalar
/// subquery embedded in a projection item is a <see cref="ScalarSubquery"/> child (B3).</summary>
public sealed class SelectClause : QueryClause
{
    public SelectClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? subqueries = null)
        : base(start, length, tokens, subqueries) { }
}

/// <summary>The <c>FROM &lt;item&gt; [, &lt;item&gt;]*</c> clause — a list of <see cref="FromItem"/>s
/// (comma-separated top-level entries; JOINs nest inside a single item as a <see cref="JoinedTable"/>).</summary>
public sealed class FromClause : QueryClause
{
    public FromClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<FromItem> items)
        : base(start, length, tokens, items)
    {
        Items = items;
    }

    /// <summary>The top-level FROM entries, in source order.</summary>
    public IReadOnlyList<FromItem> Items { get; }
}

/// <summary>The <c>WHERE &lt;predicate&gt;</c> clause. The predicate stays in
/// <see cref="QueryClause.Tokens"/>; embedded <see cref="ExistsExpression"/> / <see cref="ScalarSubquery"/>
/// nodes are children (B3).</summary>
public sealed class WhereClause : QueryClause
{
    public WhereClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? subqueries = null)
        : base(start, length, tokens, subqueries) { }
}

/// <summary>The <c>GROUP BY &lt;terms&gt;</c> clause (terms in <see cref="QueryClause.Tokens"/>; any
/// embedded subquery is a child).</summary>
public sealed class GroupByClause : QueryClause
{
    public GroupByClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? subqueries = null)
        : base(start, length, tokens, subqueries) { }
}

/// <summary>The <c>HAVING &lt;predicate&gt;</c> clause (predicate in <see cref="QueryClause.Tokens"/>;
/// embedded subqueries are children).</summary>
public sealed class HavingClause : QueryClause
{
    public HavingClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? subqueries = null)
        : base(start, length, tokens, subqueries) { }
}

/// <summary>The <c>ORDER BY &lt;terms&gt;</c> clause (terms in <see cref="QueryClause.Tokens"/>; embedded
/// subqueries are children). On a plain query it is a child of the <see cref="SelectQuery"/>; on a set
/// operation it applies to the whole and is a child of the <see cref="SetOperationQuery"/>.</summary>
public sealed class OrderByClause : QueryClause
{
    public OrderByClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? subqueries = null)
        : base(start, length, tokens, subqueries) { }
}

// ── FROM items ─────────────────────────────────────────────────────────────────────────────────

/// <summary>The abstract base of a <c>FROM</c> entry — a table reference, a derived table, or a join.
/// Carries its <see cref="Tokens"/> slice.</summary>
public abstract class FromItem : SqlNode
{
    private protected FromItem(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length) => Tokens = tokens;

    /// <summary>The item's significant tokens.</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Array.Empty<SqlNode>();
}

/// <summary>A named table reference — <c>[schema.]table [[AS] alias]</c>.</summary>
public class TableReference : FromItem
{
    public TableReference(
        int start, int length, IReadOnlyList<SqlToken> tokens, SqlToken? nameToken, SqlToken? aliasToken)
        : base(start, length, tokens)
    {
        NameToken = nameToken;
        AliasToken = aliasToken;
    }

    /// <summary>The table-name token (the last segment of a dotted name), or null when unreadable.</summary>
    public SqlToken? NameToken { get; }

    /// <summary>The alias token (<c>[AS] alias</c>), or null when the entry has no alias.</summary>
    public SqlToken? AliasToken { get; }
}

/// <summary>
/// A <b>selectable procedure invoked where a table would stand</b> — <c>… FROM MY_PROC(a, b) [[AS] alias]</c>.
///
/// <para>⭐⭐ <b>This node is the fix for a defect that was patched twice at the wrong layer.</b> The parser used
/// to read the routine's name, then go straight to the alias — so the argument list was not merely unmodelled,
/// it was <i>dropped</i>: a <see cref="TableReference"/> for <c>rap(:a, :b) r</c> carried the single token
/// <c>rap</c> and neither the arguments nor the alias. Consumers that needed the arguments therefore re-scanned
/// the SQL text, once per syntax, and each syntax not yet re-scanned silently did nothing (user report
/// 2026-08-03: parameter types read "Unknown" for every selectable procedure, then for <c>FOR SELECT</c>, then
/// for <c>INSERT … SELECT</c>). ⛔ The lesson is Contract #1: when a consumer starts token-scanning for
/// structure, the structure belongs in the parser.</para>
///
/// <para>⚠ A SUBCLASS of <see cref="TableReference"/>, deliberately: every existing consumer matches
/// <c>is TableReference</c> to resolve the name against the catalog (a selectable procedure in <c>FROM</c> is
/// how the binder colours and navigates it today), and those must keep working untouched. The new capability is
/// purely additive — exactly the additive-AST-deepening rule Etap 6.9 follows.</para>
///
/// <para>⚠ Firebird admits <c>FROM MY_PROC</c> with no parentheses for a no-argument selectable procedure, which
/// is indistinguishable from a table at parse time. That stays a plain <see cref="TableReference"/> — a name
/// alone is not evidence of an invocation, and guessing would make every table look like a call.</para>
/// </summary>
public sealed class RoutineTableReference : TableReference, IRoutineInvocation
{
    public RoutineTableReference(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        SqlToken? nameToken,
        SqlToken? aliasToken,
        string? routineName,
        string? packageName,
        IReadOnlyList<CallArgument> arguments)
        : base(start, length, tokens, nameToken, aliasToken)
    {
        RoutineName = routineName;
        PackageName = packageName;
        Arguments = arguments;
    }

    /// <inheritdoc />
    public string? RoutineName { get; }

    /// <inheritdoc />
    public string? PackageName { get; }

    /// <inheritdoc />
    public IReadOnlyList<CallArgument> Arguments { get; }
}

/// <summary>A derived table — <c>( &lt;subquery&gt; ) [[AS] alias]</c>. B3 recurses: the subquery is a real
/// <see cref="Query"/> (<see cref="QueryNode"/>) child, so nesting (and nested CTEs) fall out of the tree.
/// The subquery text also lives in <see cref="FromItem.Tokens"/> (§0).</summary>
public sealed class DerivedTable : FromItem
{
    public DerivedTable(
        int start, int length, IReadOnlyList<SqlToken> tokens, QueryNode? query, SqlToken? aliasToken)
        : base(start, length, tokens)
    {
        Query = query;
        AliasToken = aliasToken;
    }

    /// <summary>The derived table's inner query (B3). Null only when the parens held no recognisable
    /// query (§0-safe; the tokens still round-trip).</summary>
    public QueryNode? Query { get; }

    /// <summary>The alias token (<c>[AS] alias</c>), or null when the entry has no alias.</summary>
    public SqlToken? AliasToken { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children =>
        Query is null ? Array.Empty<SqlNode>() : new SqlNode[] { Query };
}

/// <summary>A join — <c>&lt;left&gt; &lt;kind&gt; JOIN &lt;right&gt; [ON &lt;cond&gt; | USING (…)]</c>.
/// Left-associative, so <c>a JOIN b JOIN c</c> nests as <c>(a JOIN b) JOIN c</c>. The <c>ON</c> condition
/// stays in <see cref="OnTokens"/> (an expression fragment); any subquery embedded in it is a child
/// (after <see cref="Left"/>/<see cref="Right"/>, in source order).</summary>
public sealed class JoinedTable : FromItem
{
    private readonly SqlNode[] _children;

    public JoinedTable(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        FromItem left,
        JoinKind kind,
        FromItem right,
        IReadOnlyList<SqlToken>? onTokens,
        IReadOnlyList<SqlNode>? onSubqueries = null)
        : base(start, length, tokens)
    {
        Left = left;
        Kind = kind;
        Right = right;
        OnTokens = onTokens;

        if (onSubqueries is { Count: > 0 })
        {
            _children = new SqlNode[2 + onSubqueries.Count];
            _children[0] = left;
            _children[1] = right;
            for (int i = 0; i < onSubqueries.Count; i++) _children[2 + i] = onSubqueries[i];
        }
        else
        {
            _children = new SqlNode[] { left, right };
        }
    }

    /// <summary>The left-hand table/join.</summary>
    public FromItem Left { get; }

    /// <summary>The join kind.</summary>
    public JoinKind Kind { get; }

    /// <summary>The right-hand table/derived table.</summary>
    public FromItem Right { get; }

    /// <summary>The <c>ON &lt;condition&gt;</c> (or <c>USING (…)</c>) tokens, or null for a CROSS/NATURAL
    /// join that has no join predicate.</summary>
    public IReadOnlyList<SqlToken>? OnTokens { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

// ── Structural expression nodes (embedded subqueries) ────────────────────────────────────────────

/// <summary>The abstract base of a structurally-meaningful subquery expression embedded in a clause —
/// an <see cref="ExistsExpression"/> or a <see cref="ScalarSubquery"/>. It owns a real
/// <see cref="Query"/> (<see cref="QueryNode"/>) so the recursion is uniform; the surrounding ordinary
/// expression stays a token fragment on the owning clause (structural-depth boundary).</summary>
public abstract class SubqueryExpression : SqlNode
{
    private protected SubqueryExpression(int start, int length, IReadOnlyList<SqlToken> tokens, QueryNode? query)
        : base(start, length)
    {
        Tokens = tokens;
        Query = query;
    }

    /// <summary>The expression's significant tokens.</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <summary>The embedded query. Null only when the parens held no recognisable query (§0-safe).</summary>
    public QueryNode? Query { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children =>
        Query is null ? Array.Empty<SqlNode>() : new SqlNode[] { Query };
}

/// <summary>An <c>EXISTS ( &lt;subquery&gt; )</c> predicate. Spans the <c>EXISTS</c> keyword through the
/// closing paren; owns the subquery as a <see cref="SubqueryExpression.Query"/> child (B3).</summary>
public sealed class ExistsExpression : SubqueryExpression
{
    public ExistsExpression(int start, int length, IReadOnlyList<SqlToken> tokens, QueryNode? query)
        : base(start, length, tokens, query) { }
}

/// <summary>A subquery embedded in an expression position that is NOT an <c>EXISTS</c> — a scalar
/// comparison subquery (<c>x = (SELECT …)</c>), an <c>IN (SELECT …)</c>, or a quantified
/// <c>= ANY/ALL (SELECT …)</c>. Spans the <c>( … )</c>; owns the subquery as a
/// <see cref="SubqueryExpression.Query"/> child (B3). (The name follows the design doc; structurally all
/// of these are "a query nested inside an expression".)</summary>
public sealed class ScalarSubquery : SubqueryExpression
{
    public ScalarSubquery(int start, int length, IReadOnlyList<SqlToken> tokens, QueryNode? query)
        : base(start, length, tokens, query) { }
}

// ── Queries ────────────────────────────────────────────────────────────────────────────────────

/// <summary>A single <c>SELECT</c> query with its clauses — the concrete <see cref="QueryNode"/> a plain
/// (non-set-operation, non-<c>WITH</c>) query resolves to. Any clause but <see cref="Select"/> may be null
/// (absent). Children are the non-null clauses in source order.</summary>
public sealed class SelectQuery : QueryNode
{
    private readonly SqlNode[] _children;

    public SelectQuery(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        SelectClause select,
        FromClause? from,
        WhereClause? where,
        GroupByClause? groupBy,
        HavingClause? having,
        OrderByClause? orderBy)
        : base(start, length, tokens)
    {
        Select = select;
        From = from;
        Where = where;
        GroupBy = groupBy;
        Having = having;
        OrderBy = orderBy;

        var kids = new List<SqlNode>(6) { select };
        if (from is not null) kids.Add(from);
        if (where is not null) kids.Add(where);
        if (groupBy is not null) kids.Add(groupBy);
        if (having is not null) kids.Add(having);
        if (orderBy is not null) kids.Add(orderBy);
        _children = kids.ToArray();
    }

    /// <summary>The <c>SELECT</c> clause (always present for a recognised query).</summary>
    public SelectClause Select { get; }

    /// <summary>The <c>FROM</c> clause, or null (a FROM-less <c>SELECT :x</c> in PSQL, etc.).</summary>
    public FromClause? From { get; }

    /// <summary>The <c>WHERE</c> clause, or null.</summary>
    public WhereClause? Where { get; }

    /// <summary>The <c>GROUP BY</c> clause, or null.</summary>
    public GroupByClause? GroupBy { get; }

    /// <summary>The <c>HAVING</c> clause, or null.</summary>
    public HavingClause? Having { get; }

    /// <summary>The <c>ORDER BY</c> clause, or null.</summary>
    public OrderByClause? OrderBy { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>A set operation joining two queries — <c>&lt;left&gt; UNION|INTERSECT|EXCEPT [ALL]
/// &lt;right&gt; [ORDER BY …]</c>. Left-associative, so <c>a UNION b UNION c</c> nests as
/// <c>(a UNION b) UNION c</c>. A trailing <see cref="OrderBy"/> applies to the whole set and hangs on the
/// outermost node.</summary>
public sealed class SetOperationQuery : QueryNode
{
    private readonly SqlNode[] _children;

    public SetOperationQuery(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        QueryNode left,
        SetOperator @operator,
        bool all,
        QueryNode right,
        OrderByClause? orderBy)
        : base(start, length, tokens)
    {
        Left = left;
        Operator = @operator;
        All = all;
        Right = right;
        OrderBy = orderBy;

        _children = orderBy is null
            ? new SqlNode[] { left, right }
            : new SqlNode[] { left, right, orderBy };
    }

    /// <summary>The left-hand query.</summary>
    public QueryNode Left { get; }

    /// <summary>The set operator.</summary>
    public SetOperator Operator { get; }

    /// <summary><c>ALL</c> (keep duplicates) vs the default <c>DISTINCT</c>.</summary>
    public bool All { get; }

    /// <summary>The right-hand query.</summary>
    public QueryNode Right { get; }

    /// <summary>The trailing <c>ORDER BY</c> applying to the whole set operation, or null.</summary>
    public OrderByClause? OrderBy { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>A <c>WITH [RECURSIVE] &lt;cte-list&gt; &lt;main-query&gt;</c> query (B3). The CTE declarations
/// live in <see cref="With"/> (a <see cref="WithClause"/> whose CTE bodies are real queries); the main
/// query that consumes them is a real <see cref="Query"/> (<see cref="QueryNode"/>). Because a CTE body or
/// the main query may itself be a <c>WithQuery</c>, nested CTEs recurse with no special handling. This is
/// the single representation of a WITH query everywhere it appears — the top-level statement, a CTE body,
/// a derived table, a subquery.</summary>
public sealed class WithQuery : QueryNode
{
    private readonly SqlNode[] _children;

    public WithQuery(int start, int length, IReadOnlyList<SqlToken> tokens, WithClause with, QueryNode query)
        : base(start, length, tokens)
    {
        With = with;
        Query = query;
        _children = new SqlNode[] { with, query };
    }

    /// <summary>The <c>WITH</c> clause (the CTE declarations).</summary>
    public WithClause With { get; }

    /// <summary>The main query that follows the CTE list.</summary>
    public QueryNode Query { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>The query-level §0 valve — a query range the producer could not model as a
/// <see cref="SelectQuery"/> / <see cref="SetOperationQuery"/> / <see cref="WithQuery"/> (e.g. a mid-typed
/// or unusual fragment). It reproduces its exact source range through <see cref="QueryNode.Tokens"/> and
/// has no children; the analogue of <see cref="RawStatement"/> / <see cref="PsqlLeafStatement"/> at the
/// query level, so a nested-query slot is never left null when a consumer needs the text.</summary>
public sealed class RawQuery : QueryNode
{
    public RawQuery(int start, int length, IReadOnlyList<SqlToken> tokens) : base(start, length, tokens) { }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Array.Empty<SqlNode>();
}
