using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage 7 (Diagnostics) — Easy-mode routine editors. The body editor holds only the BODY (no CREATE
/// header, no DECLARE section); the parameters and DECLAREd variables live in the surrounding grids and
/// reach the model as <b>ambient symbols</b> seeded into the root scope (gotcha #218 — the same seam
/// completion/highlighting use). These pin that the <see cref="DiagnosticsEngine"/> honours those ambient
/// symbols, so a param/variable declared in a grid is NOT falsely flagged as unresolved — the concern a
/// manual QA pass raised against S3.
/// </summary>
public class EasyModeDiagnosticsTests
{
    // The Easy-mode BODY fragment exactly as the editor holds it — no header, no DECLARE section.
    private const string Body = @"begin
  dzien = :dataod;
  while (:dzien <= :datado) do
  begin
    dzien = :dzien + 1;
    test = :test + 1;
  end
end";

    private static IReadOnlyList<Diagnostic> Analyze(IReadOnlyList<Symbol>? ambient)
        => DiagnosticsEngine.Analyze(SemanticModel.Build(Body, metadata: null, ambientSymbols: ambient));

    [Fact]
    public void AmbientParamsAndVariables_SuppressAllUnresolvedDiagnostics()
    {
        var ambient = new List<Symbol>
        {
            new ParameterSymbol("DATAOD") { Direction = ParameterDirection.Input },
            new ParameterSymbol("DATADO") { Direction = ParameterDirection.Input },
            new VariableSymbol("DZIEN"),
            new VariableSymbol("TEST"),
        };

        // Every :name resolves against an ambient param/variable → the model is clean.
        Assert.Empty(Analyze(ambient));
    }

    [Fact]
    public void AmbientParams_Resolve_EvenWhenSomeVariablesAreNotSeeded()
    {
        // Mirrors the QA screenshot: Input params populated, the Variables grid empty. The PARAMETERS
        // must still resolve (no false positive on :dataod / :datado); only the genuinely-undeclared
        // variables (:dzien, :test) are reported.
        var ambient = new List<Symbol>
        {
            new ParameterSymbol("DATAOD") { Direction = ParameterDirection.Input },
            new ParameterSymbol("DATADO") { Direction = ParameterDirection.Input },
        };

        var diagnostics = Analyze(ambient);

        Assert.All(diagnostics, d => Assert.Equal(DiagnosticCategory.UnresolvedVariable, d.Category));
        var names = diagnostics.Select(d => Body.Substring(d.Start, d.Length)).ToList();
        Assert.DoesNotContain(":dataod", names);
        Assert.DoesNotContain(":datado", names);
        Assert.Contains(":dzien", names);
        Assert.Contains(":test", names);
    }

    [Fact]
    public void NoAmbient_FlagsEverything_ConfirmingTheModelIsFragmentPlusAmbientOnly()
    {
        // With no ambient at all (the fragment alone), even the parameters are unresolved — proving the
        // model analyses the visible fragment + ambient symbols, never a hidden full generated source.
        var names = Analyze(ambient: null).Select(d => Body.Substring(d.Start, d.Length)).ToList();
        Assert.Contains(":dataod", names);
        Assert.Contains(":datado", names);
    }
}
