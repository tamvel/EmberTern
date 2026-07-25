using EmberTern.Core.Sql.Debugging;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Stage X — Firebird Debugger, D5 seam (b): the lexical side-effect flag for Watches (§9.5). Pure,
/// reuses the one <see cref="EmberTern.Core.Sql.Language.SqlLexer"/> — a keyword only matches as a bare token,
/// so strings / quoted identifiers never trip it.</summary>
public class WatchSideEffectDetectorTests
{
    [Theory]
    [InlineData("a + b")]
    [InlineData("count(*)")]
    [InlineData("coalesce(v_total, 0) * 1.23")]
    [InlineData("(select count(*) from orders where id = :v)")] // a scalar subquery is pure
    [InlineData("v = 5")]                                       // an equality comparison, not an assignment
    [InlineData("'please UPDATE the record'")]                  // keyword inside a string literal
    public void PureExpression_NotFlagged(string expression)
        => Assert.False(WatchSideEffectDetector.HasSideEffect(expression));

    [Theory]
    [InlineData("update t set x = 1")]
    [InlineData("insert into audit values (1)")]
    [InlineData("delete from t")]
    [InlineData("execute procedure sp_do_something")]
    [InlineData("post_event 'x'")]
    [InlineData("merge into t using s on (t.id = s.id) when matched then update set x = 1")]
    public void SideEffectingFragment_Flagged(string expression)
        => Assert.True(WatchSideEffectDetector.HasSideEffect(expression));

    [Fact]
    public void CaseInsensitive()
        => Assert.True(WatchSideEffectDetector.HasSideEffect("Update T Set X = 1"));

    [Fact]
    public void Blank_NotFlagged()
        => Assert.False(WatchSideEffectDetector.HasSideEffect("   "));
}
