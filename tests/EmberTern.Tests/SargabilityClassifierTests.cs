using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SargabilityClassifierTests
{
    private static QueryPredicate Pred(bool bare, SqlPredicateOperator op, string rhs = "")
        => new() { Column = "C", Operator = op, Rhs = rhs, Kind = SqlPredicateKind.Where, IsColumnBare = bare };

    [Fact]
    public void BareEquality_IsSargable()
    {
        var v = SargabilityClassifier.Classify(Pred(true, SqlPredicateOperator.Equal, "5"));
        Assert.True(v.IsSargable);
        Assert.Equal(SargabilityIssue.None, v.Issue);
    }

    [Fact]
    public void FunctionOnColumn_IsNonSargable()
    {
        var v = SargabilityClassifier.Classify(Pred(false, SqlPredicateOperator.Equal, "'X'"));
        Assert.False(v.IsSargable);
        Assert.Equal(SargabilityIssue.FunctionOnColumn, v.Issue);
    }

    [Fact]
    public void LeadingWildcardLike_IsNonSargable()
    {
        var v = SargabilityClassifier.Classify(Pred(true, SqlPredicateOperator.Like, "'%x'"));
        Assert.False(v.IsSargable);
        Assert.Equal(SargabilityIssue.LeadingWildcardLike, v.Issue);
    }

    [Fact]
    public void TrailingWildcardLike_IsSargable()
        => Assert.True(SargabilityClassifier.Classify(Pred(true, SqlPredicateOperator.Like, "'x%'")).IsSargable);

    [Fact]
    public void IsNull_IsNotFlagged()
        => Assert.True(SargabilityClassifier.Classify(Pred(true, SqlPredicateOperator.IsNull)).IsSargable);
}
