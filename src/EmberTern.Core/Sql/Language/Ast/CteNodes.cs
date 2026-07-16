using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// One common-table-expression — <c>name [ (cols) ] AS ( body )</c> — inside a <see cref="WithClause"/>.
/// B3 promotion: the body is a real <see cref="Body"/> query (<see cref="QueryNode"/>), not a token bag —
/// "the body of a CTE is just another query", so a CTE body that is itself a <c>WITH … SELECT …</c>
/// recurses as a nested <see cref="WithQuery"/> with no special handling. The verbatim byte-for-byte
/// round-trip stays with the owning statement's token stream (§0); this node is additive structure.
/// </summary>
public sealed class CommonTableExpression : SqlNode
{
    public CommonTableExpression(
        int start,
        int length,
        SqlToken nameToken,
        IReadOnlyList<SqlToken>? columnTokens,
        QueryNode body)
        : base(start, length)
    {
        NameToken = nameToken;
        ColumnTokens = columnTokens;
        Body = body;
    }

    /// <summary>The CTE's name identifier token.</summary>
    public SqlToken NameToken { get; }

    /// <summary>The explicit column-list tokens BETWEEN the parens (excluding them) — <c>a , b</c> for
    /// <c>WITH c (a, b) AS …</c> — or <c>null</c> when the CTE declares no column list.</summary>
    public IReadOnlyList<SqlToken>? ColumnTokens { get; }

    /// <summary>The CTE body query — the <c>SELECT …</c> (or nested <c>WITH …</c>) between the
    /// <c>AS ( … )</c> parens, modelled as a real <see cref="QueryNode"/> (B3). Its
    /// <see cref="QueryNode.Tokens"/> reproduce the exact body source range (§0).</summary>
    public QueryNode Body { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => new SqlNode[] { Body };
}

/// <summary>
/// A <c>WITH [RECURSIVE] cte [, cte]*</c> clause — the CTE declarations of a <see cref="WithQuery"/>.
/// B3: the main query that consumes the CTEs is no longer kept here as a token bag; it lives on the
/// owning <see cref="WithQuery.Query"/> as a real <see cref="QueryNode"/>, so this node models purely the
/// declarations (no parallel main-query representation).
/// </summary>
public sealed class WithClause : SqlNode
{
    public WithClause(int start, int length, bool isRecursive, IReadOnlyList<CommonTableExpression> ctes)
        : base(start, length)
    {
        IsRecursive = isRecursive;
        Ctes = ctes;
    }

    /// <summary><c>WITH RECURSIVE …</c>.</summary>
    public bool IsRecursive { get; }

    /// <summary>The declared CTEs, in source order.</summary>
    public IReadOnlyList<CommonTableExpression> Ctes { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Ctes;
}
