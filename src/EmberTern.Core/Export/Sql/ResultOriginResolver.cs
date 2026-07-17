using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// <b>The verdict.</b> Takes the facts a grid supplied (signals A + B) plus the catalog (signal C) and
/// decides whether EmberTern can prove which table's rows a result holds — and, if so, whether the
/// primary key can identify one of them.
/// <para>
/// <b>Why three signals.</b> No single source is sufficient, and each is blind to something the others
/// see:
/// <list type="bullet">
/// <item><b>A</b> (the server's provenance) is blind to a UNION's later legs, to self-joins, and to
/// whether the named object is a table, a view, or a procedure.</item>
/// <item><b>B</b> (the AST) is the only signal that sees the UNION — but only exists where there is a
/// statement.</item>
/// <item><b>C</b> (the catalog) is the only signal that knows the <em>complete</em> primary key, which is
/// the one fact that makes an UPDATE safe.</item>
/// </list>
/// They must agree unanimously; any one of them may veto.
/// </para>
/// <para>
/// <b>The rule this class exists to enforce:</b> the driver's per-column <c>IsKey</c>/<c>IsUnique</c> are
/// <em>participation</em> flags, not row-identity guarantees. For <c>select ORDER_ID, QTY from
/// ORDER_ITEMS</c> whose PK is <c>(ORDER_ID, LINE_NO)</c>, the driver reports <c>ORDER_ID IsKey=True</c>
/// — and a WHERE built from it updates <b>every line of the order</b>. Key completeness is therefore
/// verified against the catalog's full column list, and a partial key is a refusal, never a fallback.
/// </para>
/// <para>Pure — a function of its inputs — so every measured trap is a unit test.</para>
/// </summary>
public static class ResultOriginResolver
{
    /// <summary>Resolves what <paramref name="origin"/>'s rows are, against <paramref name="catalog"/>.
    /// Never throws; an obstacle is a <see cref="TargetResolution.Unavailable"/> carrying its reason.</summary>
    public static TargetResolution Resolve(ResultOrigin origin, ISqlMetadataProvider catalog)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(catalog);

        // Distinct base tables come first: several vetoes want to NAME the tables involved, and a
        // message that names the obstacle is the whole point of refusing rather than generating.
        var tables = origin.Columns
            .Where(c => !c.IsDerivedExpression)
            .Select(c => c.BaseTable!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (VetoByShape(origin.Shape, tables) is { } shapeVeto)
            return new TargetResolution.Unavailable(shapeVeto);

        // ── Signal A ─────────────────────────────────────────────────────────
        if (tables.Length == 0)
            return Unavailable(ExportUnavailableCode.NoSourceTable);
        if (tables.Length > 1)
            return Unavailable(ExportUnavailableCode.MultipleSourceTables, tables);

        var table = origin.Shape is OriginShape.DirectTable direct ? direct.TableName : tables[0];

        // `select ID, ID as AGAIN from T` — two result columns, one base column. Emitting both would be
        // invalid SQL, and silently dropping one would be a guess about which the user meant.
        var duplicate = origin.Columns
            .Where(c => !c.IsDerivedExpression && !string.IsNullOrEmpty(c.BaseColumn))
            .GroupBy(c => c.BaseColumn!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Unavailable(ExportUnavailableCode.DuplicateSourceColumn, duplicate.Key);

        // ── Signal C ─────────────────────────────────────────────────────────
        var obj = catalog.FindObject(table);
        if (obj is null)
            return Unavailable(ExportUnavailableCode.UnknownObject, table);
        if (obj.Kind != SymbolKind.Table)
            return new TargetResolution.Unavailable(
                new ExportUnavailableReason(ExportUnavailableCode.NotATable)
                {
                    Names = new[] { table },
                    ObjectKind = obj.Kind,
                });

        var catalogColumns = catalog.GetColumns(table);
        if (catalogColumns.Count == 0)
        {
            // A real table always has at least one column, so "no columns" cannot mean "no columns" —
            // it means the metadata is not warmed yet. Reporting this as "CUSTOMERS has no primary key"
            // would be a confident lie about the user's schema; the caller warms and asks again.
            return Unavailable(ExportUnavailableCode.CatalogNotLoaded, table);
        }

        var byName = catalogColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<ResolvedColumn>(origin.Columns.Count);
        for (int i = 0; i < origin.Columns.Count; i++)
        {
            var c = origin.Columns[i];
            if (c.IsDerivedExpression) continue; // an expression is not a table column — nothing to write
            if (string.IsNullOrEmpty(c.BaseColumn)) continue;

            if (!byName.TryGetValue(c.BaseColumn!, out var meta))
            {
                // Provenance and catalog disagree — the cached metadata is stale (an uncommitted DDL is
                // invisible to the metadata attachment). Refuse rather than silently omit the column.
                return Unavailable(ExportUnavailableCode.UnknownSourceColumn, c.BaseColumn!);
            }

            resolved.Add(new ResolvedColumn(i, meta.Name, c.ValueKind)
            {
                // The catalog is the authority on what the COLUMN is; the driver's flag is its view of
                // this result. Either marking it computed is enough to keep it out of generated DML.
                IsComputed = c.IsComputed || meta.IsComputed,
                IsPrimaryKey = meta.IsPrimaryKey,
                Identity = meta.Identity,
            });
        }

        return new TargetResolution.Resolved(table, resolved, ResolvePrimaryKey(catalogColumns, resolved));
    }

    // ── Signal B ─────────────────────────────────────────────────────────────
    private static ExportUnavailableReason? VetoByShape(OriginShape shape, string[] tables) => shape switch
    {
        // Table Data needs no inference at all: the grid IS the table. Strictly safer than a statement.
        OriginShape.DirectTable => null,
        OriginShape.NotATable n => n.Reason,
        OriginShape.Statement s => VetoByStatementShape(s.Shape, tables),
        _ => ExportUnavailableReason.Of(ExportUnavailableCode.StatementNotUnderstood),
    };

    private static ExportUnavailableReason? VetoByStatementShape(StatementShape s, string[] tables)
    {
        if (!s.IsUnderstood) return ExportUnavailableReason.Of(ExportUnavailableCode.StatementNotUnderstood);
        if (s.IsSetOperation) return ExportUnavailableReason.Of(ExportUnavailableCode.SetOperation);
        if (s.IsWithQuery) return ExportUnavailableReason.Of(ExportUnavailableCode.CommonTableExpression);
        if (s.HasGroupBy) return ExportUnavailableReason.Of(ExportUnavailableCode.Aggregate);

        // A join is refused whatever A says: one base table name can be two row instances (a self-join),
        // which is why this is a shape question and not a "how many distinct names?" question.
        if (s.HasJoin) return ExportUnavailableReason.Of(ExportUnavailableCode.Join, tables);

        if (s.FromItemCount == 0) return ExportUnavailableReason.Of(ExportUnavailableCode.NoSourceTable);
        if (s.FromItemCount > 1) return ExportUnavailableReason.Of(ExportUnavailableCode.MultipleSourceTables, tables);
        return null;
    }

    // ── §6 key verification ──────────────────────────────────────────────────
    private static KeyResolution ResolvePrimaryKey(
        IReadOnlyList<ColumnMetadata> catalogColumns,
        IReadOnlyList<ResolvedColumn> resolved)
    {
        // The catalog's FULL column list is what makes completeness checkable — the exact check the
        // driver's per-column IsKey cannot make, because it never sees the columns you did not select.
        var keyColumns = catalogColumns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToArray();
        if (keyColumns.Length == 0)
            return new KeyResolution.Unavailable(ExportUnavailableReason.Of(ExportUnavailableCode.NoPrimaryKey));

        var projected = resolved.ToDictionary(c => c.BaseColumn, StringComparer.OrdinalIgnoreCase);

        var missing = keyColumns.Where(k => !projected.ContainsKey(k)).ToArray();
        if (missing.Length > 0)
        {
            // NOT a fallback to a partial key. This single rule is the whole point: `WHERE ORDER_ID = 1`
            // on a PK of (ORDER_ID, LINE_NO) hits every line of the order, and succeeds while doing it.
            return new KeyResolution.Unavailable(
                ExportUnavailableReason.Of(ExportUnavailableCode.IncompletePrimaryKey, missing));
        }

        return new KeyResolution.Verified(keyColumns.Select(k => projected[k]).ToArray());
    }

    private static TargetResolution Unavailable(ExportUnavailableCode code, params string[] names)
        => new TargetResolution.Unavailable(ExportUnavailableReason.Of(code, names));
}
