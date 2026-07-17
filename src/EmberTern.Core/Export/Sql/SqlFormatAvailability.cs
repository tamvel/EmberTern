using System;
using System.Linq;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// Answers "may this result be copied as INSERT / as UPDATE, and if not, why?" — the one place the menu's
/// enabled state and its explanation come from, so a greyed item and its tooltip cannot disagree.
/// <para>
/// The asymmetry is deliberate and is the milestone in miniature: <b>INSERT needs a proven table;
/// UPDATE needs that plus a key proven to identify exactly one row.</b> So UPDATE is unavailable
/// strictly more often, and each refusal names its own obstacle rather than collapsing into "not
/// available".
/// </para>
/// </summary>
public static class SqlFormatAvailability
{
    /// <summary>Whether <see cref="ExportFormat.InsertScript"/> can run on <paramref name="resolution"/>.</summary>
    public static FormatAvailability ForInsert(TargetResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (resolution is not TargetResolution.Resolved resolved)
            return FormatAvailability.Unavailable(((TargetResolution.Unavailable)resolution).Reason);

        // Every column computed / derived ⇒ nothing to write. Rare, but a real result shape
        // (`select AREA from RECT`), and "INSERT INTO RECT () VALUES ()" is not a statement.
        return resolved.Columns.Any(c => !c.IsComputed)
            ? FormatAvailability.Available
            : FormatAvailability.Unavailable(
                ExportUnavailableReason.Of(ExportUnavailableCode.NoWritableColumns, resolved.Table));
    }

    /// <summary>Whether <see cref="ExportFormat.UpdateScript"/> can run on <paramref name="resolution"/>.</summary>
    public static FormatAvailability ForUpdate(TargetResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (resolution is not TargetResolution.Resolved resolved)
            return FormatAvailability.Unavailable(((TargetResolution.Unavailable)resolution).Reason);

        // The key comes first: without one there is no safe UPDATE at all, and its reason is the most
        // useful thing we can tell the user ("LINE_NO is not in the result" points at a fix).
        if (resolved.PrimaryKey is KeyResolution.Unavailable noKey)
            return FormatAvailability.Unavailable(noKey.Reason);

        // A key is necessary but not sufficient — there must also be something to SET. A result of key
        // columns alone yields `UPDATE T SET  WHERE ID = 1`, which is not a statement either.
        return resolved.Columns.Any(c => !c.IsComputed && !c.IsPrimaryKey)
            ? FormatAvailability.Available
            : FormatAvailability.Unavailable(
                ExportUnavailableReason.Of(ExportUnavailableCode.NoWritableColumns, resolved.Table));
    }

    /// <summary>Resolves <paramref name="origin"/> against <paramref name="catalog"/> and answers for
    /// <paramref name="format"/> in one step — the shape a UI wants, since it asks both questions about
    /// the same result at the same moment.</summary>
    public static FormatAvailability For(
        ExportFormat format,
        ResultOrigin origin,
        ISqlMetadataProvider catalog)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(catalog);

        var resolution = ResultOriginResolver.Resolve(origin, catalog);
        return format switch
        {
            ExportFormat.InsertScript => ForInsert(resolution),
            ExportFormat.UpdateScript => ForUpdate(resolution),
            // Every other format ignores provenance entirely — that they need less is the sign the seam
            // is in the right place.
            _ => FormatAvailability.Available,
        };
    }
}
