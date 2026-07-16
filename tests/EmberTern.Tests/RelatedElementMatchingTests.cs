using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Matching;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage 8 / M1 — the pure-Core "Related Elements Highlighting" matcher and its producers: matching
/// brackets (via the shared lexer, caret-adjacent), matching BEGIN/END (via the AST), the semantic
/// caret-symbol references, and selection-word occurrences. Offline — no window.
/// </summary>
public class RelatedElementMatchingTests
{
    // ── Bracket matching (model-independent; uses the shared SqlLexer) ───────────────────────────

    private static List<TextSpan> Brackets(string text, int caret)
    {
        var into = new List<TextSpan>();
        new BracketMatchProducer().Collect(new MatchContext(text, caret, null, null), into);
        return into;
    }

    private static void AssertPair(IReadOnlyCollection<TextSpan> spans, TextSpan a, TextSpan b)
    {
        Assert.Equal(2, spans.Count);
        Assert.Contains(a, spans);
        Assert.Contains(b, spans);
    }

    [Fact]
    public void Paren_CaretAfterOpen_MatchesClose()
    {
        const string text = "select (a+b)";      // '(' at 7, ')' at 11
        AssertPair(Brackets(text, 8), new TextSpan(7, 1), new TextSpan(11, 1));
    }

    [Fact]
    public void Paren_CaretBeforeClose_MatchesOpen()
    {
        const string text = "select (a+b)";
        AssertPair(Brackets(text, 11), new TextSpan(7, 1), new TextSpan(11, 1));
    }

    [Fact]
    public void Paren_CaretBeforeOpen_Matches()
    {
        const string text = "select (a+b)";
        AssertPair(Brackets(text, 7), new TextSpan(7, 1), new TextSpan(11, 1));
    }

    [Fact]
    public void EmptyParens_CaretBetween_Matches()
    {
        const string text = "count()";           // '(' at 5, ')' at 6
        AssertPair(Brackets(text, 6), new TextSpan(5, 1), new TextSpan(6, 1));
    }

    [Fact]
    public void NestedParens_CaretOnOuter_MatchesOuter()
    {
        const string text = "((a))";             // ( ( a ) )  at 0 1 _ 3 4
        AssertPair(Brackets(text, 0), new TextSpan(0, 1), new TextSpan(4, 1));
    }

    [Fact]
    public void NestedParens_CaretOnInner_MatchesInner()
    {
        const string text = "((a))";
        AssertPair(Brackets(text, 1), new TextSpan(1, 1), new TextSpan(3, 1));
    }

    [Fact]
    public void Bracket_InsideStringLiteral_NotMatched()
    {
        const string text = "select '(' from t";  // the '(' at index 8 is inside a string literal
        Assert.Empty(Brackets(text, 9));
        Assert.Empty(Brackets(text, 8));
    }

    [Fact]
    public void Bracket_InsideComment_NotMatched()
    {
        const string text = "select 1 -- (\nfrom t"; // '(' at 12 is in a line comment (trivia, not a token)
        Assert.Empty(Brackets(text, 13));
    }

    [Fact]
    public void Bracket_Unmatched_NoResult()
    {
        const string text = "select (a";          // no closing paren
        Assert.Empty(Brackets(text, 8));
    }

    [Fact]
    public void Caret_NotAdjacentToBracket_NoResult()
    {
        const string text = "select (a+b)";
        Assert.Empty(Brackets(text, 2));            // caret inside "select"
    }

    [Fact]
    public void SquareBrackets_Match()
    {
        const string text = "a[b]";                // [ at 1, ] at 3
        AssertPair(Brackets(text, 2), new TextSpan(1, 1), new TextSpan(3, 1));
    }

    [Fact]
    public void CurlyBraces_Match()
    {
        const string text = "x{y}";                // { at 1, } at 3
        AssertPair(Brackets(text, 1), new TextSpan(1, 1), new TextSpan(3, 1));
    }

    [Fact]
    public void DifferentFamilies_DoNotCross()
    {
        const string text = "([)]";                // degenerate — '(' matches ')' ignoring the '['
        // '(' at 0, '[' at 1, ')' at 2, ']' at 3. Per-family depth: '(' pairs with ')'.
        AssertPair(Brackets(text, 0), new TextSpan(0, 1), new TextSpan(2, 1));
    }

    [Fact]
    public void ExecuteProcedureCall_FirstStatement_MatchesWithModelPresent()
    {
        // Manual-QA report: bracket matching on the FIRST `execute procedure name(args)` call. Proves the
        // FULL default matcher (all producers, a real model present, like in the app) returns the bracket
        // pair and does not throw — isolating that the earlier "doesn't activate first time" was an App
        // repaint-timing issue, not the matcher.
        const string sql =
            "execute procedure sp$_xxx_pk1_594_mk1(:id_dokument)\n\n" +
            "select status from xxx_zest_faktur_cr(:datado, :dataod)\n\n" +
            "select * from xxx_bc_zaleznosci_wybor_view";
        int open = sql.IndexOf('(');
        int close = sql.IndexOf(')');
        var spans = RelatedElementMatcher.CreateDefault()
            .Match(new MatchContext(sql, open + 1, null, SemanticModel.Build(sql)));
        Assert.Contains(new TextSpan(open, 1), spans);
        Assert.Contains(new TextSpan(close, 1), spans);
    }

    // ── BEGIN / END matching (AST-driven) ────────────────────────────────────────────────────────

    private static List<TextSpan> Blocks(string sql, int caret)
    {
        var into = new List<TextSpan>();
        var model = SemanticModel.Build(sql);
        new BlockMatchProducer().Collect(new MatchContext(sql, caret, null, model), into);
        return into;
    }

    private const string Proc =
        "create procedure p as\nbegin\n  x = 1;\nend";

    [Fact]
    public void Block_CaretOnBegin_MatchesEnd()
    {
        int begin = Proc.IndexOf("begin", StringComparison.Ordinal);
        int end = Proc.LastIndexOf("end", StringComparison.Ordinal);
        AssertPair(Blocks(Proc, begin), new TextSpan(begin, 5), new TextSpan(end, 3));
    }

    [Fact]
    public void Block_CaretOnEnd_MatchesBegin()
    {
        int begin = Proc.IndexOf("begin", StringComparison.Ordinal);
        int end = Proc.LastIndexOf("end", StringComparison.Ordinal);
        AssertPair(Blocks(Proc, end + 3), new TextSpan(begin, 5), new TextSpan(end, 3)); // caret just after END
    }

    [Fact]
    public void Block_CaretInsideBeginKeyword_Matches()
    {
        int begin = Proc.IndexOf("begin", StringComparison.Ordinal);
        int end = Proc.LastIndexOf("end", StringComparison.Ordinal);
        AssertPair(Blocks(Proc, begin + 2), new TextSpan(begin, 5), new TextSpan(end, 3)); // "be|gin"
    }

    [Fact]
    public void Block_CaretElsewhere_NoResult()
    {
        int x = Proc.IndexOf("x = 1", StringComparison.Ordinal);
        Assert.Empty(Blocks(Proc, x));
    }

    [Fact]
    public void NestedBlocks_CaretOnInnerBegin_MatchesInnerPair()
    {
        const string sql = "create procedure p as\nbegin\n  begin\n    x = 1;\n  end\nend";
        int innerBegin = sql.IndexOf("begin", sql.IndexOf("begin", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
        int innerEnd = sql.IndexOf("end", StringComparison.Ordinal); // the FIRST 'end' closes the inner block
        AssertPair(Blocks(sql, innerBegin), new TextSpan(innerBegin, 5), new TextSpan(innerEnd, 3));
    }

    [Fact]
    public void IfThenBlock_CaretOnBegin_MatchesItsEnd()
    {
        const string sql = "create procedure p as\nbegin\n  if (x = 1) then\n  begin\n    y = 2;\n  end\nend";
        int ifBegin = sql.IndexOf("begin", sql.IndexOf("then", StringComparison.Ordinal), StringComparison.Ordinal);
        int ifEnd = sql.IndexOf("end", StringComparison.Ordinal); // inner (IF-body) END comes first
        AssertPair(Blocks(sql, ifBegin), new TextSpan(ifBegin, 5), new TextSpan(ifEnd, 3));
    }

    [Fact]
    public void ExecuteBlock_BeginEnd_Matches()
    {
        const string sql = "execute block as\nbegin\n  x = 1;\nend";
        int begin = sql.IndexOf("begin", StringComparison.Ordinal);
        int end = sql.LastIndexOf("end", StringComparison.Ordinal);
        AssertPair(Blocks(sql, begin), new TextSpan(begin, 5), new TextSpan(end, 3));
    }

    [Fact]
    public void CaseEnd_InsideBlock_NotMatchedAsBlockEnd()
    {
        // A CASE…END is not a BlockStatement — caret on the CASE's END must NOT match the block's BEGIN.
        const string sql = "create procedure p as\nbegin\n  y = case when x = 1 then 2 else 3 end;\nend";
        int caseEnd = sql.IndexOf("end;", StringComparison.Ordinal); // the CASE's end (has the trailing ';')
        Assert.Empty(Blocks(sql, caseEnd));

        // …but the block's own END (the last one) still matches.
        int blockEnd = sql.LastIndexOf("end", StringComparison.Ordinal);
        int begin = sql.IndexOf("begin", StringComparison.Ordinal);
        AssertPair(Blocks(sql, blockEnd), new TextSpan(begin, 5), new TextSpan(blockEnd, 3));
    }

    [Fact]
    public void Block_NoModel_NoResult()
    {
        var into = new List<TextSpan>();
        new BlockMatchProducer().Collect(new MatchContext(Proc, Proc.IndexOf("begin", StringComparison.Ordinal), null, null), into);
        Assert.Empty(into);
    }

    // ── Selection-word occurrences (text-based; regression pin for the old OccurrenceHighlighter) ──

    private static List<TextSpan> Selection(string text, string selected)
    {
        var into = new List<TextSpan>();
        new SelectionOccurrenceProducer().Collect(new MatchContext(text, 0, selected, null), into);
        return into;
    }

    [Fact]
    public void Selection_BoxesEveryWholeWordOccurrence()
    {
        const string text = "select id from t where id = 1";
        var spans = Selection(text, "id");
        Assert.Equal(2, spans.Count);
        Assert.All(spans, s => Assert.Equal("id", text.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void Selection_RespectsWordBoundaries()
    {
        const string text = "select grid, id from t"; // "id" must not match inside "grid"
        var spans = Selection(text, "id");
        Assert.Single(spans);
        Assert.Equal(text.IndexOf(", id", StringComparison.Ordinal) + 2, spans[0].Start);
    }

    [Fact]
    public void Selection_TooShortOrNonIdentifier_Ignored()
    {
        Assert.Empty(Selection("a a a", "a"));      // < 2 chars
        Assert.Empty(Selection("1 + 1", "1"));      // not an identifier
    }

    // ── Matcher orchestration (de-duplication) ─────────────────────────────────────────────────────

    private sealed class StubProducer : IRelatedElementProducer
    {
        private readonly TextSpan[] _spans;
        public StubProducer(params TextSpan[] spans) => _spans = spans;
        public void Collect(MatchContext ctx, ICollection<TextSpan> into)
        {
            foreach (var s in _spans) into.Add(s);
        }
    }

    [Fact]
    public void Match_DeduplicatesSpansAcrossProducers()
    {
        var shared = new TextSpan(3, 4);
        var matcher = new RelatedElementMatcher(new IRelatedElementProducer[]
        {
            new StubProducer(shared, new TextSpan(10, 2)),
            new StubProducer(shared), // same span from a second producer
        });
        var result = matcher.Match(new MatchContext("whatever", 0, null, null));
        Assert.Equal(2, result.Count);
        Assert.Contains(shared, result);
        Assert.Contains(new TextSpan(10, 2), result);
    }

    [Fact]
    public void Match_EmptyWhenNoProducerContributes()
    {
        var matcher = RelatedElementMatcher.CreateDefault();
        // Plain text, caret mid-word, no selection, no model → nothing to highlight.
        Assert.Empty(matcher.Match(new MatchContext("select a from t", 2, null, null)));
    }
}
