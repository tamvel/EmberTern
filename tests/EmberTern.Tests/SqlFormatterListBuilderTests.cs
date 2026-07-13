using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P8 Krok F — the shared parenthesized-list builder. There is now ONE mechanism for every
/// "( item, item, … )" comma list the formatter lays out; the CREATE VIEW column list — the first
/// consumer — was migrated onto it, deleting its bespoke per-kind emitter. INSERT / VALUES / UPDATE
/// OR INSERT / EXECUTE BLOCK lists ride the same builder in the following steps.
/// <para>
/// The builder's private (it is exercised here through the public <see cref="SqlFormatter.Format"/>
/// via its current consumer). These tests pin the shipped one-per-line CREATE VIEW output plus the
/// correctness properties the token-level rewrite gains over the old character loop — comma-safety
/// inside quoted identifiers, and verbatim (non-lowercased) quoted column names.
/// </para>
/// </summary>
public class SqlFormatterListBuilderTests
{
    [Fact]
    public void ViewColumnList_OnePerLine_ClosingParenGluedToLast()
    {
        Assert.Equal(
            "create view v (\n    a,\n    b,\n    c)\nas\nselect 1\nfrom t",
            SqlFormatter.Format("CREATE VIEW V (A, B, C) AS SELECT 1 FROM T"));
    }

    [Fact]
    public void ViewColumnList_QuotedIdentifier_KeptVerbatim_NotLowercased()
    {
        // Item content is rendered by Emit, so a quoted identifier column passes through untouched
        // (quotes kept, case kept) while an unquoted one lowercases like everywhere else.
        Assert.Equal(
            "create view v (\n    \"MixedCase\",\n    normal)\nas\nselect 1\nfrom t",
            SqlFormatter.Format("CREATE VIEW V (\"MixedCase\", NORMAL) AS SELECT 1 FROM T"));
    }

    [Fact]
    public void ViewColumnList_CommaInsideQuotedIdentifier_DoesNotSplit()
    {
        // The token-level splitter never splits inside a quoted-identifier token — a property the old
        // string character loop had to spend an explicit quote-skip on. "a,b" stays ONE column.
        Assert.Equal(
            "create view v (\n    \"a,b\",\n    c)\nas\nselect 1\nfrom t",
            SqlFormatter.Format("CREATE VIEW V (\"a,b\", C) AS SELECT 1 FROM T"));
    }

    [Fact]
    public void ViewColumnList_SingleColumn()
    {
        Assert.Equal(
            "create view v (\n    only_col)\nas\nselect 1\nfrom t",
            SqlFormatter.Format("CREATE VIEW V (ONLY_COL) AS SELECT 1 FROM T"));
    }

    [Fact]
    public void ViewColumnList_IsIdempotent()
    {
        var once = SqlFormatter.Format("CREATE OR ALTER VIEW V (A, B, C, D) AS SELECT A, B, C, D FROM T");
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    [Fact]
    public void View_NoColumnList_StillFormats()
    {
        Assert.Equal(
            "create view v\nas\nselect 1\nfrom t",
            SqlFormatter.Format("CREATE VIEW V AS SELECT 1 FROM T"));
    }
}
