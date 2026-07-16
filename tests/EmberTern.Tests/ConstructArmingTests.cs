using EmberTern.Core.Sql.Language.Constructs;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Grammar-aware arming for Language Completion (design §5) — the simple, deterministic, previous-token
/// rule and the integrated <see cref="LanguageConstructResolver.Resolve"/> (prefix match ∩ grammar).
/// Pure and synchronous. Verifies the everyday behaviour: statement constructs arm at boundaries, clause
/// constructs arm after a table/expression, and neither competes where the developer is naming things.
/// </summary>
public class ConstructArmingTests
{
    // ── Position classification ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("select 1;", 9)]          // after ';'
    [InlineData("begin ", 6)]             // after 'begin'
    [InlineData("if (x) then ", 12)]      // after 'then'
    [InlineData("while (x) do ", 13)]     // after 'do'
    [InlineData("... as ", 7)]            // after 'as'
    [InlineData("select 1 union ", 15)]   // after 'union'
    public void Classify_StatementBoundaries(string text, int pos)
        => Assert.Equal(ConstructPosition.StatementStart, ConstructContext.Classify(text, pos));

    [Theory]
    [InlineData("select * from customer ", 23)] // after an identifier (table)
    [InlineData("where func(a) ", 14)]          // after ')'
    [InlineData("where x = 1 ", 12)]            // after a number
    [InlineData("where x = 'a' ", 14)]          // after a string
    [InlineData("where x = :p ", 13)]           // after a parameter
    public void Classify_ClauseContinuations(string text, int pos)
        => Assert.Equal(ConstructPosition.Clause, ConstructContext.Classify(text, pos));

    [Theory]
    [InlineData("select ", 7)]      // after a non-boundary keyword
    [InlineData("select a from ", 14)]
    [InlineData("where ", 6)]
    [InlineData("select a, ", 10)]  // after a comma
    public void Classify_NeitherPosition_IsNone(string text, int pos)
        => Assert.Equal(ConstructPosition.None, ConstructContext.Classify(text, pos));

    // ── Integrated Resolve (prefix ∩ grammar) ────────────────────────────────────────────────

    private static string? Arm(string text)
        => LanguageConstructResolver.Resolve(text, text.Length)?.Construct.Spelling;

    [Fact]
    public void StatementConstruct_ArmsAtStatementStart()
    {
        Assert.Equal("if", Arm("if"));
        Assert.Equal("select", Arm("sel"));
        Assert.Equal("for select", Arm("for"));
        Assert.Equal("select", Arm("begin sel"));       // PSQL body statement position
        Assert.Equal("if", Arm("select 1; if"));         // after ';'
    }

    [Fact]
    public void ClauseConstruct_DoesNotArmAtStatementStart()
    {
        Assert.Null(Arm("where"));      // clause at start of text
        Assert.Null(Arm("gro"));        // group by at start
        Assert.Null(Arm("order"));
    }

    [Fact]
    public void ClauseConstruct_ArmsAfterATableOrExpression()
    {
        Assert.Equal("where", Arm("select * from customer wher"));
        Assert.Equal("group by", Arm("select * from customer gro"));
        Assert.Equal("order by", Arm("select * from t where x = 1 ord"));
    }

    [Fact]
    public void ClauseConstruct_DoesNotArmRightAfterSelectKeyword()
        => Assert.Null(Arm("select wher"));   // between SELECT and FROM → naming columns, not a clause

    [Fact]
    public void StatementConstruct_DoesNotArmInClausePosition()
    {
        // "insert into" must not arm right after a table name.
        Assert.Null(Arm("select * from t ins"));
        // "select" must not arm after a table either (it's a clause position).
        Assert.Null(Arm("select * from customer sel"));
    }

    [Fact]
    public void UnionThenSelect_Arms()
    {
        Assert.Equal("union", Arm("select 1 from t un"));   // union arms after the table
        Assert.Equal("select", Arm("select 1 from t union sel")); // select arms after union
    }

    [Fact]
    public void Ambiguous_StaysSilent_RegardlessOfPosition()
    {
        Assert.Null(Arm("wh"));            // while/where/when — ambiguous prefix
        Assert.Null(Arm("select * from t wh")); // still ambiguous even in a clause position
    }

    [Fact]
    public void Resolve_CarriesPrefixLength_WhenArmed()
    {
        var m = LanguageConstructResolver.Resolve("begin sel", 9);
        Assert.NotNull(m);
        Assert.Equal("select", m!.Construct.Spelling);
        Assert.Equal(3, m.PrefixLength);   // replaces "sel"
    }

    [Fact]
    public void Identifiers_NeverArm()
    {
        Assert.Null(Arm("select * from customer"));  // "customer" is a name, matches no construct
        Assert.Null(Arm("nr_status"));
    }
}
