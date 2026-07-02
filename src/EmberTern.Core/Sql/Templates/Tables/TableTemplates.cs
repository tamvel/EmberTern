using System.Linq;
using EmberTern.Core.Metadata;
using static EmberTern.Core.Sql.Templates.TemplateHelpers;

namespace EmberTern.Core.Sql.Templates.Tables;

/// <summary><c>SELECT * FROM t</c> — applies to tables and views.</summary>
public sealed class TableSelectAllTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.select-all", "SELECT *", SqlTemplateGroup.Dml, 10,
            new[] { MetadataObjectKind.Table, MetadataObjectKind.View }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => SqlSnippet.Plain($"SELECT * FROM {Q(ctx.Object.Name)}");
}

/// <summary><c>SELECT col1, col2 FROM t</c> — one column per line. Tables and views.</summary>
public sealed class TableSelectColumnsTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.select-columns", "SELECT columns", SqlTemplateGroup.Dml, 20,
            new[] { MetadataObjectKind.Table, MetadataObjectKind.View }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var b = new SqlSnippetBuilder().Add("SELECT");
        for (var i = 0; i < ctx.Columns.Count; i++)
        {
            b.Add(nl).Add(ctx.Options.Indent).Add(Q(ctx.Columns[i].Name));
            if (i < ctx.Columns.Count - 1) b.Add(",");
        }
        b.Add(nl).Add("FROM ").Add(Q(ctx.Object.Name));
        return b.Build();
    }
}

/// <summary>Comma-separated field list — a fragment to drop into an existing statement.</summary>
public sealed class TableFieldListTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.field-list", "Field list", SqlTemplateGroup.Fragment, 30,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
        => SqlSnippet.Plain(string.Join(", ", ctx.Columns.Select(c => Q(c.Name))));
}

/// <summary>Comma-separated named-parameter list (<c>:c1, :c2</c>) — a fragment with tab-stops.</summary>
public sealed class TableParameterListTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.parameter-list", "Parameter list", SqlTemplateGroup.Fragment, 40,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var b = new SqlSnippetBuilder();
        for (var i = 0; i < ctx.Columns.Count; i++)
        {
            if (i > 0) b.Add(", ");
            b.Param(ctx, ctx.Columns[i].Name);
        }
        return b.Build();
    }
}

/// <summary><c>INSERT INTO t (cols) VALUES (:params)</c> — excludes computed + identity columns.</summary>
public sealed class TableInsertTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.insert", "INSERT", SqlTemplateGroup.Dml, 50,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var cols = Insertable(ctx);
        var b = new SqlSnippetBuilder().Add("INSERT INTO ").Add(Q(ctx.Object.Name)).Add(" (");
        for (var i = 0; i < cols.Count; i++)
        {
            if (i > 0) b.Add(", ");
            b.Add(Q(cols[i].Name));
        }
        b.Add(")").Add(ctx.Options.NewLine).Add("VALUES (");
        for (var i = 0; i < cols.Count; i++)
        {
            if (i > 0) b.Add(", ");
            b.Param(ctx, cols[i].Name);
        }
        b.Add(")");
        return b.Build();
    }
}

/// <summary><c>UPDATE t SET col = :col … WHERE pk = :pk</c> — SET excludes PK; WHERE is PK-aware.</summary>
public sealed class TableUpdateTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.update", "UPDATE", SqlTemplateGroup.Dml, 60,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var set = Settable(ctx);
        var b = new SqlSnippetBuilder().Add("UPDATE ").Add(Q(ctx.Object.Name)).Add(" SET");
        for (var i = 0; i < set.Count; i++)
        {
            b.Add(nl).Add(ctx.Options.Indent).Add(Q(set[i].Name)).Add(" = ").Param(ctx, set[i].Name);
            if (i < set.Count - 1) b.Add(",");
        }
        b.Add(nl);
        AppendPrimaryKeyWhere(b, ctx);
        return b.Build();
    }
}

/// <summary><c>DELETE FROM t WHERE pk = :pk</c> — PK-aware WHERE (placeholder condition when no PK).</summary>
public sealed class TableDeleteTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.delete", "DELETE", SqlTemplateGroup.Dml, 70,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var b = new SqlSnippetBuilder()
            .Add("DELETE FROM ").Add(Q(ctx.Object.Name)).Add(ctx.Options.NewLine);
        AppendPrimaryKeyWhere(b, ctx);
        return b.Build();
    }
}

/// <summary><c>UPDATE OR INSERT INTO t (cols) VALUES (:params) MATCHING (pk)</c>.</summary>
public sealed class TableUpsertTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.upsert", "UPDATE OR INSERT", SqlTemplateGroup.Dml, 80,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var cols = NonComputed(ctx);
        var b = new SqlSnippetBuilder().Add("UPDATE OR INSERT INTO ").Add(Q(ctx.Object.Name)).Add(" (");
        for (var i = 0; i < cols.Count; i++)
        {
            if (i > 0) b.Add(", ");
            b.Add(Q(cols[i].Name));
        }
        b.Add(")").Add(nl).Add("VALUES (");
        for (var i = 0; i < cols.Count; i++)
        {
            if (i > 0) b.Add(", ");
            b.Param(ctx, cols[i].Name);
        }
        b.Add(")").Add(nl).Add("MATCHING (");
        if (ctx.PrimaryKey.Count == 0)
        {
            b.Placeholder("key", "/* primary key */");
        }
        else
        {
            for (var i = 0; i < ctx.PrimaryKey.Count; i++)
            {
                if (i > 0) b.Add(", ");
                b.Add(Q(ctx.PrimaryKey[i]));
            }
        }
        b.Add(")");
        return b.Build();
    }
}
