using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Export.Sql;

/// <summary>A generated statement, or the reason there isn't one. Same contract as
/// <see cref="SqlLiteralResult"/> one level up: a caller never gets a half-built statement.</summary>
public readonly record struct SqlStatementResult
{
    private SqlStatementResult(string? sql, ExportUnavailableReason? reason)
    {
        Sql = sql;
        Reason = reason;
    }

    public string? Sql { get; }

    public ExportUnavailableReason? Reason { get; }

    public bool IsBuilt => Reason is null;

    public static SqlStatementResult Built(string sql) => new(sql, null);

    public static SqlStatementResult Unavailable(ExportUnavailableReason reason) => new(null, reason);

    public static SqlStatementResult Unavailable(ExportUnavailableCode code, params string[] names)
        => new(null, ExportUnavailableReason.Of(code, names));
}

/// <summary>
/// Assembles DML from a proven <see cref="TargetResolution.Resolved"/> plus one row's values. Shared by
/// every SQL format — INSERT today, UPDATE next, and the place a future MERGE/DELETE plugs in — so the
/// rules that make generated DML *correct* live in exactly one place.
/// <para>
/// It owns four things, each of which is a measured Firebird rule rather than a style choice:
/// column selection (computed columns are excluded — Firebird rejects writing one), the
/// <c>OVERRIDING SYSTEM VALUE</c> clause (required for a <c>GENERATED ALWAYS</c> identity, refused
/// without it), value rendering (delegated wholly to <see cref="SqlLiteralWriter"/>), and the statement
/// budget below.
/// </para>
/// <para>
/// <b>The statement budget is not paranoia — it is the only sufficient size check.</b> Firebird's DSQL
/// text limit is ~65,535 characters, and it applies to the whole statement: the largest hex literal that
/// fits shrinks by exactly the amount of surrounding text (measured: 32,752 bytes alone, 30,749 with 4 KB
/// of other text). <see cref="SqlLiteralLimits"/> can only refuse a value that could <em>never</em> fit;
/// two 20 KB blobs pass every per-value check and still overflow one statement. Only the assembled
/// length knows, and only this class sees it.
/// </para>
/// <para>Pure: no DB, no Avalonia, no culture state.</para>
/// </summary>
public static class SqlStatementBuilder
{
    /// <summary>Firebird's DSQL statement text limit — measured at ~65,536 characters on FB5. Generated
    /// SQL at or above this cannot execute, so it is refused rather than emitted.</summary>
    public const int MaxStatementLength = 65535;

    /// <summary>Builds <c>INSERT INTO t (cols) VALUES (…)</c> for one row of a resolved result.</summary>
    /// <param name="target">The proven target — never inferred here.</param>
    /// <param name="row">The row's values, indexed by <see cref="ResolvedColumn.ResultIndex"/>.</param>
    /// <param name="limits">Per-value ceilings; the statement budget applies regardless.</param>
    public static SqlStatementResult BuildInsert(
        TargetResolution.Resolved target,
        IReadOnlyList<object?> row,
        SqlLiteralLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(row);

        var writable = SelectWritable(target);
        if (writable.Count == 0)
            return SqlStatementResult.Unavailable(ExportUnavailableCode.NoWritableColumns, target.Table);

        var values = new List<string>(writable.Count);
        foreach (var column in writable)
        {
            var value = ValueAt(row, column.ResultIndex);
            var literal = SqlLiteralWriter.Write(value, column.ValueKind, limits ?? SqlLiteralLimits.Default);
            if (!literal.IsWritten) return Refuse(literal.Refusal, column);
            values.Add(literal.Literal);
        }

        var sb = new StringBuilder(256);
        sb.Append("INSERT INTO ").Append(Identifier(target.Table)).Append(" (");
        sb.Append(string.Join(", ", writable.Select(c => Identifier(c.BaseColumn))));
        sb.Append(')');

        // Required, not optional: Firebird rejects an INSERT naming a GENERATED ALWAYS identity with
        // "OVERRIDING clause should be used when an identity column defined as 'GENERATED ALWAYS' is
        // present in the INSERT's field list". We emit the clause rather than dropping the column,
        // because preserving the actual key is the entire point of copying a row (rule #11). BY DEFAULT
        // needs no clause and must not get one.
        if (writable.Any(c => c.Identity == IdentityKind.Always))
            sb.Append(" OVERRIDING SYSTEM VALUE");

        sb.Append(" VALUES (").Append(string.Join(", ", values)).Append(");");

        return Complete(sb.ToString(), target);
    }

    /// <summary>
    /// Builds <c>UPDATE t SET … WHERE &lt;verified key&gt;</c> for one row.
    /// <para>
    /// <b>The WHERE clause is built from a key verified complete against the catalog, or the statement is
    /// not offered at all.</b> There is no fallback to a partial key, and that is the entire point: the
    /// driver reports <c>IsKey=True</c> for the projected half of a composite key, and
    /// <c>WHERE ORDER_ID = 1</c> on a PK of <c>(ORDER_ID, LINE_NO)</c> updates <em>every line of the
    /// order</em> — measured, 2 rows on the lab's own shape. It does not fail; it succeeds, against the
    /// wrong rows.
    /// </para>
    /// </summary>
    public static SqlStatementResult BuildUpdate(
        TargetResolution.Resolved target,
        IReadOnlyList<object?> row,
        SqlLiteralLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(row);

        // No verified key ⇒ no UPDATE. The reason travels up verbatim so the UI names the real obstacle
        // ("…needs the complete primary key; LINE_NO is not in the result") rather than a generic no.
        if (target.PrimaryKey is not KeyResolution.Verified key)
            return SqlStatementResult.Unavailable(((KeyResolution.Unavailable)target.PrimaryKey).Reason);

        var effectiveLimits = limits ?? SqlLiteralLimits.Default;

        // A key column is not a SET column: assigning the value you are matching on is noise at best,
        // and for an identity column Firebird may reject it outright.
        var setColumns = SelectWritable(target).Where(c => !c.IsPrimaryKey).ToList();
        if (setColumns.Count == 0)
            return SqlStatementResult.Unavailable(ExportUnavailableCode.NoWritableColumns, target.Table);

        var assignments = new List<string>(setColumns.Count);
        foreach (var column in setColumns)
        {
            var literal = SqlLiteralWriter.Write(ValueAt(row, column.ResultIndex), column.ValueKind, effectiveLimits);
            if (!literal.IsWritten) return Refuse(literal.Refusal, column);
            assignments.Add($"{Identifier(column.BaseColumn)} = {literal.Literal}");
        }

        var predicates = new List<string>(key.Columns.Count);
        foreach (var column in key.Columns)
        {
            var value = ValueAt(row, column.ResultIndex);

            // A primary key's columns are NOT NULL by definition, so this cannot fire for a PK — assert
            // it anyway, because the cost of being wrong is a WHERE that matches nothing (`= NULL` is
            // never true) or, worse, the wrong rows. It becomes load-bearing the moment a UNIQUE key is
            // allowed, where NULLs are legal and multiple rows can share them.
            if (value is null or DBNull)
                return SqlStatementResult.Unavailable(ExportUnavailableCode.KeyValueIsNull, column.BaseColumn);

            var literal = SqlLiteralWriter.Write(value, column.ValueKind, effectiveLimits);
            if (!literal.IsWritten) return Refuse(literal.Refusal, column);
            predicates.Add($"{Identifier(column.BaseColumn)} = {literal.Literal}");
        }

        var sql = new StringBuilder(256)
            .Append("UPDATE ").Append(Identifier(target.Table))
            .Append(" SET ").Append(string.Join(", ", assignments))
            .Append(" WHERE ").Append(string.Join(" AND ", predicates))
            .Append(';')
            .ToString();

        return Complete(sql, target);
    }

    // A computed column is readable but never writable — Firebird answers an INSERT or UPDATE naming one
    // with "attempted update of read-only column". Derived expressions are already absent from
    // target.Columns: they are not table columns at all.
    private static List<ResolvedColumn> SelectWritable(TargetResolution.Resolved target)
        => target.Columns.Where(c => !c.IsComputed).ToList();

    private static object? ValueAt(IReadOnlyList<object?> row, int index)
        => index >= 0 && index < row.Count ? row[index] : null;

    private static SqlStatementResult Refuse(SqlLiteralRefusal refusal, ResolvedColumn column) => refusal switch
    {
        SqlLiteralRefusal.TooLarge
            => SqlStatementResult.Unavailable(ExportUnavailableCode.ValueTooLarge, column.BaseColumn),
        _ => SqlStatementResult.Unavailable(ExportUnavailableCode.ValueNotRenderable, column.BaseColumn),
    };

    private static SqlStatementResult Complete(string sql, TargetResolution.Resolved target)
        => sql.Length > MaxStatementLength
            ? SqlStatementResult.Unavailable(ExportUnavailableCode.StatementTooLong, target.Table)
            : SqlStatementResult.Built(sql);

    // Deliberately QuoteLight and NOT PresentIdentifier — the design named the latter, and it is wrong
    // here for a reason worth stating.
    //
    // PresentIdentifier folds a regular identifier to UPPERCASE and emits it bare, and argues that is
    // §0-safe "because Firebird folds an unquoted regular identifier to upper anyway, so this changes
    // only the presentation, never which object is resolved". That argument holds for the input it was
    // built for: a name the USER TYPED, which Firebird would have folded regardless — so MYTABLE and
    // mytable are the same object either way.
    //
    // These names come from the CATALOG and are exact. A table created as "MixedCase" has the catalog
    // name MixedCase, and `INSERT INTO MIXEDCASE` resolves to a DIFFERENT object — usually a loud "table
    // unknown", but a real wrong-target write if a MIXEDCASE also exists. The provenance of the string is
    // what decides: user-typed is fold-safe, catalog-exact must round-trip.
    //
    // QuoteLight is that rule: bare only when the name is exactly what an unquoted parse would produce
    // (all-upper regular), quoted verbatim otherwise. Same shared helper, correct half of it.
    internal static string Identifier(string name) => DdlGenerator.QuoteLight(name);
}
