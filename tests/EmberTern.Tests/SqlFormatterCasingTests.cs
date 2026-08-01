using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The casing settings' §0 / architecture-rule-#11 gate (design §6.4, "verification is not optional here").
///
/// <para><b>Three things are proved, and the first is the one that matters most.</b></para>
/// <list type="number">
///   <item><description>
///     ⭐ <b>The default style is byte-identical to the pre-setting formatter.</b> Everything else in this
///     etap rests on that: the ~460 existing formatter assertions are the byte-for-byte record of the shipped
///     output, and they pass unchanged — but they call <c>Format(sql)</c>, so they only pin the implicit
///     default. <see cref="DefaultStyle_IsIdenticalToTheImplicitDefault"/> ties the explicit
///     <see cref="FormatterStyle.Default"/> to it as well, so the two can never diverge.
///   </description></item>
///   <item><description>
///     <b>§0 holds under EVERY style, not just the default.</b> The lexeme-preservation and idempotency
///     invariants are re-run over the whole shared corpus (well-formed AND adversarial) for all four casing
///     combinations. This is the etap's real risk: a re-cased word must never be mistaken for a lost one by
///     the §0 safety net, which would silently revert whole statements to verbatim.
///   </description></item>
///   <item><description>
///     <b>The two settings are genuinely independent, and quoted identifiers are exempt.</b>
///   </description></item>
/// </list>
///
/// <para>⚠ <b>The existing suites were deliberately NOT parameterised.</b>
/// <c>SqlFormatterTests</c> / <c>PsqlFormatterTests</c> / <c>SqlFormatterInvariantsTests</c> stay exactly as
/// they were, because their value is being the unchanged regression record of the shipped output — editing
/// them to take a style would have destroyed the very evidence that the default did not move.</para>
/// </summary>
public class SqlFormatterCasingTests
{
    // All four combinations. Named so a failure says which pair broke.
    public static IEnumerable<object[]> Styles() => new[]
    {
        new object[] { FormatterCase.Lower, FormatterCase.Lower },
        new object[] { FormatterCase.Upper, FormatterCase.Lower },
        new object[] { FormatterCase.Lower, FormatterCase.Upper },
        new object[] { FormatterCase.Upper, FormatterCase.Upper },
    };

    // The well-formed corpus (statement kinds + edge cases + the structural constructs) crossed with the
    // three NON-default styles. The default is covered by the existing suites; re-running it here would
    // triple the corpus for no new information.
    public static IEnumerable<object[]> CorpusAndStyles() =>
        from sql in SqlTestCorpus.All
        from style in NonDefaultStyles()
        select new object[] { sql, style.Keywords, style.Identifiers };

    // The adversarial corpus (malformed / mid-typing) crossed with the same styles. §0 must hold hardest
    // exactly where the parser could not model the input.
    public static IEnumerable<object[]> MalformedAndStyles() =>
        from row in SqlFormatterSafetyTests.MalformedCorpus()
        from style in NonDefaultStyles()
        select new object[] { (string)row[0], style.Keywords, style.Identifiers };

    private static IEnumerable<(FormatterCase Keywords, FormatterCase Identifiers)> NonDefaultStyles()
    {
        yield return (FormatterCase.Upper, FormatterCase.Lower);
        yield return (FormatterCase.Lower, FormatterCase.Upper);
        yield return (FormatterCase.Upper, FormatterCase.Upper);
    }

    private static FormatterStyle Style(FormatterCase keywords, FormatterCase identifiers)
        => new() { KeywordCase = keywords, IdentifierCase = identifiers };

    // ─── 1. THE DEFAULT DID NOT MOVE ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>Format(sql)</c> and <c>Format(sql, FormatterStyle.Default)</c> agree, on every corpus case.
    /// <para>Without this, "the shipped output is unchanged" would rest only on the parameterless overload
    /// that the old tests happen to call, and the explicit default could drift away from it unnoticed —
    /// which is precisely how a user who never opens the settings page would start seeing different SQL.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(SqlTestCorpus.AllData), MemberType = typeof(SqlTestCorpus))]
    public void DefaultStyle_IsIdenticalToTheImplicitDefault(string sql)
    {
        Assert.Equal(SqlFormatter.Format(sql), SqlFormatter.Format(sql, FormatterStyle.Default));
    }

    /// <summary>Both cases default to lower — the property the whole "byte-identical" claim reduces to.</summary>
    [Fact]
    public void DefaultStyle_IsLowerOnBothAxes()
    {
        Assert.Equal(FormatterCase.Lower, FormatterStyle.Default.KeywordCase);
        Assert.Equal(FormatterCase.Lower, FormatterStyle.Default.IdentifierCase);
        Assert.Equal(FormatterStyle.Default, new FormatterStyle());
    }

    /// <summary>A null style means the default, not an exception and not some other style.</summary>
    [Fact]
    public void NullStyle_MeansDefault()
    {
        const string sql = "SELECT A FROM T WHERE B = 1";
        Assert.Equal(SqlFormatter.Format(sql, FormatterStyle.Default), SqlFormatter.Format(sql, style: null));
    }

    // ─── 2. §0 UNDER EVERY STYLE ─────────────────────────────────────────────────────────────

    /// <summary>
    /// §0: no lexeme is lost, added, reordered or mangled — under a NON-default style, over the well-formed
    /// corpus. The comparison is case-insensitive for words by design (re-casing is the setting's whole point)
    /// and exact for everything else.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusAndStyles))]
    public void EveryStyle_NeverLosesSignificantTokens(string sql, FormatterCase keywords, FormatterCase identifiers)
    {
        var formatted = SqlFormatter.Format(sql, Style(keywords, identifiers));
        Assert.Equal(TokenSignature(sql), TokenSignature(formatted));
        Assert.Equal(Comments(sql), Comments(formatted));
    }

    /// <summary>
    /// ⭐ The §0 safety net must not FIRE under a non-default style. This is the subtle failure the corrected
    /// comment in <c>LexemesOf</c> warns about: an exact word comparison would read <c>SELECT</c> against
    /// <c>select</c> as a lost lexeme, the net would keep the statement verbatim, and the setting would appear
    /// to do nothing at all — with every §0 assertion above still passing, because verbatim output preserves
    /// every lexeme perfectly. So this asserts the output actually CHANGED where it should have.
    /// </summary>
    [Fact]
    public void UpperKeywords_ActuallyReCase_AndDoNotTripTheSafetyNet()
    {
        var upper = SqlFormatter.Format(
            "select a, b from t where x = 1 and y = 2 order by a",
            Style(FormatterCase.Upper, FormatterCase.Lower));

        // Re-cased, not reverted to the (already lower-case) input.
        Assert.Equal("SELECT a, b\nFROM t\nWHERE x = 1\n  AND y = 2\nORDER BY a", upper);
    }

    /// <summary>The whole point of a synthesized keyword going through the one decision point: an
    /// emitter-authored keyword must not stay lower while copied keywords go upper.</summary>
    [Fact]
    public void SynthesizedKeywords_FollowTheKeywordSetting()
    {
        var s = Style(FormatterCase.Upper, FormatterCase.Lower);

        // "in" is written by EmitInList, not copied from the token — the case §2.2(a) did not count.
        Assert.Equal("DELETE\nFROM t\nWHERE x IN (1, 2, 3)", SqlFormatter.Format("delete from t where x in (1,2,3)", s));
        // "begin" / "end" are written by the PSQL block structurer.
        Assert.Equal("BEGIN\n  x = 1;\nEND", SqlFormatter.Format("begin x = 1; end", s));
        // "select" / "from" are written by the AST query core; "union"/"all" by the set-operation emitter.
        Assert.Contains("UNION ALL", SqlFormatter.Format(
            "select a from t where exists (select 1 from u) union all select b from v", s));
        // "exists" is written by EmitStructuralChild.
        Assert.Contains("EXISTS", SqlFormatter.Format(
            "select a from t where exists (select 1 from u) union all select b from v", s));
    }

    /// <summary>
    /// §0 under a non-default style over the ADVERSARIAL corpus: either every lexeme survives, or the
    /// fragment is kept unchanged. Malformed input is where the emitters' anti-stall paths run, so it is where
    /// a style-aware emitter is most likely to drop something.
    /// </summary>
    [Theory]
    [MemberData(nameof(MalformedAndStyles))]
    public void EveryStyle_IsLosslessOnMalformedInput(string sql, FormatterCase keywords, FormatterCase identifiers)
    {
        var formatted = SqlFormatter.Format(sql, Style(keywords, identifiers));
        Assert.Equal(TokenSignature(sql), TokenSignature(formatted));
        Assert.Equal(Comments(sql), Comments(formatted));
    }

    /// <summary>Idempotency under every style: <c>Format(Format(x)) == Format(x)</c>. A style that re-cases on
    /// each pass, or one whose second pass re-classifies a word, would show up here.</summary>
    [Theory]
    [MemberData(nameof(CorpusAndStyles))]
    public void EveryStyle_IsIdempotent(string sql, FormatterCase keywords, FormatterCase identifiers)
    {
        var style = Style(keywords, identifiers);
        var once = SqlFormatter.Format(sql, style);
        Assert.Equal(once, SqlFormatter.Format(once, style));
    }

    /// <summary>Formatting never throws, whatever the style.</summary>
    [Theory]
    [MemberData(nameof(MalformedAndStyles))]
    public void EveryStyle_NeverThrows(string sql, FormatterCase keywords, FormatterCase identifiers)
    {
        Assert.Null(Record.Exception(() => SqlFormatter.Format(sql, Style(keywords, identifiers))));
    }

    // ─── 3. THE TWO SETTINGS ARE INDEPENDENT, AND QUOTED NAMES ARE EXEMPT ────────────────────

    /// <summary>
    /// The keyword/identifier split is real: each setting moves its own class of word and leaves the other
    /// alone. Before <c>FWord</c> existed, <c>FKind.Word</c> fused the two and this was inexpressible (§2.2b).
    /// </summary>
    [Fact]
    public void TheTwoSettings_AreIndependent()
    {
        const string sql = "select amount from orders where status = 1";

        Assert.Equal("select amount\nfrom orders\nwhere status = 1",
            SqlFormatter.Format(sql, Style(FormatterCase.Lower, FormatterCase.Lower)));
        Assert.Equal("SELECT amount\nFROM orders\nWHERE status = 1",
            SqlFormatter.Format(sql, Style(FormatterCase.Upper, FormatterCase.Lower)));
        Assert.Equal("select AMOUNT\nfrom ORDERS\nwhere STATUS = 1",
            SqlFormatter.Format(sql, Style(FormatterCase.Lower, FormatterCase.Upper)));
        Assert.Equal("SELECT AMOUNT\nFROM ORDERS\nWHERE STATUS = 1",
            SqlFormatter.Format(sql, Style(FormatterCase.Upper, FormatterCase.Upper)));
    }

    /// <summary>
    /// ⭐ <b>A quoted identifier is NEVER re-cased, under any setting</b> — the §0 / rule-#11 half of §2.2(e).
    /// <para>In Firebird <c>"MixedCase"</c> and <c>MIXEDCASE</c> are different objects, so re-casing a quoted
    /// name changes which object the statement refers to. That is data corruption, not a formatting choice.
    /// The setting is applied strictly inside the formatter's existing quoted-identifier guard.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Styles))]
    public void QuotedIdentifiers_AreNeverReCased(FormatterCase keywords, FormatterCase identifiers)
    {
        var style = Style(keywords, identifiers);

        Assert.Contains("\"MixedCase\"", SqlFormatter.Format("select \"MixedCase\" from t", style));
        Assert.Contains("\"lower name\"", SqlFormatter.Format("select \"lower name\" from t", style));
        // A quoted name that spells a KEYWORD stays exactly as written — it is an identifier, not vocabulary.
        Assert.Contains("\"From\"", SqlFormatter.Format("select \"From\" from t", style));
    }

    /// <summary>
    /// String literals, numbers and comments are never re-cased either — they are the other half of §0's
    /// "passes through untouched".
    /// </summary>
    [Theory]
    [MemberData(nameof(Styles))]
    public void LiteralsAndComments_AreNeverReCased(FormatterCase keywords, FormatterCase identifiers)
    {
        var style = Style(keywords, identifiers);
        var outp = SqlFormatter.Format("select 'It''s Ok', 0x1F, 3.14 from t -- Keep My Case\n", style);

        Assert.Contains("'It''s Ok'", outp);
        Assert.Contains("0x1F", outp);
        Assert.Contains("-- Keep My Case", outp);
    }

    /// <summary>
    /// A named parameter follows the IDENTIFIER setting: <c>:name</c> is a variable name, not vocabulary.
    /// ⚠ The sigil itself is punctuation and is never touched.
    /// </summary>
    [Fact]
    public void NamedParameters_FollowTheIdentifierSetting()
    {
        Assert.Contains(":PARAM", SqlFormatter.Format(
            "select a from t where b = :param", Style(FormatterCase.Lower, FormatterCase.Upper)));
        Assert.Contains(":param", SqlFormatter.Format(
            "select a from t where b = :PARAM", Style(FormatterCase.Upper, FormatterCase.Lower)));
    }

    /// <summary>
    /// Data types and built-in functions are catalog keywords, so they follow the KEYWORD setting — which is
    /// what makes <c>cast(x as integer)</c> read as one language rather than three.
    /// </summary>
    [Fact]
    public void DataTypesAndBuiltInFunctions_FollowTheKeywordSetting()
    {
        var outp = SqlFormatter.Format(
            "select cast(amount as integer), coalesce(note, '') from t",
            Style(FormatterCase.Upper, FormatterCase.Lower));

        Assert.Contains("CAST", outp);
        Assert.Contains("AS", outp);
        Assert.Contains("INTEGER", outp);
        Assert.Contains("COALESCE", outp);
        Assert.Contains("amount", outp);   // an identifier stayed lower
        Assert.Contains("note", outp);
    }

    /// <summary>
    /// ⚠ A CREATE definition's HEADER stays verbatim under every style — it is the user's persistent object
    /// definition and the formatter does not reshape it (§0). So the setting reaches the body, not the header;
    /// recorded here so nobody "fixes" it later as an inconsistency.
    /// </summary>
    [Fact]
    public void CreateDefinitionHeader_StaysVerbatim_UnderEveryStyle()
    {
        const string sql = "CREATE OR ALTER PROCEDURE P RETURNS (R INTEGER) AS BEGIN R = 0; SUSPEND; END";

        foreach (var (k, i) in NonDefaultStyles())
        {
            var outp = SqlFormatter.Format(sql, Style(k, i));
            Assert.StartsWith("CREATE OR ALTER PROCEDURE P RETURNS (R INTEGER) AS", outp, StringComparison.Ordinal);
        }
    }

    // ─── §0 helpers (same normalisation as SqlFormatterInvariantsTests) ──────────────────────

    private static string TokenSignature(string sql)
    {
        var sb = new StringBuilder();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            if (t.Kind == TokenKind.EndOfFile) continue;
            sb.Append(t.Kind switch
            {
                // Words + parameter names are case-insensitive: re-casing them is the setting's job.
                TokenKind.Keyword or TokenKind.Identifier or TokenKind.Parameter => t.Text.ToUpperInvariant(),
                // Everything else must survive exactly — quoted identifiers included.
                _ => t.Text,
            });
            sb.Append('␟');
        }
        return sb.ToString();
    }

    private static List<string> Comments(string sql)
    {
        var result = new List<string>();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            foreach (var tr in t.LeadingTrivia)
            {
                if (tr.Kind == TriviaKind.LineComment) result.Add(tr.Text.TrimEnd());
                else if (tr.Kind == TriviaKind.BlockComment) result.Add(tr.Text);
            }
        }
        return result;
    }
}
