using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// One common-table-expression — <c>name [ (cols) ] AS ( body )</c> — inside a <see cref="WithClause"/>.
/// A structural AST view so consumers (the formatter today; folding / breadcrumbs / deeper semantics
/// tomorrow) read the CTE shape from the tree instead of re-scanning tokens. The verbatim byte-for-byte
/// round-trip stays with the owning statement's token stream (§0); these nodes are additive structure,
/// they never replace the tokens.
/// </summary>
public sealed class CommonTableExpression : SqlNode
{
    public CommonTableExpression(
        int start,
        int length,
        SqlToken nameToken,
        IReadOnlyList<SqlToken>? columnTokens,
        IReadOnlyList<SqlToken> bodyTokens)
        : base(start, length)
    {
        NameToken = nameToken;
        ColumnTokens = columnTokens;
        BodyTokens = bodyTokens;
    }

    /// <summary>The CTE's name identifier token.</summary>
    public SqlToken NameToken { get; }

    /// <summary>The explicit column-list tokens BETWEEN the parens (excluding them) — <c>a , b</c> for
    /// <c>WITH c (a, b) AS …</c> — or <c>null</c> when the CTE declares no column list.</summary>
    public IReadOnlyList<SqlToken>? ColumnTokens { get; }

    /// <summary>The CTE body query tokens BETWEEN the <c>AS ( … )</c> parens (excluding them).</summary>
    public IReadOnlyList<SqlToken> BodyTokens { get; }

    public override IReadOnlyList<SqlNode> Children => Array.Empty<SqlNode>();
}

/// <summary>
/// A <c>WITH [RECURSIVE] cte [, cte]*</c> clause leading a query. Attached to the
/// <see cref="SelectStatement"/> it leads (a WITH query classifies as SELECT — design §5.4). The main
/// query that consumes the CTEs is kept as <see cref="MainQueryTokens"/> (statement-skeleton depth —
/// its interior is not deep-parsed, exactly like a plain SELECT).
/// </summary>
public sealed class WithClause : SqlNode
{
    public WithClause(
        int start,
        int length,
        bool isRecursive,
        IReadOnlyList<CommonTableExpression> ctes,
        IReadOnlyList<SqlToken> mainQueryTokens)
        : base(start, length)
    {
        IsRecursive = isRecursive;
        Ctes = ctes;
        MainQueryTokens = mainQueryTokens;
    }

    /// <summary><c>WITH RECURSIVE …</c>.</summary>
    public bool IsRecursive { get; }

    /// <summary>The declared CTEs, in source order.</summary>
    public IReadOnlyList<CommonTableExpression> Ctes { get; }

    /// <summary>The tokens of the main query that follows the CTE list (the SELECT / INSERT / … that
    /// references the CTEs), including a trailing <c>;</c> if present.</summary>
    public IReadOnlyList<SqlToken> MainQueryTokens { get; }

    public override IReadOnlyList<SqlNode> Children => Ctes;
}
