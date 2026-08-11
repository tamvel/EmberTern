using System;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Export;

/// <summary>
/// Renders a Core <see cref="ExportUnavailableReason"/> as the sentence the user reads. The App/Core
/// boundary in one class: Core decides <em>why</em> and carries the data, App decides <em>how it
/// reads</em> (rule #1 — Core has no UI strings; rule #6 — no <c>.resx</c>).
/// <para>
/// <b>The wording rule, which is not decoration:</b> these reasons are three different kinds of claim,
/// and each implies a different next action, so the sentences must keep them apart.
/// <list type="bullet">
/// <item><b>Inherent to the query</b> — "the result is a UNION…". The user rewrites the query.</item>
/// <item><b>A current EmberTern limitation</b> — "EmberTern cannot yet trace which table a CTE reads".
/// Never "CTEs are not supported": the query is fine, our analysis is not deep enough. Saying otherwise
/// tells the user something false about SQL.</item>
/// <item><b>Transient</b> — "…metadata is still loading". Nothing is wrong; wait.</item>
/// </list>
/// </para>
/// </summary>
public static class SqlCopyReasonText
{
    /// <summary>The reason as a sentence fragment, for a disabled menu item's tooltip.</summary>
    public static string Describe(ExportUnavailableReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var first = reason.Names.Count > 0 ? reason.Names[0] : "?";
        var all = string.Join(", ", reason.Names);

        return reason.Code switch
        {
            ExportUnavailableCode.SetOperation => UiStrings.SqlCopyReasonSetOperation,

            ExportUnavailableCode.MultipleSourceTables => Fmt(
                UiStrings.SqlCopyReasonMultipleTablesFormat, reason.Names.Count, all),

            ExportUnavailableCode.Join => Fmt(UiStrings.SqlCopyReasonJoinFormat, all),

            ExportUnavailableCode.Aggregate => UiStrings.SqlCopyReasonAggregate,
            ExportUnavailableCode.NoSourceTable => UiStrings.SqlCopyReasonNoSourceTable,

            ExportUnavailableCode.DuplicateSourceColumn => Fmt(
                UiStrings.SqlCopyReasonDuplicateColumnFormat, first),

            ExportUnavailableCode.UnknownObject => Fmt(UiStrings.SqlCopyReasonUnknownObjectFormat, first),

            // A view gets its own sentence rather than the generic "is a View, not a table", because the
            // honest claim is about EmberTern (updatable-view analysis is not done), not about the view.
            ExportUnavailableCode.NotATable when reason.ObjectKind == SymbolKind.View
                => Fmt(UiStrings.SqlCopyReasonViewFormat, first),
            ExportUnavailableCode.NotATable => Fmt(
                UiStrings.SqlCopyReasonNotATableFormat, first, KindWord(reason.ObjectKind)),

            ExportUnavailableCode.CommonTableExpression => UiStrings.SqlCopyReasonCte,
            ExportUnavailableCode.StatementNotUnderstood => UiStrings.SqlCopyReasonNotUnderstood,

            ExportUnavailableCode.CatalogNotLoaded => Fmt(
                UiStrings.SqlCopyReasonCatalogNotLoadedFormat, first),
            ExportUnavailableCode.UnknownSourceColumn => Fmt(
                UiStrings.SqlCopyReasonUnknownColumnFormat, first),

            ExportUnavailableCode.NoPrimaryKey => Fmt(UiStrings.SqlCopyReasonNoPrimaryKeyFormat, first),
            ExportUnavailableCode.IncompletePrimaryKey => Fmt(UiStrings.SqlCopyReasonIncompletePkFormat, all),
            ExportUnavailableCode.NoWritableColumns => Fmt(UiStrings.SqlCopyReasonNoWritableColumnsFormat, first),
            ExportUnavailableCode.KeyValueIsNull => Fmt(UiStrings.SqlCopyReasonKeyValueIsNullFormat, first),
            ExportUnavailableCode.ValueNotRenderable => Fmt(
                UiStrings.SqlCopyReasonValueNotRenderableFormat, first),
            ExportUnavailableCode.ValueTooLarge => Fmt(UiStrings.SqlCopyReasonValueTooLargeFormat, first),
            ExportUnavailableCode.StatementTooLong => UiStrings.SqlCopyReasonStatementTooLongFormat,

            _ => UiStrings.SqlCopyReasonNotUnderstood,
        };
    }

    /// <summary>The full tooltip for a disabled item: "Copy as UPDATE — unavailable: &lt;reason&gt;".</summary>
    public static string DescribeForMenu(string header, ExportUnavailableReason reason)
        => $"{header} — {UiStrings.SqlCopyUnavailablePrefix}: {Describe(reason)}";

    private static string KindWord(SymbolKind? kind) => kind switch
    {
        SymbolKind.Procedure => UiStrings.SqlCopyKindProcedure,
        SymbolKind.View => UiStrings.SqlCopyKindView,
        SymbolKind.Function => UiStrings.SqlCopyKindFunction,
        SymbolKind.SystemTable => UiStrings.SqlCopyKindSystemTable,
        _ => UiStrings.SqlCopyKindNotATable,
    };

    private static string Fmt(string format, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);
}
