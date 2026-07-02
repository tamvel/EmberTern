using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// Shared column/identifier helpers for the built-in templates. Identifier quoting reuses
/// <see cref="DdlGenerator.QuoteLight"/> (same-assembly <c>internal</c>) so generated SQL
/// matches the rest of the app: SHOUTY_CASE names stay bare, lowercase/special get quoted.
/// </summary>
internal static class TemplateHelpers
{
    public static string Q(string name) => DdlGenerator.QuoteLight((name ?? string.Empty).Trim());

    /// <summary>
    /// The metadata-free half of applicability: the template's descriptor lists this object
    /// kind AND this insertion context. Every template's <c>AppliesTo</c> starts here, then
    /// adds any data gate (columns loaded, selectable proc).
    /// </summary>
    public static bool KindAndContext(SqlTemplateDescriptor descriptor, SnippetContext ctx)
        => descriptor.Kinds.Contains(ctx.Object.Kind) && descriptor.Contexts.Contains(ctx.Insertion);

    /// <summary>Columns that are not COMPUTED BY (when the option excludes them).</summary>
    public static IReadOnlyList<FieldInfo> NonComputed(SnippetContext ctx)
        => ctx.Options.ExcludeComputed
            ? ctx.Columns.Where(c => string.IsNullOrWhiteSpace(c.ComputedSource)).ToArray()
            : ctx.Columns.ToArray();

    /// <summary>Columns eligible for an INSERT column list — non-computed, minus identity.</summary>
    public static IReadOnlyList<FieldInfo> Insertable(SnippetContext ctx)
        => NonComputed(ctx)
            .Where(c => !(ctx.Options.ExcludeIdentityOnInsert && c.IsAutoIncrement))
            .ToArray();

    /// <summary>Non-computed columns that are not part of the primary key (UPDATE SET list).</summary>
    public static IReadOnlyList<FieldInfo> Settable(SnippetContext ctx)
        => NonComputed(ctx).Where(c => !IsPrimaryKey(ctx, c.Name)).ToArray();

    public static bool IsPrimaryKey(SnippetContext ctx, string column)
        => ctx.PrimaryKey.Any(p => string.Equals(p, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Emit <c>WHERE pk = :pk [AND pk2 = :pk2]</c>, or a placeholder condition when the
    /// table has no primary key (so a DELETE/UPDATE is never silently unqualified).
    /// </summary>
    public static void AppendPrimaryKeyWhere(SqlSnippetBuilder b, SnippetContext ctx)
    {
        if (ctx.PrimaryKey.Count == 0)
        {
            b.Add("WHERE ").Placeholder("condition", "/* no primary key — specify condition */");
            return;
        }

        b.Add("WHERE ");
        for (var i = 0; i < ctx.PrimaryKey.Count; i++)
        {
            if (i > 0) b.Add(ctx.Options.NewLine).Add("  AND ");
            var col = ctx.PrimaryKey[i];
            b.Add(Q(col)).Add(" = ").Param(ctx, col);
        }
    }
}
