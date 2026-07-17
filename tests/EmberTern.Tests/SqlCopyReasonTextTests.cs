using System;
using System.Linq;
using EmberTern.App;
using EmberTern.App.Export;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

// E5 — the App/Core boundary: a structured reason becomes the sentence the user reads. These tests are
// about MEANING, not phrasing: each asserts the claim the sentence makes, because the whole point of
// refusing-with-a-reason is that the user learns something true.
public class SqlCopyReasonTextTests
{
    private static string Describe(ExportUnavailableCode code, params string[] names)
        => SqlCopyReasonText.Describe(ExportUnavailableReason.Of(code, names));

    // Every code must produce a real sentence. A missing case would silently degrade to the generic
    // fallback, which is exactly the "greyed item that teaches nothing" this milestone exists to avoid.
    [Fact]
    public void Every_Reason_Code_Has_Its_Own_Sentence()
    {
        var generic = Describe(ExportUnavailableCode.StatementNotUnderstood);

        var unmapped = Enum.GetValues<ExportUnavailableCode>()
            .Where(c => c != ExportUnavailableCode.StatementNotUnderstood)
            .Where(c => Describe(c, "X", "Y") == generic)
            .ToArray();

        Assert.Empty(unmapped);
    }

    [Fact]
    public void No_Reason_Renders_Empty_Or_A_Raw_Enum_Name()
    {
        foreach (var code in Enum.GetValues<ExportUnavailableCode>())
        {
            var text = Describe(code, "CUSTOMERS", "NAME");
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain(code.ToString(), text, StringComparison.Ordinal); // never leak the enum
        }
    }

    // ── The wording rule: what EmberTern cannot do vs what the query cannot be ──
    // A CTE query is perfectly ordinary SQL; only our analysis is not deep enough yet. Telling the user
    // "CTEs are not supported" would be false about SQL, and would imply a permanence that is not real.
    [Fact]
    public void The_Cte_Reason_Blames_EmberTern_Not_The_Query()
    {
        var text = Describe(ExportUnavailableCode.CommonTableExpression);

        Assert.Contains("EmberTern", text, StringComparison.Ordinal);
        Assert.Contains("yet", text, StringComparison.OrdinalIgnoreCase); // it is a current limitation
        Assert.DoesNotContain("not supported", text, StringComparison.OrdinalIgnoreCase);
    }

    // A UNION, by contrast, IS a property of the result — no future version makes one row belong to one
    // table. The sentence should describe the result, not apologise for EmberTern.
    [Fact]
    public void The_Union_Reason_Describes_The_Result()
        => Assert.Contains("UNION", Describe(ExportUnavailableCode.SetOperation), StringComparison.OrdinalIgnoreCase);

    // Transient: nothing is wrong and the user's move is to wait — so it must not read like a verdict
    // about their schema. This is the reason E2 keeps CatalogNotLoaded distinct from NoPrimaryKey.
    [Fact]
    public void A_Cold_Catalog_Reads_As_Loading_Not_As_A_Missing_Key()
    {
        var text = Describe(ExportUnavailableCode.CatalogNotLoaded, "CUSTOMERS");

        Assert.Contains("loading", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("primary key", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── The sentences that must name the obstacle ────────────────────────────
    // "…LINE_NO is not in the result" points at a fix. That is the difference between a refusal the user
    // can act on and one they can only be annoyed by.
    [Fact]
    public void An_Incomplete_Key_Names_The_Missing_Columns()
        => Assert.Contains("LINE_NO", Describe(ExportUnavailableCode.IncompletePrimaryKey, "LINE_NO"),
            StringComparison.Ordinal);

    [Fact]
    public void A_Multi_Table_Result_Names_Every_Table_And_Counts_Them()
    {
        var text = Describe(ExportUnavailableCode.MultipleSourceTables, "ORDERS", "CUSTOMERS", "PRODUCTS");

        Assert.Contains("3", text, StringComparison.Ordinal);
        Assert.Contains("ORDERS", text, StringComparison.Ordinal);
        Assert.Contains("PRODUCTS", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Table_With_No_Primary_Key_Names_The_Table()
        => Assert.Contains("LOG_ENTRY", Describe(ExportUnavailableCode.NoPrimaryKey, "LOG_ENTRY"),
            StringComparison.Ordinal);

    // ── NotATable — the kind decides the sentence ────────────────────────────
    [Fact]
    public void A_Procedure_Is_Named_As_A_Procedure()
    {
        var text = SqlCopyReasonText.Describe(
            new ExportUnavailableReason(ExportUnavailableCode.NotATable)
            {
                Names = new[] { "SP_CUSTOMER_ORDERS" },
                ObjectKind = SymbolKind.Procedure,
            });

        Assert.Contains("SP_CUSTOMER_ORDERS", text, StringComparison.Ordinal);
        Assert.Contains("procedure", text, StringComparison.OrdinalIgnoreCase);
    }

    // A view gets its OWN sentence rather than the generic "is a View, not a table": the honest claim is
    // that updatable-view analysis is not done, which is about EmberTern, not about the view.
    [Fact]
    public void A_View_Says_EmberTern_Does_Not_Do_Views_Yet_Rather_Than_Miscalling_It()
    {
        var text = SqlCopyReasonText.Describe(
            new ExportUnavailableReason(ExportUnavailableCode.NotATable)
            {
                Names = new[] { "V_ORDER_DETAILS" },
                ObjectKind = SymbolKind.View,
            });

        Assert.Contains("V_ORDER_DETAILS", text, StringComparison.Ordinal);
        Assert.Contains("yet", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── The menu tooltip ─────────────────────────────────────────────────────
    [Fact]
    public void The_Menu_Tooltip_Leads_With_The_Action_Then_Says_Why()
    {
        var tooltip = SqlCopyReasonText.DescribeForMenu(
            UiStrings.GridCopyAsUpdate,
            ExportUnavailableReason.Of(ExportUnavailableCode.IncompletePrimaryKey, "LINE_NO"));

        Assert.StartsWith(UiStrings.GridCopyAsUpdate, tooltip, StringComparison.Ordinal);
        Assert.Contains(UiStrings.SqlCopyUnavailablePrefix, tooltip, StringComparison.Ordinal);
        Assert.Contains("LINE_NO", tooltip, StringComparison.Ordinal);
    }
}
