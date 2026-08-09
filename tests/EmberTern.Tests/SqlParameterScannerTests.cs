using System.Linq;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Smart-Parameters scanner — extraction is lexer-based (no regex): literals,
/// comments and quoted identifiers are skipped; <c>::</c> is the cast operator.</summary>
public class SqlParameterScannerTests
{
    [Fact]
    public void Scan_ExtractsColonAndAtNames_InOrder()
    {
        var p = SqlParameterScanner.Scan("select * from t where a = :id and b = @code");
        Assert.Equal(new[] { "id", "code" }, p.Select(x => x.Name).ToArray());
        Assert.Equal(':', p[0].Marker);
        Assert.Equal('@', p[1].Marker);
    }

    [Fact]
    public void Scan_SkipsLiteralsCommentsQuotedIdents_AndCast()
    {
        const string sql =
            "select cast(x as integer), ':notparam', \"col:name\" -- :nocomment\n" +
            "/* @noblock */ from t where a = :real and n = x::int";
        var names = SqlParameterScanner.Scan(sql).Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "real" }, names); // literal / comment / quoted-ident / :: all skipped
    }

    [Fact]
    public void Scan_LoneMarkersAndDigits_AreNotParameters()
    {
        // ':' before a space, '@' before a digit, and a bare '::' are not parameters.
        Assert.Empty(SqlParameterScanner.Scan("select a : b, @1, x::y from t"));
    }

    [Fact]
    public void Scan_ExecuteProcedureColonNames()
    {
        var names = SqlParameterScanner.Scan("execute procedure xxx_test(:id_kontrahent, :id_magazyn, :tryb)")
            .Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "id_kontrahent", "id_magazyn", "tryb" }, names);
    }

    [Fact]
    public void RewriteToDriverMarkers_ColonToAt_AndNormalizesCase()
    {
        var (sql, names) = SqlParameterScanner.RewriteToDriverMarkers(
            "select * from t where a = :Id and b = :ID and c = @other");

        // :Id and :ID collapse to one param (first spelling wins); marker becomes @.
        Assert.Equal(new[] { "Id", "other" }, names.ToArray());
        Assert.Contains("a = @Id", sql);
        Assert.Contains("b = @Id", sql); // :ID normalized to the first occurrence @Id
        Assert.Contains("c = @other", sql);
    }

    [Fact]
    public void RewriteToDriverMarkers_LiteralWithColon_Untouched()
    {
        var (sql, names) = SqlParameterScanner.RewriteToDriverMarkers("update t set note = ':keep' where id = :id");
        Assert.Equal(new[] { "id" }, names.ToArray());
        Assert.Contains("':keep'", sql);          // literal left alone
        Assert.Contains("id = @id", sql);
    }

    [Fact]
    public void RewriteToDriverMarkers_NoParams_ReturnsSqlUnchanged()
    {
        const string sql = "select 1 from rdb$database";
        var (rewritten, names) = SqlParameterScanner.RewriteToDriverMarkers(sql);
        Assert.Equal(sql, rewritten);
        Assert.Empty(names);
    }

    [Theory]
    [InlineData("execute block returns (x integer) as begin x = :local; suspend; end", true)]
    [InlineData("  EXECUTE  BLOCK (a int = ?) as begin end", true)]
    [InlineData("execute procedure p(:a)", false)]
    [InlineData("select :a from t", false)]
    public void IsExecuteBlock_DetectsBlock(string sql, bool expected)
        => Assert.Equal(expected, SqlParameterScanner.IsExecuteBlock(sql));

    /// <summary>
    /// ⭐⭐ <b>THE POINT OF THIS THEORY IS THAT NO ROW OF IT NEEDED CODE OF ITS OWN.</b> The recognition of a
    /// routine call used to be a list of statement shapes, and the user reported the same defect three times —
    /// once for <c>EXECUTE PROCEDURE</c>, once for a selectable procedure in <c>SELECT … FROM</c>, then for
    /// <c>FOR SELECT … INTO</c> and <c>INSERT … SELECT</c> — because each fix enumerated one more syntax.
    /// <c>IRoutineInvocation</c> made the invocation a fact the AST carries, so a single descendant walk finds
    /// every row below, and the rows the user has not thought of yet.
    /// </summary>
    [Theory]
    // The executable shape.
    [InlineData("execute procedure xxx_test(:a, :b)", "XXX_TEST", 2)]
    [InlineData("EXECUTE PROCEDURE Recalc", "RECALC", 0)]
    // The SELECTABLE shape, in the reported statement's structure.
    [InlineData("select a, b from xxx_sel_rap_czasupracy(:p_dataod, :p_datado, :p_id_jedkadr)",
        "XXX_SEL_RAP_CZASUPRACY", 3)]
    [InlineData("select * from Rap_Test(:a) r where r.x > 0", "RAP_TEST", 1)]
    [InlineData("select *\r\nfrom t\r\njoin rap(:a, :b) r on r.id = t.id", "RAP", 2)]
    // ⭐ …and the shapes the third report named, none of which has a line of code behind it.
    [InlineData("for select a, b from rap(:a, :b) into :x, :y do suspend;", "RAP", 2)]
    [InlineData("insert into t (a, b) select a, b from rap(:a, :b)", "RAP", 2)]
    [InlineData("update or insert into t (a) values ((select first 1 x from rap(:a)))", "RAP", 1)]
    [InlineData("with c as (select x from rap(:a)) select * from c", "RAP", 1)]
    [InlineData("merge into t using rap(:a) s on t.id = s.id when matched then update set t.x = s.x",
        "RAP", 1)]
    // ⚠ A cursor declaration only exists inside a PSQL body, so the case has to be written as one — the earlier
    // standalone spelling was simply not a DECLARE CURSOR and proved nothing.
    [InlineData("execute block as declare cur cursor for (select x from rap(:a));\nbegin\n  open cur;\nend",
        "RAP", 1)]
    [InlineData("select * from (select x from rap(:a) ) d", "RAP", 1)]
    [InlineData("select * from t where exists (select 1 from rap(:a))", "RAP", 1)]
    // A plain table is not a routine call, and neither is anything else.
    [InlineData("select * from t", null, 0)]
    [InlineData("select * from t where x = :a", null, 0)]
    [InlineData("execute block as begin end", null, 0)]
    [InlineData("update t set x = :a", null, 0)]
    [InlineData("select * from (select x from t where y = :a) d", null, 0)]
    public void RoutineInvocations_AreFoundByTheModel_NotByStatementShape(
        string sql, string? expectedName, int expectedArgs)
    {
        var calls = SqlParameterScanner.RoutineInvocations(sql);
        if (expectedName is null)
        {
            Assert.Empty(calls);
            return;
        }

        var call = Assert.Single(calls);
        Assert.Equal(expectedName, SqlParameterScanner.CatalogName(call));
        Assert.Equal(expectedArgs, call.Arguments.Count);
    }

    /// <summary>⚠ A no-argument selectable procedure written without parentheses is indistinguishable from a
    /// table, and is deliberately NOT claimed — a bare name is not evidence of a call, and guessing would make
    /// every table in every query look like one.</summary>
    [Fact]
    public void ABareNameInFrom_IsNotClaimedAsACall()
        => Assert.Empty(SqlParameterScanner.RoutineInvocations("select * from my_selectable_proc"));

    /// <summary>A packaged member keeps its qualifier, so the catalog lookup finds the member rather than a
    /// nonexistent standalone routine of the same name.</summary>
    [Fact]
    public void APackagedCall_KeepsItsQualifier()
    {
        var call = Assert.Single(SqlParameterScanner.RoutineInvocations("execute procedure pkg.proc(:a)"));
        Assert.Equal("PKG.PROC", SqlParameterScanner.CatalogName(call));
    }

    // ── The OTHER provable type source: a value written into a named column ──────────────────────
    //
    // ⭐⭐ The user's directive (2026-08-03): *"Jeżeli AST potrafi jednoznacznie ustalić, z jaką kolumną jest
    // związany placeholder, to chcę, żeby typ był rozpoznawany również dla DML … nie seria if-ów dla kolejnych
    // instrukcji, tylko wykorzystanie modelu AST jako jednego źródła wiedzy."* So the resolver returns the same
    // ParameterTypeSource for both origins, and the consumer switches on the KIND OF SOURCE, never on a syntax.

    /// <summary>The two statements from the report's screenshots, plus UPDATE, which pairs differently.</summary>
    [Theory]
    // INSERT — positional pairing of (cols) against VALUES.
    [InlineData("insert into bomitem (id_bomitem, id_bom) values (:id_bomitem, :v_id_bom)",
        "id_bomitem,v_id_bom", "BOMITEM:ID_BOMITEM|BOMITEM:ID_BOM")]
    // ⭐ A literal between placeholders must not shift the pairing.
    [InlineData("insert into t (a, b, c) values (:x, 5, :z)", "x,z", "T:A|T:C")]
    // UPDATE OR INSERT — the same shape, hence the same producer.
    [InlineData("UPDATE OR INSERT INTO URZZEWNAGL_AKCEPT (ID_NAGL, CZYAKCEPT) VALUES (:ID_NAGL, :CZYAKCEPT) "
        + "MATCHING (ID_NAGL)", "ID_NAGL,CZYAKCEPT",
        "URZZEWNAGL_AKCEPT:ID_NAGL|URZZEWNAGL_AKCEPT:CZYAKCEPT")]
    // UPDATE — pairs by adjacency in SET, which is why the model carries PAIRS rather than two lists.
    [InlineData("update t set a = :x, b = :y where id = 1", "x,y", "T:A|T:B")]
    // ⚠ The WHERE predicate is not a modelled pairing at structural depth, so its placeholder stays untyped.
    [InlineData("update t set a = :x where id = :key", "x,key", "T:A|")]
    // ⚠ No column list ⇒ nothing is claimed: matching values to columns would need the catalog's order, which is
    // a lookup rather than a fact about the text.
    [InlineData("insert into t values (:a, :b)", "a,b", "|")]
    // ⚠ A length mismatch is a statement Firebird rejects anyway; pairing the prefix would type values whose
    // column is not yet decided.
    [InlineData("insert into t (a, b) values (:a)", "a", "")]
    // ⚠ Not the WHOLE value ⇒ the column's declared type is not this placeholder's type.
    [InlineData("insert into t (a) values (:a + 1)", "a", "")]
    // INSERT … SELECT supplies values as a query's columns, not as spans — no pairs.
    [InlineData("insert into t (a) select x from u where y = :p", "p", "")]
    public void ColumnValues_AreAnotherProvableTypeSource(string sql, string names, string expected)
    {
        var sources = SqlParameterScanner.ResolveTypeSources(sql, names.Split(','));
        var rendered = string.Join("|", sources.Select(
            s => s.Kind == SqlParameterScanner.TypeSourceKind.TableColumn
                ? s.Owner + ":" + s.ColumnName
                : string.Empty));
        Assert.Equal(expected, rendered);
    }

    /// <summary>⭐ The two sources coexist in one statement without either learning about the other: the argument
    /// of a selectable procedure is typed from the routine, the inserted value from the column.</summary>
    [Fact]
    public void BothTypeSources_CoexistInOneStatement()
    {
        const string sql = "insert into t (a) values ((select first 1 x from rap(:p))) ";
        var sources = SqlParameterScanner.ResolveTypeSources(sql, new[] { "p" });

        Assert.Equal(SqlParameterScanner.TypeSourceKind.RoutineParameter, sources[0].Kind);
        Assert.Equal("RAP", sources[0].Owner);
        Assert.Equal(0, sources[0].Slot);
    }

    /// <summary>⭐ Two routines in one statement: each placeholder is bound to the routine it actually stands in.
    /// A single "the call of this statement" would have to pick one and be wrong about the other.</summary>
    [Fact]
    public void TwoInvocations_BindTheirOwnPlaceholders()
    {
        const string sql = "select * from rap_a(:a) x join rap_b(:b) y on x.id = y.id";
        var bindings = SqlParameterScanner.ResolveTypeSources(sql, new[] { "a", "b" });

        Assert.Equal("RAP_A", bindings[0].Owner);
        Assert.Equal(0, bindings[0].Slot);
        Assert.Equal("RAP_B", bindings[1].Owner);
        Assert.Equal(0, bindings[1].Slot);
    }

    /// <summary>⚠ …and one value standing in two DIFFERENT routines is ambiguous: it cannot carry two declared
    /// types, so nothing is claimed for it.</summary>
    [Fact]
    public void OnePlaceholderInTwoRoutines_IsAmbiguous()
    {
        const string sql = "select * from rap_a(:p) x join rap_b(:p) y on x.id = y.id";
        var bindings = SqlParameterScanner.ResolveTypeSources(sql, new[] { "p" });
        Assert.False(bindings[0].IsResolved);
    }

    // ─── Placeholder → argument slot (user report 2026-08-03: "type detection stopped working") ──────
    //
    // The defect was in the CONSUMER, which typed the placeholders only when the input-parameter count equalled
    // the placeholder count and fell back to "Unknown" for ALL of them otherwise. These pin the mapping that
    // replaces that equality: which input parameter each placeholder provably stands for.

    /// <summary>The reported case and the two neighbours that break the same way.</summary>
    [Theory]
    // The shape that always worked: one whole placeholder per argument, in order.
    [InlineData("execute procedure p(:a, :b)", "a,b", "0,1")]
    [InlineData("execute procedure p :a, :b;", "a,b", "0,1")]
    // ⭐ RETURNING_VALUES: its target IS a placeholder but is NOT an argument. Three names vs two input
    // parameters made the old equality fail, so a and b lost their types too.
    [InlineData("execute procedure p(:a, :b) returning_values :r;", "a,b,r", "0,1,-1")]
    // ⭐ A call that omits parameters carrying DEFAULTS — routine in ERP code, and the count never matches.
    [InlineData("execute procedure p(:a)", "a", "0")]
    // A literal argument does not shift the placeholders after it.
    [InlineData("execute procedure p(:a, 5, :b)", "a,b", "0,2")]
    // Not a whole argument ⇒ the parameter's type is not the argument's declared type. Unknown, deliberately.
    [InlineData("execute procedure p(:a + 1, :b)", "a,b", "-1,1")]
    // One value bound into two slots whose declared types may differ ⇒ ambiguous, so neither is claimed.
    [InlineData("execute procedure p(:a, :a)", "a", "-1")]
    // Nothing else maps at all.
    [InlineData("select :a from t", "a", "-1")]
    [InlineData("update t set x = :a where id = :b", "a,b", "-1,-1")]
    // ⭐ And the same rules apply to a SELECTABLE call, because it is the same mapping over the same kind of
    // argument spans — the reported statement's own shape is the first row below.
    [InlineData("select a from rap(:p_dataod, :p_datado, :p_id_jedkadr)", "p_dataod,p_datado,p_id_jedkadr", "0,1,2")]
    [InlineData("select a from rap(:a, 5, :b) r", "a,b", "0,2")]
    [InlineData("select a from rap(:a + 1, :b)", "a,b", "-1,1")]
    [InlineData("select a from rap(:a, :a)", "a", "-1")]
    // ⚠ A placeholder in the WHERE clause is not an argument of the call in FROM.
    [InlineData("select a from rap(:a) r where r.x = :b", "a,b", "0,-1")]
    // ⭐ …and in every other syntax, for free — these rows exercise the same mapping through FOR SELECT,
    // INSERT … SELECT, a CTE and a subquery, none of which the mapping knows anything about.
    [InlineData("for select a from rap(:a, :b) into :x, :y do suspend;", "a,b,x,y", "0,1,-1,-1")]
    [InlineData("insert into t (a) select a from rap(:a, :b)", "a,b", "0,1")]
    [InlineData("with c as (select x from rap(:a)) select * from c", "a", "0")]
    [InlineData("select * from t where exists (select 1 from rap(:a))", "a", "0")]
    public void MapNamesToArgumentSlots_MapsOnlyWhatItCanProve(
        string sql, string names, string expectedSlots)
    {
        var bindings = SqlParameterScanner.ResolveTypeSources(sql, names.Split(','));
        Assert.Equal(expectedSlots, string.Join(",", bindings.Select(b => b.Slot)));
    }

    /// <summary>The names come from <see cref="SqlParameterScanner.RewriteToDriverMarkers"/>, so the mapping has
    /// to line up with <i>that</i> list rather than with a hand-written one — including its case normalization.
    /// Asserted end to end because the two are only ever used together.</summary>
    [Fact]
    public void MapNamesToArgumentSlots_LinesUpWithTheRewrittenNames()
    {
        const string sql = "EXECUTE PROCEDURE Recalc(:DataOd, :dataod, :Kwota);";
        var (_, names) = SqlParameterScanner.RewriteToDriverMarkers(sql);

        // ":dataod" is the same parameter as ":DataOd" (case-insensitive), so there are two names, not three —
        // and the repeated one now stands in two slots, which is exactly the ambiguity the mapping refuses.
        Assert.Equal(new[] { "DataOd", "Kwota" }, names);
        Assert.Equal("-1,2", string.Join(
            ",", SqlParameterScanner.ResolveTypeSources(sql, names).Select(b => b.Slot)));
    }
}
