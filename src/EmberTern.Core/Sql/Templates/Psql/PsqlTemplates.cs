using EmberTern.Core.Metadata;
using static EmberTern.Core.Sql.Templates.TemplateHelpers;

namespace EmberTern.Core.Sql.Templates.Psql;

/// <summary>
/// <c>FOR SELECT &lt;cols&gt; FROM t INTO :cols DO BEGIN … END</c> — a cursor loop over a
/// table, generated from its columns. PSQL body only.
/// </summary>
public sealed class TableForSelectTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.for-select", "FOR SELECT", SqlTemplateGroup.PsqlScaffold, 90,
            new[] { MetadataObjectKind.Table }, SnippetContexts.PsqlOnly);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var cols = ctx.Columns;
        var b = new SqlSnippetBuilder().Add("FOR").Add(nl).Add("    SELECT").Add(nl);
        for (var i = 0; i < cols.Count; i++)
        {
            b.Add("        ").Add(Q(cols[i].Name));
            if (i < cols.Count - 1) b.Add(",");
            b.Add(nl);
        }
        b.Add("    FROM ").Add(Q(ctx.Object.Name)).Add(nl).Add("    INTO").Add(nl);
        for (var i = 0; i < cols.Count; i++)
        {
            b.Add("        ").Param(ctx, cols[i].Name);
            if (i < cols.Count - 1) b.Add(",");
            b.Add(nl);
        }
        b.Add("DO").Add(nl).Add("BEGIN").Add(nl).Add(nl).Add("END");
        return b.Build();
    }
}

/// <summary>
/// <c>DECLARE VARIABLE V_col TYPE OF COLUMN t.col;</c> per column — declares locals typed
/// to a table's columns. PSQL body only.
/// </summary>
public sealed class TableDeclareVariablesTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.declare-vars", "DECLARE VARIABLES", SqlTemplateGroup.PsqlScaffold, 100,
            new[] { MetadataObjectKind.Table }, SnippetContexts.PsqlOnly);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var table = Q(ctx.Object.Name);
        var b = new SqlSnippetBuilder();
        for (var i = 0; i < ctx.Columns.Count; i++)
        {
            var col = ctx.Columns[i].Name;
            b.Add("DECLARE VARIABLE ")
                .Placeholder(col, ctx.Options.VarPrefix + col.Trim())
                .Add(" TYPE OF COLUMN ").Add(table).Add(".").Add(Q(col)).Add(";");
            if (i < ctx.Columns.Count - 1) b.Add(nl);
        }
        return b.Build();
    }
}

/// <summary>
/// <c>FOR SELECT &lt;outputs&gt; FROM p(:in) INTO :outputs DO BEGIN … END</c> — a cursor loop
/// over a selectable procedure. PSQL body only; needs output parameters.
/// </summary>
public sealed class ProcedureForSelectFromTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("procedure.for-select-from", "FOR SELECT FROM procedure", SqlTemplateGroup.PsqlScaffold, 130,
            new[] { MetadataObjectKind.Procedure }, SnippetContexts.PsqlOnly);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.ProcedureIsSelectable;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var outs = ctx.Outputs;
        var b = new SqlSnippetBuilder().Add("FOR").Add(nl).Add("    SELECT").Add(nl);
        for (var i = 0; i < outs.Count; i++)
        {
            b.Add("        ").Add(Q(outs[i].Name));
            if (i < outs.Count - 1) b.Add(",");
            b.Add(nl);
        }

        b.Add("    FROM ").Add(Q(ctx.Object.Name));
        if (ctx.Inputs.Count > 0)
        {
            b.Add("(");
            for (var i = 0; i < ctx.Inputs.Count; i++)
            {
                if (i > 0) b.Add(", ");
                b.Param(ctx, ctx.Inputs[i].Name);
            }
            b.Add(")");
        }
        b.Add(nl).Add("    INTO").Add(nl);
        for (var i = 0; i < outs.Count; i++)
        {
            b.Add("        ").Param(ctx, outs[i].Name);
            if (i < outs.Count - 1) b.Add(",");
            b.Add(nl);
        }
        b.Add("DO").Add(nl).Add("BEGIN").Add(nl).Add(nl).Add("END");
        return b.Build();
    }
}

/// <summary><c>EXCEPTION exception_name;</c> — raise an exception. PSQL body only.</summary>
public sealed class ExceptionRaiseTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("exception.raise", "EXCEPTION", SqlTemplateGroup.PsqlScaffold, 400,
            new[] { MetadataObjectKind.Exception }, SnippetContexts.PsqlOnly);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => SqlSnippet.Plain($"EXCEPTION {Q(ctx.Object.Name)};");
}

/// <summary>
/// <c>EXCEPTION exception_name 'message';</c> — raise with a custom message (FB 2.0+).
/// The message literal is a tab-stop. PSQL body only.
/// </summary>
public sealed class ExceptionRaiseMessageTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("exception.raise-message", "EXCEPTION with message", SqlTemplateGroup.PsqlScaffold, 410,
            new[] { MetadataObjectKind.Exception }, SnippetContexts.PsqlOnly);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => new SqlSnippetBuilder()
            .Add("EXCEPTION ").Add(Q(ctx.Object.Name)).Add(" ")
            .Placeholder("message", "'message'").Add(";")
            .Build();
}
