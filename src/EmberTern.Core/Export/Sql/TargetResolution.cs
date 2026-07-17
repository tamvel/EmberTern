using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Export.Sql;

/// <summary>One result column that maps to a real, writable-or-not column of the target table — the
/// product of signals A (which base column) and C (what the catalog says about it).</summary>
/// <param name="ResultIndex">The column's index in the result / row array. Derived-expression columns
/// have no entry at all, so this is not simply a list position.</param>
/// <param name="BaseColumn">The column's real name on the target table — <b>de-aliased</b>, so
/// <c>select NAME as CUSTOMER_NAME</c> resolves to <c>NAME</c>.</param>
/// <param name="ValueKind">The declared type, for <see cref="SqlLiteralWriter"/>.</param>
public sealed record ResolvedColumn(int ResultIndex, string BaseColumn, SqlValueKind ValueKind)
{
    /// <summary><c>COMPUTED BY</c> — readable, never writable. Firebird rejects an INSERT or UPDATE that
    /// names it, so both formats must exclude it.</summary>
    public bool IsComputed { get; init; }

    /// <summary>Part of the target table's primary key.</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>How the column's identity value is generated. <see cref="IdentityKind.Always"/> is the
    /// one that changes the emitted SQL: Firebird rejects an INSERT naming such a column unless the
    /// statement carries <c>OVERRIDING SYSTEM VALUE</c>.</summary>
    public IdentityKind Identity { get; init; }
}

/// <summary>
/// Whether a key can identify <em>exactly one</em> row of the target table — verified against the
/// catalog, never against the driver's per-column <c>IsKey</c> flag (which reports <c>true</c> for the
/// projected half of a composite key and is therefore a multi-row-update bug waiting to happen).
/// </summary>
public abstract record KeyResolution
{
    private protected KeyResolution() { }

    /// <summary>The key is <b>complete</b> in the projection: every one of its catalog columns is present
    /// and maps to a real base column of the one target table. A WHERE built from these matches one row.</summary>
    public sealed record Verified(IReadOnlyList<ResolvedColumn> Columns) : KeyResolution;

    /// <summary>No usable key — with the specific obstacle. Never degrade to a partial key: that is the
    /// exact failure this type exists to prevent.</summary>
    public sealed record Unavailable(ExportUnavailableReason Reason) : KeyResolution;
}

/// <summary>
/// <b>The verdict</b> — what <see cref="ResultOriginResolver"/> concluded from signals A + B + C. Either
/// EmberTern has proven which table's rows these are, or it has not and says why. There is no third
/// state and no best-effort: generated DML is the one place in EmberTern where being wrong is
/// <em>silent</em> (a malformed statement fails loudly and harmlessly; a statement built from a partial
/// key succeeds, against the wrong rows).
/// </summary>
public abstract record TargetResolution
{
    private protected TargetResolution() { }

    /// <summary>The result's rows are rows of <paramref name="Table"/>.</summary>
    /// <param name="Table">The proven target table.</param>
    /// <param name="Columns">The result columns that map to real columns of it, in result order.
    /// Derived-expression columns are absent — they are not table columns and cannot be written.</param>
    /// <param name="PrimaryKey">Whether the PK can identify one row here. INSERT ignores this; UPDATE
    /// requires <see cref="KeyResolution.Verified"/>. Resolved eagerly because it is a property of the
    /// shape, not of the format asking.</param>
    public sealed record Resolved(
        string Table,
        IReadOnlyList<ResolvedColumn> Columns,
        KeyResolution PrimaryKey) : TargetResolution;

    /// <summary>No statement may be generated for this result, for this reason.</summary>
    public sealed record Unavailable(ExportUnavailableReason Reason) : TargetResolution;
}
