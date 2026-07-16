using System.Linq;
using EmberTern.App.Completion;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Snippets;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The keyword live-template Snippet Engine (Etap 5 / M8, design §5.11 / §22) — pure Core: the
/// template set, each template's text + placeholder (tab-stop) offsets, and PSQL/top-level scope
/// gating from the Semantic Model. The App-side Tab-between-stops expansion is manual smoke.
/// </summary>
public class SnippetEngineTests
{
    private static string[] Keywords(System.Collections.Generic.IReadOnlyList<SnippetTemplate> ts)
        => ts.Select(t => t.Keyword).ToArray();

    // ── The template library ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AllTemplates_ContainsExpectedTriggers()
    {
        var kws = Keywords(SnippetEngine.AllTemplates);
        foreach (var expected in new[]
                 {
                     "if", "while", "for select", "begin", "case", "declare",
                     "execute", "create procedure", "create function", "create trigger",
                     "create exception", "create domain", "create index",
                 })
        {
            Assert.Contains(expected, kws);
        }
    }

    [Fact]
    public void EveryTemplate_HasValidNonOverlappingPlaceholders_AndIsDeterministic()
    {
        foreach (var t in SnippetEngine.AllTemplates)
        {
            var s = t.Create();
            Assert.False(string.IsNullOrEmpty(s.Text), $"{t.Keyword}: empty text");

            int prevEnd = 0;
            foreach (var ph in s.Placeholders.OrderBy(p => p.Start))
            {
                Assert.True(ph.Length > 0, $"{t.Keyword}: zero-length placeholder");
                Assert.True(ph.Start >= prevEnd, $"{t.Keyword}: overlapping placeholders");
                Assert.True(ph.Start + ph.Length <= s.Text.Length, $"{t.Keyword}: placeholder out of bounds");
                prevEnd = ph.Start + ph.Length;
            }

            // Deterministic: same text on repeat generation.
            Assert.Equal(s.Text, t.Create().Text);
        }
    }

    [Fact]
    public void IfTemplate_ShapeAndFirstStop()
    {
        var t = SnippetEngine.AllTemplates.Single(x => x.Keyword == "if");
        var s = t.Create();
        Assert.Contains("if (", s.Text);
        Assert.Contains("then", s.Text);
        Assert.Contains("begin", s.Text);
        Assert.Contains("end", s.Text);
        var first = s.Placeholders.OrderBy(p => p.Start).First();
        Assert.Equal("condition", s.Text.Substring(first.Start, first.Length));
    }

    [Fact]
    public void DeclareTemplate_ExactText_AndStopsMatchTokens()
    {
        var s = SnippetEngine.AllTemplates.Single(x => x.Keyword == "declare").Create();
        Assert.Equal("declare variable name type;", s.Text);
        var stops = s.Placeholders.OrderBy(p => p.Start).ToArray();
        Assert.Equal("name", s.Text.Substring(stops[0].Start, stops[0].Length));
        Assert.Equal("type", s.Text.Substring(stops[1].Start, stops[1].Length));
    }

    // ── Scope gating ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InPsqlBody_OffersControlFlow_NotDdl()
    {
        const string sql = "create procedure p as begin  end";
        int caret = sql.IndexOf("begin", System.StringComparison.Ordinal) + "begin ".Length;
        var model = SemanticModel.Build(sql);

        var kws = Keywords(SnippetEngine.GetSnippets(model, caret));
        Assert.Contains("if", kws);
        Assert.Contains("while", kws);
        Assert.Contains("for select", kws);
        Assert.Contains("declare", kws);
        Assert.DoesNotContain("execute", kws);
        Assert.DoesNotContain("create procedure", kws);
    }

    [Fact]
    public void OutsidePsql_OffersTopLevel_NotControlFlow()
    {
        const string sql = "select 1 from rdb$database";
        var model = SemanticModel.Build(sql);

        var kws = Keywords(SnippetEngine.GetSnippets(model, 3));
        Assert.Contains("execute", kws);
        Assert.Contains("create procedure", kws);
        Assert.DoesNotContain("if", kws);
        Assert.DoesNotContain("declare", kws);
    }

    // A bare BEGIN…END (the Easy-mode procedure/trigger/function BODY editor, parsed as an anonymous
    // block) must offer PSQL control-flow — that's where `if`/`for`/`declare` are most needed (P7).
    [Fact]
    public void InAnonymousBlockBody_OffersControlFlow()
    {
        const string sql = "begin  end";
        int caret = sql.IndexOf("begin", System.StringComparison.Ordinal) + "begin ".Length;
        var kws = Keywords(SnippetEngine.GetSnippets(SemanticModel.Build(sql), caret));
        Assert.Contains("if", kws);
        Assert.Contains("for select", kws);
        Assert.DoesNotContain("execute", kws);
    }

    // An ad-hoc EXECUTE BLOCK in the SQL editor is a routine body too — control-flow inside it (P7).
    [Fact]
    public void InsideExecuteBlockBody_OffersControlFlow()
    {
        const string sql = "execute block as begin  end";
        int caret = sql.IndexOf("begin", System.StringComparison.Ordinal) + "begin ".Length;
        var kws = Keywords(SnippetEngine.GetSnippets(SemanticModel.Build(sql), caret));
        Assert.Contains("if", kws);
        Assert.Contains("while", kws);
        Assert.DoesNotContain("create procedure", kws);
    }

    // ── P7: short snippet-keyword prefixes still auto-trigger (2-char "if") ───────────────────

    [Fact]
    public void WordMayTriggerSnippet_ShortSnippetPrefix_Triggers()
    {
        var all = SnippetEngine.AllTemplates;
        Assert.True(SqlCompletionController.WordMayTriggerSnippet("if", all));   // 2-char snippet keyword
        Assert.True(SqlCompletionController.WordMayTriggerSnippet("IF", all));   // case-insensitive
        Assert.True(SqlCompletionController.WordMayTriggerSnippet("for", all));  // prefix of "for select"
    }

    [Fact]
    public void WordMayTriggerSnippet_NonPrefixOrTooShort_DoesNot()
    {
        var all = SnippetEngine.AllTemplates;
        Assert.False(SqlCompletionController.WordMayTriggerSnippet("x", all));    // 1 char
        Assert.False(SqlCompletionController.WordMayTriggerSnippet("", all));     // empty
        Assert.False(SqlCompletionController.WordMayTriggerSnippet("zz", all));   // not a snippet prefix
    }

    [Fact]
    public void NullModel_ReturnsEmpty_NoThrow()
        => Assert.Empty(SnippetEngine.GetSnippets(null!, 0));
}
