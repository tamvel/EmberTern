using System.Collections.Generic;
using EmberTern.Core.Sql.Debugging;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D2 seam (b): the Evaluation Harness builder (spec §3.2) and its
/// §3.4 declaration rules R1–R5. Pure — no server, no UI: the harness text + parameter binding + write-back
/// map are a pure function of the request, which is exactly why the non-negotiable rules are pinned here
/// (fidelity vs real execution is seam c's lab proof; the RULES are proven here).
/// </summary>
public class HarnessBuilderTests
{
    private static HarnessVariable Var(string name, string baseType, object? value, bool hasValue, string? decl = null)
        => new(name, decl ?? $"DECLARE {name} {baseType};", baseType, value, hasValue);

    [Fact]
    public void Statement_InjectsReads_WithParametersAndAssignments()
    {
        var req = new HarnessRequest
        {
            Fragment = "V = W + 1;",
            Variables = new[] { Var("V", "INTEGER", 5, true), Var("W", "INTEGER", 3, true) },
            Reads = new[] { "V", "W" },
            Writes = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        // Two injected params, in read order, bound to the current values.
        Assert.Equal(new object?[] { 5, 3 }, result.Parameters);
        Assert.Contains("EXECUTE BLOCK (ET_P_V INTEGER = ?, ET_P_W INTEGER = ?)", result.Sql);
        Assert.Contains("RETURNS (ET_O_V INTEGER)", result.Sql);
        Assert.Contains("V = ET_P_V;", result.Sql);
        Assert.Contains("W = ET_P_W;", result.Sql);
        Assert.Contains("V = W + 1;", result.Sql);   // fragment verbatim
        Assert.Contains("ET_O_V = V;", result.Sql);   // write-back
        Assert.Contains("SUSPEND;", result.Sql);
        Assert.Equal(new[] { new HarnessWriteBack("ET_O_V", "V") }, result.WriteBacks);
        Assert.Null(result.ResultColumn);
    }

    // ── R1: never assign an injected NULL ─────────────────────────────────────────────────────────

    [Fact]
    public void R1_NullValuedRead_IsNeitherParameterNorAssigned()
    {
        var req = new HarnessRequest
        {
            Fragment = "V = W;",
            // W is read but currently NULL — a declared variable is already NULL; injecting V=NULL is what
            // crashes a NOT NULL-domain variable.
            Variables = new[] { Var("V", "INTEGER", 1, true), Var("W", "INTEGER", null, true) },
            Reads = new[] { "V", "W" },
            Writes = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Equal(new object?[] { 1 }, result.Parameters);   // only V injected
        Assert.DoesNotContain("ET_P_W", result.Sql);             // W is not a parameter
        Assert.DoesNotContain("W = ET_P_W", result.Sql);         // W is not assigned
        Assert.Contains("V = ET_P_V;", result.Sql);
    }

    [Fact]
    public void R1_AbsentValueRead_IsNotInjected()
    {
        var req = new HarnessRequest
        {
            Fragment = "V = 1;",
            Variables = new[] { Var("V", "INTEGER", null, hasValue: false) },
            Reads = new[] { "V" },
            Writes = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Empty(result.Parameters);
        Assert.DoesNotContain("ET_P_V", result.Sql);
        Assert.DoesNotContain("(", result.Sql.Split('\n')[0]); // no param list on the header line
    }

    // ── R2: parameters and RETURNS use BASE types, never domains ──────────────────────────────────

    [Fact]
    public void R2_ParameterAndReturns_UseBaseType_NotDomain()
    {
        var req = new HarnessRequest
        {
            Fragment = "V = 1;",
            // Declared verbatim with the domain + null suffix (R3), but the param/RETURNS type is the base
            // type INTEGER (R2) — a domain-typed RETURNS would re-validate on SUSPEND.
            Variables = new[]
            {
                new HarnessVariable("V", "DECLARE V T_ILOSCSTAN NOT NULL;", "INTEGER", 7, true),
            },
            Reads = new[] { "V" },
            Writes = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Contains("ET_P_V INTEGER = ?", result.Sql);      // base type on the parameter
        Assert.Contains("RETURNS (ET_O_V INTEGER)", result.Sql); // base type on the RETURNS column
        Assert.DoesNotContain("ET_P_V T_ILOSCSTAN", result.Sql);
    }

    // ── R3: frame variables declared VERBATIM ─────────────────────────────────────────────────────

    [Fact]
    public void R3_FrameVariables_AreDeclaredVerbatim()
    {
        var req = new HarnessRequest
        {
            Fragment = "V = 1;",
            Variables = new[]
            {
                new HarnessVariable("V", "DECLARE VARIABLE V T_ILOSCSTAN NOT NULL /* stan */;", "INTEGER"),
            },
            Reads = System.Array.Empty<string>(),
            Writes = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Contains("DECLARE VARIABLE V T_ILOSCSTAN NOT NULL /* stan */;", result.Sql);
    }

    // ── R4: inject only reads, return only writes ─────────────────────────────────────────────────

    [Fact]
    public void R4_UnreferencedVariable_IsDeclaredButNeitherInjectedNorReturned()
    {
        var req = new HarnessRequest
        {
            Fragment = "V = 1;",
            Variables = new[] { Var("V", "INTEGER", 1, true), Var("UNUSED", "INTEGER", 9, true) },
            Reads = new[] { "V" },
            Writes = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Contains("DECLARE UNUSED INTEGER;", result.Sql); // declared (R3 pool)
        Assert.DoesNotContain("ET_P_UNUSED", result.Sql);        // not injected (R4)
        Assert.DoesNotContain("ET_O_UNUSED", result.Sql);        // not returned (R4)
        Assert.DoesNotContain(9, result.Parameters);
    }

    // ── R5: sub-routine declarations always carried, verbatim ─────────────────────────────────────

    [Fact]
    public void R5_SubRoutineDeclarations_AreAlwaysCarriedVerbatim()
    {
        const string localF = "DECLARE FUNCTION LOCAL_F(X INTEGER) RETURNS INTEGER AS BEGIN RETURN X * 10; END";
        var req = new HarnessRequest
        {
            Fragment = "V = LOCAL_F(V);",
            Variables = new[] { Var("V", "INTEGER", 3, true) },
            Reads = new[] { "V" },
            Writes = new[] { "V" },
            SubRoutines = new[] { localF },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Contains(localF, result.Sql);
        // Variable declaration precedes the sub-routine declaration (Firebird's required order).
        Assert.True(result.Sql.IndexOf("DECLARE V INTEGER;", System.StringComparison.Ordinal)
                    < result.Sql.IndexOf("DECLARE FUNCTION LOCAL_F", System.StringComparison.Ordinal));
    }

    // ── Expression mode (conditions / watches) ────────────────────────────────────────────────────

    [Fact]
    public void ExpressionMode_EvaluatesFragmentIntoResultColumn()
    {
        var req = new HarnessRequest
        {
            Fragment = "V > 0",
            Mode = HarnessMode.Expression,
            ExpressionResultType = "BOOLEAN",
            Variables = new[] { Var("V", "INTEGER", 5, true) },
            Reads = new[] { "V" },
        };

        var result = HarnessBuilder.Build(req);

        Assert.Equal("ET_DBG_RESULT", result.ResultColumn);
        Assert.Contains("RETURNS (ET_DBG_RESULT BOOLEAN)", result.Sql);
        Assert.Contains("ET_DBG_RESULT = V > 0;", result.Sql);
        Assert.Contains("SUSPEND;", result.Sql);
        Assert.Empty(result.WriteBacks); // a condition writes nothing back
    }

    [Fact]
    public void ExpressionMode_RequiresAResultType()
        => Assert.Throws<System.ArgumentException>(() => HarnessBuilder.Build(new HarnessRequest
        {
            Fragment = "V > 0",
            Mode = HarnessMode.Expression,
        }));

    // ── A statement with no writes and no reads is a plain executable block (no RETURNS/SUSPEND) ──

    [Fact]
    public void Statement_NoReadsNoWrites_IsPlainExecutableBlock()
    {
        var req = new HarnessRequest
        {
            Fragment = "INSERT INTO LOG (MSG) VALUES ('x');",
            Variables = System.Array.Empty<HarnessVariable>(),
        };

        var result = HarnessBuilder.Build(req);

        Assert.DoesNotContain("RETURNS", result.Sql);
        Assert.DoesNotContain("SUSPEND", result.Sql);
        Assert.Contains("INSERT INTO LOG (MSG) VALUES ('x');", result.Sql);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public void Statement_FragmentWithoutTerminator_GetsOne()
    {
        var req = new HarnessRequest { Fragment = "V = 1", Variables = System.Array.Empty<HarnessVariable>() };
        var result = HarnessBuilder.Build(req);
        Assert.Contains("V = 1;", result.Sql);
    }
}
