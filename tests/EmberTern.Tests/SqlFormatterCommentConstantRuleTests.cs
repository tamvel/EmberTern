using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// THE CONSTANT RULE — an AST-driven clause emitter that rebuilds its clause from a keyword CONSTANT
/// (<c>Kw("select")</c>, <c>Kw("from")</c>, <c>Kw("with")</c>, <c>Kw("as")</c>, a set operator) never renders
/// the TOKENS that constant replaces, so a comment carried as those tokens' leading trivia was rendered by
/// nobody.
///
/// <para>⚠⚠ <b>The symptom was not a missing comment.</b> §0's lexeme net saw the loss and reverted the whole
/// statement to verbatim, so the formatter silently DID NOTHING — a user's procedure whose body opened with a
/// header comment "could not be formatted at all", and deleting the comment fixed it (report 2026-08-12).
/// Every case below was MEASURED broken before the fix: 9 of the first 13 shapes tried, across four separate
/// emitters. That is why the guard is a corpus and not one regression case — the report named one shape and
/// the defect had four homes.</para>
///
/// <para>⭐ The assertion is <b>"the formatter CHANGED the input"</b>, not a pinned layout. That is the
/// observable the defect actually destroyed, and it stays true when the layout is legitimately re-tuned —
/// whereas an expected-output string would fail for reasons unrelated to this bug (gotcha #359: a formatter
/// that drops a lexeme does not look like a formatting bug, it looks like a feature that does nothing).
/// The companion assertion is idempotency, which is what proves the recovered comment landed somewhere
/// stable rather than being re-moved on every pass.</para>
/// </summary>
public class SqlFormatterCommentConstantRuleTests
{
    [Theory]
    // ── the projection (EmitProjection) — the reported class. Needs a structural child (subquery / CASE)
    //    in the column list, because that is the path that rebuilds the SELECT keyword from a constant.
    [InlineData("/* c */\nselect (select 1 from u) from t")]
    [InlineData("/* c */\nselect case when a = 1 then 2 else 3 end from t")]
    [InlineData("-- c\nselect (select 1 from u) from t")]
    // ── FROM, when a derived table forces the structural layout
    [InlineData("select a /* c */ from (select 1 from u) x")]
    // ── WITH: before the keyword, before the CTE name, before AS, before the body's "(",
    //    between the body's ")" and the next CTE's ","
    [InlineData("/* c */\nwith q as (select 1 from t) select * from q")]
    [InlineData("with /* c */ q as (select 1 from t) select * from q")]
    [InlineData("with q /* c */ as (select 1 from t) select * from q")]
    [InlineData("with q as /* c */ (select 1 from t) select * from q")]
    [InlineData("with -- c\n q as (select 1 from t) select * from q")]
    [InlineData("with q as (select 1 from t) /* c */, r as (select 2 from u) select * from q, r")]
    [InlineData("with q as (select 1 from t) -- c\n, r as (select 2 from u) select * from q, r")]
    [InlineData("with q (a /* c */, b) as (select 1, 2 from t) select * from q")]
    [InlineData("with q as (select 1 from t /* c */) select * from q")]
    // ── set operation: before the operator and between the operator and ALL
    [InlineData("select a from t /* c */ union select b from u")]
    [InlineData("select a from t union /* c */ all select b from u")]
    // ── the reported shape itself: a PSQL block opening with a comment, over a SELECT … INTO leaf whose
    //    column list carries a scalar subquery
    [InlineData("begin\n  /* c */\n  select (select 1 from u), b\n  from t\n  into :a, :b;\nend")]
    [InlineData("begin\n  /*\n    multi\n    line\n  */\n\n  select (select 1 from u), b\n  from t\n  into :a, :b;\nend")]
    public void ACommentOnAReplacedKeyword_DoesNotDisableTheFormatter(string sql)
    {
        var once = SqlFormatter.Format(sql);

        // ⭐ The §0 net returns the INPUT unchanged when a lexeme was lost, added or REORDERED — so
        // "output == input" is precisely the failure signature this class exists to catch. Every shape here
        // is one the formatter has real work to do on (a clause break, an indent, a block), so an unchanged
        // result cannot be a legitimate "already canonical" outcome.
        Assert.NotEqual(sql, once);

        // …and the recovered comment must land somewhere STABLE: a position that moves on each pass would
        // make the formatter non-idempotent, which is design principle #7.
        Assert.Equal(once, SqlFormatter.Format(once));

        // The comment itself survives — §0 in its plainest form.
        Assert.Contains("c", once, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The user's own routine body from the 2026-08-12 report, trimmed to the shape that mattered: a header
    /// block comment, then a <c>SELECT … INTO</c> whose projection carries scalar subqueries. This is the
    /// case that was reported as "autoformat does not work for this procedure", and it is kept whole because
    /// the minimal reproducers above each isolate one emitter while this one crosses several at once.
    /// </summary>
    [Fact]
    public void TheReportedRoutineBody_Formats()
    {
        const string body = @"begin
  /*
    Cel:
      Czyste wyliczenie cech normy i czasu dla meldunku - bez zapisu.

    Jednostka wszystkich wynikow: godziny dziesietne.
  */

  select
         m.id_meldunek, m.ilosc,
         (select first 1 xxx_fn_czas_bez_przezbrojenia(mz.id_meldunek, xxx_fn_czas_to_decimal(mz.czas))
             from meldunekzasob mz
             where mz.id_meldunek = m.id_meldunek
             order by mz.id_meldunekzasob
         ),
         iif(
             exists (
                 select 1
                 from xxx_meldowanie_grupowe_elem e
                 where e.id_meldunek = m.id_meldunek
             ),
             1,
             0)
  from meldunek m
  left join operacja o on o.id_operacja = m.id_operacja
  where m.id_meldunek = :p_id_meldunek
  into :id_meldunek, :v_ilosc_meldunku, :czy_grupowy;

  if (id_meldunek is null) then
    exit;

  suspend;
end";

        var once = SqlFormatter.Format(body);
        Assert.NotEqual(body, once);
        Assert.Equal(once, SqlFormatter.Format(once));

        // The header comment is still one block, still above the SELECT — not hoisted, not split.
        Assert.Contains("Jednostka wszystkich wynikow: godziny dziesietne.", once, System.StringComparison.Ordinal);
        Assert.True(
            once.IndexOf("*/", System.StringComparison.Ordinal) < once.IndexOf("select", System.StringComparison.Ordinal),
            "the recovered header comment must still stand ABOVE the select it annotates");
    }
}
