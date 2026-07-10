using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The single Firebird keyword catalog (Etap 1) — the source of truth that unifies the
/// completion vocabulary and the two XSHD highlighting keyword blocks. These tests pin the
/// catalog invariants and, crucially, enforce that the XSHD <c>&lt;Keywords&gt;</c> blocks stay
/// derived from <see cref="FirebirdSyntax"/> (they cannot drift).
/// </summary>
public class FirebirdSyntaxTests
{
    // ── Catalog invariants ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsKeyword_RecognisesKeywords_AndRejectsPlainIdentifiers()
    {
        Assert.True(FirebirdSyntax.IsKeyword("SELECT"));
        Assert.True(FirebirdSyntax.IsKeyword("select")); // case-insensitive
        Assert.True(FirebirdSyntax.IsKeyword("INTEGER"));
        Assert.False(FirebirdSyntax.IsKeyword("mycolumn"));
        Assert.False(FirebirdSyntax.IsKeyword(null));
    }

    [Theory]
    [InlineData("SELECT", SqlKeywordCategory.Dml)]
    [InlineData("JOIN", SqlKeywordCategory.Dml)]
    [InlineData("INSERT", SqlKeywordCategory.Statement)]
    [InlineData("CREATE", SqlKeywordCategory.Statement)]
    [InlineData("INTEGER", SqlKeywordCategory.DataType)]
    [InlineData("VARCHAR", SqlKeywordCategory.DataType)]
    [InlineData("COUNT", SqlKeywordCategory.Function)]
    [InlineData("CASE", SqlKeywordCategory.Function)]
    [InlineData("ANY", SqlKeywordCategory.Keyword)]     // completion-only, uncoloured
    [InlineData("ESCAPE", SqlKeywordCategory.Keyword)]
    public void CategoryOf_MatchesExpected(string word, SqlKeywordCategory expected)
        => Assert.Equal(expected, FirebirdSyntax.CategoryOf(word));

    [Fact]
    public void HighlightCategories_ArePairwiseDisjoint()
    {
        var cats = new[]
        {
            SqlKeywordCategory.Dml, SqlKeywordCategory.Statement,
            SqlKeywordCategory.DataType, SqlKeywordCategory.Function,
        };
        var seen = new Dictionary<string, SqlKeywordCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in cats)
        {
            foreach (var w in FirebirdSyntax.KeywordsInCategory(cat))
            {
                Assert.False(seen.ContainsKey(w), $"'{w}' is in both {seen.GetValueOrDefault(w)} and {cat}");
                seen[w] = cat;
            }
        }
    }

    [Fact]
    public void KeywordCategoryWords_AreNotInAnyHighlightCategory()
    {
        var highlighted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in new[]
                 {
                     SqlKeywordCategory.Dml, SqlKeywordCategory.Statement,
                     SqlKeywordCategory.DataType, SqlKeywordCategory.Function,
                 })
        {
            highlighted.UnionWith(FirebirdSyntax.KeywordsInCategory(cat));
        }

        foreach (var w in FirebirdSyntax.KeywordsInCategory(SqlKeywordCategory.Keyword))
        {
            Assert.DoesNotContain(w, highlighted);
        }
    }

    [Fact]
    public void EveryCompletionKeyword_IsRecognisedByTheCatalog()
    {
        foreach (var w in FirebirdSyntax.CompletionKeywords)
        {
            Assert.True(FirebirdSyntax.IsKeyword(w), $"completion word '{w}' is not catalogued");
        }
    }

    [Fact]
    public void SqlKeywordsAll_IsExactlyTheCatalogCompletionSet()
    {
        // SqlKeywords.All is now a thin ADAPTER over FirebirdSyntax.CompletionKeywords.
        Assert.Equal(FirebirdSyntax.CompletionKeywords, SqlKeywords.All);
    }

    // ── XSHD drift guard — the highlight keyword blocks must equal the catalog categories ──

    [Fact]
    public void DarkXshd_KeywordBlocks_MatchCatalogCategories()
        => AssertXshdMatchesCatalog(XshdPath("FirebirdSql.xshd"));

    [Fact]
    public void LightXshd_KeywordBlocks_MatchCatalogCategories()
        => AssertXshdMatchesCatalog(XshdPath("FirebirdSql.Light.xshd"));

    [Fact]
    public void LightAndDarkXshd_ShareIdenticalKeywordBlocks()
    {
        var dark = ReadXshdBlocks(XshdPath("FirebirdSql.xshd"));
        var light = ReadXshdBlocks(XshdPath("FirebirdSql.Light.xshd"));
        Assert.Equal(dark.Keys.OrderBy(k => k), light.Keys.OrderBy(k => k));
        foreach (var color in dark.Keys)
        {
            Assert.True(SetEquals(dark[color], light[color]),
                $"light/dark keyword block '{color}' differs");
        }
    }

    private static void AssertXshdMatchesCatalog(string path)
    {
        var blocks = ReadXshdBlocks(path);
        foreach (var (color, category) in ColorToCategory)
        {
            Assert.True(blocks.ContainsKey(color), $"XSHD is missing the '{color}' keyword block");
            var expected = FirebirdSyntax.KeywordsInCategory(category);
            Assert.True(SetEquals(blocks[color], expected),
                $"'{color}' block ≠ FirebirdSyntax {category}. " +
                $"Only in XSHD: [{string.Join(", ", blocks[color].Except(expected, StringComparer.OrdinalIgnoreCase))}]; " +
                $"only in catalog: [{string.Join(", ", expected.Except(blocks[color], StringComparer.OrdinalIgnoreCase))}]");
        }
    }

    private static readonly (string Color, SqlKeywordCategory Category)[] ColorToCategory =
    {
        ("DmlKeyword", SqlKeywordCategory.Dml),
        ("StatementKeyword", SqlKeywordCategory.Statement),
        ("DataType", SqlKeywordCategory.DataType),
        ("Function", SqlKeywordCategory.Function),
    };

    private static bool SetEquals(IEnumerable<string> a, IEnumerable<string> b)
        => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase)
            .SetEquals(new HashSet<string>(b, StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string, List<string>> ReadXshdBlocks(string path)
    {
        Assert.True(File.Exists(path), $"XSHD not found: {path}");
        var doc = XDocument.Load(path);
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var kw in doc.Descendants().Where(e => e.Name.LocalName == "Keywords"))
        {
            var color = kw.Attribute("color")?.Value;
            if (color is null) continue;
            var words = kw.Elements()
                .Where(e => e.Name.LocalName == "Word")
                .Select(e => e.Value.Trim())
                .ToList();
            result[color] = words;
        }
        return result;
    }

    private static string XshdPath(string fileName)
        => Path.Combine(RepoRoot(), "src", "EmberTern.App", "Assets", fileName);

    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
