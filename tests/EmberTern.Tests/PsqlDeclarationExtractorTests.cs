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

    // ── ExtractSignature (Stage X / D9 seam a part 2) ────────────────────────────────────────────────
    // A local sub-routine is not a catalog object, so the debugger reads its parameter/RETURNS types from the
    // AST header (there is no RDB$PROCEDURE_PARAMETERS row). These pin that pure extraction.

    [Fact]
    public void ExtractSignature_ReadsInputAndOutputParams_OfALocalProcedure()
    {
        const string sql = """
            create procedure p (base integer) returns (total integer) as
            declare procedure add_tax (amount integer, rate numeric(15,2)) returns (with_tax integer) as
              declare variable bonus integer;
            begin
              with_tax = amount + bonus;
            end
            begin
              execute procedure add_tax(base, 10) returning_values total;
            end
            """;
        var (body, source) = Build(sql);
        var sub = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(sub, source);

        Assert.Equal(new[] { "AMOUNT", "RATE" }, sig.Inputs.Select(p => p.Name));
        Assert.Equal(new[] { "integer", "numeric(15,2)" }, sig.Inputs.Select(p => p.TypeSpec)); // parametrised whole
        Assert.Equal(new[] { "WITH_TAX" }, sig.Outputs.Select(p => p.Name));
        Assert.Equal("integer", sig.Outputs.Single().TypeSpec);
    }

    [Fact]
    public void ExtractSignature_YieldsNoOutputs_ForALocalFunction()
    {
        const string sql = """
            create procedure p (n integer) returns (r integer) as
            declare function triple (x integer) returns integer as
            begin
              return x * 3;
            end
            begin
              r = triple(n);
            end
            """;
        var (body, source) = Build(sql);
        var fn = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(fn, source);

        Assert.Equal(new[] { "X" }, sig.Inputs.Select(p => p.Name));
        // A local function's single RETURNS <type> is not a named output parameter (its value returns via RETURN).
        Assert.Empty(sig.Outputs);
        // …but its return type IS surfaced (R2 input for the Expression Harness result column, D9 seam c).
        Assert.Equal("integer", sig.ReturnType);
    }

    // ── ExtractSignature.ReturnType (Stage X / D9 seam c, §6.4) ───────────────────────────────────────────
    // A local FUNCTION's single RETURNS <type> is the R2 base-type input for the Expression Harness that
    // carries a stepped-into function's RETURN value. A PROCEDURE has named outputs, not a return type ⇒ null.

    [Fact]
    public void ExtractSignature_ReturnTypeIsNull_ForAProcedure()
    {
        const string sql = """
            create procedure p (n integer) returns (r integer) as
            declare procedure sp (a integer) returns (o integer) as
            begin
              o = a;
            end
            begin
              execute procedure sp(n) returning_values r;
            end
            """;
        var (body, source) = Build(sql);
        var sub = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(sub, source);

        Assert.Null(sig.ReturnType); // a procedure's outputs are the named Outputs, not a scalar return type
    }

    [Fact]
    public void ExtractSignature_ReturnType_CapturesAParametrisedType_Whole()
    {
        const string sql = """
            create procedure p as
            declare function label (n integer) returns varchar(80) as
            begin
              return 'x';
            end
            begin
              r = label(1);
            end
            """;
        var (body, source) = Build(sql);
        var fn = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(fn, source);

        // The type spec stops before the header's AS and captures the parametrised type whole.
        Assert.Equal("varchar(80)", sig.ReturnType);
    }

    [Fact]
    public void ExtractSignature_ReturnType_KeepsADomainName_ForR2Resolution()
    {
        const string sql = """
            create procedure p as
            declare function fee (n integer) returns d_amount as
            begin
              return 0;
            end
            begin
              r = fee(1);
            end
            """;
        var (body, source) = Build(sql);
        var fn = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(fn, source);

        // A domain return type comes back bare — the Firebird layer resolves its base type from RDB$FIELDS (R2).
        Assert.Equal("d_amount", sig.ReturnType);
    }

    [Fact]
    public void ExtractSignature_HandlesANoParameterProcedure()
    {
        const string sql = """
            create procedure p returns (r integer) as
            declare procedure sp returns (o integer) as
            begin
              o = 1;
            end
            begin
              execute procedure sp returning_values r;
            end
            """;
        var (body, source) = Build(sql);
        var sub = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(sub, source);

        Assert.Empty(sig.Inputs);
        Assert.Equal(new[] { "O" }, sig.Outputs.Select(p => p.Name));
    }

    [Fact]
    public void ExtractSignature_KeepsADomainParamType_ForR2Resolution()
    {
        // A parameter typed by a user domain comes back as the bare domain name — the Firebird layer resolves
        // its base type from RDB$FIELDS (R2), exactly as it does for a domain-typed local variable.
        const string sql = """
            create procedure p (base integer) returns (total integer) as
            declare procedure charge (amount d_amount) returns (net d_amount) as
            begin
              net = amount;
            end
            begin
              execute procedure charge(base) returning_values total;
            end
            """;
        var (body, source) = Build(sql);
        var sub = body.LocalRoutines.Single();

        var sig = PsqlDeclarationExtractor.ExtractSignature(sub, source);

        Assert.Equal("d_amount", sig.Inputs.Single().TypeSpec);
        Assert.Equal("d_amount", sig.Outputs.Single().TypeSpec);
    }
}
