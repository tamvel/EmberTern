using System.Linq;
using EmberTern.Core.Sql.Language.Completion;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Ctrl+Space inside a procedure must offer the routine's PARAMETERS and LOCAL VARIABLES,
/// exactly as it offers columns/aliases in a plain query.
/// </summary>
public class PsqlCompletionScopeTests
{
    private const string Source = @"CREATE OR ALTER PROCEDURE SP_TEST(P_CUSTOMER_ID INTEGER, P_NAME VARCHAR(50))
RETURNS (R_TOTAL NUMERIC(15,2))
AS
  DECLARE VARIABLE V_COUNT INTEGER;
  DECLARE VARIABLE V_LABEL VARCHAR(20);
BEGIN
  V_COUNT = 0;

END";

    private static string[] NamesAt(string sql, int offset, CompletionItemKind kind)
    {
        var model = SemanticModel.Build(sql);
        return CompletionEngine.GetCompletions(model, offset).Items
            .Where(i => i.Kind == kind)
            .Select(i => i.DisplayText)
            .ToArray();
    }

    // Caret sits inside the body, just after "V_COUNT = 0;".
    private static int BodyCaret =>
        Source.IndexOf("V_COUNT = 0;", System.StringComparison.Ordinal) + "V_COUNT = 0;".Length;

    [Fact]
    public void SourceMode_OffersInputParameters()
    {
        var names = NamesAt(Source, BodyCaret, CompletionItemKind.Parameter);
        Assert.Contains("P_CUSTOMER_ID", names);
        Assert.Contains("P_NAME", names);
    }

    [Fact]
    public void SourceMode_OffersOutputParameters()
    {
        var names = NamesAt(Source, BodyCaret, CompletionItemKind.Parameter);
        Assert.Contains("R_TOTAL", names);
    }

    [Fact]
    public void SourceMode_OffersLocalVariables()
    {
        var names = NamesAt(Source, BodyCaret, CompletionItemKind.Variable);
        Assert.Contains("V_COUNT", names);
        Assert.Contains("V_LABEL", names);
    }

    // ── Easy mode: the editor holds ONLY the body; params/variables live in the grids ──

    private const string BodyOnly = @"BEGIN
  V_COUNT = 0;
END";

    [Fact]
    public void EasyMode_WithoutAmbientSymbols_SeesNothing_TheOriginalBug()
    {
        var model = SemanticModel.Build(BodyOnly);
        var items = CompletionEngine.GetCompletions(model, BodyOnly.IndexOf("V_COUNT", System.StringComparison.Ordinal)).Items;
        Assert.DoesNotContain(items, i => i.Kind == CompletionItemKind.Parameter);
        Assert.DoesNotContain(items, i => i.Kind == CompletionItemKind.Variable);
    }

    [Fact]
    public void EasyMode_AmbientSymbols_MakeParamsAndVariablesVisible()
    {
        // What the Easy-mode editors now seed from their grids.
        var ambient = new Symbol[]
        {
            new ParameterSymbol("P_CUSTOMER_ID") { Direction = ParameterDirection.Input },
            new ParameterSymbol("R_TOTAL") { Direction = ParameterDirection.Output },
            new VariableSymbol("V_COUNT"),
            new VariableSymbol("V_LABEL"),
        };
        var model = SemanticModel.Build(BodyOnly, metadata: null, ambientSymbols: ambient);
        int caret = BodyOnly.IndexOf("V_COUNT = 0;", System.StringComparison.Ordinal) + "V_COUNT = 0;".Length;
        var items = CompletionEngine.GetCompletions(model, caret).Items;

        var pars = items.Where(i => i.Kind == CompletionItemKind.Parameter).Select(i => i.DisplayText).ToArray();
        var vars = items.Where(i => i.Kind == CompletionItemKind.Variable).Select(i => i.DisplayText).ToArray();
        Assert.Contains("P_CUSTOMER_ID", pars);
        Assert.Contains("R_TOTAL", pars);
        Assert.Contains("V_COUNT", vars);
        Assert.Contains("V_LABEL", vars);
    }

    // A declaration written in the text must win over an ambient one of the same name —
    // inner scopes are searched first, so the model can't end up with a stale duplicate.
    [Fact]
    public void TextDeclaration_ShadowsAmbientOfTheSameName()
    {
        var ambient = new Symbol[] { new VariableSymbol("V_COUNT") };
        var model = SemanticModel.Build(Source, metadata: null, ambientSymbols: ambient);
        var vars = CompletionEngine.GetCompletions(model, BodyCaret).Items
            .Where(i => i.Kind == CompletionItemKind.Variable)
            .Select(i => i.DisplayText)
            .ToArray();
        Assert.Single(vars, v => v == "V_COUNT"); // exactly one, not duplicated
    }
}
