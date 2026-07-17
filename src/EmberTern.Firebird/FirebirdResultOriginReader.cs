using System;
using System.Collections.Generic;
using System.Data;
using EmberTern.Core.Export.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Captures <b>signal A</b> — the server's own per-column provenance — from a prepared command's
/// <c>GetSchemaTable()</c>.
/// <para>
/// <b>Never call this on an execution path.</b> <c>GetSchemaTable()</c> costs ~7 ms, about 5.6× a small
/// query (1.55 ms to execute and read every row; 8.66 ms with the schema table). Capturing provenance on
/// every F5 to serve an occasional menu action would be a silent, across-the-board regression of the SQL
/// Editor and its execution timer. It is captured <b>lazily</b>, on the first Copy-as-INSERT/UPDATE, via
/// a <see cref="CommandBehavior.SchemaOnly"/> prepare (~6.6 ms, no rows) — the grid already holds the
/// rows; only the shape is re-derived.
/// </para>
/// <para>
/// <b>Lane + locking</b> (existing rules, not new ones): prepare on the <b>Data</b> lane — the
/// attachment that ran the query — under its command lock, because one <c>FbConnection</c> allows one
/// transaction at a time and concurrent commands must be serialized. <b>Not</b> the Metadata lane: a
/// statement may reference an object created but not committed in the Data lane's transaction, which is
/// invisible to another attachment, so a Metadata-lane prepare would fail exactly when the user is
/// iterating on new DDL.
/// </para>
/// </summary>
public static class FirebirdResultOriginReader
{
    // Verified against a live engine, because the design never established it and guessing here is a
    // silent-corruption risk (read a TIMESTAMP as a DATE and the time is gone): FbDataReader's schema
    // table carries "ProviderType" as an Int32 that IS the FbDbType — 10=Integer, 11=Numeric, 16=VarChar,
    // 5=Date, 14=Time, 15=TimeStamp, 3=Boolean, 2=Binary, 13=Text all confirmed. GetDataTypeName() is the
    // alternative, but it is a display string ("BLOB SUB_TYPE 1") and parsing it back would be a second,
    // lossier representation of what ProviderType already states exactly.
    private const string ProviderTypeColumn = "ProviderType";
    private const string BaseTableColumn = "BaseTableName";
    private const string BaseColumnColumn = "BaseColumnName";
    private const string IsExpressionColumn = "IsExpression";

    /// <summary>Reads one <see cref="ColumnOrigin"/> per output column of <paramref name="schemaTable"/>
    /// (a <see cref="FbDataReader.GetSchemaTable"/> result), in result-column order.</summary>
    /// <remarks>Takes the <see cref="DataTable"/> rather than a reader so the caller owns the lane, the
    /// lock, and the <c>SchemaOnly</c> prepare — this is a pure translation step.</remarks>
    public static IReadOnlyList<ColumnOrigin> ReadColumnOrigins(DataTable schemaTable)
    {
        ArgumentNullException.ThrowIfNull(schemaTable);

        var origins = new List<ColumnOrigin>(schemaTable.Rows.Count);
        foreach (DataRow row in schemaTable.Rows)
        {
            var baseTable = AsString(row, BaseTableColumn);
            var baseColumn = AsString(row, BaseColumnColumn);

            // An EMPTY BaseTableName is the reliable "derived expression" signal — not IsExpression,
            // which the driver reports false for `CUSTOMER_ID * 2`. For such a column BaseColumnName is
            // an operator name (MULTIPLY / COUNT / CONSTANT), which the resolver must never emit; it is
            // carried through verbatim so the fact stays inspectable rather than being nulled out here.
            var kind = ReadValueKind(row);

            origins.Add(new ColumnOrigin(
                BaseTable: string.IsNullOrEmpty(baseTable) ? null : baseTable,
                BaseColumn: baseColumn,
                IsComputed: AsBool(row, IsExpressionColumn),
                ValueKind: kind));
        }

        return origins;
    }

    // IsKey and IsUnique are DELIBERATELY not read. They are per-column PARTICIPATION flags — both report
    // true for the projected half of a composite key — so a WHERE built from them silently updates every
    // row that shares it. Key completeness is verified against the catalog instead (ResultOriginResolver).
    // This omission is the point; do not "helpfully" add them.

    private static SqlValueKind ReadValueKind(DataRow row)
    {
        if (!row.Table.Columns.Contains(ProviderTypeColumn)) return SqlValueKind.Unknown;

        var raw = row[ProviderTypeColumn];
        if (raw is null or DBNull) return SqlValueKind.Unknown;

        try
        {
            var fbType = (FbDbType)Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
            // An out-of-range int would produce an undefined enum value; the map's default answers
            // Unknown for it, which refuses — the safe direction.
            return Enum.IsDefined(fbType) ? FirebirdValueKindMap.ToValueKind(fbType) : SqlValueKind.Unknown;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            // Unknown ⇒ the literal writer refuses ⇒ no statement is generated. Never guess a type.
            return SqlValueKind.Unknown;
        }
    }

    private static string? AsString(DataRow row, string column)
        => row.Table.Columns.Contains(column) && row[column] is string s ? s : null;

    private static bool AsBool(DataRow row, string column)
        => row.Table.Columns.Contains(column) && row[column] is bool b && b;
}
