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
    public void Extract_DoesNotCarrySubRoutines_InD2()
    {
        var (body, source) = Build(Sql);

        var decls = PsqlDeclarationExtractor.Extract(body, source);

        // R5's carrier exists but is empty in D2 — a D2 routine has no in-scope sub-routine declarations in
        // the DECLARE section (they are D9), so nothing is lost.
        Assert.Empty(decls.SubRoutines);
    }
}
