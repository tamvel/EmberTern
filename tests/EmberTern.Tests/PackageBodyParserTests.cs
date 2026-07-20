using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X / D11 (packages) — <see cref="SqlParser.ParsePackageBodyMembers"/>. Firebird stores a package
/// body as one <c>BEGIN … END</c> blob (<c>RDB$PACKAGE_BODY_SOURCE</c>); this pins that the blob is turned
/// into member <see cref="SubroutineDeclaration"/> nodes (each a bare <c>PROCEDURE/FUNCTION … AS … BEGIN … END</c>,
/// the D9 sub-routine shape without <c>DECLARE</c>) so the debugger can build a frame from a member and read
/// its signature with the existing D9 machinery. Pure parse — no server, no metadata.
/// </summary>
public class PackageBodyParserTests
{
    // Mirrors the lab PKG_DBG body (RDB$PACKAGE_BODY_SOURCE): a private double + a public add + a public run
    // that calls both siblings. Blank / null yield an empty list.
    private const string PkgDbgBody = @"BEGIN
  /* the private routine is body-only */
  PROCEDURE PRIV_DOUBLE(P_N INTEGER) RETURNS (R INTEGER)
  AS
  BEGIN
    R = P_N * 2;
  END

  PROCEDURE PUB_ADD(P_N INTEGER) RETURNS (R INTEGER)
  AS
  BEGIN
    R = P_N + 1;
  END

  PROCEDURE PUB_RUN(P_N INTEGER) RETURNS (R INTEGER)
  AS
    DECLARE VARIABLE A INTEGER;
    DECLARE VARIABLE B INTEGER;
  BEGIN
    EXECUTE PROCEDURE PRIV_DOUBLE(:P_N) RETURNING_VALUES :A;
    EXECUTE PROCEDURE PUB_ADD(:P_N) RETURNING_VALUES :B;
    R = A + B;
  END
END";

    [Fact]
    public void Parses_AllMembers_InOrder_WithBodies()
    {
        var members = SqlParser.ParsePackageBodyMembers(PkgDbgBody);

        Assert.Equal(new[] { "PRIV_DOUBLE", "PUB_ADD", "PUB_RUN" }, members.Select(m => m.Name).ToArray());
        Assert.All(members, m => Assert.Equal(SubroutineKind.Procedure, m.Kind));
        Assert.All(members, m => Assert.NotNull(m.Body));
        // Spans index into the source blob (so a debugger frame can use the blob as its source).
        Assert.All(members, m =>
        {
            Assert.True(m.Start >= 0);
            Assert.True(m.Start + m.Length <= PkgDbgBody.Length);
        });
    }

    [Fact] // the sibling calls inside PUB_RUN must be visible as unqualified ExecuteProcedureStatements
    public void MemberBody_ExposesSiblingCalls_Unqualified()
    {
        var pubRun = SqlParser.ParsePackageBodyMembers(PkgDbgBody).Single(m => m.Name == "PUB_RUN");
        Assert.NotNull(pubRun.Body);

        var calls = pubRun.Body!.DescendantNodesAndSelf().OfType<ExecuteProcedureStatement>().ToList();
        Assert.Equal(new[] { "PRIV_DOUBLE", "PUB_ADD" }, calls.Select(c => c.ProcedureName).ToArray());
        Assert.All(calls, c => Assert.Null(c.PackageName)); // a sibling call is unqualified — resolved against the frame's package (Seam B)
    }

    [Fact]
    public void Parses_FunctionMember()
    {
        const string body = @"BEGIN
  FUNCTION F(N INTEGER) RETURNS INTEGER
  AS
  BEGIN
    RETURN N * 2;
  END
END";
        var member = Assert.Single(SqlParser.ParsePackageBodyMembers(body));
        Assert.Equal("F", member.Name);
        Assert.Equal(SubroutineKind.Function, member.Kind);
        Assert.NotNull(member.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void NullOrBlank_YieldsEmpty(string? body) => Assert.Empty(SqlParser.ParsePackageBodyMembers(body));

    [Fact] // a body with no member routines (defensive) → empty, never throws
    public void EmptyBody_YieldsEmpty() => Assert.Empty(SqlParser.ParsePackageBodyMembers("BEGIN END"));

    // ── ReconstructPackageMemberSource (D11 seam C) — the one shared "CREATE " + slice reconstruction ──

    [Fact] // reconstruction prefixes CREATE and slices the exact member text; the result parses as a CREATE PROCEDURE
    public void Reconstruct_ProcedureMember_ProducesStandaloneCreate()
    {
        var source = SqlParser.ReconstructPackageMemberSource(PkgDbgBody, "PUB_RUN", SubroutineKind.Procedure);
        Assert.NotNull(source);
        Assert.StartsWith("CREATE PROCEDURE PUB_RUN", source);

        // It parses back into a DdlStatement with a runnable body (the same shape a stored routine has).
        var ddl = SqlParser.Parse(source!).Root.Statements.OfType<DdlStatement>().Single();
        Assert.NotNull(ddl.Body);
        // Its sibling calls survive the reconstruction (a package root resolves them against its package).
        var calls = ddl.Body!.DescendantNodesAndSelf().OfType<ExecuteProcedureStatement>().Select(c => c.ProcedureName).ToArray();
        Assert.Equal(new[] { "PRIV_DOUBLE", "PUB_ADD" }, calls);
    }

    [Fact] // the blob-taking overload agrees with the members-taking overload (both go through the one slicer)
    public void Reconstruct_BlobOverload_MatchesMembersOverload()
    {
        var members = SqlParser.ParsePackageBodyMembers(PkgDbgBody);
        var viaBlob = SqlParser.ReconstructPackageMemberSource(PkgDbgBody, "PUB_ADD", SubroutineKind.Procedure);
        var viaMembers = SqlParser.ReconstructPackageMemberSource(PkgDbgBody, members, "PUB_ADD", SubroutineKind.Procedure);
        Assert.Equal(viaMembers, viaBlob);
        Assert.StartsWith("CREATE PROCEDURE PUB_ADD", viaBlob);
    }

    [Fact] // a missing / wrong-kind / blank member yields null (→ step over, source unavailable)
    public void Reconstruct_MissingOrWrongKind_YieldsNull()
    {
        Assert.Null(SqlParser.ReconstructPackageMemberSource(PkgDbgBody, "NOPE", SubroutineKind.Procedure));
        Assert.Null(SqlParser.ReconstructPackageMemberSource(PkgDbgBody, "PUB_RUN", SubroutineKind.Function));
        Assert.Null(SqlParser.ReconstructPackageMemberSource(null, "PUB_RUN", SubroutineKind.Procedure));
    }
}
