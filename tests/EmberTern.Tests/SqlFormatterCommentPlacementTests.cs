using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// A comment standing between a query's last CLAUSE and whatever CLOSES the query — the statement's
/// <c>;</c>, or an <c>INTO</c> — must reach the output. Reported 2026-08-10 as "the autoformatter cannot
/// cope with this procedure"; measured cause: <c>EmitSelectQuery</c> renders a query from its clauses, and
/// a comment is materialised from the LEADING trivia of the token it precedes, so a comment before a token
/// that no clause owns was rendered by nobody.
/// <para>
/// ⚠⚠ <b>Why every case asserts that the output CHANGED, and why a lexeme-preservation assertion would be
/// worthless here.</b> §0's safety net catches the dropped comment and reverts the whole statement to
/// verbatim — so the defect never lost a lexeme and never corrupted anything. It disabled the FEATURE
/// instead: one <c>--</c> comment in that position froze a 30-line procedure, and formatting it looked
/// like a no-op. A test that only checked "every lexeme survives" was green throughout the defect's life
/// (<c>SqlFormatterSafetyTests</c> and <c>SqlFormatterInvariantsTests</c> both were). The assertion that
/// can fail is <i>the net did not fire</i>, i.e. a non-canonical input really got reformatted — the same
/// reasoning as <c>SqlFormatterCasingTests</c>' "actually re-case, and do not trip the safety net".
/// </para>
/// </summary>
public class SqlFormatterCommentPlacementTests
{
    // Asserts the input was really reformatted (the §0 net did not silently revert it), the comment reached
    // the output, and the result is a fixed point.
    private static string Formats(string sql)
    {
        var formatted = SqlFormatter.Format(sql);
        Assert.NotEqual(sql, formatted);                                   // the net did NOT fire
        Assert.Contains("--tail", formatted);                              // the comment survived
        Assert.Equal(formatted, SqlFormatter.Format(formatted));           // idempotent
        return formatted;
    }

    [Fact]
    public void ATopLevelSelect_KeepsAComment_StandingBeforeItsTerminator()
    {
        // The comment is leading trivia of ';', which lies OUTSIDE the query node entirely.
        var f = Formats("select a from t where a = 1 --tail\n;");
        Assert.Contains("--tail", f);
    }

    [Fact]
    public void APsqlSingletonSelect_KeepsAComment_StandingBeforeItsInto()
    {
        // Here the comment is INSIDE the query node's tokens (the node's span swallows the INTO while its
        // clauses stop at WHERE) — covered by no clause, so it needed its own emit.
        var f = Formats("""
create procedure p as
declare variable x integer;
begin
  select a from t where a = 1 --tail
  into :x;
end
""");
        // The comment must not swallow the INTO that follows it.
        var lines = f.Replace("\r", "").Split('\n');
        int c = System.Array.FindIndex(lines, l => l.Contains("--tail"));
        Assert.True(c >= 0 && c + 1 < lines.Length, "the comment must not be the last line");
        Assert.Contains("into", lines[c + 1]);
    }

    [Fact]
    public void AForSelectLoop_KeepsAComment_StandingBetweenItsCursorAndIts_Into()
    {
        // The shape from the report: the cursor query's last line carries a trailing comment. The comment
        // is an FToken of the CURSOR token range, which the AST path replaces with the query NODE.
        var f = Formats("""
create procedure p as
declare variable x integer;
begin
  for select a from t where a = 1 --tail
  into :x
  do begin
    x = 1;
  end
end
""");
        var lines = f.Replace("\r", "").Split('\n');
        int c = System.Array.FindIndex(lines, l => l.Contains("--tail"));
        Assert.Contains("into", lines[c + 1]);
    }

    [Fact]
    public void AForSelectLoop_WithNoInto_KeepsAComment_StandingBeforeIts_Do()
    {
        // Same gap at the OTHER boundary: with no INTO the cursor range runs up to DO.
        var f = Formats("""
create procedure p as
begin
  for select a from t where a = 1 --tail
  do begin
    x = 1;
  end
end
""");
        var lines = f.Replace("\r", "").Split('\n');
        int c = System.Array.FindIndex(lines, l => l.Contains("--tail"));
        Assert.True(c + 1 < lines.Length && lines[c + 1].Trim().StartsWith("do"),
            "the comment must sit on its own line, directly above DO");
    }

    [Fact]
    public void AViewBody_KeepsAComment_StandingBeforeItsTerminator()
    {
        // The third construct that renders a query node and then a tail of its own (found by the compiler
        // when the old one-purpose ';' re-attacher was replaced by the shared tail emitter).
        Formats("create view v as select a from t where a = 1 --tail\n;");
    }

    [Fact]
    public void ACommentInsideAClause_StaysWhereItWas()
    {
        // The counter-case that keeps the fix honest: a comment preceding a token a clause DOES own was
        // never broken, and must not be moved to a line of its own by the new tail handling.
        var f = SqlFormatter.Format("select a --tail\nfrom t;");
        Assert.NotEqual("select a --tail\nfrom t;", f);
        Assert.Contains("--tail", f);
        Assert.Equal(f, SqlFormatter.Format(f));
    }

    [Fact]
    public void ACommentedOutTailOfASetList_StaysOnTheSetLine()
    {
        // Also from the report: `set a = :x--, b = :y` — a commented-out assignment glued to the previous
        // token. UPDATE has no clause node, so it goes through the token emitter and was always correct;
        // pinned so the tail rule cannot start relocating it.
        var f = SqlFormatter.Format("""
create procedure p as
declare variable x integer;
begin
  update t set a = :x--, b = :x
  where id = :x;
end
""");
        // ⚠ Asserted on the LINE, not on a transcribed substring: the emitter separates the comment from
        // the token before it with a space, so the input's own `:x--, …` spelling is not what comes out.
        var setLine = System.Array.Find(f.Replace("\r", "").Split('\n'), l => l.Contains("set a ="));
        Assert.NotNull(setLine);
        Assert.Contains("--, b = :x", setLine!);
        Assert.Equal(f, SqlFormatter.Format(f));
    }

    [Fact]
    public void TheWholeReportedRoutine_Formats()
    {
        // The user's routine, reduced to the shape that mattered: two FOR/singleton-SELECT comments before
        // INTO, a commented-out SET tail, and a trailing comment after a ';'.
        var sql = """
CREATE OR ALTER PROCEDURE XXX_AKTCZASNARZ
(
    ID_TECHNOLOGIA INTEGER
)
AS
DECLARE VARIABLE ID_OPERTECH integer;
DECLARE VARIABLE ID_ZASOB integer;
DECLARE VARIABLE CZAS timestamp;
begin
  /* block */
  for select zc1.id_operacjatech, zc1.id_zasobtechcrp
	from operacjatech ot
inner join zasobtechcrp zc1 on (zc1.id_operacjatech = ot.id_operacjatech)
where ot.id_technologia = :ID_TECHNOLOGIA and zc1.id_zasobrodzaj = 3          --tail
into :ID_OPERTECH, :ID_ZASOB
do begin
   select first 1 zcr1.czas
   from zasobtechcrp zcr1
   where zcr1.id_operacjatech = :ID_OPERTECH    --tail
   into :CZAS;

   update zasobtechcrp zc
   set zc.czas = :CZAS--, zc.czaszasobu = :CZAS
   where zc.id_zasobtechcrp = :ID_ZASOB; --tail
end
    suspend;
end
""";
        var formatted = SqlFormatter.Format(sql);
        Assert.NotEqual(sql, formatted);
        Assert.Equal(formatted, SqlFormatter.Format(formatted));
        // Every comment of the input reaches the output exactly once — none dropped, none duplicated.
        // ⚠ Counted FROM THE INPUT, never from a number typed here: writing the expected count by hand is
        // how the first version of this assertion claimed four `--tail` in a routine that has three.
        foreach (var marker in new[] { "--tail", "/* block */", "--, zc.czaszasobu" })
            Assert.Equal(CountOccurrences(sql, marker), CountOccurrences(formatted, marker));
        Assert.True(CountOccurrences(sql, "--tail") > 0, "the fixture must actually carry the comments");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal)) n++;
        return n;
    }
}
