using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D2 seam (c): the pure Core declaration extractor (spec §3.4 R2/R3).
/// It lifts a routine frame's local variable declarations verbatim (R3) and their type spec (the R2 base-type
/// resolver's input) from the parsed body + source. Pure Core, no server. Builds a real routine AST the way
/// the debugger does — the STRICT parse of a whole <c>CREATE PROCEDURE</c>, kept as one <see cref="DdlStatement"/>
/// with its declares + body together (mirrors <c>ReadWriteSetAnalyzerTests</c>).
/// </summary>
public class PsqlDeclarationExtractorTests
{
    private static (BlockStatement Body, string Source) Build(string sql)
    {
        var root = SqlParser.Parse(sql).Root;
        var ddl = root.Statements.OfType<DdlStatement>().First();
        return (ddl.Body!, sql);
    }

    private const string Sql = """
        create procedure p (a integer) returns (r integer) as
        declare variable v d_amount not null;
        declare w numeric(15,2) default 0;
        declare x varchar(80);
        declare y type of column customers.name;
        begin
          v = 1;
          r = v;
        end
        """;

    [Fact]
    public void Extract_ReturnsEveryLocal_InSourceOrder()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        Assert.Equal(new[] { "V", "W", "X", "Y" }, decls.Locals.Select(l => l.Name));
    }

    [Fact]
    public void Extract_KeepsTheDeclarationVerbatim_ForR3()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        // R3: the declaration is copied 1:1 from source — the domain and NOT NULL must survive so the
        // statement's own assignments keep domain semantics.
        Assert.Equal("declare variable v d_amount not null;", decls.Locals[0].Verbatim);
        Assert.Equal("declare w numeric(15,2) default 0;", decls.Locals[1].Verbatim);
    }

    [Fact]
    public void TypeSpec_IsTheDomainName_ForADomainTypedLocal()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        // A bare identifier the resolver will look up as a domain (R2) — the NOT NULL is not part of the type.
        Assert.Equal("d_amount", decls.Locals[0].TypeSpec);
    }

    [Fact]
    public void TypeSpec_CapturesAParametrisedBuiltin_Whole()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        Assert.Equal("numeric(15,2)", decls.Locals[1].TypeSpec); // DEFAULT 0 is excluded
        Assert.Equal("varchar(80)", decls.Locals[2].TypeSpec);
    }

    [Fact]
    public void TypeSpec_CarriesTypeOfKeywords_ForTheResolverToInterpret()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        Assert.Equal("type of column customers.name", decls.Locals[3].TypeSpec);
    }

    [Fact]
    public void Extract_CarriesNoSubRoutines_ForARoutineWithoutAny()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        // R5's carrier is empty when the routine declares no local sub-routines — nothing to lose.
        Assert.Empty(decls.SubRoutines);
    }

    [Fact]
    public void Extract_CarriesLocalSubRoutines_Verbatim_ForR5()
    {
        // Stage X / D9: a local DECLARE PROCEDURE/FUNCTION is carried into the harness 1:1 (R5), so a call
        // in this frame binds to the local, never a like-named global. The verbatim slice includes the body.
        const string sql = """
            create procedure p (n integer) returns (r integer) as
            declare procedure sp (a integer) returns (o integer) as
            begin
              o = a * 2;
            end
            declare function f (a integer) returns integer as
            begin
              return a + 1;
            end
            begin
              execute procedure sp(n) returning_values r;
            end
            """;
        var (body, source) = Build(sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        Assert.Equal(2, decls.SubRoutines.Count);
        Assert.StartsWith("declare procedure sp (a integer) returns (o integer) as", decls.SubRoutines[0]);
        Assert.Contains("o = a * 2;", decls.SubRoutines[0]);
        Assert.StartsWith("declare function f (a integer) returns integer as", decls.SubRoutines[1]);
        Assert.Contains("return a + 1;", decls.SubRoutines[1]);
        // The sub-routine's own local variables are NOT lifted into the enclosing frame's Locals — the frame
        // has none of its own here.
        Assert.Empty(decls.Locals);
    }
}
