using System;
using System.Linq;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlKeywordsTests
{
    [Fact]
    public void All_IsNonEmpty()
    {
        Assert.NotEmpty(SqlKeywords.All);
    }

    [Fact]
    public void All_ContainsCanonicalSqlKeywords()
    {
        var set = SqlKeywords.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SELECT", set);
        Assert.Contains("FROM", set);
        Assert.Contains("WHERE", set);
        Assert.Contains("JOIN", set);
        Assert.Contains("GROUP", set);
        Assert.Contains("ORDER", set);
        Assert.Contains("INSERT", set);
        Assert.Contains("UPDATE", set);
        Assert.Contains("DELETE", set);
        Assert.Contains("COMMIT", set);
        Assert.Contains("ROLLBACK", set);
    }

    [Fact]
    public void All_HasNoDuplicates_CaseInsensitive()
    {
        var distinct = SqlKeywords.All.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(SqlKeywords.All.Count, distinct);
    }

    [Fact]
    public void All_IsAlphabeticallyOrdered()
    {
        var sorted = SqlKeywords.All
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(sorted, SqlKeywords.All);
    }

    [Fact]
    public void All_EntriesAreSingleToken()
    {
        // The autocomplete inserts one identifier per completion. Multi-word
        // entries ("CHARACTER SET") would corrupt the editor when picked.
        foreach (var kw in SqlKeywords.All)
        {
            Assert.DoesNotContain(' ', kw);
        }
    }
}
