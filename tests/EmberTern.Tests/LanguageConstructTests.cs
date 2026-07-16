using System.Linq;
using EmberTern.Core.Sql.Language.Constructs;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Language Completion Core foundation (design: docs/design/editor-language-expansion.md) — the curated
/// construct catalog and the pure, synchronous, timing-independent prefix resolver. Natural prefixes,
/// silent-until-unique (measured against the catalog), multi-word aware, no grammar gating here.
/// </summary>
public class LanguageConstructTests
{
    // ── Catalog sanity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_IsWellFormed()
    {
        var all = LanguageConstructCatalog.All;
        Assert.NotEmpty(all);

        // Unique, lowercase spellings; expansions carry no leftover caret marker; caret is in range and
        // the expansion begins with the real spelling (so Tab "finishes what you typed").
        Assert.Equal(all.Count, all.Select(c => c.Spelling).Distinct().Count());
        foreach (var c in all)
        {
            Assert.Equal(c.Spelling.ToLowerInvariant(), c.Spelling);
            Assert.DoesNotContain('￿', c.Expansion);
            Assert.InRange(c.CaretOffset, 0, c.Expansion.Length);
            Assert.StartsWith(c.Spelling, c.Expansion, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Catalog_DoesNotContainBegin_ItIsADelimiterPair()
        => Assert.DoesNotContain(LanguageConstructCatalog.All, c => c.Spelling == "begin");

    [Fact]
    public void Catalog_MaxWords_IsTwo()
        => Assert.Equal(2, LanguageConstructCatalog.MaxWords);

    [Theory]
    [InlineData("if", "if () then", 4)]        // caret inside the parens
    [InlineData("while", "while () do", 7)]
    [InlineData("select", "select ", 7)]       // caret at end
    [InlineData("group by", "group by ", 9)]
    [InlineData("when", "when  do", 5)]         // caret between name-slot spaces
    public void Catalog_Expansion_AndCaret(string spelling, string expansion, int caret)
    {
        var c = LanguageConstructCatalog.All.Single(x => x.Spelling == spelling);
        Assert.Equal(expansion, c.Expansion);
        Assert.Equal(caret, c.CaretOffset);
    }

    // ── Resolver: prefix matching ─────────────────────────────────────────────────────────────

    private static (string spelling, int prefixLen)? Resolve(string text, int? caret = null)
    {
        var m = LanguageConstructResolver.Match(text, caret ?? text.Length);
        return m is null ? null : (m.Construct.Spelling, m.PrefixLength);
    }

    [Fact]
    public void FullKeyword_Arms_AndReplacesWholeWord()
        => Assert.Equal(("select", 6), Resolve("select"));

    [Fact]
    public void NaturalPrefix_Arms()
    {
        Assert.Equal(("select", 3), Resolve("sel"));
        Assert.Equal(("declare variable", 4), Resolve("decl")); // unique within catalog → arms
        Assert.Equal(("where", 4), Resolve("wher"));
        Assert.Equal(("group by", 3), Resolve("gro"));
    }

    [Fact]
    public void CaseInsensitive()
        => Assert.Equal(("while", 3), Resolve("WHI"));

    [Fact]
    public void Ambiguous_StaysSilent_UntilUnique()
    {
        Assert.Null(Resolve("wh"));            // while / where / when
        Assert.Equal(("while", 3), Resolve("whi"));
        Assert.Null(Resolve("whe"));           // where / when
        Assert.Equal(("where", 4), Resolve("wher"));
        Assert.Equal(("when", 4), Resolve("when"));

        Assert.Null(Resolve("e"));             // execute procedure / execute block
        Assert.Null(Resolve("exec"));          // still both
        Assert.Null(Resolve("execute"));       // still both (one word)
    }

    [Fact]
    public void MultiWord_ArmsAcrossWords()
    {
        Assert.Equal(("group by", 7), Resolve("group b"));            // last-2-words window
        Assert.Equal(("group by", 8), Resolve("group by"));          // full spelling → adds trailing space
        Assert.Equal(("execute block", 9), Resolve("execute b"));
        Assert.Equal(("execute procedure", 9), Resolve("execute p"));
        Assert.Equal(("for select", 3), Resolve("for"));             // unique (only for-select in catalog)
    }

    [Fact]
    public void MultiWord_IgnoresUnrelatedLeadingWords()
    {
        // "x group b" → last-2 window "group b" matches; only "group b" is replaced, not "x ".
        Assert.Equal(("group by", 7), Resolve("x group b"));
        // "x group" → last-2 "x group" matches nothing; falls back to last-1 "group".
        Assert.Equal(("group by", 5), Resolve("x group"));
    }

    [Fact]
    public void PrefixInsideLargerStatement()
    {
        const string sql = "select * from customer gro";
        Assert.Equal(("group by", 3), Resolve(sql));
    }

    [Fact]
    public void Identifiers_DoNotArm()
    {
        Assert.Null(Resolve("nr_status"));   // underscore keeps it one non-matching word
        Assert.Null(Resolve("status"));      // no construct starts with it
        Assert.Null(Resolve("customer"));
    }

    [Fact]
    public void NoWordBeforeCaret_DoesNotArm()
    {
        Assert.Null(Resolve("", 0));
        Assert.Null(Resolve("select ", 7));           // caret after the trailing space — past the keyword
        Assert.Null(LanguageConstructResolver.Match("select", 0)); // caret at start
    }

    [Fact]
    public void ZeroMatch_ReturnsNull()
        => Assert.Null(Resolve("xyzzy"));

    [Fact]
    public void PrefixLength_ReplacesExactlyWhatWasTyped()
    {
        // "if" (2 chars) → PrefixLength 2 so the App replaces "if" with "if () then".
        var m = LanguageConstructResolver.Match("if", 2);
        Assert.NotNull(m);
        Assert.Equal(2, m!.PrefixLength);
        Assert.Equal("if () then", m.Construct.Expansion);
        Assert.Equal(4, m.Construct.CaretOffset);
    }
}
