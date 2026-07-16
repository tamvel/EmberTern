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

    // ── Coverage: every construct arms in the positions the design says it may begin in ──────
    //
    // The precondition for Language Completion EXCLUSIVELY owning these words (i.e. IntelliSense no
    // longer offering them): there must be no position where a construct is legal, the developer types
    // its natural prefix, and NEITHER system helps. Under-arming used to be harmless because the
    // completion list covered it; once the word is removed from that list, an under-armed position is a
    // dead zone. This table is that guarantee — a new catalog row that arms nowhere fails here.
    //
    // Each row: the text typed so far (caret at the end) → the construct that must be armed.
    [Theory]
    // Statement / PSQL-body positions
    [InlineData("if", "if")]
    [InlineData("begin\n  if", "if")]
    [InlineData("begin\n  x = 1;\n  if", "if")]
    [InlineData("if (x) then\n  sel", "select")]
    [InlineData("while (x) do\n  sel", "select")]
    [InlineData("select * from T;\n\nif", "if")]
    [InlineData("begin\n  whi", "while")]
    [InlineData("begin\n  for s", "for select")]
    [InlineData("begin\n  decl", "declare variable")]
    [InlineData("begin\n  x = 1;\n  when", "when")]
    [InlineData("execute p", "execute procedure")]
    [InlineData("execute b", "execute block")]
    [InlineData("insert i", "insert into")]
    [InlineData("upd", "update")]
    [InlineData("delete f", "delete from")]
    // A new statement after an UNTERMINATED one, separated only by a blank line. The previous-token rule
    // alone answers `where` → None / `1` → Clause and refuses `if`; the blank line is what carries it.
    [InlineData("select *\nfrom NAGL n\nwhere\n\nif", "if")]
    [InlineData("select *\nfrom NAGL n\nwhere n.ID = 1\n\nif", "if")]
    // Clause continuations
    [InlineData("select * from CUSTOMER\nwher", "where")]
    [InlineData("update T set X = 1\nwher", "where")]
    [InlineData("delete from T\nwher", "where")]
    [InlineData("select * from T where X = 1\ngro", "group by")]
    [InlineData("select * from T group by A\nhav", "having")]
    [InlineData("select * from T\nord", "order by")]
    [InlineData("select * from T\nuni", "union")]
    // A query may begin wherever one may nest
    [InlineData("select * from T where X in (sel", "select")]
    [InlineData("with C as (sel", "select")]
    [InlineData("select * from (sel", "select")]
    [InlineData("select 1 from T union\nsel", "select")]
    // INSERT … SELECT: the caret sits at a Clause position (after ')' or the table name), so only the
    // enclosing statement's opening tokens reveal that a query may begin here.
    [InlineData("insert into T (A, B)\nsel", "select")]
    [InlineData("insert into T\nsel", "select")]
    public void EveryConstruct_ArmsWhereItMayBegin(string text, string expected)
        => Assert.Equal(expected, LanguageConstructResolver.Resolve(text, text.Length)?.Construct.Spelling);

    [Theory]
    // A value/expression slot is IntelliSense's job — naming things, not starting constructs.
    [InlineData("select * from T where X = sel")]
    [InlineData("select ")]
    // CASE … WHEN must NOT arm: the catalog's `when` is the exception handler and expands to "when ▌ do",
    // which would be wrong inside a CASE expression.
    [InlineData("select case X when")]
    public void ConstructsStaySilent_WhereTheyMayNotBegin(string text)
        => Assert.Null(LanguageConstructResolver.Resolve(text, text.Length));

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
