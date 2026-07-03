namespace EmberTern.Core.Sql;

/// <summary>Why a predicate can't use a plain b-tree index on its column.</summary>
public enum SargabilityIssue
{
    None,

    /// <summary>The column is wrapped in a function/expression (<c>UPPER(col)</c>, <c>col+0</c>),
    /// so a plain index on the column can't be used.</summary>
    FunctionOnColumn,

    /// <summary>A <c>LIKE '%…'</c> leading wildcard — an index can't seek on it.</summary>
    LeadingWildcardLike,
}

/// <summary>Sargability verdict for one predicate.</summary>
public sealed record SargabilityVerdict(QueryPredicate Predicate, bool IsSargable, SargabilityIssue Issue);

/// <summary>Classifies whether a predicate is sargable (can use a plain column index). Pure,
/// high-precision: only the two unambiguous non-sargable patterns are flagged (function on the
/// column, leading-wildcard LIKE). Everything else — including <c>IS NULL</c>, whose index
/// usability is version-dependent — is treated as sargable so the advisor doesn't over-flag.</summary>
public static class SargabilityClassifier
{
    public static SargabilityVerdict Classify(QueryPredicate predicate)
    {
        if (!predicate.IsColumnBare)
        {
            return new SargabilityVerdict(predicate, IsSargable: false, SargabilityIssue.FunctionOnColumn);
        }
        if (predicate.Operator == SqlPredicateOperator.Like && HasLeadingWildcard(predicate.Rhs))
        {
            return new SargabilityVerdict(predicate, IsSargable: false, SargabilityIssue.LeadingWildcardLike);
        }
        return new SargabilityVerdict(predicate, IsSargable: true, SargabilityIssue.None);
    }

    // A quoted literal whose first content char is '%' or '_' (SQL LIKE wildcards).
    private static bool HasLeadingWildcard(string rhs)
    {
        var t = rhs.TrimStart();
        return t.Length >= 2 && t[0] == '\'' && (t[1] == '%' || t[1] == '_');
    }
}
