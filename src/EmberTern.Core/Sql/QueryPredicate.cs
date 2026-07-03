namespace EmberTern.Core.Sql;

/// <summary>Where a predicate came from.</summary>
public enum SqlPredicateKind
{
    Where,
    JoinOn,
}

/// <summary>The comparison operator of a predicate. Only the forms the lightweight
/// extractor recognizes; anything else means the conjunct is skipped (no predicate emitted).</summary>
public enum SqlPredicateOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Like,
    StartingWith,
    Containing,
    In,
    Between,
    IsNull,
    IsNotNull,
}

/// <summary>One extracted predicate: a comparison whose left side references a single column.
/// Produced by <see cref="PredicateExtractor"/> — a lightweight scanner, not a grammar, so it
/// only carries predicates it could confidently identify (see the extractor's remarks). Pure
/// Core value object; feeds sargability + the performance advisor.</summary>
public sealed record QueryPredicate
{
    /// <summary>Column name as written (case preserved); matching against the catalog is
    /// case-insensitive so casing doesn't matter downstream.</summary>
    public required string Column { get; init; }

    /// <summary>Qualifier as written (alias or table), uppercased; null when unqualified.</summary>
    public string? Alias { get; init; }

    /// <summary>Resolved table (via <see cref="SqlAliasResolver"/>), or null when the qualifier
    /// couldn't be resolved (multi-table query with an unqualified column, unknown alias, …).</summary>
    public string? Table { get; init; }

    public required SqlPredicateOperator Operator { get; init; }

    /// <summary>Right-hand side text, trimmed (for evidence/messages).</summary>
    public string Rhs { get; init; } = string.Empty;

    public required SqlPredicateKind Kind { get; init; }

    /// <summary>True when the left side is exactly the (optionally qualified) column; false when
    /// the column is wrapped in a function/expression (<c>UPPER(col)</c>, <c>col+0</c>, …) — the
    /// signal sargability analysis keys on.</summary>
    public bool IsColumnBare { get; init; }

    /// <summary>The left-hand side as written (for evidence/messages).</summary>
    public string LhsRaw { get; init; } = string.Empty;
}
